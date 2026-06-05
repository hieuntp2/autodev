using AutoDev.Core.Models;
using AutoDev.Verification;

namespace AutoDev.Codex;

public sealed class CodexRunner(CommandRunner commandRunner)
{
    public async Task<CommandResult> RunAsync(
        ProjectConfig project,
        string workspacePath,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await commandRunner.RunProcessAsync(
                "codex",
                ["exec", "--cd", project.RepoPath, "--ask-for-approval", "never", "--sandbox", "workspace-write", "-"],
                project.RepoPath,
                prompt,
                "codex exec --cd \"{repoPath}\" --ask-for-approval never --sandbox workspace-write -",
                cancellationToken);

            await WriteLogAsync(workspacePath, result, cancellationToken);
            return result;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("codex is not installed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Codex CLI is not installed or not available in PATH.", ex);
        }
    }

    private static async Task WriteLogAsync(string workspacePath, CommandResult result, CancellationToken cancellationToken)
    {
        var log = $"""
        $ {result.Command}
        Exit Code: {result.ExitCode}
        Duration: {result.Duration}

        STDOUT
        {result.StandardOutput}

        STDERR
        {result.StandardError}
        """;

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "02-implementation", "codex-output.log"),
            log,
            cancellationToken);
    }
}
