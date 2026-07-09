using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using AutoDevRunner.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunHistoryServiceTests : IDisposable
{
    private readonly string _repo;
    private readonly string _runsDir;
    private readonly RunMetadataStore _store = new();
    private readonly RunHistoryService _history;

    public RunHistoryServiceTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "adhist-" + Guid.NewGuid().ToString("N"));
        _runsDir = Path.Combine(_repo, ".ai-runner", "runs");
        Directory.CreateDirectory(_runsDir);
        _history = new RunHistoryService(_store);
    }

    public void Dispose() { try { Directory.Delete(_repo, true); } catch { } }

    private async Task WriteRun(int id, string task, string status, int dayOffset,
        string? reason = null, IEnumerable<string>? next = null)
    {
        var md = Path.Combine(_runsDir, $"2026070{dayOffset}-run{id}.md");
        await _store.WriteAsync(md, new RunMetadata
        {
            RunId = id,
            Task = task,
            Status = status,
            Reason = reason,
            StartedAt = new DateTime(2026, 7, dayOffset, 0, 0, 0, DateTimeKind.Utc),
            NextSuggestedTasks = next?.ToList() ?? new List<string>()
        });
    }

    [Fact]
    public void Empty_repo_yields_empty_lessons()
    {
        var lessons = _history.Analyze(_repo, window: 5, failureThreshold: 2);
        Assert.False(lessons.HasAny);
        Assert.Empty(lessons.RepeatedlyFailingTasks);
    }

    [Fact]
    public async Task Detects_a_task_that_failed_repeatedly()
    {
        await WriteRun(1, "Add hop animation", "Failed", 1, reason: "build error");
        await WriteRun(2, "Add hop animation", "Failed", 2, reason: "build error again");
        await WriteRun(3, "Add blink", "Success", 3);

        var lessons = _history.Analyze(_repo, window: 5, failureThreshold: 2);

        Assert.True(lessons.HasAny);
        Assert.Contains("Add hop animation", lessons.RepeatedlyFailingTasks);
        Assert.True(lessons.IsRepeatedlyFailing("add hop animation")); // case/space-insensitive
        Assert.DoesNotContain("Add blink", lessons.RepeatedlyFailingTasks);
    }

    [Fact]
    public async Task A_single_failure_is_not_repeatedly_failing()
    {
        await WriteRun(1, "Add hop animation", "Failed", 1);
        var lessons = _history.Analyze(_repo, window: 5, failureThreshold: 2);
        Assert.Empty(lessons.RepeatedlyFailingTasks);
    }

    [Fact]
    public async Task Collects_suggested_next_tasks_excluding_failing_ones()
    {
        await WriteRun(1, "Add hop animation", "Failed", 1, next: new[] { "Add hop animation", "Add sleep state" });
        await WriteRun(2, "Add hop animation", "Failed", 2, next: new[] { "Add sleep state", "Add idle blink" });

        var lessons = _history.Analyze(_repo, window: 5, failureThreshold: 2);

        Assert.Contains("Add sleep state", lessons.SuggestedNextTasks);
        Assert.Contains("Add idle blink", lessons.SuggestedNextTasks);
        // The repeatedly-failing task must not be re-suggested.
        Assert.DoesNotContain("Add hop animation", lessons.SuggestedNextTasks);
    }

    [Fact]
    public async Task Window_limits_how_many_runs_are_considered()
    {
        await WriteRun(1, "Old failing task", "Failed", 1);
        await WriteRun(2, "Old failing task", "Failed", 2);
        await WriteRun(3, "Recent ok", "Success", 3);
        await WriteRun(4, "Recent ok", "Success", 4);

        // Window of 2 sees only the newest two (both successes) → nothing failing.
        var lessons = _history.Analyze(_repo, window: 2, failureThreshold: 2);
        Assert.Empty(lessons.RepeatedlyFailingTasks);
    }
}

public class RetrospectiveWriterTests
{
    private static RunLessons NoLessons => RunLessons.Empty;
    private static RiskAssessment Normal => new(RiskLevel.Normal, new());

    [Fact]
    public void Success_run_reports_what_worked_and_nothing_failed()
    {
        var run = new RunRecord { Id = 7, Status = RunStatus.Success, TaskTitle = "Add blink", ValidationRun = true, ValidationPassed = true };
        var summary = new ParsedSummary(null, "created blink frames", null, null, null, null, "full",
            TaskTitle: "Add blink", NextSuggestedTasks: "add hop");

        var text = RetrospectiveWriter.BuildText(run, summary, Normal, new[] { "src/Pet.cs" }, NoLessons);

        Assert.Contains("# Retrospective — run 7", text);
        Assert.Contains("## What worked", text);
        Assert.Contains("created blink frames", text);
        Assert.Contains("Validation: PASSED", text);
        Assert.Contains("Nothing failed this run.", text);
        Assert.Contains("add hop", text);
        Assert.Contains("src/Pet.cs", text);
    }

