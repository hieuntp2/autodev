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

// Run as a Windows Service when launched by the SCM (no-op otherwise).
builder.Host.UseWindowsService(o => o.ServiceName = "AutoDevRunner");

// ---- Options ----
builder.Services.Configure<AutoDevOptions>(builder.Configuration.GetSection(AutoDevOptions.SectionName));
var autoDevOptions = builder.Configuration.GetSection(AutoDevOptions.SectionName).Get<AutoDevOptions>() ?? new();

// ---- Storage (PostgreSQL) ----
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=autodev;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

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

// ---- Providers ----
builder.Services.AddSingleton<IAiProvider, CodexCliProvider>();
builder.Services.AddSingleton<IAiProvider, ClaudeCliProvider>();
builder.Services.AddSingleton<ProviderRegistry>();

// ---- Orchestrator (scoped: one per run, owns a DbContext) ----
builder.Services.AddScoped<RunOrchestrator>();

// ---- Optional in-process scheduler (off by default; Task Scheduler is preferred) ----
if (autoDevOptions.Scheduler.Enabled && !runOnce)
    builder.Services.AddHostedService<SchedulerService>();

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ---- Initialize DB + seed provider states ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    foreach (var kind in Enum.GetValues<ProviderKind>())
        if (!await db.ProviderStates.AnyAsync(p => p.Provider == kind))
            db.ProviderStates.Add(new ProviderState { Provider = kind });
    await db.SaveChangesAsync();
}

// ---- One-shot run mode (Windows Task Scheduler) ----
if (runOnce)
{
    var runner = app.Services.GetRequiredService<DueProjectsRunner>();
    await runner.RunAllDueAsync();
    return;
}

// ---- Web host (API + dashboard) ----
app.UseDefaultFiles();   // serve wwwroot/index.html at "/"
app.UseStaticFiles();

app.MapApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

app.Run();
