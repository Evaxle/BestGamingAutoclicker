using System.Text.Json;
using System.Text.Json.Serialization;
using BestAutoClicker.Models;

namespace BestAutoClicker.Services;

/// <summary>
/// Stores clicker profiles as JSON files under
/// %LOCALAPPDATA%\BestAutoClicker\profiles. Tracks the last-used profile.
/// </summary>
public sealed class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _baseDir;
    private readonly string _profilesDir;

    public ProfileService(string? baseDir = null)
    {
        _baseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BestAutoClicker");
        _profilesDir = Path.Combine(_baseDir, "profiles");
        Directory.CreateDirectory(_profilesDir);
    }

    private static string Sanitize(string name)
    {
        var chars = name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ' ').ToArray();
        string r = new(chars);
        return string.IsNullOrWhiteSpace(r) ? "profile" : r.Trim();
    }

    private string PathFor(string profileName) => Path.Combine(_profilesDir, Sanitize(profileName) + ".json");
    private string MetaPath() => Path.Combine(_baseDir, "_meta.json");

    public IReadOnlyList<string> ListProfiles()
    {
        var list = Directory.EnumerateFiles(_profilesDir, "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p) ?? "")
            .Where(n => !string.Equals(n, "_meta", StringComparison.OrdinalIgnoreCase) && n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
        {
            Save(new ClickerSettings { ProfileName = "Default" });
            list.Add("Default");
        }
        return list;
    }

    public bool Exists(string profileName) => File.Exists(PathFor(profileName));

    public void Save(ClickerSettings settings)
    {
        File.WriteAllText(PathFor(settings.ProfileName), JsonSerializer.Serialize(settings, JsonOptions));
    }

    public bool TryLoad(string profileName, out ClickerSettings settings)
    {
        string path = PathFor(profileName);
        if (!File.Exists(path))
        {
            settings = new ClickerSettings { ProfileName = profileName };
            return false;
        }
        try
        {
            var loaded = JsonSerializer.Deserialize<ClickerSettings>(File.ReadAllText(path), JsonOptions);
            if (loaded == null)
            {
                settings = new ClickerSettings { ProfileName = profileName };
                return false;
            }
            loaded.ProfileName = profileName;
            settings = loaded;
            return true;
        }
        catch (JsonException)
        {
            settings = new ClickerSettings { ProfileName = profileName };
            return false;
        }
    }

    public void Delete(string profileName)
    {
        string path = PathFor(profileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public string? LoadLastProfile()
    {
        try
        {
            if (!File.Exists(MetaPath())) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(MetaPath()));
            if (doc.RootElement.TryGetProperty("lastProfile", out var el))
                return el.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    public void SaveLastProfile(string profileName)
    {
        try
        {
            File.WriteAllText(MetaPath(), JsonSerializer.Serialize(new { lastProfile = profileName }));
        }
        catch (IOException) { }
    }
}
