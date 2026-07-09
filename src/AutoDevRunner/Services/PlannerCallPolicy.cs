using AutoDevRunner.Config;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public static class PlannerCallPolicy
{
    public static bool ShouldCallPlanner(PlannerOptions options, Project project)
    {
        if (!options.Enabled) return false;

        if (options.SkipWhenTaskInProgress
            && !string.IsNullOrWhiteSpace(project.CurrentTask)
            && project.LastRunStatus is RunStatus.Success)
        {
            return false;
        }

        return true;
    }
}
