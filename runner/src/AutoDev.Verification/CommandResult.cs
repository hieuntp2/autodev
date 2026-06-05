namespace AutoDev.Verification;

public sealed record CommandResult
{
    public required string Command { get; init; }
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required TimeSpan Duration { get; init; }
    public bool Passed => ExitCode == 0;
}
