using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunFinalizationPolicyTests
{
    [Fact]
    public async Task Terminal_state_is_persisted_before_optional_work_and_optional_failure_is_swallowed()
    {
        var order = new List<string>();
        Exception? observed = null;

        var completed = await RunFinalizationPolicy.PersistThenTryOptionalAsync(
            persistTerminal: () => { order.Add("persist"); return Task.CompletedTask; },
            optionalWork: () => { order.Add("learning"); throw new InvalidOperationException("learning failed"); },
            onOptionalError: ex => observed = ex);

        Assert.False(completed);
        Assert.Equal(new[] { "persist", "learning" }, order);
        Assert.Equal("learning failed", observed?.Message);
    }

    [Fact]
    public void Apply_terminal_state_updates_run_and_project_snapshot()
    {
        var run = new RunRecord { Status = RunStatus.Running };
        var project = new Project { LastRunStatus = RunStatus.Running, LastError = "old" };
        var finishedAt = new DateTime(2026, 7, 11, 9, 0, 0, DateTimeKind.Utc);

        RunFinalizationPolicy.ApplyTerminalState(
            project, run, RunStatus.Success, finishedAt,
            LifecycleStage.Reported.ToString(), RiskLevel.Normal.ToString());

        Assert.Equal(RunStatus.Success, run.Status);
        Assert.Equal(finishedAt, run.FinishedAt);
        Assert.Equal("Reported", run.Stage);
        Assert.Equal("Normal", run.Risk);
        Assert.Equal(RunStatus.Success, project.LastRunStatus);
        Assert.Equal(finishedAt, project.LastRunAt);
        Assert.Null(project.LastError);
    }

    [Fact]
    public void Resume_task_policy_preserves_current_task_when_summary_has_no_structured_next_task()
    {
        var summary = new SummaryParser().Parse("TASK: diff output without marker");

        Assert.Equal("keep current", ResumeTaskPolicy.Resolve("keep current", summary));
        Assert.Null(ResumeTaskPolicy.Resolve("old", summary with { NextTask = "none" }));
    }
}