    [Fact]
    public void Failed_run_records_reason_and_what_to_avoid()
    {
        var run = new RunRecord { Id = 8, Status = RunStatus.Failed, TaskTitle = "Add hop", Reason = "validation failed: build error" };
        var text = RetrospectiveWriter.BuildText(run, summary: null, Normal, Array.Empty<string>(), NoLessons);

        Assert.Contains("Status: **Failed**", text);
        Assert.Contains("validation failed: build error", text);
        Assert.Contains("What to avoid next time", text);
        Assert.Contains("Re-attempting \"Add hop\"", text);
        Assert.Contains("No files changed.", text);
    }

    [Fact]
    public void Repeatedly_failing_task_is_called_out_to_avoid()
    {
        var run = new RunRecord { Id = 9, Status = RunStatus.Failed, TaskTitle = "Add hop" };
        var lessons = new RunLessons(
            new[] { new RunLesson(1, DateTime.UtcNow, "Add hop", "Failed", true, null, false, false, Array.Empty<string>()) },
            new[] { "Add hop" }, Array.Empty<string>());

        var text = RetrospectiveWriter.BuildText(run, null, Normal, Array.Empty<string>(), lessons);
        Assert.Contains("has now failed repeatedly", text);
    }
}

public class TaskProposerLearningTests
{
    private static TaskProposer Make()
    {
        var skills = new SkillRegistry(Options.Create(new AutoDevOptions()), NullLogger<SkillRegistry>.Instance);
        return new TaskProposer(new RiskAssessor(), skills);
    }

    private static ProjectGoal Goal(string? backlog = null, string? ideas = null)
        => new("goal", null, backlog, ideas, null);

    [Fact]
    public void Skips_a_backlog_item_that_keeps_failing()
    {
        var lessons = new RunLessons(Array.Empty<RunLesson>(),
            new[] { "Add a happy hop animation" }, Array.Empty<string>());

        var p = Make().Propose(new Project { Name = "P" },
            Goal(backlog: "# Backlog\n- [ ] Add a happy hop animation\n- [ ] Add a wave animation"),
            lessons);

        Assert.Equal("backlog", p.Source);
        Assert.Contains("wave", p.Title);            // skipped the failing hop item
        Assert.DoesNotContain("hop", p.Title);
    }

    [Fact]
    public void Uses_a_learned_suggestion_when_backlog_and_ideas_are_empty()
    {
        var lessons = new RunLessons(Array.Empty<RunLesson>(),
            Array.Empty<string>(), new[] { "Add a sleep state" });

        var p = Make().Propose(new Project { Name = "P" }, Goal(), lessons);

        Assert.Equal("learned", p.Source);
        Assert.Contains("sleep state", p.Title);
    }

    [Fact]
    public void Falls_back_to_maintenance_when_the_only_learned_task_is_failing()
    {
        var lessons = new RunLessons(Array.Empty<RunLesson>(),
            new[] { "Add a sleep state" }, Array.Empty<string>()); // suggested list is empty

        var p = Make().Propose(new Project { Name = "P" }, Goal(), lessons);
        Assert.Equal("maintenance", p.Source);
    }
}

public class PromptBuilderLearningTests
{
    [Fact]
    public void Prompt_includes_recent_run_history_and_avoid_list()
    {
        var lessons = new RunLessons(
            new[]
            {
                new RunLesson(3, DateTime.UtcNow, "Add hop", "Failed", true, "build error", true, false, new[] { "add sleep" }),
                new RunLesson(2, DateTime.UtcNow, "Add blink", "Success", false, null, true, true, Array.Empty<string>()),
            },
            new[] { "Add hop" },
            new[] { "add sleep" });

        var prompt = new PromptBuilder().Build(
            new Project { Name = "P", RepoPath = "/r", CurrentTask = "do a thing" },
            brief: "b", run: new RunRecord(), creativePlan: null, skills: null,
            goal: new ProjectGoal("g", null, null, null, null),
            proposal: null, risk: RiskLevel.Normal, riskPolicy: null, lessons: lessons);

        Assert.Contains("## Recent run history (lessons)", prompt);
        Assert.Contains("run #3", prompt);
        Assert.Contains("build error", prompt);
        Assert.Contains("Do NOT retry these", prompt);
        Assert.Contains("Add hop", prompt);
        Assert.Contains("Follow-ups suggested by earlier runs", prompt);
    }

    [Fact]
    public void No_history_section_when_there_are_no_recent_runs()
    {
        var prompt = new PromptBuilder().Build(
            new Project { Name = "P", RepoPath = "/r", CurrentTask = "t" },
            brief: "b", run: new RunRecord(), creativePlan: null, skills: null,
            goal: new ProjectGoal("g", null, null, null, null),
            proposal: null, risk: RiskLevel.Normal, riskPolicy: null, lessons: RunLessons.Empty);

        Assert.DoesNotContain("## Recent run history", prompt);
    }
}
