namespace AutoDev.Core.Models;

public sealed record TargetRepoContext
{
    public string AgentRules { get; init; } = string.Empty;
    public string MainRequirement { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string Roadmap { get; init; } = string.Empty;
    public string Backlog { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<string> MissingDocPaths { get; init; } = [];
}
