using AutoDevRunner.Data;
using AutoDevRunner.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoDevRunner.Services;

public static class ProjectPromptService
{
    public static Task<string?> GetLatestContentAsync(AppDbContext db, int projectId, CancellationToken ct = default) =>
        db.PromptDirectives
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.Version)
            .Select(p => p.Content)
            .FirstOrDefaultAsync(ct);

    public static Task<PromptDirective?> GetLatestAsync(AppDbContext db, int projectId, CancellationToken ct = default) =>
        db.PromptDirectives
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

    public static Task<List<PromptDirective>> GetHistoryAsync(AppDbContext db, int projectId, CancellationToken ct = default) =>
        db.PromptDirectives
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);

    public static async Task<PromptDirective?> AddVersionIfChangedAsync(
        AppDbContext db,
        int projectId,
        string? content,
        PromptDirectiveAuthor author,
        string? note,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var latest = await GetLatestAsync(db, projectId, ct);
        if (latest is not null && string.Equals(latest.Content.Trim(), content.Trim(), StringComparison.Ordinal))
            return null;

        var version = new PromptDirective
        {
            ProjectId = projectId,
            Version = (latest?.Version ?? 0) + 1,
            Content = content.Trim(),
            Author = author,
            Note = note
        };
        db.PromptDirectives.Add(version);
        return version;
    }

    public static async Task<PromptDirective?> RevertAsNewVersionAsync(
        AppDbContext db,
        int projectId,
        int version,
        CancellationToken ct = default)
    {
        var target = await db.PromptDirectives
            .Where(p => p.ProjectId == projectId && p.Version == version)
            .FirstOrDefaultAsync(ct);
        if (target is null) return null;

        return await AddVersionIfChangedAsync(
            db,
            projectId,
            target.Content,
            PromptDirectiveAuthor.User,
            $"reverted to version {version}",
            ct);
    }
}
