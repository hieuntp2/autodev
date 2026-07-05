using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>The project's goal-layer files, loaded from <c>&lt;repo&gt;/.ai-runner/</c>.</summary>
public record ProjectGoal(
    string? Goal,        // PROJECT_GOAL.md
    string? Roadmap,     // ROADMAP.md
    string? Backlog,     // BACKLOG.md
    string? Ideas,       // IDEAS.md
    string? Decisions)   // DECISIONS.md
{
    public bool HasGoal => !string.IsNullOrWhiteSpace(Goal);
    public bool HasAny => HasGoal
        || !string.IsNullOrWhiteSpace(Roadmap) || !string.IsNullOrWhiteSpace(Backlog)
        || !string.IsNullOrWhiteSpace(Ideas) || !string.IsNullOrWhiteSpace(Decisions);
}

/// <summary>
/// Reads the Project Goal Layer that turns AutoDev from a one-shot runner into a
/// goal-directed one. All files live under <c>&lt;repo&gt;/.ai-runner/</c> and are
/// optional; PROJECT_GOAL.md is the primary one.
/// </summary>
public class ProjectGoalService
{
    public const string Dir = ".ai-runner";
    public const string GoalFile = "PROJECT_GOAL.md";
    public const string RoadmapFile = "ROADMAP.md";
    public const string BacklogFile = "BACKLOG.md";
    public const string IdeasFile = "IDEAS.md";
    public const string DecisionsFile = "DECISIONS.md";

    public string GoalDir(string repoPath) => Path.Combine(repoPath, Dir);

    public async Task<ProjectGoal> LoadAsync(string repoPath, CancellationToken ct = default)
    {
        var dir = GoalDir(repoPath);
        async Task<string?> Read(string name)
        {
            var p = Path.Combine(dir, name);
            return File.Exists(p) ? (await File.ReadAllTextAsync(p, ct)).Trim() : null;
        }

        return new ProjectGoal(
            Goal: await Read(GoalFile),
            Roadmap: await Read(RoadmapFile),
            Backlog: await Read(BacklogFile),
            Ideas: await Read(IdeasFile),
            Decisions: await Read(DecisionsFile));
    }

    /// <summary>Which goal-layer files exist (for the dashboard).</summary>
    public IReadOnlyDictionary<string, bool> Presence(string repoPath)
    {
        var dir = GoalDir(repoPath);
        return new Dictionary<string, bool>
        {
            [GoalFile] = File.Exists(Path.Combine(dir, GoalFile)),
            [RoadmapFile] = File.Exists(Path.Combine(dir, RoadmapFile)),
            [BacklogFile] = File.Exists(Path.Combine(dir, BacklogFile)),
            [IdeasFile] = File.Exists(Path.Combine(dir, IdeasFile)),
            [DecisionsFile] = File.Exists(Path.Combine(dir, DecisionsFile)),
        };
    }
}
