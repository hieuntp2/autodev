namespace AutoDev.Verification;

public sealed record VerificationSummary
{
    public IReadOnlyList<CommandResult> BuildResults { get; init; } = [];
    public IReadOnlyList<CommandResult> TestResults { get; init; } = [];
    public bool BuildPassed => BuildResults.Count == 0 || BuildResults.All(result => result.Passed);
    public bool TestPassed => TestResults.Count == 0 || TestResults.All(result => result.Passed);
}
