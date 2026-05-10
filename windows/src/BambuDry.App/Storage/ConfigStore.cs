using System.Text.Json;

namespace BambuDry.App.Storage;

/// <summary>
/// Reads / writes <see cref="AppConfig"/> to <c>%APPDATA%\BambuDry\config.json</c>.
/// Mirrors macOS <c>Storage.configURL</c>, which uses
/// <c>~/Library/Application Support/BambuDry/config.json</c>.
/// </summary>
public static class ConfigStore
{
    /// <summary>
    /// Public so tests + UI can use the same shape. CamelCase + case-insensitive
    /// reads make the JSON file compatible with the macOS app's Codable output
    /// (Swift defaults to lower-camelCase keys), so a config copied between
    /// platforms is interchangeable.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BambuDry");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load() => LoadFrom(ConfigFile);

    public static void Save(AppConfig config) => SaveTo(config, ConfigFile);

    /// <summary>Path-overridable load for tests + alternate locations.</summary>
    public static AppConfig LoadFrom(string path)
    {
        if (!File.Exists(path)) return AppConfig.Empty();
        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<AppConfig>(bytes, JsonOpts) ?? AppConfig.Empty();
        }
        catch
        {
            // Corrupt config — fall back to defaults rather than failing to start.
            return AppConfig.Empty();
        }
    }

    public static void SaveTo(AppConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonOpts);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        // Atomic-ish replace: write tmp, move into place. Avoids torn writes.
        File.Move(tmp, path, overwrite: true);
    }
}
