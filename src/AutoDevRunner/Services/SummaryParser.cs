using System.Text.RegularExpressions;

namespace AutoDevRunner.Services;

public record ParsedSummary(
    string? Task,
    string? Done,
    string? Pending,
    string? Ideas,
    string? Files,
    string? NextTask,
    string FullText);

/// <summary>Extracts the structured summary block the AI is asked to emit.</summary>
public class SummaryParser
{
    public ParsedSummary Parse(string output)
    {
        var idx = output.LastIndexOf(PromptBuilder.SummaryMarker, StringComparison.Ordinal);
        var block = idx >= 0 ? output[(idx + PromptBuilder.SummaryMarker.Length)..].Trim() : output.Trim();

        string? Field(string key)
        {
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
            FullText: idx >= 0 ? block : Truncate(block, 4000));
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n…(truncated)";
}
