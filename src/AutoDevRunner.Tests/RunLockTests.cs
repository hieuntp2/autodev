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

    [Fact]
    public void Non_expired_lock_owned_by_a_dead_process_is_recovered_after_restart()
    {
        var project = new Project { Id = 7, RepoPath = _repo, MaxRunMinutes = 120 };
        var lockPath = RunLock.LockPathFor(_repo);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllText(lockPath, $$"""
        {
          "projectId": 7,
          "token": "dead-owner",
          "processId": 2147483647,
          "processStartedAtUtc": "2000-01-01T00:00:00Z",
          "startedAtUtc": "{{DateTimeOffset.UtcNow.AddMinutes(-5):O}}",
          "expiresAtUtc": "{{DateTimeOffset.UtcNow.AddHours(5):O}}"
        }
        """);
        var locks = new RunLock();

        var recovered = locks.TryRecoverDeadOwner(project, out var reason);

        Assert.True(recovered, reason);
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void Matching_live_owner_lock_is_not_recovered()
    {
        var project = new Project { Id = 7, RepoPath = _repo, MaxRunMinutes = 30 };
        var locks = new RunLock();
        Assert.True(locks.TryAcquire(project, out var lease, out _));

        var recovered = locks.TryRecoverDeadOwner(project, out var reason);

        Assert.False(recovered);
        Assert.Contains("still alive", reason, StringComparison.OrdinalIgnoreCase);
        lease!.Dispose();
    }
}
