using System.IO;
using System.Text.Json;
using Chaos.Client.ViewModels;

namespace Chaos.Client.Services;

public interface IClientSettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

// Stub — populated when UserSettings is defined and a server endpoint is added
public interface IServerSettingsStore { }

public class LocalJsonSettingsStore : IClientSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Chaos", "client-settings.json");

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
