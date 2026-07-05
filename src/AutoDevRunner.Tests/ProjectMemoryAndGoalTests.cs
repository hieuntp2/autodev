using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoDevRunner.Tests;

public class ProjectMemoryAndGoalTests : IDisposable
{
    private readonly string _repo;
    private readonly ProjectMemoryWriter _memory = new(NullLogger<ProjectMemoryWriter>.Instance);
    private readonly ProjectGoalService _goals = new();

    public ProjectMemoryAndGoalTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "adtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { /* best effort */ }
    }

    private static ParsedSummary Summary(string? ideas = null, string? next = null,
        string? done = null, string? task = null)
        => new(Task: task, Done: done, Pending: null, Ideas: ideas, Files: null, NextTask: next, FullText: "");

    [Fact]
    public async Task Writes_ideas_backlog_decisions()
    {
        var updated = await _memory.UpdateAsync(_repo,
            Summary(ideas: "Add a sleep animation", next: "Build the runtime player", done: "Added blink"),
            "2026-07-05", autoWrite: true);

        Assert.Contains(ProjectGoalService.IdeasFile, updated);
        Assert.Contains(ProjectGoalService.BacklogFile, updated);
        Assert.Contains(ProjectGoalService.DecisionsFile, updated);

        var dir = _goals.GoalDir(_repo);
        Assert.Contains("Add a sleep animation", await File.ReadAllTextAsync(Path.Combine(dir, ProjectGoalService.IdeasFile)));
        Assert.Contains("Build the runtime player", await File.ReadAllTextAsync(Path.Combine(dir, ProjectGoalService.BacklogFile)));
        Assert.Contains("Added blink", await File.ReadAllTextAsync(Path.Combine(dir, ProjectGoalService.DecisionsFile)));
    }

    [Fact]
    public async Task Does_not_duplicate_the_same_idea()
    {
        await _memory.UpdateAsync(_repo, Summary(ideas: "Add a sleep animation"), "2026-07-05", autoWrite: true);
        var second = await _memory.UpdateAsync(_repo, Summary(ideas: "add a SLEEP animation"), "2026-07-06", autoWrite: true);

        // Second (case/space-insensitive duplicate) should not touch IDEAS.md again.
        Assert.DoesNotContain(ProjectGoalService.IdeasFile, second);

        var ideas = await File.ReadAllTextAsync(Path.Combine(_goals.GoalDir(_repo), ProjectGoalService.IdeasFile));
        var count = System.Text.RegularExpressions.Regex.Matches(
            ideas, "sleep animation", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Empty_or_none_summary_writes_nothing()
    {
        var updated = await _memory.UpdateAsync(_repo, Summary(ideas: "none", next: null, done: null), "2026-07-05", autoWrite: true);
        Assert.Empty(updated);
    }

    [Fact]
    public async Task AutoWrite_disabled_writes_nothing()
    {
        var updated = await _memory.UpdateAsync(_repo,
            Summary(ideas: "Add a sleep animation", done: "did stuff"), "2026-07-05", autoWrite: false);
        Assert.Empty(updated);
        Assert.False(File.Exists(Path.Combine(_goals.GoalDir(_repo), ProjectGoalService.IdeasFile)));
    }

    [Fact]
    public async Task Goal_service_reads_files_and_presence()
    {
        var dir = _goals.GoalDir(_repo);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, ProjectGoalService.GoalFile), "# Goal\nMake a pixel pet.");

        var goal = await _goals.LoadAsync(_repo);
        Assert.True(goal.HasGoal);
        Assert.Contains("pixel pet", goal.Goal);

        var presence = _goals.Presence(_repo);
        Assert.True(presence[ProjectGoalService.GoalFile]);
        Assert.False(presence[ProjectGoalService.RoadmapFile]);
    }
}
