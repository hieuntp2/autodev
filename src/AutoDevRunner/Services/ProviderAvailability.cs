using System.Collections.Concurrent;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>
/// Runtime provider bench. A provider gets suspended (until a known reset time
/// or a cooldown) when its quota usage crosses the configured ceiling or it
/// reports a quota error; RunOrchestrator skips suspended providers so a
/// benched provider is not burned further. In-memory only — a fresh process
/// starts with everyone available and re-derives suspensions from usage probes.
/// </summary>
public class ProviderAvailability
{
    private readonly ConcurrentDictionary<ProviderKind, (DateTimeOffset Until, string Reason)> _suspensions = new();

    public bool IsAvailable(ProviderKind kind, out string? reason)
    {
        reason = null;
        if (!_suspensions.TryGetValue(kind, out var s)) return true;
        if (s.Until <= DateTimeOffset.Now)
        {
            _suspensions.TryRemove(kind, out _);
            return true;
        }

        reason = $"{s.Reason} (until {s.Until.ToLocalTime():HH:mm})";
        return false;
    }

    public void Suspend(ProviderKind kind, DateTimeOffset until, string reason) =>
        _suspensions[kind] = (until, reason);

    public void Clear(ProviderKind kind) => _suspensions.TryRemove(kind, out _);
}
