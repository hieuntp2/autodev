using System.Text.Json;
using AutoDevRunner.Config;
using AutoDevRunner.Data;
using AutoDevRunner.Models;
using AutoDevRunner.Providers;
using AutoDevRunner.Services;
using AutoDevRunner.Skills;
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
            var runningProjectIds = runLock.ActiveProjectIdsFor(projects).ToHashSet();
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
                RunningProjects: runningProjectIds.Count,
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
            return Results.Ok(projects.Select(p => ProjectView(p, runLock.IsRunning(p))));
        });

        api.MapGet("/projects/{id:int}", async (int id, AppDbContext db, RunLock runLock) =>
        {
            var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            var recentRuns = await db.Runs.AsNoTracking()
                .Where(r => r.ProjectId == id)
                .OrderByDescending(r => r.StartedAt).Take(10)
                .ToListAsync();
            var brief = await ProjectBriefService.GetLatestAsync(db, id);
            return Results.Ok(new
            {
                project = ProjectView(p, runLock.IsRunning(p), full: true),
                brief = brief is null ? null : new { brief.Version, Author = brief.Author.ToString(), brief.CreatedAt, brief.Content },
                recentRuns = recentRuns.Select(r => r.ToDto(p.Name))
            });
        });

        api.MapGet("/projects/{id:int}/metrics", async (int id, AppDbContext db, int? take) =>
        {
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == id))
                return Results.NotFound();
            var runs = await db.Runs.AsNoTracking()
                .Where(r => r.ProjectId == id)
                .ToListAsync();
            return Results.Ok(RunMetricsAggregator.Aggregate(runs, take ?? 20));
        });

        api.MapGet("/projects/{id:int}/prompt", async (int id, AppDbContext db) =>
        {
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == id))
                return Results.NotFound();
            var prompt = await ProjectPromptService.GetLatestAsync(db, id);
            return Results.Ok(prompt is null ? null : PromptDto(prompt));
        });

        api.MapGet("/projects/{id:int}/prompt/history", async (int id, AppDbContext db) =>
        {
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == id))
                return Results.NotFound();
            var versions = await ProjectPromptService.GetHistoryAsync(db, id);
            return Results.Ok(versions.Select(PromptDto));
        });

        api.MapPut("/projects/{id:int}/prompt", async (
            int id,
            UpdatePromptDirectiveDto dto,
            AppDbContext db,
            PromptDirectivesService filePrompt) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id);
            if (project is null) return Results.NotFound();
            var version = await ProjectPromptService.AddVersionIfChangedAsync(
                db, id, dto.Content, PromptDirectiveAuthor.User, dto.Note ?? "edited in UI");
            await db.SaveChangesAsync();
            var latest = version ?? await ProjectPromptService.GetLatestAsync(db, id);
            if (latest is not null)
                await filePrompt.WriteMirrorAsync(project.RepoPath, latest.Content);
            return Results.Ok(latest is null ? null : PromptDto(latest));
        });

        api.MapPost("/projects/{id:int}/prompt/revert/{version:int}", async (
            int id,
            int version,
            AppDbContext db,
            PromptDirectivesService filePrompt) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id);
            if (project is null) return Results.NotFound();
            var reverted = await ProjectPromptService.RevertAsNewVersionAsync(db, id, version);
            if (reverted is null) return Results.NotFound();
            await db.SaveChangesAsync();
            await filePrompt.WriteMirrorAsync(project.RepoPath, reverted.Content);
            return Results.Ok(PromptDto(reverted));
        });

        api.MapGet("/projects/{id:int}/learning", async (
            int id,
            AppDbContext db,
            IOptions<AutoDevOptions> opt) =>
        {
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == id))
                return Results.NotFound();
            var state = await db.ProjectLearningStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ProjectId == id);
            var tasks = await db.ProjectTaskStats.AsNoTracking()
                .Where(s => s.ProjectId == id)
                .ToListAsync();
            var changes = await db.ProjectSettingChanges.AsNoTracking()
                .Where(c => c.ProjectId == id)
                .OrderByDescending(c => c.CreatedAt)
                .Take(20)
                .ToListAsync();
            return Results.Ok(ProjectLearningSurface.Build(
                state, tasks, changes, opt.Value.Learning.RepeatedFailureThreshold));
        });

        api.MapGet("/projects/{id:int}/settings/changes", async (int id, AppDbContext db, int? take) =>
        {
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == id))
                return Results.NotFound();
            var changes = await db.ProjectSettingChanges.AsNoTracking()
                .Where(c => c.ProjectId == id)
                .OrderByDescending(c => c.CreatedAt)
                .Take(take ?? 50)
                .Select(c => new ProjectSettingChangeView(c.Key, c.OldValue, c.NewValue, c.Source, c.CreatedAt))
                .ToListAsync();
            return Results.Ok(changes);
        });

        // Full brief version history (newest first).
        api.MapGet("/projects/{id:int}/briefs", async (int id, AppDbContext db) =>
        {
            var versions = await db.ProjectBriefs.AsNoTracking()
                .Where(b => b.ProjectId == id)
                .OrderByDescending(b => b.Version)
                .Select(b => new BriefVersionDto(b.Version, b.Author.ToString(), b.Note, b.CreatedAt, b.Content))
                .ToListAsync();
            return Results.Ok(versions);
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
                ProjectType = string.IsNullOrWhiteSpace(dto.ProjectType) ? null : dto.ProjectType!.Trim(),
                Priority = dto.Priority ?? 0,
                ProviderPriority = string.IsNullOrWhiteSpace(dto.ProviderPriority) ? "Codex,Claude" : dto.ProviderPriority!,
                ValidationCommand = dto.ValidationCommand,
                MaxRunMinutes = dto.MaxRunMinutes ?? 30,
                AutoCommit = dto.AutoCommit ?? true,
                AutoPush = dto.AutoPush ?? false,
                AllowRunOnMainBranch = dto.AllowRunOnMainBranch ?? false,
                AllowAiEditBrief = dto.AllowAiEditBrief ?? false,
                AllowAiEditSettings = dto.AllowAiEditSettings ?? false,
                Notes = dto.Notes
            };
            db.Projects.Add(p);
            await db.SaveChangesAsync();

            // Store the initial brief as version 1 (if provided in the form).
            await ProjectBriefService.AddVersionIfChangedAsync(
                db, p.Id, dto.Brief, BriefAuthor.User, "initial brief");
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
            if (dto.ProjectType is not null) p.ProjectType = string.IsNullOrWhiteSpace(dto.ProjectType) ? null : dto.ProjectType.Trim();
            if (dto.Enabled is not null) p.Enabled = dto.Enabled.Value;
            if (dto.Paused is not null) p.Paused = dto.Paused.Value;
            if (dto.Priority is not null) p.Priority = dto.Priority.Value;
            if (dto.ProviderPriority is not null) p.ProviderPriority = dto.ProviderPriority;
            if (dto.ValidationCommand is not null) p.ValidationCommand = dto.ValidationCommand;
            if (dto.MaxRunMinutes is not null) p.MaxRunMinutes = dto.MaxRunMinutes.Value;
            if (dto.AutoCommit is not null) p.AutoCommit = dto.AutoCommit.Value;
            if (dto.AutoPush is not null) p.AutoPush = dto.AutoPush.Value;
            if (dto.AllowRunOnMainBranch is not null) p.AllowRunOnMainBranch = dto.AllowRunOnMainBranch.Value;
            if (dto.AllowAiEditBrief is not null) p.AllowAiEditBrief = dto.AllowAiEditBrief.Value;
            if (dto.AllowAiEditSettings is not null) p.AllowAiEditSettings = dto.AllowAiEditSettings.Value;
            if (dto.Notes is not null) p.Notes = dto.Notes;

            // Editing the brief appends a new version (history is never overwritten).
            await ProjectBriefService.AddVersionIfChangedAsync(
                db, p.Id, dto.Brief, BriefAuthor.User, "edited in UI");

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

        api.MapGet("/runs/{id:int}", async (int id, AppDbContext db, RunMetadataStore runMeta) =>
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
                logPath = r.LogPath,
                // Reproducibility / evolution history (persisted per run).
                taskTitle = r.TaskTitle,
                taskSource = r.TaskSource,
                prompt = r.Prompt,
                creativePlan = r.CreativePlan,
                stage = r.Stage,
                risk = r.Risk,
                // Task lifecycle + selected skills + artifacts from the run sidecar.
                meta = runMeta.ReadForLog(r.LogPath)
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

        api.MapGet("/runs/{id:int}/events", async (
            int id, HttpContext context, AppDbContext db, RunEventStore events) =>
        {
            var run = await db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, context.RequestAborted);
            if (run is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var repoPath = await db.Projects.AsNoTracking()
                .Where(p => p.Id == run.ProjectId)
                .Select(p => p.RepoPath)
                .FirstOrDefaultAsync(context.RequestAborted);
            if (string.IsNullOrWhiteSpace(repoPath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Append("X-Accel-Buffering", "no");
            var after = long.TryParse(context.Request.Headers["Last-Event-ID"], out var requested)
                ? requested : 0;

            while (!context.RequestAborted.IsCancellationRequested)
            {
                var batch = events.ReadAfter(repoPath, id, after);
                foreach (var item in batch)
                {
                    after = item.Sequence;
                    await context.Response.WriteAsync($"id: {item.Sequence}\n", context.RequestAborted);
                    await context.Response.WriteAsync($"event: {item.Kind}\n", context.RequestAborted);
                    await context.Response.WriteAsync(
                        "data: " + JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\n\n",
                        context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    if (item.Terminal) return;
                }

                if (batch.Count == 0 && run.Status is not (RunStatus.Pending or RunStatus.Running)) return;
                await Task.Delay(500, context.RequestAborted);
            }
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

        // ---- Project goal layer + lifecycle + artifacts (file-based) ----
        api.MapGet("/projects/{id:int}/goal", async (int id, AppDbContext db, ProjectGoalService goals) =>
        {
            var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            var goal = await goals.LoadAsync(p.RepoPath);
            return Results.Ok(new ProjectGoalDto(
                HasGoal: goal.HasGoal,
                Files: goals.Presence(p.RepoPath),
                Goal: goal.Goal,
                Roadmap: goal.Roadmap,
                Backlog: goal.Backlog,
                Ideas: goal.Ideas,
                Decisions: goal.Decisions));
        });

        // Recent runs' lifecycle/skill/artifact/risk (from sidecars), newest first.
        api.MapGet("/projects/{id:int}/lifecycle", async (int id, AppDbContext db, RunMetadataStore runMeta, int? take) =>
        {
            var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            var metas = runMeta.ReadAllForRepo(p.RepoPath).Take(take ?? 20);
            return Results.Ok(metas);
        });

        // All artifacts a project has generated, most-recent first, de-duplicated by path.
        api.MapGet("/projects/{id:int}/artifacts", async (int id, AppDbContext db, RunMetadataStore runMeta) =>
        {
            var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<object>();
            foreach (var m in runMeta.ReadAllForRepo(p.RepoPath))
                foreach (var a in m.Artifacts)
                    if (seen.Add(a.Path))
                        list.Add(new { a.Path, Kind = a.Kind.ToString(), a.SkillId, a.SizeBytes, runId = m.RunId, m.StartedAt });
            return Results.Ok(list);
        });

        // Serve a generated artifact file for preview (scoped strictly inside the repo).
        api.MapGet("/projects/{id:int}/artifact", async (int id, string path, AppDbContext db) =>
        {
            var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            var full = SafeRepoPath(p.RepoPath, path);
            if (full is null) return Results.BadRequest("Invalid path.");
            if (!File.Exists(full)) return Results.NotFound();
            return Results.File(await File.ReadAllBytesAsync(full), ContentTypeFor(full));
        });

        // ---- Skills (global skill store) ----
        api.MapGet("/skills", (SkillRegistry skills) =>
            Results.Ok(skills.All.Select(SkillToDto)));

        api.MapGet("/skills/log", (SkillRegistry skills) =>
            Results.Ok(skills.RecentSelections.Select(s =>
                new SkillSelectionDto(s.SkillId, s.SkillName, s.MatchedKeywords, s.ProjectName, s.Task, s.SelectedAt))));

        api.MapGet("/skills/{id}", (string id, SkillRegistry skills) =>
        {
            var s = skills.Get(id);
            return s is null ? Results.NotFound() : Results.Ok(SkillToDto(s));
        });

        api.MapPost("/skills/{id}/enable", (string id, SkillRegistry skills) =>
            skills.SetEnabled(id, true) ? Results.Ok(new { id, enabled = true }) : Results.NotFound());

        api.MapPost("/skills/{id}/disable", (string id, SkillRegistry skills) =>
            skills.SetEnabled(id, false) ? Results.Ok(new { id, enabled = false }) : Results.NotFound());

        api.MapPost("/skills/reload", (SkillRegistry skills) =>
        {
            skills.Reload();
            return Results.Ok(new { reloaded = true, count = skills.All.Count, root = skills.Root });
        });

        // Preview which skill(s) a piece of task text would trigger (debug/tuning).
        api.MapGet("/skills/match", (string? text, SkillRegistry skills) =>
            Results.Ok(skills.Match(text).Select(m => new
            {
                skillId = m.Skill.Id,
                skillName = m.Skill.Name,
                score = m.Score,
                matchedKeywords = m.MatchedKeywords
            })));

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
                burnTokens = new { Enabled = o.BurnTokensEnabled, ExplicitEnabled = o.BurnTokens.Enabled },
                continuous = o.Continuous,
                email = new { o.Email.Enabled, To = o.Email.ToEmail, Provider = "EmailJS", o.Email.ServiceId },
                providers = new
                {
                    codex = new { o.Providers.Codex.Enabled, o.Providers.Codex.Command },
                    claude = new { o.Providers.Claude.Enabled, o.Providers.Claude.Command }
                }
            });
        });
    }

    /// <summary>Resolve a repo-relative path and refuse anything escaping the repo root.</summary>
    private static string? SafeRepoPath(string repoPath, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var root = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootWithSep = root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".json" => "application/json",
        ".md" or ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };

    private static SkillDto SkillToDto(SkillManifest s) => new(
        s.Id, s.Name, s.Version, s.Description, s.EffectiveEnabled,
        s.Triggers, s.InvocationHint, s.SourcePath);

    private static PromptDirectiveVersionDto PromptDto(PromptDirective p) => new(
        p.Version, p.Author.ToString(), p.Note, p.CreatedAt, p.Content);

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
            p.Id, p.Name, p.RepoPath, p.BriefPath, p.ProjectType, p.Enabled, p.Paused, p.Priority,
            p.ProviderPriority, p.ValidationCommand, p.MaxRunMinutes,
            p.AutoCommit, p.AutoPush, p.AllowRunOnMainBranch, p.AllowAiEditBrief, p.AllowAiEditSettings, p.AiBranchPrefix,
            p.Notes, p.CurrentTask, p.LastSummary, p.CurrentBranch, p.ProviderSessionId,
            LastRunStatus = p.LastRunStatus?.ToString(),
            LastProvider = p.LastProvider?.ToString(),
            p.LastRunAt, p.LastError, isRunning
        };
    }
}
