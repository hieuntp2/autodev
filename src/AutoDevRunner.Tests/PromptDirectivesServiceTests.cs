using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class PromptDirectivesServiceTests : IDisposable
{
    private readonly string _repo;
    private readonly string _runnerDir;
    private readonly PromptDirectivesService _service = new();

    public PromptDirectivesServiceTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "adprompt-" + Guid.NewGuid().ToString("N"));
        _runnerDir = Path.Combine(_repo, ".ai-runner");
        Directory.CreateDirectory(_runnerDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }

    [Fact]
    public async Task Adopt_proposal_writes_prompt_and_archives_existing_prompt()
    {
        File.WriteAllText(Path.Combine(_runnerDir, "PROMPT.md"), "old directives");
        File.WriteAllText(Path.Combine(_runnerDir, "prompt-proposal.md"), "new directives");
        var now = new DateTime(2026, 7, 9, 8, 10, 0, DateTimeKind.Utc);

        var result = await _service.AdoptProposalAsync(_repo, now);

        Assert.True(result.Updated);
        Assert.Equal("prompt-history/20260709-081000.md", result.ArchiveRelativePath);
        Assert.Equal("new directives", File.ReadAllText(Path.Combine(_runnerDir, "PROMPT.md")));
        Assert.Equal("old directives", File.ReadAllText(Path.Combine(_runnerDir, "prompt-history", "20260709-081000.md")));
        Assert.False(File.Exists(Path.Combine(_runnerDir, "prompt-proposal.md")));
    }

    [Fact]
    public async Task Oversized_proposal_is_rejected()
    {
        File.WriteAllText(Path.Combine(_runnerDir, "prompt-proposal.md"), new string('x', 4001));

        var result = await _service.AdoptProposalAsync(_repo, DateTime.UtcNow);

        Assert.False(result.Updated);
        Assert.False(File.Exists(Path.Combine(_runnerDir, "PROMPT.md")));
        Assert.False(File.Exists(Path.Combine(_runnerDir, "prompt-proposal.md")));
    }

    [Fact]
    public async Task History_is_pruned_to_newest_30_files()
    {
        var history = Path.Combine(_runnerDir, "prompt-history");
        Directory.CreateDirectory(history);
        for (var i = 0; i < 31; i++)
            File.WriteAllText(Path.Combine(history, $"20260709-08{i:00}00.md"), i.ToString());
        File.WriteAllText(Path.Combine(_runnerDir, "prompt-proposal.md"), "new directives");

        await _service.AdoptProposalAsync(_repo, new DateTime(2026, 7, 9, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal(30, Directory.EnumerateFiles(history, "*.md").Count());
        Assert.False(File.Exists(Path.Combine(history, "20260709-080000.md")));
    }
}
