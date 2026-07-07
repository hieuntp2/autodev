using System.Collections.Concurrent;
using System.Text.Json;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>
/// Prevents concurrent runs of the same project across both threads and
/// processes. A per-repo lock file is used because dashboard/manual runs and
/// Windows Task Scheduler runs may execute in different AutoDevRunner processes.
/// </summary>
public class RunLock
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<int, RunLockLease> _active = new();

    public static string LockPathFor(string repoPath) =>
        Path.Combine(repoPath, ".ai-runner", "run.lock");

    public static TimeSpan LeaseDurationFor(Project project)
    {
        var maxRun = Math.Max(1, project.MaxRunMinutes);
        var minutes = Math.Max(maxRun * 3, maxRun + 15);
        return TimeSpan.FromMinutes(minutes);
    }

    public bool TryAcquire(Project project, out RunLockLease? lease, out string? reason)
    {
        lease = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(project.RepoPath))
        {
            reason = "Project has no repository path.";
            return false;
        }

        CleanupExpiredInMemory(project.Id);
        if (_active.TryGetValue(project.Id, out var current))
        {
            reason = $"Project {project.Id} is already running in this process since {current.StartedAtUtc:u}.";
            return false;
        }

        var lockPath = LockPathFor(project.RepoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (File.Exists(lockPath))
            {
                if (!TryRemoveStaleLock(lockPath, out reason))
                    return false;
            }

            try
            {
                var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                var now = DateTimeOffset.UtcNow;
                var created = new RunLockLease(
                    this, project.Id, project.RepoPath, lockPath, Guid.NewGuid().ToString("N"),
                    Environment.ProcessId, now, stream);
                created.Renew(LeaseDurationFor(project));

                if (_active.TryAdd(project.Id, created))
                {
                    lease = created;
                    return true;
                }

                created.Dispose();
                reason = $"Project {project.Id} is already running in this process.";
                return false;
            }
            catch (IOException) when (attempt == 0)
            {
                // Another process won the create race. Re-read the file once so
                // the caller gets a useful active/stale reason.
                continue;
            }
            catch (IOException ex)
            {
                reason = ActiveReason(lockPath) ?? $"Project lock is already held: {ex.Message}";
                return false;
            }
        }

        reason = ActiveReason(lockPath) ?? "Project lock is already held.";
        return false;
    }

    public bool IsRunning(Project project)
    {
        CleanupExpiredInMemory(project.Id);
        if (_active.ContainsKey(project.Id)) return true;

        var lockPath = LockPathFor(project.RepoPath);
        if (!File.Exists(lockPath)) return false;

        if (TryRead(lockPath, out var info) && IsExpired(info!))
        {
            TryDelete(lockPath);
            return false;
        }

        return true;
    }

    /// <summary>In-process view retained for callers that do not have a Project.</summary>
    public bool IsRunning(int projectId)
    {
        CleanupExpiredInMemory(projectId);
        return _active.ContainsKey(projectId);
    }

    public IReadOnlyCollection<int> ActiveProjectIds => _active.Keys.ToArray();

    public IReadOnlyCollection<int> ActiveProjectIdsFor(IEnumerable<Project> projects) =>
        projects.Where(IsRunning).Select(p => p.Id).ToArray();

    internal void Release(RunLockLease lease)
    {
        if (_active.TryGetValue(lease.ProjectId, out var current) && ReferenceEquals(current, lease))
            _active.TryRemove(lease.ProjectId, out _);

        lease.CloseStream();
        TryDelete(lease.LockPath);
    }

    private void CleanupExpiredInMemory(int projectId)
    {
        if (!_active.TryGetValue(projectId, out var lease)) return;
        if (lease.IsDisposed)
            _active.TryRemove(projectId, out _);
    }

    private static bool TryRemoveStaleLock(string lockPath, out string? reason)
    {
        reason = null;
        if (!TryRead(lockPath, out var info))
        {
            reason = $"Project lock exists but could not be read: {lockPath}";
            return false;
        }

        if (!IsExpired(info!))
        {
            reason = ActiveReason(info!);
            return false;
        }

        if (TryDelete(lockPath))
            return true;

        reason = $"Project lock expired at {info!.ExpiresAtUtc:u}, but it is still held by another process.";
        return false;
    }

    private static bool TryRead(string lockPath, out LockFile? info)
    {
        info = null;
        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            info = JsonSerializer.Deserialize<LockFile>(stream, JsonOpts);
            return info is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDelete(string lockPath)
    {
        try
        {
            if (File.Exists(lockPath)) File.Delete(lockPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExpired(LockFile info) =>
        info.ExpiresAtUtc <= DateTimeOffset.UtcNow;

    private static string? ActiveReason(string lockPath) =>
        TryRead(lockPath, out var info) ? ActiveReason(info!) : null;

    private static string ActiveReason(LockFile info) =>
        $"Project is already running (pid {info.ProcessId}, started {info.StartedAtUtc:u}, expires {info.ExpiresAtUtc:u}).";

    internal static void WriteLockFile(FileStream stream, RunLockLease lease)
    {
        stream.Position = 0;
        stream.SetLength(0);
        JsonSerializer.Serialize(stream, new LockFile
        {
            ProjectId = lease.ProjectId,
            Token = lease.Token,
            ProcessId = lease.ProcessId,
            StartedAtUtc = lease.StartedAtUtc,
            ExpiresAtUtc = lease.ExpiresAtUtc
        }, JsonOpts);
        stream.Flush(flushToDisk: true);
    }

    private sealed class LockFile
    {
        public int ProjectId { get; set; }
        public string Token { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}

public sealed class RunLockLease : IDisposable
{
    private readonly RunLock _owner;
    private readonly object _gate = new();
    private FileStream? _stream;
    private bool _disposed;

    internal RunLockLease(RunLock owner, int projectId, string repoPath, string lockPath,
        string token, int processId, DateTimeOffset startedAtUtc, FileStream stream)
    {
        _owner = owner;
        ProjectId = projectId;
        RepoPath = repoPath;
        LockPath = lockPath;
        Token = token;
        ProcessId = processId;
        StartedAtUtc = startedAtUtc;
        _stream = stream;
    }

    public int ProjectId { get; }
    public string RepoPath { get; }
    public string LockPath { get; }
    public string Token { get; }
    public int ProcessId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool IsDisposed { get; private set; }

    public void Renew(TimeSpan duration)
    {
        lock (_gate)
        {
            if (_disposed || _stream is null) return;
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(duration);
            RunLock.WriteLockFile(_stream, this);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            IsDisposed = true;
        }

        _owner.Release(this);
    }

    internal void CloseStream()
    {
        lock (_gate)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}
