using System.Text.Json.Serialization;

namespace AutoDevRunner.Models;

/// <summary>Kind of generated artifact, for grouping/preview on the dashboard.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArtifactKind
{
    Other = 0,
    Frame = 1,          // frames/frame_000.png
    SpriteSheet = 2,    // <id>_sheet.png
    Gif = 3,            // <id>_preview.gif
    AnimationManifest = 4, // <id>.animation.json
    Image = 5,          // any other png/jpg/webp
    Report = 6          // a generated report/markdown
}

/// <summary>A tracked output file produced during a run (relative to the repo).</summary>
public record ArtifactRef(
    string Path,
    ArtifactKind Kind,
    string? SkillId,
    long SizeBytes);

/// <summary>A skill used in a run and the keywords that selected it.</summary>
public record RunSkillRef(string Id, IReadOnlyList<string> MatchedKeywords);

/// <summary>
/// Machine-readable sidecar for a run, written next to the run's markdown log at
/// <c>&lt;repo&gt;/.ai-runner/runs/&lt;stamp&gt;-run&lt;id&gt;.json</c>. This is how the dashboard
/// reads task lifecycle, selected skills, risk and artifacts without any DB
/// schema change (the app uses EF EnsureCreated, so new columns would break
/// existing databases).
/// </summary>
public class RunMetadata
{
    public int RunId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Branch { get; set; }
    public string? CommitSha { get; set; }

    public string? Task { get; set; }

    public int? PromptChars { get; set; }
    public int? PromptEstTokens { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public string? Model { get; set; }

    /// <summary>Why the run failed/paused (if any). Read by the learning loop.</summary>
    public string? Reason { get; set; }

    /// <summary>Furthest <see cref="LifecycleStage"/> reached, as a string.</summary>
    public string Stage { get; set; } = LifecycleStage.Planned.ToString();

    /// <summary><see cref="RiskLevel"/> as a string.</summary>
    public string Risk { get; set; } = RiskLevel.Normal.ToString();
    public List<string> RiskReasons { get; set; } = new();

    public List<RunSkillRef> Skills { get; set; } = new();
    public List<ArtifactRef> Artifacts { get; set; } = new();

    public bool ValidationRun { get; set; }
    public bool ValidationPassed { get; set; }
    public int? RepairAttempts { get; set; }
    public string? SessionResult { get; set; }

    /// <summary>Project-memory files updated this run (e.g. IDEAS.md, DECISIONS.md).</summary>
    public List<string> MemoryUpdates { get; set; } = new();

    /// <summary>Follow-up tasks the provider suggested for next runs.</summary>
    public List<string> NextSuggestedTasks { get; set; } = new();

    /// <summary>How the run's task was chosen (explicit, backlog, ideas, planner, maintenance).</summary>
    public string? TaskSource { get; set; }
}
