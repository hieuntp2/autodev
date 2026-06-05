namespace AutoDev.Core.Models;

public sealed record RunMetadata
{
    public required string ProjectId { get; init; }
    public required string RunDate { get; init; }
    public required string Branch { get; init; }
    public string Status { get; init; } = "created";
    public string? CommitBefore { get; init; }
    public string? CommitAfter { get; init; }
    public string PlannerModel { get; init; } = "gpt-4.1";
    public string Implementer { get; init; } = "codex-cli";
    public bool BuildPassed { get; init; }
    public bool TestPassed { get; init; }
    public int FilesChangedCount { get; init; }
    public int LinesAdded { get; init; }
    public int LinesDeleted { get; init; }
    public bool RequirementProposalCreated { get; init; }
    public IReadOnlyList<string> CompletedSteps { get; init; } = [];
    public string? FailedStep { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
}
