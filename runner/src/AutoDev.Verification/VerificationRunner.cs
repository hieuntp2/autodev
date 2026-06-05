using System.Text;
using AutoDev.Core.Models;

namespace AutoDev.Verification;

public sealed class VerificationRunner(CommandRunner commandRunner)
{
    public async Task<VerificationSummary> RunAsync(
        ProjectConfig project,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var buildLog = Path.Combine(workspacePath, "03-verification", "build-output.log");
        var testLog = Path.Combine(workspacePath, "03-verification", "test-output.log");

        var buildResults = await RunCommandsAsync(project.BuildCommands, project.RepoPath, buildLog, cancellationToken);
        var testResults = await RunCommandsAsync(project.TestCommands, project.RepoPath, testLog, cancellationToken);

        var summary = new VerificationSummary
        {
            BuildResults = buildResults,
            TestResults = testResults
        };

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "03-verification", "verification-result.md"),
            RenderVerificationResult(summary),
            cancellationToken);

        return summary;
    }

    private async Task<IReadOnlyList<CommandResult>> RunCommandsAsync(
        IReadOnlyList<string> commands,
        string workingDirectory,
        string logPath,
        CancellationToken cancellationToken)
    {
        var results = new List<CommandResult>();
        var log = new StringBuilder();

        if (commands.Count == 0)
        {
            log.AppendLine("No commands configured.");
        }

        foreach (var command in commands)
        {
            var result = await commandRunner.RunShellAsync(command, workingDirectory, cancellationToken);
            results.Add(result);
            AppendCommandResult(log, result);
        }

        await File.WriteAllTextAsync(logPath, log.ToString(), cancellationToken);
        return results;
    }

    public static string RenderVerificationResult(VerificationSummary summary)
    {
        var build = summary.BuildResults.FirstOrDefault();
        var test = summary.TestResults.FirstOrDefault();

        return $$"""
        # Verification Result

        ## Build

        - Command: {{build?.Command ?? "none"}}
        - Passed: {{summary.BuildPassed}}
        - Exit Code: {{build?.ExitCode.ToString() ?? "n/a"}}

        ## Tests

        - Command: {{test?.Command ?? "none"}}
        - Passed: {{summary.TestPassed}}
        - Exit Code: {{test?.ExitCode.ToString() ?? "n/a"}}

        ## Summary

        Build passed: {{summary.BuildPassed}}
        Tests passed: {{summary.TestPassed}}

        ## Failure Notes

        {{RenderFailureNotes(summary)}}
        """;
    }

    private static string RenderFailureNotes(VerificationSummary summary)
    {
        var failures = summary.BuildResults.Concat(summary.TestResults)
            .Where(result => !result.Passed)
            .Select(result => $"- `{result.Command}` exited with {result.ExitCode}");

        var text = string.Join(Environment.NewLine, failures);
        return string.IsNullOrWhiteSpace(text) ? "No verification failures recorded." : text;
    }

    private static void AppendCommandResult(StringBuilder log, CommandResult result)
    {
        log.AppendLine($"$ {result.Command}");
        log.AppendLine($"Exit Code: {result.ExitCode}");
        log.AppendLine($"Duration: {result.Duration}");
        log.AppendLine();
        log.AppendLine("STDOUT");
        log.AppendLine(result.StandardOutput);
        log.AppendLine("STDERR");
        log.AppendLine(result.StandardError);
        log.AppendLine();
    }
}
