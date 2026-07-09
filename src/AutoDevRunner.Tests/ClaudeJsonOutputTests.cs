using AutoDevRunner.Providers;
using AutoDevRunner.Config;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDevRunner.Tests;

public class ClaudeJsonOutputTests
{
    [Fact]
    public void Valid_envelope_extracts_result_usage_session_and_model()
    {
        const string raw = """
            {
              "type": "result",
              "subtype": "success",
              "result": "=== AUTODEV SUMMARY ===\nTASK: Measure usage",
              "session_id": "11111111-2222-3333-4444-555555555555",
              "total_cost_usd": 0.012345,
              "model": "claude-sonnet-5",
              "usage": {
                "input_tokens": 1234,
                "cache_creation_input_tokens": 50,
                "cache_read_input_tokens": 75,
                "output_tokens": 234
              }
            }
            """;

        var parsed = ClaudeJsonOutput.Parse(raw);

        Assert.True(parsed.IsJsonEnvelope);
        Assert.Equal("=== AUTODEV SUMMARY ===\nTASK: Measure usage", parsed.Text);
        Assert.Equal("11111111-2222-3333-4444-555555555555", parsed.SessionId);
        Assert.Equal(1359, parsed.InputTokens);
        Assert.Equal(234, parsed.OutputTokens);
        Assert.Equal(0.012345m, parsed.CostUsd);
        Assert.Equal("claude-sonnet-5", parsed.Model);
    }

    [Fact]
    public void Plain_text_falls_back_to_raw_output()
    {
        const string raw = "plain provider output";

        var parsed = ClaudeJsonOutput.Parse(raw);

        Assert.False(parsed.IsJsonEnvelope);
        Assert.Equal(raw, parsed.Text);
        Assert.Null(parsed.SessionId);
        Assert.Null(parsed.InputTokens);
        Assert.Null(parsed.OutputTokens);
        Assert.Null(parsed.CostUsd);
        Assert.Null(parsed.Model);
    }

    [Fact]
    public void Claude_provider_parses_json_envelope_from_stdout_when_stderr_has_warnings()
    {
        const string stdout = """
            {
              "type": "result",
              "subtype": "success",
              "result": "=== AUTODEV SUMMARY ===\nTASK: Fix stderr parsing",
              "session_id": "session-123",
              "total_cost_usd": 0.02,
              "model": "claude-sonnet-5",
              "usage": {
                "input_tokens": 10,
                "output_tokens": 5
              }
            }
            """;
        const string stderr = "warning: stderr notice";

        var provider = new TestClaudeCliProvider();
        var invocation = provider.Build(new ProcessResult(0, stdout, stderr, TimedOut: false));

        Assert.Equal(ProviderOutcome.Success, invocation.Outcome);
        Assert.Equal("=== AUTODEV SUMMARY ===\nTASK: Fix stderr parsing", invocation.Output);
        Assert.Equal("session-123", invocation.SessionId);
        Assert.Equal(10, invocation.InputTokens);
        Assert.Equal(5, invocation.OutputTokens);
        Assert.Equal(0.02m, invocation.CostUsd);
        Assert.Equal("claude-sonnet-5", invocation.Model);
    }

    private sealed class TestClaudeCliProvider : ClaudeCliProvider
    {
        public TestClaudeCliProvider()
            : base(new ProcessRunner(), Options.Create(new AutoDevOptions())) { }

        public ProviderInvocation Build(ProcessResult result) =>
            BuildInvocation(result, TimeSpan.FromMinutes(30));
    }
}
