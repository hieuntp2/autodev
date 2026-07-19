using AutoDevRunner.Api;
using AutoDevRunner.Config;
using AutoDevRunner.Data;
using AutoDevRunner.Models;
using AutoDevRunner.Providers;
using AutoDevRunner.Services;
using Microsoft.EntityFrameworkCore;

// One-shot mode: when invoked with `--run-due` (what Windows Task Scheduler runs),
// run all due projects once and exit instead of starting the web host.
var runOnce = args.Any(a => a is "--run-due" or "run-due" or "run");

var builder = WebApplication.CreateBuilder(args);

// Local, untracked secret overrides (API keys). Loaded last so it wins over
// appsettings.json. Kept out of git via .gitignore — see appsettings.Local.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Run as a Windows Service when launched by the SCM (no-op otherwise).
builder.Host.UseWindowsService(o => o.ServiceName = "AutoDevRunner");

// File log (logs/autodev-<date>.log next to the exe). The app is headless
// (WinExe, no console window), so this file is how humans and AI follow it:
//   Get-Content .\publish\logs\autodev-*.log -Tail 50 -Wait
if (builder.Configuration.GetValue("Logging:File:Enabled", true))
{
    var logDir = builder.Configuration["Logging:File:Directory"] ?? "logs";
    if (!Path.IsPathRooted(logDir))
        logDir = Path.Combine(AppContext.BaseDirectory, logDir);
    var minLevel = builder.Configuration.GetValue("Logging:File:MinLevel", LogLevel.Information);
    builder.Logging.AddProvider(new FileLoggerProvider(logDir, minLevel));
}

// ---- Options ----
builder.Services.Configure<AutoDevOptions>(builder.Configuration.GetSection(AutoDevOptions.SectionName));
var autoDevOptions = builder.Configuration.GetSection(AutoDevOptions.SectionName).Get<AutoDevOptions>() ?? new();

// ---- Storage (PostgreSQL) ----
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5433;Database=autodev;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddScoped<DatabaseInitializer>();

// ---- Core services ----
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<GitService>();
builder.Services.AddSingleton<GuardrailService>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<OpenAiCreativePlanner>();
builder.Services.AddSingleton<SummaryParser>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<RunLock>();
builder.Services.AddSingleton<RunLauncher>();
builder.Services.AddSingleton<DueProjectsRunner>();
builder.Services.AddSingleton<ProviderAvailability>();
builder.Services.AddSingleton<CodexUsageReader>();
builder.Services.AddSingleton<ContinuousRunner>();
builder.Services.AddSingleton<RunEventStore>();

// ---- Global skill system ----
builder.Services.AddSingleton<AutoDevRunner.Skills.SkillRegistry>();
builder.Services.AddSingleton<AutoDevRunner.Skills.SkillExporter>();

// ---- Goal layer, lifecycle, risk, artifacts, memory (all file-based) ----
builder.Services.AddSingleton<ProjectGoalService>();
builder.Services.AddSingleton<RiskAssessor>();
builder.Services.AddSingleton<ArtifactTracker>();
builder.Services.AddSingleton<ProjectMemoryWriter>();
builder.Services.AddSingleton<PromptDirectivesService>();
builder.Services.AddSingleton<RunMetadataStore>();
builder.Services.AddSingleton<TaskProposer>();
builder.Services.AddSingleton<RunHistoryService>();
builder.Services.AddSingleton<RetrospectiveWriter>();

// ---- Providers ----
builder.Services.AddSingleton<IAiProvider, CodexCliProvider>();
builder.Services.AddSingleton<IAiProvider, ClaudeCliProvider>();
builder.Services.AddSingleton<ProviderRegistry>();

// ---- Orchestrator (scoped: one per run, owns a DbContext) ----
builder.Services.AddScoped<RunOrchestrator>();

// ---- Optional in-process scheduler (off by default; Task Scheduler is preferred) ----
if (autoDevOptions.Scheduler.Enabled && !runOnce)
    builder.Services.AddHostedService<SchedulerService>();

// ---- Run watchdog (web host only): pauses runs orphaned by a dead runner process ----
if (autoDevOptions.Watchdog.Enabled && !runOnce)
    builder.Services.AddHostedService<RunWatchdogService>();

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ---- Initialize DB + seed provider states ----
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var reconciledAt = DateTime.UtcNow;
    var orphaned = await db.Runs
        .Where(r => (r.Status == RunStatus.Running || r.Status == RunStatus.Pending) && r.FinishedAt == null)
        .ToListAsync();

    // Dashboard and Task Scheduler runs live in separate processes over one DB,
    // so THIS process starting proves nothing about other owners: probe each
    // project's run.lock and only reclaim runs with no live owner (plus the
    // watchdog grace so a run mid-startup is not misread as orphaned).
    var projects = await db.Projects.ToListAsync();
    var runLock = scope.ServiceProvider.GetRequiredService<RunLock>();
    var livenessById = projects.ToDictionary(p => p.Id, p => runLock.CheckLiveness(p));
    Func<int, RunLockLiveness> livenessFor = projectId =>
        livenessById.TryGetValue(projectId, out var liveness) ? liveness : RunLockLiveness.NotHeld;
    var reclaimGrace = TimeSpan.FromMinutes(Math.Max(1, autoDevOptions.Watchdog.GraceMinutes));

    var cleanedRuns = RunStartupCleanup.MarkOrphanedRuns(orphaned, reconciledAt, livenessFor, reclaimGrace);
    var cleanedProjects = RunStartupCleanup.ReconcileOrphanedProjects(projects, reconciledAt, livenessFor, reclaimGrace);
    var sanitizedTasks = RunStartupCleanup.SanitizeResumeTasks(projects);
    var latestRunIds = await db.Runs
        .GroupBy(run => run.ProjectId)
        .Select(group => group.Max(run => run.Id))
        .ToListAsync();
    var latestRuns = await db.Runs.Where(run => latestRunIds.Contains(run.Id)).ToListAsync();
    var reconciledSnapshots = RunStartupCleanup.ReconcileLatestRunSnapshots(projects, latestRuns);
    var recoveredLocks = projects.Count(project => runLock.TryRecoverDeadOwner(project, out _));

    if (cleanedRuns > 0 || cleanedProjects > 0 || reconciledSnapshots > 0
        || sanitizedTasks > 0 || recoveredLocks > 0)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogWarning(
            "Restart recovery: {Runs} run(s) paused, {Projects} orphaned project(s) reconciled, " +
            "{Snapshots} latest snapshot(s) refreshed, " +
            "{Tasks} oversized task(s) sanitized, {Locks} dead lock(s) removed.",
            cleanedRuns, cleanedProjects, reconciledSnapshots, sanitizedTasks, recoveredLocks);
    }

    foreach (var kind in Enum.GetValues<ProviderKind>())
        if (!await db.ProviderStates.AnyAsync(p => p.Provider == kind))
            db.ProviderStates.Add(new ProviderState { Provider = kind });
    await db.SaveChangesAsync();
}

// ---- One-shot run mode (Windows Task Scheduler) ----
if (runOnce)
{
    if (autoDevOptions.BurnTokensEnabled)
    {
        // Continuous: loop run → commit → usage check → run again, until every
        // provider hits the usage ceiling (or the safety cap).
        await app.Services.GetRequiredService<ContinuousRunner>().RunAsync();
    }
    else
    {
        await app.Services.GetRequiredService<DueProjectsRunner>().RunAllDueAsync();
    }
    return;
}

// ---- Web host (API + dashboard) ----
app.UseDefaultFiles();   // serve wwwroot/index.html at "/"
app.UseStaticFiles();

app.MapApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
