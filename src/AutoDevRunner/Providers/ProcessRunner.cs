using System.Diagnostics;
using System.Text;

namespace AutoDevRunner.Providers;

public record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
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
        string? stdin = null)
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

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
