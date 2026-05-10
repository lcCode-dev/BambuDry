using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BambuDry.Core.Net;

public enum DryCtrlMode
{
    Off = 0,
    OnTime = 1,
    OnHumidity = 2,
}

// MARK: - Outbound: ams_filament_drying request

/// <summary>
/// Mirrors device/{serial}/request envelope for AMS drying.
/// Reference: lagos/src/slic3r/GUI/DeviceCore/DevFilaSystemCtrl.cpp:19-57
/// </summary>
public sealed record DryRequest([property: JsonPropertyName("print")] DryRequest.PrintBody Print)
{
    public sealed record PrintBody(
        [property: JsonPropertyName("command")]              string Command,
        [property: JsonPropertyName("sequence_id")]          string SequenceId,
        [property: JsonPropertyName("ams_id")]               int    AmsId,
        [property: JsonPropertyName("mode")]                 int    Mode,
        [property: JsonPropertyName("filament")]             string Filament,
        [property: JsonPropertyName("temp")]                 int    Temp,
        [property: JsonPropertyName("duration")]             int    Duration,
        [property: JsonPropertyName("humidity")]             int    Humidity,
        [property: JsonPropertyName("rotate_tray")]          bool   RotateTray,
        [property: JsonPropertyName("cooling_temp")]         int    CoolingTemp,
        [property: JsonPropertyName("close_power_conflict")] bool   ClosePowerConflict);

    public static DryRequest Start(int amsId, string filament, int tempC, int hours, string sequenceId,
                                   bool rotateTray = false, int coolingTemp = 50, bool closePowerConflict = false)
        => new(new PrintBody(
            Command: "ams_filament_drying",
            SequenceId: sequenceId,
            AmsId: amsId,
            Mode: (int)DryCtrlMode.OnTime,
            Filament: filament,
            Temp: tempC,
            Duration: hours,
            Humidity: 0,
            RotateTray: rotateTray,
            CoolingTemp: coolingTemp,
            ClosePowerConflict: closePowerConflict));

    public static DryRequest Stop(int amsId, string sequenceId)
        => new(new PrintBody(
            Command: "ams_filament_drying",
            SequenceId: sequenceId,
            AmsId: amsId,
            Mode: (int)DryCtrlMode.Off,
            Filament: "",
            Temp: 0,
            Duration: 0,
            Humidity: 0,
            RotateTray: false,
            CoolingTemp: 0,
            ClosePowerConflict: false));
}

// MARK: - Inbound: device/{serial}/report (AMS subset)
//
// Reference: lagos/src/slic3r/GUI/DeviceCore/DevFilaSystem.cpp:630-700.
// Only fields needed for auto-dry are decoded; the printer publishes many more.

public sealed class ReportEnvelope
{
    [JsonPropertyName("print")] public PrintReport? Print { get; set; }
}

public sealed class PrintReport
{
    [JsonPropertyName("ams")]         public AmsContainer? Ams { get; set; }
    /// <summary>"idle" / "running" / etc.</summary>
    [JsonPropertyName("print_type")]  public string? PrintType { get; set; }
    /// <summary>"PRINTING" / "IDLE" / "PAUSE" / "FINISH" / "RUNNING"</summary>
    [JsonPropertyName("gcode_state")] public string? GcodeState { get; set; }
}

public sealed class AmsContainer
{
    [JsonPropertyName("ams")] public List<AmsReport>? Ams { get; set; }
}

public sealed class AmsReport
{
    /// <summary>Numeric string; may be 128+ for switchers.</summary>
    [JsonPropertyName("id")]            public string Id { get; set; } = "";
    /// <summary>0..5 humidity-level (legacy AMS only).</summary>
    [JsonPropertyName("humidity")]      public string? Humidity { get; set; }
    /// <summary>"47" → 47%, "-1" → invalid (N3F/N3S).</summary>
    [JsonPropertyName("humidity_raw")]  public string? HumidityRaw { get; set; }
    [JsonPropertyName("temp")]          public string? Temp { get; set; }
    /// <summary>Hex bitfield: bits 0..3 = AMSModel, bits 4..7 = DryStatus.</summary>
    [JsonPropertyName("info")]          public string? Info { get; set; }
    /// <summary>Remaining dry minutes.</summary>
    [JsonPropertyName("dry_time")]      public int? DryTime { get; set; }
    [JsonPropertyName("dry_setting")]   public DrySettingReport? DrySetting { get; set; }
    /// <summary>CannotDryReason raw values.</summary>
    [JsonPropertyName("dry_sf_reason")] public List<int>? DrySfReason { get; set; }
    [JsonPropertyName("tray")]          public List<TrayReport>? Tray { get; set; }

