using System.Runtime.InteropServices;
using System.Text;

namespace BestAutoClicker.Core;

internal static class Win32
{
    // ---- Constants ----
    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;
    public const int HC_ACTION = 0;

    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_QUIT = 0x0012;

    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

    public const uint VK_LBUTTON = 0x01;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_SHIFT = 0x10;

    public const uint MAPVK_VK_TO_VSC = 0x00;

    /// <summary>Tag on our synthetic clicks so the hook can ignore them.</summary>
    public const ulong INJECTED_SIGNATURE = 0xACAC1234;

    // ---- Structures ----
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    // INPUT = union of mouse/keyboard/hardware; we only send mouse input, so a
    // sequential layout of type + MOUSEINPUT is byte-compatible.
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ---- P/Invoke ----
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint idThread, uint Msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetKeyNameText(uint lParam, [Out] StringBuilder lpString, int nMaxCount);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    // ---- Helpers ----
    /// <summary>
    /// Sends one synthetic left-click (down + up) at the current cursor position.
    /// The events carry <see cref="INJECTED_SIGNATURE"/> in dwExtraInfo so the
    /// low-level hooks can tell them apart from real user input.
    /// </summary>
    public static void SendSyntheticClick()
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        inputs[0].mi.dwExtraInfo = (UIntPtr)INJECTED_SIGNATURE;
        inputs[1].type = INPUT_MOUSE;
        inputs[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
        inputs[1].mi.dwExtraInfo = (UIntPtr)INJECTED_SIGNATURE;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public static bool IsRealKeyDown(uint vk)
    {
        if (vk == 0) return true;
        return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
    }

    /// <summary>
    /// Human-readable key name for a virtual key code, e.g. "SHIFT", "F5".
    /// </summary>
    public static string VkToDisplayName(uint vk)
    {
        if (vk == 0) return "(none)";
        uint scanCode = MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        uint lParam = scanCode << 16;
        var sb = new StringBuilder(128);
        if (GetKeyNameText(lParam, sb, sb.Capacity) > 0 && sb.Length > 0)
            return sb.ToString();
        return $"VK_{vk}";
    }
}
