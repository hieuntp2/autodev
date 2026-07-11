using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;

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
        CancellationToken ct = default,
        TimeSpan? idleTimeout = null,
        TimeSpan? heartbeatInterval = null,
        TaskTier tier = TaskTier.Standard,
        bool modelRoutingEnabled = false,
        string? resumeSessionId = null,
        bool resumeEnabled = false)
    {
        // Three ways to hand the prompt to the CLI, chosen by the args template:
        //   no placeholder -> stdin (default). No OS arg-length limit — required:
        //                     prompts with brief + creative plan + resume context
        //                     can exceed the 32K Windows command-line cap.
        //   {PROMPT}      -> inline (escaped). Simple, but limited by arg length.
        //   {PROMPT_FILE} -> path to a temp file holding the prompt, for CLIs
        //                    that take a file path argument.
        string? promptFile = null;
        string? stdin = null;
        string arguments;

        var argumentTemplate = _options.ResolveArguments(tier, modelRoutingEnabled, resumeSessionId, resumeEnabled);

        if (argumentTemplate.Contains("{PROMPT_FILE}"))
        {
            promptFile = Path.Combine(Path.GetTempPath(), $"autodev-prompt-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(promptFile, prompt, ct);
            arguments = argumentTemplate.Replace("{PROMPT_FILE}", $"\"{promptFile}\"");
        }
        else if (argumentTemplate.Contains("{PROMPT}"))
        {
            arguments = argumentTemplate.Replace("{PROMPT}", EscapeForArg(prompt));
        }
        else
        {
            arguments = argumentTemplate;
            stdin = prompt;
        }

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(
                _options.Command, arguments, workingDirectory, timeout, WrapOutput(onOutput), ct, stdin,
                idleTimeout, heartbeatInterval, TerminalDetector);
        }
        finally
        {
            if (promptFile is not null)
                try { File.Delete(promptFile); } catch { /* best effort */ }
        }

        var invocation = BuildInvocation(result, timeout);
        return string.IsNullOrWhiteSpace(invocation.Model)
            ? invocation with { Model = ExtractModel(arguments) }
            : invocation;
    }

    protected virtual Func<string, ProcessTerminalSignal?>? TerminalDetector => null;

    protected virtual Action<string>? WrapOutput(Action<string>? onOutput) => onOutput;

    protected virtual ProviderInvocation BuildInvocation(ProcessResult result, TimeSpan timeout)
    {
        var outcome = ProviderOutputAnalyzer.Classify(result.ExitCode, result.TimedOut, result.Combined);
        var usage = ProviderOutputAnalyzer.ExtractUsage(result.Combined);
        var sessionId = ProviderOutputAnalyzer.ExtractSessionId(result.Combined);
        var reason = BuildReason(outcome, result.ExitCode, result.Combined, timeout,
            result.TimeoutKind, result.TimeoutLimit);

        return new ProviderInvocation(outcome, result.Combined, usage, reason, sessionId);
    }

    protected static string? BuildReason(
        ProviderOutcome outcome,
        int exitCode,
        string output,
        TimeSpan timeout,
        ProcessTimeoutKind? timeoutKind = null,
        TimeSpan? timeoutLimit = null) =>
        outcome switch
        {
            ProviderOutcome.QuotaLimit => ProviderOutputAnalyzer.ExtractResetHint(output)
                                          ?? "Provider reported quota / rate limit.",
            ProviderOutcome.AuthError => "Provider authentication failed.",
            ProviderOutcome.Timeout => ProcessTimeoutPolicy.BuildReason(
                timeoutKind ?? ProcessTimeoutKind.HardBackstop, timeoutLimit ?? timeout),
            ProviderOutcome.Error => $"Provider exited with code {exitCode}.",
            _ => null
        };

    /// <summary>
    /// Replace embedded double quotes so the argument template's surrounding
    /// quotes survive. Newlines are kept; the OS arg parser handles them.
    /// </summary>
    private static string EscapeForArg(string prompt) =>
        prompt.Replace("\"", "\\\"");

    private static string? ExtractModel(string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if ((parts[i] == "-m" || parts[i] == "--model") && i + 1 < parts.Length)
                return parts[i + 1].Trim('"');
            if (parts[i].StartsWith("--model=", StringComparison.Ordinal))
                return parts[i]["--model=".Length..].Trim('"');
        }
        return null;
    }
}
