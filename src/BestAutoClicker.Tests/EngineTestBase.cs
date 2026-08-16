using BestAutoClicker.Core;
using BestAutoClicker.Models;

namespace BestAutoClicker.Tests;

/// <summary>Shared helpers for driving the clicker state machine deterministically.</summary>
internal static class EngineTestBase
{
    public static (ClickerEngine Engine, FakePlatform Platform) Create(Action<ClickerSettings>? configure = null)
    {
        var platform = new FakePlatform();
        var engine = new ClickerEngine(platform);
        var settings = new ClickerSettings();
        configure?.Invoke(settings);
        engine.SetSettings(settings);
        return (engine, platform);
    }

    /// <summary>Simulates <paramref name="clicks"/> fast real clicks spaced <paramref name="intervalMs"/> apart.</summary>
    public static void ClickFast(FakePlatform platform, int clicks, int intervalMs)
    {
        for (int i = 0; i < clicks; i++)
        {
            platform.SimulateClick();
            platform.Advance(intervalMs);
        }
    }

    public static void PumpSpam(ClickerEngine engine, FakePlatform platform, ClickerSettings settings, int steps, int stepMs, Action<int>? onStep = null)
    {
        for (int i = 0; i < steps; i++)
        {
            onStep?.Invoke(i);
            platform.Advance(stepMs);
            engine.TickSpam(settings);
        }
    }

    public static void PumpHold(ClickerEngine engine, FakePlatform platform, ClickerSettings settings, int steps, int stepMs, Action<int>? onStep = null)
    {
        for (int i = 0; i < steps; i++)
        {
            onStep?.Invoke(i);
            platform.Advance(stepMs);
            engine.TickHold(settings);
        }
    }

    public static ClickerSettings SpamSettings() => new()
    {
        Mode = ClickMode.Spam,
        SpamAutoClickCps = 10,
        SpamTriggerCps = 5,
        SpamDelayMs = 100,
        TriggerSampleWindowMs = 400,
        StopCheckCount = 3,
        StopCheckWindowMs = 150,
        ClickerThreadTickMs = 2,
        MinClickIntervalMs = 1
    };

    public static ClickerSettings HoldSettings(HoldSubMode subMode = HoldSubMode.Immediate) => new()
    {
        Mode = ClickMode.Hold,
        HoldSubMode = subMode,
        HoldAutoClickCps = 10,
        HoldDelayMs = 100,
        DoubleClickIntervalMs = 300,
        WaitForKeyVk = 0x10,
        StopCheckCount = 3,
        StopCheckWindowMs = 150,
        ClickerThreadTickMs = 2,
        MinClickIntervalMs = 1
    };
}
