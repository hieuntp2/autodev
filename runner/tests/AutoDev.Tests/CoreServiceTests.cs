using AutoDev.Core.Models;
using AutoDev.Core.Services;

namespace AutoDev.Tests;

public sealed class CoreServiceTests
{
    [Fact]
    public async Task ProjectConfigLoader_loads_project_config_from_projects_folder()
    {
        var root = CreateTempDirectory();
        var projects = Path.Combine(root, "projects");
        Directory.CreateDirectory(projects);
        await File.WriteAllTextAsync(Path.Combine(projects, "ai-pet.json"), """
        {
          "projectId": "ai-pet",
          "displayName": "AI Pet Companion Android",
          "repoPath": "D:\\Projects\\ai-pet-android",
          "branch": "ai/autonomous-30-days",
          "projectType": "android",
          "buildCommands": [".\\gradlew.bat build"],
          "testCommands": [".\\gradlew.bat test"],
          "allowedWritePaths": ["app/", "docs/active/"],
          "protectedPaths": ["docs/product/vision.md", ".env", "secrets/"],
          "dailyGoal": "Improve the pet",
          "maxTasksPerRun": 1,
          "maxDiffLines": 800,
          "allowRequirementProposal": true,
          "allowRequirementDirectEdit": false,
          "allowAutoCommit": true,
          "allowAutoPush": false,
          "commitOnlyIfBuildPasses": true
        }
        """);

        var config = await new ProjectConfigLoader(root).LoadAsync("ai-pet");

        Assert.Equal("ai-pet", config.ProjectId);
        Assert.Equal("AI Pet Companion Android", config.DisplayName);
        Assert.Equal(@"D:\Projects\ai-pet-android", config.RepoPath);
        Assert.Equal("ai/autonomous-30-days", config.Branch);
        Assert.Contains(@".\gradlew.bat build", config.BuildCommands);
        Assert.False(config.AllowAutoPush);
    }

    [Fact]
    public async Task WorkspaceService_creates_daily_workspace_and_metadata()
    {
        var root = CreateTempDirectory();
        var config = MinimalConfig();
        var date = new DateOnly(2026, 6, 4);

        var context = await new WorkspaceService(root).CreateAsync(config, date);

        Assert.Equal(Path.Combine(root, "workspaces", "ai-pet", "2026-06-04"), context.WorkspacePath);
        Assert.True(File.Exists(Path.Combine(context.WorkspacePath, "metadata.json")));
        Assert.True(Directory.Exists(Path.Combine(context.WorkspacePath, "00-input")));
        Assert.True(Directory.Exists(Path.Combine(context.WorkspacePath, "01-planning")));
        Assert.True(Directory.Exists(Path.Combine(context.WorkspacePath, "02-implementation")));
        Assert.True(Directory.Exists(Path.Combine(context.WorkspacePath, "03-verification")));
        Assert.True(Directory.Exists(Path.Combine(context.WorkspacePath, "04-review")));
        Assert.True(Directory.Exists(Path.Combine(context.WorkspacePath, "05-retrospective")));
    }

    [Fact]
    public void GuardrailValidator_flags_protected_paths_and_oversized_diffs()
    {
        var config = MinimalConfig() with
        {
            ProtectedPaths = ["docs/product/vision.md", "secrets/", "keystore/"],
            MaxDiffLines = 3,
            AllowRequirementDirectEdit = false,
            ScopeFile = "docs/product/scope.md"
        };
        var changedFiles = new[] { "app/src/Main.kt", "secrets/api.txt", "docs/product/scope.md" };
        var diff = string.Join(Environment.NewLine, ["a", "b", "c", "d"]);

        var result = new GuardrailValidator().Validate(config, changedFiles, diff);

        Assert.True(result.HasProtectedPathChanges);
        Assert.True(result.IsDiffTooLarge);
        Assert.Contains("secrets/api.txt", result.ProtectedPathChanges);
        Assert.Contains("docs/product/scope.md", result.ProtectedPathChanges);
        Assert.False(result.CanAutoCommit);
    }

    [Fact]
    public void TemplateRenderer_replaces_scalar_and_list_tokens()
    {
        var config = MinimalConfig() with
        {
            ProtectedPaths = ["docs/product/vision.md", "secrets/"],
            AllowedWritePaths = ["app/", "docs/active/"],
            BuildCommands = ["dotnet build"],
            TestCommands = ["dotnet test"]
        };
        var template = """
        Project: {{projectId}} / {{displayName}}
        Protected:
        {{protectedPaths}}
        Allowed:
        {{allowedWritePaths}}
        Build:
        {{buildCommands}}
        Context:
        {{projectContext}}
        """;

        var rendered = new TemplateRenderer().RenderPlannerTemplate(config, template, "memory summary");

        Assert.Contains("Project: ai-pet / AI Pet", rendered);
        Assert.Contains("- docs/product/vision.md", rendered);
        Assert.Contains("- app/", rendered);
        Assert.Contains("- dotnet build", rendered);
        Assert.Contains("memory summary", rendered);
        Assert.DoesNotContain("{{", rendered);
    }

    private static ProjectConfig MinimalConfig() => new()
    {
        ProjectId = "ai-pet",
        DisplayName = "AI Pet",
        RepoPath = @"D:\Projects\ai-pet-android",
        Branch = "ai/autonomous-30-days",
        ProjectType = "android",
        DailyGoal = "Improve the pet",
        BuildCommands = [],
        TestCommands = [],
        AllowedWritePaths = [],
        ProtectedPaths = [],
        MaxTasksPerRun = 1,
        MaxDiffLines = 800,
        AllowRequirementProposal = true,
        AllowRequirementDirectEdit = false,
        AllowAutoCommit = true,
        AllowAutoPush = false,
        CommitOnlyIfBuildPasses = true
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "autodev-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
