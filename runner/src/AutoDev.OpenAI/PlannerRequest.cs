using AutoDev.Core.Models;

namespace AutoDev.OpenAI;

public sealed class PlannerRequest
{
    public required ProjectConfig Project { get; init; }
    public required string WorkspacePath { get; init; }
    public required string ProjectContext { get; init; }
    public required string Template { get; init; }
    public required string Prompt { get; init; }
}
