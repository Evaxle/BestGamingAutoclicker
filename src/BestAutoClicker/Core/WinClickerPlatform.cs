using System.Diagnostics;

namespace BestAutoClicker.Core;

/// <summary>
/// Production <see cref="IClickerPlatform"/> backed by Win32 low-level hooks and
/// SendInput. Synthetic clicks (tagged with <see cref="Win32.INJECTED_SIGNATURE"/>)
/// are ignored by the hook so only genuine user input reaches the engine.
/// </summary>
internal sealed class WinClickerPlatform : IClickerPlatform
{
    private readonly HookManager _hooks;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly object _timestampsLock = new();
    private readonly List<long> _realClickTimestampsMs = new();
    private volatile bool _realMouseDown;

    public WinClickerPlatform()
    {
        _hooks = new HookManager();
        _hooks.RealMouseDown += _ =>
        {
            _realMouseDown = true;
            RecordRealClick();
            MouseDown?.Invoke(NowMs);
        };
        _hooks.RealMouseUp += _ =>
        {
            _realMouseDown = false;
            MouseUp?.Invoke(NowMs);
        };
        _hooks.RealKeyDown += vk => KeyDown?.Invoke(vk);
    }

    public event Action<long>? MouseDown;
    public event Action<long>? MouseUp;
    public event Action<uint>? KeyDown;

    public bool CaptureNextKey
    {
        get => _hooks.CaptureNextKey;
        set => _hooks.CaptureNextKey = value;
    }

    public long NowMs => _sw.ElapsedMilliseconds;

    public bool IsMouseDown
    {
        get
        {
            // Prefer the hook-tracked state: it ignores synthetic clicks, so it
            // stays "down" while the user physically holds the button even during
            // our own SendInput bursts.
            if (_realMouseDown) return true;
            // Fallback if the hook is blocked by policy/security software.
            return (Win32.GetAsyncKeyState((int)Win32.VK_LBUTTON) & 0x8000) != 0;
        }
    }

    public bool IsKeyDown(uint vk) => Win32.IsRealKeyDown(vk);

    public double RecentCps(int windowMs)
    {
        long now = NowMs;
        long cutoff = now - windowMs;
        int count = 0;
        lock (_timestampsLock)
        {
            for (int i = _realClickTimestampsMs.Count - 1; i >= 0; i--)
            {
                if (_realClickTimestampsMs[i] < cutoff) break;
                count++;
            }
        }
        if (windowMs <= 0) return 0.0;
        return count / ((double)windowMs / 1000.0);
    }

    public void SendClick() => Win32.SendSyntheticClick();

    public void Sleep(int milliseconds)
    {
        int t = milliseconds > 0 ? milliseconds : 2;
        if (t <= 1)
        {
            Thread.SpinWait(1);
            return;
        }
        Thread.Sleep(t);
    }

    public void Start()
    {
        Win32.timeBeginPeriod(1); // 1ms timer resolution for precise click timing
        _hooks.Start();
    }

    public void Stop()
    {
        _hooks.Stop();
        Win32.timeEndPeriod(1);
    }

    private void RecordRealClick()
    {
        lock (_timestampsLock)
        {
            _realClickTimestampsMs.Add(NowMs);
            long cutoff = NowMs - 2000;
            while (_realClickTimestampsMs.Count > 0 && _realClickTimestampsMs[0] < cutoff)
                _realClickTimestampsMs.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        _hooks.Dispose();
    }
}
