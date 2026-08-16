namespace BestAutoClicker.Models;

/// <summary>
/// All tunable settings for the clicker, mirroring the original C++ engine.
/// Persisted as JSON per profile.
/// </summary>
public sealed class ClickerSettings
{
    public string ProfileName { get; set; } = "Default";

    // ---- Mode selection ----
    public ClickMode Mode { get; set; } = ClickMode.Spam;

    // ---- Spam mode ----
    public double SpamAutoClickCps { get; set; } = 10.0;
    public double SpamTriggerCps { get; set; } = 5.0;
    public int SpamDelayMs { get; set; } = 100;

    // ---- Hold mode ----
    public double HoldAutoClickCps { get; set; } = 10.0;
    public int HoldDelayMs { get; set; } = 100;
    public HoldSubMode HoldSubMode { get; set; } = HoldSubMode.Immediate;
    public int DoubleClickIntervalMs { get; set; } = 300;
    public uint WaitForKeyVk { get; set; } = 0x10; // VK_SHIFT

    // ---- Advanced / stop-check ----
    public int StopCheckCount { get; set; } = 3;
    public int StopCheckWindowMs { get; set; } = 150;
    public int TriggerSampleWindowMs { get; set; } = 400;
    public int ClickerThreadTickMs { get; set; } = 2;
    public int MinClickIntervalMs { get; set; } = 1;

    public ClickerSettings Clone() => (ClickerSettings)MemberwiseClone();
}
