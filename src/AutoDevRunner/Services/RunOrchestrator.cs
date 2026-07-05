using System.Text;
using AutoDevRunner.Config;
using AutoDevRunner.Data;
using AutoDevRunner.Models;
using AutoDevRunner.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// The full per-project run pipeline:
/// validate repo → branch → build prompt → call provider (by priority) →
/// classify outcome → validate build → guardrails → commit/push →
/// persist run + state → write summary file → email.
/// Scoped service: resolve one per run.
/// </summary>
public class RunOrchestrator
{
    private readonly AppDbContext _db;
    private readonly GitService _git;
    private readonly GuardrailService _guard;
    private readonly PromptBuilder _promptBuilder;
    private readonly OpenAiCreativePlanner _planner;
    private readonly SummaryParser _summaryParser;
    private readonly EmailService _email;
    private readonly ProviderRegistry _providers;
    private readonly ProcessRunner _proc;
    private readonly RunLock _lock;
    private readonly AutoDevOptions _opt;
    private readonly ILogger<RunOrchestrator> _log;

    public RunOrchestrator(
        AppDbContext db, GitService git, GuardrailService guard,
        PromptBuilder promptBuilder, OpenAiCreativePlanner planner,
        SummaryParser summaryParser, EmailService email,
        ProviderRegistry providers, ProcessRunner proc, RunLock runLock,
        IOptions<AutoDevOptions> opt, ILogger<RunOrchestrator> log)
    {
        _db = db; _git = git; _guard = guard; _promptBuilder = promptBuilder;
        _planner = planner; _summaryParser = summaryParser; _email = email;
        _providers = providers; _proc = proc; _lock = runLock; _opt = opt.Value; _log = log;
    }

    public async Task<RunRecord?> RunProjectAsync(int projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) { _log.LogWarning("Project {Id} not found.", projectId); return null; }

        if (!_lock.TryAcquire(projectId))
        {
            _log.LogInformation("Project {Name} is already running; skipping.", project.Name);
            return null;
        }

