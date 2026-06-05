using AutoDev.Core.Models;
using AutoDev.Core.Services;

namespace AutoDev.Tests;

public sealed class StatusWriterTests
{
    [Fact]
    public async Task Prepends_to_status_file_when_writable()
    {
        var repoPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(repoPath, "docs", "active"));
        var statusFile = "docs/active/implementation-status.md";
        var fullPath = Path.Combine(repoPath, statusFile);
        await File.WriteAllTextAsync(fullPath, "# Existing Status\n\nPrevious content.");

        var project = MinimalConfig(repoPath) with
        {
            StatusFile = statusFile,
            AllowedWritePaths = ["docs/active/"]
        };
        var report = new RunReportData
        {
            RunTimestamp = new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero),
            TaskId = "TASK-001",
            TaskTitle = "Add login screen",
            BuildPassed = true
        };

        var written = await new StatusWriter().WriteAsync(project, report);

        Assert.True(written);
        var content = await File.ReadAllTextAsync(fullPath);
        Assert.Contains("AutoDev Run", content);
        Assert.Contains("TASK-001", content);
        Assert.Contains("PASSED", content);
        Assert.Contains("Existing Status", content);
        Assert.True(content.IndexOf("AutoDev Run", StringComparison.Ordinal) < content.IndexOf("Existing Status", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Creates_fallback_report_when_status_file_not_writable()
    {
        var repoPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(repoPath, "docs", "active"));

        var project = MinimalConfig(repoPath) with
        {
            StatusFile = "docs/product/status.md",
            AllowedWritePaths = ["docs/active/"]
        };
        var report = new RunReportData
        {
            RunTimestamp = new DateTimeOffset(2026, 6, 5, 10, 0, 0, TimeSpan.Zero),
            TaskId = "TASK-001",
            TaskTitle = "Add login screen",
            BuildPassed = true
        };

        var written = await new StatusWriter().WriteAsync(project, report);

        Assert.True(written);
        var files = Directory.GetFiles(Path.Combine(repoPath, "docs", "active"), "autodev-run-report-*.md");
        Assert.Single(files);
        var content = await File.ReadAllTextAsync(files[0]);
        Assert.Contains("TASK-001", content);
    }

    [Fact]
    public async Task Returns_false_when_nothing_is_writable()
    {
        var repoPath = CreateTempDirectory();
        var project = MinimalConfig(repoPath) with
        {
            StatusFile = "docs/product/status.md",
            AllowedWritePaths = ["app/"]
        };
        var report = new RunReportData
        {
            RunTimestamp = DateTimeOffset.Now,
            BuildPassed = false,
            BlockedReason = "No tasks"
        };

        var written = await new StatusWriter().WriteAsync(project, report);

        Assert.False(written);
    }

    private static ProjectConfig MinimalConfig(string repoPath) => new()
    {
        ProjectId = "test",
        DisplayName = "Test",
        RepoPath = repoPath,
        Branch = "main",
        ProjectType = "android",
        DailyGoal = "Test",
        BuildCommands = [],
        TestCommands = [],
        AllowedWritePaths = [],
        ProtectedPaths = [],
        MaxTasksPerRun = 1,
        MaxDiffLines = 800
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "autodev-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
