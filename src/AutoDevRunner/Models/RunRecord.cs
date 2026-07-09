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

    // --- Reproducibility / evolution history (queryable in the DB, not just in files) ---

    /// <summary>Short title of the task this run worked on.</summary>
    public string? TaskTitle { get; set; }

    /// <summary>Where the task came from: explicit | planner | proposal source.</summary>
    public string? TaskSource { get; set; }

    /// <summary>The full prompt sent to the provider this run. Lets us diff prompt evolution over time.</summary>
    public string? Prompt { get; set; }

    /// <summary>The OpenAI creative plan used this run (null if the planner was disabled/failed).</summary>
    public string? CreativePlan { get; set; }

    /// <summary>Lifecycle stage reached (Planned/Running/Validated/Committed/…). Mirror of the file sidecar.</summary>
    public string? Stage { get; set; }

    /// <summary>Assessed risk level for this run (Safe/Normal/Risky).</summary>
    public string? Risk { get; set; }

    // --- Queryable usage/session metrics (also mirrored to the run sidecar) ---

    public int? PromptChars { get; set; }
    public int? PromptEstTokens { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public string? Model { get; set; }
    public string? Tier { get; set; }
    public bool? Resumed { get; set; }
    public int? RepairAttempts { get; set; }
    public string? SessionResult { get; set; }
    public bool? ValidationInferred { get; set; }
    public bool? NotVerified { get; set; }
}
