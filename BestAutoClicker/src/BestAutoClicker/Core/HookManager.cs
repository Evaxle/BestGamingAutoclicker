using System.Runtime.InteropServices;

namespace BestAutoClicker.Core;

/// <summary>
/// Installs global low-level mouse + keyboard hooks (WH_MOUSE_LL / WH_KEYBOARD_LL)
/// on a dedicated thread that pumps messages. Synthetic clicks (tagged with
/// <see cref="Win32.INJECTED_SIGNATURE"/>) are filtered out so the rest of the app
/// only ever sees genuine user input.
/// </summary>
internal sealed class HookManager : IDisposable
{
    private Thread? _thread;
    private volatile bool _running;

    private Win32.HookProc? _mouseProc;     // kept alive to prevent GC
    private Win32.HookProc? _keyboardProc;  // kept alive to prevent GC
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private uint _threadId;

    private volatile bool _captureNextKey;

    public bool CaptureNextKey
    {
        get => _captureNextKey;
        set => _captureNextKey = value;
    }

    public event Action<Win32.MSLLHOOKSTRUCT>? RealMouseDown;
    public event Action<Win32.MSLLHOOKSTRUCT>? RealMouseUp;
    public event Action<uint>? RealKeyDown;

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "InputHookThread"
        };
        _thread.Start();
    }

    private void ThreadMain()
    {
        _threadId = Win32.GetCurrentThreadId();

        _mouseProc = MouseHookProc;
        _keyboardProc = KeyboardHookProc;

        // Low-level hooks run the callback on the installing thread (this one),
        // so a NULL hMod is valid even though the delegate lives in the process.
        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, IntPtr.Zero, 0);

        while (_running)
        {
            if (Win32.GetMessage(out Win32.MSG msg, IntPtr.Zero, 0, 0) <= 0)
            {
                if (msg.message == Win32.WM_QUIT) break;
                continue;
            }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = IntPtr.Zero;
        _keyboardHook = IntPtr.Zero;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == Win32.HC_ACTION)
        {
            var info = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            bool synthetic = info.dwExtraInfo.ToUInt64() == Win32.INJECTED_SIGNATURE;
            if (!synthetic)
            {
                uint msg = (uint)wParam.ToInt64();
                if (msg == Win32.WM_LBUTTONDOWN) RealMouseDown?.Invoke(info);
                else if (msg == Win32.WM_LBUTTONUP) RealMouseUp?.Invoke(info);
            }
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == Win32.HC_ACTION)
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            bool synthetic = info.dwExtraInfo.ToUInt64() == Win32.INJECTED_SIGNATURE;
            if (!synthetic && CaptureNextKey)
            {
                uint msg = (uint)wParam.ToInt64();
                if (msg == Win32.WM_KEYDOWN && info.vkCode != Win32.VK_SHIFT)
                {
                    RealKeyDown?.Invoke(info.vkCode);
                }
            }
        }
        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_threadId != 0)
        {
            Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }
        try { _thread?.Join(1000); } catch { /* ignore */ }
        _thread = null;
    }
}
