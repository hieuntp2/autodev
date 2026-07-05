using System.Text;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>
/// Builds the autonomous-dev prompt sent to the AI CLI, including the project
/// brief, resume state from the previous run, guardrails, and the required
/// summary format the runner parses back out.
/// </summary>
public class PromptBuilder
{
    public const string SummaryMarker = "=== AUTODEV SUMMARY ===";

    public string Build(Project project, string brief, RunRecord run, string? creativePlan = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an autonomous software engineer working on this repository.");
        sb.AppendLine("You have COMPLETE creative freedom and FULL authority to read the codebase, plan, choose the");
        sb.AppendLine("most valuable task, implement it, write/refactor tests, fix build/test failures, and write docs.");
        sb.AppendLine("You do NOT need to ask for approval — ever. Decide boldly and commit. Do not present options for a");
        sb.AppendLine("human to pick; make the call yourself. Take creative risks and go beyond the literal ask when it");
        sb.AppendLine("makes the product better — delightful polish, expressive details, and small surprises are encouraged.");
        sb.AppendLine("The only accountability is the automated daily report sent after this run, so make your changes");
        sb.AppendLine("real and committed-quality, and make sure the summary below tells a clear story of what you built.");
        sb.AppendLine();

        sb.AppendLine("## Project");
        sb.AppendLine($"- Name: {project.Name}");
        sb.AppendLine($"- Repo: {project.RepoPath}");
        if (!string.IsNullOrWhiteSpace(project.Notes))
            sb.AppendLine($"- User notes: {project.Notes}");
        sb.AppendLine();

        sb.AppendLine("## Project brief");
        sb.AppendLine(string.IsNullOrWhiteSpace(brief)
            ? "(No brief file found. Infer the goal from the codebase.)"
            : brief.Trim());
        sb.AppendLine();

        // Creative plan from the OpenAI knowledge-base planner (when configured).
        if (!string.IsNullOrWhiteSpace(creativePlan))
        {
            sb.AppendLine("## Creative plan for this run (from the knowledge base)");
            sb.AppendLine("A creative director drafted this plan for today, grounded in the product knowledge base.");
            sb.AppendLine("Treat it as your strong default direction; deviate only if the codebase makes a better path obvious.");
            sb.AppendLine();
            sb.AppendLine(creativePlan.Trim());
            sb.AppendLine();
        }

        // Resume context.
        if (!string.IsNullOrWhiteSpace(project.LastSummary) || !string.IsNullOrWhiteSpace(project.CurrentTask))
        {
            sb.AppendLine("## Resume context (from the previous run)");
            if (!string.IsNullOrWhiteSpace(project.CurrentTask))
                sb.AppendLine($"- Task in progress: {project.CurrentTask}");
            if (!string.IsNullOrWhiteSpace(project.LastSummary))
            {
                sb.AppendLine("- Previous run summary:");
                sb.AppendLine(Indent(project.LastSummary!.Trim()));
            }
            sb.AppendLine("Continue this work where it left off if it still makes sense; otherwise pick the next most valuable task.");
            sb.AppendLine();
        }

        sb.AppendLine("## What to do this run");
        sb.AppendLine("1. Read the codebase and the brief.");
        sb.AppendLine("2. If there is no backlog, create one and pick the single most valuable task.");
        sb.AppendLine("3. Implement the task. Split it if it's too large; do a coherent slice this run.");
        sb.AppendLine("4. Add or update tests where reasonable.");
        sb.AppendLine("5. Keep the project buildable.");
        sb.AppendLine();

        sb.AppendLine(GuardrailService.PromptGuardrails(project.AllowRunOnMainBranch, project.AutoPush));
        sb.AppendLine();

        sb.AppendLine("## Required output");
        sb.AppendLine($"At the very end of your response, print a summary block starting with the exact line `{SummaryMarker}` and using this format:");
        sb.AppendLine($"{SummaryMarker}");
        sb.AppendLine("TASK: <the task you worked on>");
        sb.AppendLine("DONE: <what you completed>");
        sb.AppendLine("PENDING: <what remains for next time>");
        sb.AppendLine("IDEAS: <new feature ideas you thought of>");
        sb.AppendLine("FILES: <key files you changed>");
        sb.AppendLine("NEXT_TASK: <the single task to resume next run>");

        return sb.ToString();
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(l => "    " + l));
}
