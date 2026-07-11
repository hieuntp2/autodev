using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Providers;

/// <summary>Adapter for the Codex CLI (the primary provider).</summary>
public class CodexCliProvider : CliProviderBase
{
    public CodexCliProvider(ProcessRunner runner, IOptions<AutoDevOptions> options)
        : base(runner, options.Value.Providers.Codex) { }

    public override ProviderKind Kind => ProviderKind.Codex;

    protected override Func<string, ProcessTerminalSignal?>? TerminalDetector =>
        CodexJsonOutput.DetectTerminal;

    protected override Action<string>? WrapOutput(Action<string>? onOutput)
    {
        if (onOutput is null) return null;
        return line =>
        {
            var display = CodexJsonOutput.FormatLiveLine(line);
            if (!string.IsNullOrWhiteSpace(display)) onOutput(display);
        };
    }

    protected override ProviderInvocation BuildInvocation(ProcessResult result, TimeSpan timeout)
    {
        var parsed = CodexJsonOutput.Parse(result.StdOut);
        if (!parsed.IsJsonStream) return base.BuildInvocation(result, timeout);

        var outcome = parsed.TerminalKind switch
        {
            CodexTerminalKind.Completed => ProviderOutcome.Success,
            CodexTerminalKind.Failed => ClassifyFailure(result),
            _ => ProviderOutputAnalyzer.Classify(result.ExitCode, result.TimedOut, result.Combined)
        };
        var reason = outcome switch
        {
            ProviderOutcome.Success => null,
            ProviderOutcome.Timeout => BuildReason(outcome, result.ExitCode, result.Combined, timeout,
                result.TimeoutKind, result.TimeoutLimit),
            _ => parsed.Error ?? BuildReason(outcome, result.ExitCode, result.Combined, timeout,
                result.TimeoutKind, result.TimeoutLimit)
        };
        var usage = parsed.InputTokens is null && parsed.OutputTokens is null
            ? ProviderOutputAnalyzer.ExtractUsage(result.Combined)
            : $"in {parsed.InputTokens ?? 0:N0} / out {parsed.OutputTokens ?? 0:N0}";

        return new ProviderInvocation(
            outcome,
            parsed.FinalMessage ?? string.Empty,
            usage,
            reason,
            parsed.SessionId,
            parsed.InputTokens,
            parsed.OutputTokens);
    }

    private static ProviderOutcome ClassifyFailure(ProcessResult result)
    {
        var classified = ProviderOutputAnalyzer.Classify(
            result.ExitCode == 0 ? -1 : result.ExitCode, result.TimedOut, result.Combined);
        return classified is ProviderOutcome.Success ? ProviderOutcome.Error : classified;
    }
}
