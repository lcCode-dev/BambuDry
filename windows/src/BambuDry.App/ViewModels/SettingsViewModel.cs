using System.Diagnostics;
using BambuDry.App.Services;
using BambuDry.App.Storage;
using BambuDry.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BambuDry.App.ViewModels;

/// <summary>
/// Settings window VM. Holds editable copies of every config field across the
/// Printer / Defaults / Advanced tabs and persists changes back through
/// <see cref="AppViewModel"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppViewModel _app;

    // Printer tab
    [ObservableProperty] private string _printerName;
    [ObservableProperty] private string _lanIp;
    [ObservableProperty] private string _serial;
    [ObservableProperty] private string _accessCode;

    // Defaults tab
    [ObservableProperty] private bool _defaultEnabled;
    [ObservableProperty] private bool _defaultRunDuringPrint;
    [ObservableProperty] private int _defaultHigh;
    [ObservableProperty] private int _defaultLow;
    [ObservableProperty] private int _defaultMinOn;
    [ObservableProperty] private int _defaultMinOff;

    // Advanced tab
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DryRunBannerText))]
    private bool _dryRunMode;

    public string DryRunBannerText => DryRunMode
        ? "Dry-run mode is ON — the controller decides actions but DOES NOT publish START/STOP commands to the printer. Untoggle when you're ready for live control."
        : "LIVE: the controller will publish START/STOP commands to the printer when the hysteresis triggers.";

    public System.Collections.ObjectModel.ObservableCollection<AppViewModel.PublishLogEntry> RecentPublishes => _app.RecentPublishes;

    public SettingsViewModel(AppViewModel app)
    {
        _app = app;

        var c = app.Config;
        _printerName = c.PrinterName;
        _lanIp       = c.LanIp;
        _serial      = c.Serial;
        _accessCode  = app.LoadAccessCode() ?? string.Empty;

        var d = c.DefaultSettings;
        _defaultEnabled        = d.Enabled;
        _defaultRunDuringPrint = d.RunDuringPrint;
        _defaultHigh           = d.HighThreshold;
        _defaultLow            = d.LowThreshold;
        _defaultMinOn          = d.MinOnMinutes;
        _defaultMinOff         = d.MinOffMinutes;

        _launchAtLogin = c.LaunchAtLogin;
        _dryRunMode    = c.DryRunMode;
    }

    [RelayCommand]
    private async Task SavePrinterAsync()
    {
        var newConfig = _app.Config with
        {
            PrinterName = string.IsNullOrWhiteSpace(PrinterName) ? "My Bambu Printer" : PrinterName,
            LanIp       = (LanIp ?? "").Trim(),
            Serial      = (Serial ?? "").Trim(),
        };
        _app.SaveAndPersistConfig(newConfig);
        if (!string.IsNullOrEmpty(AccessCode))
            _app.SaveAccessCode(AccessCode);

        await _app.StartAsync();
    }

    [RelayCommand]
    private void SaveDefaults()
    {
        var newDefaults = new AutoDrySettings
        {
            Enabled        = DefaultEnabled,
            RunDuringPrint = DefaultRunDuringPrint,
            HighThreshold  = DefaultHigh,
            LowThreshold   = DefaultLow,
            MinOnMinutes   = DefaultMinOn,
            MinOffMinutes  = DefaultMinOff,
            TargetTemp     = 45,
        };
        _app.UpdateDefaultSettings(newDefaults);
    }

    [RelayCommand]
    private async Task ApplyAdvancedAsync()
    {
        // launch-at-login
        try
        {
            if (LaunchAtLogin)
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrEmpty(exe)) LaunchAtLoginService.Enable(exe);
            }
            else
            {
                LaunchAtLoginService.Disable();
            }
        }
        catch { /* registry write failure is non-fatal — user can retry */ }

        // dry-run + launch-at-login persisted into config
        var newConfig = _app.Config with
        {
            DryRunMode    = DryRunMode,
            LaunchAtLogin = LaunchAtLogin,
        };
        _app.SaveAndPersistConfig(newConfig);
        // Reconnect so the orchestrator picks up the new DryRun value.
        await _app.StartAsync();
    }

    [RelayCommand]
    private async Task ReconnectAsync() => await _app.StartAsync();

    [RelayCommand]
    private async Task DisconnectAsync() => await _app.StopAsync();
}
