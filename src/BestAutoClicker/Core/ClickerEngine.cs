using System.Diagnostics;
using System.Runtime.InteropServices;
using BestAutoClicker.Models;

namespace BestAutoClicker.Core;

/// <summary>
/// The auto-clicking engine. Faithful port of the original C++ engine's state
/// machine, but written for .NET:
///
///  - Low-level hooks track the user's REAL mouse/keyboard input (synthetic
///    clicks carry a signature and are ignored).
///  - Spam mode: user must be actively clicking (recent real clicks within a
///    grace window); once the clicker takes over it keeps clicking while the
///    user keeps clicking, then clicks out a short tail and stops.
///  - Hold mode: user must hold the mouse button (and optionally a key). Stops
///    as soon as the button is released — verified N consecutive times over a
///    window so a dropped hook message can never leave it clicking forever.
///  - Double-click sub-mode: a real double click (two clicks within the
///    interval) arms the clicker, which then runs while the button is held.
/// </summary>
internal sealed class ClickerEngine : IDisposable
{
    private readonly HookManager _hooks = new();
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly object _settingsLock = new();
    private readonly object _timestampsLock = new();

    private ClickerSettings _settings = new();

    private volatile bool _enabled;
    private volatile bool _requestExit;
    private volatile RunState _runState = RunState.Idle;
    private long _stateEnteredTickMs;

    private volatile bool _realMouseDown;
    private long _lastRealMouseDownTick;
    private long _lastRealMouseUpTick;
    private readonly List<long> _realClickTimestampsMs = new();

    private readonly object _spamHistoryLock = new();
    private readonly List<(long time, bool released)> _spamReleaseHistory = new();
    private readonly object _holdHistoryLock = new();
    private readonly List<(long time, bool released)> _holdReleaseHistory = new();

    private Thread? _worker;

    public event Action<RunState, bool>? StateChanged;
    public event Action<uint>? KeyCaptured;

    private long NowMs() => _sw.ElapsedMilliseconds;

    public bool IsEnabled => _enabled;
    public RunState CurrentState => _runState;
    public HookManager Hooks => _hooks;

