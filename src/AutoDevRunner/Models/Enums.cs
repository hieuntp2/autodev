namespace AutoDevRunner.Models;

/// <summary>Outcome of a single run.</summary>
public enum RunStatus
{
    Pending = 0,
    Running = 1,
    Success = 2,
    Paused = 3,      // stopped gracefully, can resume (e.g. quota limit)
    Failed = 4,      // error during run
    QuotaLimit = 5,  // provider out of quota / rate limited
    AuthError = 6    // provider authentication failed
}

/// <summary>Which AI CLI was used.</summary>
public enum ProviderKind
{
    Codex = 0,
    Claude = 1
}
