using BestAutoClicker.Models;

namespace BestAutoClicker.Tests;

public sealed class HoldModeEngineTests
{
    [Fact]
    public void Hold_Immediate_DoesNotClickBeforePress()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings(HoldSubMode.Immediate);
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        EngineTestBase.PumpHold(engine, platform, settings, 10, 10);

        Assert.Equal(RunState.Idle, engine.CurrentState);
        Assert.Equal(0, platform.SentClickCount);
    }

    [Fact]
    public void Hold_Immediate_FullCycle_ArmsOnPress_ClicksWhileHeld_StopsOnRelease()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings(HoldSubMode.Immediate);
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        // press -> armed
        platform.SimulateMouseDown();
        engine.TickHold(settings);
        Assert.Equal(RunState.ArmedWaitingDelay, engine.CurrentState);

        // hold through 100ms arm delay -> active
        EngineTestBase.PumpHold(engine, platform, settings, 12, 10);
        Assert.Equal(RunState.Active, engine.CurrentState);

        // keeps clicking (~10 cps) while held
        int before = platform.SentClickCount;
        EngineTestBase.PumpHold(engine, platform, settings, 100, 10);
        Assert.InRange(platform.SentClickCount - before, 7, 13);

        // release -> idle after stop-check, and no more clicks
        platform.SimulateMouseUp();
        EngineTestBase.PumpHold(engine, platform, settings, 30, 10);
        Assert.Equal(RunState.Idle, engine.CurrentState);

        int after = platform.SentClickCount;
        EngineTestBase.PumpHold(engine, platform, settings, 50, 10);
        Assert.Equal(after, platform.SentClickCount);
    }

    [Fact]
    public void Hold_DoubleClick_SingleClickTimesOut_DoesNotClick()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings(HoldSubMode.DoubleClick);
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        // one click (down + up) -> waiting for the second click
        platform.SimulateMouseDown();
        engine.TickHold(settings);
        platform.SimulateMouseUp();
        engine.TickHold(settings);
        Assert.Equal(RunState.WaitingSecondClick, engine.CurrentState);

        // no second click within 300ms -> back to idle, no clicks
        EngineTestBase.PumpHold(engine, platform, settings, 50, 10); // 500ms
        Assert.Equal(RunState.Idle, engine.CurrentState);
        Assert.Equal(0, platform.SentClickCount);
    }

    [Fact]
    public void Hold_DoubleClick_FullCycle()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings(HoldSubMode.DoubleClick);
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        // click 1
        platform.SimulateMouseDown();
        engine.TickHold(settings);
        platform.SimulateMouseUp();
        engine.TickHold(settings);
        Assert.Equal(RunState.WaitingSecondClick, engine.CurrentState);

        // click 2 within interval -> armed
        platform.Advance(50);
        platform.SimulateMouseDown();
        engine.TickHold(settings);
        Assert.Equal(RunState.ArmedWaitingDelay, engine.CurrentState);

        // hold -> active -> clicks
        EngineTestBase.PumpHold(engine, platform, settings, 12, 10);
        Assert.Equal(RunState.Active, engine.CurrentState);

        int before = platform.SentClickCount;
        EngineTestBase.PumpHold(engine, platform, settings, 50, 10);
        Assert.True(platform.SentClickCount - before >= 3);

        // release -> idle
        platform.SimulateMouseUp();
        EngineTestBase.PumpHold(engine, platform, settings, 30, 10);
        Assert.Equal(RunState.Idle, engine.CurrentState);
    }

    [Fact]
    public void Hold_WaitForKey_RequiresKeyAndMouse()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings(HoldSubMode.WaitForKey);
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        // mouse alone does not arm
        platform.SimulateMouseDown();
        engine.TickHold(settings);
        Assert.Equal(RunState.Idle, engine.CurrentState);
        platform.SimulateMouseUp();
        engine.TickHold(settings);

        // key alone does not arm
        platform.PressKey(0x10);
        engine.TickHold(settings);
        Assert.Equal(RunState.Idle, engine.CurrentState);

        // key + mouse -> armed
        platform.SimulateMouseDown();
        engine.TickHold(settings);
        Assert.Equal(RunState.ArmedWaitingDelay, engine.CurrentState);

        // releasing the key while armed cancels it
        platform.ReleaseKey(0x10);
        engine.TickHold(settings);
        Assert.Equal(RunState.Idle, engine.CurrentState);
        Assert.Equal(0, platform.SentClickCount);

        // reset for a clean rising edge
        platform.SimulateMouseUp();
        engine.TickHold(settings);
    }

    [Fact]
    public void Hold_WaitForKey_FullCycle()
    {
        var (engine, platform) = EngineTestBase.Create();
        var settings = EngineTestBase.HoldSettings(HoldSubMode.WaitForKey);
        engine.SetSettings(settings);
        engine.Enable();
        engine.ResetTickState();

        platform.PressKey(0x10);
        platform.SimulateMouseDown();
        engine.TickHold(settings);
        Assert.Equal(RunState.ArmedWaitingDelay, engine.CurrentState);

        EngineTestBase.PumpHold(engine, platform, settings, 12, 10);
        Assert.Equal(RunState.Active, engine.CurrentState);

        int before = platform.SentClickCount;
        EngineTestBase.PumpHold(engine, platform, settings, 50, 10);
        Assert.True(platform.SentClickCount - before >= 3);

        // releasing the key while active stops the clicker
        platform.ReleaseKey(0x10);
        EngineTestBase.PumpHold(engine, platform, settings, 30, 10);
        Assert.Equal(RunState.Idle, engine.CurrentState);

        int after = platform.SentClickCount;
        EngineTestBase.PumpHold(engine, platform, settings, 30, 10);
        Assert.Equal(after, platform.SentClickCount);
    }
}
