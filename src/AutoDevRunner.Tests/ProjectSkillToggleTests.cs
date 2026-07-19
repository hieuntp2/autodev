using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDevRunner.Tests;

/// <summary>
/// Per-project skill toggles: skills stay global, but each project can switch
/// individual skills off (and back on) without affecting other projects.
/// </summary>
public class ProjectSkillToggleTests : IDisposable
{
    private readonly string _dir;

    public ProjectSkillToggleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "projskills-" + Guid.NewGuid().ToString("N"));
        WriteSkill("alpha-skill", "zebra-trigger-alpha");
        WriteSkill("beta-skill", "zebra-trigger-beta");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void WriteSkill(string id, string trigger)
    {
        var skillDir = Path.Combine(_dir, "store", id);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "skill.json"),
            $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "1.0", "enabled": true, "triggers": ["{{trigger}}"] }""");
    }

    private SkillRegistry MakeRegistry()
    {
        var opt = new AutoDevOptions();
        opt.Skills.Root = Path.Combine(_dir, "store");
        opt.Skills.StateFile = Path.Combine(_dir, "skills-state.json");
        return new SkillRegistry(Options.Create(opt), NullLogger<SkillRegistry>.Instance);
    }

    [Fact]
    public void Skills_default_to_enabled_for_every_project()
    {
        var reg = MakeRegistry();

        Assert.Equal(2, reg.EnabledFor(1).Count);
        Assert.Equal(2, reg.EnabledFor(2).Count);
        Assert.Empty(reg.DisabledForProject(1));
    }

    [Fact]
    public void Disabling_for_one_project_does_not_affect_others_or_global()
    {
        var reg = MakeRegistry();

        Assert.True(reg.SetEnabledForProject(1, "alpha-skill", false));

        Assert.Equal(new[] { "beta-skill" }, reg.EnabledFor(1).Select(s => s.Id));
        Assert.Equal(2, reg.EnabledFor(2).Count);
        Assert.True(reg.Get("alpha-skill")!.EffectiveEnabled);

        // Matching honors the per-project toggle.
        Assert.Empty(reg.Match("do the zebra-trigger-alpha thing", projectId: 1));
        Assert.Single(reg.Match("do the zebra-trigger-alpha thing", projectId: 2));
        Assert.Single(reg.Match("do the zebra-trigger-alpha thing"));
    }

    [Fact]
    public void Reenabling_restores_the_skill_for_the_project()
    {
        var reg = MakeRegistry();
        reg.SetEnabledForProject(1, "alpha-skill", false);

        Assert.True(reg.SetEnabledForProject(1, "alpha-skill", true));

        Assert.Equal(2, reg.EnabledFor(1).Count);
        Assert.Empty(reg.DisabledForProject(1));
    }

    [Fact]
    public void Project_toggles_persist_across_registry_restarts()
    {
        MakeRegistry().SetEnabledForProject(7, "beta-skill", false);

        var reloaded = MakeRegistry();

        Assert.Equal(new[] { "beta-skill" }, reloaded.DisabledForProject(7));
        Assert.Equal(new[] { "alpha-skill" }, reloaded.EnabledFor(7).Select(s => s.Id));
    }

    [Fact]
    public void Globally_disabled_skill_is_off_for_all_projects_regardless_of_toggle()
    {
        var reg = MakeRegistry();
        reg.SetEnabled("alpha-skill", false);
        reg.SetEnabledForProject(1, "alpha-skill", true);

        Assert.DoesNotContain(reg.EnabledFor(1), s => s.Id == "alpha-skill");
    }

    [Fact]
    public void Unknown_skill_id_is_rejected()
    {
        Assert.False(MakeRegistry().SetEnabledForProject(1, "nope", false));
    }
}

/// <summary>
/// Per-lifecycle-stage skill toggles: each stage can narrow which skills may be
/// used while a run is in that stage (default: every skill on in every stage).
/// </summary>
public class LifecycleSkillToggleTests : IDisposable
{
    private readonly string _dir;

    public LifecycleSkillToggleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lcskills-" + Guid.NewGuid().ToString("N"));
        WriteSkill("alpha-skill", "yak-trigger-alpha");
        WriteSkill("beta-skill", "yak-trigger-beta");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void WriteSkill(string id, string trigger)
    {
        var skillDir = Path.Combine(_dir, "store", id);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "skill.json"),
            $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "1.0", "enabled": true, "triggers": ["{{trigger}}"] }""");
    }

    private SkillRegistry MakeRegistry()
    {
        var opt = new AutoDevOptions();
        opt.Skills.Root = Path.Combine(_dir, "store");
        opt.Skills.StateFile = Path.Combine(_dir, "skills-state.json");
        return new SkillRegistry(Options.Create(opt), NullLogger<SkillRegistry>.Instance);
    }

    [Fact]
    public void Skills_default_to_enabled_for_every_stage()
    {
        var reg = MakeRegistry();

        foreach (var stage in Enum.GetValues<LifecycleStage>())
        {
            Assert.Empty(reg.DisabledForStage(stage));
            Assert.False(reg.IsStageDisabled(stage, "alpha-skill"));
        }
        Assert.Single(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Running));
    }

    [Fact]
    public void Disabling_for_one_stage_does_not_affect_other_stages()
    {
        var reg = MakeRegistry();

        Assert.True(reg.SetEnabledForStage(LifecycleStage.Running, "alpha-skill", false));

        Assert.Empty(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Running));
        Assert.Single(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Idea));
        Assert.Single(reg.Match("yak-trigger-alpha", projectId: 1));           // no stage = no stage filter
        Assert.Equal(new[] { "beta-skill" },
            reg.EnabledFor(1, LifecycleStage.Running).Select(s => s.Id));
        Assert.True(reg.Get("alpha-skill")!.EffectiveEnabled);                 // global state untouched
    }

    [Fact]
    public void Stage_toggle_works_without_a_project_filter()
    {
        var reg = MakeRegistry();
        reg.SetEnabledForStage(LifecycleStage.Idea, "beta-skill", false);

        Assert.Empty(reg.Match("yak-trigger-beta", stage: LifecycleStage.Idea));
        Assert.Single(reg.Match("yak-trigger-beta"));
    }

    [Fact]
    public void Reenabling_restores_the_skill_for_the_stage()
    {
        var reg = MakeRegistry();
        reg.SetEnabledForStage(LifecycleStage.Running, "alpha-skill", false);

        Assert.True(reg.SetEnabledForStage(LifecycleStage.Running, "alpha-skill", true));

        Assert.Empty(reg.DisabledForStage(LifecycleStage.Running));
        Assert.Single(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Running));
    }

    [Fact]
    public void Stage_toggles_persist_across_registry_restarts()
    {
        MakeRegistry().SetEnabledForStage(LifecycleStage.Validated, "alpha-skill", false);

        var reloaded = MakeRegistry();

        Assert.Equal(new[] { "alpha-skill" }, reloaded.DisabledForStage(LifecycleStage.Validated));
        Assert.True(reloaded.IsStageDisabled(LifecycleStage.Validated, "alpha-skill"));
    }

    [Fact]
    public void Stage_project_and_global_filters_all_compose()
    {
        var reg = MakeRegistry();
        reg.SetEnabledForStage(LifecycleStage.Running, "alpha-skill", false);
        reg.SetEnabledForProject(1, "beta-skill", false);

        Assert.Empty(reg.EnabledFor(1, LifecycleStage.Running));               // both filtered out
        Assert.Equal(new[] { "alpha-skill" },
            reg.EnabledFor(1, LifecycleStage.Idea).Select(s => s.Id));         // beta off for project 1
        Assert.Equal(new[] { "beta-skill" },
            reg.EnabledFor(2, LifecycleStage.Running).Select(s => s.Id));      // alpha off in Running
    }

    [Fact]
    public void Unknown_skill_id_is_rejected()
    {
        Assert.False(MakeRegistry().SetEnabledForStage(LifecycleStage.Running, "nope", false));
    }

    [Fact]
    public void Project_stage_toggle_only_affects_that_project_and_stage()
    {
        var reg = MakeRegistry();

        Assert.True(reg.SetEnabledForProjectStage(1, LifecycleStage.Running, "alpha-skill", false));

        Assert.Empty(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Running));
        Assert.Single(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Idea));   // other stage
        Assert.Single(reg.Match("yak-trigger-alpha", projectId: 2, stage: LifecycleStage.Running)); // other project
        Assert.Empty(reg.DisabledForStage(LifecycleStage.Running));                                 // global matrix untouched
        Assert.True(reg.IsProjectStageDisabled(1, LifecycleStage.Running, "alpha-skill"));
    }

    [Fact]
    public void Project_stage_toggles_persist_across_registry_restarts()
    {
        MakeRegistry().SetEnabledForProjectStage(4, LifecycleStage.Idea, "beta-skill", false);

        var reloaded = MakeRegistry();

        Assert.True(reloaded.IsProjectStageDisabled(4, LifecycleStage.Idea, "beta-skill"));
        var overrides = reloaded.DisabledStagesForProject(4);
        Assert.Equal(new[] { "beta-skill" }, overrides[LifecycleStage.Idea]);
        Assert.Equal(new[] { "alpha-skill" },
            reloaded.EnabledFor(4, LifecycleStage.Idea).Select(s => s.Id));
    }

    [Fact]
    public void Reenabling_a_project_stage_override_restores_the_global_default()
    {
        var reg = MakeRegistry();
        reg.SetEnabledForProjectStage(1, LifecycleStage.Running, "alpha-skill", false);

        Assert.True(reg.SetEnabledForProjectStage(1, LifecycleStage.Running, "alpha-skill", true));

        Assert.Empty(reg.DisabledStagesForProject(1));
        Assert.Single(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Running));
    }

    [Fact]
    public void Global_stage_off_wins_over_a_project_level_enable()
    {
        var reg = MakeRegistry();
        reg.SetEnabledForStage(LifecycleStage.Running, "alpha-skill", false);
        reg.SetEnabledForProjectStage(1, LifecycleStage.Running, "alpha-skill", true);

        Assert.Empty(reg.Match("yak-trigger-alpha", projectId: 1, stage: LifecycleStage.Running));
    }
}