    public sealed class DrySettingReport
    {
        [JsonPropertyName("dry_filament")]    public string? DryFilament { get; set; }
        [JsonPropertyName("dry_temperature")] public int? DryTemperature { get; set; }
        [JsonPropertyName("dry_duration")]    public int? DryDuration { get; set; }
    }

    public sealed class TrayReport
    {
        [JsonPropertyName("id")]              public string Id { get; set; } = "";
        [JsonPropertyName("tray_type")]       public string? TrayType { get; set; }
        [JsonPropertyName("tray_sub_brands")] public string? TraySubBrands { get; set; }
    }

    /// <summary>
    /// Decode <paramref name="hexString"/>, extract <paramref name="count"/> bits starting at <paramref name="start"/>.
    /// Reference: lagos/src/slic3r/GUI/DeviceCore/DevUtil.cpp:7-26
    /// </summary>
    public static int? Bits(string hexString, int start, int count)
    {
        var trimmed = hexString.Trim();
        var cleaned = (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            ? trimmed[2..]
            : trimmed;
        if (!ulong.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return null;
        ulong mask = (1UL << count) - 1;
        return (int)((value >> start) & mask);
    }

    public DryStatus? GetDryStatus()
    {
        if (Info is null) return null;
        var bits = Bits(Info, 4, 4);
        if (bits is null) return null;
        return Enum.IsDefined(typeof(DryStatus), bits.Value) ? (DryStatus)bits.Value : null;
    }

    /// <summary>
    /// AMS model is encoded in bits 0..3 of the <c>info</c> hex string.
    /// Reference: lagos/src/slic3r/GUI/DeviceCore/DevFilaSystem.cpp:585
    /// </summary>
    public AmsModel? GetAmsModel()
    {
        if (Info is null) return null;
        var bits = Bits(Info, 0, 4);
        if (bits is null) return null;
        return Enum.IsDefined(typeof(AmsModel), bits.Value) ? (AmsModel)bits.Value : null;
    }

    /// <summary>Auto-detects model from the <c>info</c> bitfield. Returns null if undecodable.</summary>
    public AmsSnapshot? Snapshot()
    {
        var model = GetAmsModel();
        return model is null ? null : Snapshot(model.Value);
    }

    /// <summary>Build a snapshot using an explicit model.</summary>
    public AmsSnapshot Snapshot(AmsModel model)
    {
        var amsIdInt = int.TryParse(Id, out var v) ? v : -1;

        int? humidity = null;
        if (HumidityRaw is { } hr && int.TryParse(hr, out var hv) && hv >= 0)
            humidity = hv;

        var dryStatus = GetDryStatus();

        var cannotReasons = new List<CannotDryReason>();
        if (DrySfReason is { } reasons)
            foreach (var r in reasons)
                if (Enum.IsDefined(typeof(CannotDryReason), r))
                    cannotReasons.Add((CannotDryReason)r);

        string? dominant = null;
        if (Tray is { } trays)
            foreach (var t in trays)
                if (!string.IsNullOrEmpty(t.TrayType)) { dominant = t.TrayType; break; }

        double? tempC = null;
        if (Temp is { } ts && double.TryParse(ts, NumberStyles.Float, CultureInfo.InvariantCulture, out var tv))
            tempC = tv;

        return new AmsSnapshot(
            AmsId: amsIdInt,
            Model: model,
            HumidityPercent: humidity,
            IsCurrentlyDrying: dryStatus?.IsActive() ?? false,
            LeftDryTimeMinutes: DryTime ?? 0,
            CannotDryReasons: cannotReasons,
            DominantFilamentType: dominant,
            CurrentTempC: tempC);
    }
}

/// <summary>Shared System.Text.Json options used by the transport.</summary>
public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
