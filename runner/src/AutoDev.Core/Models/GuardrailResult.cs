namespace AutoDev.Core.Models;

public sealed record GuardrailResult
{
    public bool HasProtectedPathChanges { get; init; }
    public bool IsDiffTooLarge { get; init; }
    public int DiffLines { get; init; }
    public IReadOnlyList<string> ProtectedPathChanges { get; init; } = [];
    public IReadOnlyList<string> DependencyChanges { get; init; } = [];
    public bool CanAutoCommit { get; init; }
}
