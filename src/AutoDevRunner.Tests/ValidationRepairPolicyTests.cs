using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ValidationRepairPolicyTests
{
    [Fact]
    public void Failed_validation_then_retry_pass_results_in_success_with_one_attempt()
    {
        var outcome = ValidationRepairPolicy.Evaluate(new[] { false, true }, maxRepairAttempts: 2);

        Assert.Equal(RunStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.RepairAttempts);
    }

    [Fact]
    public void Failed_validation_then_retry_fail_results_in_failed_with_attempt_recorded()
    {
        var outcome = ValidationRepairPolicy.Evaluate(new[] { false, false }, maxRepairAttempts: 1);

        Assert.Equal(RunStatus.Failed, outcome.Status);
        Assert.Equal(1, outcome.RepairAttempts);
    }

    [Fact]
    public void Validation_that_passes_first_try_does_not_repair()
    {
        var outcome = ValidationRepairPolicy.Evaluate(new[] { true }, maxRepairAttempts: 2);

        Assert.Equal(RunStatus.Success, outcome.Status);
        Assert.Equal(0, outcome.RepairAttempts);
    }

    [Fact]
    public void Missing_validation_without_changes_is_not_success()
    {
        var outcome = ValidationRepairPolicy.Evaluate(Array.Empty<bool>(), maxRepairAttempts: 2);

        Assert.Equal(RunStatus.Failed, outcome.Status);
        Assert.Equal(0, outcome.RepairAttempts);
    }

    [Fact]
    public void Missing_validation_with_changes_is_success_so_it_can_commit()
    {
        var status = ValidationRepairPolicy.DetermineStatus(
            validationRun: false,
            validationPassed: false,
            hasChanges: true);

        Assert.Equal(RunStatus.Success, status);
    }

    [Fact]
    public void Failed_validation_stays_failed_even_with_changes()
    {
        var status = ValidationRepairPolicy.DetermineStatus(
            validationRun: true,
            validationPassed: false,
            hasChanges: true);

        Assert.Equal(RunStatus.Failed, status);
    }
}
