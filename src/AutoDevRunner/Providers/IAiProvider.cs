using AutoDevRunner.Models;

namespace AutoDevRunner.Providers;

public enum ProviderOutcome
{
    Success,
    QuotaLimit,
    AuthError,
    Error,
    Timeout
}

public record ProviderInvocation(
    ProviderOutcome Outcome,
    string Output,
    string? Usage,
    string? Reason,
    string? SessionId,
    int? InputTokens = null,
    int? OutputTokens = null,
    decimal? CostUsd = null,
    string? Model = null);

/// <summary>An AI CLI adapter (Codex, Claude, ...).</summary>
public interface IAiProvider
{
    ProviderKind Kind { get; }
    bool IsEnabled { get; }

    /// <summary>
    /// Run the AI against the given prompt in <paramref name="workingDirectory"/>.
    /// Never throws for normal provider failures — those are reported via the outcome.
    /// </summary>
    Task<ProviderInvocation> RunAsync(
        string prompt,
        string workingDirectory,
        TimeSpan timeout,
        Action<string>? onOutput = null,
        CancellationToken ct = default,
        TimeSpan? idleTimeout = null,
        TimeSpan? heartbeatInterval = null);
}
