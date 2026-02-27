using System.IO;
using System.Text.Json;

namespace Chaos.Client.Services;

public interface IKeyValueStore
{
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
}

// Stub — populated when UserSettings and a server endpoint are defined
public interface IServerSettingsStore { }

public class LocalJsonKeyValueStore : IKeyValueStore
{
    private static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Chaos", "client-settings.json");

    private readonly string _settingsPath;
    private readonly Dictionary<string, JsonElement> _cache;

    public LocalJsonKeyValueStore(string? path = null)
    {
        _settingsPath = path ?? DefaultSettingsPath;
        _cache = Load();
    }

    public T Get<T>(string key, T defaultValue)
    {
        if (!_cache.TryGetValue(key, out var element)) return defaultValue;
        try { return element.Deserialize<T>() ?? defaultValue; }
        catch { return defaultValue; }
    }

    public void Set<T>(string key, T value)
    {
        _cache[key] = JsonSerializer.SerializeToElement(value);
        Flush();
    }

    private Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new();
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch { return new(); }
    }

    private void Flush()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }
}
