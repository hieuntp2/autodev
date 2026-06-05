using AutoDev.Core.Models;
using AutoDev.Core.Services;
using TaskStatus = AutoDev.Core.Models.TaskStatus;

namespace AutoDev.Tests;

public sealed class TaskSelectorTests
{
    [Fact]
    public void Selects_first_pending_task_from_backlog()
    {
        var items = new[]
        {
            new TaskItem { Id = "TASK-001", Title = "First task", Status = TaskStatus.Pending },
            new TaskItem { Id = "TASK-002", Title = "Second task", Status = TaskStatus.Pending }
        };
        var status = new StatusSummary();

        var result = new TaskSelector().Select(items, status);

        Assert.False(result.IsBlocked);
        Assert.Equal("TASK-001", result.SelectedTask!.Id);
    }

    [Fact]
    public void Prefers_status_recommended_task_over_first_pending()
    {
        var items = new[]
        {
            new TaskItem { Id = "TASK-001", Title = "First task", Status = TaskStatus.Pending },
            new TaskItem { Id = "TASK-002", Title = "Second task", Status = TaskStatus.Pending }
        };
        var status = new StatusSummary { NextRecommendedTaskId = "TASK-002" };

        var result = new TaskSelector().Select(items, status);

        Assert.False(result.IsBlocked);
        Assert.Equal("TASK-002", result.SelectedTask!.Id);
    }

    [Fact]
    public void Skips_done_tasks()
    {
        var items = new[]
        {
            new TaskItem { Id = "TASK-001", Title = "Done task", Status = TaskStatus.Done },
            new TaskItem { Id = "TASK-002", Title = "Pending task", Status = TaskStatus.Pending }
        };
        var status = new StatusSummary();

        var result = new TaskSelector().Select(items, status);

        Assert.Equal("TASK-002", result.SelectedTask!.Id);
    }

    [Fact]
    public void Returns_blocked_when_no_candidates()
    {
        var items = new[]
        {
            new TaskItem { Id = "TASK-001", Title = "Done", Status = TaskStatus.Done }
        };
        var status = new StatusSummary();

        var result = new TaskSelector().Select(items, status);

        Assert.True(result.IsBlocked);
        Assert.NotNull(result.BlockedReason);
    }

    [Fact]
    public void Returns_blocked_when_backlog_is_empty()
    {
        var result = new TaskSelector().Select([], new StatusSummary());

        Assert.True(result.IsBlocked);
        Assert.Contains("empty", result.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocks_task_with_secret_keyword_in_title()
    {
        var items = new[]
        {
            new TaskItem { Id = "TASK-001", Title = "Configure secret API credentials", Status = TaskStatus.Pending }
        };
        var status = new StatusSummary();

        var result = new TaskSelector().Select(items, status);

        Assert.True(result.IsBlocked);
        Assert.Contains("secret", result.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void In_progress_task_is_preferred_over_pending()
    {
        var items = new[]
        {
            new TaskItem { Id = "TASK-001", Title = "Pending task", Status = TaskStatus.Pending },
            new TaskItem { Id = "TASK-002", Title = "In progress task", Status = TaskStatus.InProgress }
        };
        var status = new StatusSummary();

        var result = new TaskSelector().Select(items, status);

        Assert.Equal("TASK-002", result.SelectedTask!.Id);
    }
}
