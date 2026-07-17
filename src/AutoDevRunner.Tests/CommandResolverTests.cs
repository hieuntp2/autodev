using System.Runtime.InteropServices;
using AutoDevRunner.Providers;
using Xunit;

namespace AutoDevRunner.Tests;

/// <summary>
/// Regression cover for the 2026-07-17 outage: every Codex run died instantly
/// with "Provider exited with code -1 / cannot find the file specified" because
/// codex is an npm ".cmd" shim (no codex.exe) and Process.Start could not
/// launch a bare ".cmd". <see cref="CommandResolver"/> routes such shims
/// through cmd.exe so they run.
/// </summary>
public class CommandResolverTests
{
    [Fact]
    public void NonWindows_or_missing_command_passes_through_unchanged()
    {
        // A command that resolves to nothing must be returned verbatim so
        // Process.Start still produces its own diagnostic.
        var (file, args) = CommandResolver.Resolve("definitely-not-a-real-command-xyz", "--help");
        Assert.Equal("definitely-not-a-real-command-xyz", file);
        Assert.Equal("--help", args);
    }

    [Fact]
    public void Windows_cmd_shim_is_routed_through_comspec()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var dir = Path.Combine(Path.GetTempPath(), "autodev-cmdresolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "faketool.cmd");
        File.WriteAllText(shim, "@echo off\r\necho hello-from-shim %*\r\n");
        var savedPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + savedPath);

            var (file, args) = CommandResolver.Resolve("faketool", "--flag value");

            Assert.EndsWith("cmd.exe", file, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("faketool.cmd", args, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--flag value", args);
            Assert.StartsWith("/d /s /c", args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", savedPath);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Windows_exe_is_launched_directly_without_wrapping()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // cmd.exe itself is a native executable on PATH: it must resolve to a
        // full path and NOT be re-wrapped through another cmd.exe.
        var (file, args) = CommandResolver.Resolve("cmd.exe", "/c echo hi");

        Assert.EndsWith("cmd.exe", file, StringComparison.OrdinalIgnoreCase);
        Assert.True(Path.IsPathRooted(file));
        Assert.Equal("/c echo hi", args);
    }

    [Fact]
    public async Task ProcessRunner_launches_a_bare_cmd_shim_end_to_end()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var dir = Path.Combine(Path.GetTempPath(), "autodev-cmdresolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var shim = Path.Combine(dir, "faketool.cmd");
        File.WriteAllText(shim, "@echo off\r\necho shim-ran %*\r\n");
        var savedPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + savedPath);

            var result = await new ProcessRunner().RunAsync(
                "faketool", "arg1", dir, TimeSpan.FromSeconds(30));

            Assert.False(result.TimedOut);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("shim-ran", result.StdOut);
            Assert.Contains("arg1", result.StdOut);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", savedPath);
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
