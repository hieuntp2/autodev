namespace AutoDev.Core.Models;

public sealed record RunContext
{
    public required ProjectConfig Project { get; init; }
    public required DateOnly RunDate { get; init; }
    public required string WorkspacePath { get; init; }
}
