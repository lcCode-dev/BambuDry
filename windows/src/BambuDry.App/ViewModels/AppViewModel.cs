using System.Collections.ObjectModel;
using BambuDry.App.Storage;
using BambuDry.Core;
using BambuDry.Core.Control;
using BambuDry.Core.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BambuDry.App.ViewModels;

/// <summary>
/// Top-level view model: owns the Orchestrator + Transport, surfaces config
/// and connection state to the UI. Mirrors the role of macOS <c>AppModel</c>.
/// </summary>
public sealed partial class AppViewModel : ObservableObject
{
    public enum ConnectionState { NotConfigured, Connecting, Connected, Disconnected }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigured), nameof(StateText), nameof(StateBrush))]
    private AppConfig _config;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(StateBrush))]
    private ConnectionState _state;

    [ObservableProperty] private string? _lastError;

    public bool IsConfigured => Config.IsConfigured;

    public string StateText => State switch
    {
        ConnectionState.NotConfigured => "Not configured",
        ConnectionState.Connecting    => "Connecting…",
        ConnectionState.Connected     => "Connected",
        ConnectionState.Disconnected  => "Disconnected",
        _ => string.Empty,
    };

    public Avalonia.Media.IBrush StateBrush => State switch
    {
        ConnectionState.Connected     => Avalonia.Media.Brushes.LimeGreen,
        ConnectionState.Connecting    => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xFF, 0xA8, 0x26)),
        ConnectionState.Disconnected  => Avalonia.Media.Brushes.OrangeRed,
        _                             => Avalonia.Media.Brushes.Gray,
    };

    public ObservableCollection<AmsRowViewModel> Rows { get; } = new();

    private LanTransport? _transport;
    private Orchestrator? _orchestrator;
    private CancellationTokenSource? _runCts;

    public AppViewModel(AppConfig config)
    {
        _config = config;
        _state = config.IsConfigured ? ConnectionState.Disconnected : ConnectionState.NotConfigured;
    }

    public void SaveAndPersistConfig(AppConfig newConfig)
    {
        Config = newConfig;
        ConfigStore.Save(newConfig);
        if (!newConfig.IsConfigured) State = ConnectionState.NotConfigured;
    }

    public void SaveAccessCode(string code)
    {
        if (string.IsNullOrEmpty(Config.Serial)) return;
        CredentialStore.Save(code, Config.Serial);
    }

    public string? LoadAccessCode() =>
        string.IsNullOrEmpty(Config.Serial) ? null : CredentialStore.Load(Config.Serial);

    /// <summary>
    /// Replace the default per-AMS settings, persist to disk, push to a running
    /// orchestrator, and refresh the row VMs so display thresholds update too.
    /// </summary>
    public void UpdateDefaultSettings(AutoDrySettings newDefaults)
    {
        Config = Config with { DefaultSettings = newDefaults };
        ConfigStore.Save(Config);
        _orchestrator?.SetDefaultSettings(newDefaults);
        foreach (var row in Rows) row.Settings = newDefaults;
    }

    public async Task StartAsync()
    {
        await StopAsync().ConfigureAwait(false);

        if (!Config.IsConfigured) { State = ConnectionState.NotConfigured; return; }
        var code = LoadAccessCode();
        if (string.IsNullOrEmpty(code))
        {
            State = ConnectionState.NotConfigured;
            LastError = "Access code not set.";
            return;
        }

        State = ConnectionState.Connecting;
        LastError = null;
        try
        {
            _transport = new LanTransport(new LanTransport.Config(
                Host: Config.LanIp,
                Serial: Config.Serial,
                AccessCode: code));

            var settingsByAms = new Dictionary<int, AutoDrySettings>();
            foreach (var (k, v) in Config.AmsSettings)
                if (int.TryParse(k, out var amsId)) settingsByAms[amsId] = v;

            _orchestrator = new Orchestrator(_transport, new OrchestratorConfig
            {
                SettingsByAmsId = settingsByAms,
                DefaultSettings = Config.DefaultSettings,
                DryRun = true,   // start safe; user can toggle off later
            });

            _runCts = new CancellationTokenSource();
            _ = Task.Run(() => RunAndObserveAsync(_runCts.Token));
        }
        catch (Exception ex)
        {
            State = ConnectionState.Disconnected;
            LastError = ex.Message;
        }
    }

    public async Task StopAsync()
    {
        _runCts?.Cancel();
        if (_transport is not null)
        {
            try { await _transport.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }
        _transport = null;
        _orchestrator = null;
        _runCts = null;
        State = Config.IsConfigured ? ConnectionState.Disconnected : ConnectionState.NotConfigured;
    }

    private async Task RunAndObserveAsync(CancellationToken ct)
    {
        try
        {
            var orch = _orchestrator!;
            var observe = Task.Run(async () =>
            {
                await foreach (var ev in orch.Events(ct).ConfigureAwait(false))
                {
                    switch (ev)
                    {
                        case Orchestrator.Event.Connected:
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (State == ConnectionState.Connecting) State = ConnectionState.Connected;
                            });
                            break;

                        case Orchestrator.Event.Observed o:
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                // Belt-and-suspenders: if Connected event was missed (e.g. raced past
                                // first report), promote state on first observed too.
                                if (State == ConnectionState.Connecting) State = ConnectionState.Connected;
                                UpsertSnapshot(o.Snapshot);
                            });
                            break;

                        case Orchestrator.Event.DecisionMade d:
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpsertDecision(d.AmsId, d.Decision));
                            break;

                        case Orchestrator.Event.ErrorOccurred err:
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => LastError = err.Message);
                            break;
                    }
                }
            }, ct);

            await orch.RunAsync(ct).ConfigureAwait(false);
            await observe.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                State = ConnectionState.Disconnected;
                LastError = ex.Message;
            });
        }
    }

    private void UpsertSnapshot(AmsSnapshot snap)
    {
        var settings = Config.SettingsForAmsId(snap.AmsId);
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].AmsId == snap.AmsId)
            {
                Rows[i].Snapshot = snap;
                Rows[i].Settings = settings;
                return;
            }
        }
        Rows.Add(new AmsRowViewModel(snap, settings));
        // keep stable order by AMS id
        var sorted = Rows.OrderBy(r => r.AmsId).ToList();
        Rows.Clear();
        foreach (var r in sorted) Rows.Add(r);
    }

    private void UpsertDecision(int amsId, Decision decision)
    {
        foreach (var row in Rows)
            if (row.AmsId == amsId) { row.LastDecision = decision; return; }
    }
}
