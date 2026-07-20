using AutoDevRunner.Config;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>Which AI (if any) runs the PLAN step of a run.</summary>
public enum PlannerChoice
{
    None = 0,
    OpenAi = 1,
    Codex = 2,
    Claude = 3
}

public static class PlannerCallPolicy
{
    /// <summary>
    /// Parse a project's PlannerProvider setting. Empty or unknown tokens mean
    /// "use the global default" (null), so existing rows keep today's behavior.
    /// </summary>
    public static PlannerChoice? ParseChoice(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "none" or "off" or "heuristic" => PlannerChoice.None,
        "openai" => PlannerChoice.OpenAi,
        "codex" => PlannerChoice.Codex,
        "claude" => PlannerChoice.Claude,
        _ => null
    };

    /// <summary>
    /// The planner configured for this project, before the skip rule. A per-project
    /// choice overrides the global switch; the global AutoDev:Planner:Enabled only
    /// decides the default (OpenAI when on, none when off).
    /// </summary>
    public static PlannerChoice ResolveConfigured(PlannerOptions options, Project project) =>
        ParseChoice(project.PlannerProvider)
        ?? (options.Enabled ? PlannerChoice.OpenAi : PlannerChoice.None);

    /// <summary>
    /// The planner to actually call this run: the configured choice, unless the
    /// skip rule applies (a task is mid-flight and the last run succeeded).
    /// </summary>
    public static PlannerChoice Resolve(PlannerOptions options, Project project)
    {
        var configured = ResolveConfigured(options, project);
        if (configured is PlannerChoice.None) return PlannerChoice.None;
        return ShouldSkipForTaskInProgress(options, project) ? PlannerChoice.None : configured;
    }

    /// <summary>True when the in-progress-task skip rule applies this run.</summary>
    public static bool ShouldSkipForTaskInProgress(PlannerOptions options, Project project) =>
        options.SkipWhenTaskInProgress
        && !string.IsNullOrWhiteSpace(project.CurrentTask)
        && project.LastRunStatus is RunStatus.Success;

    public static bool ShouldCallPlanner(PlannerOptions options, Project project) =>
        Resolve(options, project) is not PlannerChoice.None;
}
