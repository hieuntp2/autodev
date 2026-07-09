using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Providers;

/// <summary>Adapter for the Claude CLI.</summary>
public class ClaudeCliProvider : CliProviderBase
{
    public ClaudeCliProvider(ProcessRunner runner, IOptions<AutoDevOptions> options)
        : base(runner, options.Value.Providers.Claude) { }

    public override ProviderKind Kind => ProviderKind.Claude;

    protected override ProviderInvocation BuildInvocation(ProcessResult result, TimeSpan timeout)
    {
        var parsed = ClaudeJsonOutput.Parse(result.StdOut);
        if (!parsed.IsJsonEnvelope)
            return base.BuildInvocation(result, timeout);

        var outcome = ProviderOutputAnalyzer.Classify(result.ExitCode, result.TimedOut, result.Combined);
        var usage = FormatUsage(parsed.InputTokens, parsed.OutputTokens, parsed.CostUsd)
                    ?? ProviderOutputAnalyzer.ExtractUsage(result.Combined);
        var sessionId = parsed.SessionId ?? ProviderOutputAnalyzer.ExtractSessionId(result.Combined);
        var reason = BuildReason(outcome, result.ExitCode, result.Combined, timeout);

        return new ProviderInvocation(
            outcome,
            parsed.Text,
            usage,
            reason,
            sessionId,
            parsed.InputTokens,
            parsed.OutputTokens,
            parsed.CostUsd,
            parsed.Model);
    }

    private static string? FormatUsage(int? inputTokens, int? outputTokens, decimal? costUsd)
    {
        var parts = new List<string>();
        if (inputTokens is not null || outputTokens is not null)
            parts.Add($"in {inputTokens?.ToString() ?? "?"} / out {outputTokens?.ToString() ?? "?"}");
        if (costUsd is not null)
            parts.Add($"${costUsd.Value:0.######}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
