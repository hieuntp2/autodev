using System.Text;
using AutoDevRunner.Config;
using AutoDevRunner.Data;
using AutoDevRunner.Models;
using AutoDevRunner.Providers;
using AutoDevRunner.Skills;
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
    private readonly ProviderAvailability _availability;
    private readonly ProcessRunner _proc;
    private readonly RunLock _lock;
    private readonly SkillRegistry _skills;
    private readonly SkillExporter _skillExporter;
    private readonly ProjectGoalService _goals;
    private readonly RiskAssessor _risk;
    private readonly ArtifactTracker _artifacts;
    private readonly ProjectMemoryWriter _memory;
    private readonly RunMetadataStore _runMeta;
    private readonly TaskProposer _proposer;
    private readonly RunHistoryService _history;
    private readonly RetrospectiveWriter _retro;
    private readonly CodexUsageReader _codexUsage;
    private readonly AutoDevOptions _opt;
    private readonly ILogger<RunOrchestrator> _log;

    // ---- Per-run state, recorded in the run's JSON sidecar (scoped instance) ----
    private IReadOnlyList<SkillMatch> _selectedSkills = Array.Empty<SkillMatch>();
    private LifecycleStage _stage = LifecycleStage.Planned;
    private RiskAssessment _riskAssessment = new(RiskLevel.Normal, new());
    private List<ArtifactRef> _trackedArtifacts = new();
    private List<string> _memoryUpdates = new();
    private List<string> _nextSuggestedTasks = new();
    private string? _taskSource;
    private RunLessons _lessons = RunLessons.Empty;
    private int? _promptChars;
    private int? _promptEstTokens;
    private int? _inputTokens;
    private int? _outputTokens;
    private decimal? _costUsd;
    private string? _model;

    public RunOrchestrator(
        AppDbContext db, GitService git, GuardrailService guard,
        PromptBuilder promptBuilder, OpenAiCreativePlanner planner,
        SummaryParser summaryParser, EmailService email,
        ProviderRegistry providers, ProviderAvailability availability,
        ProcessRunner proc, RunLock runLock,
        SkillRegistry skills, SkillExporter skillExporter,
        ProjectGoalService goals, RiskAssessor risk, ArtifactTracker artifacts,
        ProjectMemoryWriter memory, RunMetadataStore runMeta, TaskProposer proposer,
        RunHistoryService history, RetrospectiveWriter retro, CodexUsageReader codexUsage,
        IOptions<AutoDevOptions> opt, ILogger<RunOrchestrator> log)
    {
        _db = db; _git = git; _guard = guard; _promptBuilder = promptBuilder;
        _planner = planner; _summaryParser = summaryParser; _email = email;
        _providers = providers; _availability = availability;
        _proc = proc; _lock = runLock; _skills = skills; _skillExporter = skillExporter;
        _goals = goals; _risk = risk; _artifacts = artifacts; _memory = memory; _runMeta = runMeta;
        _proposer = proposer; _history = history; _retro = retro; _opt = opt.Value; _log = log;
        _codexUsage = codexUsage;
    }

    public Task<RunRecord?> RunProjectAsync(int projectId, CancellationToken ct = default) =>
        RunProjectCoreAsync(projectId, acquiredLease: null, releaseLeaseOnCompletion: true, ct);

    public Task<RunRecord?> RunProjectAsync(int projectId, RunLockLease acquiredLease,
        bool releaseLeaseOnCompletion, CancellationToken ct = default) =>
        RunProjectCoreAsync(projectId, acquiredLease, releaseLeaseOnCompletion, ct);

    private async Task<RunRecord?> RunProjectCoreAsync(int projectId, RunLockLease? acquiredLease,
        bool releaseLeaseOnCompletion, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
        {
            _log.LogWarning("Project {Id} not found.", projectId);
            if (acquiredLease is not null && releaseLeaseOnCompletion) acquiredLease.Dispose();
            return null;
        }

        RunLockLease? lease = acquiredLease;
        if (lease is not null && lease.ProjectId != projectId)
        {
            _log.LogWarning("Project {Name}: acquired lock belongs to project {LockedProject}; skipping.",
                project.Name, lease.ProjectId);
            if (releaseLeaseOnCompletion) lease.Dispose();
            return null;
        }

        if (lease is null && !_lock.TryAcquire(project, out lease, out var reason))
        {
            _log.LogInformation("Project {Name} is already running; skipping. {Reason}", project.Name, reason);
            return null;
        }

        lease?.Renew(RunLock.LeaseDurationFor(project));
        try
        {
            return await ExecuteAsync(project, ct);
        }
        finally
        {
            if (lease is not null && releaseLeaseOnCompletion)
                lease.Dispose();
        }
    }

    private async Task<RunRecord> ExecuteAsync(Project project, CancellationToken ct)
    {
        var run = new RunRecord { ProjectId = project.Id, Status = RunStatus.Running };
        _db.Runs.Add(run);
        await _db.SaveChangesAsync(ct);

        var logBuffer = new StringBuilder();
        void Log(string line)
        {
            logBuffer.AppendLine(line);
            // Mirror to the file log so a live run can be followed with
            // Logging:File:MinLevel=Debug (the buffer is only persisted at the end).
            if (line.StartsWith("still running - last output ", StringComparison.Ordinal))
                _log.LogInformation("run#{RunId} {Line}", run.Id, line);
            else
                _log.LogDebug("run#{RunId} {Line}", run.Id, line);
        }

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

            // 3. Load brief + project goal layer (.ai-runner/PROJECT_GOAL.md etc.).
            var brief = await LoadBriefAsync(project, ct);
            var goal = await _goals.LoadAsync(project.RepoPath, ct);
            if (goal.HasGoal) Log("Loaded project goal from .ai-runner/PROJECT_GOAL.md.");

            // 3a. Learning loop: read the most recent runs to feed task selection
            //     and the prompt. File-based (run sidecars), no external cost.
            if (_opt.Learning.Enabled)
            {
                _lessons = _history.Analyze(project.RepoPath,
                    _opt.Learning.RecentRunsWindow, _opt.Learning.RepeatedFailureThreshold);
                if (_lessons.HasAny)
                    Log($"Learning: read {_lessons.Recent.Count} recent run(s)"
                        + (_lessons.RepeatedlyFailingTasks.Count > 0
                            ? $"; avoiding {_lessons.RepeatedlyFailingTasks.Count} repeatedly-failing task(s)."
                            : "."));
            }

            // 3b. Optional OpenAI creative planner (goal + KB grounded). Fail-soft.
            //     When no task is in progress, it proposes the next small task.
            var creativePlan = await _planner.CreatePlanAsync(project, brief, goal, ct);
            if (!string.IsNullOrWhiteSpace(creativePlan))
                Log(string.IsNullOrWhiteSpace(project.CurrentTask)
                    ? "Creative planner proposed the next task toward the project goal."
                    : "Creative planner produced a goal-grounded plan for this run.");

            // 3c. Task selection: if no task is in progress and the planner did not
            //     supply direction, propose the next small task locally (heuristic
            //     fallback over BACKLOG/IDEAS/maintenance).
            TaskProposal? proposal = null;
            if (string.IsNullOrWhiteSpace(project.CurrentTask))
            {
                proposal = _proposer.Propose(project, goal, _lessons);
                _taskSource = string.IsNullOrWhiteSpace(creativePlan) ? proposal.Source : "planner";
                Log($"No current task — proposed ({_taskSource}): {proposal.Title} [risk {proposal.Risk}]");
            }
            else
            {
                _taskSource = "explicit";
            }

            // 3d. Pre-run risk from the task intent (title/plan). Refined post-run
            //     with the actual changed files.
            var intent = string.Join('\n', new[] { project.CurrentTask, proposal?.Title, creativePlan }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            _riskAssessment = _risk.Assess(Array.Empty<GitChange>(), intent);

            // 3e. Risk gate: hard guards (file deletion / writes outside the repo)
            //     always block; other risky tasks block only when risky autonomous
            //     runs are not allowed.
            var preGate = EvaluateRiskGate();
            if (preGate.Blocked)
            {
                _stage = LifecycleStage.Failed;
                Log("RISK GATE: " + preGate.Why);
                await FailAsync(project, run, RunStatus.Paused, preGate.Why, logBuffer, null, ct);
                return run;
            }

            // 3f. Select global AutoDev skills (also match against the proposed title).
            _selectedSkills = SelectSkills(project, brief + "\n" + (proposal?.Title ?? ""), creativePlan, Log);

            // 4. Build prompt. Task is now planned.
            _stage = LifecycleStage.Planned;
            var prompt = _promptBuilder.Build(project, brief, run, creativePlan, _selectedSkills,
                goal, proposal, _riskAssessment.Level, _opt.Risk, _lessons);
            _promptChars = prompt.Length;
            _promptEstTokens = EstimateTokens(prompt.Length);
            Log($"Prompt prepared: {_promptChars} chars (~{_promptEstTokens} tokens).");

            // Persist the exact prompt, plan and task on the run so prompt/plan
            // evolution is queryable in the DB (not just in the .ai-runner files).
            run.Prompt = prompt;
            run.CreativePlan = creativePlan;
            run.TaskTitle = !string.IsNullOrWhiteSpace(project.CurrentTask)
                ? project.CurrentTask!.Trim() : proposal?.Title;
            run.TaskSource = _taskSource;

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
            var idleTimeout = TimeSpan.FromMinutes(Math.Max(1, _opt.Execution.IdleTimeoutMinutes));
            var heartbeat = TimeSpan.FromMinutes(Math.Max(1, _opt.Execution.HeartbeatMinutes));
            if (project.MaxRunMinutes < 60)
                Log($"Hard time cap is {project.MaxRunMinutes} minutes; existing project settings below 60 minutes may interrupt long runs.");

            foreach (var provider in order)
            {
                // Benched providers (quota ceiling reached / recent quota error)
                // are skipped so they are not burned further.
                if (!_availability.IsAvailable(provider.Kind, out var benchedWhy))
                {
                    Log($"Skipping {provider.Kind}: {benchedWhy}");
                    continue;
                }

                run.Provider = provider.Kind;
                _stage = LifecycleStage.Running;
                Log($"--- Invoking {provider.Kind} (hard cap {timeout.TotalMinutes:0}m, idle timeout {idleTimeout.TotalMinutes:0}m) ---");
                _log.LogInformation("Project {Name}: invoking {Provider}", project.Name, provider.Kind);

                var providerStartedAt = DateTimeOffset.UtcNow;
                invocation = await provider.RunAsync(prompt, project.RepoPath, timeout, Log, ct,
                    idleTimeout, heartbeat);
                if (provider.Kind is ProviderKind.Codex)
                    invocation = EnrichCodexTokenUsage(invocation, providerStartedAt);

                await UpdateProviderStateAsync(provider.Kind, invocation, ct);

                if (invocation.Outcome is ProviderOutcome.QuotaLimit)
                {
                    _availability.Suspend(provider.Kind,
                        DateTimeOffset.Now.AddMinutes(Math.Max(1, _opt.Continuous.QuotaCooldownMinutes)),
                        invocation.Reason ?? "provider reported quota/rate limit");
                }

                // Success ends the run. Any failure (quota/auth/timeout/error) falls
                // through to the next provider in the priority order, if any.
                if (invocation.Outcome is ProviderOutcome.Success)
                    break;

                Log($"{provider.Kind} returned {invocation.Outcome}: {invocation.Reason}");
            }

            if (invocation is null)
            {
                await FailAsync(project, run, RunStatus.Failed,
                    "All providers are currently benched (quota ceiling / cooldown).", logBuffer, null, ct);
                return run;
            }

            run.Usage = invocation.Usage ?? "Unknown / provider does not expose usage";
            _inputTokens = invocation.InputTokens;
            _outputTokens = invocation.OutputTokens;
            _costUsd = invocation.CostUsd;
            _model = invocation.Model;
            Log($"Prompt: {_promptChars ?? 0} chars (~{_promptEstTokens ?? 0} tokens); provider used {FormatCount(_inputTokens)} in / {FormatCount(_outputTokens)} out.");
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

            // 7. Parse the AI summary (v2 structured fields, backward compatible).
            var summary = _summaryParser.Parse(invocation.Output);
            run.Summary = summary.FullText;
            project.LastSummary = summary.FullText;
            project.CurrentTask = summary.NextTask ?? summary.Pending ?? summary.Task;
            _nextSuggestedTasks = SplitTasks(summary.NextSuggestedTasks ?? summary.NextTask);

            // 7b. Project memory: fold ideas/decisions/next-tasks into .ai-runner/ —
            //     GATED by AutoDev:ProjectMemory:AutoWriteEnabled. Done BEFORE capturing
            //     changed files so any updates are part of this commit.
            _memoryUpdates = await _memory.UpdateAsync(
                project.RepoPath, summary, DateTime.UtcNow.ToString("yyyy-MM-dd"),
                _opt.ProjectMemory.AutoWriteEnabled, ct);
            if (_memoryUpdates.Count > 0)
                Log("Updated project memory: " + string.Join(", ", _memoryUpdates));
            else if (!_opt.ProjectMemory.AutoWriteEnabled && !string.IsNullOrWhiteSpace(summary.MemoryUpdates))
                Log("Memory auto-write disabled — recorded in run log only: " + summary.MemoryUpdates);

            // 7c. Optional AI brief evolution (gated per project). Done BEFORE capturing
            //     changed files so the proposal file is imported to the DB, not committed.
            await MaybeEvolveBriefAsync(project, run, Log, ct);

            // 8. Record changed files (+ status for risk) and track generated artifacts.
            //    Merge git-detected artifacts with the paths the AI declared.
            var changes = await _git.GetChangesAsync(project.RepoPath, ct);
            var changed = changes.Select(c => c.Path).ToList();
            run.ChangedFiles = string.Join('\n', changed);
            var gitArtifacts = _artifacts.Track(changed, project.RepoPath);
            _trackedArtifacts = _artifacts.Merge(gitArtifacts, summary.ArtifactPaths, project.RepoPath);
            if (_trackedArtifacts.Count > 0)
                Log($"Tracked {_trackedArtifacts.Count} artifact(s): " +
                    string.Join(", ", _trackedArtifacts.Take(6).Select(a => a.Path)));

            // 8b. Refine risk with the actual changed files (keep the worst of pre/post).
            var postRisk = _risk.Assess(changes, project.CurrentTask ?? summary.EffectiveTitle);
            if ((int)postRisk.Level >= (int)_riskAssessment.Level) _riskAssessment = postRisk;
            if (_riskAssessment.Level == RiskLevel.Risky)
                Log("RISK: risky run — " + string.Join("; ", _riskAssessment.Reasons));
            else
                Log($"Risk level: {_riskAssessment.Level}.");

            // 8c. If the changes hit a hard guard (file deletion / writes outside
            //     the repo) — always blocked — or turned out risky while risky runs
            //     aren't allowed, do not commit them: leave them for manual review.
            var postGate = EvaluateRiskGate();
            if (postGate.Blocked)
            {
                _stage = LifecycleStage.Failed;
                run.Reason = postGate.Hard
                    ? "Changes left uncommitted — " + postGate.Why
                    : "Risky changes left uncommitted (requires manual review): "
                      + string.Join("; ", _riskAssessment.Reasons);
                Log("RISK GATE: " + run.Reason);
                await FinalizeAsync(project, run, RunStatus.Paused, summary, logBuffer, ct, commit: false);
                return run;
            }

            // 9. Guardrail check on changed files.
            var guard = _guard.Check(changed);
            if (!guard.Ok)
            {
                run.Reason = "Guardrail violation: protected files changed: " + string.Join(", ", guard.Violations);
                Log(run.Reason);
                _stage = LifecycleStage.Failed;
                await FinalizeAsync(project, run, RunStatus.Failed, summary, logBuffer, ct, commit: false);
                return run;
            }

            // 10. Validation command.
            if (!string.IsNullOrWhiteSpace(project.ValidationCommand))
                await RunValidationAsync(project, run, timeout, Log, ct);
            if (!run.ValidationRun || run.ValidationPassed)
                _stage = LifecycleStage.Validated;

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

    private ProviderInvocation EnrichCodexTokenUsage(ProviderInvocation invocation, DateTimeOffset providerStartedAt)
    {
        if (invocation.InputTokens is not null || invocation.OutputTokens is not null)
            return invocation;

        var usage = _codexUsage.TryReadLatestTurnUsage(providerStartedAt.AddSeconds(-5));
        if (usage is null) return invocation;

        return invocation with
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            Model = invocation.Model ?? usage.Model,
            Usage = invocation.Usage ?? FormatInvocationUsage(usage.InputTokens, usage.OutputTokens, invocation.CostUsd)
        };
    }

    private static int EstimateTokens(int chars) => (int)Math.Ceiling(chars / 4.0);

    private static string FormatCount(int? count) => count?.ToString("N0") ?? "?";

    private static string? FormatInvocationUsage(int? inputTokens, int? outputTokens, decimal? costUsd)
    {
        var parts = new List<string>();
        if (inputTokens is not null || outputTokens is not null)
            parts.Add($"in {FormatCount(inputTokens)} / out {FormatCount(outputTokens)}");
        if (costUsd is not null)
            parts.Add($"${costUsd.Value:0.######}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private string MeasurementSummary()
    {
        var parts = new List<string>();
        if (_promptChars is not null)
            parts.Add($"prompt {_promptChars.Value:N0} chars (~{_promptEstTokens ?? EstimateTokens(_promptChars.Value):N0} tokens)");
        if (_inputTokens is not null || _outputTokens is not null)
            parts.Add($"provider {FormatCount(_inputTokens)} in / {FormatCount(_outputTokens)} out");
        if (_costUsd is not null)
            parts.Add($"cost ${_costUsd.Value:0.######}");
        if (!string.IsNullOrWhiteSpace(_model))
            parts.Add($"model {_model}");
        return parts.Count == 0 ? "prompt/token usage unknown" : string.Join("; ", parts);
    }

    /// <summary>
    /// Match the run's task text (brief + notes + resume task + creative plan)
    /// against the global skill store's triggers, log the decision, and — when
    /// configured — export the skill into the repo for CLI discovery.
    /// </summary>
    private IReadOnlyList<SkillMatch> SelectSkills(Project project, string brief, string? creativePlan, Action<string> log)
    {
        if (!_opt.Skills.Enabled || !_opt.Skills.AutoSelect)
            return Array.Empty<SkillMatch>();

        var text = string.Join('\n', new[] { brief, project.Notes, project.CurrentTask, creativePlan }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var matches = _skills.Match(text);
        if (matches.Count == 0)
            return matches;

        foreach (var m in matches)
        {
            log($"Skill selected: '{m.Skill.Id}' (matched: {string.Join(", ", m.MatchedKeywords)})");
            _log.LogInformation("Project {Name}: selected skill '{Skill}' (matched: {Keywords})",
                project.Name, m.Skill.Id, string.Join(", ", m.MatchedKeywords));
            _skills.RecordSelection(new SkillSelectionLog(
                m.Skill.Id, m.Skill.Name, m.MatchedKeywords, project.Name, project.CurrentTask, DateTime.UtcNow));

            if (_opt.Skills.ExportToProject)
            {
                var dest = _skillExporter.Export(m.Skill, project.RepoPath);
                if (dest is not null) log($"Exported skill '{m.Skill.Id}' to {dest} (git-excluded).");
            }
        }
        return matches;
    }

    private async Task<string> LoadBriefAsync(Project project, CancellationToken ct)
    {
        // Primary source: the latest brief version in the DB.
        var dbBrief = await ProjectBriefService.GetLatestContentAsync(_db, project.Id, ct);
        if (!string.IsNullOrWhiteSpace(dbBrief)) return dbBrief;

        // One-time seed: import the legacy on-disk brief (BriefPath) as version 1 so
        // existing projects keep working and become editable/versioned from now on.
        var path = Path.IsPathRooted(project.BriefPath)
            ? project.BriefPath
            : Path.Combine(project.RepoPath, project.BriefPath);
        var fileBrief = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : string.Empty;
        if (!string.IsNullOrWhiteSpace(fileBrief))
        {
            await ProjectBriefService.AddVersionIfChangedAsync(
                _db, project.Id, fileBrief, BriefAuthor.Seed, $"seeded from {project.BriefPath}", ct);
            await _db.SaveChangesAsync(ct);
        }
        return fileBrief;
    }

    /// <summary>
    /// When the project allows it, import an AI-proposed brief revision left at
    /// .ai-runner/brief-proposal.md into the DB as a NEW version (history preserved),
    /// then remove the proposal file so it is not committed as a project artifact.
    /// </summary>
    private async Task MaybeEvolveBriefAsync(Project project, RunRecord run, Action<string> log, CancellationToken ct)
    {
        if (!project.AllowAiEditBrief) return;

        var path = Path.Combine(project.RepoPath, ".ai-runner", "brief-proposal.md");
        if (!File.Exists(path)) return;

        string content;
        try { content = await File.ReadAllTextAsync(path, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not read AI brief proposal."); return; }

        try { File.Delete(path); } catch { /* best effort — keep going */ }

        var version = await ProjectBriefService.AddVersionIfChangedAsync(
            _db, project.Id, content, BriefAuthor.Ai, $"AI-evolved during run #{run.Id}", ct);
        if (version is null) { log("AI brief proposal was empty or unchanged — ignored."); return; }

        await _db.SaveChangesAsync(ct);
        log($"AI evolved the brief → version {version.Version} (stored in DB; history preserved).");
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

    /// <summary>
    /// Decide whether the current <see cref="_riskAssessment"/> blocks autonomous
    /// execution. File deletions and writes outside the repo are ALWAYS blocked
    /// (config-gated hard guards), independent of AllowRiskyAutonomousRuns. Other
    /// risky runs are blocked only when AllowRiskyAutonomousRuns is false.
    /// </summary>
    private (bool Blocked, bool Hard, string Why) EvaluateRiskGate()
    {
        var hard = new List<string>();
        if (_opt.Risk.BlockFileDeletions) hard.AddRange(_riskAssessment.Deletions);
        if (_opt.Risk.BlockOutOfProjectChanges) hard.AddRange(_riskAssessment.OutOfProject);

        if (hard.Count > 0)
            return (true, true,
                "Always-blocked action (not permitted even with AllowRiskyAutonomousRuns): "
                + string.Join("; ", hard.Distinct(StringComparer.OrdinalIgnoreCase)));

        if (_riskAssessment.Level == RiskLevel.Risky && !_opt.Risk.AllowRiskyAutonomousRuns)
            return (true, false,
                "Risky task blocked (requires manual approval): "
                + string.Join("; ", _riskAssessment.Reasons)
                + ". Set AutoDev:Risk:AllowRiskyAutonomousRuns=true to allow.");

        return (false, false, string.Empty);
    }

    private async Task PauseAsync(Project project, RunRecord run, RunStatus status,
        ProviderInvocation inv, StringBuilder logBuffer, CancellationToken ct)
    {
        run.Reason = inv.Reason ?? status.ToString();
        // Capture any partial progress (changed files, artifacts, risk).
        var changes = await _git.GetChangesAsync(project.RepoPath, ct);
        var changed = changes.Select(c => c.Path).ToList();
        run.ChangedFiles = string.Join('\n', changed);
        _trackedArtifacts = _artifacts.Track(changed, project.RepoPath);
        _riskAssessment = _risk.Assess(changes, project.CurrentTask);
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
            if (sha is not null) _stage = LifecycleStage.Committed;

            if (sha is not null && project.AutoPush && run.Branch is not null)
            {
                var pushed = await _git.PushAsync(project.RepoPath, run.Branch, ct);
                logBuffer.AppendLine(pushed ? "Pushed to origin." : "Push failed.");
            }
        }

        // Learned = memory updated this run.
        if (_memoryUpdates.Count > 0 && _stage < LifecycleStage.Learned)
            _stage = LifecycleStage.Learned;

        // Persist run log + markdown summary to disk, then the machine-readable sidecar.
        run.LogPath = await WriteRunFilesAsync(project, run, summary, logBuffer.ToString(), ct);
        if (_stage != LifecycleStage.Failed)
            _stage = (LifecycleStage)Math.Max((int)_stage, (int)LifecycleStage.Reported);
        if (status is RunStatus.Failed) _stage = LifecycleStage.Failed;
        await WriteRunMetadataAsync(project, run, ct);

        // Learning loop: per-run retrospective (what worked/failed, what to try/avoid).
        if (_opt.Learning.Enabled && !string.IsNullOrWhiteSpace(run.LogPath))
        {
            var changed = string.IsNullOrWhiteSpace(run.ChangedFiles)
                ? Array.Empty<string>()
                : run.ChangedFiles!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var retroPath = await _retro.WriteAsync(run.LogPath!, run, summary, _riskAssessment, changed, _lessons, ct);
            if (retroPath is not null)
                logBuffer.AppendLine("Wrote retrospective: " + Path.GetFileName(retroPath));
        }

        // Persist the final lifecycle stage + risk on the run (queryable history).
        run.Stage = _stage.ToString();
        run.Risk = _riskAssessment.Level.ToString();

        // Update denormalized project snapshot.
        project.LastRunStatus = status;
        project.LastProvider = run.Provider;
        project.LastRunAt = run.FinishedAt;
        project.LastError = status is RunStatus.Success ? null : run.Reason;

        await _db.SaveChangesAsync(ct);

        // Email report.
        var report = _email.BuildReport(project, run, summary, MeasurementSummary());
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
        if (_selectedSkills.Count > 0)
        {
            md.AppendLine("## Skills selected");
            foreach (var m in _selectedSkills)
                md.AppendLine($"- **{m.Skill.Id}** — matched: {string.Join(", ", m.MatchedKeywords)}");
            md.AppendLine();
            md.AppendLine("(Output path, generated files, validation result and next suggested animations "
                          + "are reported by the agent in the summary above under SKILL/ASSET_PATH/GENERATED_FILES/VALIDATION/NEXT_ANIMATIONS.)");
            md.AppendLine();
        }
        md.AppendLine("## Lifecycle & risk");
        md.AppendLine($"- Lifecycle stage reached: **{_stage}**");
        if (!string.IsNullOrWhiteSpace(_taskSource))
            md.AppendLine($"- Task source: {_taskSource}");
        md.AppendLine($"- Cost summary: {MeasurementSummary()}");
        md.AppendLine($"- Risk level: **{_riskAssessment.Level}**"
                      + (_riskAssessment.Reasons.Count > 0 ? $" — {string.Join("; ", _riskAssessment.Reasons)}" : ""));
        if (_memoryUpdates.Count > 0)
            md.AppendLine($"- Memory updated: {string.Join(", ", _memoryUpdates)}");
        if (_nextSuggestedTasks.Count > 0)
        {
            md.AppendLine("- Next suggested tasks:");
            foreach (var t in _nextSuggestedTasks) md.AppendLine($"    - {t}");
        }
        md.AppendLine();
        if (_trackedArtifacts.Count > 0)
        {
            md.AppendLine("## Generated artifacts");
            foreach (var a in _trackedArtifacts)
                md.AppendLine($"- `{a.Path}` — {a.Kind}{(a.SkillId is null ? "" : $" (skill: {a.SkillId})")}");
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

    /// <summary>Write the machine-readable run sidecar next to the markdown log.</summary>
    private async Task WriteRunMetadataAsync(Project project, RunRecord run, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(run.LogPath)) return;
        var meta = new RunMetadata
        {
            RunId = run.Id,
            ProjectId = project.Id,
            ProjectName = project.Name,
            Provider = run.Provider.ToString(),
            Status = run.Status.ToString(),
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Branch = run.Branch,
            CommitSha = run.CommitSha,
            Task = run.TaskTitle ?? project.CurrentTask,
            PromptChars = _promptChars,
            PromptEstTokens = _promptEstTokens,
            InputTokens = _inputTokens,
            OutputTokens = _outputTokens,
            CostUsd = _costUsd,
            Model = _model,
            Reason = run.Reason,
            Stage = _stage.ToString(),
            Risk = _riskAssessment.Level.ToString(),
            RiskReasons = _riskAssessment.Reasons,
            Skills = _selectedSkills.Select(s => new RunSkillRef(s.Skill.Id, s.MatchedKeywords)).ToList(),
            Artifacts = _trackedArtifacts,
            ValidationRun = run.ValidationRun,
            ValidationPassed = run.ValidationPassed,
            MemoryUpdates = _memoryUpdates,
            NextSuggestedTasks = _nextSuggestedTasks,
            TaskSource = _taskSource
        };
        try { await _runMeta.WriteAsync(run.LogPath!, meta, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to write run sidecar for run #{Run}.", run.Id); }
    }

    private static string BuildCommitMessage(Project project, ParsedSummary? summary)
    {
        var task = summary?.Task ?? project.CurrentTask ?? "autonomous changes";
        var first = task.Split('\n')[0];
        if (first.Length > 72) first = first[..72];
        return $"autodev: {first}\n\nAutomated change by AutoDev runner.";
    }

    /// <summary>Split a free-text "next tasks" field into individual task lines.</summary>
    private static List<string> SplitTasks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.TrimStart('-', '*', '•', ' ').Trim())
            .Where(l => l.Length > 0 && !l.Equals("none", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();
    }

    private static (string file, string args) SplitCommand(string command)
    {
        command = command.Trim();
        var idx = command.IndexOf(' ');
        return idx < 0 ? (command, string.Empty) : (command[..idx], command[(idx + 1)..]);
    }
}
