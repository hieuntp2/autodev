using AutoDevRunner.Config;
using AutoDevRunner.Models;

namespace AutoDevRunner.Providers;

/// <summary>
/// Shared CLI provider: substitutes the prompt into an argument template,
/// runs the process, and classifies the output.
/// </summary>
public abstract class CliProviderBase : IAiProvider
{
    private readonly ProcessRunner _runner;
    private readonly ProviderCliOptions _options;

    protected CliProviderBase(ProcessRunner runner, ProviderCliOptions options)
    {
        _runner = runner;
        _options = options;
    }

    public abstract ProviderKind Kind { get; }
    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Command);

    public async Task<ProviderInvocation> RunAsync(
        string prompt,
        string workingDirectory,
        TimeSpan timeout,
        Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        // Two ways to hand the prompt to the CLI, chosen by the args template:
        //   {PROMPT}      -> inline (escaped). Simple, but limited by OS arg length.
        //   {PROMPT_FILE} -> path to a temp file holding the prompt. Robust for
        //                    large briefs; the CLI reads it (e.g. `codex exec < file`).
        string? promptFile = null;
        string arguments;

        if (_options.Arguments.Contains("{PROMPT_FILE}"))
        {
            promptFile = Path.Combine(Path.GetTempPath(), $"autodev-prompt-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(promptFile, prompt, ct);
            arguments = _options.Arguments.Replace("{PROMPT_FILE}", $"\"{promptFile}\"");
        }
        else
        {
            arguments = _options.Arguments.Replace("{PROMPT}", EscapeForArg(prompt));
        }

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(
                _options.Command, arguments, workingDirectory, timeout, onOutput, ct);
        }
        finally
        {
            if (promptFile is not null)
                try { File.Delete(promptFile); } catch { /* best effort */ }
        }

        var outcome = ProviderOutputAnalyzer.Classify(result.ExitCode, result.TimedOut, result.Combined);
        var usage = ProviderOutputAnalyzer.ExtractUsage(result.Combined);
        var sessionId = ProviderOutputAnalyzer.ExtractSessionId(result.Combined);

        string? reason = outcome switch
        {
            ProviderOutcome.QuotaLimit => ProviderOutputAnalyzer.ExtractResetHint(result.Combined)
                                          ?? "Provider reported quota / rate limit.",
            ProviderOutcome.AuthError => "Provider authentication failed.",
            ProviderOutcome.Timeout => $"Run exceeded the {timeout.TotalMinutes:0} minute limit.",
            ProviderOutcome.Error => $"Provider exited with code {result.ExitCode}.",
            _ => null
        };

        return new ProviderInvocation(outcome, result.Combined, usage, reason, sessionId);
    }

    /// <summary>
    /// Replace embedded double quotes so the argument template's surrounding
    /// quotes survive. Newlines are kept; the OS arg parser handles them.
    /// </summary>
    private static string EscapeForArg(string prompt) =>
        prompt.Replace("\"", "\\\"");
}
