using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public sealed record ProjectTaskStatView(
    string TaskTitle,
    int Attempts,
    int Failures,
    string LastOutcome,
    DateTime LastAttemptAt);

public sealed record ProjectSettingChangeView(
    string Key,
    string? OldValue,
    string? NewValue,
    string Source,
    DateTime CreatedAt);

public sealed record ProjectLearningSnapshot(
    int TotalRuns,
    int Successes,
    int Failures,
    int NotVerified,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    double RollingSuccessRate,
    DateTime? UpdatedAt,
    IReadOnlyList<ProjectTaskStatView> RepeatedFailingTasks,
    IReadOnlyList<ProjectSettingChangeView> SettingChanges);

public static class ProjectLearningSurface
{
    public static ProjectLearningSnapshot Build(
        ProjectLearningState? state,
        IEnumerable<ProjectTaskStat> taskStats,
        IEnumerable<ProjectSettingChange> settingChanges,
        int failureThreshold)
    {
        failureThreshold = Math.Max(1, failureThreshold);
        var repeated = taskStats
            .Where(t => t.Failures >= failureThreshold)
            .OrderByDescending(t => t.LastAttemptAt)
            .Select(t => new ProjectTaskStatView(
                t.TaskTitle,
                t.Attempts,
                t.Failures,
                t.LastOutcome,
                t.LastAttemptAt))
            .ToList();

        var changes = settingChanges
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ProjectSettingChangeView(c.Key, c.OldValue, c.NewValue, c.Source, c.CreatedAt))
            .ToList();

        return new ProjectLearningSnapshot(
            TotalRuns: state?.TotalRuns ?? 0,
            Successes: state?.Successes ?? 0,
            Failures: state?.Failures ?? 0,
            NotVerified: state?.NotVerified ?? 0,
            LastSuccessAt: state?.LastSuccessAt,
            LastFailureAt: state?.LastFailureAt,
            RollingSuccessRate: state?.RollingSuccessRate ?? 0,
            UpdatedAt: state?.UpdatedAt,
            RepeatedFailingTasks: repeated,
            SettingChanges: changes);
    }
}
