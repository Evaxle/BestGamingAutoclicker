using BestAutoClicker.Models;

namespace BestAutoClicker.Tests;

public sealed class ClickerSettingsTests
{
    [Fact]
    public void Defaults_MatchOriginalCppEngine()
    {
        var s = new ClickerSettings();

        Assert.Equal("Default", s.ProfileName);
        Assert.Equal(ClickMode.Spam, s.Mode);
        Assert.Equal(10.0, s.SpamAutoClickCps);
        Assert.Equal(5.0, s.SpamTriggerCps);
        Assert.Equal(100, s.SpamDelayMs);
        Assert.Equal(10.0, s.HoldAutoClickCps);
        Assert.Equal(100, s.HoldDelayMs);
        Assert.Equal(HoldSubMode.Immediate, s.HoldSubMode);
        Assert.Equal(300, s.DoubleClickIntervalMs);
        Assert.Equal(0x10u, s.WaitForKeyVk); // VK_SHIFT
        Assert.Equal(3, s.StopCheckCount);
        Assert.Equal(150, s.StopCheckWindowMs);
        Assert.Equal(400, s.TriggerSampleWindowMs);
        Assert.Equal(2, s.ClickerThreadTickMs);
        Assert.Equal(1, s.MinClickIntervalMs);
    }

    [Fact]
    public void Clone_ProducesIndependentCopy()
    {
        var original = new ClickerSettings { SpamAutoClickCps = 25.0, HoldSubMode = HoldSubMode.DoubleClick };
        var copy = original.Clone();

        copy.SpamAutoClickCps = 1.0;
        copy.HoldSubMode = HoldSubMode.Immediate;

        Assert.Equal(25.0, original.SpamAutoClickCps);
        Assert.Equal(HoldSubMode.DoubleClick, original.HoldSubMode);
        Assert.Equal(1.0, copy.SpamAutoClickCps);
    }
}
