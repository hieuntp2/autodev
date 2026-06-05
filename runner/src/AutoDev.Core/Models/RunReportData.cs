namespace AutoDev.Core.Models;

public sealed record RunReportData
{
    public DateTimeOffset RunTimestamp { get; init; }
    public string? TaskId { get; init; }
    public string? TaskTitle { get; init; }
    public IReadOnlyList<string> FilesChanged { get; init; } = [];
    public bool BuildPassed { get; init; }
    public bool? TestPassed { get; init; }
    public string? CommitHash { get; init; }
    public string? NextTaskTitle { get; init; }
    public string? BlockedReason { get; init; }
    public IReadOnlyList<string> RemainingRisks { get; init; } = [];
    public string? BuildOutput { get; init; }
}
