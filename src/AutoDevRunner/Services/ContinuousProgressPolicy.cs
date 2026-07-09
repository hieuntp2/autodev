namespace AutoDevRunner.Services;

public sealed record ContinuousRunOutcome(bool HasChangedFiles, bool ValidationFailed, string? TaskTitle)
{
    public bool NoProgress => !HasChangedFiles || ValidationFailed;
}

public static class ContinuousProgressPolicy
{
    public static bool ShouldStop(IReadOnlyList<ContinuousRunOutcome> recentOutcomes, int stopAfterNoProgressRuns)
    {
        var threshold = Math.Max(1, stopAfterNoProgressRuns);
        if (recentOutcomes.Count < threshold) return false;

        return recentOutcomes
            .TakeLast(threshold)
            .All(o => o.NoProgress);
    }
}
