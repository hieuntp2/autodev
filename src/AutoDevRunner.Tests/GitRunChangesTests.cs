using AutoDevRunner.Providers;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class GitRunChangesTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ad-git-" + Guid.NewGuid().ToString("N"));
    private readonly ProcessRunner _process = new();

    public GitRunChangesTests() => Directory.CreateDirectory(_repo);

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }

    [Fact]
    public async Task Snapshot_merges_provider_commits_with_working_tree_changes()
    {
        await Git("init");
        await Git("config user.email autodev-tests@example.invalid");
        await Git("config user.name AutoDevTests");
        await File.WriteAllTextAsync(Path.Combine(_repo, "committed.txt"), "one");
        await Git("add committed.txt");
        await Git("commit -m initial");
        var service = new GitService(_process);
        var startingHead = await service.GetHeadShaAsync(_repo);

        await File.WriteAllTextAsync(Path.Combine(_repo, "committed.txt"), "two");
        await Git("add committed.txt");
        await Git("commit -m provider-change");
        await File.WriteAllTextAsync(Path.Combine(_repo, "working.txt"), "working");

        var snapshot = await service.GetRunChangesAsync(_repo, startingHead);

        Assert.True(snapshot.ProviderCommitted);
        Assert.NotEqual(startingHead, snapshot.CurrentHead);
        Assert.Contains(snapshot.Changes, change => change.Path == "committed.txt");
        Assert.Contains(snapshot.Changes, change => change.Path == "working.txt");
    }

    private async Task Git(string args)
    {
        var result = await _process.RunAsync("git", args, _repo, TimeSpan.FromSeconds(10));
        Assert.Equal(0, result.ExitCode);
    }
}
