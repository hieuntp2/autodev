using AutoDevRunner.Providers;
using Xunit;

namespace AutoDevRunner.Tests;

public class ClaudeStreamJsonOutputTests
{
    private const string SuccessStream =
        """
        {"type":"system","subtype":"init","session_id":"sess-123","model":"claude-sonnet-5","tools":["Bash"]}
        {"type":"assistant","message":{"content":[{"type":"text","text":"Working on it."}]},"session_id":"sess-123"}
        {"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"dotnet build"}}]},"session_id":"sess-123"}
        {"type":"user","message":{"content":[{"type":"tool_result","content":"ok"}]},"session_id":"sess-123"}
        {"type":"result","subtype":"success","is_error":false,"result":"SUMMARY: done","session_id":"sess-123","total_cost_usd":0.1234,"usage":{"input_tokens":10,"cache_creation_input_tokens":20,"cache_read_input_tokens":30,"output_tokens":40}}
        """;

    [Fact]
    public void Parses_success_stream()
    {
        var r = ClaudeStreamJsonOutput.Parse(SuccessStream);

        Assert.True(r.IsStreamJson);
        Assert.Equal(ClaudeTerminalKind.Completed, r.TerminalKind);
        Assert.Equal("SUMMARY: done", r.FinalMessage);
        Assert.Equal("sess-123", r.SessionId);
        Assert.Equal(60, r.InputTokens); // input + cache_creation + cache_read
        Assert.Equal(40, r.OutputTokens);
        Assert.Equal(0.1234m, r.CostUsd);
        Assert.Equal("claude-sonnet-5", r.Model);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parses_error_result()
    {
        var r = ClaudeStreamJsonOutput.Parse(
            """
            {"type":"system","subtype":"init","session_id":"s1","model":"claude-sonnet-5"}
            {"type":"result","subtype":"error_during_execution","is_error":true,"result":"Something broke","session_id":"s1"}
            """);

        Assert.Equal(ClaudeTerminalKind.Failed, r.TerminalKind);
        Assert.Equal("Something broke", r.Error);
    }

    [Fact]
    public void Plain_text_output_is_not_a_stream()
    {
        var r = ClaudeStreamJsonOutput.Parse("hello\nworld");

        Assert.False(r.IsStreamJson);
        Assert.Equal(ClaudeTerminalKind.None, r.TerminalKind);
    }

    [Fact]
    public void Legacy_single_envelope_still_parses()
    {
        // The old --output-format json envelope also has type "result"; it IS
        // handled by the stream parser (same shape), which keeps both formats working.
        var r = ClaudeStreamJsonOutput.Parse(
            """{"type":"result","subtype":"success","is_error":false,"result":"done","session_id":"s2","total_cost_usd":0.01,"usage":{"input_tokens":1,"output_tokens":2}}""");

        Assert.True(r.IsStreamJson);
        Assert.Equal(ClaudeTerminalKind.Completed, r.TerminalKind);
        Assert.Equal("done", r.FinalMessage);
    }

    [Fact]
    public void Detects_terminal_only_on_result_events()
    {
        Assert.Null(ClaudeStreamJsonOutput.DetectTerminal(
            """{"type":"assistant","message":{"content":[{"type":"text","text":"hi"}]}}"""));
        Assert.Null(ClaudeStreamJsonOutput.DetectTerminal("not json"));

        var ok = ClaudeStreamJsonOutput.DetectTerminal(
            """{"type":"result","subtype":"success","is_error":false,"result":"done"}""");
        Assert.Equal(ProcessTerminalKind.Completed, ok!.Kind);

        var failed = ClaudeStreamJsonOutput.DetectTerminal(
            """{"type":"result","subtype":"error_max_turns","is_error":true,"result":"ran out of turns"}""");
        Assert.Equal(ProcessTerminalKind.Failed, failed!.Kind);
        Assert.Equal("ran out of turns", failed.Reason);
    }

    [Fact]
    public void Formats_live_lines_and_drops_noise()
    {
        Assert.Equal("[session] sess-123 (claude-sonnet-5)", ClaudeStreamJsonOutput.FormatLiveLine(
            """{"type":"system","subtype":"init","session_id":"sess-123","model":"claude-sonnet-5"}"""));
        Assert.Equal("[agent] Working on it.", ClaudeStreamJsonOutput.FormatLiveLine(
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Working on it."}]}}"""));
        Assert.Equal("[tool] Bash: dotnet build", ClaudeStreamJsonOutput.FormatLiveLine(
            """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"dotnet build"}}]}}"""));
        // Tool results are noise; non-JSON lines pass through untouched.
        Assert.Null(ClaudeStreamJsonOutput.FormatLiveLine(
            """{"type":"user","message":{"content":[{"type":"tool_result","content":"ok"}]}}"""));
        Assert.Equal("raw line", ClaudeStreamJsonOutput.FormatLiveLine("raw line"));
    }

    [Fact]
    public void Null_numeric_fields_do_not_throw()
    {
        // Guard against the codex-style "value: null" hazard in output callbacks.
        var r = ClaudeStreamJsonOutput.Parse(
            """{"type":"result","subtype":"success","is_error":false,"result":"done","total_cost_usd":null,"usage":{"input_tokens":null,"output_tokens":null}}""");

        Assert.Equal(ClaudeTerminalKind.Completed, r.TerminalKind);
        Assert.Null(r.CostUsd);
        Assert.Null(r.InputTokens);
        Assert.Null(r.OutputTokens);
    }
}
