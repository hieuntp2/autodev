using System.Text.RegularExpressions;
using AutoDev.Core.Models;
using TaskStatus = AutoDev.Core.Models.TaskStatus;

namespace AutoDev.Core.Services;

public sealed partial class BacklogParser
{
    // - [ ] TASK-001: Title or - [x] TASK-001: Title
    [GeneratedRegex(@"^- \[(?<done>[xX ])\]\s+(?:(?<id>[A-Za-z0-9_-]+):\s+)?(?<title>.+)$", RegexOptions.Multiline)]
    private static partial Regex CheckboxPattern();

    // ## TASK-001: Title
    [GeneratedRegex(@"^#{1,3}\s+(?<id>[A-Za-z0-9_-]+):\s+(?<title>[^\n]+)", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    // Status: blocked\nBlocked: reason
    [GeneratedRegex(@"^Status:\s*(?<status>\w+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex StatusLinePattern();

    [GeneratedRegex(@"^Blocked:\s*(?<reason>.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockedReasonPattern();

    // 1. Title or 1) Title
    [GeneratedRegex(@"^(?<num>\d+)[.)]\s+(?<title>.+)$", RegexOptions.Multiline)]
    private static partial Regex NumberedPattern();

    // - Title (without checkbox)
    [GeneratedRegex(@"^- (?!\[)(?<title>.+)$", RegexOptions.Multiline)]
    private static partial Regex SimpleListPattern();

    public IReadOnlyList<TaskItem> Parse(string backlogContent)
    {
        if (string.IsNullOrWhiteSpace(backlogContent))
        {
            return [];
        }

        var checkboxItems = TryParseCheckboxFormat(backlogContent);
        if (checkboxItems.Count > 0)
        {
            return checkboxItems;
        }

        var headingItems = TryParseHeadingFormat(backlogContent);
        if (headingItems.Count > 0)
        {
            return headingItems;
        }

        var numberedItems = TryParseNumberedFormat(backlogContent);
        if (numberedItems.Count > 0)
        {
            return numberedItems;
        }

        return TryParseSimpleListFormat(backlogContent);
    }

    private static IReadOnlyList<TaskItem> TryParseCheckboxFormat(string content)
    {
        var items = new List<TaskItem>();
        foreach (Match match in CheckboxPattern().Matches(content))
        {
            var isDone = match.Groups["done"].Value.Trim().Equals("x", StringComparison.OrdinalIgnoreCase);
            var rawTitle = match.Groups["title"].Value.Trim();
            var rawId = match.Groups["id"].Value.Trim();

            var (id, title) = ExtractIdFromTitle(rawId, rawTitle, items.Count);
            var (status, blockedReason) = ParseInlineStatus(rawTitle, isDone);

            items.Add(new TaskItem
            {
                Id = id,
                Title = title,
                Status = status,
                BlockedReason = blockedReason,
                OriginalLineIndex = items.Count
            });
        }

        return items;
    }

    private static IReadOnlyList<TaskItem> TryParseHeadingFormat(string content)
    {
        var items = new List<TaskItem>();
        var headingMatches = HeadingPattern().Matches(content);
        if (headingMatches.Count == 0)
        {
            return [];
        }

        for (var i = 0; i < headingMatches.Count; i++)
        {
            var match = headingMatches[i];
            var id = match.Groups["id"].Value.Trim();
            var title = match.Groups["title"].Value.Trim();

            var sectionEnd = i + 1 < headingMatches.Count ? headingMatches[i + 1].Index : content.Length;
            var sectionContent = content[match.Index..sectionEnd];

            var statusMatch = StatusLinePattern().Match(sectionContent);
            var status = ParseStatusWord(statusMatch.Success ? statusMatch.Groups["status"].Value : "pending");

            string? blockedReason = null;
            if (status == TaskStatus.Blocked)
            {
                var blockedMatch = BlockedReasonPattern().Match(sectionContent);
                blockedReason = blockedMatch.Success ? blockedMatch.Groups["reason"].Value.Trim() : null;
            }

            items.Add(new TaskItem
            {
                Id = id,
                Title = title,
                Status = status,
                BlockedReason = blockedReason,
                OriginalLineIndex = i
            });
        }

        return items;
    }

    private static IReadOnlyList<TaskItem> TryParseNumberedFormat(string content)
    {
        var items = new List<TaskItem>();
        foreach (Match match in NumberedPattern().Matches(content))
        {
            var title = match.Groups["title"].Value.Trim();
            var id = $"TASK-{match.Groups["num"].Value}";
            items.Add(new TaskItem
            {
                Id = id,
                Title = title,
                Status = TaskStatus.Pending,
                OriginalLineIndex = items.Count
            });
        }

        return items;
    }

    private static IReadOnlyList<TaskItem> TryParseSimpleListFormat(string content)
    {
        var items = new List<TaskItem>();
        var counter = 1;
        foreach (Match match in SimpleListPattern().Matches(content))
        {
            var title = match.Groups["title"].Value.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            items.Add(new TaskItem
            {
                Id = $"TASK-{counter:D3}",
                Title = title,
                Status = TaskStatus.Pending,
                OriginalLineIndex = items.Count
            });
            counter++;
        }

        return items;
    }

    private static (string id, string title) ExtractIdFromTitle(string rawId, string rawTitle, int index)
    {
        if (!string.IsNullOrEmpty(rawId))
        {
            return (rawId, rawTitle);
        }

        var colonIdx = rawTitle.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 20)
        {
            var potentialId = rawTitle[..colonIdx].Trim();
            if (IsValidTaskId(potentialId))
            {
                return (potentialId, rawTitle[(colonIdx + 1)..].Trim());
            }
        }

        return ($"TASK-{index + 1:D3}", rawTitle);
    }

    private static bool IsValidTaskId(string s) =>
        s.Length > 0 && s.Length <= 30 && s.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

    private static (TaskStatus status, string? blockedReason) ParseInlineStatus(string title, bool isDone)
    {
        if (isDone)
        {
            return (TaskStatus.Done, null);
        }

        var lower = title.ToLowerInvariant();
        if (lower.Contains("[blocked]") || lower.StartsWith("blocked:", StringComparison.Ordinal))
        {
            return (TaskStatus.Blocked, null);
        }

        if (lower.Contains("[skip]") || lower.Contains("[skipped]"))
        {
            return (TaskStatus.Skipped, null);
        }

        if (lower.Contains("[in progress]") || lower.Contains("[wip]"))
        {
            return (TaskStatus.InProgress, null);
        }

        return (TaskStatus.Pending, null);
    }

    private static TaskStatus ParseStatusWord(string word) => word.ToLowerInvariant() switch
    {
        "done" or "complete" or "completed" or "finished" => TaskStatus.Done,
        "blocked" => TaskStatus.Blocked,
        "skipped" or "skip" => TaskStatus.Skipped,
        "in-progress" or "inprogress" or "in_progress" or "wip" => TaskStatus.InProgress,
        _ => TaskStatus.Pending
    };
}
