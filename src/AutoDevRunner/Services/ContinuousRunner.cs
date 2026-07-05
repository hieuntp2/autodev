using AutoDevRunner.Config;
using AutoDevRunner.Providers;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// Continuous mode: viability check → run (auto-commit per project policy) →
/// re-check → run again, until no provider is viable or the safety cap hits.
/// The goal is to burn each trigger up to the usage ceiling (plan → code, many
/// times) rather than stopping after a single run.
///
/// Two entry points share the same loop:
///  - <see cref="RunAsync"/> loops over ALL due projects (CLI --run-due, scheduler).
///  - <see cref="RunProjectAsync"/> loops a SINGLE project (manual dashboard/API trigger).
///
/// Viability per provider:
///  - Codex: local session files expose exact quota windows (5h/weekly
///    used_percent + reset). Benched once usage ≥ MaxUsagePercent, until the
///    busiest window resets.
///  - Claude: no headless usage API — assumed viable until it reports a
///    quota error, then benched for QuotaCooldownMinutes (RunOrchestrator
///    does the benching for both providers on quota outcomes).
/// </summary>
public class ContinuousRunner
{
    private readonly DueProjectsRunner _due;
    private readonly IServiceScopeFactory _scopes;
    private readonly ProviderRegistry _providers;
    private readonly ProviderAvailability _availability;
    private readonly CodexUsageReader _codexUsage;
    private readonly ContinuousOptions _opt;
    private readonly ILogger<ContinuousRunner> _log;

    public ContinuousRunner(
        DueProjectsRunner due, IServiceScopeFactory scopes, ProviderRegistry providers,
        ProviderAvailability availability, CodexUsageReader codexUsage,
        IOptions<AutoDevOptions> opt, ILogger<ContinuousRunner> log)
    {
        _due = due; _scopes = scopes; _providers = providers; _availability = availability;
        _codexUsage = codexUsage; _opt = opt.Value.Continuous; _log = log;
    }

    /// <summary>Loop over every due project until no provider is viable / the cap hits.</summary>
    public Task RunAsync(CancellationToken ct = default)
        => LoopAsync("all due projects", ct2 => _due.RunAllDueAsync(ct2), ct);

    /// <summary>Loop a single project until no provider is viable / the cap hits.</summary>
    public Task RunProjectAsync(int projectId, CancellationToken ct = default)
        => LoopAsync($"project {projectId}", async ct2 =>
        {
            using var scope = _scopes.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<RunOrchestrator>();
            await orchestrator.RunProjectAsync(projectId, ct2);
        }, ct);

    private async Task LoopAsync(string label, Func<CancellationToken, Task> body, CancellationToken ct)
    {
        _log.LogInformation(
            "Continuous mode ({Label}): looping runs until providers reach {Max}% usage (cap {Cap} runs, {Delay}s between runs).",
            label, _opt.MaxUsagePercent, _opt.MaxRunsPerSession, _opt.DelayBetweenRunsSeconds);

        for (var i = 1; i <= _opt.MaxRunsPerSession && !ct.IsCancellationRequested; i++)
        {
            if (!AnyViableProvider(out var report))
            {
                _log.LogInformation("Stopping continuous loop before iteration {I}: {Report}", i, report);
                return;
            }

            _log.LogInformation("Continuous iteration {I}/{Max} — {Report}", i, _opt.MaxRunsPerSession, report);
            await body(ct);

            if (!AnyViableProvider(out report))
            {
                _log.LogInformation("Stopping continuous loop after iteration {I}: {Report}", i, report);
                return;
            }

            if (i < _opt.MaxRunsPerSession)
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _opt.DelayBetweenRunsSeconds)), ct);
        }

        _log.LogInformation("Continuous loop reached the {Cap}-run safety cap; exiting.", _opt.MaxRunsPerSession);
    }

    /// <summary>
    /// Refreshes Codex usage, benches it if over the ceiling, and reports the
    /// per-provider verdict. True when at least one enabled provider may run.
    /// </summary>
    private bool AnyViableProvider(out string report)
    {
        var now = DateTimeOffset.Now;
        var codexUsage = _codexUsage.TryRead();

        // Bench Codex when its busiest current window crosses the ceiling.
        if (codexUsage is not null)
        {
            var pct = codexUsage.EffectiveUsedPercent(now);
            if (pct >= _opt.MaxUsagePercent)
            {
                var until = codexUsage.BusiestResetsAt(now) ?? now.AddMinutes(_opt.QuotaCooldownMinutes);
                _availability.Suspend(Models.ProviderKind.Codex, until,
                    $"usage {pct:0.#}% ≥ ceiling {_opt.MaxUsagePercent}%");
            }
        }

        var lines = new List<string>();
        var anyViable = false;

        foreach (var provider in _providers.All.Where(p => p.IsEnabled))
        {
            if (_availability.IsAvailable(provider.Kind, out var why))
            {
                anyViable = true;
                var usageNote = provider.Kind == Models.ProviderKind.Codex
                    ? codexUsage is null ? " (usage unknown)" : $" ({codexUsage.Describe(now)})"
                    : " (usage unknown — benched only on quota errors)";
                lines.Add($"{provider.Kind}: viable{usageNote}");
            }
            else
            {
                lines.Add($"{provider.Kind}: benched — {why}");
            }
        }

        if (lines.Count == 0) lines.Add("no providers enabled");
        report = string.Join("; ", lines);
        return anyViable;
    }
}
