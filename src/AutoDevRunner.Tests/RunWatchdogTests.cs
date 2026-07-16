using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunWatchdogPolicyTests
{
    private static readonly DateTime Now = new(2026, 07, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    [Fact]
    public void Run_held_by_live_owner_is_never_reclaimed()
    {
        Assert.False(RunWatchdogPolicy.ShouldReclaim(
            RunLockLiveness.HeldByLiveOwner, Now.AddHours(-5), Now, Grace));
    }

    [Theory]
    [InlineData(RunLockLiveness.NotHeld)]
    [InlineData(RunLockLiveness.Stale)]
    public void Ownerless_run_is_reclaimed_after_grace(RunLockLiveness liveness)
    {
        Assert.True(RunWatchdogPolicy.ShouldReclaim(liveness, Now.AddMinutes(-3), Now, Grace));
    }

    [Theory]
    [InlineData(RunLockLiveness.NotHeld)]
    [InlineData(RunLockLiveness.Stale)]
    public void Fresh_run_is_left_alone_within_grace(RunLockLiveness liveness)
    {
        // A run that just started may not have its lock observable yet.
        Assert.False(RunWatchdogPolicy.ShouldReclaim(liveness, Now.AddSeconds(-30), Now, Grace));
    }
}

public class RunLockLivenessTests : IDisposable
{
    private readonly string _repo = Path.Combine(
        Path.GetTempPath(), "autodev-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { /* best effort */ }
    }

    private Project MakeProject() => new()
    {
        Id = 1,
        Name = "P",
        RepoPath = _repo,
        MaxRunMinutes = 30
    };

    [Fact]
    public void No_lock_file_reports_not_held()
    {
        Directory.CreateDirectory(_repo);

        Assert.Equal(RunLockLiveness.NotHeld, new RunLock().CheckLiveness(MakeProject()));
    }

    [Fact]
    public void Own_acquired_lock_reports_live_owner_and_release_reports_not_held()
    {
        var runLock = new RunLock();
        var project = MakeProject();
        Assert.True(runLock.TryAcquire(project, out var lease, out _));

        Assert.Equal(RunLockLiveness.HeldByLiveOwner, runLock.CheckLiveness(project));

        // A different RunLock instance (another process's view) still sees the
        // live owner through the lock file's PID + process start time.
        Assert.Equal(RunLockLiveness.HeldByLiveOwner, new RunLock().CheckLiveness(project));

        lease!.Dispose();
        Assert.Equal(RunLockLiveness.NotHeld, runLock.CheckLiveness(project));
    }

    [Fact]
    public void Lock_owned_by_dead_process_reports_stale()
    {
        var project = MakeProject();
        var lockPath = RunLock.LockPathFor(_repo);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        // Current PID but a process start time that cannot match — the owner
        // identity check treats it as a dead process (PID was reused).
        File.WriteAllText(lockPath, $$"""
            {
              "ProjectId": 1,
              "Token": "t",
              "ProcessId": {{Environment.ProcessId}},
              "ProcessStartedAtUtc": "2000-01-01T00:00:00+00:00",
              "StartedAtUtc": "2026-07-16T00:00:00+00:00",
              "ExpiresAtUtc": "2999-01-01T00:00:00+00:00"
            }
            """);

        Assert.Equal(RunLockLiveness.Stale, new RunLock().CheckLiveness(project));
    }

    [Fact]
    public void Expired_lease_reports_stale()
    {
        var project = MakeProject();
        var lockPath = RunLock.LockPathFor(_repo);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllText(lockPath, $$"""
            {
              "ProjectId": 1,
              "Token": "t",
              "ProcessId": {{Environment.ProcessId}},
              "StartedAtUtc": "2026-07-16T00:00:00+00:00",
              "ExpiresAtUtc": "2026-07-16T00:30:00+00:00"
            }
            """);

        Assert.Equal(RunLockLiveness.Stale, new RunLock().CheckLiveness(project));
    }
}
