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

    [ObservableProperty] private AppConfig _config;
    [ObservableProperty] private ConnectionState _state;
    [ObservableProperty] private string? _lastError;

    public ObservableCollection<AmsSnapshot> AmsSnapshots { get; } = new();

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

    public async Task StartAsync()
    {
        await StopAsync().ConfigureAwait(false);

        if (!Config.IsConfigured) { State = ConnectionState.NotConfigured; return; }
        var code = LoadAccessCode();
        if (string.IsNullOrEmpty(code)) { State = ConnectionState.NotConfigured; LastError = "Access code not set."; return; }

        State = ConnectionState.Connecting;
        try
        {
            _transport = new LanTransport(new LanTransport.Config(
                Host: Config.LanIp,
                Serial: Config.Serial,
                AccessCode: code));

            // Build orchestrator config from current AppConfig.
            var settingsByAms = new Dictionary<int, AutoDrySettings>();
            foreach (var (k, v) in Config.AmsSettings)
                if (int.TryParse(k, out var amsId)) settingsByAms[amsId] = v;

            _orchestrator = new Orchestrator(_transport, new OrchestratorConfig
            {
                SettingsByAmsId = settingsByAms,
                DefaultSettings = Config.DefaultSettings,
                DryRun = true,  // start safe; user can toggle off in Advanced
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
                    if (ev is Orchestrator.Event.Observed o)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => UpsertSnapshot(o.Snapshot));
                    else if (ev is Orchestrator.Event.ErrorOccurred err)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => LastError = err.Message);
                }
            }, ct);

            await orch.RunAsync(ct).ConfigureAwait(false);
            State = ConnectionState.Connected;
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
        for (var i = 0; i < AmsSnapshots.Count; i++)
        {
            if (AmsSnapshots[i].AmsId == snap.AmsId) { AmsSnapshots[i] = snap; return; }
        }
        AmsSnapshots.Add(snap);
        // keep stable order by AMS id
        var sorted = AmsSnapshots.OrderBy(s => s.AmsId).ToList();
        AmsSnapshots.Clear();
        foreach (var s in sorted) AmsSnapshots.Add(s);
    }
}
