using AutoDev.Core.Models;
using TaskStatus = AutoDev.Core.Models.TaskStatus;

namespace AutoDev.Core.Services;

public sealed class TaskSelector
{
    private static readonly string[] BlockedKeywords =
    [
        "secret", "credential", "api key", "private key",
        "cloud deploy", "cloud deployment", "paid api",
        "physical hardware", "user input required",
        "requires user", "interactive", "manual step"
    ];

    public TaskSelectionResult Select(
        IReadOnlyList<TaskItem> backlogItems,
        StatusSummary statusSummary,
        int maxTasksPerRun = 1)
    {
        if (backlogItems.Count == 0)
        {
            return TaskSelectionResult.Blocked("Backlog is empty or could not be parsed.");
        }

        var candidates = backlogItems
            .Where(t => t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress)
            .ToArray();

        if (candidates.Length == 0)
        {
            return TaskSelectionResult.Blocked("No pending or in-progress tasks found in backlog.");
        }

        TaskItem? selected = null;

        if (!string.IsNullOrWhiteSpace(statusSummary.NextRecommendedTaskId))
        {
            selected = candidates.FirstOrDefault(t =>
                string.Equals(t.Id, statusSummary.NextRecommendedTaskId, StringComparison.OrdinalIgnoreCase));
        }

        selected ??= candidates.FirstOrDefault(t => t.Status == TaskStatus.InProgress);
        selected ??= candidates.FirstOrDefault(t => t.Status == TaskStatus.Pending);

        if (selected is null)
        {
            return TaskSelectionResult.Blocked("No eligible task found after filtering.");
        }

        if (IsKeywordBlocked(selected))
        {
            return TaskSelectionResult.Blocked(
                $"Task '{selected.Id}' appears to require secrets, credentials, cloud deployment, or user interaction — blocked for autonomous run.");
        }

        return TaskSelectionResult.Selected(selected);
    }

    private static bool IsKeywordBlocked(TaskItem task)
    {
        var text = (task.Title + " " + (task.BlockedReason ?? "")).ToLowerInvariant();
        return BlockedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
