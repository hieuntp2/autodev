using System.Text;
using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Skills;

namespace AutoDevRunner.Services;

/// <summary>
/// Builds the autonomous-dev prompt sent to the AI CLI, including the project
/// brief, resume state from the previous run, guardrails, and the required
/// summary format the runner parses back out.
/// </summary>
public class PromptBuilder
{
    public const string SummaryMarker = "=== AUTODEV SUMMARY ===";

    public string Build(Project project, string brief, RunRecord run,
        string? creativePlan = null, IReadOnlyList<SkillMatch>? skills = null,
        ProjectGoal? goal = null, TaskProposal? proposal = null, RiskLevel? risk = null,
        Config.RiskOptions? riskPolicy = null, RunLessons? lessons = null,
        string? validationCommandOverride = null,
        string? projectPromptDirectives = null,
        PromptOptions? promptOptions = null)
    {
        promptOptions ??= new PromptOptions();
        var sb = new StringBuilder();

        sb.AppendLine("You are an autonomous software engineer working on this repository.");
        sb.AppendLine("You have COMPLETE creative freedom and FULL authority to read the codebase, plan, choose the");
        sb.AppendLine("most valuable task, implement it, write/refactor tests, fix build/test failures, and write docs.");
        sb.AppendLine("You do NOT need to ask for approval — ever. Decide boldly and commit. Do not present options for a");
        sb.AppendLine("human to pick; make the call yourself. Take creative risks and go beyond the literal ask when it");
        sb.AppendLine("makes the product better — delightful polish, expressive details, and small surprises are encouraged.");
        sb.AppendLine("The only accountability is the automated daily report sent after this run, so make your changes");
        sb.AppendLine("real and committed-quality, and make sure the summary below tells a clear story of what you built.");
        sb.AppendLine("Your creative freedom is over WHAT you build and HOW you delight the user — it is NOT freedom to");
        sb.AppendLine("change the target platform, language, or tech stack. Those are fixed by the constraint below.");
        sb.AppendLine();

        // Hard, non-negotiable platform/stack contract (prevents e.g. an HTML
        // prototype being shipped for an Android app). Emitted only when set.
        AppendPlatformContract(sb, project);

        sb.AppendLine("## Project");
        sb.AppendLine($"- Name: {project.Name}");
        sb.AppendLine($"- Repo: {project.RepoPath}");
        if (!string.IsNullOrWhiteSpace(project.Notes))
            sb.AppendLine($"- User notes: {project.Notes}");
        sb.AppendLine();

        sb.AppendLine("## Project brief");
        sb.AppendLine(string.IsNullOrWhiteSpace(brief)
            ? "(No brief found. Infer the goal from the codebase.)"
            : Compact(brief.Trim(), promptOptions.BriefMaxChars));
        sb.AppendLine();

        // Optional: let the agent evolve the brief itself (versioned in the DB).
        if (project.AllowAiEditBrief)
        {
            sb.AppendLine("## Evolving the brief (optional)");
            sb.AppendLine("You may improve this brief when you have learned something that makes it clearer, better");
            sb.AppendLine("aligned with the product, or more useful for future runs — but ONLY if it is a real");
            sb.AppendLine("improvement. Do NOT change the target platform/stack and do NOT drop existing intent.");
            sb.AppendLine("To propose a revision, write the FULL revised brief (complete Markdown, not a diff) to");
            sb.AppendLine("`.ai-runner/brief-proposal.md`. The runner stores it as a new brief version (previous");
            sb.AppendLine("versions are kept) and future runs will use the latest automatically. If no change is");
            sb.AppendLine("warranted, do not create that file.");
            sb.AppendLine();
        }

        // Project Goal Layer (.ai-runner/PROJECT_GOAL.md + optional roadmap/backlog/…).
        AppendGoal(sb, goal, promptOptions, !string.IsNullOrWhiteSpace(project.CurrentTask));
        AppendProjectPromptDirectives(sb, projectPromptDirectives, promptOptions);

        if (project.AllowAiEditBrief)
        {
            sb.AppendLine("## Evolving prompt directives (optional)");
            sb.AppendLine("You may improve your standing project-specific directives when you learn a durable");
            sb.AppendLine("style rule, review checklist, priority, or workflow that should guide future runs.");
            sb.AppendLine("To propose a change, write the FULL revised directives Markdown to");
            sb.AppendLine("`.ai-runner/prompt-proposal.md`. The runner validates it, archives the previous");
            sb.AppendLine("PROMPT.md, and uses the new directives on future runs. If no change is warranted,");
            sb.AppendLine("do not create that file.");
            sb.AppendLine();
        }

        // Creative plan from the OpenAI knowledge-base planner (when configured).
        if (!string.IsNullOrWhiteSpace(creativePlan))
        {
            sb.AppendLine("## Creative plan for this run (from the knowledge base)");
            sb.AppendLine("A creative director drafted this plan for today, grounded in the product knowledge base.");
            sb.AppendLine("Treat it as your strong default direction; deviate only if the codebase makes a better path obvious.");
            sb.AppendLine();
            sb.AppendLine(Compact(creativePlan.Trim(), promptOptions.CreativePlanMaxChars));
            sb.AppendLine();
        }

        // Global AutoDev skills selected for this run (explicit, not implicit).
        AppendSkills(sb, skills);

        // Resume context.
        if (!string.IsNullOrWhiteSpace(project.LastSummary) || !string.IsNullOrWhiteSpace(project.CurrentTask))
        {
            sb.AppendLine("## Resume context (from the previous run)");
            if (!string.IsNullOrWhiteSpace(project.CurrentTask))
                sb.AppendLine($"- Task in progress: {project.CurrentTask}");
            if (!string.IsNullOrWhiteSpace(project.LastSummary))
            {
                sb.AppendLine("- Previous run summary:");
                var resumeCap = lessons is { HasAny: true }
                    ? promptOptions.ResumeSummaryWithLessonsMaxChars
                    : promptOptions.ResumeSummaryMaxChars;
                sb.AppendLine(Indent(Compact(project.LastSummary!.Trim(), resumeCap)));
            }
            sb.AppendLine("Continue this work where it left off if it still makes sense; otherwise pick the next most valuable task.");
            sb.AppendLine();
        }

        // Relevant project memory (recent ideas + past decisions), compacted.
        AppendMemory(sb, goal, promptOptions);

        // Lessons from the most recent runs (outcomes + what to avoid).
        AppendLessons(sb, lessons);

        var taskTitle = !string.IsNullOrWhiteSpace(project.CurrentTask) ? project.CurrentTask!.Trim()
                        : proposal?.Title;
        var validation = validationCommandOverride ?? proposal?.ValidationCommand ?? project.ValidationCommand;

        sb.AppendLine("## This run's task");
        if (!string.IsNullOrWhiteSpace(taskTitle))
            sb.AppendLine($"- Task: {taskTitle}");
        else
            sb.AppendLine("- No task is in progress. Choose the single smallest valuable step that moves the "
                          + "PROJECT GOAL above forward (consult the creative plan / backlog), then do it.");
        if (proposal is not null && string.IsNullOrWhiteSpace(project.CurrentTask))
            sb.AppendLine($"- (Proposed by AutoDev from {proposal.Source}.)");
        if (skills is { Count: > 0 })
            sb.AppendLine($"- Use the selected skill(s): {string.Join(", ", skills.Select(s => "$" + s.Skill.Id))} (details below).");
        else if (!string.IsNullOrWhiteSpace(proposal?.SuggestedSkill))
            sb.AppendLine($"- Suggested skill: ${proposal!.SuggestedSkill}.");
        sb.AppendLine();

        sb.AppendLine("## Why this task matters");
        sb.AppendLine(proposal?.Reason
            ?? "It is the next most valuable step toward the project goal above.");
        sb.AppendLine();

        sb.AppendLine("## Constraints");
        sb.AppendLine("- Keep changes small, coherent, and reversible; do a single clean slice this run.");
        sb.AppendLine("- Leave the project buildable; add/update tests where reasonable.");
        if (risk is not null)
            sb.AppendLine($"- Assessed risk level for this task: {risk.ToString()!.ToLowerInvariant()}. "
                          + "Avoid escalating scope; keep risky changes small, reversible, and clearly tied to the goal.");
        sb.AppendLine("- Follow the hard safety rules below.");
        sb.AppendLine();

        sb.AppendLine("## Expected output");
        sb.AppendLine(proposal?.ExpectedOutput
            ?? "Real, committed-quality changes. If a skill is selected, produce its concrete asset output and validate it.");
        if (!string.IsNullOrWhiteSpace(validation))
            sb.AppendLine($"- Validation command that MUST pass before you finish: `{validation}`");
        else
            sb.AppendLine("- Keep the project buildable (no validation command is configured).");
        sb.AppendLine();

        sb.AppendLine("## What to do this run");
        sb.AppendLine("1. Read the codebase, the brief, and the project goal.");
        sb.AppendLine("2. If there is no backlog, create one and pick the single most valuable task toward the goal.");
        sb.AppendLine("3. Implement the task. Split it if it's too large; do a coherent slice this run.");
        sb.AppendLine("4. Add or update tests where reasonable, and run the validation command if configured.");
        sb.AppendLine("5. Keep the project buildable.");
        sb.AppendLine();

        sb.AppendLine(GuardrailService.PromptGuardrails(project.AllowRunOnMainBranch, project.AutoPush,
            project.RepoPath, riskPolicy?.BlockFileDeletions ?? true, riskPolicy?.BlockOutOfProjectChanges ?? true));
        sb.AppendLine();

        AppendRequiredOutput(sb, skills);
        return ApplyBudget(sb.ToString(), promptOptions);
    }

