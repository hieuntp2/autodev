namespace AutoDevRunner.Services;

public sealed record PromptDirectivesUpdateResult(
    bool Updated,
    string? ArchiveRelativePath = null,
    string? Reason = null);

public class PromptDirectivesService
{
    private const int MaxProposalChars = 4000;
    private const int MaxHistoryFiles = 30;

    public async Task<string?> LoadAsync(string repoPath, CancellationToken ct = default)
    {
        var path = PromptPath(repoPath);
        if (!File.Exists(path)) return null;

        try
        {
            var content = await File.ReadAllTextAsync(path, ct);
            return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async Task<PromptDirectivesUpdateResult> AdoptProposalAsync(
        string repoPath,
        DateTime utcNow,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var runnerDir = RunnerDir(repoPath);
        var proposalPath = Path.Combine(runnerDir, "prompt-proposal.md");
        if (!File.Exists(proposalPath))
            return new PromptDirectivesUpdateResult(false);

        string proposal;
        try
        {
            proposal = await File.ReadAllTextAsync(proposalPath, ct);
        }
        catch (Exception ex)
        {
            log?.Invoke("Prompt directives proposal could not be read: " + ex.Message);
            return new PromptDirectivesUpdateResult(false, Reason: ex.Message);
        }

        try { File.Delete(proposalPath); } catch { /* best effort */ }

        if (string.IsNullOrWhiteSpace(proposal))
        {
            const string reason = "prompt proposal was empty";
            log?.Invoke("Prompt directives proposal rejected: " + reason);
            return new PromptDirectivesUpdateResult(false, Reason: reason);
        }

        if (proposal.Length > MaxProposalChars)
        {
            var reason = $"prompt proposal exceeded {MaxProposalChars} chars";
            log?.Invoke("Prompt directives proposal rejected: " + reason);
            return new PromptDirectivesUpdateResult(false, Reason: reason);
        }

        Directory.CreateDirectory(runnerDir);
        var promptPath = PromptPath(repoPath);
        var historyDir = Path.Combine(runnerDir, "prompt-history");
        Directory.CreateDirectory(historyDir);

        string? archiveRel = null;
        if (File.Exists(promptPath))
        {
            var stamp = utcNow.ToString("yyyyMMdd-HHmmss");
            var archivePath = Path.Combine(historyDir, stamp + ".md");
            File.Copy(promptPath, archivePath, overwrite: true);
            archiveRel = $"prompt-history/{stamp}.md";
        }

        await File.WriteAllTextAsync(promptPath, proposal.Trim(), ct);
        var pruned = PruneHistory(historyDir);
        if (pruned > 0)
            log?.Invoke($"Pruned {pruned} old prompt directive history file(s).");

        log?.Invoke("Prompt directives evolved -> " + (archiveRel ?? "PROMPT.md"));
        return new PromptDirectivesUpdateResult(true, archiveRel);
    }

    private static int PruneHistory(string historyDir)
    {
        if (!Directory.Exists(historyDir)) return 0;
        var files = Directory.EnumerateFiles(historyDir, "*.md")
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pruned = 0;
        foreach (var file in files.Skip(MaxHistoryFiles))
        {
            try { file.Delete(); pruned++; } catch { /* best effort */ }
        }
        return pruned;
    }

    private static string RunnerDir(string repoPath) => Path.Combine(repoPath, ".ai-runner");

    private static string PromptPath(string repoPath) => Path.Combine(RunnerDir(repoPath), "PROMPT.md");
}
