namespace AutoDevRunner.Services;

/// <summary>
/// Fire-and-forget launcher for manual run triggers from the API. Creates its
/// own DI scope so the run outlives the HTTP request. Singleton.
/// </summary>
public class RunLauncher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RunLock _lock;
    private readonly ILogger<RunLauncher> _log;

    public RunLauncher(IServiceScopeFactory scopes, RunLock runLock, ILogger<RunLauncher> log)
    {
        _scopes = scopes;
        _lock = runLock;
        _log = log;
    }

    /// <summary>Returns false if the project is already running.</summary>
    public bool TryLaunch(int projectId)
    {
        if (_lock.IsRunning(projectId)) return false;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<RunOrchestrator>();
                await orchestrator.RunProjectAsync(projectId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Manual run for project {Id} failed.", projectId);
            }
        });

        return true;
    }
}
