using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class StatusWriter
{
    public async Task<bool> WriteAsync(
        ProjectConfig project,
        RunReportData report,
        CancellationToken cancellationToken = default)
    {
        var policy = new PathPolicy(project.AllowedWritePaths, project.ProtectedPaths);

        if (TryWriteToStatusFile(project, policy, report, cancellationToken, out var statusTask))
        {
            await statusTask!;
            return true;
        }

        if (TryWriteRunReport(project, policy, report, cancellationToken, out var reportTask))
        {
            await reportTask!;
            return true;
        }

        Console.WriteLine($"[StatusWriter] Could not write run status: statusFile and docs/active/ are not within allowedWritePaths.");
        return false;
    }

    private static bool TryWriteToStatusFile(
        ProjectConfig project,
        PathPolicy policy,
        RunReportData report,
        CancellationToken cancellationToken,
        out Task? task)
    {
        if (string.IsNullOrWhiteSpace(project.StatusFile) || !policy.IsWriteAllowed(project.StatusFile))
        {
            task = null;
            return false;
        }

        var fullPath = Path.Combine(project.RepoPath, project.StatusFile.Replace('/', Path.DirectorySeparatorChar));
        var section = BuildStatusSection(report);

        task = PrependToFileAsync(fullPath, section, cancellationToken);
        return true;
    }

    private static bool TryWriteRunReport(
        ProjectConfig project,
        PathPolicy policy,
        RunReportData report,
        CancellationToken cancellationToken,
        out Task? task)
    {
        var reportDir = "docs/active";
        if (!policy.IsWriteAllowed(reportDir + "/"))
        {
            task = null;
            return false;
        }

        var timestamp = report.RunTimestamp.ToString("yyyyMMdd-HHmm");
        var relativePath = $"docs/active/autodev-run-report-{timestamp}.md";
        var fullPath = Path.Combine(project.RepoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var content = BuildStatusSection(report);
        task = File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return true;
    }

    private static string BuildStatusSection(RunReportData report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## AutoDev Run — {report.RunTimestamp:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(report.TaskId))
        {
            sb.AppendLine($"**Task:** {report.TaskId} — {report.TaskTitle}");
        }

        if (!string.IsNullOrWhiteSpace(report.BlockedReason))
        {
            sb.AppendLine($"**Blocked:** {report.BlockedReason}");
        }

        sb.AppendLine($"**Build:** {(report.BuildPassed ? "PASSED" : "FAILED")}");

        if (report.TestPassed.HasValue)
        {
            sb.AppendLine($"**Tests:** {(report.TestPassed.Value ? "PASSED" : "FAILED")}");
        }

        if (!string.IsNullOrWhiteSpace(report.CommitHash))
        {
            sb.AppendLine($"**Commit:** {report.CommitHash}");
        }

        if (report.FilesChanged.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Files Changed:**");
            foreach (var file in report.FilesChanged)
            {
                sb.AppendLine($"- {file}");
            }
        }

        if (report.RemainingRisks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Remaining Risks:**");
            foreach (var risk in report.RemainingRisks)
            {
                sb.AppendLine($"- {risk}");
            }
        }

        if (!string.IsNullOrWhiteSpace(report.NextTaskTitle))
        {
            sb.AppendLine();
            sb.AppendLine($"**Next Recommended Task:** {report.NextTaskTitle}");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        return sb.ToString();
    }

    private static async Task PrependToFileAsync(string path, string newContent, CancellationToken cancellationToken)
    {
        var existing = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : string.Empty;

        await File.WriteAllTextAsync(path, newContent + existing, cancellationToken);
    }
}
