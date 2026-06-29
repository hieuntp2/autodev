namespace AutoDevRunner.Models;

/// <summary>A single execution of the runner against a project.</summary>
public class RunRecord
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public ProviderKind Provider { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Pending;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public string? Branch { get; set; }

    /// <summary>Why a run was paused/failed (quota reset time, auth message, etc.).</summary>
    public string? Reason { get; set; }

    /// <summary>AI-produced summary of what was done this run.</summary>
    public string? Summary { get; set; }

    /// <summary>Newline-separated list of changed files (from git).</summary>
    public string? ChangedFiles { get; set; }

    public bool ValidationRun { get; set; }
    public bool ValidationPassed { get; set; }
    public string? ValidationOutput { get; set; }

    /// <summary>Token/cost/usage if the provider exposes it, else "Unknown".</summary>
    public string? Usage { get; set; }

    /// <summary>Path to the full run log on disk.</summary>
    public string? LogPath { get; set; }

    public bool EmailSent { get; set; }
    public string? CommitSha { get; set; }
}
