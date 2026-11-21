using System.Text.Json;

namespace NebulaReader.Services;

/// <summary>Preferencias locales de la distribución portable de Windows.</summary>
public static class AppPreferences
{
#if WINDOWS
    private static readonly object Sync = new();
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "Data", "preferences.json");

    public static T Get<T>(string key, T defaultValue)
    {
        lock (Sync)
        {
            var values = Read();
            if (values.TryGetValue(key, out var value))
            {
                try { return value.Deserialize<T>() ?? defaultValue; }
                catch (JsonException) { }
            }
            return defaultValue;
        }
    }

    public static bool ContainsKey(string key)
    {
        lock (Sync) return Read().ContainsKey(key);
    }

    public static void Set<T>(string key, T value)
    {
        lock (Sync)
        {
            var values = Read();
            values[key] = JsonSerializer.SerializeToElement(value);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(values));
            File.Move(temporary, FilePath, true);
        }
    }

    private static Dictionary<string, JsonElement> Read()
    {
        if (!File.Exists(FilePath)) return new(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(FilePath))
                ?? new(StringComparer.Ordinal);
        }
        catch (JsonException) { return new(StringComparer.Ordinal); }
    }
#else
    public static T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);
    public static bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);
    public static void Set<T>(string key, T value) => Preferences.Default.Set(key, value);
#endif
}
