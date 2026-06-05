namespace AutoDev.Core.Models;

public sealed record TaskSelectionResult
{
    public TaskItem? SelectedTask { get; init; }
    public bool IsBlocked { get; init; }
    public string? BlockedReason { get; init; }

    public static TaskSelectionResult Selected(TaskItem task) =>
        new() { SelectedTask = task };

    public static TaskSelectionResult Blocked(string reason) =>
        new() { IsBlocked = true, BlockedReason = reason };
}
