using AutoDevRunner.Data;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AutoDevRunner.Tests;

public class ProjectLearningUpdaterTests
{
    [Fact]
    public void Success_run_updates_counts_rate_and_task_stat()
    {
        var state = new ProjectLearningState { ProjectId = 4 };
        var stats = new List<ProjectTaskStat>();
        var finished = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);
        var run = new RunRecord
        {
            ProjectId = 4,
            Status = RunStatus.Success,
            TaskTitle = "  Add   Hop Animation ",
            FinishedAt = finished,
            NotVerified = true
        };

        ProjectLearningUpdater.Apply(state, stats, run,
            rollingWindowStatuses: new[] { RunStatus.Success, RunStatus.Failed, RunStatus.Success },
            utcNow: finished);

        Assert.Equal(1, state.TotalRuns);
        Assert.Equal(1, state.Successes);
        Assert.Equal(0, state.Failures);
        Assert.Equal(1, state.NotVerified);
        Assert.Equal(finished, state.LastSuccessAt);
        Assert.Null(state.LastFailureAt);
        Assert.Equal(2d / 3d, state.RollingSuccessRate);

        var stat = Assert.Single(stats);
        Assert.Equal("add hop animation", stat.TaskKeyNormalized);
        Assert.Equal("Add   Hop Animation", stat.TaskTitle);
        Assert.Equal(1, stat.Attempts);
        Assert.Equal(0, stat.Failures);
        Assert.Equal("Success", stat.LastOutcome);
    }

    [Fact]
    public void Failure_run_upserts_existing_task_stat()
    {
        var state = new ProjectLearningState { ProjectId = 4 };
        var stats = new List<ProjectTaskStat>
        {
            new()
            {
                ProjectId = 4,
                TaskKeyNormalized = "add hop animation",
                TaskTitle = "Add hop animation",
                Attempts = 1,
                Failures = 1
            }
        };
        var finished = new DateTime(2026, 7, 9, 12, 30, 0, DateTimeKind.Utc);
        var run = new RunRecord
        {
            ProjectId = 4,
            Status = RunStatus.Failed,
            TaskTitle = "Add hop animation",
            FinishedAt = finished
        };

        ProjectLearningUpdater.Apply(state, stats, run,
            rollingWindowStatuses: new[] { RunStatus.Failed, RunStatus.Success },
            utcNow: finished);

        Assert.Equal(1, state.TotalRuns);
        Assert.Equal(0, state.Successes);
        Assert.Equal(1, state.Failures);
        Assert.Equal(finished, state.LastFailureAt);
        Assert.Equal(0.5, state.RollingSuccessRate);
        Assert.Single(stats);
        Assert.Equal(2, stats[0].Attempts);
        Assert.Equal(2, stats[0].Failures);
        Assert.Equal("Failed", stats[0].LastOutcome);
    }

    [Fact]
    public void Oversized_provider_task_is_bounded_before_it_reaches_the_database_index()
    {
        var state = new ProjectLearningState { ProjectId = 4 };
        var stats = new List<ProjectTaskStat>();
        var title = "Short task title\n" + new string('x', 15_000);
        var run = new RunRecord
        {
            ProjectId = 4,
            Status = RunStatus.Paused,
            TaskTitle = title,
            FinishedAt = DateTime.UtcNow
        };

        ProjectLearningUpdater.Apply(state, stats, run,
            rollingWindowStatuses: new[] { RunStatus.Paused },
            utcNow: DateTime.UtcNow);

        var stat = Assert.Single(stats);
        Assert.True(stat.TaskKeyNormalized.Length <= 256);
        Assert.True(stat.TaskTitle.Length <= 500);
        Assert.StartsWith("Short task title", stat.TaskTitle);
    }
}

public class RunHistoryDbLearningTests : IDisposable
{
    private readonly string _repo;
    private readonly RunHistoryService _history = new(new RunMetadataStore());

    public RunHistoryDbLearningTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "addblearn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }

    [Fact]
    public async Task Repeated_failure_detection_prefers_db_task_stats()
    {
        await using var db = Db();
        var project = new Project { Name = "P", RepoPath = _repo };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        db.ProjectTaskStats.Add(new ProjectTaskStat
        {
            ProjectId = project.Id,
            TaskKeyNormalized = "add hop animation",
            TaskTitle = "Add hop animation",
            Attempts = 3,
            Failures = 2,
            LastOutcome = "Failed",
            LastAttemptAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var lessons = await _history.AnalyzeAsync(db, project.Id, _repo, window: 5, failureThreshold: 2);

        Assert.Contains("Add hop animation", lessons.RepeatedlyFailingTasks);
    }

    private static AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("learning-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }
}

public class ProjectSettingsTunerTests
{
    [Fact]
    public void Applies_only_whitelisted_settings_and_records_changes()
    {
        var project = new Project
        {
            ValidationCommand = "dotnet build",
            ProviderPriority = "Codex,Claude",
            MaxRunMinutes = 30,
            AutoPush = false
        };
        var now = new DateTime(2026, 7, 9, 13, 0, 0, DateTimeKind.Utc);

        var changes = ProjectSettingsTuner.Apply(project,
            """{"ValidationCommand":"dotnet test","ProviderPriority":"Claude,Codex","MaxRunMinutes":45,"AutoPush":true}""",
            source: "Ai",
            utcNow: now);

        Assert.Equal("dotnet test", project.ValidationCommand);
        Assert.Equal("Claude,Codex", project.ProviderPriority);
        Assert.Equal(45, project.MaxRunMinutes);
        Assert.False(project.AutoPush);
        Assert.Equal(new[] { "ValidationCommand", "ProviderPriority", "MaxRunMinutes" },
            changes.Select(c => c.Key).ToArray());
        Assert.All(changes, c => Assert.Equal("Ai", c.Source));
    }

    [Fact]
    public void Ignores_invalid_values_and_unlisted_keys()
    {
        var project = new Project
        {
            ValidationCommand = "dotnet build",
            ProviderPriority = "Codex,Claude",
            MaxRunMinutes = 30
        };

        var changes = ProjectSettingsTuner.Apply(project,
            "MaxRunMinutes: not-a-number\nAllowRunOnMainBranch: true",
            source: "Ai",
            utcNow: DateTime.UtcNow);

        Assert.Empty(changes);
        Assert.Equal(30, project.MaxRunMinutes);
        Assert.False(project.AllowRunOnMainBranch);
    }
}
