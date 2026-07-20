using System.Text;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>
/// Builds the PLAN-step instructions shared by every planner backend: the
/// OpenAI Responses API call and the local CLI providers (Codex/Claude) in
/// plan-only mode. Keeping the text in one place means a project can switch
/// its planner AI without changing what is asked of it.
/// </summary>
public static class PlannerPromptBuilder
{
    public static string BuildInstructions(Project project, string brief, ProjectGoal? goal)
    {
        var instructions = new StringBuilder();
        instructions.AppendLine("You are the Creative Director for an autonomous software project.");
        instructions.AppendLine("Design the boldest, most delightful, highest-impact plan for the NEXT development run.");
        instructions.AppendLine("You have COMPLETE creative freedom and full autonomy: decide boldly, take creative risks,");
        instructions.AppendLine("and go beyond the literal ask when it makes the product better. Nothing needs prior approval.");
        instructions.AppendLine("Search the attached knowledge base freely for product vision, design language, and inspiration,");
        instructions.AppendLine("and let it fuel — not limit — your creativity.");
        instructions.AppendLine();
        instructions.AppendLine("Anchor the plan to the PROJECT GOAL below. If no task is currently in progress, PROPOSE the");
        instructions.AppendLine("single smallest valuable next task that advances the goal, grounded in the goal + run history.");
        instructions.AppendLine();
        instructions.AppendLine("Output concise Markdown a coding agent can execute this run, with sections:");
        instructions.AppendLine("## Creative Direction — the vision/theme for this run");
        instructions.AppendLine("## This Run's Task — one coherent, buildable slice (propose it if none is in progress)");
        instructions.AppendLine("## Concrete Steps — ordered, specific");
        instructions.AppendLine("## Delight & Polish — small touches that make it shine");
        instructions.AppendLine("## Acceptance — how we know it's done");
        instructions.AppendLine();
        if (!string.IsNullOrWhiteSpace(project.ProjectType))
        {
            var platform = project.ProjectType!.Trim();
            instructions.AppendLine("## TARGET PLATFORM — NON-NEGOTIABLE");
            instructions.AppendLine($"- This project targets {platform}. Every task you propose MUST be implementable as");
            instructions.AppendLine($"  native {platform} code in this repo's real toolchain. Do NOT propose an HTML/JS/canvas");
            instructions.AppendLine("  web page, a browser demo, or a reimplementation on any other platform/framework —");
            instructions.AppendLine("  not even as a faster way to show a feature. Creativity is about the product, not the stack.");
            instructions.AppendLine();
        }

        instructions.AppendLine("## Project");
        instructions.AppendLine($"- Name: {project.Name}");
        if (!string.IsNullOrWhiteSpace(project.Notes))
            instructions.AppendLine($"- Notes: {project.Notes}");
        if (!string.IsNullOrWhiteSpace(project.CurrentTask))
            instructions.AppendLine($"- Task in progress from last run: {project.CurrentTask}");
        else
            instructions.AppendLine("- No task is currently in progress — propose the next one toward the goal.");
        if (!string.IsNullOrWhiteSpace(project.LastSummary))
        {
            instructions.AppendLine("- Previous run summary:");
            instructions.AppendLine(project.LastSummary!.Trim());
        }
        instructions.AppendLine();
        if (goal is { HasAny: true })
        {
            instructions.AppendLine("## Project goal & memory");
            if (goal.HasGoal) { instructions.AppendLine("### PROJECT_GOAL"); instructions.AppendLine(goal.Goal!.Trim()); }
            if (!string.IsNullOrWhiteSpace(goal.Roadmap)) { instructions.AppendLine("### ROADMAP"); instructions.AppendLine(goal.Roadmap!.Trim()); }
            if (!string.IsNullOrWhiteSpace(goal.Backlog)) { instructions.AppendLine("### BACKLOG"); instructions.AppendLine(goal.Backlog!.Trim()); }
            if (!string.IsNullOrWhiteSpace(goal.Ideas)) { instructions.AppendLine("### IDEAS"); instructions.AppendLine(goal.Ideas!.Trim()); }
            instructions.AppendLine();
        }
        instructions.AppendLine("## Project brief");
        instructions.AppendLine(string.IsNullOrWhiteSpace(brief)
            ? "(No brief provided. Infer the goal from the knowledge base and the project name.)"
            : brief.Trim());

        return instructions.ToString();
    }

    /// <summary>
    /// The same instructions wrapped for a local CLI agent: no knowledge base is
    /// attached (it may read the repo instead), and it must plan WITHOUT touching
    /// the working tree — the execute step runs later against these plans.
    /// </summary>
    public static string BuildCliPrompt(Project project, string brief, ProjectGoal? goal)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PLANNING-ONLY CALL. You are invoked as the planner, not the implementer.");
        sb.AppendLine("- Do NOT create, modify, or delete any file, and do NOT run commands that change state.");
        sb.AppendLine("- You MAY read the repository to ground the plan in the real codebase.");
        sb.AppendLine("- There is no attached knowledge base in this mode — the repo and the brief below are your sources.");
        sb.AppendLine("- Reply with ONLY the Markdown plan requested below (no preamble, no tool logs).");
        sb.AppendLine();
        sb.Append(BuildInstructions(project, brief, goal));
        return sb.ToString();
    }
}
