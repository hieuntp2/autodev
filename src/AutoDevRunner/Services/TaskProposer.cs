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

    public TaskProposal Propose(Project project, ProjectGoal goal)
    {
        var (title, source) =
            FirstOpenItem(goal.Backlog) is { } b ? (b, "backlog") :
            FirstItem(goal.Ideas) is { } i ? (i, "ideas") :
            ("Small maintenance pass: tidy code, improve docs/tests, and take one safe step toward the project goal.", "maintenance");

        var suggestedSkill = _skills.Match(title).FirstOrDefault()?.Skill.Id;
        var risk = _risk.Assess(Array.Empty<GitChange>(), title);

        var reason = source switch
        {
            "backlog" => "Top open item in .ai-runner/BACKLOG.md; advances the roadmap.",
            "ideas" => "First idea in .ai-runner/IDEAS.md worth trying toward the goal.",
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

    /// <summary>First unchecked "- [ ] item" (or first bullet) in a markdown list.</summary>
    private static string? FirstOpenItem(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("- [ ]", StringComparison.OrdinalIgnoreCase))
                return Clean(line[5..]);
        }
        return FirstItem(markdown); // no checkbox list → first plain bullet
    }

    private static string? FirstItem(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var text = Clean(line[2..]);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        return null;
    }

    private static string Clean(string s) =>
        s.TrimStart('[', ']', 'x', 'X', ' ', '-', '*').Trim();
}
