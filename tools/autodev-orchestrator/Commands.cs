using System.Diagnostics;
using System.Text;

namespace AutoDevOrchestrator;

public static class Commands
{
    public static int Status(RepoPaths paths, OrchestratorConfig config)
    {
        var plan = PlanStore.Load(paths);

        Console.WriteLine("Daily Pixel Garden — autodev status");
        Console.WriteLine($"  Repo root:       {paths.Root}");
        Console.WriteLine($"  Garden app:      {config.GardenAppRelPath} (exists: {(Directory.Exists(config.GardenAppFullPath(paths)) ? "yes" : "no")})");

        if (plan is null)
        {
            Console.WriteLine("  Current plan:    none (CURRENT_PLAN.json missing)");
        }
        else
        {
            var byStatus = plan.Tasks
                .GroupBy(t => t.Status.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count());
            string Count(string status) => byStatus.TryGetValue(status, out var n) ? n.ToString() : "0";

            Console.WriteLine($"  Current plan:    {plan.PlanDate} — \"{plan.Theme}\"");
            Console.WriteLine($"  Tasks:           {plan.Tasks.Count} total | pending {Count("pending")} | in_progress {Count("in_progress")} | completed {Count("completed")} | skipped {Count("skipped")}");
            var next = plan.PendingTasks.FirstOrDefault();
            if (next is not null) Console.WriteLine($"  Next task:       {next.Id} — {next.Title}");
        }

        Console.WriteLine($"  Last run:        {(File.Exists(paths.LastRunFile) ? File.ReadAllText(paths.LastRunFile).Trim() : "never (no last-run marker)")}");

        var needsOpenAi = plan is null || plan.IsCompleted;
        Console.WriteLine($"  OpenAI needed:   {(needsOpenAi ? "YES — plan is missing or fully completed" : "no — pending tasks remain")}");
        if (needsOpenAi && string.IsNullOrWhiteSpace(config.OpenAiApiKey) && !config.DryRun)
            Console.WriteLine("  Warning:         OPENAI_API_KEY is not set — `plan` will fail until it is (or use AUTODEV_DRY_RUN=true).");
        return 0;
    }

    public static int Report(RepoPaths paths, OrchestratorConfig config)
    {
        var plan = PlanStore.Load(paths);
        var (_, filePath) = ReportGenerator.Generate(paths, config, plan);
        Console.WriteLine($"Report written to {filePath}");
        return 0;
    }

    public static async Task<int> Plan(RepoPaths paths, OrchestratorConfig config)
    {
        var current = PlanStore.Load(paths);
        if (current is not null && !current.IsCompleted)
        {
            Console.WriteLine($"Current plan ({current.PlanDate}) still has {current.PendingTasks.Count()} pending task(s) — not calling OpenAI.");
            Console.WriteLine("Complete or skip the remaining tasks first, or wait for the next cycle.");
            return 0;
        }

        // All done (or no plan yet): report first, then ask the Creative Director for the next plan.
        var (reportMarkdown, reportPath) = ReportGenerator.Generate(paths, config, current);
        Console.WriteLine($"Report written to {reportPath}");

        PlanDocument newPlan;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (config.DryRun)
        {
            Console.WriteLine("AUTODEV_DRY_RUN=true — generating a built-in sample plan without calling OpenAI.");
            newPlan = SamplePlan.Create(today);
        }
        else
        {
            if (!File.Exists(paths.PlannerPromptFile))
                throw new OrchestratorException($"Planner prompt not found: {paths.PlannerPromptFile}");
            var systemPrompt = File.ReadAllText(paths.PlannerPromptFile);
            var userContext = BuildPlannerContext(paths, config, reportMarkdown);
            newPlan = await OpenAiPlanner.RequestPlanAsync(paths, config, systemPrompt, userContext);
        }

        PlanStore.Save(newPlan, paths.CurrentPlanFile);
        PlanStore.Save(newPlan, paths.PlanFileFor(today));
        Console.WriteLine($"New plan saved: \"{newPlan.Theme}\" with {newPlan.Tasks.Count} task(s).");
        Console.WriteLine($"  -> {paths.CurrentPlanFile}");
        Console.WriteLine($"  -> {paths.PlanFileFor(today)}");

        AppendDailyLog(paths, newPlan, today);
        AppendIdeaMemory(paths, newPlan, today);

        var taskId = ImplementerPromptWriter.WriteNextTaskPrompt(paths, config, newPlan);
        if (taskId is not null)
            Console.WriteLine($"Next implementer prompt written for {taskId}: {paths.NextImplementerPromptFile}");
        return 0;
    }

