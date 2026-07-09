using AutoDevRunner.Models;
using AutoDevRunner.Skills;

namespace AutoDevRunner.Services;

/// <summary>
/// A concrete, single-run-sized task proposal. Produced by the local heuristic
/// (<see cref="TaskProposer"/>) when no task is in progress and the OpenAI
/// planner is disabled or fails. Mirrors the fields the planner is asked to
/// emit so the prompt and dashboard can render it uniformly.
/// </summary>
public record TaskProposal(
    string Title,
    string Reason,
    string FilesLikelyAffected,
    string ExpectedOutput,
    string? ValidationCommand,
    RiskLevel Risk,
    string? SuggestedSkill,
    string Source); // "backlog" | "ideas" | "maintenance"

/// <summary>
/// Picks the next small task when a run has none, WITHOUT calling any external
/// service: it reads the project's BACKLOG.md, then IDEAS.md, then falls back to
/// a safe maintenance task. This is the deterministic fallback for the OpenAI
/// creative planner (which, when enabled, proposes the next task instead).
/// </summary>
public class TaskProposer
{
    private readonly RiskAssessor _risk;
    private readonly SkillRegistry _skills;

    public TaskProposer(RiskAssessor risk, SkillRegistry skills)
    {
        _risk = risk;
        _skills = skills;
    }

    public TaskProposal Propose(Project project, ProjectGoal goal, RunLessons? lessons = null)
    {
        lessons ??= RunLessons.Empty;

        // Prefer the top backlog/ideas item, but skip any title that has been
        // failing repeatedly so we do not keep re-attempting a broken approach.
        // When both are exhausted, fall back to a follow-up an earlier run
        // suggested (also skipping failing ones), then to safe maintenance.
        var (title, source) =
            FirstUsableItem(goal.Backlog, lessons, checkbox: true) is { } b ? (b, "backlog") :
            FirstUsableItem(goal.Ideas, lessons, checkbox: false) is { } i ? (i, "ideas") :
            FirstUsable(lessons.SuggestedNextTasks, lessons) is { } l ? (l, "learned") :
            ("Small maintenance pass: tidy code, improve docs/tests, and take one safe step toward the project goal.", "maintenance");

        var suggestedSkill = _skills.Match(title).FirstOrDefault()?.Skill.Id;
        var risk = _risk.Assess(Array.Empty<GitChange>(), title);

        var reason = source switch
        {
            "backlog" => "Top open item in .ai-runner/BACKLOG.md; advances the roadmap.",
            "ideas" => "First idea in .ai-runner/IDEAS.md worth trying toward the goal.",
            "learned" => "Follow-up suggested by a recent run (from its retrospective).",
            _ => "No backlog/ideas available; take a safe incremental step toward the goal."
        };

        return new TaskProposal(
            Title: title,
            Reason: reason,
            FilesLikelyAffected: "(the agent decides after reading the codebase)",
            ExpectedOutput: suggestedSkill is not null
                ? $"Concrete asset output from ${suggestedSkill}, validated."
                : "A small, committed, buildable change with tests where reasonable.",
            ValidationCommand: project.ValidationCommand,
            Risk: risk.Level,
            SuggestedSkill: suggestedSkill,
            Source: source);
    }

    /// <summary>
    /// First list item that is not a repeatedly-failing task. <paramref name="checkbox"/>
    /// prefers unchecked "- [ ]" items (backlog); otherwise any bullet (ideas).
    /// </summary>
    private static string? FirstUsableItem(string? markdown, RunLessons lessons, bool checkbox) =>
        FirstUsable(checkbox ? OpenItems(markdown) : Items(markdown), lessons);

    /// <summary>First candidate not flagged as repeatedly failing, if any.</summary>
    private static string? FirstUsable(IEnumerable<string> candidates, RunLessons lessons) =>
        candidates.FirstOrDefault(c => !lessons.IsRepeatedlyFailing(c));

    /// <summary>Unchecked "- [ ] item" lines; if none, all plain bullets.</summary>
    private static IEnumerable<string> OpenItems(string? markdown)
    {
        var open = Lines(markdown)
            .Where(l => l.StartsWith("- [ ]", StringComparison.OrdinalIgnoreCase))
            .Select(l => Clean(l[5..]))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        return open.Count > 0 ? open : Items(markdown);
    }

    private static IEnumerable<string> Items(string? markdown) =>
        Lines(markdown)
            .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => Clean(l[2..]))
            .Where(t => !string.IsNullOrWhiteSpace(t));

    private static IEnumerable<string> Lines(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? Enumerable.Empty<string>()
            : markdown.Split('\n').Select(l => l.Trim());

    private static string Clean(string s) =>
        s.TrimStart('[', ']', 'x', 'X', ' ', '-', '*').Trim();
}
