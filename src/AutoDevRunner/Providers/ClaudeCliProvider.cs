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

    // Streaming (--output-format stream-json --verbose) gives live progress,
    // keeps the idle timeout honest, and ends the run on the "result" event.
    protected override Func<string, ProcessTerminalSignal?>? TerminalDetector =>
        ClaudeStreamJsonOutput.DetectTerminal;

    protected override Action<string>? WrapOutput(Action<string>? onOutput)
    {
        if (onOutput is null) return null;
        return line =>
        {
            var display = ClaudeStreamJsonOutput.FormatLiveLine(line);
            if (!string.IsNullOrWhiteSpace(display)) onOutput(display);
        };
    }

    protected override ProviderInvocation BuildInvocation(ProcessResult result, TimeSpan timeout)
    {
        var stream = ClaudeStreamJsonOutput.Parse(result.StdOut);
        if (stream.IsStreamJson)
            return BuildStreamInvocation(stream, result, timeout);

        // Legacy fallback: single JSON envelope from --output-format json.
        var parsed = ClaudeJsonOutput.Parse(result.StdOut);
        if (!parsed.IsJsonEnvelope)
            return base.BuildInvocation(result, timeout);

        var outcome = ProviderOutputAnalyzer.Classify(result.ExitCode, result.TimedOut, result.Combined);
        var usage = FormatUsage(parsed.InputTokens, parsed.OutputTokens, parsed.CostUsd)
                    ?? ProviderOutputAnalyzer.ExtractUsage(result.Combined);
        var sessionId = parsed.SessionId ?? ProviderOutputAnalyzer.ExtractSessionId(result.Combined);
        var reason = BuildReason(outcome, result.ExitCode, result.Combined, timeout,
            result.TimeoutKind, result.TimeoutLimit);

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

    private ProviderInvocation BuildStreamInvocation(
        ClaudeStreamJsonResult stream, ProcessResult result, TimeSpan timeout)
    {
        var outcome = stream.TerminalKind switch
        {
            ClaudeTerminalKind.Completed => ProviderOutcome.Success,
            ClaudeTerminalKind.Failed => ClassifyFailure(result),
            _ => ProviderOutputAnalyzer.Classify(result.ExitCode, result.TimedOut, result.Combined)
        };
        var reason = outcome switch
        {
            ProviderOutcome.Success => null,
            ProviderOutcome.Timeout => BuildReason(outcome, result.ExitCode, result.Combined, timeout,
                result.TimeoutKind, result.TimeoutLimit),
            _ => stream.Error ?? BuildReason(outcome, result.ExitCode, result.Combined, timeout,
                result.TimeoutKind, result.TimeoutLimit)
        };
        var usage = FormatUsage(stream.InputTokens, stream.OutputTokens, stream.CostUsd)
                    ?? ProviderOutputAnalyzer.ExtractUsage(result.Combined);

        return new ProviderInvocation(
            outcome,
            stream.FinalMessage ?? string.Empty,
            usage,
            reason,
            stream.SessionId,
            stream.InputTokens,
            stream.OutputTokens,
            stream.CostUsd,
            stream.Model);
    }

    private static ProviderOutcome ClassifyFailure(ProcessResult result)
    {
        var classified = ProviderOutputAnalyzer.Classify(
            result.ExitCode == 0 ? -1 : result.ExitCode, result.TimedOut, result.Combined);
        return classified is ProviderOutcome.Success ? ProviderOutcome.Error : classified;
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
