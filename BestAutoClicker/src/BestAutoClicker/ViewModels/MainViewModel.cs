using System.Collections.ObjectModel;
using Avalonia.Threading;
using BestAutoClicker.Core;
using BestAutoClicker.Models;
using BestAutoClicker.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BestAutoClicker.ViewModels;

public sealed class ModeOption
{
    public required string Label { get; init; }
    public required ClickMode Value { get; init; }
    public required string Description { get; init; }
}

public sealed class SubModeOption
{
    public required string Label { get; init; }
    public required HoldSubMode Value { get; init; }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ClickerEngine _engine;
    private readonly ProfileService _profiles;

    private bool _syncingProfile;
    private bool _capturingKey;
    private string? _lastProfileName;

    public MainViewModel()
    {
        _engine = new ClickerEngine();
        _profiles = new ProfileService();
        _engine.StateChanged += OnEngineStateChanged;

        ModeOptions = new[]
        {
            new ModeOption { Label = "Spam", Value = ClickMode.Spam,
                Description = "Click automatically while you click rapidly" },
            new ModeOption { Label = "Hold", Value = ClickMode.Hold,
                Description = "Click automatically while you hold the mouse button" }
        };
        SubModeOptions = new[]
        {
            new SubModeOption { Label = "Immediate", Value = HoldSubMode.Immediate },
            new SubModeOption { Label = "Double Click", Value = HoldSubMode.DoubleClick },
            new SubModeOption { Label = "Wait For Key", Value = HoldSubMode.WaitForKey }
        };
        Profiles = new ObservableCollection<string>();

        _engine.Start();

        var profiles = _profiles.ListProfiles();
        foreach (var p in profiles) Profiles.Add(p);

        string? last = _profiles.LoadLastProfile();
        if (string.IsNullOrEmpty(last) || !_profiles.Exists(last)) last = "Default";

        _syncingProfile = true;
        CurrentProfileName = last;
        _syncingProfile = false;

        LoadSettings(last);
        _profiles.SaveLastProfile(last);
    }

    // ------------------------------------------------------------------
    // Mode selection
    // ------------------------------------------------------------------

