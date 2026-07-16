using AutoDevRunner.Config;
using AutoDevRunner.Data;
using AutoDevRunner.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// Pure decision logic for the run watchdog, separated for testability.
/// </summary>
public static class RunWatchdogPolicy
{
    public const string ReclaimedReason = "orphaned: runner process died mid-run (watchdog)";

    /// <summary>
    /// A run should be reclaimed (paused as orphaned) when no live process
    /// holds its project lock AND it is old enough that the missing lock
    /// cannot be a startup race. HeldByLiveOwner always means "leave it alone".
    /// </summary>
    public static bool ShouldReclaim(
        RunLockLiveness liveness, DateTime startedAtUtc, DateTime nowUtc, TimeSpan grace)
    {
        if (liveness == RunLockLiveness.HeldByLiveOwner) return false;
        return nowUtc - startedAtUtc >= grace;
    }
}

/// <summary>
/// Background reconciler for run status. Runs are finalized by the process
/// that executes them (web host or a Task Scheduler one-shot); when that
/// process dies mid-run (PC sleep/shutdown/restart) the RunRecord stays
/// "Running" in the DB and, before this service existed, was only cleaned up
/// by the next process start — hours later, showing as a stuck run in the
/// dashboard. This watchdog runs inside the long-lived web host and closes
/// the gap: every tick it looks for unfinished runs whose run.lock is gone,
/// expired, or owned by a dead process, and pauses them (resumable) at once.
/// </summary>
public class RunWatchdogService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RunLock _lock;
    private readonly WatchdogOptions _opt;
    private readonly ILogger<RunWatchdogService> _log;

    public RunWatchdogService(IServiceScopeFactory scopes, RunLock runLock,
        IOptions<AutoDevOptions> opt, ILogger<RunWatchdogService> log)
    {
        _scopes = scopes;
        _lock = runLock;
        _opt = opt.Value.Watchdog;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(10, _opt.IntervalSeconds));
        _log.LogInformation(
            "Run watchdog started: reconciling unfinished runs every {Interval}s (grace {Grace}m).",
            (int)interval.TotalSeconds, _opt.GraceMinutes);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await SweepAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The watchdog must outlive transient DB/file errors.
                    _log.LogError(ex, "Run watchdog sweep failed; retrying next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>One reconciliation pass. Returns how many runs were reclaimed.</summary>
    internal async Task<int> SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unfinished = await db.Runs
            .Where(r => (r.Status == RunStatus.Running || r.Status == RunStatus.Pending)
                        && r.FinishedAt == null)
            .Select(r => new { r.Id, r.ProjectId, r.StartedAt })
            .ToListAsync(ct);
        if (unfinished.Count == 0) return 0;

        var projectIds = unfinished.Select(r => r.ProjectId).Distinct().ToList();
        var projects = await db.Projects
            .Where(p => projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var now = DateTime.UtcNow;
        var grace = TimeSpan.FromMinutes(Math.Max(1, _opt.GraceMinutes));
        var reclaimed = 0;

        foreach (var run in unfinished)
        {
            if (!projects.TryGetValue(run.ProjectId, out var project)) continue;

            var liveness = _lock.CheckLiveness(project);
            if (!RunWatchdogPolicy.ShouldReclaim(liveness, run.StartedAt, now, grace)) continue;

            if (liveness == RunLockLiveness.Stale)
                _lock.TryRecoverDeadOwner(project, out _);

            // Conditional set-based updates: if the owning process finalized the
            // run between our read and this write, the WHERE clause matches
            // nothing and the real outcome is preserved.
            var changed = await db.Runs
                .Where(r => r.Id == run.Id && r.FinishedAt == null
                            && (r.Status == RunStatus.Running || r.Status == RunStatus.Pending))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RunStatus.Paused)
                    .SetProperty(r => r.FinishedAt, now)
                    .SetProperty(r => r.Reason, RunWatchdogPolicy.ReclaimedReason)
                    .SetProperty(r => r.Stage, r =>
                        r.Stage == null || r.Stage == "" || r.Stage == nameof(LifecycleStage.Planned)
                            || r.Stage == nameof(LifecycleStage.Running)
                            ? nameof(LifecycleStage.Reported)
                            : r.Stage), ct);
            if (changed == 0) continue;

            await db.Projects
                .Where(p => p.Id == run.ProjectId
                            && (p.LastRunStatus == RunStatus.Running
                                || p.LastRunStatus == RunStatus.Pending))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.LastRunStatus, RunStatus.Paused)
                    .SetProperty(p => p.LastRunAt, now)
                    .SetProperty(p => p.LastError, p =>
                        p.LastError == null || p.LastError == ""
                            ? RunWatchdogPolicy.ReclaimedReason
                            : p.LastError), ct);

            reclaimed++;
            _log.LogWarning(
                "Watchdog: run #{RunId} (project {ProjectName}) has no live owner process " +
                "({Liveness}); paused as resumable ({Reason}).",
                run.Id, project.Name, liveness, RunWatchdogPolicy.ReclaimedReason);
        }

        return reclaimed;
    }
}
