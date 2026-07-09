using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ModelRoutingTests
{
    [Fact]
    public void Maintenance_safe_task_routes_to_light()
    {
        var tier = TaskTierClassifier.Classify(
            new TaskProposal("Tidy docs", "r", "o", "e", null, RiskLevel.Safe, null, "maintenance"),
            new RiskAssessment(RiskLevel.Safe, new()),
            RunLessons.Empty,
            "Tidy docs");

        Assert.Equal(TaskTier.Light, tier);
    }

    [Fact]
    public void Risky_task_routes_to_deep()
    {
        var tier = TaskTierClassifier.Classify(
            null,
            new RiskAssessment(RiskLevel.Risky, new() { "large refactor" }),
            RunLessons.Empty,
            "Implement risky architecture change");

        Assert.Equal(TaskTier.Deep, tier);
    }

    [Fact]
    public void Ordinary_task_routes_to_standard()
    {
        var tier = TaskTierClassifier.Classify(
            null,
            new RiskAssessment(RiskLevel.Normal, new()),
            RunLessons.Empty,
            "Add settings UI");

        Assert.Equal(TaskTier.Standard, tier);
    }

    [Fact]
    public void Repeatedly_failing_task_routes_to_deep()
    {
        var lessons = new RunLessons(
            Array.Empty<RunLesson>(),
            new[] { "Fix flaky validation" },
            Array.Empty<string>());

        var tier = TaskTierClassifier.Classify(
            null,
            new RiskAssessment(RiskLevel.Normal, new()),
            lessons,
            "Fix flaky validation");

        Assert.Equal(TaskTier.Deep, tier);
    }

    [Fact]
    public void Missing_tier_arguments_fall_back_to_base_arguments()
    {
        var opt = new ProviderCliOptions
        {
            Arguments = "exec base",
            Tiers = new Dictionary<string, string> { ["Light"] = "exec light" }
        };

        Assert.Equal("exec light", opt.ResolveArguments(TaskTier.Light, modelRoutingEnabled: true));
        Assert.Equal("exec base", opt.ResolveArguments(TaskTier.Deep, modelRoutingEnabled: true));
        Assert.Equal("exec base", opt.ResolveArguments(TaskTier.Light, modelRoutingEnabled: false));
    }
}
