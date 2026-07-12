using System.Text.Json;
using Xunit;

namespace AutoDevRunner.Tests;

public class ShippedConfigurationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Appsettings_uses_requested_OpenAI_models()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot, "src", "AutoDevRunner", "appsettings.json")));
        var autoDev = doc.RootElement.GetProperty("AutoDev");
        var codex = autoDev.GetProperty("Providers").GetProperty("Codex");
        var tiers = codex.GetProperty("Tiers");

        Assert.Contains("-m gpt-5.6-sol", codex.GetProperty("Arguments").GetString());
        Assert.Contains("-m gpt-5.6-sol", codex.GetProperty("ResumeArguments").GetString());
        Assert.Contains("-m gpt-5.5", tiers.GetProperty("Light").GetString());
        Assert.Contains("-m gpt-5.6-sol", tiers.GetProperty("Standard").GetString());
        Assert.Contains("-m gpt-5.6-sol", tiers.GetProperty("Deep").GetString());
        Assert.Equal("gpt-5.6-sol", autoDev.GetProperty("Planner").GetProperty("Model").GetString());
    }

    [Fact]
    public void Appsettings_scheduler_interval_is_two_hours()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot, "src", "AutoDevRunner", "appsettings.json")));

        Assert.Equal(2, doc.RootElement.GetProperty("AutoDev")
            .GetProperty("Scheduler").GetProperty("IntervalHours").GetDouble());
    }

    [Fact]
    public void Installer_defaults_to_two_hour_interval()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "installer.ps1"));

        Assert.Contains("How often the run task fires. Default 2.", script);
        Assert.Contains("[double]$IntervalHours = 2", script);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AutoDevRunner.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AutoDev Runner repository root.");
    }
}
