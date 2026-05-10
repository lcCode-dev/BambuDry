using Avalonia.Media;
using BambuDry.Core;
using BambuDry.Core.Control;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BambuDry.App.ViewModels;

/// <summary>
/// One row per AMS unit in the dropdown. Owns the latest snapshot, the latest
/// controller decision (for the descriptive status line), and the settings
/// being applied to it (currently always the default settings — per-AMS
/// overrides are a follow-up).
/// </summary>
public sealed partial class AmsRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(HumidityPercent),
        nameof(HumidityPercentText), nameof(HumidityFraction), nameof(CurrentTempCText),
        nameof(IsDrying), nameof(StatusText), nameof(StatusBrush), nameof(HumidityBrush),
        nameof(SupportsDrying))]
    private AmsSnapshot _snapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusBrush))]
    private Decision? _lastDecision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighThreshold), nameof(LowThreshold), nameof(HumidityBrush),
        nameof(StatusText))]
    private AutoDrySettings _settings;

    public AmsRowViewModel(AmsSnapshot snapshot, AutoDrySettings settings)
    {
        _snapshot = snapshot;
        _settings = settings;
    }

    public int AmsId => Snapshot.AmsId;

    public string Title => AmsId == 0 ? "AMS 1" : $"Slot {AmsId - 127}";

    public string Subtitle => Snapshot.Model switch
    {
        AmsModel.N3F => "(AMS 2 Pro)",
        AmsModel.N3S => "(AMS HT)",
        AmsModel.AmsLite => "(AMS Lite)",
        AmsModel.Ams => "(AMS)",
        AmsModel.ExtSpool => "(External spool)",
        _ => string.Empty,
    };

    public bool SupportsDrying => Snapshot.SupportsDrying;
    public bool IsDrying => Snapshot.IsCurrentlyDrying;

    public int? HumidityPercent => Snapshot.HumidityPercent;
    public string HumidityPercentText => HumidityPercent is int rh ? $"{rh}%" : "—";
    public double HumidityFraction => HumidityPercent is int rh ? rh / 100.0 : 0;

    public string CurrentTempCText => Snapshot.CurrentTempC is double t ? $"{t:0.0}°C" : "—";

    public int HighThreshold => Settings.HighThreshold;
    public int LowThreshold => Settings.LowThreshold;

    /// <summary>Color of the humidity bar based on snapshot + thresholds.</summary>
    public IBrush HumidityBrush
    {
        get
        {
            if (IsDrying) return Brushes.OrangeRed;
            if (HumidityPercent is not int rh) return Brushes.Gray;
            if (rh >= Settings.HighThreshold) return Brushes.OrangeRed;
            if (rh > Settings.LowThreshold)   return Brushes.Goldenrod;
            return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));   // green
        }
    }

    /// <summary>Status line below humidity. Mirrors macOS AmsRowView's status helper.</summary>
    public string StatusText
    {
        get
        {
            if (!SupportsDrying)             return "Heater not supported";
            if (HumidityPercent is null)     return "Humidity unknown";

            return LastDecision switch
            {
                Decision.Start    => "Heat command sent",
                Decision.Stop     => "Stop command sent",
                Decision.Noop n   => StatusFromNoop(n.Reason),
                _                 => IsDrying ? "Dehumidifying" : "Idle",
            };
        }
    }

    public IBrush StatusBrush => IsDrying
        ? Brushes.OrangeRed
        : new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB8));

    private static string StatusFromNoop(NoopReason reason) => reason switch
    {
        NoopReason.Disabled                       => "Auto off",
        NoopReason.UnsupportedAms                 => "Heater not supported",
        NoopReason.HumidityUnknown                => "Humidity unknown",
        NoopReason.Printing                       => "Idle (printing)",
        NoopReason.WithinHysteresisBand           => "Idle (in band)",
        NoopReason.WaitingAfterStop ws            => $"Cooldown — can heat in {FormatSeconds(ws.SecondsRemaining)}",
        NoopReason.WaitingAfterStart wa           => $"Dehumidifying — can stop in {FormatSeconds(wa.SecondsRemaining)}",
        NoopReason.StartInFlight si               => $"Heat command sent — retry in {si.SecondsRemaining}s",
        NoopReason.StopInFlight sp                => $"Stop command sent — retry in {sp.SecondsRemaining}s",
        NoopReason.SettingsInvalid                => "Settings invalid",
        NoopReason.CannotDry cd                   => $"Blocked: {string.Join(", ", cd.Reasons)}",
        _                                         => "Idle",
    };

    private static string FormatSeconds(int seconds)
    {
        var m = seconds / 60;
        var s = seconds % 60;
        return m > 0 ? $"{m}m {s:D2}s" : $"{s}s";
    }
}