    public IReadOnlyList<ModeOption> ModeOptions { get; }
    public IReadOnlyList<SubModeOption> SubModeOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpamMode))]
    [NotifyPropertyChangedFor(nameof(IsHoldMode))]
    private ModeOption? _selectedMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImmediate))]
    [NotifyPropertyChangedFor(nameof(IsDoubleClick))]
    [NotifyPropertyChangedFor(nameof(IsWaitForKey))]
    [NotifyPropertyChangedFor(nameof(ShowDoubleClickInterval))]
    [NotifyPropertyChangedFor(nameof(ShowTriggerKey))]
    private SubModeOption? _selectedSubMode;

    [ObservableProperty]
    private ClickMode _mode;

    [ObservableProperty]
    private HoldSubMode _holdSubMode;

    public bool IsSpamMode => Mode == ClickMode.Spam;
    public bool IsHoldMode => Mode == ClickMode.Hold;
    public bool IsImmediate => HoldSubMode == HoldSubMode.Immediate;
    public bool IsDoubleClick => HoldSubMode == HoldSubMode.DoubleClick;
    public bool IsWaitForKey => HoldSubMode == HoldSubMode.WaitForKey;
    public bool ShowDoubleClickInterval => IsDoubleClick;
    public bool ShowTriggerKey => IsWaitForKey;

    partial void OnSelectedModeChanged(ModeOption? value)
    {
        if (value != null)
        {
            Mode = value.Value;
            OnPropertyChanged(nameof(IsSpamMode));
            OnPropertyChanged(nameof(IsHoldMode));
            StopIfRunning();
            MarkDirty();
            PushToEngine();
        }
    }

    partial void OnSelectedSubModeChanged(SubModeOption? value)
    {
        if (value != null)
        {
            HoldSubMode = value.Value;
            OnPropertyChanged(nameof(IsImmediate));
            OnPropertyChanged(nameof(IsDoubleClick));
            OnPropertyChanged(nameof(IsWaitForKey));
            OnPropertyChanged(nameof(ShowDoubleClickInterval));
            OnPropertyChanged(nameof(ShowTriggerKey));
            StopIfRunning();
            MarkDirty();
            PushToEngine();
        }
    }

    // ------------------------------------------------------------------
    // Spam settings
    // ------------------------------------------------------------------

    [ObservableProperty] private double _spamAutoCps;
    [ObservableProperty] private double _spamTriggerCps;
    [ObservableProperty] private double _spamDelayMs;

    partial void OnSpamAutoCpsChanged(double value) => OnTuningChanged();
    partial void OnSpamTriggerCpsChanged(double value) => OnTuningChanged();
    partial void OnSpamDelayMsChanged(double value) => OnTuningChanged();

    // ------------------------------------------------------------------
    // Hold settings
    // ------------------------------------------------------------------

    [ObservableProperty] private double _holdAutoCps;
    [ObservableProperty] private double _holdDelayMs;
    [ObservableProperty] private double _doubleClickIntervalMs;
    [ObservableProperty] private uint _triggerKeyVk;
    [ObservableProperty] private string _triggerKeyName = "Shift";

    partial void OnHoldAutoCpsChanged(double value) => OnTuningChanged();
    partial void OnHoldDelayMsChanged(double value) => OnTuningChanged();
    partial void OnDoubleClickIntervalMsChanged(double value) => OnTuningChanged();

    // ------------------------------------------------------------------
    // Advanced settings
    // ------------------------------------------------------------------

    [ObservableProperty] private double _stopCheckCount;
    [ObservableProperty] private double _stopCheckWindowMs;
    [ObservableProperty] private double _triggerSampleWindowMs;
    [ObservableProperty] private double _tickMs;
    [ObservableProperty] private double _minIntervalMs;

    partial void OnStopCheckCountChanged(double value) => OnTuningChanged();
    partial void OnStopCheckWindowMsChanged(double value) => OnTuningChanged();
    partial void OnTriggerSampleWindowMsChanged(double value) => OnTuningChanged();
    partial void OnTickMsChanged(double value) => OnTuningChanged();
    partial void OnMinIntervalMsChanged(double value) => OnTuningChanged();

    // ------------------------------------------------------------------
    // Runtime state
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartStopText))]
    [NotifyPropertyChangedFor(nameof(IsRunningText))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(IsArmed))]
    [NotifyPropertyChangedFor(nameof(IsClicking))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private RunState _runState = RunState.Idle;

    public bool IsIdle => IsRunning && RunState == RunState.Idle;
    public bool IsArmed => IsRunning && (RunState == RunState.ArmedWaitingDelay || RunState == RunState.WaitingSecondClick);
    public bool IsClicking => IsRunning && RunState == RunState.Active;

    public string StartStopText => IsRunning ? "STOP" : "START";
    public string IsRunningText => IsRunning ? "Running" : "Stopped";

    public string StatusText => !IsRunning
        ? "Clicker disabled"
        : RunState switch
        {
            RunState.ArmedWaitingDelay => "Enabled · arming…",
            RunState.WaitingSecondClick => "Enabled · waiting for 2nd click…",
            RunState.Active => "CLICKING",
            _ => "Enabled · waiting for activation"
        };

    // ------------------------------------------------------------------
    // UI state
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedToggleText))]
    private bool _isAdvancedOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopmostText))]
    private bool _isTopmost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCapturingKey))]
    private bool _isCapturingKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = "";

    public bool IsNotCapturingKey => !IsCapturingKey;
    public bool HasNotice => !string.IsNullOrEmpty(Notice);
    public string AdvancedToggleText => IsAdvancedOpen ? "Hide advanced settings" : "Show advanced settings";
    public string TopmostText => IsTopmost ? "Unpin" : "Pin";

    // ------------------------------------------------------------------
    // Profiles
    // ------------------------------------------------------------------

    public ObservableCollection<string> Profiles { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileSaveText))]
    private string _currentProfileName = "Default";

    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private bool _hasUnsavedChanges;

    public string ProfileSaveText => HasUnsavedChanges ? "Save *" : "Save";

    partial void OnCurrentProfileNameChanged(string value)
    {
        if (_syncingProfile) return;
        MarkDirty();
        if (Profiles.Contains(value)) SelectedProfile = value;
    }

    partial void OnSelectedProfileChanged(string? value)
    {
        if (_syncingProfile || value == null || value == CurrentProfileName) return;
        TryLoadProfile(value);
    }

    // ------------------------------------------------------------------
    // Engine bridging
    // ------------------------------------------------------------------

    private void OnEngineStateChanged(RunState state, bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RunState = state;
            IsRunning = enabled;
        });
    }

    private ClickerSettings BuildSettings()
    {
        return new ClickerSettings
        {
            ProfileName = CurrentProfileName,
            Mode = Mode,
            SpamAutoClickCps = SpamAutoCps,
            SpamTriggerCps = SpamTriggerCps,
            SpamDelayMs = (int)Math.Round(SpamDelayMs),
            HoldAutoClickCps = HoldAutoCps,
            HoldDelayMs = (int)Math.Round(HoldDelayMs),
            HoldSubMode = HoldSubMode,
            DoubleClickIntervalMs = (int)Math.Round(DoubleClickIntervalMs),
            WaitForKeyVk = TriggerKeyVk,
            StopCheckCount = (int)Math.Round(StopCheckCount),
            StopCheckWindowMs = (int)Math.Round(StopCheckWindowMs),
            TriggerSampleWindowMs = (int)Math.Round(TriggerSampleWindowMs),
            ClickerThreadTickMs = (int)Math.Round(TickMs),
            MinClickIntervalMs = (int)Math.Round(MinIntervalMs)
        };
    }

    private void PushToEngine()
    {
        _engine.SetSettings(BuildSettings());
    }

    private void LoadSettings(string profileName)
    {
        if (!_profiles.TryLoad(profileName, out var s)) s = new ClickerSettings { ProfileName = profileName };

        _syncingProfile = true;
        CurrentProfileName = s.ProfileName;
        _syncingProfile = false;
        SelectedProfile = s.ProfileName;

        Mode = s.Mode;
        SelectedMode = ModeOptions.FirstOrDefault(o => o.Value == s.Mode) ?? ModeOptions[0];
        SpamAutoCps = s.SpamAutoClickCps;
        SpamTriggerCps = s.SpamTriggerCps;
        SpamDelayMs = s.SpamDelayMs;
        HoldAutoCps = s.HoldAutoClickCps;
        HoldDelayMs = s.HoldDelayMs;
        HoldSubMode = s.HoldSubMode;
        SelectedSubMode = SubModeOptions.FirstOrDefault(o => o.Value == s.HoldSubMode) ?? SubModeOptions[0];
        DoubleClickIntervalMs = s.DoubleClickIntervalMs;
        TriggerKeyVk = s.WaitForKeyVk;
        TriggerKeyName = Win32.VkToDisplayName(s.WaitForKeyVk);
        StopCheckCount = s.StopCheckCount;
        StopCheckWindowMs = s.StopCheckWindowMs;
        TriggerSampleWindowMs = s.TriggerSampleWindowMs;
        TickMs = s.ClickerThreadTickMs;
        MinIntervalMs = s.MinClickIntervalMs;

        HasUnsavedChanges = false;
        _lastProfileName = profileName;
        OnPropertyChanged(nameof(IsSpamMode));
        OnPropertyChanged(nameof(IsHoldMode));
        OnPropertyChanged(nameof(ShowDoubleClickInterval));
        OnPropertyChanged(nameof(ShowTriggerKey));
        PushToEngine();
    }

    /// <summary>
    /// Writes the current working settings to a named profile and keeps the
    /// in-memory profile list, selection and last-used bookmarks in sync.
    /// </summary>
    private void PersistCurrentUnder(string name)
    {
        var settings = BuildSettings();
        settings.ProfileName = name;
        _profiles.Save(settings);

        HasUnsavedChanges = false;
        _lastProfileName = name;
        _profiles.SaveLastProfile(name);

        if (!Profiles.Contains(name))
        {
            _syncingProfile = true;
            Profiles.Add(name);
            var sorted = Profiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            Profiles.Clear();
            foreach (var p in sorted) Profiles.Add(p);
            _syncingProfile = false;
        }
        SelectedProfile = name;
    }

    private void SaveCurrentProfile()
    {
        PersistCurrentUnder(CurrentProfileName);
        SetNotice($"Profile \"{CurrentProfileName}\" saved");
    }

    private void TryLoadProfile(string profileName)
    {
        if (_syncingProfile) return;
        if (_engine.IsEnabled) _engine.Disable();

        // Don't lose the current work when switching profiles.
        _syncingProfile = true;
        if (HasUnsavedChanges && _lastProfileName != null) PersistCurrentUnder(_lastProfileName);
        LoadSettings(profileName);
        _syncingProfile = false;
        SetNotice($"Loaded \"{profileName}\"");
    }

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------

    [RelayCommand]
    private void ToggleRunning()
    {
        if (IsRunning)
        {
            _engine.Disable();
            IsRunning = false;
            SetNotice("Stopped");
        }
        else
        {
            if (HasUnsavedChanges) SaveCurrentProfile();
            PushToEngine();
            _engine.Enable();
            IsRunning = true;
            SetNotice("Enabled — activate in game to start clicking");
        }
    }

    [RelayCommand]
    private async Task CaptureKey()
    {
        if (_capturingKey) return;
        _capturingKey = true;
        IsCapturingKey = true;
        _engine.Hooks.CaptureNextKey = true;
        SetNotice("Press a key…  (Esc to cancel)");

        var tcs = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<uint> handler = vk => tcs.TrySetResult(vk);
        _engine.KeyCaptured += handler;
        try
        {
            var token = new CancellationTokenSource(TimeSpan.FromSeconds(12)).Token;
            uint vk = await tcs.Task.WaitAsync(token);
            if (vk == Win32.VK_ESCAPE)
            {
                SetNotice("Key capture cancelled");
            }
            else
            {
                TriggerKeyVk = vk;
                TriggerKeyName = Win32.VkToDisplayName(vk);
                StopIfRunning();
                MarkDirty();
                PushToEngine();
                SetNotice($"Trigger key set to {TriggerKeyName}");
            }
        }
        catch (OperationCanceledException)
        {
            SetNotice("Key capture timed out");
        }
        finally
        {
            _engine.KeyCaptured -= handler;
            _engine.Hooks.CaptureNextKey = false;
            _capturingKey = false;
            IsCapturingKey = false;
        }
    }

    [RelayCommand]
    private void SaveProfile() => SaveCurrentProfile();

    [RelayCommand]
    private void NewProfile()
    {
        string name = CurrentProfileName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SetNotice("Enter a name first");
            return;
        }
        if (_profiles.Exists(name))
        {
            SetNotice($"Profile \"{name}\" already exists");
            return;
        }

        // Don't lose the current work when creating a fresh profile.
        if (HasUnsavedChanges && _lastProfileName != null)
        {
            _syncingProfile = true;
            PersistCurrentUnder(_lastProfileName);
            _syncingProfile = false;
        }

        var fresh = new ClickerSettings { ProfileName = name };
        _profiles.Save(fresh);
        LoadSettings(name);
        SetNotice($"Created profile \"{name}\"");
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (Profiles.Count <= 1)
        {
            SetNotice("Cannot delete the last profile");
            return;
        }

        string name = CurrentProfileName;
        if (!_profiles.Exists(name) && SelectedProfile != null) name = SelectedProfile;
        if (!_profiles.Exists(name))
        {
            SetNotice("No saved profile selected to delete");
            return;
        }

        _profiles.Delete(name);
        _syncingProfile = true;
        Profiles.Remove(name);
        _syncingProfile = false;

        string next = Profiles.FirstOrDefault(p => !string.Equals(p, name, StringComparison.OrdinalIgnoreCase))
                      ?? "Default";
        if (_engine.IsEnabled) _engine.Disable();
        LoadSettings(next);
        SetNotice($"Deleted \"{name}\"");
    }

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedOpen = !IsAdvancedOpen;

    [RelayCommand]
    private void ToggleTopmost() => IsTopmost = !IsTopmost;

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void OnTuningChanged()
    {
        StopIfRunning();
        MarkDirty();
        PushToEngine();
    }

    private void StopIfRunning()
    {
        if (IsRunning)
        {
            _engine.Disable();
            IsRunning = false;
            SetNotice("Stopped — settings changed");
        }
    }

    private void MarkDirty() => HasUnsavedChanges = true;

    private CancellationTokenSource? _noticeCts;

    private void SetNotice(string text)
    {
        _noticeCts?.Cancel();
        _noticeCts = new CancellationTokenSource();
        Notice = text;
        _ = ClearNoticeAfterAsync(text, _noticeCts.Token);
    }

    private async Task ClearNoticeAfterAsync(string text, CancellationToken token)
    {
        try
        {
            await Task.Delay(2800, token);
            if (Notice == text) Notice = "";
        }
        catch (TaskCanceledException) { }
    }

    public void Shutdown()
    {
        if (HasUnsavedChanges) SaveCurrentProfile();
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.Dispose();
    }
}
