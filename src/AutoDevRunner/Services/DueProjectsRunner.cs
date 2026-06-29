using AutoDevRunner.Data;
using Microsoft.EntityFrameworkCore;

namespace AutoDevRunner.Services;

/// <summary>
/// Runs every enabled, non-paused project once, in priority order, sequentially.
/// Shared by the in-process scheduler and the one-shot `--run-due` CLI mode
/// (the latter is what Windows Task Scheduler invokes). Singleton.
/// </summary>
public class DueProjectsRunner
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DueProjectsRunner> _log;

    public DueProjectsRunner(IServiceScopeFactory scopes, ILogger<DueProjectsRunner> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public async Task RunAllDueAsync(CancellationToken ct = default)
    {
        List<int> projectIds;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            projectIds = await db.Projects
                .Where(p => p.Enabled && !p.Paused)
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.Id)
                .Select(p => p.Id)
                .ToListAsync(ct);
        }

        _log.LogInformation("Running {Count} due project(s).", projectIds.Count);

        foreach (var id in projectIds)
        {
            if (ct.IsCancellationRequested) break;
            using var scope = _scopes.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<RunOrchestrator>();
            await orchestrator.RunProjectAsync(id, ct);
        }
    }
}
