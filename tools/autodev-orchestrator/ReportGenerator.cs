using System.Text;

namespace AutoDevOrchestrator;

public static class ReportGenerator
{
    /// <summary>Builds the daily report markdown and writes it to docs/autodev/reports/YYYY-MM-DD-report.md.</summary>
    public static (string Markdown, string FilePath) Generate(RepoPaths paths, OrchestratorConfig config, PlanDocument? plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var sb = new StringBuilder();

        sb.AppendLine($"# Daily Pixel Garden — Report {today:yyyy-MM-dd}");
        sb.AppendLine();

        var appPath = config.GardenAppFullPath(paths);
        sb.AppendLine($"- Garden app path: `{config.GardenAppRelPath}` (exists: {(Directory.Exists(appPath) ? "yes" : "no")})");
        sb.AppendLine($"- Report generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        sb.AppendLine("## Current plan");
        sb.AppendLine();
        if (plan is null)
        {
            sb.AppendLine("No current plan (`CURRENT_PLAN.json` missing). A new plan is needed.");
        }
        else
        {
            sb.AppendLine($"- Plan date: {plan.PlanDate}");
            sb.AppendLine($"- Theme: {plan.Theme}");
            sb.AppendLine($"- Creative direction: {plan.CreativeDirection}");
            sb.AppendLine();
            sb.AppendLine("| Task | Title | Status | Completed at |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var task in plan.Tasks)
                sb.AppendLine($"| {task.Id} | {task.Title} | {task.Status} | {task.CompletedAt ?? "-"} |");
            sb.AppendLine();
            var pending = plan.PendingTasks.Count();
            sb.AppendLine(pending == 0
                ? "All tasks are completed or skipped. Ready for the next plan."
                : $"{pending} task(s) still pending.");
        }
        sb.AppendLine();

        sb.AppendLine("## Recent daily log");
        sb.AppendLine();
        var logTail = TailOfFile(paths.DailyLogFile, 40);
        sb.AppendLine(logTail ?? "_No daily log yet._");
        sb.AppendLine();

        sb.AppendLine("## Git snapshot");
        sb.AppendLine();
        var gitStatus = GitHelper.Run(paths, "status --short");
        var gitLog = GitHelper.Run(paths, "log --oneline -5");
        if (gitStatus is null && gitLog is null)
        {
            sb.AppendLine("_git not available._");
        }
        else
        {
            sb.AppendLine("Working tree:");
            sb.AppendLine("```");
            sb.AppendLine(string.IsNullOrWhiteSpace(gitStatus) ? "(clean)" : gitStatus);
            sb.AppendLine("```");
            sb.AppendLine("Recent commits:");
            sb.AppendLine("```");
            sb.AppendLine(gitLog ?? "(none)");
            sb.AppendLine("```");
        }

        var markdown = sb.ToString();
        var filePath = paths.ReportFileFor(today);
        File.WriteAllText(filePath, markdown);
        return (markdown, filePath);
    }

    public static string? TailOfFile(string path, int lines)
    {
        if (!File.Exists(path)) return null;
        var all = File.ReadAllLines(path);
        var tail = all.Skip(Math.Max(0, all.Length - lines));
        var text = string.Join(Environment.NewLine, tail).Trim();
        return text.Length == 0 ? null : text;
    }
}
