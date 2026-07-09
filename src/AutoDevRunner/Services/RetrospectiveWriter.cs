using System.Text;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>
/// After a run, writes a human- and planner-readable retrospective next to the
/// run's markdown log (<c>…run7.retro.md</c>): what was attempted, what changed,
/// what worked, what failed and why, what to try next, and what to avoid. Future
/// runs read the recent retrospectives' facts through <see cref="RunHistoryService"/>
/// (which reads the JSON sidecars) — this file is the readable narrative. Fail-soft.
/// </summary>
public class RetrospectiveWriter
{
    private readonly ILogger<RetrospectiveWriter> _log;

    public RetrospectiveWriter(ILogger<RetrospectiveWriter> log) => _log = log;

    /// <summary>Retrospective path from the run log (…run7.md → …run7.retro.md).</summary>
    public static string PathFor(string markdownLogPath) =>
        Path.Combine(Path.GetDirectoryName(markdownLogPath)!,
            Path.GetFileNameWithoutExtension(markdownLogPath) + ".retro.md");

    public async Task<string?> WriteAsync(string markdownLogPath, RunRecord run, ParsedSummary? summary,
        RiskAssessment risk, IReadOnlyList<string> changedFiles, RunLessons lessons, CancellationToken ct = default)
    {
        try
        {
            var path = PathFor(markdownLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, BuildText(run, summary, risk, changedFiles, lessons), ct);
            return path;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to write retrospective for run #{Run}.", run.Id);
            return null;
        }
    }

    /// <summary>Pure text builder (no I/O) — the unit-testable core.</summary>
    public static string BuildText(RunRecord run, ParsedSummary? summary, RiskAssessment risk,
        IReadOnlyList<string> changedFiles, RunLessons lessons)
    {
        const string untitled = "(untitled task)";
        var failed = run.Status is RunStatus.Failed;
        var validationFailed = run.ValidationRun && !run.ValidationPassed;
        var title = FirstNonEmpty(summary?.EffectiveTitle, run.TaskTitle) ?? untitled;

        var sb = new StringBuilder();
        sb.AppendLine($"# Retrospective — run {run.Id}");
        sb.AppendLine($"- Status: **{run.Status}**");
        sb.AppendLine($"- Provider: {run.Provider}");
        sb.AppendLine($"- Task: {title}");
        if (!string.IsNullOrWhiteSpace(run.TaskSource)) sb.AppendLine($"- Task source: {run.TaskSource}");
        sb.AppendLine();

        sb.AppendLine("## What was attempted");
        sb.AppendLine(title);
        sb.AppendLine();

        sb.AppendLine("## What changed");
        if (changedFiles.Count == 0)
            sb.AppendLine("No files changed.");
        else
        {
            sb.AppendLine($"{changedFiles.Count} file(s) changed:");
            foreach (var f in changedFiles.Take(30)) sb.AppendLine($"- {f}");
            if (changedFiles.Count > 30) sb.AppendLine($"- …and {changedFiles.Count - 30} more");
        }
        sb.AppendLine();

        sb.AppendLine("## What worked");
        if (!string.IsNullOrWhiteSpace(summary?.Done)) sb.AppendLine(summary!.Done!.Trim());
        else if (!failed && !validationFailed) sb.AppendLine("Run completed without a reported failure.");
        else sb.AppendLine("(nothing conclusive)");
        sb.AppendLine(run.ValidationRun
            ? $"- Validation: {(run.ValidationPassed ? "PASSED" : "FAILED")}"
            : "- Validation: not run");
        sb.AppendLine();

        sb.AppendLine("## What failed / why");
        if (failed || validationFailed)
        {
            if (!string.IsNullOrWhiteSpace(run.Reason)) sb.AppendLine($"- Reason: {run.Reason!.Trim()}");
            if (validationFailed) sb.AppendLine("- The configured validation command did not pass.");
            if (!string.IsNullOrWhiteSpace(summary?.Pending)) sb.AppendLine($"- Left pending: {summary!.Pending!.Trim()}");
            if (string.IsNullOrWhiteSpace(run.Reason) && !validationFailed && string.IsNullOrWhiteSpace(summary?.Pending))
                sb.AppendLine("- Marked failed, but no reason was captured.");
        }
        else sb.AppendLine("Nothing failed this run.");
        sb.AppendLine();

        sb.AppendLine("## What to try next");
        var next = FirstNonEmpty(summary?.NextSuggestedTasks, summary?.NextTask, summary?.Pending);
        sb.AppendLine(string.IsNullOrWhiteSpace(next) ? "(no explicit next step recorded)" : next!.Trim());
        sb.AppendLine();

        sb.AppendLine("## What to avoid next time");
        var avoid = new List<string>();
        if (risk.Level == RiskLevel.Risky && risk.Reasons.Count > 0)
            avoid.Add("Escalating scope — this run was flagged risky: " + string.Join("; ", risk.Reasons));
        if ((failed || validationFailed) && title != untitled)
            avoid.Add($"Re-attempting \"{title}\" the same way without addressing the failure above.");
        if (lessons.IsRepeatedlyFailing(title))
            avoid.Add($"\"{title}\" has now failed repeatedly — change the approach, break it into a smaller step, or drop it.");
        if (avoid.Count == 0)
            avoid.Add("Nothing specific — keep changes small and verified.");
        foreach (var a in avoid) sb.AppendLine($"- {a}");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
