using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Providers;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// PLAN-step backend for projects whose PlannerProvider is a local CLI
/// (Codex/Claude): invokes the CLI once in plan-only mode against the project
/// repo and returns the Markdown plan. Fail-soft like the OpenAI planner — any
/// failure returns null and the run proceeds with the base prompt.
/// </summary>
public class CliCreativePlanner
{
    private readonly ProviderRegistry _providers;
    private readonly PlannerOptions _opt;
    private readonly ILogger<CliCreativePlanner> _log;

    public CliCreativePlanner(ProviderRegistry providers, IOptions<AutoDevOptions> opt,
        ILogger<CliCreativePlanner> log)
    {
        _providers = providers;
        _opt = opt.Value.Planner;
        _log = log;
    }

    public async Task<string?> CreatePlanAsync(ProviderKind kind, Project project, string brief,
        ProjectGoal? goal = null, Action<string>? onOutput = null, CancellationToken ct = default)
    {
        var provider = _providers.Get(kind);
        if (provider is null || !provider.IsEnabled)
        {
            _log.LogWarning("Plan step wants {Kind} but that provider is not enabled; skipping planning.", kind);
            return null;
        }

        var prompt = PlannerPromptBuilder.BuildCliPrompt(project, brief, goal);
        var timeout = TimeSpan.FromMinutes(Math.Max(1, _opt.CliTimeoutMinutes));

        try
        {
            var invocation = await provider.RunAsync(prompt, project.RepoPath, timeout, onOutput, ct,
                planMode: true);
            if (invocation.Outcome is not ProviderOutcome.Success
                || string.IsNullOrWhiteSpace(invocation.Output))
            {
                _log.LogWarning("CLI planner {Kind} returned {Outcome} ({Reason}); continuing with the base prompt.",
                    kind, invocation.Outcome, invocation.Reason ?? "no reason");
                return null;
            }

            _log.LogInformation("CLI planner {Kind} produced a plan for {Project} ({Chars} chars).",
                kind, project.Name, invocation.Output.Length);
            return invocation.Output.Trim();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CLI planner {Kind} failed; continuing with the base prompt.", kind);
            return null;
        }
    }
}
