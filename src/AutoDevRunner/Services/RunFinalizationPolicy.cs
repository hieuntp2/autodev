using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public static class RunFinalizationPolicy
{
    public static void ApplyTerminalState(
        Project project,
        RunRecord run,
        RunStatus status,
        DateTime finishedAtUtc,
        string stage,
        string risk)
    {
        run.Status = status;
        run.FinishedAt = finishedAtUtc;
        run.Stage = stage;
        run.Risk = risk;

        project.LastRunStatus = status;
        project.LastProvider = run.Provider;
        project.LastRunAt = finishedAtUtc;
        project.LastError = status is RunStatus.Success ? null : run.Reason;
    }

    public static async Task<bool> PersistThenTryOptionalAsync(
        Func<Task> persistTerminal,
        Func<Task> optionalWork,
        Action<Exception> onOptionalError)
    {
        await persistTerminal();
        try
        {
            await optionalWork();
            return true;
        }
        catch (Exception ex)
        {
            onOptionalError(ex);
            return false;
        }
    }
}

public static class ResumeTaskPolicy
{
    public static string? Resolve(string? currentTask, ParsedSummary summary)
    {
        var candidate = summary.NextTask ?? summary.Pending ?? summary.Task;
        if (string.IsNullOrWhiteSpace(candidate)) return currentTask;

        var normalized = candidate.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var first = normalized.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? normalized;
        return first.Length <= 500 ? first : first[..500];
    }
}
