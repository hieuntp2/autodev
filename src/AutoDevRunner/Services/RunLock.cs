using System.Collections.Concurrent;

namespace AutoDevRunner.Services;

/// <summary>
/// Prevents concurrent runs of the same project (scheduler + manual trigger).
/// Singleton.
/// </summary>
public class RunLock
{
    private readonly ConcurrentDictionary<int, byte> _active = new();

    public bool TryAcquire(int projectId) => _active.TryAdd(projectId, 0);
    public void Release(int projectId) => _active.TryRemove(projectId, out _);
    public bool IsRunning(int projectId) => _active.ContainsKey(projectId);
    public IReadOnlyCollection<int> ActiveProjectIds => _active.Keys.ToArray();
}
