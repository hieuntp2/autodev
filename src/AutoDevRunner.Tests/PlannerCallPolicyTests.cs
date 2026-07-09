using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class PlannerCallPolicyTests
{
    [Fact]
    public void Skips_when_task_in_progress_and_last_run_succeeded()
    {
        var shouldCall = PlannerCallPolicy.ShouldCallPlanner(
            new PlannerOptions { Enabled = true, SkipWhenTaskInProgress = true },
            new Project { CurrentTask = "Finish feature", LastRunStatus = RunStatus.Success });

        Assert.False(shouldCall);
    }

    [Fact]
    public void Calls_when_no_task_is_in_progress()
    {
        var shouldCall = PlannerCallPolicy.ShouldCallPlanner(
            new PlannerOptions { Enabled = true, SkipWhenTaskInProgress = true },
            new Project { CurrentTask = null, LastRunStatus = RunStatus.Success });

        Assert.True(shouldCall);
    }
}
