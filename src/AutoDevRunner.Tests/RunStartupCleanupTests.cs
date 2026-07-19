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
        Assert.Equal(RunStartupCleanup.OrphanedReason, run.Reason);
        Assert.Equal(nameof(LifecycleStage.Reported), run.Stage);
    }

    [Fact]
    public void Later_stage_runs_keep_their_stage()
    {
        var run = new RunRecord { Status = RunStatus.Running, Stage = nameof(LifecycleStage.Committed) };

        RunStartupCleanup.MarkOrphanedRuns(new[] { run }, DateTime.UtcNow);

        Assert.Equal(RunStatus.Paused, run.Status);
        Assert.Equal(nameof(LifecycleStage.Committed), run.Stage);
    }

    [Fact]
    public void Runs_owned_by_a_live_process_are_not_paused()
    {
        var now = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var run = new RunRecord
        {
            ProjectId = 3,
            Status = RunStatus.Running,
            StartedAt = now.AddHours(-1)
        };

        var cleaned = RunStartupCleanup.MarkOrphanedRuns(new[] { run }, now,
            _ => RunLockLiveness.HeldByLiveOwner, TimeSpan.FromMinutes(2));

        Assert.Equal(0, cleaned);
        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Null(run.FinishedAt);
    }

    [Fact]
    public void Runs_younger_than_the_grace_window_are_not_paused()
    {
        var now = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var run = new RunRecord
        {
            ProjectId = 3,
            Status = RunStatus.Pending,
            StartedAt = now.AddSeconds(-30)
        };

        var cleaned = RunStartupCleanup.MarkOrphanedRuns(new[] { run }, now,
            _ => RunLockLiveness.NotHeld, TimeSpan.FromMinutes(2));

        Assert.Equal(0, cleaned);
        Assert.Equal(RunStatus.Pending, run.Status);
    }

    [Fact]
    public void Runs_with_a_dead_owner_past_grace_are_paused()
    {
        var now = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var run = new RunRecord
        {
            ProjectId = 3,
            Status = RunStatus.Running,
            StartedAt = now.AddMinutes(-10)
        };

        var cleaned = RunStartupCleanup.MarkOrphanedRuns(new[] { run }, now,
            _ => RunLockLiveness.Stale, TimeSpan.FromMinutes(2));

        Assert.Equal(1, cleaned);
        Assert.Equal(RunStatus.Paused, run.Status);
        Assert.Equal(RunStartupCleanup.OrphanedReason, run.Reason);
    }

    [Fact]
    public void Project_snapshot_with_a_live_owner_keeps_running_status()
    {
        var now = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var project = new Project
        {
            Id = 3,
            LastRunStatus = RunStatus.Running,
            LastRunAt = now.AddHours(-1)
        };

        var cleaned = RunStartupCleanup.ReconcileOrphanedProjects(new[] { project }, now,
            _ => RunLockLiveness.HeldByLiveOwner, TimeSpan.FromMinutes(2));

        Assert.Equal(0, cleaned);
        Assert.Equal(RunStatus.Running, project.LastRunStatus);
        Assert.Null(project.LastError);
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

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Pending)]
    public void Reconciles_project_snapshot_without_losing_resume_state(RunStatus status)
    {
        var at = new DateTime(2026, 7, 11, 5, 0, 0, DateTimeKind.Utc);
        var project = new Project
        {
            LastRunStatus = status,
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
    public void Settled_projects_and_existing_errors_are_left_alone()
    {
        var projects = new[]
        {
            new Project { LastRunStatus = RunStatus.Success },
            new Project { LastRunStatus = RunStatus.Running, LastError = "real error" }
        };

        var cleaned = RunStartupCleanup.ReconcileOrphanedProjects(projects, DateTime.UtcNow);

        Assert.Equal(1, cleaned);
        Assert.Equal(RunStatus.Success, projects[0].LastRunStatus);
        Assert.Equal(RunStatus.Paused, projects[1].LastRunStatus);
        Assert.Equal("real error", projects[1].LastError);
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

    [Fact]
    public void Latest_run_replaces_a_stale_project_snapshot_after_restart()
    {
        var finishedAt = new DateTime(2026, 7, 11, 5, 23, 37, DateTimeKind.Utc);
        var project = new Project
        {
            Id = 7,
            LastRunStatus = RunStatus.Paused,
            LastError = "Run exceeded the 60 minute limit.",
            LastProvider = ProviderKind.Claude
        };
        var latest = new RunRecord
        {
            Id = 123,
            ProjectId = 7,
            Provider = ProviderKind.Codex,
            Status = RunStatus.Paused,
            FinishedAt = finishedAt,
            Reason = RunStartupCleanup.OrphanedReason
        };

        var changed = RunStartupCleanup.ReconcileLatestRunSnapshots(
            new[] { project }, new[] { latest });

        Assert.Equal(1, changed);
        Assert.Equal(RunStatus.Paused, project.LastRunStatus);
        Assert.Equal(ProviderKind.Codex, project.LastProvider);
        Assert.Equal(finishedAt, project.LastRunAt);
        Assert.Equal(RunStartupCleanup.OrphanedReason, project.LastError);
    }
}
