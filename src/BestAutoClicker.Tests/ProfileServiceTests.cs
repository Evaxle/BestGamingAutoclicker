using BestAutoClicker.Models;
using BestAutoClicker.Services;

namespace BestAutoClicker.Tests;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _dir;

    public ProfileServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bactests_" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    private ProfileService NewService() => new(_dir);

    [Fact]
    public void ListProfiles_CreatesDefaultProfileOnFirstRun()
    {
        var svc = NewService();
        var list = svc.ListProfiles();

        Assert.Contains("Default", list);
        Assert.True(svc.Exists("Default"));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEveryParameter()
    {
        var svc = NewService();
        var original = new ClickerSettings
        {
            ProfileName = "PvP",
            Mode = ClickMode.Hold,
            SpamAutoClickCps = 17.5,
            SpamTriggerCps = 6.2,
            SpamDelayMs = 123,
            HoldAutoClickCps = 22.0,
            HoldDelayMs = 77,
            HoldSubMode = HoldSubMode.DoubleClick,
            DoubleClickIntervalMs = 350,
            WaitForKeyVk = 0x11,
            StopCheckCount = 5,
            StopCheckWindowMs = 220,
            TriggerSampleWindowMs = 600,
            ClickerThreadTickMs = 4,
            MinClickIntervalMs = 3,
        };
        svc.Save(original);

        Assert.True(svc.Exists("PvP"));
        Assert.True(svc.TryLoad("PvP", out var loaded));
        Assert.Equal("PvP", loaded.ProfileName);
        Assert.Equal(ClickMode.Hold, loaded.Mode);
        Assert.Equal(17.5, loaded.SpamAutoClickCps);
        Assert.Equal(6.2, loaded.SpamTriggerCps);
        Assert.Equal(123, loaded.SpamDelayMs);
        Assert.Equal(22.0, loaded.HoldAutoClickCps);
        Assert.Equal(77, loaded.HoldDelayMs);
        Assert.Equal(HoldSubMode.DoubleClick, loaded.HoldSubMode);
        Assert.Equal(350, loaded.DoubleClickIntervalMs);
        Assert.Equal(0x11u, loaded.WaitForKeyVk);
        Assert.Equal(5, loaded.StopCheckCount);
        Assert.Equal(220, loaded.StopCheckWindowMs);
        Assert.Equal(600, loaded.TriggerSampleWindowMs);
        Assert.Equal(4, loaded.ClickerThreadTickMs);
        Assert.Equal(3, loaded.MinClickIntervalMs);
    }

    [Fact]
    public void TryLoad_MissingProfile_ReturnsDefaultSettings()
    {
        var svc = NewService();
        var ok = svc.TryLoad("DoesNotExist", out var loaded);

        Assert.False(ok);
        Assert.Equal("DoesNotExist", loaded.ProfileName);
    }

    [Fact]
    public void Delete_RemovesProfileFromDiskAndList()
    {
        var svc = NewService();
        svc.Save(new ClickerSettings { ProfileName = "Temp" });

        Assert.True(svc.Exists("Temp"));
        svc.Delete("Temp");

        Assert.False(svc.Exists("Temp"));
        Assert.DoesNotContain("Temp", svc.ListProfiles());
    }

    [Fact]
    public void LastProfileMeta_RoundTrips()
    {
        var svc = NewService();
        Assert.Null(svc.LoadLastProfile());

        svc.SaveLastProfile("PvP");
        Assert.Equal("PvP", svc.LoadLastProfile());
    }

    [Fact]
    public void Save_WithNewName_AppearsInProfileList()
    {
        var svc = NewService();
        svc.ListProfiles(); // ensures the Default profile exists (as the app does at startup)
        svc.Save(new ClickerSettings { ProfileName = "Mining" });

        var list = svc.ListProfiles();
        Assert.Contains("Mining", list);
        Assert.Contains("Default", list);
    }

    [Fact]
    public void Overwrite_ExistingProfile_UpdatesSettings()
    {
        var svc = NewService();
        var s = new ClickerSettings { ProfileName = "X", HoldAutoClickCps = 10.0 };
        svc.Save(s);

        s.HoldAutoClickCps = 30.0;
        svc.Save(s);

        Assert.True(svc.TryLoad("X", out var loaded));
        Assert.Equal(30.0, loaded.HoldAutoClickCps);
    }

    [Fact]
    public void Save_ProfileWithUnsafeCharacters_DoesNotThrow()
    {
        var svc = NewService();
        svc.Save(new ClickerSettings { ProfileName = "A/B:C*d?" });

        Assert.True(svc.Exists("A/B:C*d?"));
    }

    [Fact]
    public void ListProfiles_IsSorted()
    {
        var svc = NewService();
        svc.Save(new ClickerSettings { ProfileName = "Zebra" });
        svc.Save(new ClickerSettings { ProfileName = "Alpha" });

        var list = svc.ListProfiles();
        var sorted = list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, list);
    }
}
