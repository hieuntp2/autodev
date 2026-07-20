using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class PlannerCallPolicyTests
{
    private static PlannerOptions Enabled() => new() { Enabled = true, SkipWhenTaskInProgress = true };
    private static PlannerOptions Disabled() => new() { Enabled = false, SkipWhenTaskInProgress = true };

    [Fact]
    public void Skips_when_task_in_progress_and_last_run_succeeded()
    {
        var choice = PlannerCallPolicy.Resolve(Enabled(),
            new Project { CurrentTask = "Finish feature", LastRunStatus = RunStatus.Success });

        Assert.Equal(PlannerChoice.None, choice);
    }

    [Fact]
    public void Calls_when_no_task_is_in_progress()
    {
        var choice = PlannerCallPolicy.Resolve(Enabled(),
            new Project { CurrentTask = null, LastRunStatus = RunStatus.Success });

        Assert.Equal(PlannerChoice.OpenAi, choice);
    }

    [Fact]
    public void Global_default_is_openai_when_enabled_and_none_when_disabled()
    {
        var project = new Project();
        Assert.Equal(PlannerChoice.OpenAi, PlannerCallPolicy.Resolve(Enabled(), project));
        Assert.Equal(PlannerChoice.None, PlannerCallPolicy.Resolve(Disabled(), project));
    }

    [Theory]
    [InlineData("Codex", PlannerChoice.Codex)]
    [InlineData("claude", PlannerChoice.Claude)]
    [InlineData("OPENAI", PlannerChoice.OpenAi)]
    [InlineData("None", PlannerChoice.None)]
    [InlineData("off", PlannerChoice.None)]
    public void Per_project_choice_overrides_global_switch(string configured, PlannerChoice expected)
    {
        // Even with the global planner disabled, an explicit per-project choice wins.
        var choice = PlannerCallPolicy.Resolve(Disabled(), new Project { PlannerProvider = configured });

        Assert.Equal(expected, choice);
    }

    [Fact]
    public void Per_project_none_beats_enabled_global_planner()
    {
        var choice = PlannerCallPolicy.Resolve(Enabled(), new Project { PlannerProvider = "None" });

        Assert.Equal(PlannerChoice.None, choice);
    }

    [Fact]
    public void Unknown_token_falls_back_to_global_default()
    {
        Assert.Equal(PlannerChoice.OpenAi,
            PlannerCallPolicy.Resolve(Enabled(), new Project { PlannerProvider = "gemini" }));
        Assert.Equal(PlannerChoice.None,
            PlannerCallPolicy.Resolve(Disabled(), new Project { PlannerProvider = "gemini" }));
    }

    [Fact]
    public void Skip_rule_also_applies_to_per_project_cli_planner()
    {
        var choice = PlannerCallPolicy.Resolve(Enabled(), new Project
        {
            PlannerProvider = "Codex",
            CurrentTask = "Finish feature",
            LastRunStatus = RunStatus.Success
        });

        Assert.Equal(PlannerChoice.None, choice);
    }

    [Fact]
    public void ResolveConfigured_ignores_the_skip_rule()
    {
        var project = new Project
        {
            PlannerProvider = "Claude",
            CurrentTask = "Finish feature",
            LastRunStatus = RunStatus.Success
        };

        Assert.Equal(PlannerChoice.Claude, PlannerCallPolicy.ResolveConfigured(Enabled(), project));
        Assert.Equal(PlannerChoice.None, PlannerCallPolicy.Resolve(Enabled(), project));
    }
}
