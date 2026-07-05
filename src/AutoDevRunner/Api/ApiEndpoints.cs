using AutoDevRunner.Config;
using AutoDevRunner.Data;
using AutoDevRunner.Models;
using AutoDevRunner.Providers;
using AutoDevRunner.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Api;

public static class ApiEndpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ---- Overview ----
        api.MapGet("/overview", async (AppDbContext db, RunLock runLock, ProviderRegistry providers) =>
        {
            var projects = await db.Projects.AsNoTracking().ToListAsync();
            var lastRun = await db.Runs.AsNoTracking()
                .OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync();
            var lastRunProjectName = lastRun is null ? null
                : projects.FirstOrDefault(p => p.Id == lastRun.ProjectId)?.Name ?? "?";

            var states = await db.ProviderStates.AsNoTracking().ToListAsync();
            var providerDtos = providers.All.Select(p =>
            {
                var s = states.FirstOrDefault(x => x.Provider == p.Kind);
                return new ProviderStatusDto(p.Kind.ToString(), p.IsEnabled,
                    s?.LastSuccessAt, s?.LastAuthErrorAt, s?.LastQuotaLimitAt,
                    s?.LastQuotaResetHint, s?.LastKnownUsage);
            });

            return Results.Ok(new OverviewDto(
                TotalProjects: projects.Count,
                EnabledProjects: projects.Count(p => p.Enabled),
                PausedProjects: projects.Count(p => p.Paused),
                RunningProjects: runLock.ActiveProjectIds.Count,
                LastRun: lastRun?.ToDto(lastRunProjectName!),
                LastProvider: lastRun?.Provider.ToString(),
                LastError: projects.Where(p => p.LastError != null)
                    .OrderByDescending(p => p.LastRunAt).FirstOrDefault()?.LastError,
                LastUsage: lastRun?.Usage,
                Providers: providerDtos));
        });

        // ---- Projects ----
        api.MapGet("/projects", async (AppDbContext db, RunLock runLock) =>
        {
            var projects = await db.Projects.AsNoTracking()
                .OrderByDescending(p => p.Priority).ThenBy(p => p.Id).ToListAsync();
            return Results.Ok(projects.Select(p => ProjectView(p, runLock.IsRunning(p.Id))));
        });

        api.MapGet("/projects/{id:int}", async (int id, AppDbContext db, RunLock runLock) =>
        {
            var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            var recentRuns = await db.Runs.AsNoTracking()
                .Where(r => r.ProjectId == id)
                .OrderByDescending(r => r.StartedAt).Take(10)
                .ToListAsync();
            return Results.Ok(new
            {
                project = ProjectView(p, runLock.IsRunning(p.Id), full: true),
                recentRuns = recentRuns.Select(r => r.ToDto(p.Name))
            });
        });

        api.MapPost("/projects", async (CreateProjectDto dto, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.RepoPath))
                return Results.BadRequest("Name and RepoPath are required.");
            if (await db.Projects.AnyAsync(p => p.Name == dto.Name))
                return Results.Conflict($"Project '{dto.Name}' already exists.");

            var p = new Project
            {
                Name = dto.Name.Trim(),
                RepoPath = dto.RepoPath.Trim(),
                BriefPath = string.IsNullOrWhiteSpace(dto.BriefPath) ? "ai-autonomous.md" : dto.BriefPath!,
                Priority = dto.Priority ?? 0,
                ProviderPriority = string.IsNullOrWhiteSpace(dto.ProviderPriority) ? "Codex,Claude" : dto.ProviderPriority!,
                ValidationCommand = dto.ValidationCommand,
                MaxRunMinutes = dto.MaxRunMinutes ?? 30,
                AutoCommit = dto.AutoCommit ?? true,
                AutoPush = dto.AutoPush ?? false,
                AllowRunOnMainBranch = dto.AllowRunOnMainBranch ?? false,
                Notes = dto.Notes
            };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            return Results.Created($"/api/projects/{p.Id}", ProjectView(p, false, full: true));
        });

        api.MapPut("/projects/{id:int}", async (int id, UpdateProjectDto dto, AppDbContext db) =>
        {
            var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();

            if (dto.Name is not null) p.Name = dto.Name.Trim();
            if (dto.RepoPath is not null) p.RepoPath = dto.RepoPath.Trim();
            if (dto.BriefPath is not null) p.BriefPath = dto.BriefPath;
            if (dto.Enabled is not null) p.Enabled = dto.Enabled.Value;
            if (dto.Paused is not null) p.Paused = dto.Paused.Value;
            if (dto.Priority is not null) p.Priority = dto.Priority.Value;
            if (dto.ProviderPriority is not null) p.ProviderPriority = dto.ProviderPriority;
            if (dto.ValidationCommand is not null) p.ValidationCommand = dto.ValidationCommand;
            if (dto.MaxRunMinutes is not null) p.MaxRunMinutes = dto.MaxRunMinutes.Value;
            if (dto.AutoCommit is not null) p.AutoCommit = dto.AutoCommit.Value;
            if (dto.AutoPush is not null) p.AutoPush = dto.AutoPush.Value;
            if (dto.AllowRunOnMainBranch is not null) p.AllowRunOnMainBranch = dto.AllowRunOnMainBranch.Value;
            if (dto.Notes is not null) p.Notes = dto.Notes;

            await db.SaveChangesAsync();
            return Results.Ok(ProjectView(p, false, full: true));
        });

        api.MapDelete("/projects/{id:int}", async (int id, AppDbContext db) =>
        {
            var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            db.Projects.Remove(p);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---- Project actions ----
        api.MapPost("/projects/{id:int}/run", (int id, RunLauncher launcher) =>
            launcher.TryLaunch(id)
                ? Results.Accepted($"/api/projects/{id}", new { started = true })
                : Results.Conflict(new { started = false, reason = "Already running." }));

        api.MapPost("/projects/{id:int}/pause", (int id, AppDbContext db) => SetFlag(db, id, p => p.Paused = true));
        api.MapPost("/projects/{id:int}/resume", (int id, AppDbContext db) => SetFlag(db, id, p => p.Paused = false));
        api.MapPost("/projects/{id:int}/enable", (int id, AppDbContext db) => SetFlag(db, id, p => p.Enabled = true));
        api.MapPost("/projects/{id:int}/disable", (int id, AppDbContext db) => SetFlag(db, id, p => p.Enabled = false));

        // ---- Runs ----
        api.MapGet("/runs", async (AppDbContext db, int? projectId, int? take) =>
        {
            var q = db.Runs.AsNoTracking().AsQueryable();
            if (projectId is not null) q = q.Where(r => r.ProjectId == projectId);
            var runs = await q.OrderByDescending(r => r.StartedAt).Take(take ?? 100).ToListAsync();
            var names = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);
            return Results.Ok(runs.Select(r => r.ToDto(names.GetValueOrDefault(r.ProjectId, "?"))));
        });

        api.MapGet("/runs/{id:int}", async (int id, AppDbContext db) =>
        {
            var r = await db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            var name = (await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.ProjectId))?.Name ?? "?";
            return Results.Ok(new
            {
                run = r.ToDto(name),
                summary = r.Summary,
                changedFiles = r.ChangedFiles,
                validationOutput = r.ValidationOutput,
                validationRun = r.ValidationRun,
                validationPassed = r.ValidationPassed,
                logPath = r.LogPath
            });
        });

        api.MapGet("/runs/{id:int}/log", async (int id, AppDbContext db) =>
        {
            var r = await db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (r is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(r.LogPath) || !File.Exists(r.LogPath))
                return Results.Ok(new { log = "(log file not found)" });
            var content = await File.ReadAllTextAsync(r.LogPath);
            return Results.Ok(new { log = content });
        });

        // ---- Providers ----
        api.MapGet("/providers", async (AppDbContext db, ProviderRegistry providers) =>
        {
            var states = await db.ProviderStates.AsNoTracking().ToListAsync();
            return Results.Ok(providers.All.Select(p =>
            {
                var s = states.FirstOrDefault(x => x.Provider == p.Kind);
                return new ProviderStatusDto(p.Kind.ToString(), p.IsEnabled,
                    s?.LastSuccessAt, s?.LastAuthErrorAt, s?.LastQuotaLimitAt,
                    s?.LastQuotaResetHint, s?.LastKnownUsage);
            }));
        });

        // ---- Filesystem browse (folder picker for repo path) ----
        api.MapGet("/fs/browse", (string? path) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new DirEntryDto(d.Name, d.RootDirectory.FullName))
                    .ToList();
                return Results.Ok(new BrowseDto("", null, drives));
            }

            try
            {
                var full = Path.GetFullPath(path);
                if (!Directory.Exists(full))
                    return Results.NotFound($"Directory not found: {full}");

                var parent = Directory.GetParent(full)?.FullName ?? "";
                var dirs = new DirectoryInfo(full).EnumerateDirectories()
                    .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new DirEntryDto(d.Name, d.FullName))
                    .ToList();
                return Results.Ok(new BrowseDto(full, parent, dirs));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem("Access denied.", statusCode: StatusCodes.Status403Forbidden);
            }
        });

        // ---- Scheduler settings (read-only view of config) ----
        api.MapGet("/settings", (IOptions<AutoDevOptions> opt) =>
        {
            var o = opt.Value;
            return Results.Ok(new
            {
                scheduler = o.Scheduler,
                email = new { o.Email.Enabled, To = o.Email.ToEmail, Provider = "EmailJS", o.Email.ServiceId },
                providers = new
                {
                    codex = new { o.Providers.Codex.Enabled, o.Providers.Codex.Command },
                    claude = new { o.Providers.Claude.Enabled, o.Providers.Claude.Command }
                }
            });
        });
    }

    private static async Task<IResult> SetFlag(AppDbContext db, int id, Action<Project> mutate)
    {
        var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return Results.NotFound();
        mutate(p);
        await db.SaveChangesAsync();
        return Results.Ok(new { p.Id, p.Enabled, p.Paused });
    }

    private static object ProjectView(Project p, bool isRunning, bool full = false)
    {
        if (!full)
            return new
            {
                p.Id, p.Name, p.RepoPath, p.Enabled, p.Paused, p.Priority,
                LastRunStatus = p.LastRunStatus?.ToString(),
                LastProvider = p.LastProvider?.ToString(),
                p.LastRunAt, p.CurrentBranch, p.CurrentTask, isRunning
            };

        return new
        {
            p.Id, p.Name, p.RepoPath, p.BriefPath, p.Enabled, p.Paused, p.Priority,
            p.ProviderPriority, p.ValidationCommand, p.MaxRunMinutes,
            p.AutoCommit, p.AutoPush, p.AllowRunOnMainBranch, p.AiBranchPrefix,
            p.Notes, p.CurrentTask, p.LastSummary, p.CurrentBranch, p.ProviderSessionId,
            LastRunStatus = p.LastRunStatus?.ToString(),
            LastProvider = p.LastProvider?.ToString(),
            p.LastRunAt, p.LastError, isRunning
        };
    }
}
