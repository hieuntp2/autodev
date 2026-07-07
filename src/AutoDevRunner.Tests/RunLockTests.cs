using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunLockTests : IDisposable
{
    private readonly string _repo;

    public RunLockTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "adlock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Rejects_second_active_session_for_same_project_until_release()
    {
        var project = new Project { Id = 7, RepoPath = _repo, MaxRunMinutes = 30 };
        var locks = new RunLock();

        Assert.True(locks.TryAcquire(project, out var first, out var firstReason));
        Assert.Null(firstReason);
        Assert.True(locks.IsRunning(project));

        Assert.False(locks.TryAcquire(project, out var second, out var secondReason));
        Assert.Null(second);
        Assert.Contains("already running", secondReason, StringComparison.OrdinalIgnoreCase);

        first!.Dispose();

        Assert.False(locks.IsRunning(project));
        Assert.True(locks.TryAcquire(project, out var afterRelease, out _));
        afterRelease!.Dispose();
    }

    [Fact]
    public void Expired_lock_file_can_be_replaced()
    {
        var project = new Project { Id = 7, RepoPath = _repo, MaxRunMinutes = 30 };
        var lockPath = RunLock.LockPathFor(_repo);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllText(lockPath, """
        {
          "projectId": 7,
          "token": "stale-token",
          "processId": 123,
          "startedAtUtc": "2000-01-01T00:00:00Z",
          "expiresAtUtc": "2000-01-01T00:01:00Z"
        }
        """);

        var locks = new RunLock();

        Assert.True(locks.TryAcquire(project, out var lease, out var reason));
        Assert.Null(reason);
        Assert.True(locks.IsRunning(project));

        lease!.Dispose();
    }
}
