namespace AutoDevOrchestrator;

/// <summary>Resolves the repo root and all well-known autodev file locations.</summary>
public sealed class RepoPaths
{
    public required string Root { get; init; }

    public string ToolDir => Path.Combine(Root, "tools", "autodev-orchestrator");
    public string DocsDir => Path.Combine(Root, "docs", "autodev");
    public string PlansDir => Path.Combine(DocsDir, "plans");
    public string ReportsDir => Path.Combine(DocsDir, "reports");
    public string LogsDir => Path.Combine(DocsDir, "logs");

    public string CurrentPlanFile => Path.Combine(DocsDir, "CURRENT_PLAN.json");
    public string DailyLogFile => Path.Combine(DocsDir, "DAILY_LOG.md");
    public string IdeaMemoryFile => Path.Combine(DocsDir, "IDEA_MEMORY.md");
    public string PlannerPromptFile => Path.Combine(DocsDir, "OPENAI_PLANNER_PROMPT.md");
    public string NextImplementerPromptFile => Path.Combine(DocsDir, "NEXT_IMPLEMENTER_PROMPT.md");
    public string LastRunFile => Path.Combine(LogsDir, "last-run.txt");

    public string PlanFileFor(DateOnly date) => Path.Combine(PlansDir, $"{date:yyyy-MM-dd}-plan.json");
    public string ReportFileFor(DateOnly date) => Path.Combine(ReportsDir, $"{date:yyyy-MM-dd}-report.md");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DocsDir);
        Directory.CreateDirectory(PlansDir);
        Directory.CreateDirectory(ReportsDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>Walk up from the current directory (then the exe location) until a repo marker is found.</summary>
    public static RepoPaths Discover()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                    File.Exists(Path.Combine(dir.FullName, "AutoDevRunner.sln")))
                {
                    return new RepoPaths { Root = dir.FullName };
                }
                dir = dir.Parent;
            }
        }

        throw new OrchestratorException(
            "Could not find the repo root (no .git directory or AutoDevRunner.sln found upwards from " +
            $"'{Environment.CurrentDirectory}'). Run the orchestrator from inside the repo.");
    }
}

public sealed class OrchestratorException(string message) : Exception(message);
