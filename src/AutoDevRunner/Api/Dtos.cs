using AutoDevRunner.Models;

namespace AutoDevRunner.Api;

/// <summary>Payload to create a project. Only Name + RepoPath are required.</summary>
public record CreateProjectDto(
    string Name,
    string RepoPath,
    string? BriefPath,
    int? Priority,
    string? ProviderPriority,
    string? ValidationCommand,
    int? MaxRunMinutes,
    bool? AutoCommit,
    bool? AutoPush,
    bool? AllowRunOnMainBranch,
    string? Notes);

/// <summary>Partial update. Null fields are left unchanged.</summary>
public record UpdateProjectDto(
    string? Name,
    string? RepoPath,
    string? BriefPath,
    bool? Enabled,
    bool? Paused,
    int? Priority,
    string? ProviderPriority,
    string? ValidationCommand,
    int? MaxRunMinutes,
    bool? AutoCommit,
    bool? AutoPush,
    bool? AllowRunOnMainBranch,
    string? Notes);

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
    string? CommitSha);

public record ProviderStatusDto(
    string Provider,
    bool Enabled,
    DateTime? LastSuccessAt,
    DateTime? LastAuthErrorAt,
    DateTime? LastQuotaLimitAt,
    string? LastQuotaResetHint,
    string? LastKnownUsage);

/// <summary>One directory entry in the folder picker. For drive roots, Name is e.g. "C:\".</summary>
public record DirEntryDto(string Name, string Path);

/// <summary>Folder-picker listing. Empty Path means the drive list; Parent is null at that level.</summary>
public record BrowseDto(string Path, string? Parent, IReadOnlyList<DirEntryDto> Dirs);

public static class DtoMappers
{
    public static RunSummaryDto ToDto(this RunRecord r, string projectName) => new(
        r.Id, r.ProjectId, projectName, r.Provider.ToString(), r.Status.ToString(),
        r.StartedAt, r.FinishedAt, r.Branch, r.Reason, r.Usage, r.EmailSent, r.CommitSha);
}
