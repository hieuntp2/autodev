using System.Text.Json.Nodes;

namespace AutoDevRunner.Services;

/// <summary>
/// One rate-limit window as reported by Codex (e.g. the 5-hour or weekly cap).
/// A window whose <see cref="ResetsAt"/> is in the past has rolled over — its
/// recorded usage no longer applies.
/// </summary>
public sealed record CodexLimitWindow(double UsedPercent, int WindowMinutes, DateTimeOffset ResetsAt)
{
    public bool IsCurrent(DateTimeOffset now) => ResetsAt > now;
    public string Label => WindowMinutes >= 10080 ? "weekly" : $"{WindowMinutes / 60}h";
}

public sealed record CodexUsage(CodexLimitWindow? Primary, CodexLimitWindow? Secondary, DateTimeOffset ObservedAt)
{
    /// <summary>Highest usage among windows that have not reset yet.</summary>
    public double EffectiveUsedPercent(DateTimeOffset now) =>
        Windows(now).Select(w => w.UsedPercent).DefaultIfEmpty(0).Max();

    /// <summary>Reset time of the busiest current window (when usage will drop).</summary>
    public DateTimeOffset? BusiestResetsAt(DateTimeOffset now) =>
        Windows(now).OrderByDescending(w => w.UsedPercent).FirstOrDefault()?.ResetsAt;

    public string Describe(DateTimeOffset now) =>
        Windows(now).Count == 0
            ? "no current rate-limit window (all reset)"
            : string.Join(", ", Windows(now).Select(w => $"{w.Label} {w.UsedPercent:0.#}% (resets {w.ResetsAt.ToLocalTime():HH:mm})"));

    private List<CodexLimitWindow> Windows(DateTimeOffset now) =>
        new[] { Primary, Secondary }.Where(w => w is not null && w.IsCurrent(now)).Cast<CodexLimitWindow>().ToList();
}

/// <summary>
/// Best-effort reader of Codex quota usage. The Codex CLI has no headless
/// "usage" command, but it appends rate-limit snapshots (used_percent per
/// window + reset time) to its session rollout files under
/// ~/.codex/sessions/yyyy/MM/dd/rollout-*.jsonl after every turn. We read the
/// most recent snapshot. Returns null when nothing (recent) is available —
/// callers must treat that as "unknown", not "exhausted".
/// </summary>
public class CodexUsageReader
{
    private readonly ILogger<CodexUsageReader> _log;

    public CodexUsageReader(ILogger<CodexUsageReader> log) => _log = log;

    public virtual CodexUsage? TryRead()
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            if (!Directory.Exists(root)) return null;

            // Newest few rollout files; the snapshot we want is near the end of
            // the most recent session that made an API turn.
            var files = Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(8);

            foreach (var file in files)
            {
                var usage = TryReadFile(file.FullName);
                if (usage is not null) return usage;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read Codex usage from session files.");
        }

        return null;
    }

    private CodexUsage? TryReadFile(string path)
    {
        string? lastLine = null;
        DateTimeOffset observedAt = DateTimeOffset.MinValue;

        // Codex may still be writing this file — share read/write.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains("\"rate_limits\"", StringComparison.Ordinal))
                lastLine = line;
        }

        if (lastLine is null) return null;

        var node = JsonNode.Parse(lastLine);
        var limits = node?["payload"]?["rate_limits"];
        if (limits is null) return null;

        if (DateTimeOffset.TryParse(node?["timestamp"]?.GetValue<string>(), out var ts))
            observedAt = ts;

        return new CodexUsage(ParseWindow(limits["primary"]), ParseWindow(limits["secondary"]), observedAt);
    }

    private static CodexLimitWindow? ParseWindow(JsonNode? window)
    {
        if (window is null) return null;
        var used = window["used_percent"]?.GetValue<double>();
        var minutes = window["window_minutes"]?.GetValue<int>();
        var resets = window["resets_at"]?.GetValue<long>();
        if (used is null || minutes is null || resets is null) return null;
        return new CodexLimitWindow(used.Value, minutes.Value, DateTimeOffset.FromUnixTimeSeconds(resets.Value));
    }
}
