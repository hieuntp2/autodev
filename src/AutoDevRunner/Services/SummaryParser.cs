using System.Text.RegularExpressions;

namespace AutoDevRunner.Services;

/// <summary>
/// The structured summary the provider emits after the run marker. The first
/// six fields are the original v1 contract (kept for backward compatibility);
/// the rest are the goal-driven fields (v2). Every field is optional — an old
/// provider response that only emits TASK/DONE/... still parses fine.
/// </summary>
public record ParsedSummary(
    string? Task,
    string? Done,
    string? Pending,
    string? Ideas,
    string? Files,
    string? NextTask,
    string FullText,
    // ---- v2 goal-driven fields (all optional) ----
    string? TaskTitle = null,
    string? TaskStatus = null,
    string? SkillUsed = null,
    string? ArtifactPaths = null,
    string? FilesChanged = null,
    string? ValidationResult = null,
    string? RiskLevel = null,
    string? NextSuggestedTasks = null,
    string? MemoryUpdates = null,
    string? SettingsProposal = null)
{
    /// <summary>Best available human title for the task worked on.</summary>
    public string? EffectiveTitle => TaskTitle ?? Task;
}

/// <summary>Extracts the structured summary block the AI is asked to emit.</summary>
public class SummaryParser
{
    public ParsedSummary Parse(string output)
    {
        var idx = output.LastIndexOf(PromptBuilder.SummaryMarker, StringComparison.Ordinal);
        var hasMarker = idx >= 0;
        var block = idx >= 0 ? output[(idx + PromptBuilder.SummaryMarker.Length)..].Trim() : output.Trim();

        string? Field(string key)
        {
            if (!hasMarker) return null;
            // Capture from "KEY:" up to the next "KEY:" line or end of block.
            var m = Regex.Match(block,
                $@"^{key}:\s*(?<v>.*?)(?=^\w[\w ]*:\s|\z)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            var v = m.Success ? m.Groups["v"].Value.Trim() : null;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        return new ParsedSummary(
            Task: Field("TASK"),
            Done: Field("DONE"),
            Pending: Field("PENDING"),
            Ideas: Field("IDEAS"),
            Files: Field("FILES"),
            NextTask: Field("NEXT_TASK"),
            // Always cap: CLIs sometimes print diffs/logs after the summary
            // block, and FullText feeds the next run's prompt (resume context) —
            // unbounded text ballooned prompts past the OS command-line limit.
            FullText: Truncate(block, 4000),
            // v2 fields — fall back to v1 equivalents where they overlap.
            TaskTitle: Field("TASK_TITLE"),
            TaskStatus: Field("TASK_STATUS"),
            SkillUsed: Field("SKILL_USED") ?? Field("SKILL"),
            ArtifactPaths: Field("ARTIFACT_PATHS") ?? Field("GENERATED_FILES") ?? Field("ASSET_PATH"),
            FilesChanged: Field("FILES_CHANGED"),
            ValidationResult: Field("VALIDATION_RESULT") ?? Field("VALIDATION"),
            RiskLevel: Field("RISK_LEVEL"),
            NextSuggestedTasks: Field("NEXT_SUGGESTED_TASKS") ?? Field("NEXT_ANIMATIONS"),
            MemoryUpdates: Field("MEMORY_UPDATES"),
            SettingsProposal: Field("SETTINGS_PROPOSAL"));
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n…(truncated)";
}
