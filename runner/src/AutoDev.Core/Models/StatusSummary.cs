namespace AutoDev.Core.Models;

public sealed record StatusSummary
{
    public string? NextRecommendedTaskId { get; init; }
    public IReadOnlyList<string> InProgressTaskIds { get; init; } = [];
    public IReadOnlyList<string> CompletedTaskIds { get; init; } = [];
    public string? LastRunSummary { get; init; }
}
