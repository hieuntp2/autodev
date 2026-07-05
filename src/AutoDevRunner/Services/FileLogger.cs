using System.Text;

namespace AutoDevRunner.Services;

/// <summary>
/// Minimal dependency-free file logger: one rolling file per day
/// (logs/autodev-yyyyMMdd.log). Exists because the app runs headless
/// (WinExe, no console), so the file is the primary way to follow the
/// scheduler, planner, and provider activity. Logging must never take the
/// app down: all IO failures are swallowed.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly object _sync = new();

    public FileLoggerProvider(string directory, LogLevel minLevel)
    {
        _directory = directory;
        _minLevel = minLevel;
        try { Directory.CreateDirectory(directory); } catch { /* logging is best-effort */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(" [").Append(ShortLevel(level)).Append("] ")
            .Append(category).Append(": ")
            .Append(message);
        if (exception is not null)
            line.AppendLine().Append(exception);

        var path = Path.Combine(_directory, $"autodev-{DateTime.Now:yyyyMMdd}.log");
        lock (_sync)
        {
            try
            {
                // FileShare.ReadWrite: the dashboard and a --run-due task may
                // both be appending; tail -f style readers stay possible too.
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.WriteLine(line.ToString());
            }
            catch
            {
                // Never let logging break a run.
            }
        }
    }

    private static string ShortLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => level.ToString().ToUpperInvariant()
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= provider._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
