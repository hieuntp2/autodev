using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunStartupCleanupTests
{
    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Pending)]
    public void Orphaned_running_or_pending_runs_are_paused_and_reported(RunStatus status)
    {
        var finishedAt = new DateTime(2026, 7, 9, 8, 0, 0, DateTimeKind.Utc);
        var run = new RunRecord { Status = status, Stage = nameof(LifecycleStage.Running) };

        var cleaned = RunStartupCleanup.MarkOrphanedRuns(new[] { run }, finishedAt);

        Assert.Equal(1, cleaned);
        Assert.Equal(RunStatus.Paused, run.Status);
        Assert.Equal(finishedAt, run.FinishedAt);
        Assert.Equal("orphaned: runner restarted mid-run", run.Reason);
        Assert.Equal(nameof(LifecycleStage.Reported), run.Stage);
    }

    [Fact]
    public void Finished_or_terminal_runs_are_not_changed()
    {
        var existingFinishedAt = new DateTime(2026, 7, 8, 8, 0, 0, DateTimeKind.Utc);
        var runs = new[]
        {
            new RunRecord { Status = RunStatus.Running, FinishedAt = existingFinishedAt, Reason = "kept" },
            new RunRecord { Status = RunStatus.Success }
        };

        var cleaned = RunStartupCleanup.MarkOrphanedRuns(runs, DateTime.UtcNow);

        Assert.Equal(0, cleaned);
        Assert.Equal(RunStatus.Running, runs[0].Status);
        Assert.Equal(existingFinishedAt, runs[0].FinishedAt);
        Assert.Equal("kept", runs[0].Reason);
        Assert.Equal(RunStatus.Success, runs[1].Status);
    }

    [Fact]
    public void Reconciles_project_snapshot_without_losing_resume_state()
    {
        var at = new DateTime(2026, 7, 11, 5, 0, 0, DateTimeKind.Utc);
        var project = new Project
        {
            LastRunStatus = RunStatus.Running,
            CurrentTask = "continue this task",
            ProviderSessionId = "session-1"
        };

        var cleaned = RunStartupCleanup.ReconcileOrphanedProjects(new[] { project }, at);

        Assert.Equal(1, cleaned);
        Assert.Equal(RunStatus.Paused, project.LastRunStatus);
        Assert.Equal(at, project.LastRunAt);
        Assert.Equal(RunStartupCleanup.OrphanedReason, project.LastError);
        Assert.Equal("continue this task", project.CurrentTask);
        Assert.Equal("session-1", project.ProviderSessionId);
    }

    [Fact]
    public void Oversized_resume_task_is_reduced_to_its_first_bounded_line()
    {
        var project = new Project
        {
            CurrentTask = "Re-run Gradle validation and commit the slice.\n" + new string('x', 15_000)
        };

        var cleaned = RunStartupCleanup.SanitizeResumeTasks(new[] { project });

        Assert.Equal(1, cleaned);
        Assert.Equal("Re-run Gradle validation and commit the slice.", project.CurrentTask);
        Assert.True(project.CurrentTask!.Length <= 500);
    }
}
