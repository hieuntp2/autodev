using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public static class SessionResultFormatter
{
    public const string NotVerifiable = "NOT VERIFIABLE — no validation command configured or inferable";

    public static string Build(RunRecord run, string? validationCommand)
    {
        if (!run.ValidationRun)
        {
            if (!string.IsNullOrWhiteSpace(run.Reason)
                && !string.Equals(run.Reason, NotVerifiable, StringComparison.Ordinal))
            {
                return $"FAILED — {run.Reason!.Trim()}";
            }
            return NotVerifiable;
        }

        if (run.ValidationPassed)
            return $"SUCCESS — validation passed ({validationCommand ?? "validation"})";

        var reason = !string.IsNullOrWhiteSpace(run.Reason)
            ? run.Reason!.Trim()
            : "validation failed";
        return $"FAILED — {reason}";
    }
}
