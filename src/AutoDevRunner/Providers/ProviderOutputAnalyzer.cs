using System.Text.RegularExpressions;

namespace AutoDevRunner.Providers;

/// <summary>
/// Heuristic classification of AI CLI output. The CLIs don't expose a stable
/// machine-readable status, so we pattern-match stdout/stderr for known
/// quota / rate-limit / auth signals and any usage figures.
/// </summary>
public static class ProviderOutputAnalyzer
{
    private static readonly string[] QuotaSignals =
    {
        "quota", "rate limit", "rate-limit", "ratelimit", "too many requests",
        "429", "usage limit", "out of credits", "insufficient_quota",
        "exceeded your current quota", "you have hit your", "limit reached",
        "overloaded", "capacity"
    };

    private static readonly string[] AuthSignals =
    {
        "unauthorized", "401", "authentication", "not logged in", "please log in",
        "please login", "invalid api key", "no api key", "auth error",
        "credentials", "forbidden", "403", "session expired", "run /login",
        "please run", "login required"
    };

    public static ProviderOutcome Classify(int exitCode, bool timedOut, string output)
    {
        if (timedOut) return ProviderOutcome.Timeout;

        var lower = output.ToLowerInvariant();

        // Auth checked before quota: an auth failure is more actionable.
        if (ContainsAny(lower, AuthSignals)) return ProviderOutcome.AuthError;
        if (ContainsAny(lower, QuotaSignals)) return ProviderOutcome.QuotaLimit;

        return exitCode == 0 ? ProviderOutcome.Success : ProviderOutcome.Error;
    }

    private static bool ContainsAny(string text, string[] needles)
    {
        foreach (var n in needles)
            if (text.Contains(n, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Best-effort extraction of token/cost usage. Returns null if nothing found.</summary>
    public static string? ExtractUsage(string output)
    {
        var parts = new List<string>();

        var tokens = Regex.Match(output,
            @"(?<n>[\d,\.]+)\s*(?:total\s*)?tokens", RegexOptions.IgnoreCase);
        if (tokens.Success) parts.Add($"{tokens.Groups["n"].Value} tokens");

        var cost = Regex.Match(output, @"\$\s?(?<n>[\d]+\.[\d]{2,6})", RegexOptions.IgnoreCase);
        if (cost.Success) parts.Add($"${cost.Groups["n"].Value}");

        var inOut = Regex.Match(output,
            @"input[:\s]+(?<in>[\d,]+).*?output[:\s]+(?<out>[\d,]+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (inOut.Success)
            parts.Add($"in {inOut.Groups["in"].Value} / out {inOut.Groups["out"].Value}");

        return parts.Count == 0 ? null : string.Join(", ", parts.Distinct());
    }

    /// <summary>Try to extract a quota reset hint (e.g. "resets in 3h", "try again at ...").</summary>
    public static string? ExtractResetHint(string output)
    {
        var m = Regex.Match(output,
            @"(reset[s]?\s+(in|at|after)\s+[^\.\n]{1,40}|try again (in|at|after)\s+[^\.\n]{1,40})",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Value.Trim() : null;
    }

    /// <summary>Try to extract a CLI session id for native resume.</summary>
    public static string? ExtractSessionId(string output)
    {
        var m = Regex.Match(output,
            @"session[_\s-]?id[:\s""]+(?<id>[A-Za-z0-9\-_]{6,})",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["id"].Value : null;
    }
}
