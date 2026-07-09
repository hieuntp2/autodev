using AutoDevRunner.Config;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public sealed record ResumeDecision(bool ShouldResume, string? SessionId, int SessionRuns);

public static class ResumePolicy
{
    public static ResumeDecision Decide(ResumeOptions options, string? sessionId,
        ProviderKind provider, ProviderKind? lastProvider, string? currentTask, string? lastTask,
        int resumedRuns)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(sessionId))
            return new ResumeDecision(false, null, 0);

        if (lastProvider is not null && lastProvider != provider)
            return new ResumeDecision(false, null, 0);

        if (options.ResetOnTaskChange
            && !SameTask(currentTask, lastTask))
        {
            return new ResumeDecision(false, null, 0);
        }

        if (resumedRuns >= Math.Max(0, options.MaxResumedRuns))
            return new ResumeDecision(false, null, 0);

        return new ResumeDecision(true, sessionId, resumedRuns + 1);
    }

    private static bool SameTask(string? currentTask, string? lastTask)
    {
        if (string.IsNullOrWhiteSpace(currentTask) || string.IsNullOrWhiteSpace(lastTask))
            return true;
        return RunLessons.Norm(currentTask) == RunLessons.Norm(lastTask);
    }
}
