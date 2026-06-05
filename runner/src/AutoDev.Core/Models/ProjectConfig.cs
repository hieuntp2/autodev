namespace AutoDev.Core.Models;

public sealed record ProjectConfig
{
    public required string ProjectId { get; init; }
    public required string DisplayName { get; init; }
    public required string RepoPath { get; init; }
    public required string Branch { get; init; }
    public required string ProjectType { get; init; }
    public string? MainRequirementFile { get; init; }
    public string? ScopeFile { get; init; }
    public string? RoadmapFile { get; init; }
    public string? BacklogFile { get; init; }
    public string? StatusFile { get; init; }
    public string? AgentRulesFile { get; init; }
    public IReadOnlyList<string> BuildCommands { get; init; } = [];
    public IReadOnlyList<string> TestCommands { get; init; } = [];
    public IReadOnlyList<string> AllowedWritePaths { get; init; } = [];
    public IReadOnlyList<string> ProtectedPaths { get; init; } = [];
    public ScheduleConfig? Schedule { get; init; }
    public required string DailyGoal { get; init; }
    public int MaxTasksPerRun { get; init; } = 1;
    public int MaxDiffLines { get; init; } = 800;
    public bool AllowRequirementProposal { get; init; } = true;
    public bool AllowRequirementDirectEdit { get; init; }
    public bool AllowAutoCommit { get; init; }
    public bool AllowAutoPush { get; init; }
    public bool CommitOnlyIfBuildPasses { get; init; } = true;
}

public sealed record ScheduleConfig
{
    public bool Enabled { get; init; }
    public string? Mode { get; init; }
    public string? Time { get; init; }
    public string? Timezone { get; init; }
}
