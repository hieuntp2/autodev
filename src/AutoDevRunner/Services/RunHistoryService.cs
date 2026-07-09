namespace AutoDevRunner.Services;

/// <summary>One prior run distilled to the facts the learning loop needs.</summary>
public record RunLesson(
    int RunId,
    DateTime StartedAt,
    string? Task,
    string Status,
    bool Failed,
    string? Reason,
    bool ValidationRun,
    bool ValidationPassed,
    IReadOnlyList<string> NextSuggestedTasks);

/// <summary>
/// What AutoDev has learned from recent runs of a project: the recent outcomes,
/// task titles that keep failing (so they are not retried blindly), and the
/// follow-up tasks prior runs suggested (so an empty backlog still has direction).
/// </summary>
public record RunLessons(
    IReadOnlyList<RunLesson> Recent,
    IReadOnlyList<string> RepeatedlyFailingTasks,
    IReadOnlyList<string> SuggestedNextTasks)
{
    public static readonly RunLessons Empty =
        new(Array.Empty<RunLesson>(), Array.Empty<string>(), Array.Empty<string>());

    public bool HasAny => Recent.Count > 0;

    /// <summary>True when <paramref name="title"/> matches a repeatedly-failing task.</summary>
    public bool IsRepeatedlyFailing(string? title) =>
        !string.IsNullOrWhiteSpace(title)
        && RepeatedlyFailingTasks.Any(t => Norm(t) == Norm(title!));

    /// <summary>Normalise a title for comparison: lowercased, whitespace-collapsed.</summary>
    internal static string Norm(string s) =>
        string.Join(' ', s.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Reads the run sidecars a project already writes (<see cref="RunMetadataStore"/>)
/// and turns the most recent ones into <see cref="RunLessons"/>. Pure over the
/// files on disk — no external calls — so the learning loop is deterministic and
/// costs nothing.
/// </summary>
public class RunHistoryService
{
    private readonly RunMetadataStore _store;

    public RunHistoryService(RunMetadataStore store) => _store = store;

    public RunLessons Analyze(string repoPath, int window, int failureThreshold)
    {
        window = Math.Max(1, window);
        failureThreshold = Math.Max(1, failureThreshold);

        var recentMeta = _store.ReadAllForRepo(repoPath).Take(window).ToList(); // newest first
        if (recentMeta.Count == 0) return RunLessons.Empty;

        var recent = recentMeta.Select(m => new RunLesson(
            m.RunId, m.StartedAt, m.Task,
            m.Status, IsFailure(m.Status), m.Reason,
            m.ValidationRun, m.ValidationPassed,
            (IReadOnlyList<string>)(m.NextSuggestedTasks ?? new List<string>()))).ToList();

        // Repeated failures: the same task title failed >= threshold times in the window.
        var repeatedlyFailing = recent
            .Where(r => r.Failed && !string.IsNullOrWhiteSpace(r.Task))
            .GroupBy(r => RunLessons.Norm(r.Task!))
            .Where(g => g.Count() >= failureThreshold)
            .Select(g => g.First().Task!.Trim())
            .ToList();

        // Follow-up tasks suggested by prior runs (newest first), minus failing ones.
        var seen = new HashSet<string>();
        var suggested = new List<string>();
        foreach (var r in recent)
        {
            foreach (var raw in r.NextSuggestedTasks)
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                if (repeatedlyFailing.Any(f => RunLessons.Norm(f) == RunLessons.Norm(t))) continue;
                if (seen.Add(RunLessons.Norm(t))) suggested.Add(t);
                if (suggested.Count >= 5) break;
            }
            if (suggested.Count >= 5) break;
        }

        return new RunLessons(recent, repeatedlyFailing, suggested);
    }

    private static bool IsFailure(string status) =>
        status.Equals("Failed", StringComparison.OrdinalIgnoreCase);
}
