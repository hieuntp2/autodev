using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class CodexUsageReaderTests
{
    [Fact]
    public void Parses_latest_turn_tokens_from_rollout_line()
    {
        const string line = """
            {
              "timestamp": "2026-07-09T12:00:00Z",
              "payload": {
                "info": {
                  "last_token_usage": {
                    "input_tokens": 20451,
                    "cached_input_tokens": 5504,
                    "output_tokens": 794,
                    "reasoning_output_tokens": 516,
                    "total_tokens": 21245
                  },
                  "model_context_window": 258400
                }
              }
            }
            """;

        var usage = CodexUsageReader.TryParseTurnUsageLine(line);

        Assert.NotNull(usage);
        Assert.Equal(20451, usage!.InputTokens);
        Assert.Equal(794, usage.OutputTokens);
        Assert.Null(usage.Model);
    }
}