    public string BuildRepair(Project project, string? taskTitle, string validationCommand,
        string? validationOutput, IReadOnlyList<string> changedFiles,
        Config.RiskOptions? riskPolicy = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are continuing the same AutoDev provider session to repair a failed validation run.");
        sb.AppendLine("Fix the build/test failure only. Do not expand scope, do not start new features, and do not rewrite unrelated code.");
        sb.AppendLine();
        sb.AppendLine("## Project");
        sb.AppendLine($"- Name: {project.Name}");
        sb.AppendLine($"- Repo: {project.RepoPath}");
        if (!string.IsNullOrWhiteSpace(taskTitle))
            sb.AppendLine($"- Task: {taskTitle!.Trim()}");
        sb.AppendLine();
        sb.AppendLine("## Validation command");
        sb.AppendLine($"`{validationCommand}`");
        sb.AppendLine();
        sb.AppendLine("## Validation output tail");
        sb.AppendLine(Tail(validationOutput ?? string.Empty, 3000));
        sb.AppendLine();
        sb.AppendLine("## Changed files so far");
        if (changedFiles.Count == 0)
            sb.AppendLine("(none detected)");
        else
            foreach (var file in changedFiles.Take(80))
                sb.AppendLine($"- {file}");
        sb.AppendLine();
        sb.AppendLine(GuardrailService.PromptGuardrails(project.AllowRunOnMainBranch, project.AutoPush,
            project.RepoPath, riskPolicy?.BlockFileDeletions ?? true, riskPolicy?.BlockOutOfProjectChanges ?? true));
        sb.AppendLine();
        AppendRequiredOutput(sb, skills: null);
        return sb.ToString();
    }

