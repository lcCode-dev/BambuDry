using BambuDry.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BambuDry.App.ViewModels;

/// <summary>
/// Mutable wrapper around <see cref="AutoDrySettings"/> for two-way Avalonia
/// bindings. Core's <see cref="AutoDrySettings"/> stays immutable; this VM
/// holds editable copies and rebuilds an immutable instance on save.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _runDuringPrint;
    [ObservableProperty] private int  _highThreshold;
    [ObservableProperty] private int  _lowThreshold;
    [ObservableProperty] private int  _targetTemp;
    [ObservableProperty] private int  _minOnMinutes;
    [ObservableProperty] private int  _minOffMinutes;

    public SettingsViewModel(AutoDrySettings source)
    {
        _enabled        = source.Enabled;
        _runDuringPrint = source.RunDuringPrint;
        _highThreshold  = source.HighThreshold;
        _lowThreshold   = source.LowThreshold;
        _targetTemp     = source.TargetTemp;
        _minOnMinutes   = source.MinOnMinutes;
        _minOffMinutes  = source.MinOffMinutes;
    }

    public AutoDrySettings ToSettings() => new()
    {
        Enabled        = Enabled,
        RunDuringPrint = RunDuringPrint,
        HighThreshold  = HighThreshold,
        LowThreshold   = LowThreshold,
        TargetTemp     = TargetTemp,
        MinOnMinutes   = MinOnMinutes,
        MinOffMinutes  = MinOffMinutes,
    };
}
