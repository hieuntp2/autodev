using AutoDevRunner.Data;
using AutoDevRunner.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoDevRunner.Services;

/// <summary>
/// Append-only brief version helpers. Editing a brief never overwrites: it adds a
/// new <see cref="ProjectBrief"/> version. The active brief is always the highest
/// version for the project. Stateless — operates on the caller's DbContext.
/// </summary>
public static class ProjectBriefService
{
    /// <summary>Latest brief content for a project, or null if none exists yet.</summary>
    public static Task<string?> GetLatestContentAsync(AppDbContext db, int projectId, CancellationToken ct = default) =>
        db.ProjectBriefs
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.Version)
            .Select(b => b.Content)
            .FirstOrDefaultAsync(ct);

    /// <summary>The latest brief version row (metadata + content), or null.</summary>
    public static Task<ProjectBrief?> GetLatestAsync(AppDbContext db, int projectId, CancellationToken ct = default) =>
        db.ProjectBriefs
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.Version)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Append a new brief version if the content is non-empty and differs from the
    /// current latest (whitespace-insensitive). Returns the new version, or null when
    /// nothing was added (empty or unchanged). Does NOT call SaveChanges — the caller
    /// commits, so this can join an existing unit of work.
    /// </summary>
    public static async Task<ProjectBrief?> AddVersionIfChangedAsync(
        AppDbContext db, int projectId, string? content, BriefAuthor author, string? note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var latest = await GetLatestAsync(db, projectId, ct);
        if (latest is not null && string.Equals(latest.Content.Trim(), content.Trim(), StringComparison.Ordinal))
            return null;

        var version = new ProjectBrief
        {
            ProjectId = projectId,
            Version = (latest?.Version ?? 0) + 1,
            Content = content.Trim(),
            Author = author,
            Note = note
        };
        db.ProjectBriefs.Add(version);
        return version;
    }
}
