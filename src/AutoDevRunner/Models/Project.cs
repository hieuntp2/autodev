using System.ComponentModel.DataAnnotations;

namespace AutoDevRunner.Models;

/// <summary>
/// A target project the runner can autonomously develop. Most behavior is
/// configured here; the brief file describes the product goal to the AI.
/// </summary>
public class Project
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to the git repository root.</summary>
    [Required]
    public string RepoPath { get; set; } = string.Empty;

    /// <summary>Path to the project brief (e.g. ai-autonomous.md), absolute or relative to RepoPath.</summary>
    public string BriefPath { get; set; } = "ai-autonomous.md";

    public bool Enabled { get; set; } = true;
    public bool Paused { get; set; } = false;

    /// <summary>Higher number = runs first.</summary>
    public int Priority { get; set; } = 0;

    // --- Provider selection ---
    /// <summary>Comma-separated provider order, e.g. "Codex,Claude".</summary>
    public string ProviderPriority { get; set; } = "Codex,Claude";

    // --- Git / safety policy ---
    /// <summary>Prefix for the per-run AI branch. Run id is appended.</summary>
    public string AiBranchPrefix { get; set; } = "ai/auto";
    public bool AllowRunOnMainBranch { get; set; } = false;
    public bool AutoCommit { get; set; } = true;
    public bool AutoPush { get; set; } = false;

    // --- Validation ---
    /// <summary>Shell command run after the AI finishes, e.g. "dotnet build". Empty to skip.</summary>
    public string? ValidationCommand { get; set; }

    // --- Limits ---
    public int MaxRunMinutes { get; set; } = 30;

    // --- Free-form ---
    public string? Notes { get; set; }

    // --- State carried between runs (for resume) ---
    public string? CurrentTask { get; set; }
    public string? LastSummary { get; set; }
    public string? CurrentBranch { get; set; }
    /// <summary>Provider session id if the CLI exposes one (for native resume).</summary>
    public string? ProviderSessionId { get; set; }

    // --- Denormalized last-run snapshot for quick dashboard reads ---
    public RunStatus? LastRunStatus { get; set; }
    public ProviderKind? LastProvider { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RunRecord> Runs { get; set; } = new();
}
