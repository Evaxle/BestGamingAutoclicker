using BestAutoClicker.Core;

namespace BestAutoClicker.Tests;

/// <summary>
/// Scriptable <see cref="IClickerPlatform"/> for deterministic engine tests.
/// Time is virtual (<see cref="Now"/>), the mouse/key state is driven by the
/// test, and every synthetic click is counted in <see cref="SentClickCount"/>.
/// </summary>
public sealed class FakePlatform : IClickerPlatform
{
    private readonly object _lock = new();
    private readonly List<long> _clickTimes = new();
    private readonly HashSet<uint> _keysDown = new();

    public long Now { get; set; } = 100_000; // start nonzero like a real Stopwatch
    public bool MouseDownState { get; set; }
    public int SentClickCount { get; set; }
    public bool CaptureNextKey { get; set; }

    public event Action<long>? MouseDown;
    public event Action<long>? MouseUp;
    public event Action<uint>? KeyDown;

    public long NowMs => Now;
    public bool IsMouseDown => MouseDownState;
    public bool IsKeyDown(uint vk) => _keysDown.Contains(vk);

    public double RecentCps(int windowMs)
    {
        lock (_lock)
        {
            int count = 0;
            for (int i = _clickTimes.Count - 1; i >= 0; i--)
            {
                if (_clickTimes[i] < Now - windowMs) break;
                count++;
            }
            if (windowMs <= 0) return 0.0;
            return count / (windowMs / 1000.0);
        }
    }

    public void SendClick() => SentClickCount++;
    public void Sleep(int milliseconds) { }
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }

    // ---- Test driver helpers ----

    public void Advance(int ms) => Now += ms;

    public void SimulateMouseDown()
    {
        MouseDownState = true;
        lock (_lock) _clickTimes.Add(Now);
        MouseDown?.Invoke(Now);
    }

    public void SimulateMouseUp()
    {
        MouseDownState = false;
        MouseUp?.Invoke(Now);
    }

    public void SimulateClick()
    {
        SimulateMouseDown();
        SimulateMouseUp();
    }

    public void PressKey(uint vk) => _keysDown.Add(vk);
    public void ReleaseKey(uint vk) => _keysDown.Remove(vk);

    // Mirrors the real hook: key presses are only reported while capturing.
    public void RaiseKeyDown(uint vk)
    {
        if (CaptureNextKey) KeyDown?.Invoke(vk);
    }
}
