using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public sealed record ValidationRepairOutcome(RunStatus Status, int RepairAttempts);

public static class ValidationRepairPolicy
{
    public static bool ShouldAttemptRepair(bool validationRun, bool validationPassed, int attempts, int maxAttempts) =>
        validationRun && !validationPassed && attempts < Math.Max(0, maxAttempts);

    public static RunStatus DetermineStatus(bool validationRun, bool validationPassed, bool hasChanges = false)
    {
        if (validationRun)
            return validationPassed ? RunStatus.Success : RunStatus.Failed;

        return hasChanges ? RunStatus.Success : RunStatus.Failed;
    }

    public static ValidationRepairOutcome Evaluate(IReadOnlyList<bool> validationResults, int maxRepairAttempts)
    {
        if (validationResults.Count == 0)
            return new ValidationRepairOutcome(RunStatus.Failed, 0);

        var attempts = 0;
        var last = validationResults[0];
        for (var i = 1; !last && i < validationResults.Count && attempts < maxRepairAttempts; i++)
        {
            attempts++;
            last = validationResults[i];
        }

        return new ValidationRepairOutcome(DetermineStatus(validationRun: true, validationPassed: last), attempts);
    }
}
