using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunStartupCleanupTests
{
    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Pending)]
    public void Orphaned_running_or_pending_runs_are_marked_failed(RunStatus status)
    {
        var finishedAt = new DateTime(2026, 7, 9, 8, 0, 0, DateTimeKind.Utc);
        var run = new RunRecord { Status = status };

        var cleaned = RunStartupCleanup.MarkOrphanedRuns(new[] { run }, finishedAt);

        Assert.Equal(1, cleaned);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(finishedAt, run.FinishedAt);
        Assert.Equal("orphaned: runner restarted mid-run", run.Reason);
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
}
