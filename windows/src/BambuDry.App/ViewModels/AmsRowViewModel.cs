using Avalonia.Media;
using BambuDry.Core;
using BambuDry.Core.Control;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BambuDry.App.ViewModels;

/// <summary>
/// One row per AMS unit in the dropdown. Owns the latest snapshot, the latest
/// controller decision (for the descriptive status line), and the editable
/// per-AMS settings. Property changes route back to <see cref="AppViewModel"/>
/// for persistence + live-push to the running orchestrator.
/// </summary>
public sealed partial class AmsRowViewModel : ObservableObject
{
    private readonly AppViewModel _app;

    /// <summary>True while we're applying settings from the parent — suppresses
    /// the change-handlers from echoing the same values back as a "user edit".</summary>
    private bool _suppressPush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(HumidityPercent),
        nameof(HumidityPercentText), nameof(HumidityFraction), nameof(CurrentTempCText),
        nameof(IsDrying), nameof(StatusText), nameof(StatusBrush), nameof(HumidityBrush),
        nameof(SupportsDrying))]
    private AmsSnapshot _snapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusBrush))]
    private Decision? _lastDecision;

    // Editable per-AMS settings — bindable two-way to the row's controls.
    [ObservableProperty] private bool _autoEnabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HumidityBrush))]
    private int _highThreshold;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HumidityBrush))]
    private int _lowThreshold;
    [ObservableProperty] private int _minOnMinutes;
    [ObservableProperty] private int _minOffMinutes;
    [ObservableProperty] private bool _runDuringPrint;
    [ObservableProperty] private int _targetTemp;

    public AmsRowViewModel(AppViewModel app, AmsSnapshot snapshot, AutoDrySettings settings)
    {
        _app = app;
        _snapshot = snapshot;
        ApplyFromSettings(settings);
    }

    public int AmsId => Snapshot.AmsId;

    public string Title => AmsId == 0 ? "AMS 1" : $"Slot {AmsId - 127}";

    public string Subtitle => Snapshot.Model switch
    {
        AmsModel.N3F      => "(AMS 2 Pro)",
        AmsModel.N3S      => "(AMS HT)",
        AmsModel.AmsLite  => "(AMS Lite)",
        AmsModel.Ams      => "(AMS)",
        AmsModel.ExtSpool => "(External spool)",
        _ => string.Empty,
    };

    public bool SupportsDrying => Snapshot.SupportsDrying;
    public bool IsDrying => Snapshot.IsCurrentlyDrying;

    public int? HumidityPercent     => Snapshot.HumidityPercent;
    public string HumidityPercentText => HumidityPercent is int rh ? $"{rh}%" : "—";
    public double HumidityFraction  => HumidityPercent is int rh ? rh / 100.0 : 0;

    public string CurrentTempCText => Snapshot.CurrentTempC is double t ? $"{t:0.0}°C" : "—";

    /// <summary>Re-seed editable properties from a settings object (e.g. from disk).</summary>
    public void ApplyFromSettings(AutoDrySettings s)
    {
        _suppressPush = true;
        AutoEnabled    = s.Enabled;
        HighThreshold  = s.HighThreshold;
        LowThreshold   = s.LowThreshold;
        MinOnMinutes   = s.MinOnMinutes;
        MinOffMinutes  = s.MinOffMinutes;
        RunDuringPrint = s.RunDuringPrint;
        TargetTemp     = s.TargetTemp;
        _suppressPush = false;
    }

    public AutoDrySettings BuildSettings() => new()
    {
        Enabled        = AutoEnabled,
        HighThreshold  = HighThreshold,
        LowThreshold   = LowThreshold,
        MinOnMinutes   = MinOnMinutes,
        MinOffMinutes  = MinOffMinutes,
        RunDuringPrint = RunDuringPrint,
        TargetTemp     = TargetTemp == 0 ? 45 : TargetTemp,
    };

    partial void OnAutoEnabledChanged(bool value)    => MaybePush();
    partial void OnHighThresholdChanged(int value)   => MaybePush();
    partial void OnLowThresholdChanged(int value)    => MaybePush();
    partial void OnMinOnMinutesChanged(int value)    => MaybePush();
    partial void OnMinOffMinutesChanged(int value)   => MaybePush();
    partial void OnRunDuringPrintChanged(bool value) => MaybePush();
    partial void OnTargetTempChanged(int value)      => MaybePush();

    private void MaybePush()
    {
        if (_suppressPush) return;
        _app.UpdateAmsSettings(AmsId, BuildSettings());
    }

    public void RequestManualStop() => _ = _app.ManualStopAsync(AmsId);

    /// <summary>Color of the humidity bar based on snapshot + thresholds.</summary>
    public IBrush HumidityBrush
    {
        get
        {
            if (IsDrying) return Brushes.OrangeRed;
            if (HumidityPercent is not int rh) return Brushes.Gray;
            if (rh >= HighThreshold) return Brushes.OrangeRed;
            if (rh > LowThreshold)   return Brushes.Goldenrod;
            return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
    }

    public string StatusText
    {
        get
        {
            if (!SupportsDrying)         return "Heater not supported";
            if (HumidityPercent is null) return "Humidity unknown";

            return LastDecision switch
            {
                Decision.Start  => "Heat command sent",
                Decision.Stop   => "Stop command sent",
                Decision.Noop n => StatusFromNoop(n.Reason),
                _               => IsDrying ? "Dehumidifying" : "Idle",
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
