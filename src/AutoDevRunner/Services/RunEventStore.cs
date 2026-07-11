using System.Text.Json;

namespace AutoDevRunner.Services;

public sealed record RunLiveEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string Kind,
    string Message,
    bool Terminal = false);

/// <summary>Append-only cross-process event files used by scheduled runs and the dashboard.</summary>
public sealed class RunEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string PathFor(string repoPath, int runId) =>
        Path.Combine(repoPath, ".ai-runner", "runs", $"run-{runId}.events.jsonl");

    public RunEventSink Open(string repoPath, int runId)
    {
        var path = PathFor(repoPath, runId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var last = ReadAfter(repoPath, runId, 0).LastOrDefault()?.Sequence ?? 0;
        return new RunEventSink(path, last, JsonOptions);
    }

    public IReadOnlyList<RunLiveEvent> ReadAfter(string repoPath, int runId, long afterSequence)
    {
        var path = PathFor(repoPath, runId);
        if (!File.Exists(path)) return Array.Empty<RunLiveEvent>();

        var result = new List<RunLiveEvent>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<RunLiveEvent>(line, JsonOptions);
                    if (item is not null && item.Sequence > afterSequence) result.Add(item);
                }
                catch (JsonException)
                {
                    // The writer may be between bytes of the final line. The next poll re-reads it.
                }
            }
        }
        catch (IOException)
        {
            return Array.Empty<RunLiveEvent>();
        }
        return result.OrderBy(item => item.Sequence).ToList();
    }

    public static string KindFor(string message)
    {
        if (message.StartsWith("still running - last output ", StringComparison.Ordinal)) return "heartbeat";
        if (message.StartsWith("[agent]", StringComparison.Ordinal)) return "agent";
        if (message.StartsWith("[command]", StringComparison.Ordinal)) return "command";
        if (message.StartsWith("[file]", StringComparison.Ordinal)) return "file";
        if (message.StartsWith("[tool]", StringComparison.Ordinal)) return "tool";
        if (message.StartsWith("[error]", StringComparison.Ordinal)) return "error";
        if (message.StartsWith("[complete]", StringComparison.Ordinal)) return "provider-complete";
        return "lifecycle";
    }

    public sealed class RunEventSink : IDisposable
    {
        private readonly object _gate = new();
        private readonly StreamWriter _writer;
        private readonly JsonSerializerOptions _jsonOptions;
        private long _sequence;
        private bool _disposed;

        internal RunEventSink(string path, long lastSequence, JsonSerializerOptions jsonOptions)
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _sequence = lastSequence;
            _jsonOptions = jsonOptions;
        }

        public RunLiveEvent Append(string kind, string message, bool terminal = false)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RunEventSink));
                var item = new RunLiveEvent(++_sequence, DateTimeOffset.UtcNow, kind, message, terminal);
                _writer.WriteLine(JsonSerializer.Serialize(item, _jsonOptions));
                return item;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _writer.Dispose();
            }
        }
    }
}