    public static async Task<int> Run(RepoPaths paths, OrchestratorConfig config)
    {
        Status(paths, config);
        Console.WriteLine();

        var plan = PlanStore.Load(paths);
        if (plan is null || plan.IsCompleted)
        {
            await Plan(paths, config);
            plan = PlanStore.Load(paths);
        }

        if (plan is null || plan.PendingTasks.FirstOrDefault() is not { } nextTask)
        {
            Console.WriteLine("No pending tasks and no new plan was produced — nothing to hand off.");
            return 0;
        }

        ImplementerPromptWriter.WriteNextTaskPrompt(paths, config, plan);
        Console.WriteLine($"Next task for the implementer: {nextTask.Id} — {nextTask.Title}");
        Console.WriteLine($"Handoff prompt: {paths.NextImplementerPromptFile}");

        var exitCode = 0;
        if (config.RunImplementer)
        {
            if (string.IsNullOrWhiteSpace(config.ImplementerCommand))
            {
                Console.WriteLine("AUTODEV_RUN_IMPLEMENTER=true but AUTODEV_IMPLEMENTER_COMMAND is empty — skipping implementer.");
            }
            else
            {
                exitCode = RunImplementerCommand(paths, config);
            }
        }
        else
        {
            Console.WriteLine("AUTODEV_RUN_IMPLEMENTER=false — leaving the prompt for Claude CLI / Codex to pick up.");
        }

        File.WriteAllText(paths.LastRunFile, $"{DateTime.Now:yyyy-MM-dd HH:mm} (exit {exitCode})");
        return exitCode;
    }

    private static int RunImplementerCommand(RepoPaths paths, OrchestratorConfig config)
    {
        var command = config.ImplementerCommand.Replace("{PROMPT_FILE}", paths.NextImplementerPromptFile);
        Console.WriteLine($"Running implementer: {command}");
        var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            WorkingDirectory = paths.Root,
            UseShellExecute = false
        };
        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.Error.WriteLine("Failed to start the implementer process.");
            return 1;
        }
        process.WaitForExit();
        Console.WriteLine($"Implementer exited with code {process.ExitCode}.");
        return process.ExitCode;
    }

    private static string BuildPlannerContext(RepoPaths paths, OrchestratorConfig config, string reportMarkdown)
    {
        var sb = new StringBuilder();
        var appExists = Directory.Exists(config.GardenAppFullPath(paths));
        sb.AppendLine($"Today: {DateTime.Now:yyyy-MM-dd (dddd)}");
        sb.AppendLine($"Garden app: `{config.GardenAppRelPath}` (exists: {(appExists ? "yes" : "no — the world has not been scaffolded yet")})");
        sb.AppendLine();
        sb.AppendLine("## Latest report (plan state, story so far, git snapshot)");
        sb.AppendLine();
        sb.AppendLine(Cap(reportMarkdown, 6000));
        sb.AppendLine();
        sb.AppendLine("## Idea memory (tail)");
        sb.AppendLine();
        sb.AppendLine(Cap(ReportGenerator.TailOfFile(paths.IdeaMemoryFile, 60) ?? "_empty_", 3000));
        sb.AppendLine();
        sb.AppendLine("Create the next small daily plan now. Return strict JSON only, matching the schema in your instructions.");
        return sb.ToString();
    }

    private static void AppendDailyLog(RepoPaths paths, PlanDocument plan, DateOnly today)
    {
        var sb = new StringBuilder();
        if (!File.Exists(paths.DailyLogFile))
            sb.AppendLine("# Daily Pixel Garden — Daily Log").AppendLine();
        sb.AppendLine($"## {today:yyyy-MM-dd} — {plan.Theme}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(plan.StoryLog)) sb.AppendLine(plan.StoryLog).AppendLine();
        sb.AppendLine($"Planned {plan.Tasks.Count} task(s): {string.Join("; ", plan.Tasks.Select(t => t.Title))}.");
        sb.AppendLine();
        File.AppendAllText(paths.DailyLogFile, sb.ToString());
    }

    private static void AppendIdeaMemory(RepoPaths paths, PlanDocument plan, DateOnly today)
    {
        if (plan.FutureIdeas.Count == 0) return;
        var existing = File.Exists(paths.IdeaMemoryFile) ? File.ReadAllText(paths.IdeaMemoryFile) : "";
        var sb = new StringBuilder();
        if (existing.Length == 0)
            sb.AppendLine("# Daily Pixel Garden — Idea Memory").AppendLine();
        foreach (var idea in plan.FutureIdeas)
        {
            if (existing.Contains(idea, StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine($"- ({today:yyyy-MM-dd}) {idea}");
        }
        if (sb.Length > 0) File.AppendAllText(paths.IdeaMemoryFile, sb.ToString());
    }

    private static string Cap(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + Environment.NewLine + "_(truncated)_";

    public static int Help()
    {
        Console.WriteLine("""
            autodev-orchestrator — creative loop for Daily Pixel Garden

            Usage: autodev-orchestrator <command>

              status   Show plan state, task counts, last run, and whether an OpenAI call is needed.
              report   Write docs/autodev/reports/YYYY-MM-DD-report.md from the current state.
              plan     If the plan is missing/completed: write a report, ask OpenAI for the next plan.
                       If pending tasks remain, OpenAI is NOT called.
              run      status + (report/plan if needed) + write NEXT_IMPLEMENTER_PROMPT.md
                       and optionally launch the implementer (AUTODEV_RUN_IMPLEMENTER=true).

            Config comes from the repo-root .env file (see .env.example). Set AUTODEV_DRY_RUN=true
            to exercise the whole loop without an OpenAI key.
            """);
        return 0;
    }
}
