namespace AutoDevRunner.Models;

/// <summary>Who authored a brief version.</summary>
public enum BriefAuthor
{
    /// <summary>Edited by a human in the UI/API.</summary>
    User,
    /// <summary>Evolved autonomously by the AI during a run.</summary>
    Ai,
    /// <summary>Seeded once from the legacy on-disk brief file (BriefPath).</summary>
    Seed
}

/// <summary>
/// One immutable version of a project's brief (the product prompt sent to the
/// planner/implementer). Briefs are append-only: editing creates a new version,
/// history is never lost, and the run always uses the HIGHEST Version for a
/// project. This replaces reading the brief from ai-autonomous.md on disk.
/// </summary>
public class ProjectBrief
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>1-based, monotonically increasing per project. Latest = active.</summary>
    public int Version { get; set; }

    public string Content { get; set; } = string.Empty;

    public BriefAuthor Author { get; set; } = BriefAuthor.User;

    /// <summary>Optional reason/summary for this revision (e.g. why the AI evolved it).</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
