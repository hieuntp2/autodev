using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class SessionResultFormatterTests
{
    [Fact]
    public void Successful_validation_reports_success_with_command()
    {
        var run = new RunRecord { Status = RunStatus.Success, ValidationRun = true, ValidationPassed = true };

        var result = SessionResultFormatter.Build(run, "dotnet build");

        Assert.Equal("SUCCESS — validation passed (dotnet build)", result);
    }

    [Fact]
    public void Missing_validation_reports_not_verifiable()
    {
        var run = new RunRecord { Status = RunStatus.Failed, ValidationRun = false };

        var result = SessionResultFormatter.Build(run, null);

        Assert.Equal("NOT VERIFIABLE — no validation command configured or inferable", result);
    }

    [Fact]
    public void Failed_run_reports_reason()
    {
        var run = new RunRecord { Status = RunStatus.Failed, Reason = "validation failed: compile error" };

        var result = SessionResultFormatter.Build(run, "dotnet build");

        Assert.Equal("FAILED — validation failed: compile error", result);
    }
}