    public ClickerEngine()
    {
        _hooks.RealMouseDown += OnRealMouseDown;
        _hooks.RealMouseUp += OnRealMouseUp;
        _hooks.RealKeyDown += vk => KeyCaptured?.Invoke(vk);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void Start()
    {
        if (_worker != null) return;
        Win32.timeBeginPeriod(1); // 1ms timer resolution for precise click timing
        _requestExit = false;
        _hooks.Start();
        _worker = new Thread(WorkerMain) { IsBackground = true, Name = "ClickerEngine" };
        _worker.Start();
    }

    public void Stop()
    {
        if (_worker == null) return;
        _requestExit = true;
        _enabled = false;
        _runState = RunState.Idle;
        try { _worker.Join(1000); } catch { /* ignore */ }
        _worker = null;
        _hooks.Stop();
        Win32.timeEndPeriod(1);
    }

    public void Enable()
    {
        _enabled = true;
    }

    public void Disable()
    {
        _enabled = false;
        SetRunState(RunState.Idle);
    }

    public void SetSettings(ClickerSettings settings)
    {
        lock (_settingsLock)
        {
            _settings = settings.Clone();
        }
    }

    private ClickerSettings Snapshot()
    {
        lock (_settingsLock) return _settings.Clone();
    }

    // ------------------------------------------------------------------
    // Hook callbacks (real user input only)
    // ------------------------------------------------------------------

    private void OnRealMouseDown(Win32.MSLLHOOKSTRUCT info)
    {
        _realMouseDown = true;
        Volatile.Write(ref _lastRealMouseDownTick, NowMs());
        lock (_timestampsLock)
        {
            _realClickTimestampsMs.Add(NowMs());
            long cutoff = NowMs() - 2000;
            while (_realClickTimestampsMs.Count > 0 && _realClickTimestampsMs[0] < cutoff)
                _realClickTimestampsMs.RemoveAt(0);
        }
    }

    private void OnRealMouseUp(Win32.MSLLHOOKSTRUCT info)
    {
        _realMouseDown = false;
        Volatile.Write(ref _lastRealMouseUpTick, NowMs());
    }

    private double ComputeRecentCps(int windowMs)
    {
        long now = NowMs();
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

    private bool IsRealMouseCurrentlyDown()
    {
        // Prefer hook-tracked state: ignores synthetic clicks, so it stays
        // "down" while the user physically holds the button even during our own
        // SendInput bursts.
        if (_realMouseDown) return true;
        // Fallback in case the hook is blocked by policy/security software.
        return (Win32.GetAsyncKeyState((int)Win32.VK_LBUTTON) & 0x8000) != 0;
    }

    private static void SendSyntheticClick()
    {
        var inputs = new Win32.INPUT[2];
        inputs[0].type = Win32.INPUT_MOUSE;
        inputs[0].mi.dwFlags = Win32.MOUSEEVENTF_LEFTDOWN;
        inputs[0].mi.dwExtraInfo = (UIntPtr)Win32.INJECTED_SIGNATURE;
        inputs[1].type = Win32.INPUT_MOUSE;
        inputs[1].mi.dwFlags = Win32.MOUSEEVENTF_LEFTUP;
        inputs[1].mi.dwExtraInfo = (UIntPtr)Win32.INJECTED_SIGNATURE;
        Win32.SendInput(2, inputs, Marshal.SizeOf<Win32.INPUT>());
    }

    // ------------------------------------------------------------------
    // State helpers
    // ------------------------------------------------------------------

    private void SetRunState(RunState state)
    {
        if (_runState != state)
        {
            _runState = state;
            Volatile.Write(ref _stateEnteredTickMs, NowMs());
        }
        StateChanged?.Invoke(state, _enabled);
    }

    /// <summary>
    /// Samples "is the user still activating the clicker" and, once N
    /// consecutive samples within the window say "released", reports true so
    /// the caller can idle. This is the anti-runaway safeguard.
    /// </summary>
    private bool SampleAndCheckReleased(List<(long time, bool released)> history, object historyLock, bool doingHold, ClickerSettings s)
    {
        bool stillActive;
        if (doingHold)
        {
            stillActive = IsRealMouseCurrentlyDown();
        }
        else
        {
            // Spam: stay active for a grace window after the user's last real
            // click (the user stops clicking once the clicker takes over, so a
            // CPS-rate check would stop us almost instantly — that was the bug
            // in the original engine this port fixes).
            long graceMs = s.TriggerSampleWindowMs > 0 ? s.TriggerSampleWindowMs : 400;
            if (graceMs < 100) graceMs = 100;
            stillActive = (NowMs() - Volatile.Read(ref _lastRealMouseDownTick)) < graceMs;
        }

        long now = NowMs();
        lock (historyLock)
        {
            history.Add((now, !stillActive));
            int windowMs = s.StopCheckWindowMs > 0 ? s.StopCheckWindowMs : 150;
            long cutoff = now - windowMs;
            while (history.Count > 0 && history[0].time < cutoff)
                history.RemoveAt(0);

            int required = s.StopCheckCount > 0 ? s.StopCheckCount : 3;
            if (history.Count < required) return false;
            for (int i = history.Count - 1; i >= 0 && required > 0; i--, required--)
            {
                if (!history[i].released) return false;
            }
            return true;
        }
    }

    private void ResetReleaseHistory(List<(long time, bool released)> history, object historyLock)
    {
        lock (historyLock) history.Clear();
    }

    // ------------------------------------------------------------------
    // Worker / state machines
    // ------------------------------------------------------------------

    private void WorkerMain()
    {
        while (!_requestExit)
        {
            if (!_enabled)
            {
                Thread.Sleep(15);
                continue;
            }
            SetRunState(RunState.Idle);
            ClickerSettings s = Snapshot();
            if (s.Mode == ClickMode.Spam) RunSpamModeLoop();
            else RunHoldModeLoop();
        }
    }

    private void RunSpamModeLoop()
    {
        long lastClickTime = 0;
        while (_enabled && !_requestExit)
        {
            ClickerSettings s = Snapshot();
            if (s.Mode != ClickMode.Spam) return;

            switch (_runState)
            {
                case RunState.Idle:
                {
                    double cps = ComputeRecentCps(s.TriggerSampleWindowMs);
                    if (s.SpamTriggerCps > 0.0 && cps >= s.SpamTriggerCps)
                    {
                        SetRunState(RunState.ArmedWaitingDelay);
                    }
                    break;
                }
                case RunState.ArmedWaitingDelay:
                {
                    long elapsed = NowMs() - Volatile.Read(ref _stateEnteredTickMs);
                    double cps = ComputeRecentCps(s.TriggerSampleWindowMs);
                    if (cps < s.SpamTriggerCps)
                    {
                        SetRunState(RunState.Idle);
                    }
                    else if (elapsed >= s.SpamDelayMs)
                    {
                        lastClickTime = 0;
                        ResetReleaseHistory(_spamReleaseHistory, _spamHistoryLock);
                        SetRunState(RunState.Active);
                    }
                    break;
                }
                case RunState.Active:
                {
                    TryClick(ref lastClickTime, s.SpamAutoClickCps, s.MinClickIntervalMs);
                    bool released = SampleAndCheckReleased(_spamReleaseHistory, _spamHistoryLock, false, s);
                    if (released)
                    {
                        SetRunState(RunState.Idle);
                    }
                    break;
                }
            }

            SleepTick(s.ClickerThreadTickMs);
        }
    }

    private void RunHoldModeLoop()
    {
        long lastClickTime = 0;
        bool prevMouseDown = false;
        long firstClickDownTime = 0;
        bool waitingSecondArmed = false;

        while (_enabled && !_requestExit)
        {
            ClickerSettings s = Snapshot();
            if (s.Mode != ClickMode.Hold) return;

            bool mouseDownNow = IsRealMouseCurrentlyDown();

            switch (_runState)
            {
                case RunState.Idle:
                {
                    if (s.HoldSubMode == HoldSubMode.Immediate)
                    {
                        if (mouseDownNow && !prevMouseDown)
                            SetRunState(RunState.ArmedWaitingDelay);
                    }
                    else if (s.HoldSubMode == HoldSubMode.WaitForKey)
                    {
                        if (mouseDownNow && !prevMouseDown && Win32.IsRealKeyDown(s.WaitForKeyVk))
                            SetRunState(RunState.ArmedWaitingDelay);
                    }
                    else if (s.HoldSubMode == HoldSubMode.DoubleClick)
                    {
                        if (mouseDownNow && !prevMouseDown && !waitingSecondArmed)
                            firstClickDownTime = NowMs();
                        if (!mouseDownNow && prevMouseDown && firstClickDownTime != 0 && !waitingSecondArmed)
                        {
                            waitingSecondArmed = true;
                            SetRunState(RunState.WaitingSecondClick);
                        }
                    }
                    break;
                }
                case RunState.WaitingSecondClick:
                {
                    long elapsedSinceFirstDown = NowMs() - firstClickDownTime;
                    if (elapsedSinceFirstDown > s.DoubleClickIntervalMs && !mouseDownNow)
                    {
                        waitingSecondArmed = false;
                        firstClickDownTime = 0;
                        SetRunState(RunState.Idle);
                    }
                    else if (mouseDownNow && !prevMouseDown)
                    {
                        long secondDownTime = NowMs();
                        if (secondDownTime - firstClickDownTime <= s.DoubleClickIntervalMs)
                        {
                            waitingSecondArmed = false;
                            firstClickDownTime = 0;
                            SetRunState(RunState.ArmedWaitingDelay);
                        }
                        else
                        {
                            waitingSecondArmed = false;
                            firstClickDownTime = 0;
                            SetRunState(RunState.Idle);
                        }
                    }
                    break;
                }
                case RunState.ArmedWaitingDelay:
                {
                    long elapsed = NowMs() - Volatile.Read(ref _stateEnteredTickMs);
                    bool keyStillOk = (s.HoldSubMode != HoldSubMode.WaitForKey) || Win32.IsRealKeyDown(s.WaitForKeyVk);
                    if (!mouseDownNow || !keyStillOk)
                    {
                        SetRunState(RunState.Idle);
                    }
                    else if (elapsed >= s.HoldDelayMs)
                    {
                        lastClickTime = 0;
                        ResetReleaseHistory(_holdReleaseHistory, _holdHistoryLock);
                        SetRunState(RunState.Active);
                    }
                    break;
                }
                case RunState.Active:
                {
                    TryClick(ref lastClickTime, s.HoldAutoClickCps, s.MinClickIntervalMs);
                    bool released = SampleAndCheckReleased(_holdReleaseHistory, _holdHistoryLock, true, s);
                    if (released)
                    {
                        SetRunState(RunState.Idle);
                    }
                    break;
                }
            }

            prevMouseDown = mouseDownNow;
            SleepTick(s.ClickerThreadTickMs);
        }
    }

    private void TryClick(ref long lastClickTime, double cps, int minIntervalMs)
    {
        if (cps <= 0.0) return;
        long intervalMs = (long)Math.Round(1000.0 / cps, MidpointRounding.AwayFromZero);
        if (intervalMs < minIntervalMs) intervalMs = minIntervalMs;
        long now = NowMs();
        if (now - lastClickTime >= intervalMs)
        {
            SendSyntheticClick();
            lastClickTime = now;
        }
    }

    private void SleepTick(int tickMs)
    {
        int t = tickMs > 0 ? tickMs : 2;
        if (t <= 1)
        {
            Thread.SpinWait(1);
            return;
        }
        Thread.Sleep(t);
    }

    public void Dispose()
    {
        Stop();
        _hooks.Dispose();
    }
}
