using AutoDevRunner.Config;
using AutoDevRunner.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// Fire-and-forget launcher for manual run triggers from the API. Creates its
/// own DI scope so the run outlives the HTTP request. Singleton.
///
/// When continuous mode is on, a manual trigger loops the project back-to-back
/// (plan → code, auto-commit per policy) until its providers reach the usage
/// ceiling — same "burn to ~95%" behaviour as the CLI --run-due path — instead
/// of running exactly once.
/// </summary>
public class RunLauncher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ContinuousRunner _continuous;
    private readonly RunLock _lock;
    private readonly bool _burnTokensEnabled;
    private readonly ILogger<RunLauncher> _log;

    public RunLauncher(
        IServiceScopeFactory scopes, ContinuousRunner continuous, RunLock runLock,
        IOptions<AutoDevOptions> opt, ILogger<RunLauncher> log)
    {
        _scopes = scopes;
        _continuous = continuous;
        _lock = runLock;
        _burnTokensEnabled = opt.Value.BurnTokensEnabled;
        _log = log;
    }

    /// <summary>Returns false if the project is already running.</summary>
    public bool TryLaunch(int projectId)
    {
        RunLockLease? lease;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = db.Projects.AsNoTracking().FirstOrDefault(p => p.Id == projectId);
            if (project is null)
            {
                _log.LogWarning("Manual run requested for missing project {Id}.", projectId);
                return false;
            }

            if (!_lock.TryAcquire(project, out lease, out var reason))
            {
                _log.LogInformation("Manual run for project {Id} rejected: {Reason}", projectId, reason);
                return false;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (_burnTokensEnabled)
                {
                    // Loop the project until its providers hit the usage ceiling.
                    await _continuous.RunProjectAsync(projectId, lease!, CancellationToken.None);
                }
                else
                {
                    using var scope = _scopes.CreateScope();
                    var orchestrator = scope.ServiceProvider.GetRequiredService<RunOrchestrator>();
                    await orchestrator.RunProjectAsync(projectId, lease!, releaseLeaseOnCompletion: true,
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Manual run for project {Id} failed.", projectId);
                lease?.Dispose();
            }
        });

        return true;
    }
}
