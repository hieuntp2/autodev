using AutoDevRunner.Providers;
using Xunit;

namespace AutoDevRunner.Tests;

public class CodexJsonOutputTests
{
    [Fact]
    public void Completed_jsonl_extracts_final_message_session_usage_and_live_lines()
    {
        const string stdout = """
            {"type":"thread.started","thread_id":"thread-123"}
            {"type":"turn.started"}
            {"type":"item.started","item":{"id":"cmd-1","type":"command_execution","command":"dotnet test","status":"in_progress"}}
            {"type":"item.completed","item":{"id":"cmd-1","type":"command_execution","command":"dotnet test","status":"completed","exit_code":0,"aggregated_output":"Passed"}}
            {"type":"item.completed","item":{"id":"msg-1","type":"agent_message","text":"=== AUTODEV SUMMARY ===\nTASK: Stream Codex\nNEXT_TASK: none"}}
            {"type":"turn.completed","usage":{"input_tokens":120,"cached_input_tokens":20,"output_tokens":30,"reasoning_output_tokens":5}}
            """;

        var parsed = CodexJsonOutput.Parse(stdout);

        Assert.True(parsed.IsJsonStream);
        Assert.Equal(CodexTerminalKind.Completed, parsed.TerminalKind);
        Assert.Equal("thread-123", parsed.SessionId);
        Assert.Contains("TASK: Stream Codex", parsed.FinalMessage);
        Assert.Equal(120, parsed.InputTokens);
        Assert.Equal(30, parsed.OutputTokens);
        Assert.Contains(parsed.LiveLines, line => line.Contains("dotnet test"));
        Assert.Contains(parsed.LiveLines, line => line.Contains("turn completed"));
    }

    [Fact]
    public void Failed_jsonl_exposes_terminal_error()
    {
        const string stdout = """
            {"type":"thread.started","thread_id":"thread-err"}
            {"type":"turn.failed","error":{"message":"model unavailable"}}
            """;

        var parsed = CodexJsonOutput.Parse(stdout);

        Assert.True(parsed.IsJsonStream);
        Assert.Equal(CodexTerminalKind.Failed, parsed.TerminalKind);
        Assert.Equal("model unavailable", parsed.Error);
        Assert.Equal(ProcessTerminalKind.Failed, CodexJsonOutput.DetectTerminal(stdout.Split('\n')[1])?.Kind);
    }

    [Fact]
    public void Malformed_text_is_not_treated_as_a_json_stream_or_terminal_event()
    {
        const string stdout = "warning: not json\nplain final output";

        var parsed = CodexJsonOutput.Parse(stdout);

        Assert.False(parsed.IsJsonStream);
        Assert.Equal(CodexTerminalKind.None, parsed.TerminalKind);
        Assert.Null(CodexJsonOutput.DetectTerminal("plain output"));
    }

    [Fact]
    public void Reasoning_items_are_not_exposed_as_live_dashboard_lines()
    {
        const string line = "{\"type\":\"item.completed\",\"item\":{\"id\":\"r1\",\"type\":\"reasoning\",\"text\":\"private reasoning\"}}";

        Assert.Null(CodexJsonOutput.FormatLiveLine(line));
    }
}
