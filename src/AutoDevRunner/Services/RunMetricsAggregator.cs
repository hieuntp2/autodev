using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public sealed record RunMetricsTrendPoint(
    int RunId,
    DateTime StartedAt,
    string Status,
    decimal? CostUsd,
    int? InputTokens,
    int? OutputTokens,
    string? Tier,
    bool Resumed);

public sealed record RunMetricsSummary(
    int RunCount,
    decimal TotalCostUsd,
    int TotalInputTokens,
    int TotalOutputTokens,
    double SuccessRate,
    int EstimatedTokensSavedByResume,
    IReadOnlyList<RunMetricsTrendPoint> Trend);

public static class RunMetricsAggregator
{
    public static RunMetricsSummary Aggregate(IEnumerable<RunRecord> runs, int trendTake)
    {
        var all = runs.ToList();
        var runCount = all.Count;
        var successes = all.Count(r => r.Status is RunStatus.Success);
        var trend = all
            .OrderByDescending(r => r.StartedAt)
            .ThenByDescending(r => r.Id)
            .Take(Math.Max(1, trendTake))
            .OrderBy(r => r.StartedAt)
            .ThenBy(r => r.Id)
            .Select(r => new RunMetricsTrendPoint(
                r.Id,
                r.StartedAt,
                r.Status.ToString(),
                r.CostUsd,
                r.InputTokens,
                r.OutputTokens,
                r.Tier,
                r.Resumed ?? false))
            .ToList();

        return new RunMetricsSummary(
            RunCount: runCount,
            TotalCostUsd: all.Sum(r => r.CostUsd ?? 0m),
            TotalInputTokens: all.Sum(r => r.InputTokens ?? 0),
            TotalOutputTokens: all.Sum(r => r.OutputTokens ?? 0),
            SuccessRate: runCount == 0 ? 0 : (double)successes / runCount,
            EstimatedTokensSavedByResume: all.Sum(EstimatedResumeSavings),
            Trend: trend);
    }

    private static int EstimatedResumeSavings(RunRecord run)
    {
        if (run.Resumed is not true) return 0;
        var fullPromptEstimate = run.PromptEstTokens ?? 0;
        var providerInput = run.InputTokens ?? 0;
        return Math.Max(0, fullPromptEstimate - providerInput);
    }
}
