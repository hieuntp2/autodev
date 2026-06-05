using System.Text.RegularExpressions;
using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed partial class StatusReader
{
    [GeneratedRegex(@"(?:next\s+(?:recommended\s+)?task|next\s+up)[:\s]+[`']?(?<id>[A-Za-z0-9_-]+)[`']?", RegexOptions.IgnoreCase)]
    private static partial Regex NextTaskIdPattern();

    [GeneratedRegex(@"(?:in[\s-]progress|current\s+task)[:\s]+[`']?(?<id>[A-Za-z0-9_-]+)[`']?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InProgressPattern();

    [GeneratedRegex(@"(?:completed|done)[:\s]+(?<ids>[A-Za-z0-9_,\s-]+?)(?:\n|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CompletedPattern();

    [GeneratedRegex(@"^#{1,3}\s+(?:next\s+task|next\s+recommended|next\s+up)[^\n]*\n+[`']?(?<id>[A-Za-z0-9_-]+)[`']?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex NextTaskSectionPattern();

    public StatusSummary Parse(string statusContent)
    {
        if (string.IsNullOrWhiteSpace(statusContent))
        {
            return new StatusSummary();
        }

        var nextTaskId = ExtractNextTaskId(statusContent);
        var inProgressIds = ExtractInProgressIds(statusContent);
        var completedIds = ExtractCompletedIds(statusContent);

        return new StatusSummary
        {
            NextRecommendedTaskId = nextTaskId,
            InProgressTaskIds = inProgressIds,
            CompletedTaskIds = completedIds
        };
    }

    private static string? ExtractNextTaskId(string content)
    {
        var sectionMatch = NextTaskSectionPattern().Match(content);
        if (sectionMatch.Success)
        {
            return sectionMatch.Groups["id"].Value.Trim();
        }

        var lineMatch = NextTaskIdPattern().Match(content);
        return lineMatch.Success ? lineMatch.Groups["id"].Value.Trim() : null;
    }

    private static IReadOnlyList<string> ExtractInProgressIds(string content)
    {
        return InProgressPattern()
            .Matches(content)
            .Select(m => m.Groups["id"].Value.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractCompletedIds(string content)
    {
        var ids = new List<string>();
        foreach (Match match in CompletedPattern().Matches(content))
        {
            var parts = match.Groups["ids"].Value
                .Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries);
            ids.AddRange(parts
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 40));
        }

        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
