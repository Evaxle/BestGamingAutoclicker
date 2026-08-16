using BestAutoClicker.Models;

namespace BestAutoClicker.Core;

/// <summary>
/// The auto-clicking state machine. Faithful port of the original C++ engine:
///
///  - Spam mode: user must be actively clicking (recent real clicks within a
///    grace window). Once the clicker takes over it keeps clicking while the
///    user keeps clicking, then clicks out a short tail and stops.
///  - Hold mode: user must hold the mouse button (and optionally a key). Stops
///    as soon as the button is released — verified N consecutive times over a
///    window so a dropped hook message can never leave it clicking forever.
///  - Double-click sub-mode: a real double click (two clicks within the
///    interval) arms the clicker, which then runs while the button is held.
///
/// The state machine runs on a background worker thread via
/// <see cref="RunSpamLoop"/> / <see cref="RunHoldLoop"/>. The per-tick methods
/// <see cref="TickSpam"/> / <see cref="TickHold"/> are internal so tests can
/// drive them deterministically against a fake <see cref="IClickerPlatform"/>.
/// </summary>
internal sealed class ClickerEngine : IDisposable
{
    private readonly IClickerPlatform _platform;
    private readonly object _settingsLock = new();
    private readonly object _spamHistoryLock = new();
    private readonly object _holdHistoryLock = new();
    private readonly List<(long time, bool released)> _spamReleaseHistory = new();
    private readonly List<(long time, bool released)> _holdReleaseHistory = new();

    private ClickerSettings _settings = new();

    private volatile bool _enabled;
    private volatile bool _requestExit;
    private volatile RunState _runState = RunState.Idle;
    private long _stateEnteredTickMs;
    private long _lastRealMouseDownTick;
    private long _lastClickTime;
    private bool _prevMouseDown;
    private long _firstClickDownTime;
    private bool _waitingSecondArmed;

    private Thread? _worker;

    public event Action<RunState, bool>? StateChanged;
    public event Action<uint>? KeyCaptured;

    private long NowMs => _platform.NowMs;

    public bool IsEnabled => _enabled;
    public RunState CurrentState => _runState;

    /// <summary>Arming/arming-mode trigger key capture flag (delegates to the platform).</summary>
    public bool CaptureNextKey
    {
        get => _platform.CaptureNextKey;
        set => _platform.CaptureNextKey = value;
    }

    public ClickerEngine() : this(new WinClickerPlatform())
    {
    }

