using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunMetricsAggregatorTests
{
    [Fact]
    public void Aggregates_totals_success_rate_and_resume_savings()
    {
        var runs = new[]
        {
            new RunRecord
            {
                Id = 1,
                ProjectId = 7,
                Status = RunStatus.Success,
                StartedAt = new DateTime(2026, 7, 9, 10, 0, 0, DateTimeKind.Utc),
                PromptEstTokens = 1000,
                InputTokens = 600,
                OutputTokens = 200,
                CostUsd = 0.25m,
                Tier = "Light",
                Resumed = true
            },
            new RunRecord
            {
                Id = 2,
                ProjectId = 7,
                Status = RunStatus.Failed,
                StartedAt = new DateTime(2026, 7, 9, 11, 0, 0, DateTimeKind.Utc),
                PromptEstTokens = 500,
                InputTokens = 500,
                OutputTokens = 100,
                CostUsd = 0.10m,
                Tier = "Standard",
                Resumed = false
            }
        };

        var summary = RunMetricsAggregator.Aggregate(runs, trendTake: 10);

        Assert.Equal(2, summary.RunCount);
        Assert.Equal(0.35m, summary.TotalCostUsd);
        Assert.Equal(1100, summary.TotalInputTokens);
        Assert.Equal(300, summary.TotalOutputTokens);
        Assert.Equal(0.5, summary.SuccessRate);
        Assert.Equal(400, summary.EstimatedTokensSavedByResume);
    }

    [Fact]
    public void Trend_uses_the_latest_runs_in_chronological_order()
    {
        var runs = Enumerable.Range(1, 4)
            .Select(i => new RunRecord
            {
                Id = i,
                ProjectId = 3,
                Status = i % 2 == 0 ? RunStatus.Success : RunStatus.Failed,
                StartedAt = new DateTime(2026, 7, 9, i, 0, 0, DateTimeKind.Utc),
                InputTokens = i * 10,
                OutputTokens = i,
                CostUsd = i / 100m,
                Tier = i % 2 == 0 ? "Light" : "Standard"
            })
            .ToList();

        var summary = RunMetricsAggregator.Aggregate(runs, trendTake: 2);

        Assert.Equal(new[] { 3, 4 }, summary.Trend.Select(t => t.RunId).ToArray());
        Assert.Equal(new[] { "Failed", "Success" }, summary.Trend.Select(t => t.Status).ToArray());
        Assert.Equal(new[] { "Standard", "Light" }, summary.Trend.Select(t => t.Tier).ToArray());
    }
}
