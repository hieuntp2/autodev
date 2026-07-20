using AutoDevRunner.Models;

namespace AutoDevRunner.Api;

/// <summary>Payload to create a project. Only Name + RepoPath are required.</summary>
public record CreateProjectDto(
    string Name,
    string RepoPath,
    string? BriefPath,
    string? Brief,
    string? ProjectType,
    int? Priority,
    string? ProviderPriority,
    string? PlannerProvider,
    string? ValidationCommand,
    int? MaxRunMinutes,
    bool? AutoCommit,
    bool? AutoPush,
    bool? AllowRunOnMainBranch,
    bool? AllowAiEditBrief,
    bool? AllowAiEditSettings,
    string? Notes);

/// <summary>Partial update. Null fields are left unchanged.</summary>
public record UpdateProjectDto(
    string? Name,
    string? RepoPath,
    string? BriefPath,
    string? Brief,
    string? ProjectType,
    bool? Enabled,
    bool? Paused,
    int? Priority,
    string? ProviderPriority,
    string? PlannerProvider,
    string? ValidationCommand,
    int? MaxRunMinutes,
    bool? AutoCommit,
    bool? AutoPush,
    bool? AllowRunOnMainBranch,
    bool? AllowAiEditBrief,
    bool? AllowAiEditSettings,
    string? Notes);

/// <summary>One brief version for the history view.</summary>
public record BriefVersionDto(
    int Version,
    string Author,
    string? Note,
    DateTime CreatedAt,
    string Content);

public record PromptDirectiveVersionDto(
    int Version,
    string Author,
    string? Note,
    DateTime CreatedAt,
    string Content);

public record UpdatePromptDirectiveDto(string Content, string? Note);

public record OverviewDto(
    int TotalProjects,
    int EnabledProjects,
    int PausedProjects,
    int RunningProjects,
    RunSummaryDto? LastRun,
    string? LastProvider,
    string? LastError,
    string? LastUsage,
    IEnumerable<ProviderStatusDto> Providers);

public record RunSummaryDto(
    int Id,
    int ProjectId,
    string ProjectName,
    string Provider,
    string Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? Branch,
    string? Reason,
    string? Usage,
    bool EmailSent,
    string? CommitSha,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    string? Tier,
    bool? Resumed,
    bool? NotVerified);

public record ProviderStatusDto(
    string Provider,
    bool Enabled,
    DateTime? LastSuccessAt,
    DateTime? LastAuthErrorAt,
    DateTime? LastQuotaLimitAt,
    string? LastQuotaResetHint,
    string? LastKnownUsage);

/// <summary>Project goal-layer status + content for the dashboard.</summary>
public record ProjectGoalDto(
    bool HasGoal,
    IReadOnlyDictionary<string, bool> Files,
    string? Goal,
    string? Roadmap,
    string? Backlog,
    string? Ideas,
    string? Decisions);

/// <summary>A global skill as shown on the dashboard.</summary>
public record SkillDto(
    string Id,
    string Name,
    string Version,
    string Description,
    bool Enabled,
    IReadOnlyList<string> Triggers,
    string InvocationHint,
    string SourcePath);

/// <summary>A skill as seen from one project: global state plus the per-project toggle.</summary>
public record ProjectSkillDto(
    string Id,
    string Name,
    string Version,
    string Description,
    bool EnabledGlobal,
    bool DisabledForProject,
    bool EnabledForProject,
    IReadOnlyList<string> Triggers);

/// <summary>A logged skill-selection decision (which skill for which task).</summary>
public record SkillSelectionDto(
    string SkillId,
    string SkillName,
    IReadOnlyList<string> MatchedKeywords,
    string ProjectName,
    string? Task,
    DateTime SelectedAt);

/// <summary>One directory entry in the folder picker. For drive roots, Name is e.g. "C:\".</summary>
public record DirEntryDto(string Name, string Path);

/// <summary>Folder-picker listing. Empty Path means the drive list; Parent is null at that level.</summary>
public record BrowseDto(string Path, string? Parent, IReadOnlyList<DirEntryDto> Dirs);

public static class DtoMappers
{
    public static RunSummaryDto ToDto(this RunRecord r, string projectName) => new(
        r.Id, r.ProjectId, projectName, r.Provider.ToString(), r.Status.ToString(),
        r.StartedAt, r.FinishedAt, r.Branch, r.Reason, r.Usage, r.EmailSent, r.CommitSha,
        r.InputTokens, r.OutputTokens, r.CostUsd, r.Tier, r.Resumed, r.NotVerified);
}
