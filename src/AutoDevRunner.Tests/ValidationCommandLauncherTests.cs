using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ValidationCommandLauncherTests
{
    [Fact]
    public void Windows_validation_runs_through_cmd_so_relative_batch_files_resolve()
    {
        var launch = ValidationCommandLauncher.Resolve("gradlew.bat assembleDebug", windows: true);

        Assert.Equal("cmd.exe", launch.FileName);
        Assert.Equal("/d /s /c \"gradlew.bat assembleDebug\"", launch.Arguments);
    }

    [Fact]
    public void Non_windows_validation_keeps_direct_process_invocation()
    {
        var launch = ValidationCommandLauncher.Resolve("./gradlew assembleDebug", windows: false);

        Assert.Equal("./gradlew", launch.FileName);
        Assert.Equal("assembleDebug", launch.Arguments);
    }
}
