using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class PromptBuilderTests
{
    private readonly PromptBuilder _builder = new();

    private static Project Project(string? task = null, string? validation = null) => new()
    {
        Name = "Pixel Pet",
        RepoPath = "/repo",
        CurrentTask = task,
        ValidationCommand = validation
    };

    [Fact]
    public void Prompt_includes_project_goal_and_validation_and_task()
    {
        var goal = new ProjectGoal("Make a delightful pixel pet.", null, null, null, null);
        var prompt = _builder.Build(Project(task: "Add blink animation", validation: "dotnet build"),
            brief: "brief text", run: new RunRecord(), creativePlan: null, skills: null, goal: goal);

        Assert.Contains("## Project goal", prompt);
        Assert.Contains("Make a delightful pixel pet.", prompt);
        Assert.Contains("Task: Add blink animation", prompt);
        Assert.Contains("dotnet build", prompt);
        Assert.Contains("## Expected output", prompt);
    }

    [Fact]
    public void When_no_task_prompt_asks_to_advance_the_goal()
    {
        var goal = new ProjectGoal("Make a delightful pixel pet.", null, null, null, null);
        var prompt = _builder.Build(Project(task: null),
            brief: "", run: new RunRecord(), creativePlan: null, skills: null, goal: goal);

        Assert.Contains("No task is in progress", prompt);
        Assert.Contains("PROJECT GOAL", prompt);
    }

    [Fact]
    public void Without_a_goal_no_goal_section_is_emitted()
    {
        var prompt = _builder.Build(Project(task: "do a thing"),
            brief: "b", run: new RunRecord(), creativePlan: null, skills: null, goal: null);
        Assert.DoesNotContain("## Project goal", prompt);
    }

    [Fact]
    public void Platform_contract_is_emitted_as_a_hard_constraint_when_set()
    {
        var p = Project(task: "do a thing");
        p.ProjectType = "Android (Kotlin/Jetpack Compose)";
        var prompt = _builder.Build(p, brief: "b", run: new RunRecord(),
            creativePlan: null, skills: null, goal: null);

        Assert.Contains("TARGET PLATFORM — NON-NEGOTIABLE", prompt);
        Assert.Contains("Android (Kotlin/Jetpack Compose)", prompt);
        // The specific failure mode we are guarding against.
        Assert.Contains("HTML", prompt);
    }

    [Fact]
    public void No_platform_contract_when_project_type_is_unset()
    {
        var prompt = _builder.Build(Project(task: "do a thing"),
            brief: "b", run: new RunRecord(), creativePlan: null, skills: null, goal: null);
        Assert.DoesNotContain("TARGET PLATFORM — NON-NEGOTIABLE", prompt);
    }

    [Fact]
    public void Brief_evolution_instructions_only_appear_when_allowed()
    {
        var off = _builder.Build(Project(task: "t"), brief: "b", run: new RunRecord(),
            creativePlan: null, skills: null, goal: null);
        Assert.DoesNotContain("brief-proposal.md", off);

        var p = Project(task: "t");
        p.AllowAiEditBrief = true;
        var on = _builder.Build(p, brief: "b", run: new RunRecord(),
            creativePlan: null, skills: null, goal: null);
        Assert.Contains("brief-proposal.md", on);
    }

    [Fact]
    public void Prompt_directives_section_appears_only_when_content_is_provided()
    {
        var without = _builder.Build(Project(task: "t"), brief: "b", run: new RunRecord(),
            creativePlan: null, skills: null, goal: null);

        var with = _builder.Build(Project(task: "t"), brief: "b", run: new RunRecord(),
            creativePlan: null, skills: null, goal: null,
            projectPromptDirectives: "Prefer small commits and explicit validation.");

        Assert.DoesNotContain("## Project prompt directives (self-evolved)", without);
        Assert.Contains("## Project prompt directives (self-evolved)", with);
        Assert.Contains("Prefer small commits", with);
    }

    [Fact]
    public void Prompt_directive_evolution_instruction_only_appears_when_allowed()
    {
        var off = _builder.Build(Project(task: "t"), brief: "b", run: new RunRecord(),
            creativePlan: null, skills: null, goal: null);

        var p = Project(task: "t");
        p.AllowAiEditBrief = true;
        var on = _builder.Build(p, brief: "b", run: new RunRecord(),
            creativePlan: null, skills: null, goal: null);

        Assert.DoesNotContain("prompt-proposal.md", off);
        Assert.Contains("prompt-proposal.md", on);
    }
}
