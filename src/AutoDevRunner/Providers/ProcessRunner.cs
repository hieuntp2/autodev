using System.Diagnostics;
using System.Text;

namespace AutoDevRunner.Providers;

public record ProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    ProcessTimeoutKind? TimeoutKind = null,
    TimeSpan? TimeoutLimit = null)
{
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
}

/// <summary>
/// Thin wrapper around <see cref="Process"/> that captures stdout/stderr,
/// enforces a timeout, and streams output to a logger callback.
/// </summary>
public class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        Action<string>? onOutput = null,
        CancellationToken ct = default,
        string? stdin = null,
        TimeSpan? idleTimeout = null,
        TimeSpan? heartbeatInterval = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (stdin is not null)
            psi.StandardInputEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var startedAt = DateTimeOffset.UtcNow;
        var lastOutputAt = startedAt;
        var outputLock = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputLock)
            {
                stdout.AppendLine(e.Data);
                lastOutputAt = DateTimeOffset.UtcNow;
            }
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputLock)
            {
                stderr.AppendLine(e.Data);
                lastOutputAt = DateTimeOffset.UtcNow;
            }
            onOutput?.Invoke(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, $"Failed to start '{fileName}': {ex.Message}", false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdin is not null)
        {
            try
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close(); // EOF so the CLI stops reading
            }
            catch (IOException)
            {
                // Process may have exited before consuming stdin; the exit
                // code/output classification below reports what happened.
            }
        }

        var waitTask = process.WaitForExitAsync(ct);
        var nextHeartbeatAt = heartbeatInterval is { TotalMilliseconds: > 0 }
            ? DateTimeOffset.UtcNow.Add(heartbeatInterval.Value)
            : DateTimeOffset.MaxValue;

        try
        {
            while (true)
            {
                var delayTask = Task.Delay(TimeSpan.FromSeconds(1), ct);
                var completed = await Task.WhenAny(waitTask, delayTask);
                if (completed == waitTask)
                {
                    await waitTask;
                    return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
                }

                var now = DateTimeOffset.UtcNow;
                if (now - startedAt >= timeout)
                {
                    TryKill(process);
                    return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true,
                        ProcessTimeoutKind.HardBackstop, timeout);
                }

                DateTimeOffset lastOutputSnapshot;
                lock (outputLock) lastOutputSnapshot = lastOutputAt;

                if (idleTimeout is { TotalMilliseconds: > 0 }
                    && ProcessTimeoutPolicy.CheckIdle(lastOutputSnapshot, now, idleTimeout.Value)
                        == ProcessWatchdogDecision.KillForIdle)
                {
                    TryKill(process);
                    return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true,
                        ProcessTimeoutKind.Idle, idleTimeout.Value);
                }

                if (now >= nextHeartbeatAt)
                {
                    onOutput?.Invoke($"still running - last output {(int)(now - lastOutputSnapshot).TotalSeconds}s ago");
                    nextHeartbeatAt = now.Add(heartbeatInterval!.Value);
                }
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true,
                ProcessTimeoutKind.HardBackstop, timeout);
        }
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
