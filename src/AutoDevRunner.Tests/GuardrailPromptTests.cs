using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class GuardrailPromptTests
{
    [Fact]
    public void Prompt_guardrails_allow_in_repo_deletions_when_policy_allows_them()
    {
        var prompt = GuardrailService.PromptGuardrails(
            allowMain: false,
            autoPush: false,
            repoPath: "C:/repo",
            blockDeletions: false,
            blockOutOfProject: true);

        Assert.Contains("Deletions inside the project are permitted", prompt);
        Assert.DoesNotContain("Do NOT delete files", prompt);
        Assert.Contains("Do NOT touch, create, or delete anything outside it", prompt);
    }
}
