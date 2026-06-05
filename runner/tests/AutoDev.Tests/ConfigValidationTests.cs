using AutoDev.Core.Models;
using AutoDev.Core.Services;

namespace AutoDev.Tests;

public sealed class ConfigValidationTests
{
    [Fact]
    public void Valid_config_with_existing_paths_passes()
    {
        var (root, repoPath) = CreateTempRepoWithBacklog();
        var config = MinimalConfig(repoPath) with { BacklogFile = "docs/backlog.md" };
        File.WriteAllText(Path.Combine(repoPath, "docs", "backlog.md"), "- [ ] TASK-001: First task");

        var result = new ProjectConfigValidator().Validate(config);

        Assert.True(result.IsValid);
        Assert.True(result.CanProceed);
        Assert.Empty(result.Errors);
        _ = root;
    }

    [Fact]
    public void Missing_repoPath_on_disk_fails()
    {
        var config = MinimalConfig(@"C:\does\not\exist\at\all\12345") with
        {
            BacklogFile = "docs/backlog.md"
        };

        var result = new ProjectConfigValidator().Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("repoPath does not exist"));
    }

    [Fact]
    public void Missing_backlogFile_is_an_error()
    {
        var repoPath = CreateTempDirectory();
        var config = MinimalConfig(repoPath);

        var result = new ProjectConfigValidator().Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("backlogFile is required"));
    }

    [Fact]
    public void BacklogFile_not_found_in_repo_is_an_error()
    {
        var repoPath = CreateTempDirectory();
        var config = MinimalConfig(repoPath) with { BacklogFile = "docs/missing-backlog.md" };

        var result = new ProjectConfigValidator().Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("backlogFile not found"));
    }

    [Fact]
    public void Empty_buildCommands_produces_warning_not_error()
    {
        var (root, repoPath) = CreateTempRepoWithBacklog();
        var config = MinimalConfig(repoPath) with
        {
            BacklogFile = "docs/backlog.md",
            BuildCommands = []
        };
        File.WriteAllText(Path.Combine(repoPath, "docs", "backlog.md"), "- [ ] TASK-001: First task");

        var result = new ProjectConfigValidator().Validate(config);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("buildCommands is empty"));
        _ = root;
    }

    [Fact]
    public void Optional_doc_paths_that_are_missing_produce_warnings()
    {
        var (root, repoPath) = CreateTempRepoWithBacklog();
        var config = MinimalConfig(repoPath) with
        {
            BacklogFile = "docs/backlog.md",
            MainRequirementFile = "docs/missing-vision.md"
        };
        File.WriteAllText(Path.Combine(repoPath, "docs", "backlog.md"), "- [ ] TASK-001: First task");

        var result = new ProjectConfigValidator().Validate(config);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("mainRequirementFile") && w.Contains("not found"));
        _ = root;
    }

    private static (string root, string repoPath) CreateTempRepoWithBacklog()
    {
        var root = CreateTempDirectory();
        var repoPath = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(repoPath, "docs"));
        return (root, repoPath);
    }

    private static ProjectConfig MinimalConfig(string repoPath) => new()
    {
        ProjectId = "test",
        DisplayName = "Test",
        RepoPath = repoPath,
        Branch = "main",
        ProjectType = "android",
        DailyGoal = "Test goal",
        BuildCommands = ["./gradlew build"],
        TestCommands = [],
        AllowedWritePaths = ["app/", "docs/active/"],
        ProtectedPaths = [".env"],
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