    public string BuildResume(Project project, string? validationCommand, RunLessons? lessons,
        Config.RiskOptions? riskPolicy = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are resuming the existing AutoDev provider session for this repository.");
        sb.AppendLine("Continue the same task using the context already present in this session. This is a delta prompt, so do not expect the full brief, roadmap, backlog, or memory here.");
        sb.AppendLine();
        sb.AppendLine("## Task reminder");
        sb.AppendLine(string.IsNullOrWhiteSpace(project.CurrentTask)
            ? "Continue the most valuable in-progress work from this session."
            : project.CurrentTask.Trim());
        sb.AppendLine();

        if (lessons is { RepeatedlyFailingTasks.Count: > 0 })
        {
            sb.AppendLine("## Do NOT retry these the same way");
            foreach (var task in lessons.RepeatedlyFailingTasks)
                sb.AppendLine($"- {task}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(validationCommand))
        {
            sb.AppendLine("## Validation command");
            sb.AppendLine($"`{validationCommand}` must pass before the run can succeed.");
            sb.AppendLine();
        }

        sb.AppendLine(GuardrailService.PromptGuardrails(project.AllowRunOnMainBranch, project.AutoPush,
            project.RepoPath, riskPolicy?.BlockFileDeletions ?? true, riskPolicy?.BlockOutOfProjectChanges ?? true));
        sb.AppendLine();
        AppendRequiredOutput(sb, skills: null);

        var prompt = sb.ToString();
        return prompt.Length <= 4000 ? prompt : ApplyBudget(prompt, new PromptOptions { MaxChars = 4000 });
    }

    /// <summary>
    /// The required final summary block. Keeps the original v1 fields (TASK/DONE/…
    /// — still parsed and used for the email report and resume) and always adds
    /// the v2 goal-driven fields the runner uses for lifecycle, artifacts, risk
    /// and memory.
    /// </summary>
    private static void AppendRequiredOutput(StringBuilder sb, IReadOnlyList<SkillMatch>? skills)
    {
        sb.AppendLine("## Required output");
        sb.AppendLine($"At the very end of your response, print a summary block starting with the exact line `{SummaryMarker}` and using this format (fill every field; use \"none\" when not applicable):");
        sb.AppendLine($"{SummaryMarker}");
        sb.AppendLine("TASK_TITLE: <short title of the task you worked on>");
        sb.AppendLine("TASK_STATUS: <validated | committed | partial | failed>");
        sb.AppendLine("DONE: <what you completed>");
        sb.AppendLine("PENDING: <what remains for next time>");
        sb.AppendLine("IDEAS: <new ideas you thought of>");
        sb.AppendLine("SKILL_USED: <skill id you used, e.g. pixel-animation-artist, or none>");
        sb.AppendLine("ARTIFACT_PATHS: <comma/newline-separated output files or folders you generated, or none>");
        sb.AppendLine("FILES_CHANGED: <key source files you changed>");
        sb.AppendLine("VALIDATION_RESULT: <PASS/FAIL + notes, or not run>");
        sb.AppendLine("RISK_LEVEL: <safe | normal | risky>");
        sb.AppendLine("NEXT_SUGGESTED_TASKS: <1-3 small next tasks toward the goal>");
        sb.AppendLine("MEMORY_UPDATES: <important decisions/learnings to remember, or none>");
        sb.AppendLine("NEXT_TASK: <the single task to resume next run>");
        if (skills is { Count: > 0 })
            sb.AppendLine("(For a pixel-animation-artist run, ARTIFACT_PATHS should include the frames/, sprite sheet, GIF and .animation.json.)");
    }

    /// <summary>
    /// Emit the target platform / stack as a hard, non-negotiable constraint at
    /// the top of the prompt. Without this the agent — told it has "complete
    /// creative freedom" — will take the cheapest path to a visible result (e.g.
    /// an HTML/canvas prototype) instead of the required native stack.
    /// </summary>
    private static void AppendPlatformContract(StringBuilder sb, Project project)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectType)) return;

