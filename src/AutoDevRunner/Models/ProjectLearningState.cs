namespace AutoDevRunner.Models;

/// <summary>Durable per-project learning counters updated after every run.</summary>
public class ProjectLearningState
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int TotalRuns { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
    public int NotVerified { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public double RollingSuccessRate { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Durable per-task outcome statistics, keyed by normalized title.</summary>
public class ProjectTaskStat
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string TaskKeyNormalized { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int Failures { get; set; }
    public string LastOutcome { get; set; } = string.Empty;
    public DateTime LastAttemptAt { get; set; } = DateTime.UtcNow;
}
