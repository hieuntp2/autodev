namespace AutoDev.Core.Models;

public sealed record ConfigValidationResult
{
    public bool IsValid { get; init; }
    public bool CanProceed { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static ConfigValidationResult Valid(IReadOnlyList<string>? warnings = null) =>
        new() { IsValid = true, CanProceed = true, Warnings = warnings ?? [] };

    public static ConfigValidationResult Invalid(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null) =>
        new() { IsValid = false, CanProceed = false, Errors = errors, Warnings = warnings ?? [] };
}
