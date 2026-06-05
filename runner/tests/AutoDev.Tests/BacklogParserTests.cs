using AutoDev.Core.Services;
using TaskStatus = AutoDev.Core.Models.TaskStatus;

namespace AutoDev.Tests;

public sealed class BacklogParserTests
{
    [Fact]
    public void Parses_checkbox_format()
    {
        var content = """
        - [ ] TASK-001: Add login screen
        - [x] TASK-002: Setup project structure
        - [ ] TASK-003: Add navigation
        """;

        var items = new BacklogParser().Parse(content);

        Assert.Equal(3, items.Count);
        Assert.Equal("TASK-001", items[0].Id);
        Assert.Equal("Add login screen", items[0].Title);
        Assert.Equal(TaskStatus.Pending, items[0].Status);

        Assert.Equal("TASK-002", items[1].Id);
        Assert.Equal(TaskStatus.Done, items[1].Status);

        Assert.Equal("TASK-003", items[2].Id);
        Assert.Equal(TaskStatus.Pending, items[2].Status);
    }

    [Fact]
    public void Parses_heading_format_with_status_lines()
    {
        var content = """
        ## TASK-001: Add login screen
        Status: pending

        ## TASK-002: Setup navigation
        Status: blocked
        Blocked: Waiting for UI design

        ## TASK-003: Write tests
        Status: done
        """;

        var items = new BacklogParser().Parse(content);

        Assert.Equal(3, items.Count);
        Assert.Equal("TASK-001", items[0].Id);
        Assert.Equal(TaskStatus.Pending, items[0].Status);

        Assert.Equal("TASK-002", items[1].Id);
        Assert.Equal(TaskStatus.Blocked, items[1].Status);
        Assert.Equal("Waiting for UI design", items[1].BlockedReason);

        Assert.Equal("TASK-003", items[2].Id);
        Assert.Equal(TaskStatus.Done, items[2].Status);
    }

    [Fact]
    public void Parses_numbered_list_format()
    {
        var content = """
        1. Add login screen
        2. Setup navigation
        3. Write tests
        """;

        var items = new BacklogParser().Parse(content);

        Assert.Equal(3, items.Count);
        Assert.Equal("TASK-1", items[0].Id);
        Assert.Equal("Add login screen", items[0].Title);
        Assert.Equal(TaskStatus.Pending, items[0].Status);
    }

    [Fact]
    public void Parses_simple_list_fallback()
    {
        var content = """
        - Add login screen
        - Setup navigation
        - Write tests
        """;

        var items = new BacklogParser().Parse(content);

        Assert.Equal(3, items.Count);
        Assert.Equal("Add login screen", items[0].Title);
        Assert.Equal(TaskStatus.Pending, items[0].Status);
    }

    [Fact]
    public void Empty_backlog_returns_empty_list()
    {
        var items = new BacklogParser().Parse(string.Empty);

        Assert.Empty(items);
    }

    [Fact]
    public void Checkbox_inline_blocked_marker_is_detected()
    {
        var content = "- [ ] TASK-001: Add feature [blocked]";

        var items = new BacklogParser().Parse(content);

        Assert.Single(items);
        Assert.Equal(TaskStatus.Blocked, items[0].Status);
    }
}
