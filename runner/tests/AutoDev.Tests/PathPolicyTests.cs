using AutoDev.Core.Services;

namespace AutoDev.Tests;

public sealed class PathPolicyTests
{
    [Fact]
    public void Write_allowed_inside_allowed_path()
    {
        var policy = new PathPolicy(["app/", "docs/active/"], [".env"]);

        Assert.True(policy.IsWriteAllowed("app/src/Main.kt"));
        Assert.True(policy.IsWriteAllowed("docs/active/backlog.md"));
    }

    [Fact]
    public void Write_blocked_outside_allowed_paths()
    {
        var policy = new PathPolicy(["app/"], [".env"]);

        Assert.False(policy.IsWriteAllowed("docs/product/vision.md"));
        Assert.False(policy.IsWriteAllowed("build/outputs/apk/debug.apk"));
    }

    [Fact]
    public void Protected_path_beats_allowed_path()
    {
        var policy = new PathPolicy(["docs/"], ["docs/product/"]);

        Assert.False(policy.IsWriteAllowed("docs/product/vision.md"));
        Assert.True(policy.IsWriteAllowed("docs/active/backlog.md"));
    }

    [Fact]
    public void Exact_protected_file_beats_allowed_directory()
    {
        var policy = new PathPolicy(["app/"], ["app/secrets.txt"]);

        Assert.False(policy.IsWriteAllowed("app/secrets.txt"));
        Assert.True(policy.IsWriteAllowed("app/src/Main.kt"));
    }

    [Fact]
    public void Backslash_paths_are_normalized()
    {
        var policy = new PathPolicy([@"app\"], [@"app\secrets.txt"]);

        Assert.False(policy.IsWriteAllowed(@"app\secrets.txt"));
        Assert.True(policy.IsWriteAllowed(@"app\src\Main.kt"));
    }

    [Fact]
    public void Empty_allowed_paths_blocks_everything()
    {
        var policy = new PathPolicy([], []);

        Assert.False(policy.IsWriteAllowed("app/src/Main.kt"));
    }

    [Fact]
    public void HasPathOverlap_detects_overlap()
    {
        var policy = new PathPolicy(["docs/"], ["docs/product/"]);

        var hasOverlap = policy.HasPathOverlap(out var overlaps);

        Assert.True(hasOverlap);
        Assert.NotEmpty(overlaps);
    }

    [Fact]
    public void HasPathOverlap_returns_false_when_no_overlap()
    {
        var policy = new PathPolicy(["app/", "docs/active/"], ["docs/product/", ".env"]);

        var hasOverlap = policy.HasPathOverlap(out var overlaps);

        Assert.False(hasOverlap);
        Assert.Empty(overlaps);
    }
}
