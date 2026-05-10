using System.Text.Json.Serialization;

namespace BambuDry.Core;

public enum AmsModel
{
    ExtSpool = 0,
    Ams = 1,
    AmsLite = 2,
    /// <summary>AMS 2 Pro</summary>
    N3F = 3,
    /// <summary>AMS HT</summary>
    N3S = 4,
}

public static class AmsModelExtensions
{
    public static bool SupportsDrying(this AmsModel m) => m == AmsModel.N3F || m == AmsModel.N3S;
    public static bool SupportsHumidityPercent(this AmsModel m) => m == AmsModel.N3F || m == AmsModel.N3S;

    public static (int Min, int Max) DryTempRangeC(this AmsModel m) => m switch
    {
        AmsModel.N3F => (35, 65),
        AmsModel.N3S => (35, 85),
        _            => (35, 65),
    };
}

public enum DryStatus
{
    Off = 0,
    Checking = 1,
    Drying = 2,
    Cooling = 3,
    Stopping = 4,
    Error = 5,
    CannotStopHeatOutOfControl = 6,
    PrdTesting = 7,
}

public static class DryStatusExtensions
{
    public static bool IsActive(this DryStatus s) => s switch
    {
        DryStatus.Checking
            or DryStatus.Drying
            or DryStatus.Cooling
            or DryStatus.Stopping
            or DryStatus.Error
            or DryStatus.CannotStopHeatOutOfControl => true,
        _ => false,
    };
}

public enum CannotDryReason
{
    TaskOccupied = 0,
    InsufficientPower = 1,
    AmsBusy = 2,
    ConsumableAtAmsOutlet = 3,
    InitiatingAmsDrying = 4,
    NotSupportedIn2dMode = 5,
    DryingInProgress = 6,
    Upgrading = 7,
    InsufficientPowerNeedPluginPower = 8,
    FilamentAtAmsOutletManualUnload = 10,
}

public sealed record Printer(
    Guid Id,
    string Name,
    string Serial,
    string? LanIp = null,
    Printer.TransportKind Transport = Printer.TransportKind.Lan)
{
    public enum TransportKind { Lan, Cloud }

    public static Printer New(string name, string serial, string? lanIp = null, TransportKind transport = TransportKind.Lan)
        => new(Guid.NewGuid(), name, serial, lanIp, transport);
}

public sealed record AutoDrySettings
{
    public bool Enabled { get; init; } = false;
    /// <summary>Start drying at &gt;= this RH%.</summary>
    public int HighThreshold { get; init; } = 30;
    /// <summary>Stop drying at &lt;= this RH%.</summary>
    public int LowThreshold { get; init; } = 18;
    public int TargetTemp { get; init; } = 45;
    public bool RunDuringPrint { get; init; } = false;
    /// <summary>Anti-cycling: minimum minutes of drying before STOP can be issued.</summary>
    public int MinOnMinutes { get; init; } = 5;
    /// <summary>Anti-cycling: minimum minutes after STOP before START can be issued.</summary>
    public int MinOffMinutes { get; init; } = 5;

    [JsonIgnore]
    public bool IsValid => LowThreshold < HighThreshold && TargetTemp > 0;

    /// <summary>Clamp every field to UI-exposed ranges. Call after decoding stored config.</summary>
    public AutoDrySettings Sanitized() => this with
    {
        TargetTemp    = 45,
        HighThreshold = Math.Max(5, Math.Min(35, HighThreshold)),
        LowThreshold  = Math.Max(0, Math.Min(Math.Min(25, HighThreshold - 1), LowThreshold)),
        MinOnMinutes  = Math.Max(0, Math.Min(60, MinOnMinutes)),
        MinOffMinutes = Math.Max(0, Math.Min(60, MinOffMinutes)),
    };
}

public sealed record AmsSnapshot(
    int AmsId,
    AmsModel Model,
    int? HumidityPercent,
    bool IsCurrentlyDrying,
    int LeftDryTimeMinutes = 0,
    IReadOnlyList<CannotDryReason>? CannotDryReasons = null,
    string? DominantFilamentType = null,
    double? CurrentTempC = null)
{
    public IReadOnlyList<CannotDryReason> CannotDryReasons { get; init; } = CannotDryReasons ?? Array.Empty<CannotDryReason>();

    [JsonIgnore]
    public bool SupportsDrying => Model.SupportsDrying();

    public bool Equals(AmsSnapshot? other) =>
        other is not null
        && AmsId == other.AmsId
        && Model == other.Model
        && HumidityPercent == other.HumidityPercent
        && IsCurrentlyDrying == other.IsCurrentlyDrying
        && LeftDryTimeMinutes == other.LeftDryTimeMinutes
        && DominantFilamentType == other.DominantFilamentType
        && CurrentTempC == other.CurrentTempC
        && CannotDryReasons.SequenceEqual(other.CannotDryReasons);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(AmsId); hc.Add(Model); hc.Add(HumidityPercent); hc.Add(IsCurrentlyDrying);
        hc.Add(LeftDryTimeMinutes); hc.Add(DominantFilamentType); hc.Add(CurrentTempC);
        foreach (var r in CannotDryReasons) hc.Add(r);
        return hc.ToHashCode();
    }
}
