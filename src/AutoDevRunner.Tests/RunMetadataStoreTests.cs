using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunMetadataStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly RunMetadataStore _store = new();

    public RunMetadataStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "admeta-" + Guid.NewGuid().ToString("N"), ".ai-runner", "runs");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(_dir)!)!, recursive: true); } catch { }
    }

    [Fact]
    public void Sidecar_path_swaps_md_for_json()
        => Assert.EndsWith("run7.json", RunMetadataStore.SidecarPathFor("/x/.ai-runner/runs/20260101-run7.md"));

    [Fact]
    public async Task Write_then_read_round_trips_with_string_enums()
    {
        var md = Path.Combine(_dir, "20260705-run42.md");
        var meta = new RunMetadata
        {
            RunId = 42,
            ProjectName = "Pixel Pet",
            Stage = LifecycleStage.Committed.ToString(),
            Risk = RiskLevel.Safe.ToString(),
            StartedAt = new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc),
            Skills = new() { new RunSkillRef("pixel-animation-artist", new[] { "sprite sheet" }) },
            Artifacts = new() { new ArtifactRef("assets/x/x_sheet.png", ArtifactKind.SpriteSheet, "pixel-animation-artist", 123) }
        };

        await _store.WriteAsync(md, meta);

        // The sidecar must store the artifact kind as its NAME, not a number.
        var json = await File.ReadAllTextAsync(RunMetadataStore.SidecarPathFor(md));
        Assert.Contains("\"SpriteSheet\"", json);

        var read = _store.ReadForLog(md);
        Assert.NotNull(read);
        Assert.Equal(42, read!.RunId);
        Assert.Equal("Committed", read.Stage);
        Assert.Single(read.Artifacts);
        Assert.Equal(ArtifactKind.SpriteSheet, read.Artifacts[0].Kind);
        Assert.Equal("pixel-animation-artist", read.Skills[0].Id);
    }

    [Fact]
    public async Task ReadAllForRepo_returns_newest_first()
    {
        var repo = Path.GetDirectoryName(Path.GetDirectoryName(_dir)!)!; // <tmp>/admeta-xxx
        await _store.WriteAsync(Path.Combine(_dir, "a-run1.md"),
            new RunMetadata { RunId = 1, StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await _store.WriteAsync(Path.Combine(_dir, "b-run2.md"),
            new RunMetadata { RunId = 2, StartedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) });

        var all = _store.ReadAllForRepo(repo);
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all[0].RunId); // newest first
    }
}
