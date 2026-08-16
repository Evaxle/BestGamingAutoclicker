using BestAutoClicker.Models;

namespace BestAutoClicker.Tests;

/// <summary>
/// Tests for the enable/disable (Start/Stop) gating and the constant
/// stop-check safety system that verifies the user is still holding / clicking.
/// </summary>
public sealed class EngineSafetyAndParamsTests
{
    [Fact]
    public void Disabled_Engine_IgnoresInput_AndNeverClicks()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings();
        engine.SetSettings(settings);
        engine.ResetTickState();

        Assert.False(engine.IsEnabled);

        platform.SimulateMouseDown();
        EngineTestBase.PumpHold(engine, platform, settings, 20, 10);

        Assert.Equal(RunState.Idle, engine.CurrentState);
        Assert.Equal(0, platform.SentClickCount);
    }

    [Fact]
    public void Enable_ThenDisable_StopsClickingImmediately()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings();
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        platform.SimulateMouseDown();
        engine.TickHold(settings);
        Assert.Equal(RunState.ArmedWaitingDelay, engine.CurrentState);

        EngineTestBase.PumpHold(engine, platform, settings, 12, 10);
        Assert.Equal(RunState.Active, engine.CurrentState);
        Assert.True(platform.SentClickCount > 0);

        engine.Disable();
        Assert.False(engine.IsEnabled);
        Assert.Equal(RunState.Idle, engine.CurrentState);

        int afterDisable = platform.SentClickCount;
        EngineTestBase.PumpHold(engine, platform, settings, 30, 10);
        Assert.Equal(afterDisable, platform.SentClickCount);
    }

    [Fact]
    public void StopCheck_HigherCount_StopsLater_ButAlwaysStops()
    {
        static int TicksToStop(int stopCheckCount)
        {
            var (engine, platform) = EngineTestBase.Create(s =>
            {
                s.Mode = ClickMode.Hold;
                s.HoldSubMode = HoldSubMode.Immediate;
                s.HoldAutoClickCps = 10;
                s.HoldDelayMs = 0;
                s.StopCheckCount = stopCheckCount;
                s.StopCheckWindowMs = 200;
                s.ClickerThreadTickMs = 2;
                s.MinClickIntervalMs = 1;
            });
            var settings = engine.CurrentSettings();
            engine.Enable();
            engine.ResetTickState();

            platform.SimulateMouseDown();
            engine.TickHold(settings); // armed (delay 0)
            engine.TickHold(settings); // active
            Assert.Equal(RunState.Active, engine.CurrentState);

            platform.SimulateMouseUp();
            int ticks = 0;
            while (engine.CurrentState != RunState.Idle && ticks < 100)
            {
                platform.Advance(10);
                engine.TickHold(settings);
                ticks++;
            }
            return ticks;
        }

        int fast = TicksToStop(1);
        int slow = TicksToStop(6);

        Assert.True(fast < slow, $"stop-check 1 should stop sooner ({fast} ticks) than 6 ({slow} ticks)");
        Assert.True(slow < 100, "must always stop, never run forever");
    }

    [Fact]
    public void StopCheckWindow_ExpiresOldSamples_StillStops()
    {
        var (engine, platform) = EngineTestBase.Create(s =>
        {
            s.Mode = ClickMode.Hold;
            s.HoldSubMode = HoldSubMode.Immediate;
            s.HoldAutoClickCps = 10;
            s.HoldDelayMs = 0;
            s.StopCheckCount = 5;
            s.StopCheckWindowMs = 40; // tight window
            s.ClickerThreadTickMs = 2;
            s.MinClickIntervalMs = 1;
        });
        var settings = engine.CurrentSettings();
        engine.Enable();
        engine.ResetTickState();

        platform.SimulateMouseDown();
        engine.TickHold(settings);
        engine.TickHold(settings);
        Assert.Equal(RunState.Active, engine.CurrentState);

        platform.SimulateMouseUp();
        int ticks = 0;
        while (engine.CurrentState != RunState.Idle && ticks < 100)
        {
            platform.Advance(10);
            engine.TickHold(settings);
            ticks++;
        }

        Assert.Equal(RunState.Idle, engine.CurrentState);
        Assert.True(ticks < 100);
    }

    [Fact]
    public void KeyCapture_ReportsTheNextRealKeyOnly()
    {
        var (engine, platform) = EngineTestBase.Create();
        uint? captured = null;
        engine.KeyCaptured += vk => captured = vk;

        // nothing captured while not in capture mode
        platform.RaiseKeyDown(0x41);
        Assert.Null(captured);

        // capture mode: next key is reported
        engine.CaptureNextKey = true;
        platform.RaiseKeyDown(0x41);
        Assert.Equal(0x41u, captured);

        // after capture mode ends, keys are ignored again
        engine.CaptureNextKey = false;
        captured = null;
        platform.RaiseKeyDown(0x42);
        Assert.Null(captured);
    }

    [Fact]
    public void SetSettings_ThenEnable_UsesLatestSettings()
    {
        var (engine, platform) = EngineTestBase.Create();
        var fast = EngineTestBase.SpamSettings();
        fast.SpamAutoClickCps = 50;
        engine.SetSettings(fast);
        engine.Enable();
        engine.ResetTickState();

        // switch to a slower profile while running, then it must slow down
        var slow = EngineTestBase.SpamSettings();
        slow.SpamAutoClickCps = 5;
        engine.SetSettings(slow);

        EngineTestBase.ClickFast(platform, 8, 20);
        engine.TickSpam(engine.CurrentSettings()); // armed
        engine.TickSpam(engine.CurrentSettings()); // active (delay 0)

        int before = platform.SentClickCount;
        EngineTestBase.PumpSpam(engine, platform, engine.CurrentSettings(), 100, 10,
            i => { if (i % 5 == 0) platform.SimulateClick(); });

        int clicks = platform.SentClickCount - before;
        Assert.InRange(clicks, 3, 7); // ~5 cps, not 50
    }
}
