using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RiskAssessorTests
{
    private readonly RiskAssessor _risk = new();

    private static List<GitChange> Changes(params (string status, string path)[] items)
        => items.Select(i => new GitChange(i.status, i.path)).ToList();

    [Fact]
    public void Deleting_a_file_is_risky()
    {
        var r = _risk.Assess(Changes((" D", "src/Old.cs")), "cleanup");
        Assert.Equal(RiskLevel.Risky, r.Level);
        Assert.Contains(r.Reasons, x => x.Contains("delete", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Editing_appsettings_is_risky()
    {
        var r = _risk.Assess(Changes((" M", "src/AutoDevRunner/appsettings.json")), "tweak config");
        Assert.Equal(RiskLevel.Risky, r.Level);
    }

    [Theory]
    [InlineData("Deploy the service to production")]
    [InlineData("Run the database migration")]
    [InlineData("Rotate the API key secret")]
    public void Risky_intent_in_task_text_is_risky(string task)
    {
        var r = _risk.Assess(Changes((" M", "src/Foo.cs")), task);
        Assert.Equal(RiskLevel.Risky, r.Level);
    }

    [Fact]
    public void Only_assets_and_docs_is_safe()
    {
        var r = _risk.Assess(Changes(
            ("A ", "assets/pet/animations/blink/blink_sheet.png"),
            ("A ", "assets/pet/animations/blink/blink.animation.json"),
            ("A ", "README.md")), "add blink animation");
        Assert.Equal(RiskLevel.Safe, r.Level);
    }

    [Fact]
    public void Ordinary_source_change_is_normal()
    {
        var r = _risk.Assess(Changes((" M", "src/Player.cs"), (" M", "src/Pet.cs")), "improve player");
        Assert.Equal(RiskLevel.Normal, r.Level);
    }

    [Fact]
    public void No_changes_is_normal()
    {
        var r = _risk.Assess(new List<GitChange>(), null);
        Assert.Equal(RiskLevel.Normal, r.Level);
    }
}
