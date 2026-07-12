using AutoDevRunner.Providers;
using Xunit;

namespace AutoDevRunner.Tests;

/// <summary>
/// The output/terminal callbacks run on AsyncStreamReader thread-pool threads:
/// an exception escaping them is an unhandled exception that terminates the
/// whole runner process mid-run (the "Codex stops after a moment" outage of
/// 2026-07-11/12). These tests pin the guarantee that a throwing observer can
/// never take the agent process down or fail the run.
/// </summary>
public class ProcessRunnerResilienceTests
{
    private static Task<ProcessResult> EchoAsync(
        Action<string>? onOutput = null,
        Func<string, ProcessTerminalSignal?>? terminalDetector = null) =>
        new ProcessRunner().RunAsync(
            "cmd.exe", "/c echo first-line& echo second-line",
            Environment.CurrentDirectory, TimeSpan.FromSeconds(30),
            onOutput, terminalDetector: terminalDetector);

    [Fact]
    public async Task Throwing_output_callback_does_not_fail_the_run()
    {
        var result = await EchoAsync(_ => throw new InvalidOperationException("observer bug"));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("first-line", result.StdOut);
        Assert.Contains("second-line", result.StdOut); // reading continued past the throw
        Assert.Contains("output callback threw and was ignored", result.StdErr);
    }

    [Fact]
    public async Task Throwing_terminal_detector_does_not_fail_the_run()
    {
        var result = await EchoAsync(
            terminalDetector: _ => throw new InvalidOperationException("detector bug"));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("first-line", result.StdOut);
        Assert.Contains("callback threw and was ignored", result.StdErr);
    }
}
