using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ContinuousProgressPolicyTests
{
    [Fact]
    public void Stops_after_consecutive_no_progress_runs()
    {
        var outcomes = new[]
        {
            new ContinuousRunOutcome(HasChangedFiles: false, ValidationFailed: false, TaskTitle: "a"),
            new ContinuousRunOutcome(HasChangedFiles: true, ValidationFailed: true, TaskTitle: "b")
        };

        Assert.True(ContinuousProgressPolicy.ShouldStop(outcomes, stopAfterNoProgressRuns: 2));
    }

    [Fact]
    public void Progress_resets_no_progress_count()
    {
        var outcomes = new[]
        {
            new ContinuousRunOutcome(HasChangedFiles: false, ValidationFailed: false, TaskTitle: "a"),
            new ContinuousRunOutcome(HasChangedFiles: true, ValidationFailed: false, TaskTitle: "b")
        };

        Assert.False(ContinuousProgressPolicy.ShouldStop(outcomes, stopAfterNoProgressRuns: 2));
    }
}
