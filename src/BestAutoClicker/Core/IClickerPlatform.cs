namespace BestAutoClicker.Core;

/// <summary>
/// Abstracts the OS-level input plumbing (hooks, SendInput, clock, sleeping)
/// from the clicker state machine so the engine can be driven deterministically
/// in tests. The production implementation uses Win32 low-level hooks and
/// SendInput; tests substitute a scripted fake.
/// </summary>
public interface IClickerPlatform : IDisposable
{
    /// <summary>Raised on a REAL (non-synthetic) left mouse button down, with the tick time in ms.</summary>
    event Action<long>? MouseDown;

    /// <summary>Raised on a REAL (non-synthetic) left mouse button up, with the tick time in ms.</summary>
    event Action<long>? MouseUp;

    /// <summary>Raised on a real key down while <see cref="CaptureNextKey"/> is set.</summary>
    event Action<uint>? KeyDown;

    /// <summary>Whether the next real key press should be reported via <see cref="KeyDown"/>.</summary>
    bool CaptureNextKey { get; set; }

    /// <summary>Monotonic millisecond clock used for all timing decisions.</summary>
    long NowMs { get; }

    /// <summary>Whether the user's physical left mouse button is currently held.</summary>
    bool IsMouseDown { get; }

    /// <summary>Whether the given virtual key code is currently held down.</summary>
    bool IsKeyDown(uint vk);

    /// <summary>Recent real-click rate (clicks/second) over the last <paramref name="windowMs"/>.</summary>
    double RecentCps(int windowMs);

    /// <summary>Sends one synthetic click (down+up) at the current cursor position.</summary>
    void SendClick();

    /// <summary>Yields the loop for the given number of milliseconds.</summary>
    void Sleep(int milliseconds);

    /// <summary>Starts background input capture (hooks) and high-resolution timing.</summary>
    void Start();

    /// <summary>Stops background input capture and restores normal timing.</summary>
    void Stop();
}
