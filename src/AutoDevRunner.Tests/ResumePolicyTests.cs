using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ResumePolicyTests
{
    [Fact]
    public void Resume_allowed_when_session_provider_task_and_count_match()
    {
        var decision = ResumePolicy.Decide(
            new ResumeOptions { Enabled = true, MaxResumedRuns = 3, ResetOnTaskChange = true },
            sessionId: "session-1",
            provider: ProviderKind.Codex,
            lastProvider: ProviderKind.Codex,
            currentTask: "Continue task",
            lastTask: "Continue task",
            resumedRuns: 2);

        Assert.True(decision.ShouldResume);
    }

    [Fact]
    public void Task_change_forces_fresh_session()
    {
        var decision = ResumePolicy.Decide(
            new ResumeOptions { Enabled = true, MaxResumedRuns = 3, ResetOnTaskChange = true },
            sessionId: "session-1",
            provider: ProviderKind.Codex,
            lastProvider: ProviderKind.Codex,
            currentTask: "New task",
            lastTask: "Old task",
            resumedRuns: 0);

        Assert.False(decision.ShouldResume);
    }

    [Fact]
    public void Resume_count_cap_forces_fresh_session()
    {
        var decision = ResumePolicy.Decide(
            new ResumeOptions { Enabled = true, MaxResumedRuns = 3 },
            sessionId: "session-1",
            provider: ProviderKind.Codex,
            lastProvider: ProviderKind.Codex,
            currentTask: "Task",
            lastTask: "Task",
            resumedRuns: 3);

        Assert.False(decision.ShouldResume);
    }

    [Fact]
    public void Resume_arguments_fall_back_when_template_missing()
    {
        var opt = new ProviderCliOptions
        {
            Arguments = "exec fresh",
            ResumeArguments = "exec resume {SESSION_ID}"
        };

        Assert.Equal("exec resume abc", opt.ResolveArguments(TaskTier.Standard, false, "abc", resumeEnabled: true));

        opt.ResumeArguments = "";
        Assert.Equal("exec fresh", opt.ResolveArguments(TaskTier.Standard, false, "abc", resumeEnabled: true));
    }

    [Fact]
    public void Delta_prompt_contains_no_full_brief_and_stays_small()
    {
        var prompt = new PromptBuilder().BuildResume(
            new Project { Name = "P", RepoPath = "/repo", CurrentTask = "Finish validation" },
            validationCommand: "dotnet build",
            lessons: new RunLessons(
                Array.Empty<RunLesson>(),
                new[] { "Do not retry broad rewrite" },
                Array.Empty<string>()),
            riskPolicy: null);

        Assert.True(prompt.Length < 4000);
        Assert.Contains("Finish validation", prompt);
        Assert.Contains("dotnet build", prompt);
        Assert.Contains("Do not retry broad rewrite", prompt);
        Assert.DoesNotContain("## Project brief", prompt);
        Assert.Contains(PromptBuilder.SummaryMarker, prompt);
    }
}
