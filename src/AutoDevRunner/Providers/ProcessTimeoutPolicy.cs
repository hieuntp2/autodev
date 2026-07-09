namespace AutoDevRunner.Providers;

public enum ProcessTimeoutKind
{
    Idle,
    HardBackstop
}

public enum ProcessWatchdogDecision
{
    KeepWaiting,
    KillForIdle
}

public static class ProcessTimeoutPolicy
{
    public static ProcessWatchdogDecision CheckIdle(
        DateTimeOffset lastOutputAt,
        DateTimeOffset now,
        TimeSpan idleTimeout) =>
        now - lastOutputAt >= idleTimeout
            ? ProcessWatchdogDecision.KillForIdle
            : ProcessWatchdogDecision.KeepWaiting;

    public static string BuildReason(ProcessTimeoutKind kind, TimeSpan limit)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(limit.TotalMinutes));
        return kind switch
        {
            ProcessTimeoutKind.Idle => $"hung: no output for {minutes} minutes",
            _ => $"hard time cap ({minutes} minutes) reached"
        };
    }
}
