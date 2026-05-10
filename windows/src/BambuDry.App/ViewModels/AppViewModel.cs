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

    /// <summary>Most-recent publishes (and dry-run skipped intents) for the Advanced tab log.</summary>
    public ObservableCollection<PublishLogEntry> RecentPublishes { get; } = new();

    public sealed record PublishLogEntry(DateTime Timestamp, string Description);

    private const int MaxRecentPublishes = 50;

    private LanTransport? _transport;
    private Orchestrator? _orchestrator;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _persistCts;

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
    /// Replace the default per-AMS settings, persist (debounced), push to a running
    /// orchestrator, and refresh the row VMs so display thresholds update too.
    /// </summary>
    public void UpdateDefaultSettings(AutoDrySettings newDefaults)
    {
        var sanitized = newDefaults.Sanitized();
        Config = Config with { DefaultSettings = sanitized };
        _orchestrator?.SetDefaultSettings(sanitized);

        // Refresh any rows still using the default fallback.
        foreach (var row in Rows)
            if (!Config.AmsSettings.ContainsKey(row.AmsId.ToString()))
                row.ApplyFromSettings(sanitized);

        SchedulePersist();
    }

    /// <summary>
    /// Replace per-AMS settings (creates the override if absent), persist
    /// (debounced), push to the orchestrator, refresh the row.
    /// </summary>
    public void UpdateAmsSettings(int amsId, AutoDrySettings settings)
    {
        var sanitized = settings.Sanitized();
        Config = Config with { AmsSettings = WithAms(Config.AmsSettings, amsId, sanitized) };
        _orchestrator?.SetSettings(sanitized, amsId);

        foreach (var row in Rows)
            if (row.AmsId == amsId) row.ApplyFromSettings(sanitized);

        SchedulePersist();
    }

    public async Task ManualStopAsync(int amsId)
    {
        if (_transport is null)
        {
            LastError = "Not connected — can't send Stop.";
            return;
        }
        try { await _transport.StopDryingAsync(amsId).ConfigureAwait(false); }
        catch (Exception ex) { LastError = $"Stop failed: {ex.Message}"; }
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
                DryRun = Config.DryRunMode,
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
                                if (State == ConnectionState.Connecting) State = ConnectionState.Connected;
                                UpsertSnapshot(o.Snapshot);
                            });
                            break;

                        case Orchestrator.Event.DecisionMade d:
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpsertDecision(d.AmsId, d.Decision));
                            break;

                        case Orchestrator.Event.Sent sent:
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                AppendPublishLog($"AMS {sent.AmsId}: {SummarizeJson(sent.Json)}"));
                            break;

                        case Orchestrator.Event.DryRunSkipped skip:
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                AppendPublishLog($"AMS {skip.AmsId}: [dry-run] {DescribeDecision(skip.Decision)}"));
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
                Rows[i].ApplyFromSettings(settings);
                return;
            }
        }
        Rows.Add(new AmsRowViewModel(this, snap, settings));
        var sorted = Rows.OrderBy(r => r.AmsId).ToList();
        Rows.Clear();
        foreach (var r in sorted) Rows.Add(r);
    }

    private void UpsertDecision(int amsId, Decision decision)
    {
        foreach (var row in Rows)
            if (row.AmsId == amsId) { row.LastDecision = decision; return; }
    }

    private void AppendPublishLog(string description)
    {
        RecentPublishes.Insert(0, new PublishLogEntry(DateTime.Now, description));
        while (RecentPublishes.Count > MaxRecentPublishes)
            RecentPublishes.RemoveAt(RecentPublishes.Count - 1);
    }

    private static string SummarizeJson(string json)
    {
        // Cheap shape match — avoid pulling JsonDocument for a log-line summary.
        if (json.Contains("\"mode\":1")) return "START drying";
        if (json.Contains("\"mode\":0") && json.Contains("ams_filament_drying")) return "STOP drying";
        if (json.Contains("\"command\":\"pushall\"")) return "pushall";
        return json.Length > 80 ? json[..80] + "…" : json;
    }

    private static string DescribeDecision(Decision d) => d switch
    {
        Decision.Start s => $"START at {s.TempC}°C / {s.Hours}h ({s.Filament})",
        Decision.Stop   => "STOP",
        _               => d.GetType().Name,
    };

    private static IReadOnlyDictionary<string, AutoDrySettings> WithAms(
        IReadOnlyDictionary<string, AutoDrySettings> src, int amsId, AutoDrySettings value)
    {
        var copy = new Dictionary<string, AutoDrySettings>(src) { [amsId.ToString()] = value };
        return copy;
    }

    /// <summary>
    /// Debounce config saves so a slider drag doesn't write the JSON 60 times
    /// per second. The orchestrator gets the change immediately; only the
    /// disk write is deferred.
    /// </summary>
    private void SchedulePersist()
    {
        _persistCts?.Cancel();
        _persistCts = new CancellationTokenSource();
        var ct = _persistCts.Token;
        var snapshot = Config;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, ct).ConfigureAwait(false);
                ConfigStore.Save(snapshot);
            }
            catch (OperationCanceledException) { /* coalesced */ }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => LastError = $"Save failed: {ex.Message}");
            }
        }, ct);
    }
}
