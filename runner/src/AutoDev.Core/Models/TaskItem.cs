namespace AutoDev.Core.Models;

public enum TaskStatus
{
    Pending,
    InProgress,
    Done,
    Blocked,
    Skipped
}

public sealed record TaskItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public TaskStatus Status { get; init; } = TaskStatus.Pending;
    public string? BlockedReason { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int OriginalLineIndex { get; init; }
}
