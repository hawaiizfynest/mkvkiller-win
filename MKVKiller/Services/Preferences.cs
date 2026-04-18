using System.IO;
using System.Text.Json;

namespace MKVKiller.Services;

public class PreferencesData
{
    public string Theme { get; set; } = "blue";
    public string? LastInputFolder { get; set; }
    public string? LastOutputFolder { get; set; }
    public string SortMode { get; set; } = "name";
    public int MaxConcurrent { get; set; } = 1;
}

public static class Preferences
{
    public static PreferencesData Current { get; private set; } = new();

    private static string Path => System.IO.Path.Combine(App.AppDataPath, "prefs.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                Current = JsonSerializer.Deserialize<PreferencesData>(json) ?? new PreferencesData();
            }
        }
        catch { /* ignore */ }
    }

    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, json);
        }
        catch { /* ignore */ }
    }
}
