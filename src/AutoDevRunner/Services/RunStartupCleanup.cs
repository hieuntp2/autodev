using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public static class RunStartupCleanup
{
    public const string OrphanedReason = "orphaned: runner restarted mid-run";

    public static int MarkOrphanedRuns(IEnumerable<RunRecord> runs, DateTime finishedAtUtc)
    {
        var count = 0;
        foreach (var run in runs)
        {
            if (run.FinishedAt is not null) continue;
            if (run.Status is not (RunStatus.Running or RunStatus.Pending)) continue;

            run.Status = RunStatus.Paused;
            run.FinishedAt = finishedAtUtc;
            run.Reason = OrphanedReason;
            if (string.IsNullOrWhiteSpace(run.Stage)
                || run.Stage == nameof(LifecycleStage.Planned)
                || run.Stage == nameof(LifecycleStage.Running))
            {
                run.Stage = nameof(LifecycleStage.Reported);
            }
            count++;
        }
        return count;
    }

    public static int ReconcileOrphanedProjects(IEnumerable<Project> projects, DateTime finishedAtUtc)
    {
        var count = 0;
        foreach (var project in projects)
        {
            if (project.LastRunStatus is not (RunStatus.Running or RunStatus.Pending)) continue;

            project.LastRunStatus = RunStatus.Paused;
            project.LastRunAt = finishedAtUtc;
            if (string.IsNullOrWhiteSpace(project.LastError)) project.LastError = OrphanedReason;
            count++;
        }
        return count;
    }

    public static int SanitizeResumeTasks(IEnumerable<Project> projects)
    {
        var count = 0;
        foreach (var project in projects)
        {
            var task = project.CurrentTask;
            if (string.IsNullOrWhiteSpace(task) || task.Length <= 500) continue;

            var first = task.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0) ?? task.Trim();
            project.CurrentTask = first.Length <= 500 ? first : first[..500];
            count++;
        }
        return count;
    }
}
