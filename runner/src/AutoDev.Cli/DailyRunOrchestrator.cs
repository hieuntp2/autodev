using System.Text;
using System.Text.RegularExpressions;
using AutoDev.Codex;
using AutoDev.Core.Models;
using AutoDev.Core.Services;
using AutoDev.Git;
using AutoDev.OpenAI;
using AutoDev.Verification;

namespace AutoDev.Cli;

public sealed partial class DailyRunOrchestrator(
    string rootPath,
    ProjectConfigLoader configLoader,
    WorkspaceService workspaceService,
    GitService gitService,
    OpenAIPlannerClient plannerClient,
    CodexRunner codexRunner,
    VerificationRunner verificationRunner)
{
    private readonly TemplateRenderer _templateRenderer = new();
    private readonly GuardrailValidator _guardrailValidator = new();
    private readonly TargetDocumentLoader _docLoader = new();
    private readonly RunContextBuilder _contextBuilder = new();
    private readonly BacklogParser _backlogParser = new();
    private readonly StatusReader _statusReader = new();
    private readonly TaskSelector _taskSelector = new();
    private readonly StatusWriter _statusWriter = new();

    public async Task<int> RunAsync(string projectId, CancellationToken cancellationToken = default)
    {
        RunContext? context = null;
        RunMetadata? metadata = null;

        try
        {
            var project = await configLoader.LoadAsync(projectId, cancellationToken);

            // Config validation
            var validation = new ProjectConfigValidator().Validate(project);
            foreach (var warning in validation.Warnings)
            {
                Console.WriteLine($"[WARN] {warning}");
            }

            if (!validation.CanProceed)
            {
                foreach (var error in validation.Errors)
                {
                    Console.Error.WriteLine($"[ERROR] {error}");
                }

                Console.Error.WriteLine("Config validation failed. Cannot proceed.");
                return ExitCodes.InvalidConfig;
            }

            context = await workspaceService.CreateAsync(project, DateOnly.FromDateTime(DateTime.Now), cancellationToken);
            metadata = await UpdateMetadataAsync(context, status: "running", startedAt: DateTimeOffset.Now, cancellationToken: cancellationToken);
            await LogAsync(context, "Created daily workspace.", cancellationToken);

            var commitBefore = await RunGitSetupAndSnapshotsAsync(context, cancellationToken);
            metadata = await UpdateMetadataAsync(context, metadata, "snapshotted-inputs", commitBefore: commitBefore, cancellationToken: cancellationToken);

            // Load target repo documents
            var targetDocs = await _docLoader.LoadAsync(project, cancellationToken);
            if (targetDocs.MissingDocPaths.Count > 0)
            {
                foreach (var missing in targetDocs.MissingDocPaths)
                {
                    await LogAsync(context, $"Missing doc: {missing}", cancellationToken);
                }
            }

            // Task selection
            var backlogItems = _backlogParser.Parse(targetDocs.Backlog);
            var statusSummary = _statusReader.Parse(targetDocs.Status);
            var taskSelection = _taskSelector.Select(backlogItems, statusSummary, project.MaxTasksPerRun);

            if (taskSelection.IsBlocked)
            {
                await LogAsync(context, $"Blocked: {taskSelection.BlockedReason}", cancellationToken);
                Console.Error.WriteLine($"[BLOCKED] {taskSelection.BlockedReason}");

                await WriteRunReportAsync(context, project, new RunReportData
                {
                    RunTimestamp = DateTimeOffset.Now,
                    BlockedReason = taskSelection.BlockedReason,
                    BuildPassed = false
                }, cancellationToken);

                metadata = metadata with { Status = "blocked", FinishedAt = DateTimeOffset.Now };
                await workspaceService.WriteMetadataAsync(context.WorkspacePath, metadata, cancellationToken);
                return ExitCodes.Blocked;
            }

            var selectedTask = taskSelection.SelectedTask!;
            await LogAsync(context, $"Selected task: {selectedTask.Id} — {selectedTask.Title}", cancellationToken);
            metadata = await UpdateMetadataAsync(context, metadata, "task-selected", cancellationToken: cancellationToken);

            // Build context and planner prompt
            var projectContext = _contextBuilder.Build(project, targetDocs);
            await AppendProjectMemoryAsync(projectContext, context, cancellationToken);

            var plannerPrompt = await BuildPlannerPromptAsync(context, projectContext, selectedTask, cancellationToken);
            var plannerOutput = await plannerClient.CreatePlanAsync(new PlannerRequest
            {
                Project = project,
                WorkspacePath = context.WorkspacePath,
                ProjectContext = projectContext,
                Template = plannerPrompt,
                Prompt = plannerPrompt
            }, cancellationToken);
            await WritePlanningOutputsAsync(context, plannerOutput, cancellationToken);
            metadata = await UpdateMetadataAsync(context, metadata, "planned", cancellationToken: cancellationToken);

            // Codex implementation
            var codexTask = await File.ReadAllTextAsync(Path.Combine(context.WorkspacePath, "01-planning", "codex-task.md"), cancellationToken);
            var codexResult = await codexRunner.RunAsync(project, context.WorkspacePath, codexTask, cancellationToken);
            if (!codexResult.Passed)
            {
                await LogAsync(context, $"Codex exited with {codexResult.ExitCode}. Continuing to capture diff and verification.", cancellationToken);
            }

            metadata = await UpdateMetadataAsync(context, metadata, "implemented", cancellationToken: cancellationToken);

            await CaptureImplementationStateAsync(context, cancellationToken);
            var verification = await verificationRunner.RunAsync(project, context.WorkspacePath, cancellationToken);
            metadata = await UpdateMetadataAsync(
                context,
                metadata,
                "verified",
                buildPassed: verification.BuildPassed,
                testPassed: verification.TestPassed,
                cancellationToken: cancellationToken);

            var changedFiles = ReadLines(Path.Combine(context.WorkspacePath, "02-implementation", "changed-files.txt"));
            var diff = await File.ReadAllTextAsync(Path.Combine(context.WorkspacePath, "02-implementation", "git-diff.patch"), cancellationToken);
            var guardrails = _guardrailValidator.Validate(project, changedFiles, diff);
            await WriteGuardrailReportAsync(context, guardrails, cancellationToken);

            await WriteRetrospectiveAsync(context, verification, guardrails, selectedTask, cancellationToken);

            var commitHash = await ApplySafeCommitPolicyAsync(context, verification, guardrails, selectedTask, cancellationToken);

            // Find next task
            var nextTask = backlogItems
                .FirstOrDefault(t => t.Status == AutoDev.Core.Models.TaskStatus.Pending && t.Id != selectedTask.Id);

            // Write status to target repo
            var risks = new List<string>();
            if (guardrails.HasProtectedPathChanges) risks.Add("Protected path changes detected.");
            if (guardrails.IsDiffTooLarge) risks.Add("Diff exceeded maxDiffLines.");
            if (guardrails.DependencyChanges.Count > 0) risks.Add($"Dependency files modified: {guardrails.DependencyChanges.Count}.");

            var runReport = new RunReportData
            {
                RunTimestamp = DateTimeOffset.Now,
                TaskId = selectedTask.Id,
                TaskTitle = selectedTask.Title,
                FilesChanged = changedFiles,
                BuildPassed = verification.BuildPassed,
                TestPassed = verification.TestPassed,
                CommitHash = commitHash,
                NextTaskTitle = nextTask?.Title,
                RemainingRisks = risks
            };

            await _statusWriter.WriteAsync(project, runReport, cancellationToken);
            await WriteRunReportAsync(context, project, runReport, cancellationToken);

            var commitAfter = await TryGetCommitAsync(project, cancellationToken);
            metadata = metadata with
            {
                Status = verification.BuildPassed ? "completed" : "completed-build-failed",
                CommitAfter = commitAfter,
                BuildPassed = verification.BuildPassed,
                TestPassed = verification.TestPassed,
                FilesChangedCount = changedFiles.Count,
                FinishedAt = DateTimeOffset.Now,
                CompletedSteps = [.. metadata.CompletedSteps, "reported"]
            };
            await workspaceService.WriteMetadataAsync(context.WorkspacePath, metadata, cancellationToken);
            await LogAsync(context, "Run completed.", cancellationToken);

            return verification.BuildPassed ? ExitCodes.Success : ExitCodes.FailedBuild;
        }
        catch (Exception ex)
        {
            if (context is not null)
            {
                await LogAsync(context, ex.ToString(), cancellationToken);
                metadata ??= await workspaceService.ReadMetadataAsync(context.WorkspacePath, cancellationToken);
                if (metadata is not null)
                {
                    await workspaceService.WriteMetadataAsync(
                        context.WorkspacePath,
                        metadata with
                        {
                            Status = "failed",
                            FailedStep = ex.Message,
                            FinishedAt = DateTimeOffset.Now
                        },
                        cancellationToken);
                }
            }

            Console.Error.WriteLine(ex.Message);
            return ExitCodes.UnhandledException;
        }
    }

    public async Task<int> StatusAsync(string projectId, CancellationToken cancellationToken = default)
    {
        _ = await configLoader.LoadAsync(projectId, cancellationToken);
        var latest = workspaceService.FindLatestWorkspace(projectId);
        if (latest is null)
        {
            Console.WriteLine($"No workspace exists for project '{projectId}'.");
            return 0;
        }

        var metadata = await workspaceService.ReadMetadataAsync(latest, cancellationToken);
        Console.WriteLine($"Project: {projectId}");
        Console.WriteLine($"Latest Workspace: {latest}");
        Console.WriteLine($"Status: {metadata?.Status ?? "unknown"}");
        Console.WriteLine($"Run Date: {metadata?.RunDate ?? Path.GetFileName(latest)}");
        Console.WriteLine($"Build Passed: {metadata?.BuildPassed.ToString() ?? "unknown"}");
        Console.WriteLine($"Test Passed: {metadata?.TestPassed.ToString() ?? "unknown"}");
        Console.WriteLine($"Files Changed: {metadata?.FilesChangedCount.ToString() ?? "unknown"}");
        return 0;
    }

    private async Task<string?> RunGitSetupAndSnapshotsAsync(RunContext context, CancellationToken cancellationToken)
    {
        await gitService.CheckoutAndPullAsync(context.Project, cancellationToken);
        var commit = await gitService.GetCurrentCommitAsync(context.Project, cancellationToken);

        var inputPath = Path.Combine(context.WorkspacePath, "00-input");
        File.Copy(
            Path.Combine(rootPath, "projects", $"{context.Project.ProjectId}.json"),
            Path.Combine(inputPath, "project-config.snapshot.json"),
            overwrite: true);

        await SnapshotProjectFileAsync(context, context.Project.MainRequirementFile, "requirement.snapshot.md", cancellationToken);
        await SnapshotProjectFileAsync(context, context.Project.ScopeFile, "scope.snapshot.md", cancellationToken);
        await SnapshotProjectFileAsync(context, context.Project.RoadmapFile, "roadmap.snapshot.md", cancellationToken);
        await SnapshotProjectFileAsync(context, context.Project.BacklogFile, "backlog.snapshot.md", cancellationToken);
        await SnapshotProjectFileAsync(context, context.Project.StatusFile, "implementation-status.snapshot.md", cancellationToken);
        await SnapshotProjectFileAsync(context, context.Project.AgentRulesFile, "agent-rules.snapshot.md", cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(inputPath, "git-status-before.txt"), await gitService.GetStatusAsync(context.Project, cancellationToken), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(inputPath, "git-log-before.txt"), await gitService.GetLogAsync(context.Project, cancellationToken), cancellationToken);
        return commit;
    }

    private async Task SnapshotProjectFileAsync(RunContext context, string? relativePath, string snapshotName, CancellationToken cancellationToken)
    {
        var target = Path.Combine(context.WorkspacePath, "00-input", snapshotName);
        if (string.IsNullOrWhiteSpace(relativePath) || ShouldSkipSnapshot(relativePath))
        {
            await File.WriteAllTextAsync(target, "# Snapshot skipped", cancellationToken);
            return;
        }

        var source = Path.Combine(context.Project.RepoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(source))
        {
            await File.WriteAllTextAsync(target, $"# Missing source file{Environment.NewLine}{relativePath}", cancellationToken);
            return;
        }

        File.Copy(source, target, overwrite: true);
    }

    private async Task<string> BuildPlannerPromptAsync(
        RunContext context,
        string projectContext,
        TaskItem selectedTask,
        CancellationToken cancellationToken)
    {
        var template = await File.ReadAllTextAsync(Path.Combine(rootPath, "templates", "planner.md"), cancellationToken);
        var selectedTaskText = $"**ID:** {selectedTask.Id}\n**Title:** {selectedTask.Title}\n**Status:** {selectedTask.Status}";
        var prompt = _templateRenderer.RenderPlannerTemplate(context.Project, template, projectContext, selectedTaskText);
        await File.WriteAllTextAsync(Path.Combine(context.WorkspacePath, "01-planning", "planner-prompt.md"), prompt, cancellationToken);
        return prompt;
    }

    private async Task AppendProjectMemoryAsync(string projectContext, RunContext context, CancellationToken cancellationToken)
    {
        var memoryPath = Path.Combine(rootPath, "workspaces", context.Project.ProjectId, "project-memory.md");
        if (File.Exists(memoryPath))
        {
            _ = projectContext; // already used by caller
            await LogAsync(context, "Project memory found.", cancellationToken);
        }
    }

    private async Task WritePlanningOutputsAsync(RunContext context, string plannerOutput, CancellationToken cancellationToken)
    {
        var planningPath = Path.Combine(context.WorkspacePath, "01-planning");
        await File.WriteAllTextAsync(Path.Combine(planningPath, "planner-output.md"), plannerOutput, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(planningPath, "daily-plan.md"), ExtractBefore(plannerOutput, "# task.json"), cancellationToken);

        var selectedTask = ExtractSection(plannerOutput, "## Selected Task")
            ?? ExtractSection(plannerOutput, "## Codex Task")
            ?? plannerOutput;
        var runContract = ExtractSection(plannerOutput, "# Run Contract") ?? "Follow the planner output and configured project guardrails.";
        var taskJson = ExtractJsonCodeBlock(plannerOutput) ?? "{}";

        await File.WriteAllTextAsync(Path.Combine(planningPath, "selected-task.md"), selectedTask.Trim(), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(planningPath, "task.json"), taskJson.Trim(), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(planningPath, "run-contract.md"), runContract.Trim(), cancellationToken);

        var codexTemplate = await File.ReadAllTextAsync(Path.Combine(rootPath, "templates", "codex-task.md"), cancellationToken);
        var codexTask = _templateRenderer.RenderCodexTaskTemplate(codexTemplate, selectedTask.Trim(), runContract.Trim());
        await File.WriteAllTextAsync(Path.Combine(planningPath, "codex-task.md"), codexTask, cancellationToken);
    }

    private async Task CaptureImplementationStateAsync(RunContext context, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(context.WorkspacePath, "02-implementation");
        await File.WriteAllTextAsync(Path.Combine(outputPath, "git-status-after-implementation.txt"), await gitService.GetStatusAsync(context.Project, cancellationToken), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputPath, "changed-files.txt"), await gitService.GetChangedFilesAsync(context.Project, cancellationToken), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(outputPath, "git-diff.patch"), await gitService.GetDiffAsync(context.Project, cancellationToken), cancellationToken);
    }

    private static async Task WriteGuardrailReportAsync(RunContext context, GuardrailResult guardrails, CancellationToken cancellationToken)
    {
        var report = $$"""
        # Guardrail Result

        - Protected path changes: {{guardrails.HasProtectedPathChanges}}
        - Diff lines: {{guardrails.DiffLines}}
        - Diff too large: {{guardrails.IsDiffTooLarge}}
        - Can auto commit: {{guardrails.CanAutoCommit}}

        ## Protected Path Changes

        {{RenderList(guardrails.ProtectedPathChanges)}}

        ## Dependency Changes

        {{RenderList(guardrails.DependencyChanges)}}
        """;

        await File.WriteAllTextAsync(Path.Combine(context.WorkspacePath, "04-review", "reviewer-output.md"), report, cancellationToken);
    }

    private async Task WriteRetrospectiveAsync(
        RunContext context,
        VerificationSummary verification,
        GuardrailResult guardrails,
        TaskItem selectedTask,
        CancellationToken cancellationToken)
    {
        var planningPath = Path.Combine(context.WorkspacePath, "01-planning");
        var implementationPath = Path.Combine(context.WorkspacePath, "02-implementation");
        var report = $$"""
        # Daily Report

        ## Task

        **ID:** {{selectedTask.Id}}
        **Title:** {{selectedTask.Title}}

        ## What Was Planned

        {{await SafeReadAsync(Path.Combine(planningPath, "selected-task.md"), cancellationToken)}}

        ## What Was Implemented

        See `02-implementation/codex-output.log` and `02-implementation/git-diff.patch`.

        ## Build/Test Result

        - Build passed: {{verification.BuildPassed}}
        - Tests passed: {{verification.TestPassed}}

        ## Product Impact

        One daily task was attempted for {{context.Project.DisplayName}}.

        ## Risks

        - Protected path changes: {{guardrails.HasProtectedPathChanges}}
        - Diff too large: {{guardrails.IsDiffTooLarge}}
        - Dependency changes: {{guardrails.DependencyChanges.Count}}

        ## What The AI Should Do Better Tomorrow

        Keep the task small, preserve protected files, and verify changes before commit.

        ## Suggested Next Task

        Use the next planner run to choose the next small vertical slice.
        """;

        var retrospectivePath = Path.Combine(context.WorkspacePath, "05-retrospective");
        await File.WriteAllTextAsync(Path.Combine(retrospectivePath, "daily-report.md"), report, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(retrospectivePath, "ai-learning-log.md"), "# AI Learning Log\n\nSee daily report for this MVP run.\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(retrospectivePath, "next-day-suggestions.md"), "# Next Day Suggestions\n\nRun the planner again after reviewing today's output.\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(retrospectivePath, "proposed-requirement-updates.md"), "# Proposed Requirement Updates\n\nNo direct requirement edits were made by the orchestrator.\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(retrospectivePath, "proposed-agent-rule-updates.md"), "# Proposed Agent Rule Updates\n\nNo agent rule updates proposed by the orchestrator.\n", cancellationToken);

        var memoryPath = Path.Combine(rootPath, "workspaces", context.Project.ProjectId, "project-memory.md");
        Directory.CreateDirectory(Path.GetDirectoryName(memoryPath)!);
        await File.AppendAllTextAsync(
            memoryPath,
            $"{Environment.NewLine}## {context.RunDate:yyyy-MM-dd}{Environment.NewLine}- Task: {selectedTask.Id} — {selectedTask.Title}{Environment.NewLine}- Build passed: {verification.BuildPassed}{Environment.NewLine}- Tests passed: {verification.TestPassed}{Environment.NewLine}- Changed files: {await SafeReadAsync(Path.Combine(implementationPath, "changed-files.txt"), cancellationToken)}{Environment.NewLine}",
            cancellationToken);
    }

    private static async Task WriteRunReportAsync(RunContext context, ProjectConfig project, RunReportData report, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Run Report");
        sb.AppendLine();
        sb.AppendLine("## Config Validation");
        sb.AppendLine("Config loaded and validated.");
        sb.AppendLine();
        sb.AppendLine("## Task Selection");
        if (!string.IsNullOrWhiteSpace(report.TaskId))
        {
            sb.AppendLine($"Selected: {report.TaskId} — {report.TaskTitle}");
        }
        else if (!string.IsNullOrWhiteSpace(report.BlockedReason))
        {
            sb.AppendLine($"Blocked: {report.BlockedReason}");
        }
        sb.AppendLine();
        sb.AppendLine("## Build/Test Result");
        sb.AppendLine($"Build: {(report.BuildPassed ? "PASSED" : "FAILED")}");
        if (report.TestPassed.HasValue)
        {
            sb.AppendLine($"Tests: {(report.TestPassed.Value ? "PASSED" : "FAILED")}");
        }
        sb.AppendLine();
        sb.AppendLine("## Files Changed");
        sb.AppendLine(report.FilesChanged.Count == 0 ? "None." : string.Join("\n", report.FilesChanged.Select(f => $"- {f}")));
        sb.AppendLine();
        sb.AppendLine("## Commit");
        sb.AppendLine(string.IsNullOrWhiteSpace(report.CommitHash) ? "Not committed." : report.CommitHash);
        sb.AppendLine();
        sb.AppendLine("## Remaining Risks");
        sb.AppendLine(report.RemainingRisks.Count == 0 ? "None." : string.Join("\n", report.RemainingRisks.Select(r => $"- {r}")));
        sb.AppendLine();
        sb.AppendLine("## Next Task Recommendation");
        sb.AppendLine(string.IsNullOrWhiteSpace(report.NextTaskTitle) ? "See backlog." : report.NextTaskTitle);

        await File.WriteAllTextAsync(Path.Combine(context.WorkspacePath, "05-retrospective", "run-report.md"), sb.ToString(), cancellationToken);
    }

    private async Task<string?> ApplySafeCommitPolicyAsync(
        RunContext context,
        VerificationSummary verification,
        GuardrailResult guardrails,
        TaskItem selectedTask,
        CancellationToken cancellationToken)
    {
        var project = context.Project;
        var failureAnalysisPath = Path.Combine(context.WorkspacePath, "03-verification", "failure-analysis.md");

        if (project.CommitOnlyIfBuildPasses && !verification.BuildPassed)
        {
            await File.WriteAllTextAsync(failureAnalysisPath, "# Failure Analysis\n\nBuild failed and commitOnlyIfBuildPasses is true. Auto-commit skipped.\n", cancellationToken);
            return null;
        }

        if (!project.AllowAutoCommit)
        {
            await File.WriteAllTextAsync(failureAnalysisPath, "# Commit Policy\n\nAuto-commit is disabled for this project.\n", cancellationToken);
            return null;
        }

        if (!guardrails.CanAutoCommit)
        {
            await File.WriteAllTextAsync(failureAnalysisPath, "# Commit Policy\n\nGuardrails blocked auto-commit. Review the patch manually.\n", cancellationToken);
            return null;
        }

        var changedFiles = ReadLines(Path.Combine(context.WorkspacePath, "02-implementation", "changed-files.txt"));
        if (changedFiles.Count == 0)
        {
            await File.WriteAllTextAsync(failureAnalysisPath, "# Commit Policy\n\nNo changed files to commit.\n", cancellationToken);
            return null;
        }

        var message = BuildCommitMessage(selectedTask, verification);
        await gitService.CommitAsync(project, message, cancellationToken);

        if (project.AllowAutoPush)
        {
            await gitService.PushAsync(project, cancellationToken);
        }

        return await TryGetCommitAsync(project, cancellationToken);
    }

    private static string BuildCommitMessage(TaskItem task, VerificationSummary verification)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AutoDev: {task.Id} {task.Title}");
        sb.AppendLine();
        sb.AppendLine($"Summary: Implemented {task.Title}");
        sb.AppendLine($"Build: {(verification.BuildPassed ? "PASSED" : "FAILED")}");
        sb.AppendLine($"Tests: {(verification.TestPassed ? "PASSED" : "N/A")}");
        sb.Append("Remaining risks: see run-report.md");
        return sb.ToString();
    }

    private async Task<RunMetadata> UpdateMetadataAsync(
        RunContext context,
        RunMetadata? metadata = null,
        string? completedStep = null,
        string? status = null,
        string? commitBefore = null,
        bool? buildPassed = null,
        bool? testPassed = null,
        DateTimeOffset? startedAt = null,
        CancellationToken cancellationToken = default)
    {
        metadata ??= await workspaceService.ReadMetadataAsync(context.WorkspacePath, cancellationToken)
            ?? new RunMetadata
            {
                ProjectId = context.Project.ProjectId,
                RunDate = context.RunDate.ToString("yyyy-MM-dd"),
                Branch = context.Project.Branch
            };

        var steps = completedStep is null ? metadata.CompletedSteps : [.. metadata.CompletedSteps, completedStep];
        metadata = metadata with
        {
            Status = status ?? metadata.Status,
            CommitBefore = commitBefore ?? metadata.CommitBefore,
            BuildPassed = buildPassed ?? metadata.BuildPassed,
            TestPassed = testPassed ?? metadata.TestPassed,
            StartedAt = startedAt ?? metadata.StartedAt,
            CompletedSteps = steps
        };

        await workspaceService.WriteMetadataAsync(context.WorkspacePath, metadata, cancellationToken);
        return metadata;
    }

    private async Task<string?> TryGetCommitAsync(ProjectConfig project, CancellationToken cancellationToken)
    {
        try
        {
            return await gitService.GetCurrentCommitAsync(project, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldSkipSnapshot(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return normalized.Equals(".env", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("appsettings.Production.json", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("local.properties", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("secrets/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("keystore/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractBefore(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? text : text[..index].TrimEnd();
    }

    private static string? ExtractSection(string text, string heading)
    {
        var start = text.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var next = SectionHeadingRegex().Match(text, start + heading.Length);
        return next.Success ? text[start..next.Index] : text[start..];
    }

    private static string? ExtractJsonCodeBlock(string text)
    {
        var match = JsonBlockRegex().Match(text);
        return match.Success ? match.Groups["json"].Value : null;
    }

    private static IReadOnlyList<string> ReadLines(string path)
    {
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
            : [];
    }

    private static async Task<string> SafeReadAsync(string path, CancellationToken cancellationToken)
    {
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;
    }

    private static string RenderList(IEnumerable<string> items)
    {
        var list = items.ToArray();
        return list.Length == 0 ? "- none" : string.Join(Environment.NewLine, list.Select(item => $"- {item}"));
    }

    private static Task LogAsync(RunContext context, string message, CancellationToken cancellationToken)
    {
        var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
        return File.AppendAllTextAsync(Path.Combine(context.WorkspacePath, "run.log"), line, cancellationToken);
    }

    [GeneratedRegex(@"(?m)^#{1,2}\s+")]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"```json\s*(?<json>.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonBlockRegex();
}
