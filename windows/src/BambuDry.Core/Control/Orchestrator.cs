using System.Threading.Channels;
using BambuDry.Core.Net;

namespace BambuDry.Core.Control;

/// <summary>
/// Connects a LanTransport to one AutoDryController per AMS unit on a printer.
///
/// On each report:
///   1. Decode the AMS array
///   2. For each AMS, derive an AmsSnapshot (auto-detecting model from `info`)
///   3. Throttle per-AMS evaluation to once per <see cref="OrchestratorConfig.TickInterval"/>
///   4. Call AutoDryController.Evaluate()
///   5. Publish the resulting DryRequest (or just log it, in dry-run mode)
///
/// One-orchestrator-per-printer; multiple printers run independent instances.
/// </summary>
public sealed class Orchestrator
{
    public abstract record Event(DateTime Timestamp)
    {
        public sealed record Observed(DateTime Timestamp, AmsSnapshot Snapshot) : Event(Timestamp);
        public sealed record DecisionMade(DateTime Timestamp, int AmsId, Decision Decision) : Event(Timestamp);
        public sealed record Sent(DateTime Timestamp, int AmsId, string Json) : Event(Timestamp);
        public sealed record DryRunSkipped(DateTime Timestamp, int AmsId, Decision Decision) : Event(Timestamp);
        public sealed record ErrorOccurred(DateTime Timestamp, string Message) : Event(Timestamp);
    }

    private readonly LanTransport _transport;
    private readonly object _lock = new();
    private OrchestratorConfig _config;

    private readonly Dictionary<int, AutoDryController> _controllers = new();
    private readonly Dictionary<int, DateTime> _lastEvalAt = new();
    private DateTime _lastPushAllAt = DateTime.MinValue;

    private readonly Channel<Event> _events =
        Channel.CreateUnbounded<Event>(new UnboundedChannelOptions { SingleWriter = true });

    public Orchestrator(LanTransport transport, OrchestratorConfig config)
    {
        _transport = transport;
        _config = config;
    }

    public IAsyncEnumerable<Event> Events(CancellationToken ct = default) => _events.Reader.ReadAllAsync(ct);

    // Live config mutation — the orchestrator is long-lived; the user can change
    // settings from the menu bar UI after we're already running.
    public void SetSettings(AutoDrySettings settings, int amsId)
    {
        lock (_lock) _config = _config with { SettingsByAmsId = With(_config.SettingsByAmsId, amsId, settings) };
    }
    public void SetDefaultSettings(AutoDrySettings settings)
    {
        lock (_lock) _config = _config with { DefaultSettings = settings };
    }
    public void SetDryRun(bool dryRun)
    {
        lock (_lock) _config = _config with { DryRun = dryRun };
    }

    private OrchestratorConfig CurrentConfig() { lock (_lock) return _config; }

    /// <summary>Drive the loop: connect, request pushall, then process every report.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            await _transport.ConnectAsync(ct: ct).ConfigureAwait(false);
            await _transport.RequestPushAllAsync(ct).ConfigureAwait(false);
            _lastPushAllAt = DateTime.UtcNow;

            await foreach (var envelope in _transport.ReportStream(ct).ConfigureAwait(false))
            {
                await IngestAsync(envelope, ct).ConfigureAwait(false);
                await MaybeRefreshAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _events.Writer.TryComplete();
        }
    }

    private async Task MaybeRefreshAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cfg = CurrentConfig();
        if (now - _lastPushAllAt >= cfg.PushAllInterval)
        {
            try { await _transport.RequestPushAllAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { Emit(new Event.ErrorOccurred(DateTime.UtcNow, $"pushAll failed: {ex.Message}")); }
            _lastPushAllAt = now;
        }
    }

    private async Task IngestAsync(ReportEnvelope envelope, CancellationToken ct)
    {
        var amsList = envelope.Print?.Ams?.Ams;
        if (amsList is null) return;

        var gcodeState = envelope.Print?.GcodeState ?? "";
        var isPrinting = gcodeState is "RUNNING" or "PRINTING" or "PAUSE";

        var cfg = CurrentConfig();

        foreach (var ams in amsList)
        {
            var snapshot = ams.Snapshot();
            if (snapshot is null) continue;

            Emit(new Event.Observed(DateTime.UtcNow, snapshot));

            // Throttle per-AMS evaluation.
            var now = DateTime.UtcNow;
            if (_lastEvalAt.TryGetValue(snapshot.AmsId, out var last)
                && now - last < cfg.TickInterval)
                continue;
            _lastEvalAt[snapshot.AmsId] = now;

            // One controller per AMS, lazily created.
            if (!_controllers.TryGetValue(snapshot.AmsId, out var controller))
            {
                controller = new AutoDryController(snapshot.AmsId);
                _controllers[snapshot.AmsId] = controller;
            }

            var settings = cfg.SettingsByAmsId.TryGetValue(snapshot.AmsId, out var s) ? s : cfg.DefaultSettings;
            var decision = controller.Evaluate(snapshot, settings, isPrinting);
            Emit(new Event.DecisionMade(DateTime.UtcNow, snapshot.AmsId, decision));

            switch (decision)
            {
                case Decision.Start start:
                    if (cfg.DryRun)
                    {
                        Emit(new Event.DryRunSkipped(DateTime.UtcNow, start.AmsId, decision));
                    }
                    else
                    {
                        try
                        {
                            var json = await _transport.StartDryingAsync(
                                start.AmsId, start.Filament, start.TempC, start.Hours, ct).ConfigureAwait(false);
                            Emit(new Event.Sent(DateTime.UtcNow, start.AmsId, json));
                        }
                        catch (Exception ex)
                        {
                            Emit(new Event.ErrorOccurred(DateTime.UtcNow, $"start publish failed: {ex.Message}"));
                        }
                    }
                    break;

                case Decision.Stop stop:
                    if (cfg.DryRun)
                    {
                        Emit(new Event.DryRunSkipped(DateTime.UtcNow, stop.AmsId, decision));
                    }
                    else
                    {
                        try
                        {
                            var json = await _transport.StopDryingAsync(stop.AmsId, ct).ConfigureAwait(false);
                            Emit(new Event.Sent(DateTime.UtcNow, stop.AmsId, json));
                        }
                        catch (Exception ex)
                        {
                            Emit(new Event.ErrorOccurred(DateTime.UtcNow, $"stop publish failed: {ex.Message}"));
                        }
                    }
                    break;
            }
        }
    }

    private void Emit(Event e) => _events.Writer.TryWrite(e);

    private static IReadOnlyDictionary<int, AutoDrySettings> With(IReadOnlyDictionary<int, AutoDrySettings> src, int key, AutoDrySettings value)
    {
        var copy = new Dictionary<int, AutoDrySettings>(src) { [key] = value };
        return copy;
    }
}

public sealed record OrchestratorConfig
{
    public required IReadOnlyDictionary<int, AutoDrySettings> SettingsByAmsId { get; init; }
    public required AutoDrySettings DefaultSettings { get; init; }
    public bool DryRun { get; init; } = true;
    public TimeSpan TickInterval    { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PushAllInterval { get; init; } = TimeSpan.FromSeconds(60);

    public static OrchestratorConfig Default() => new()
    {
        SettingsByAmsId = new Dictionary<int, AutoDrySettings>(),
        DefaultSettings = new AutoDrySettings(),
    };
}
