using System.Text.Json.Serialization;
using BambuDry.Core;

namespace BambuDry.App.Storage;

/// <summary>
/// On-disk app config (printer identity + per-AMS settings).
/// Access code lives in Credential Manager, NOT in this file.
///
/// Schema mirrors macOS <c>AppConfig</c> exactly (printerName, serial, lanIP,
/// amsSettings keyed by stringified AMS id, defaultSettings) so the JSON is
/// portable between platforms.
/// </summary>
public sealed record AppConfig
{
    [JsonPropertyName("printerName")]     public string PrinterName { get; init; } = "My Bambu Printer";
    [JsonPropertyName("serial")]          public string Serial { get; init; } = "";
    [JsonPropertyName("lanIP")]           public string LanIp { get; init; } = "";
    /// <summary>Map AMS id (0, 128, 129…) → settings.</summary>
    [JsonPropertyName("amsSettings")]     public IReadOnlyDictionary<string, AutoDrySettings> AmsSettings { get; init; }
        = new Dictionary<string, AutoDrySettings>();
    [JsonPropertyName("defaultSettings")] public AutoDrySettings DefaultSettings { get; init; } = new();

    /// <summary>
    /// When true, controller decides actions but does NOT publish commands to the
    /// printer. Defaults to <c>true</c> on first launch so a misconfigured threshold
    /// can't accidentally fire the heater. Toggleable in Settings → Advanced.
    /// </summary>
    [JsonPropertyName("dryRunMode")]      public bool DryRunMode { get; init; } = true;
    [JsonPropertyName("launchAtLogin")]   public bool LaunchAtLogin { get; init; } = false;

    public static AppConfig Empty() => new();

    public AutoDrySettings SettingsForAmsId(int id)
        => AmsSettings.TryGetValue(id.ToString(), out var s) ? s : DefaultSettings;

    public AppConfig WithSettings(AutoDrySettings s, int amsId)
    {
        var copy = new Dictionary<string, AutoDrySettings>(AmsSettings) { [amsId.ToString()] = s };
        return this with { AmsSettings = copy };
    }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Serial) && !string.IsNullOrWhiteSpace(LanIp);
}
