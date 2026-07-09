using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class PromptBudgetTests
{
    private readonly PromptBuilder _builder = new();

    [Fact]
    public void Over_budget_prompt_is_shrunk_under_max_chars()
    {
        var prompt = BuildLargePrompt(new PromptOptions { MaxChars = 5000 });

        Assert.True(prompt.Length <= 5000, $"Prompt length was {prompt.Length}");
    }

    [Fact]
    public void Protected_sections_survive_budgeting()
    {
        var prompt = BuildLargePrompt(new PromptOptions { MaxChars = 5000 });

        Assert.Contains("TARGET PLATFORM", prompt);
        Assert.Contains("## This run's task", prompt);
        Assert.Contains("## Constraints", prompt);
        Assert.Contains("## Hard safety rules", prompt);
        Assert.Contains("## Required output", prompt);
    }

    [Fact]
    public void Required_output_block_remains_last_and_intact()
    {
        var prompt = BuildLargePrompt(new PromptOptions { MaxChars = 5000 }).TrimEnd();

        Assert.EndsWith("NEXT_TASK: <the single task to resume next run>", prompt);
        Assert.Contains(PromptBuilder.SummaryMarker, prompt);
    }

    private string BuildLargePrompt(PromptOptions options)
    {
        var project = new Project
        {
            Name = "P",
            RepoPath = "/repo",
            ProjectType = ".NET",
            CurrentTask = "Ship the next validated slice",
            LastSummary = new string('R', 6000)
        };
        var goal = new ProjectGoal(
            "# Goal\n" + new string('G', 6000),
            "# Roadmap\n" + new string('M', 6000),
            "# Backlog\n" + new string('B', 6000),
            "# Ideas\n" + new string('I', 3000),
            "# Decisions\n" + new string('D', 3000));

        var lessons = new RunLessons(
            new[]
            {
                new RunLesson(1, DateTime.UtcNow, "Repeated task", "Failed", true,
                    "failed before", true, false, new[] { "avoid it" })
            },
            new[] { "Repeated task" },
            new[] { "small follow-up" });

        return _builder.Build(project, brief: new string('F', 6000), run: new RunRecord(),
            creativePlan: new string('C', 6000), skills: null, goal: goal, risk: RiskLevel.Normal,
            lessons: lessons, projectPromptDirectives: new string('P', 2000),
            promptOptions: options);
    }
}
