using System.Diagnostics;
using AutoDevRunner.Providers;
using Xunit;

namespace AutoDevRunner.Tests;

public class ProcessTerminalPolicyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ad-terminal-" + Guid.NewGuid().ToString("N"));

    public ProcessTerminalPolicyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Completed_terminal_line_wins_over_a_process_that_does_not_exit()
    {
        if (!OperatingSystem.IsWindows()) return;

        var script = Path.Combine(_dir, "terminal-then-hang.ps1");
        await File.WriteAllTextAsync(script, """
            Write-Output '{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}'
            Start-Sleep -Seconds 30
            """);
        var runner = new ProcessRunner();
        var sw = Stopwatch.StartNew();

        var result = await runner.RunAsync(
            "powershell",
            $"-NoProfile -File \"{script}\"",
            _dir,
            TimeSpan.FromSeconds(20),
            terminalDetector: CodexJsonOutput.DetectTerminal,
            terminalExitGrace: TimeSpan.FromMilliseconds(100));

        sw.Stop();
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ProcessTerminalKind.Completed, result.TerminalSignal?.Kind);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"terminal completion took {sw.Elapsed}");
    }
}
