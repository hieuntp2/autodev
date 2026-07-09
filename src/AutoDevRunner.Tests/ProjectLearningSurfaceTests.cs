using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ProjectLearningSurfaceTests
{
    [Fact]
    public void Builds_learning_snapshot_with_repeated_failures_and_recent_setting_changes()
    {
        var state = new ProjectLearningState
        {
            ProjectId = 8,
            TotalRuns = 5,
            Successes = 3,
            Failures = 2,
            NotVerified = 1,
            RollingSuccessRate = 0.6
        };
        var tasks = new[]
        {
            new ProjectTaskStat
            {
                TaskTitle = "Add hop animation",
                TaskKeyNormalized = "add hop animation",
                Attempts = 3,
                Failures = 2,
                LastOutcome = "Failed",
                LastAttemptAt = new DateTime(2026, 7, 9, 11, 0, 0, DateTimeKind.Utc)
            },
            new ProjectTaskStat
            {
                TaskTitle = "Add blink",
                TaskKeyNormalized = "add blink",
                Attempts = 2,
                Failures = 1,
                LastOutcome = "Success",
                LastAttemptAt = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc)
            }
        };
        var changes = new[]
        {
            new ProjectSettingChange { Key = "ValidationCommand", OldValue = "dotnet build", NewValue = "dotnet test", Source = "Ai", CreatedAt = DateTime.UtcNow }
        };

        var snapshot = ProjectLearningSurface.Build(state, tasks, changes, failureThreshold: 2);

        Assert.Equal(5, snapshot.TotalRuns);
        Assert.Equal(0.6, snapshot.RollingSuccessRate);
        var failing = Assert.Single(snapshot.RepeatedFailingTasks);
        Assert.Equal("Add hop animation", failing.TaskTitle);
        Assert.Equal(2, failing.Failures);
        Assert.Single(snapshot.SettingChanges);
    }
}
