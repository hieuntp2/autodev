using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using AutoDevRunner.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDevRunner.Tests;

public class SummaryParserV2Tests
{
    private readonly SummaryParser _parser = new();

    [Fact]
    public void Parses_v2_fields()
    {
        var output = "blah blah\n" + PromptBuilder.SummaryMarker + "\n" +
            "TASK_TITLE: Add blink animation\n" +
            "TASK_STATUS: committed\n" +
            "DONE: created blink frames\n" +
            "SKILL_USED: pixel-animation-artist\n" +
            "ARTIFACT_PATHS: assets/pet/blink/blink_sheet.png, assets/pet/blink/blink.animation.json\n" +
            "FILES_CHANGED: src/Player.cs\n" +
            "VALIDATION_RESULT: PASS\n" +
            "RISK_LEVEL: safe\n" +
            "NEXT_SUGGESTED_TASKS: add hop; add sleep\n" +
            "MEMORY_UPDATES: decided to keep 64x32 canvas\n" +
            "NEXT_TASK: build the runtime player\n";
        var s = _parser.Parse(output);

        Assert.Equal("Add blink animation", s.TaskTitle);
        Assert.Equal("committed", s.TaskStatus);
        Assert.Equal("pixel-animation-artist", s.SkillUsed);
        Assert.Contains("blink_sheet.png", s.ArtifactPaths);
        Assert.Equal("PASS", s.ValidationResult);
        Assert.Equal("safe", s.RiskLevel);
        Assert.Contains("hop", s.NextSuggestedTasks);
        Assert.Contains("64x32", s.MemoryUpdates);
        Assert.Equal("build the runtime player", s.NextTask);
    }

    [Fact]
    public void Old_v1_block_still_parses_and_v2_aliases_fill()
    {
        var output = PromptBuilder.SummaryMarker + "\n" +
            "TASK: do a thing\nDONE: did it\nSKILL: pixel-animation-artist\n" +
            "ASSET_PATH: assets/x\nVALIDATION: PASS\nNEXT_ANIMATIONS: hop\n";
        var s = _parser.Parse(output);

        Assert.Equal("do a thing", s.Task);
        Assert.Equal("do a thing", s.EffectiveTitle); // falls back to TASK
        Assert.Equal("pixel-animation-artist", s.SkillUsed);   // alias of SKILL
        Assert.Equal("assets/x", s.ArtifactPaths);             // alias of ASSET_PATH
        Assert.Equal("PASS", s.ValidationResult);              // alias of VALIDATION
        Assert.Equal("hop", s.NextSuggestedTasks);             // alias of NEXT_ANIMATIONS
    }
}

public class TaskProposerTests
{
    private static TaskProposer Make()
    {
        var skills = new SkillRegistry(Options.Create(new AutoDevOptions()), NullLogger<SkillRegistry>.Instance);
        return new TaskProposer(new RiskAssessor(), skills);
    }

    private static ProjectGoal Goal(string? backlog = null, string? ideas = null)
        => new("goal", null, backlog, ideas, null);

    [Fact]
    public void Prefers_first_open_backlog_item()
    {
        var p = Make().Propose(new Project { Name = "P" },
            Goal(backlog: "# Backlog\n- [x] done thing\n- [ ] Add a happy hop animation\n- [ ] later"));
        Assert.Equal("backlog", p.Source);
        Assert.Contains("happy hop", p.Title);
    }

    [Fact]
    public void Falls_back_to_ideas_then_maintenance()
    {
        var fromIdeas = Make().Propose(new Project { Name = "P" }, Goal(ideas: "# Ideas\n- Try a wave animation"));
        Assert.Equal("ideas", fromIdeas.Source);

        var maintenance = Make().Propose(new Project { Name = "P" }, Goal());
        Assert.Equal("maintenance", maintenance.Source);
        Assert.False(string.IsNullOrWhiteSpace(maintenance.Title));
    }

    [Fact]
    public void Suggests_pixel_skill_for_animation_backlog_item()
    {
        var p = Make().Propose(new Project { Name = "P" },
            Goal(backlog: "- [ ] Create a sprite sheet pixel animation for the pet"));
        Assert.Equal("pixel-animation-artist", p.SuggestedSkill);
    }
}

