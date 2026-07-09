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

            run.Status = RunStatus.Failed;
            run.FinishedAt = finishedAtUtc;
            run.Reason = OrphanedReason;
            count++;
        }
        return count;
    }
}
