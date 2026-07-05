using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ArtifactTrackerTests
{
    private readonly ArtifactTracker _tracker = new();
    private const string Repo = "/repo"; // paths don't need to exist; size is best-effort

    [Theory]
    [InlineData("assets/pet/animations/blink/blink_sheet.png", ArtifactKind.SpriteSheet, "pixel-animation-artist")]
    [InlineData("assets/pet/animations/blink/blink_preview.gif", ArtifactKind.Gif, "pixel-animation-artist")]
    [InlineData("assets/pet/animations/blink/blink.animation.json", ArtifactKind.AnimationManifest, "pixel-animation-artist")]
    [InlineData("assets/pet/animations/blink/frames/frame_003.png", ArtifactKind.Frame, "pixel-animation-artist")]
    [InlineData("docs/logo.png", ArtifactKind.Image, null)]
    [InlineData(".ai-runner/runs/20260101-run7.md", ArtifactKind.Report, null)]
    public void Classifies_known_artifacts(string path, ArtifactKind kind, string? skill)
    {
        var a = _tracker.Classify(path, Repo);
        Assert.NotNull(a);
        Assert.Equal(kind, a!.Kind);
        Assert.Equal(skill, a.SkillId);
    }

    [Theory]
    [InlineData("src/Player.cs")]
    [InlineData("README.md")]
    [InlineData("package.json")]
    public void Ignores_non_artifacts(string path)
        => Assert.Null(_tracker.Classify(path, Repo));

    [Fact]
    public void Track_filters_and_orders()
    {
        var changed = new[]
        {
            "src/Player.cs",
            "assets/x/frames/frame_000.png",
            "assets/x/x_sheet.png",
            "notes.txt"
        };
        var artifacts = _tracker.Track(changed, Repo);
        Assert.Equal(2, artifacts.Count);
        // Ordered by Kind: Frame(1) before SpriteSheet(2).
        Assert.Equal(ArtifactKind.Frame, artifacts[0].Kind);
        Assert.Equal(ArtifactKind.SpriteSheet, artifacts[1].Kind);
    }
}