public class ArtifactMergeTests : IDisposable
{
    private readonly string _repo;
    private readonly ArtifactTracker _tracker = new();

    public ArtifactMergeTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "admerge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_repo, "assets", "pet", "frames"));
        File.WriteAllText(Path.Combine(_repo, "assets", "pet", "pet_sheet.png"), "x");
        File.WriteAllText(Path.Combine(_repo, "assets", "pet", "pet.animation.json"), "{}");
        File.WriteAllText(Path.Combine(_repo, "assets", "pet", "frames", "frame_000.png"), "x");
    }

    public void Dispose() { try { Directory.Delete(_repo, true); } catch { } }

    [Fact]
    public void Merges_declared_directory_and_dedupes_with_git()
    {
        var git = _tracker.Track(new[] { "assets/pet/pet_sheet.png" }, _repo);
        var merged = _tracker.Merge(git, "assets/pet", _repo); // declare the whole folder

        var paths = merged.Select(a => a.Path).ToHashSet();
        Assert.Contains("assets/pet/pet_sheet.png", paths);        // from git (not duplicated)
        Assert.Contains("assets/pet/pet.animation.json", paths);   // discovered in folder
        Assert.Contains("assets/pet/frames/frame_000.png", paths); // discovered recursively
        Assert.Equal(paths.Count, merged.Count);                   // no duplicates
    }

    [Fact]
    public void Ignores_paths_outside_the_repo()
    {
        var merged = _tracker.Merge(new List<ArtifactRef>(), "../../etc/passwd, C:/Windows/win.ini", _repo);
        Assert.Empty(merged);
    }
}

public class RiskAndPromptV2Tests
{
    [Fact]
    public void Large_refactor_intent_is_risky()
    {
        var r = new RiskAssessor().Assess(Array.Empty<GitChange>(), "Do a full rewrite of the entire codebase");
        Assert.Equal(RiskLevel.Risky, r.Level);
    }

    [Fact]
    public void Prompt_shows_risk_constraint_and_expanded_summary_fields()
    {
        var prompt = new PromptBuilder().Build(
            new Project { Name = "P", RepoPath = "/r", CurrentTask = "add blink" },
            brief: "b", run: new RunRecord(), creativePlan: null, skills: null,
            goal: new ProjectGoal("make a pet", null, null, null, null),
            proposal: null, risk: RiskLevel.Normal);

        Assert.Contains("## Constraints", prompt);
        Assert.Contains("risk level for this task: normal", prompt);
        Assert.Contains("ARTIFACT_PATHS:", prompt);
        Assert.Contains("RISK_LEVEL:", prompt);
        Assert.Contains("MEMORY_UPDATES:", prompt);
        Assert.Contains("NEXT_SUGGESTED_TASKS:", prompt);
    }

    [Fact]
    public void Long_goal_is_compacted_head_and_tail()
    {
        var longGoal = "# Vision\n" + new string('A', 2000) + "\n## End\nfinal-marker";
        var prompt = new PromptBuilder().Build(
            new Project { Name = "P", RepoPath = "/r", CurrentTask = "t" },
            brief: "b", run: new RunRecord(), creativePlan: null, skills: null,
            goal: new ProjectGoal(longGoal, null, null, null, null));

        Assert.Contains("(middle trimmed)", prompt); // compaction kicked in
        Assert.Contains("final-marker", prompt);      // tail preserved
    }

    [Fact]
    public void Proposal_drives_task_when_no_current_task()
    {
        var proposal = new TaskProposal("Add hop animation", "next backlog item",
            "(agent decides)", "a validated animation", "dotnet build", RiskLevel.Safe, "pixel-animation-artist", "backlog");
        var prompt = new PromptBuilder().Build(
            new Project { Name = "P", RepoPath = "/r", CurrentTask = null },
            brief: "b", run: new RunRecord(), creativePlan: null, skills: null,
            goal: new ProjectGoal("g", null, null, null, null), proposal: proposal, risk: RiskLevel.Safe);

        Assert.Contains("Add hop animation", prompt);
        Assert.Contains("Proposed by AutoDev from backlog", prompt);
        Assert.Contains("dotnet build", prompt);
    }
}
