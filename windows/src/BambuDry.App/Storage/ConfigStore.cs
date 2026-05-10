using System.Text.Json;

namespace BambuDry.App.Storage;

/// <summary>
/// Reads / writes <see cref="AppConfig"/> to <c>%APPDATA%\BambuDry\config.json</c>.
/// Mirrors macOS <c>Storage.configURL</c>, which uses
/// <c>~/Library/Application Support/BambuDry/config.json</c>.
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DictionaryKeyPolicy = null,
        PropertyNamingPolicy = null,
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BambuDry");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigFile)) return AppConfig.Empty();
        try
        {
            var bytes = File.ReadAllBytes(ConfigFile);
            return JsonSerializer.Deserialize<AppConfig>(bytes, JsonOpts) ?? AppConfig.Empty();
        }
        catch
        {
            // Corrupt config — fall back to defaults rather than failing to start.
            return AppConfig.Empty();
        }
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(config, JsonOpts);
        var tmp = ConfigFile + ".tmp";
        File.WriteAllText(tmp, json);
        // Atomic-ish replace: write tmp, move into place. Avoids torn writes.
        File.Move(tmp, ConfigFile, overwrite: true);
    }
}