        var platform = project.ProjectType!.Trim();
        sb.AppendLine("## TARGET PLATFORM — NON-NEGOTIABLE");
        sb.AppendLine($"- This project targets **{platform}**. Every change MUST be native {platform} code that");
        sb.AppendLine("  builds with this project's real toolchain and belongs in this repository's existing structure.");
        sb.AppendLine("- Do NOT reimplement the product — or any part of it — on a different platform, language, or");
        sb.AppendLine("  framework. Specifically: do NOT create an HTML/CSS/JS or canvas web page, a browser demo, a");
        sb.AppendLine("  Node/React app, or any \"quick prototype\" on another stack, even to show a feature faster.");
        sb.AppendLine("- If a change would not compile/build as part of the real " + platform + " project, do not write it.");
        sb.AppendLine("- If the repository looks empty or unclear, scaffold the correct " + platform + " project first;");
        sb.AppendLine("  never substitute an easier stack.");
        sb.AppendLine();
    }

    /// <summary>
    /// Inject the Project Goal Layer so every run is anchored to the product's
    /// long-term goal, not just the immediate task. Optional supporting files
    /// (roadmap/backlog/ideas/decisions) are included, trimmed, when present.
    /// </summary>
    private static void AppendGoal(StringBuilder sb, ProjectGoal? goal, PromptOptions options, bool taskInProgress)
    {
        if (goal is not { HasAny: true }) return;

        sb.AppendLine("## Project goal (long-term direction)");
        sb.AppendLine(goal.HasGoal
            ? Compact(goal.Goal!, options.ProjectGoalMaxChars)
            : "(No PROJECT_GOAL.md yet. If the goal is clear from the brief, write .ai-runner/PROJECT_GOAL.md this run.)");
        sb.AppendLine();

        var roadmapCap = taskInProgress ? Math.Max(200, options.RoadmapMaxChars / 2) : options.RoadmapMaxChars;
        var backlogCap = taskInProgress ? Math.Max(200, options.BacklogMaxChars / 2) : options.BacklogMaxChars;

        void Section(string title, string? body, int max)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            sb.AppendLine($"### {title}");
            sb.AppendLine(Compact(body!, max));
            sb.AppendLine();
        }
        Section("Roadmap", goal.Roadmap, roadmapCap);
        Section("Backlog", goal.Backlog, backlogCap);
    }

    /// <summary>
    /// Inject what AutoDev learned from the most recent runs: their outcomes, the
    /// tasks that keep failing (so the agent does not repeat a broken approach),
    /// and follow-up tasks prior runs suggested. Keeps the run honest about its
    /// own history without another external call.
    /// </summary>
    private static void AppendLessons(StringBuilder sb, RunLessons? lessons)
    {
        if (lessons is not { HasAny: true }) return;

        sb.AppendLine("## Recent run history (lessons)");
        sb.AppendLine("Outcomes of the most recent runs on this project — learn from them:");
        foreach (var r in lessons.Recent.Take(5))
        {
            var line = $"- run #{r.RunId}: {(string.IsNullOrWhiteSpace(r.Task) ? "(no task)" : r.Task!.Trim())} → {r.Status}";
            if (r.Failed && !string.IsNullOrWhiteSpace(r.Reason)) line += $" ({Truncate(r.Reason!.Trim(), 160)})";
            sb.AppendLine(line);
        }
        sb.AppendLine();

        if (lessons.RepeatedlyFailingTasks.Count > 0)
        {
            sb.AppendLine("### Do NOT retry these the same way (they have failed repeatedly)");
            foreach (var t in lessons.RepeatedlyFailingTasks)
                sb.AppendLine($"- {t} — change approach, split it smaller, or pick something else.");
            sb.AppendLine();
        }

        if (lessons.SuggestedNextTasks.Count > 0)
        {
            sb.AppendLine("### Follow-ups suggested by earlier runs");
            foreach (var t in lessons.SuggestedNextTasks) sb.AppendLine($"- {t}");
            sb.AppendLine();
        }
    }

    private static void AppendProjectPromptDirectives(StringBuilder sb, string? directives, PromptOptions options)
    {
        if (string.IsNullOrWhiteSpace(directives)) return;

        sb.AppendLine("## Project prompt directives (self-evolved)");
        sb.AppendLine(Compact(directives, options.PromptDirectivesMaxChars));
        sb.AppendLine();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static string Tail(string s, int max) =>
        s.Length <= max ? s : "...\n" + s[^max..];

    /// <summary>Relevant project memory: recent ideas + past decisions, compacted.</summary>
    private static void AppendMemory(StringBuilder sb, ProjectGoal? goal, PromptOptions options)
    {
        if (goal is null) return;
        if (string.IsNullOrWhiteSpace(goal.Ideas) && string.IsNullOrWhiteSpace(goal.Decisions)) return;

        sb.AppendLine("## Relevant project memory");
        if (!string.IsNullOrWhiteSpace(goal.Decisions))
        {
            sb.AppendLine("### Past decisions (respect these)");
            sb.AppendLine(Compact(goal.Decisions!, options.MemoryMaxChars));
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(goal.Ideas))
        {
            sb.AppendLine("### Recent ideas");
            sb.AppendLine(Compact(goal.Ideas!, options.MemoryMaxChars));
            sb.AppendLine();
        }
    }

    private static string ApplyBudget(string prompt, PromptOptions options)
    {
        var max = Math.Max(1000, options.MaxChars);
        if (prompt.Length <= max) return prompt;

        var result = prompt;
        result = ShrinkSection(result, "## Creative plan for this run (from the knowledge base)", 800);
        result = ShrinkSection(result, "### Roadmap", 450);
        result = ShrinkSection(result, "### Backlog", 450);
        result = ShrinkSection(result, "## Relevant project memory", 800);
        result = ShrinkSection(result, "## Project prompt directives (self-evolved)",
            Math.Max(400, options.PromptDirectivesMinChars));
        result = ShrinkSection(result, "## Resume context (from the previous run)", 900);
        result = ShrinkSection(result, "## Recent run history (lessons)", 1000);
        if (result.Length <= max) return result;

        const string requiredHeading = "## Required output";
        const string protectedHeading = "## This run's task";
        var requiredStart = result.LastIndexOf(requiredHeading, StringComparison.Ordinal);
        var protectedStart = result.IndexOf(protectedHeading, StringComparison.Ordinal);
        if (requiredStart < 0 || protectedStart < 0 || protectedStart >= requiredStart)
            return Compact(result, max);

        var protectedTail = result[protectedStart..];
        var prefix = result[..protectedStart];
        var prefixBudget = max - protectedTail.Length;
        if (prefixBudget <= 0)
            return protectedTail.Length <= max ? protectedTail : Compact(result, max);

        var compactPrefix = Compact(prefix, prefixBudget);
        if (compactPrefix.Length > prefixBudget)
            compactPrefix = compactPrefix[..prefixBudget];
        return compactPrefix + protectedTail;
    }

    private static string ShrinkSection(string text, string heading, int targetChars)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return text;

        var contentStart = text.IndexOf('\n', start);
        if (contentStart < 0) return text;
        contentStart++;

        var end = FindNextSectionStart(text, contentStart);
        var sectionLength = end - start;
        if (sectionLength <= targetChars) return text;

        var bodyBudget = Math.Max(80, targetChars - heading.Length - 2);
        var body = text[contentStart..end];
        var replacement = heading + "\n" + Compact(body, bodyBudget).TrimEnd() + "\n\n";
        return text[..start] + replacement + text[end..];
    }

    private static int FindNextSectionStart(string text, int from)
    {
        var nextMain = text.IndexOf("\n## ", from, StringComparison.Ordinal);
        var nextSub = text.IndexOf("\n### ", from, StringComparison.Ordinal);
        if (nextMain < 0 && nextSub < 0) return text.Length;
        if (nextMain < 0) return nextSub + 1;
        if (nextSub < 0) return nextMain + 1;
        return Math.Min(nextMain, nextSub) + 1;
    }

    /// <summary>
    /// Compact a possibly-long markdown file for the prompt: if it is within the
    /// budget, keep it whole; otherwise keep the head and the tail (so both the
    /// framing and the most recent content survive) plus a list of its headings
    /// so structure is not lost.
    /// </summary>
    internal static string Compact(string text, int max)
    {
        text = text.Trim();
        if (text.Length <= max) return text;

        var headings = text.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.StartsWith("#"))
            .Take(12)
            .ToList();

        var headBudget = (int)(max * 0.6);
        var tailBudget = max - headBudget;
        var head = text[..headBudget];
        var tail = text[^tailBudget..];

        var sb = new StringBuilder();
        sb.Append(head).AppendLine().AppendLine("…(middle trimmed)…");
        if (headings.Count > 0)
        {
            sb.AppendLine("Section headings:");
            foreach (var h in headings) sb.AppendLine(h);
        }
        sb.AppendLine("…").Append(tail);
        return sb.ToString();
    }

    /// <summary>
    /// Inject the skills AutoDev selected for this run. This is the explicit
    /// invocation path — the agent is told exactly which skill to use, where its
    /// scripts live (absolute paths), and how to report its output — rather than
    /// relying on the model to discover a skill implicitly.
    /// </summary>
    private static void AppendSkills(StringBuilder sb, IReadOnlyList<SkillMatch>? skills)
    {
        if (skills is not { Count: > 0 }) return;

        sb.AppendLine("## AutoDev skills to use this run");
        sb.AppendLine("This task matched one or more global AutoDev skills. You MUST use the skill(s) below");
        sb.AppendLine("for the matching work — do not hand-roll an equivalent. Skills live in AutoDev's global");
        sb.AppendLine("store; write their OUTPUT into this project (never back into the skill directory).");
        sb.AppendLine();

        foreach (var match in skills)
        {
            var s = match.Skill;
            sb.AppendLine($"### {s.Name}  (`${s.Id}`)");
            if (!string.IsNullOrWhiteSpace(s.Description))
                sb.AppendLine(s.Description.Trim());
            if (!string.IsNullOrWhiteSpace(s.InvocationHint))
                sb.AppendLine($"- Invocation: {s.InvocationHint.Trim()}");
            sb.AppendLine($"- Matched keywords: {string.Join(", ", match.MatchedKeywords)}");
            if (!string.IsNullOrWhiteSpace(s.DocsPath))
                sb.AppendLine($"- Read the full instructions first: {s.DocsPath}");
            if (!string.IsNullOrWhiteSpace(s.GeneratePath))
                sb.AppendLine($"- Generate with: python \"{s.GeneratePath}\" --help  (then run it, writing --out into this project)");
            if (!string.IsNullOrWhiteSpace(s.ValidatePath))
                sb.AppendLine($"- Validate the result with: python \"{s.ValidatePath}\" <your-output-dir>");
            if (s.OutputConvention.Count > 0)
            {
                sb.AppendLine("- Output convention:");
                foreach (var line in s.OutputConvention)
                    sb.AppendLine($"    {line}");
            }
            sb.AppendLine();
        }
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(l => "    " + l));
}
