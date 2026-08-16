using BestAutoClicker.Models;

namespace BestAutoClicker.Tests;

public sealed class SpamModeEngineTests
{
    [Fact]
    public void Spam_DoesNotClickUntilArmed()
    {
        var (engine, platform) = EngineTestBase.Create(s => { s.Mode = ClickMode.Spam; });
        var settings = engine.CurrentSettings();
        engine.Enable();
        engine.ResetTickState();

        engine.TickSpam(settings);
        engine.TickSpam(settings);

        Assert.Equal(RunState.Idle, engine.CurrentState);
        Assert.Equal(0, platform.SentClickCount);
    }

    [Fact]
    public void Spam_SlowClicking_DoesNotArm()
    {
        var (engine, platform) = EngineTestBase.Create(s => { s.Mode = ClickMode.Spam; s.SpamTriggerCps = 5; });
        var settings = engine.CurrentSettings();
        engine.Enable();
        engine.ResetTickState();

        // ~2 clicks/sec, below the 5 cps trigger
        EngineTestBase.ClickFast(platform, 3, 500);
        engine.TickSpam(settings);

        Assert.Equal(RunState.Idle, engine.CurrentState);
        Assert.Equal(0, platform.SentClickCount);
    }

    [Fact]
    public void Spam_FastClicking_ArmsThenActivates()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.SpamSettings();
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        EngineTestBase.ClickFast(platform, 8, 20); // ~20 cps
        engine.TickSpam(settings);
        Assert.Equal(RunState.ArmedWaitingDelay, engine.CurrentState);

        // keep clicking through the 100ms arm delay
        for (int i = 0; i < 6; i++)
        {
            platform.SimulateClick();
            platform.Advance(20);
            engine.TickSpam(settings);
        }

        Assert.Equal(RunState.Active, engine.CurrentState);
    }

    [Fact]
    public void Spam_Active_ClicksAtConfiguredCps_WhileUserKeepsClicking()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.SpamSettings();
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        EngineTestBase.ClickFast(platform, 8, 20);
        engine.TickSpam(settings); // armed
        engine.TickSpam(settings); // active (after 8 clicks now ~20cps, delay checks next tick)
        for (int i = 0; i < 6; i++) { platform.SimulateClick(); platform.Advance(20); engine.TickSpam(settings); }
        Assert.Equal(RunState.Active, engine.CurrentState);

        int before = platform.SentClickCount;
        // 1000ms while the user keeps clicking (every 50ms) to refresh the grace window
        for (int i = 0; i < 100; i++)
        {
            platform.Advance(10);
            if (i % 5 == 0) platform.SimulateClick();
            engine.TickSpam(settings);
        }

        int clicks = platform.SentClickCount - before;
        Assert.InRange(clicks, 7, 13); // ~10 cps
        Assert.Equal(RunState.Active, engine.CurrentState);
    }

    [Fact]
    public void Spam_UserStops_StopsAfterGrace_NoRunawayClicks()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.SpamSettings();
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        EngineTestBase.ClickFast(platform, 8, 20);
        engine.TickSpam(settings);
        for (int i = 0; i < 6; i++) { platform.SimulateClick(); platform.Advance(20); engine.TickSpam(settings); }
        Assert.Equal(RunState.Active, engine.CurrentState);

        // user stops clicking entirely
        EngineTestBase.PumpSpam(engine, platform, settings, 80, 10);
        Assert.Equal(RunState.Idle, engine.CurrentState);

        int afterIdle = platform.SentClickCount;
        EngineTestBase.PumpSpam(engine, platform, settings, 50, 10);
        Assert.Equal(afterIdle, platform.SentClickCount); // no clicks after stopping
    }

    [Fact]
    public void Spam_HigherCps_ClickFaster()
    {
        static int ClicksInSecond(double cps)
        {
            var (engine, platform) = EngineTestBase.Create(s =>
            {
                s.Mode = ClickMode.Spam;
                s.SpamTriggerCps = 1;
                s.SpamAutoClickCps = cps;
                s.SpamDelayMs = 0;
                s.TriggerSampleWindowMs = 400;
                s.StopCheckCount = 3;
                s.StopCheckWindowMs = 150;
                s.ClickerThreadTickMs = 2;
                s.MinClickIntervalMs = 1;
            });
            var settings = engine.CurrentSettings();
            engine.Enable();
            engine.ResetTickState();

            EngineTestBase.ClickFast(platform, 8, 20);
            engine.TickSpam(settings); // armed
            engine.TickSpam(settings); // delay 0 -> active

            int before = platform.SentClickCount;
            for (int i = 0; i < 100; i++)
            {
                platform.Advance(10);
                if (i % 5 == 0) platform.SimulateClick();
                engine.TickSpam(settings);
            }
            return platform.SentClickCount - before;
        }

        int fast = ClicksInSecond(20);
        int slow = ClicksInSecond(5);

        Assert.InRange(fast, 15, 25);
        Assert.InRange(slow, 3, 7);
        Assert.True(fast > slow * 2, $"higher CPS should click faster (fast={fast}, slow={slow})");
    }

    [Fact]
    public void Spam_MinClickInterval_ClampsVeryHighCps()
    {
        var (engine, platform) = EngineTestBase.Create(s =>
        {
            s.Mode = ClickMode.Spam;
            s.SpamTriggerCps = 1;
            s.SpamAutoClickCps = 200; // wants 5ms interval
            s.SpamDelayMs = 0;
            s.TriggerSampleWindowMs = 400;
            s.StopCheckCount = 3;
            s.StopCheckWindowMs = 150;
            s.ClickerThreadTickMs = 2;
            s.MinClickIntervalMs = 50; // clamps to 50ms -> max 20 cps
        });
        var settings = engine.CurrentSettings();
        engine.Enable();
        engine.ResetTickState();

        EngineTestBase.ClickFast(platform, 8, 20);
        engine.TickSpam(settings);
        engine.TickSpam(settings);

        int before = platform.SentClickCount;
        for (int i = 0; i < 100; i++)
        {
            platform.Advance(10);
            if (i % 5 == 0) platform.SimulateClick();
            engine.TickSpam(settings);
        }

        int clicks = platform.SentClickCount - before;
        Assert.InRange(clicks, 15, 25); // ~20 cps, NOT ~200
    }
}
