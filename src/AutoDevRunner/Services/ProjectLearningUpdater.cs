using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public static class ProjectLearningUpdater
{
    public static void Apply(
        ProjectLearningState state,
        IList<ProjectTaskStat> taskStats,
        RunRecord run,
        IReadOnlyList<RunStatus> rollingWindowStatuses,
        DateTime utcNow)
    {
        var finishedAt = run.FinishedAt ?? utcNow;
        state.TotalRuns++;
        if (run.Status is RunStatus.Success)
        {
            state.Successes++;
            state.LastSuccessAt = finishedAt;
        }
        else if (run.Status is RunStatus.Failed)
        {
            state.Failures++;
            state.LastFailureAt = finishedAt;
        }
        if (run.NotVerified is true)
            state.NotVerified++;

        var rollingCount = rollingWindowStatuses.Count;
        state.RollingSuccessRate = rollingCount == 0
            ? 0
            : rollingWindowStatuses.Count(s => s is RunStatus.Success) / (double)rollingCount;
        state.UpdatedAt = utcNow;

        var title = (run.TaskTitle ?? string.Empty).Trim();
        if (title.Length == 0) return;

        var key = RunLessons.Norm(title);
        var stat = taskStats.FirstOrDefault(s => s.TaskKeyNormalized == key);
        if (stat is null)
        {
            stat = new ProjectTaskStat
            {
                ProjectId = run.ProjectId,
                TaskKeyNormalized = key,
                TaskTitle = title
            };
            taskStats.Add(stat);
        }

        stat.TaskTitle = title;
        stat.Attempts++;
        if (run.Status is RunStatus.Failed)
            stat.Failures++;
        stat.LastOutcome = run.Status.ToString();
        stat.LastAttemptAt = finishedAt;
    }
}
