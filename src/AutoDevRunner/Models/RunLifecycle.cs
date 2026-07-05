namespace AutoDevRunner.Models;

/// <summary>
/// The lifecycle a task moves through in one AutoDev run. A run records the
/// furthest stage it reached; the dashboard renders it as a progress track.
///   idea → candidate → planned → running → validated → committed → reported → learned
/// </summary>
public enum LifecycleStage
{
    /// <summary>A raw idea captured (e.g. from IDEAS.md / a previous run's IDEAS).</summary>
    Idea = 0,
    /// <summary>An idea promoted to a candidate task for consideration.</summary>
    Candidate = 1,
    /// <summary>A concrete task chosen and planned for this run.</summary>
    Planned = 2,
    /// <summary>The provider is actively working the task.</summary>
    Running = 3,
    /// <summary>Changes produced and validation (build/tests) passed.</summary>
    Validated = 4,
    /// <summary>Changes committed to the working branch.</summary>
    Committed = 5,
    /// <summary>Run report written (and emailed if configured).</summary>
    Reported = 6,
    /// <summary>Project memory updated with what was learned this run.</summary>
    Learned = 7,
    /// <summary>Terminal failure (error, guardrail/validation fail, or blocked).</summary>
    Failed = 8
}

/// <summary>
/// How risky a run's changes are. V1 only detects and surfaces this — there is
/// no blocking approval gate yet.
/// </summary>
public enum RiskLevel
{
    /// <summary>Additive, low-blast-radius changes (assets, docs, tests only).</summary>
    Safe = 0,
    /// <summary>Ordinary source changes.</summary>
    Normal = 1,
    /// <summary>Deletes files, DB migrations, deploy/production, or secret/config edits.</summary>
    Risky = 2
}
