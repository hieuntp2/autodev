using AutoDevRunner.Config;
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
}
