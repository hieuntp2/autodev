using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Xunit;

namespace AutoDevRunner.Tests;

public class AutoDevOptionsTests
{
    [Fact]
    public void BurnTokens_enabled_controls_burn_loop()
    {
        var opt = new AutoDevOptions
        {
            BurnTokens = new BurnTokensOptions { Enabled = true },
            Continuous = new ContinuousOptions { Enabled = false }
        };

        Assert.True(opt.BurnTokensEnabled);
    }

    [Fact]
    public void BurnTokens_false_overrides_legacy_continuous_enabled()
    {
        var opt = new AutoDevOptions
        {
            BurnTokens = new BurnTokensOptions { Enabled = false },
            Continuous = new ContinuousOptions { Enabled = true }
        };

        Assert.False(opt.BurnTokensEnabled);
    }

    [Fact]
    public void Missing_BurnTokens_setting_falls_back_to_legacy_continuous_enabled()
    {
        var opt = new AutoDevOptions
        {
            Continuous = new ContinuousOptions { Enabled = true }
        };

        Assert.True(opt.BurnTokensEnabled);
    }

    [Fact]
    public void Execution_defaults_use_idle_timeout_and_heartbeat()
    {
        var opt = new AutoDevOptions();

        Assert.Equal(15, opt.Execution.IdleTimeoutMinutes);
        Assert.Equal(5, opt.Execution.HeartbeatMinutes);
    }

    [Fact]
    public void New_projects_default_to_long_hard_backstop()
    {
        var project = new Project();

        Assert.Equal(240, project.MaxRunMinutes);
    }

    [Fact]
    public void Default_risk_policy_allows_in_repo_deletions_but_blocks_out_of_project_changes()
    {
        var opt = new AutoDevOptions();

        Assert.False(opt.Risk.BlockFileDeletions);
        Assert.True(opt.Risk.BlockOutOfProjectChanges);
    }
}
