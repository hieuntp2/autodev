using AutoDev.Core.Models;
using AutoDev.Verification;

namespace AutoDev.Git;

public sealed class GitService(CommandRunner commandRunner)
{
    public async Task CheckoutAndPullAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        EnsureRepoExists(project);
        await RunGitOrThrowAsync(project, ["checkout", project.Branch], cancellationToken);

        // Pull is best-effort — fresh repos or repos without remotes should not block the run
        await RunGitOrThrowAsync(project, ["pull", "--ff-only"], cancellationToken, allowFailure: true);
    }

    public async Task<string> GetCurrentCommitAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        EnsureRepoExists(project);
        var result = await RunGitOrThrowAsync(project, ["rev-parse", "HEAD"], cancellationToken);
        return result.StandardOutput.Trim();
    }

    public async Task<string> GetLogAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        EnsureRepoExists(project);
        var result = await RunGitOrThrowAsync(project, ["log", "--oneline", "-5"], cancellationToken);
        return result.StandardOutput;
    }

    public async Task<string> GetStatusAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        EnsureRepoExists(project);
        var result = await RunGitOrThrowAsync(project, ["status", "--short"], cancellationToken);
        return result.StandardOutput;
    }

    public async Task<string> GetChangedFilesAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(project, cancellationToken);
        return string.Join(Environment.NewLine, ParseStatusChangedFiles(status));
    }

    public async Task<string> GetDiffAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        EnsureRepoExists(project);
        var tracked = await RunGitOrThrowAsync(project, ["diff", "--patch", "HEAD", "--"], cancellationToken);
        var untrackedFiles = (await GetStatusAsync(project, cancellationToken))
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("?? ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim().Replace('\\', '/'))
            .ToArray();

        if (untrackedFiles.Length == 0)
        {
            return tracked.StandardOutput;
        }

        return tracked.StandardOutput
            + Environment.NewLine
            + "Untracked files are not included in git diff output:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, untrackedFiles.Select(file => $"- {file}"));
    }

    public async Task CommitAsync(ProjectConfig project, string message, CancellationToken cancellationToken = default)
    {
        EnsureRepoExists(project);
        await RunGitOrThrowAsync(project, ["add", "--all"], cancellationToken);
        await RunGitOrThrowAsync(project, ["commit", "--message", message], cancellationToken);
    }

    public async Task PushAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        EnsureRepoExists(project);
        await RunGitOrThrowAsync(project, ["push", "-u", "origin", project.Branch], cancellationToken);
    }

    private async Task<CommandResult> RunGitOrThrowAsync(
        ProjectConfig project,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        var result = await commandRunner.RunProcessAsync(
            "git",
            arguments,
            project.RepoPath,
            displayCommand: "git " + string.Join(" ", arguments),
            cancellationToken: cancellationToken);

        if (!allowFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git command failed: {result.Command}{Environment.NewLine}{result.StandardError}{result.StandardOutput}");
        }

        return result;
    }

    private static IReadOnlyList<string> ParseStatusChangedFiles(string status)
    {
        var files = new List<string>();
        foreach (var line in status.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var path = line[3..].Trim();
            var arrowIndex = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                path = path[(arrowIndex + 4)..];
            }

            files.Add(path.Replace('\\', '/'));
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void EnsureRepoExists(ProjectConfig project)
    {
        if (!Directory.Exists(project.RepoPath))
        {
            throw new DirectoryNotFoundException($"Target repo path does not exist: {project.RepoPath}");
        }
    }
}