    internal ClickerEngine(IClickerPlatform platform)
    {
        _platform = platform;
        _platform.MouseDown += ts => Volatile.Write(ref _lastRealMouseDownTick, ts);
        _platform.KeyDown += vk => KeyCaptured?.Invoke(vk);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void Start()
    {
        if (_worker != null) return;
        _requestExit = false;
        _platform.Start();
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
        _platform.Stop();
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

    internal ClickerSettings CurrentSettings()
    {
        lock (_settingsLock) return _settings.Clone();
    }

    private ClickerSettings Snapshot()
    {
        lock (_settingsLock) return _settings.Clone();
    }

    // ------------------------------------------------------------------
    // State helpers
    // ------------------------------------------------------------------

    private void SetRunState(RunState state)
    {
        if (_runState != state)
        {
            _runState = state;
            Volatile.Write(ref _stateEnteredTickMs, NowMs);
        }
        StateChanged?.Invoke(state, _enabled);
    }

    /// <summary>
    /// Samples "is the user still activating the clicker" and, once N
    /// consecutive samples within the window say "released", reports true so
    /// the caller can idle. This is the anti-runaway safeguard: the clicker
    /// can never keep going once the user lets go.
    /// </summary>
    private bool SampleAndCheckReleased(List<(long time, bool released)> history, object historyLock, bool doingHold, ClickerSettings s)
    {
        bool stillActive;
        if (doingHold)
        {
            stillActive = _platform.IsMouseDown;
            // Wait-For-Key mode: the user must keep BOTH the mouse and the key
            // held — releasing either one counts as "not activating".
            if (stillActive && s.HoldSubMode == HoldSubMode.WaitForKey && !_platform.IsKeyDown(s.WaitForKeyVk))
                stillActive = false;
        }
        else
        {
            // Spam: stay active for a grace window after the user's last real
            // click (the user stops clicking once the clicker takes over, so a
            // CPS-rate check would stop us almost instantly).
            long graceMs = s.TriggerSampleWindowMs > 0 ? s.TriggerSampleWindowMs : 400;
            if (graceMs < 100) graceMs = 100;
            stillActive = (NowMs - Volatile.Read(ref _lastRealMouseDownTick)) < graceMs;
        }

        long now = NowMs;
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

    /// <summary>
    /// Resets per-loop edge-tracking state. Called when (re)entering a mode loop.
    /// </summary>
    internal void ResetTickState()
    {
        _lastClickTime = 0;
        _prevMouseDown = false;
        _firstClickDownTime = 0;
        _waitingSecondArmed = false;
        ResetReleaseHistory(_spamReleaseHistory, _spamHistoryLock);
        ResetReleaseHistory(_holdReleaseHistory, _holdHistoryLock);
    }

    private void TryClick(double cps, int minIntervalMs)
    {
        if (cps <= 0.0) return;
        long intervalMs = (long)Math.Round(1000.0 / cps, MidpointRounding.AwayFromZero);
        if (intervalMs < minIntervalMs) intervalMs = minIntervalMs;
        long now = NowMs;
        if (now - _lastClickTime >= intervalMs)
        {
            _platform.SendClick();
            _lastClickTime = now;
        }
    }

    // ------------------------------------------------------------------
    // Per-tick state machine (internal for deterministic tests)
    // ------------------------------------------------------------------

    internal void TickSpam(ClickerSettings s)
    {
        if (!_enabled || _requestExit) return;

        switch (_runState)
        {
            case RunState.Idle:
            {
                double cps = _platform.RecentCps(s.TriggerSampleWindowMs);
                if (s.SpamTriggerCps > 0.0 && cps >= s.SpamTriggerCps)
                {
                    SetRunState(RunState.ArmedWaitingDelay);
                }
                break;
            }
            case RunState.ArmedWaitingDelay:
            {
                long elapsed = NowMs - Volatile.Read(ref _stateEnteredTickMs);
                double cps = _platform.RecentCps(s.TriggerSampleWindowMs);
                if (cps < s.SpamTriggerCps)
                {
                    SetRunState(RunState.Idle);
                }
                else if (elapsed >= s.SpamDelayMs)
                {
                    _lastClickTime = 0;
                    ResetReleaseHistory(_spamReleaseHistory, _spamHistoryLock);
                    SetRunState(RunState.Active);
                }
                break;
            }
            case RunState.Active:
            {
                TryClick(s.SpamAutoClickCps, s.MinClickIntervalMs);
                bool released = SampleAndCheckReleased(_spamReleaseHistory, _spamHistoryLock, false, s);
                if (released)
                {
                    SetRunState(RunState.Idle);
                }
                break;
            }
        }
    }

    internal void TickHold(ClickerSettings s)
    {
        if (!_enabled || _requestExit) return;

        bool mouseDownNow = _platform.IsMouseDown;

        switch (_runState)
        {
            case RunState.Idle:
            {
                if (s.HoldSubMode == HoldSubMode.Immediate)
                {
                    if (mouseDownNow && !_prevMouseDown)
                        SetRunState(RunState.ArmedWaitingDelay);
                }
                else if (s.HoldSubMode == HoldSubMode.WaitForKey)
                {
                    if (mouseDownNow && !_prevMouseDown && _platform.IsKeyDown(s.WaitForKeyVk))
                        SetRunState(RunState.ArmedWaitingDelay);
                }
                else if (s.HoldSubMode == HoldSubMode.DoubleClick)
                {
                    if (mouseDownNow && !_prevMouseDown && !_waitingSecondArmed)
                        _firstClickDownTime = NowMs;
                    if (!mouseDownNow && _prevMouseDown && _firstClickDownTime != 0 && !_waitingSecondArmed)
                    {
                        _waitingSecondArmed = true;
                        SetRunState(RunState.WaitingSecondClick);
                    }
                }
                break;
            }
            case RunState.WaitingSecondClick:
            {
                long elapsedSinceFirstDown = NowMs - _firstClickDownTime;
                if (elapsedSinceFirstDown > s.DoubleClickIntervalMs && !mouseDownNow)
                {
                    _waitingSecondArmed = false;
                    _firstClickDownTime = 0;
                    SetRunState(RunState.Idle);
                }
                else if (mouseDownNow && !_prevMouseDown)
                {
                    long secondDownTime = NowMs;
                    if (secondDownTime - _firstClickDownTime <= s.DoubleClickIntervalMs)
                    {
                        _waitingSecondArmed = false;
                        _firstClickDownTime = 0;
                        SetRunState(RunState.ArmedWaitingDelay);
                    }
                    else
                    {
                        _waitingSecondArmed = false;
                        _firstClickDownTime = 0;
                        SetRunState(RunState.Idle);
                    }
                }
                break;
            }
            case RunState.ArmedWaitingDelay:
            {
                long elapsed = NowMs - Volatile.Read(ref _stateEnteredTickMs);
                bool keyStillOk = (s.HoldSubMode != HoldSubMode.WaitForKey) || _platform.IsKeyDown(s.WaitForKeyVk);
                if (!mouseDownNow || !keyStillOk)
                {
                    SetRunState(RunState.Idle);
                }
                else if (elapsed >= s.HoldDelayMs)
                {
                    _lastClickTime = 0;
                    ResetReleaseHistory(_holdReleaseHistory, _holdHistoryLock);
                    SetRunState(RunState.Active);
                }
                break;
            }
            case RunState.Active:
            {
                TryClick(s.HoldAutoClickCps, s.MinClickIntervalMs);
                bool released = SampleAndCheckReleased(_holdReleaseHistory, _holdHistoryLock, true, s);
                if (released)
                {
                    SetRunState(RunState.Idle);
                }
                break;
            }
        }

        _prevMouseDown = mouseDownNow;
    }

    // ------------------------------------------------------------------
    // Worker / loops
    // ------------------------------------------------------------------

    private void WorkerMain()
    {
        while (!_requestExit)
        {
            if (!_enabled)
            {
                _platform.Sleep(15);
                continue;
            }
            SetRunState(RunState.Idle);
            ClickerSettings s = Snapshot();
            if (s.Mode == ClickMode.Spam) RunSpamLoop();
            else RunHoldLoop();
        }
    }

    private void RunSpamLoop()
    {
        ResetTickState();
        while (_enabled && !_requestExit)
        {
            ClickerSettings s = Snapshot();
            if (s.Mode != ClickMode.Spam) return;
            TickSpam(s);
            _platform.Sleep(s.ClickerThreadTickMs);
        }
    }

    private void RunHoldLoop()
    {
        ResetTickState();
        while (_enabled && !_requestExit)
        {
            ClickerSettings s = Snapshot();
            if (s.Mode != ClickMode.Hold) return;
            TickHold(s);
            _platform.Sleep(s.ClickerThreadTickMs);
        }
    }

    public void Dispose()
    {
        Stop();
        _platform.Dispose();
    }
}
