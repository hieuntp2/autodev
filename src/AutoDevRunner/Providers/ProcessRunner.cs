using System.Diagnostics;
using System.Text;

namespace AutoDevRunner.Providers;

public record ProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    ProcessTimeoutKind? TimeoutKind = null,
    TimeSpan? TimeoutLimit = null,
    ProcessTerminalSignal? TerminalSignal = null)
{
    public string Combined => string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}\n{StdErr}";
}

public enum ProcessTerminalKind
{
    Completed,
    Failed
}

public sealed record ProcessTerminalSignal(ProcessTerminalKind Kind, string? Reason = null);

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
        TimeSpan? heartbeatInterval = null,
        Func<string, ProcessTerminalSignal?>? terminalDetector = null,
        TimeSpan? terminalExitGrace = null,
        IReadOnlyDictionary<string, string?>? environment = null)
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
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var startedAt = DateTimeOffset.UtcNow;
        var lastOutputAt = startedAt;
        var outputLock = new object();
        var terminal = new TaskCompletionSource<ProcessTerminalSignal>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Callbacks run on the AsyncStreamReader thread-pool threads: an
        // exception escaping them is an UNHANDLED exception that terminates
        // the entire runner process mid-run (and orphans the CLI child). The
        // agent console must outlive any observer bug, so failures are noted
        // in stderr (once per callback kind) and swallowed.
        var callbackErrorNoted = 0;
        void GuardedCallback(Action action, string kind)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref callbackErrorNoted, 1) == 0)
                    lock (outputLock)
                        stderr.AppendLine($"[runner] {kind} callback threw and was ignored: {ex.Message}");
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputLock)
            {
                stdout.AppendLine(e.Data);
                lastOutputAt = DateTimeOffset.UtcNow;
            }
            GuardedCallback(() => onOutput?.Invoke(e.Data), "output");
            GuardedCallback(() =>
            {
                var signal = terminalDetector?.Invoke(e.Data);
                if (signal is not null) terminal.TrySetResult(signal);
            }, "terminal-detector");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (outputLock)
            {
                stderr.AppendLine(e.Data);
                lastOutputAt = DateTimeOffset.UtcNow;
            }
            GuardedCallback(() => onOutput?.Invoke(e.Data), "output");
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
                var completed = await Task.WhenAny(waitTask, delayTask, terminal.Task);
                if (completed == waitTask)
                {
                    await waitTask;
                    return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
                }

                if (completed == terminal.Task)
                {
                    var signal = await terminal.Task;
                    var grace = terminalExitGrace ?? TimeSpan.FromSeconds(5);
                    var exited = await Task.WhenAny(waitTask, Task.Delay(grace, ct)) == waitTask;
                    if (exited) await waitTask;
                    else
                    {
                        TryKill(process);
                        try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                    }

                    var exitCode = signal.Kind == ProcessTerminalKind.Completed ? 0
                        : process.HasExited && process.ExitCode != 0 ? process.ExitCode : -1;
                    return new ProcessResult(exitCode, stdout.ToString(), stderr.ToString(), TimedOut: false,
                        TerminalSignal: signal);
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
                    GuardedCallback(() => onOutput?.Invoke(
                        $"still running - last output {(int)(now - lastOutputSnapshot).TotalSeconds}s ago"), "heartbeat");
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