        try
        {
            return await ExecuteAsync(project, ct);
        }
        finally
        {
            _lock.Release(projectId);
        }
    }

    private async Task<RunRecord> ExecuteAsync(Project project, CancellationToken ct)
    {
        var run = new RunRecord { ProjectId = project.Id, Status = RunStatus.Running };
        _db.Runs.Add(run);
        await _db.SaveChangesAsync(ct);

        var logBuffer = new StringBuilder();
        void Log(string line) => logBuffer.AppendLine(line);

        try
        {
            // 1. Validate repo.
            if (!await _git.IsGitRepoAsync(project.RepoPath, ct))
            {
                await FailAsync(project, run, RunStatus.Failed,
                    $"Not a git repository: {project.RepoPath}", logBuffer, null, ct);
                return run;
            }

            // 2. Branch policy.
            string branch = await _git.GetCurrentBranchAsync(project.RepoPath, ct);
            if (!project.AllowRunOnMainBranch && GitService.IsProtectedBranch(branch))
            {
                var aiBranch = $"{project.AiBranchPrefix}/{DateTime.UtcNow:yyyyMMdd}-{run.Id}";
                branch = await _git.EnsureBranchAsync(project.RepoPath, aiBranch, ct);
                Log($"Created/switched to AI branch: {branch}");
            }
            run.Branch = branch;
            project.CurrentBranch = branch;

            // 3. Load brief.
            var brief = await LoadBriefAsync(project, ct);

            // 3b. Optional OpenAI creative planner (knowledge-base grounded). Fail-soft.
            var creativePlan = await _planner.CreatePlanAsync(project, brief, ct);
            if (!string.IsNullOrWhiteSpace(creativePlan))
                Log("Creative planner produced a knowledge-base-grounded plan for this run.");

            // 4. Build prompt.
            var prompt = _promptBuilder.Build(project, brief, run, creativePlan);

            // 5. Resolve provider order and try each until one runs (or all exhausted).
            var order = _providers.ResolveOrder(project.ProviderPriority).ToList();
            if (order.Count == 0)
            {
                await FailAsync(project, run, RunStatus.Failed,
                    "No enabled providers for this project.", logBuffer, null, ct);
                return run;
            }

            ProviderInvocation? invocation = null;
            var timeout = TimeSpan.FromMinutes(Math.Max(1, project.MaxRunMinutes));

            foreach (var provider in order)
            {
                run.Provider = provider.Kind;
                Log($"--- Invoking {provider.Kind} (timeout {timeout.TotalMinutes:0}m) ---");
                _log.LogInformation("Project {Name}: invoking {Provider}", project.Name, provider.Kind);

                invocation = await provider.RunAsync(prompt, project.RepoPath, timeout, Log, ct);
                await UpdateProviderStateAsync(provider.Kind, invocation, ct);

                // Success ends the run. Any failure (quota/auth/timeout/error) falls
                // through to the next provider in the priority order, if any.
                if (invocation.Outcome is ProviderOutcome.Success)
                    break;

                Log($"{provider.Kind} returned {invocation.Outcome}: {invocation.Reason}");
            }

            run.Usage = invocation!.Usage ?? "Unknown / provider does not expose usage";
            if (!string.IsNullOrWhiteSpace(invocation.SessionId))
                project.ProviderSessionId = invocation.SessionId;

            // 6. Map provider outcome to run status.
            if (invocation.Outcome is ProviderOutcome.QuotaLimit)
            {
                await PauseAsync(project, run, RunStatus.QuotaLimit, invocation, logBuffer, ct);
                return run;
            }
            if (invocation.Outcome is ProviderOutcome.AuthError)
            {
                await PauseAsync(project, run, RunStatus.AuthError, invocation, logBuffer, ct);
                return run;
            }
            if (invocation.Outcome is ProviderOutcome.Timeout)
            {
                // Timed out but may have made progress — capture what changed, mark paused.
                await PauseAsync(project, run, RunStatus.Paused, invocation, logBuffer, ct);
                return run;
            }
            if (invocation.Outcome is ProviderOutcome.Error)
            {
                // Provider ran but errored (or executable could not start). Capture
                // any partial changes (without committing) and mark the run failed.
                await PauseAsync(project, run, RunStatus.Failed, invocation, logBuffer, ct);
                return run;
            }

            // 7. Parse the AI summary.
            var summary = _summaryParser.Parse(invocation.Output);
            run.Summary = summary.FullText;
            project.LastSummary = summary.FullText;
            project.CurrentTask = summary.NextTask ?? summary.Pending ?? summary.Task;

            // 8. Record changed files.
            var changed = await _git.GetChangedFilesAsync(project.RepoPath, ct);
            run.ChangedFiles = string.Join('\n', changed);

            // 9. Guardrail check on changed files.
            var guard = _guard.Check(changed);
            if (!guard.Ok)
            {
                run.Reason = "Guardrail violation: protected files changed: " + string.Join(", ", guard.Violations);
                Log(run.Reason);
                await FinalizeAsync(project, run, RunStatus.Failed, summary, logBuffer, ct, commit: false);
                return run;
            }

            // 10. Validation command.
            if (!string.IsNullOrWhiteSpace(project.ValidationCommand))
                await RunValidationAsync(project, run, timeout, Log, ct);

            // 11. Commit (only if changes, build ok-or-not-run, and policy allows).
            var status = run.ValidationRun && !run.ValidationPassed ? RunStatus.Failed : RunStatus.Success;
            var doCommit = project.AutoCommit && changed.Count > 0
                           && (!run.ValidationRun || run.ValidationPassed);
            await FinalizeAsync(project, run, status, summary, logBuffer, ct, commit: doCommit);
            return run;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled error running project {Name}", project.Name);
            Log("EXCEPTION: " + ex);
            await FailAsync(project, run, RunStatus.Failed, ex.Message, logBuffer, null, ct);
            return run;
        }
    }

    // ---- helpers ----

    private async Task<string> LoadBriefAsync(Project project, CancellationToken ct)
    {
        var path = Path.IsPathRooted(project.BriefPath)
            ? project.BriefPath
            : Path.Combine(project.RepoPath, project.BriefPath);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : string.Empty;
    }

    private async Task RunValidationAsync(Project project, RunRecord run, TimeSpan timeout,
        Action<string> log, CancellationToken ct)
    {
        log($"--- Validation: {project.ValidationCommand} ---");
        var (file, args) = SplitCommand(project.ValidationCommand!);
        var r = await _proc.RunAsync(file, args, project.RepoPath, timeout, log, ct);
        run.ValidationRun = true;
        run.ValidationPassed = r is { ExitCode: 0, TimedOut: false };
        run.ValidationOutput = r.Combined;
        log($"Validation {(run.ValidationPassed ? "PASSED" : "FAILED")} (exit {r.ExitCode})");
    }

    private async Task UpdateProviderStateAsync(ProviderKind kind, ProviderInvocation inv, CancellationToken ct)
    {
        var st = await _db.ProviderStates.FirstOrDefaultAsync(p => p.Provider == kind, ct);
        if (st is null) { st = new ProviderState { Provider = kind }; _db.ProviderStates.Add(st); }

        switch (inv.Outcome)
        {
            case ProviderOutcome.Success:
                st.LastSuccessAt = DateTime.UtcNow; break;
            case ProviderOutcome.AuthError:
                st.LastAuthErrorAt = DateTime.UtcNow; break;
            case ProviderOutcome.QuotaLimit:
                st.LastQuotaLimitAt = DateTime.UtcNow;
                st.LastQuotaResetHint = inv.Reason; break;
        }
        if (!string.IsNullOrWhiteSpace(inv.Usage)) st.LastKnownUsage = inv.Usage;
        await _db.SaveChangesAsync(ct);
    }

    private async Task PauseAsync(Project project, RunRecord run, RunStatus status,
        ProviderInvocation inv, StringBuilder logBuffer, CancellationToken ct)
    {
        run.Reason = inv.Reason ?? status.ToString();
        // Capture any partial progress.
        var changed = await _git.GetChangedFilesAsync(project.RepoPath, ct);
        run.ChangedFiles = string.Join('\n', changed);
        await FinalizeAsync(project, run, status, _summaryParser.Parse(inv.Output), logBuffer, ct, commit: false);
    }

    private async Task FailAsync(Project project, RunRecord run, RunStatus status,
        string reason, StringBuilder logBuffer, ParsedSummary? summary, CancellationToken ct)
    {
        run.Reason = reason;
        await FinalizeAsync(project, run, status, summary, logBuffer, ct, commit: false);
    }

    private async Task FinalizeAsync(Project project, RunRecord run, RunStatus status,
        ParsedSummary? summary, StringBuilder logBuffer, CancellationToken ct, bool commit)
    {
        run.Status = status;
        run.FinishedAt = DateTime.UtcNow;

        if (commit)
        {
            var msg = BuildCommitMessage(project, summary);
            var sha = await _git.CommitAllAsync(project.RepoPath, msg, ct);
            run.CommitSha = sha;
            logBuffer.AppendLine(sha is null ? "Commit failed or nothing to commit." : $"Committed {sha}");

            if (sha is not null && project.AutoPush && run.Branch is not null)
            {
                var pushed = await _git.PushAsync(project.RepoPath, run.Branch, ct);
                logBuffer.AppendLine(pushed ? "Pushed to origin." : "Push failed.");
            }
        }

        // Persist run log + markdown summary to disk.
        run.LogPath = await WriteRunFilesAsync(project, run, summary, logBuffer.ToString(), ct);

        // Update denormalized project snapshot.
        project.LastRunStatus = status;
        project.LastProvider = run.Provider;
        project.LastRunAt = run.FinishedAt;
        project.LastError = status is RunStatus.Success ? null : run.Reason;

        await _db.SaveChangesAsync(ct);

        // Email report.
        var report = _email.BuildReport(project, run, summary);
        var subject = $"[AutoDev] {project.Name} — {status}";
        run.EmailSent = await _email.SendAsync(subject, report, project.Name, status.ToString(), ct);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Project {Name} run #{Run} finished: {Status}", project.Name, run.Id, status);
    }

    private async Task<string> WriteRunFilesAsync(Project project, RunRecord run,
        ParsedSummary? summary, string log, CancellationToken ct)
    {
        var dir = Path.Combine(project.RepoPath, ".ai-runner", "runs");
        Directory.CreateDirectory(dir);
        var stamp = run.StartedAt.ToString("yyyyMMdd-HHmmss");

        var md = new StringBuilder();
        md.AppendLine($"# Run {run.Id} — {project.Name}");
        md.AppendLine($"- Provider: {run.Provider}");
        md.AppendLine($"- Branch: {run.Branch}");
        md.AppendLine($"- Status: {run.Status}");
        md.AppendLine($"- Started: {run.StartedAt:u}");
        md.AppendLine($"- Finished: {run.FinishedAt:u}");
        md.AppendLine($"- Usage: {run.Usage}");
        if (!string.IsNullOrWhiteSpace(run.Reason)) md.AppendLine($"- Reason: {run.Reason}");
        if (!string.IsNullOrWhiteSpace(run.CommitSha)) md.AppendLine($"- Commit: {run.CommitSha}");
        md.AppendLine();
        if (summary is not null)
        {
            md.AppendLine("## Summary");
            md.AppendLine(summary.FullText);
            md.AppendLine();
        }
        md.AppendLine("## Changed files");
        md.AppendLine(string.IsNullOrWhiteSpace(run.ChangedFiles) ? "(none)" : run.ChangedFiles);
        md.AppendLine();
        md.AppendLine("## Log");
        md.AppendLine("```");
        md.AppendLine(log);
        md.AppendLine("```");

        var mdPath = Path.Combine(dir, $"{stamp}-run{run.Id}.md");
        await File.WriteAllTextAsync(mdPath, md.ToString(), ct);
        return mdPath;
    }

    private static string BuildCommitMessage(Project project, ParsedSummary? summary)
    {
        var task = summary?.Task ?? project.CurrentTask ?? "autonomous changes";
        var first = task.Split('\n')[0];
        if (first.Length > 72) first = first[..72];
        return $"autodev: {first}\n\nAutomated change by AutoDev runner.";
    }

    private static (string file, string args) SplitCommand(string command)
    {
        command = command.Trim();
        var idx = command.IndexOf(' ');
        return idx < 0 ? (command, string.Empty) : (command[..idx], command[(idx + 1)..]);
    }
}
