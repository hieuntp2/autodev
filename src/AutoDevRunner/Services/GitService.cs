using AutoDevRunner.Providers;

namespace AutoDevRunner.Services;

/// <summary>A single working-tree change: the porcelain status code and its path.</summary>
public record GitChange(string Status, string Path)
{
    /// <summary>True when the file was deleted (staged or unstaged).</summary>
    public bool IsDelete => Status.Contains('D');
}

/// <summary>Safe git operations via the git CLI. Never runs destructive commands.</summary>
public class GitService
{
    private readonly ProcessRunner _runner;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    public GitService(ProcessRunner runner) => _runner = runner;

    private async Task<ProcessResult> GitAsync(string repo, string args, CancellationToken ct = default)
        => await _runner.RunAsync("git", args, repo, GitTimeout, null, ct);

    public async Task<bool> IsGitRepoAsync(string repo, CancellationToken ct = default)
    {
        if (!Directory.Exists(repo)) return false;
        var r = await GitAsync(repo, "rev-parse --is-inside-work-tree", ct);
        return r.ExitCode == 0 && r.StdOut.Trim().StartsWith("true");
    }

    public async Task<string> GetCurrentBranchAsync(string repo, CancellationToken ct = default)
    {
        var r = await GitAsync(repo, "rev-parse --abbrev-ref HEAD", ct);
        return r.StdOut.Trim();
    }

    public static bool IsProtectedBranch(string branch) =>
        branch.Equals("main", StringComparison.OrdinalIgnoreCase) ||
        branch.Equals("master", StringComparison.OrdinalIgnoreCase);

    /// <summary>Create (or switch to) the AI working branch. Returns the branch name.</summary>
    public async Task<string> EnsureBranchAsync(string repo, string branchName, CancellationToken ct = default)
    {
        var exists = await GitAsync(repo, $"rev-parse --verify --quiet \"{branchName}\"", ct);
        if (exists.ExitCode == 0)
            await GitAsync(repo, $"checkout \"{branchName}\"", ct);
        else
            await GitAsync(repo, $"checkout -b \"{branchName}\"", ct);
        return branchName;
    }

    /// <summary>Names of files changed in the working tree (staged + unstaged + untracked).</summary>
    public async Task<List<string>> GetChangedFilesAsync(string repo, CancellationToken ct = default)
    {
        var r = await GitAsync(repo, "status --porcelain", ct);
        return r.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Length > 3 ? l[3..].Trim() : l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    public async Task<bool> HasChangesAsync(string repo, CancellationToken ct = default)
        => (await GetChangedFilesAsync(repo, ct)).Count > 0;

    /// <summary>
    /// Working-tree changes as (status, path) pairs from `git status --porcelain`.
    /// Status is the two-char XY code (e.g. " M", "A ", " D", "R "); path is the
    /// affected file (for renames, the destination). Used for risk assessment.
    /// </summary>
    public async Task<List<GitChange>> GetChangesAsync(string repo, CancellationToken ct = default)
    {
        var r = await GitAsync(repo, "status --porcelain", ct);
        var changes = new List<GitChange>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;
            var status = line[..2];
            var path = line[3..].Trim();
            // Renames/copies print "old -> new"; keep the destination path.
            var arrow = path.IndexOf("->", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 2)..].Trim();
            path = path.Trim('"');
            if (path.Length > 0) changes.Add(new GitChange(status, path));
        }
        return changes;
    }

    /// <summary>Stage everything and commit. Returns the new commit SHA, or null on failure.</summary>
    public async Task<string?> CommitAllAsync(string repo, string message, CancellationToken ct = default)
    {
        await GitAsync(repo, "add -A", ct);
        var commit = await GitAsync(repo, $"commit -m \"{message.Replace("\"", "'")}\"", ct);
        if (commit.ExitCode != 0) return null;
        var sha = await GitAsync(repo, "rev-parse HEAD", ct);
        return sha.ExitCode == 0 ? sha.StdOut.Trim() : null;
    }

    public async Task<bool> PushAsync(string repo, string branch, CancellationToken ct = default)
    {
        var r = await GitAsync(repo, $"push -u origin \"{branch}\"", ct);
        return r.ExitCode == 0;
    }
}
