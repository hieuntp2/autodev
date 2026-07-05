using System.Text;

namespace AutoDevRunner.Services;

/// <summary>
/// After a run, folds what the AI reported into the project's long-lived memory
/// files under <c>&lt;repo&gt;/.ai-runner/</c>:
///   IDEAS.md     — new ideas (summary IDEAS)
///   BACKLOG.md   — next task / follow-ups (NEXT_TASK / NEXT_SUGGESTED_TASKS / PENDING)
///   DECISIONS.md — decisions/learnings (MEMORY_UPDATES, else DONE/TASK)
/// Entries are appended under a dated <c>## AutoDev YYYY-MM-DD</c> section,
/// deduplicated (a normalised line already present is skipped) and capped so
/// memory never grows unbounded.
///
/// Writing is GATED by <c>AutoDev:ProjectMemory:AutoWriteEnabled</c>: when the
/// caller passes <c>autoWrite: false</c> this method does nothing (the run log
/// still records what would have been written). Fail-soft.
/// </summary>
public class ProjectMemoryWriter
{
    private readonly ILogger<ProjectMemoryWriter> _log;
    private const int MaxEntries = 60; // keep the newest N bullet lines per file

    public ProjectMemoryWriter(ILogger<ProjectMemoryWriter> log) => _log = log;

    /// <summary>
    /// Update memory files from a parsed summary. Returns the files actually
    /// changed (relative names). No-op (empty) when <paramref name="autoWrite"/>
    /// is false. <paramref name="dateStamp"/> is passed in (not read from the
    /// clock) so the operation is deterministic and testable.
    /// </summary>
    public async Task<List<string>> UpdateAsync(string repoPath, ParsedSummary? summary,
        string dateStamp, bool autoWrite, CancellationToken ct = default)
    {
        var updated = new List<string>();
        if (summary is null || !autoWrite) return updated;

        try
        {
            var dir = Path.Combine(repoPath, ProjectGoalService.Dir);
            Directory.CreateDirectory(dir);

            if (await AppendAsync(Path.Combine(dir, ProjectGoalService.IdeasFile), "# Ideas", dateStamp, summary.Ideas, ct))
                updated.Add(ProjectGoalService.IdeasFile);

            var next = summary.NextTask ?? summary.NextSuggestedTasks ?? summary.Pending;
            if (await AppendAsync(Path.Combine(dir, ProjectGoalService.BacklogFile), "# Backlog", dateStamp, next, ct))
                updated.Add(ProjectGoalService.BacklogFile);

            var decisions = summary.MemoryUpdates ?? summary.Done ?? summary.Task;
            if (await AppendAsync(Path.Combine(dir, ProjectGoalService.DecisionsFile), "# Decisions", dateStamp, decisions, ct))
                updated.Add(ProjectGoalService.DecisionsFile);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Project memory update failed for {Repo}.", repoPath);
        }

        return updated;
    }

    /// <summary>
    /// Append new bullets (from <paramref name="text"/>) under a dated section,
    /// skipping duplicates and capping total bullets. Returns true if changed.
    /// </summary>
    private static async Task<bool> AppendAsync(string path, string header, string dateStamp, string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var bullets = SplitToBullets(text);
        if (bullets.Count == 0) return false;

        var lines = File.Exists(path)
            ? (await File.ReadAllLinesAsync(path, ct)).ToList()
            : new List<string> { header };

        var existing = lines.Where(IsBullet).Select(Normalize).ToHashSet();
        var toAdd = bullets.Select(b => "- " + b.Trim())
            .Where(l => existing.Add(Normalize(l)))
            .ToList();
        if (toAdd.Count == 0) return false;

        var sectionHeading = $"## AutoDev {dateStamp}";
        if (!lines.Any(l => l.Trim() == sectionHeading))
        {
            if (lines.Count > 0 && lines[^1].Length != 0) lines.Add("");
            lines.Add(sectionHeading);
        }
        lines.AddRange(toAdd);

        // Cap: drop the oldest bullet lines if we exceed the limit.
        var bulletCount = lines.Count(IsBullet);
        if (bulletCount > MaxEntries)
        {
            var drop = bulletCount - MaxEntries;
            var kept = new List<string>();
            foreach (var l in lines)
            {
                if (drop > 0 && IsBullet(l)) { drop--; continue; }
                kept.Add(l);
            }
            lines = kept;
        }

        await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", ct);
        return true;
    }

    private static bool IsBullet(string line) => line.TrimStart().StartsWith("- ");

    private static List<string> SplitToBullets(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.TrimStart('-', '*', '•', ' ').Trim())
            .Where(l => l.Length > 0
                        && !l.Equals("none", StringComparison.OrdinalIgnoreCase)
                        && !l.Equals("-", StringComparison.Ordinal))
            .ToList();

    /// <summary>Normalise for dedupe: strip bullet, lowercase, collapse whitespace.</summary>
    private static string Normalize(string line)
    {
        var s = line.TrimStart('-', '*', '•', ' ').Trim().ToLowerInvariant();
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
