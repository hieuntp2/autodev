using AutoDevRunner.Providers;
using Xunit;

namespace AutoDevRunner.Tests;

public class ProcessTimeoutPolicyTests
{
    [Fact]
    public void Idle_watchdog_keeps_waiting_when_recent_output_is_within_limit()
    {
        var now = new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.Zero);
        var lastOutput = now.AddMinutes(-14);

        var decision = ProcessTimeoutPolicy.CheckIdle(lastOutput, now, TimeSpan.FromMinutes(15));

        Assert.Equal(ProcessWatchdogDecision.KeepWaiting, decision);
    }

    [Fact]
    public void Idle_watchdog_kills_when_no_output_exceeds_limit()
    {
        var now = new DateTimeOffset(2026, 7, 9, 8, 0, 0, TimeSpan.Zero);
        var lastOutput = now.AddMinutes(-16);

        var decision = ProcessTimeoutPolicy.CheckIdle(lastOutput, now, TimeSpan.FromMinutes(15));

        Assert.Equal(ProcessWatchdogDecision.KillForIdle, decision);
    }

    [Theory]
    [InlineData(ProcessTimeoutKind.Idle, 15, "hung: no output for 15 minutes")]
    [InlineData(ProcessTimeoutKind.HardBackstop, 240, "hard time cap (240 minutes) reached")]
    public void Timeout_reasons_distinguish_idle_from_hard_backstop(
        ProcessTimeoutKind kind, int minutes, string expected)
    {
        var reason = ProcessTimeoutPolicy.BuildReason(kind, TimeSpan.FromMinutes(minutes));

        Assert.Equal(expected, reason);
    }
}
