using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace BambuDry.Core.Net;

/// <summary>
/// MQTTnet-backed transport for a single Bambu printer over LAN.
///
/// Wire format (verified against firmware):
/// - Host: <c>{printer_ip}:8883</c>
/// - TLS with self-signed cert (we accept any cert from the host we configured)
/// - MQTT v3.1.1, username "bblp", password = LAN access code
/// - Subscribe <c>device/{serial}/report</c>, publish <c>device/{serial}/request</c>
/// </summary>
public sealed class LanTransport : IAsyncDisposable
{
    public sealed record Config(
        string Host,
        string Serial,
        string AccessCode,
        ushort Port = 8883,
        string? ClientId = null);

    public sealed class TransportException : Exception
    {
        public TransportException(string message) : base(message) { }
        public TransportException(string message, Exception inner) : base(message, inner) { }
    }

    public Config TransportConfig { get; }

    /// <summary>Verbose logging hook. Set to e.g. Console.WriteLine for stdout debug.</summary>
    public Action<string>? DebugLog { get; set; }

    private readonly IMqttClient _client;
    private readonly Channel<ReportEnvelope> _reports =
        Channel.CreateUnbounded<ReportEnvelope>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly string _clientId;

    /// <summary>
    /// Seed sequence_id from current Unix time so it stays monotonically increasing
    /// across app restarts. Some Bambu firmwares appear to dedupe commands based on
    /// (recent) sequence_id values — starting fresh from 1 every launch could cause
    /// the printer to silently ignore commands that look like ones it just processed.
    /// </summary>
    private long _sequenceId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private string NextSequenceId() => Interlocked.Increment(ref _sequenceId).ToString();

    public LanTransport(Config config)
    {
        TransportConfig = config;
        _clientId = config.ClientId ?? $"bambudry-{Guid.NewGuid().ToString("N")[..8]}";

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _client.DisconnectedAsync += e =>
        {
            Log($"DISCONNECT {(e.Exception?.Message ?? "no-error")}");
            return Task.CompletedTask;
        };
    }

    private void Log(string msg) => DebugLog?.Invoke($"[LanTransport] {msg}");

    /// <summary>Connect and subscribe. Throws on bad creds, TLS failure, or timeout.</summary>
    public async Task ConnectAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var connectTimeout = timeout ?? TimeSpan.FromSeconds(15);

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(TransportConfig.Host, TransportConfig.Port)
            .WithCredentials("bblp", TransportConfig.AccessCode)
            .WithClientId(_clientId)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
            .WithTimeout(connectTimeout)
            // Bambu printers ship with self-signed certs. Accept any cert presented
            // by the host we explicitly typed in. We trust the LAN endpoint, not
            // the cert chain.
            .WithTlsOptions(o => o
                .UseTls(true)
                .WithAllowUntrustedCertificates(true)
                .WithIgnoreCertificateChainErrors(true)
                .WithIgnoreCertificateRevocationErrors(true)
                .WithCertificateValidationHandler(_ => true))
            .Build();

        Log($"connecting to {TransportConfig.Host}:{TransportConfig.Port}");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(connectTimeout + TimeSpan.FromSeconds(5));
        try
        {
            var result = await _client.ConnectAsync(options, linkedCts.Token).ConfigureAwait(false);
            if (result.ResultCode != MqttClientConnectResultCode.Success)
                throw new TransportException($"MQTT connect failed: {result.ResultCode} ({result.ReasonString})");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransportException("MQTT connect timed out");
        }

        Log("CONNACK accepted");
        await _client.SubscribeAsync(
            new MqttTopicFilterBuilder()
                .WithTopic($"device/{TransportConfig.Serial}/report")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            ct).ConfigureAwait(false);
        Log($"SUBSCRIBE device/{TransportConfig.Serial}/report");
    }

    /// <summary>IAsyncEnumerable of decoded report envelopes. Single consumer.</summary>
    public IAsyncEnumerable<ReportEnvelope> ReportStream(CancellationToken ct = default)
        => _reports.Reader.ReadAllAsync(ct);

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _reports.Writer.TryComplete();
        var opts = new MqttClientDisconnectOptionsBuilder()
            .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
            .WithReasonString("client disconnect")
            .Build();
        return _client.DisconnectAsync(opts, ct);
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var bytes = e.ApplicationMessage.PayloadSegment;
            var envelope = JsonSerializer.Deserialize<ReportEnvelope>(bytes, WireJson.Options);
            if (envelope is not null)
                await _reports.Writer.WriteAsync(envelope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"decode failed: {ex.Message}");
        }
    }

    private async Task<string> PublishAsync(string json, CancellationToken ct)
    {
        var topic = $"device/{TransportConfig.Serial}/request";
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();
        var result = await _client.PublishAsync(msg, ct).ConfigureAwait(false);
        if (result.ReasonCode != MqttClientPublishReasonCode.Success)
            throw new TransportException($"MQTT publish failed: {result.ReasonCode}");
        return json;
    }

    public Task<string> SendAsync(DryRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, WireJson.Options);
        return PublishAsync(json, ct);
    }

    public Task<string> StartDryingAsync(int amsId, string filament, int tempC, int hours, CancellationToken ct = default)
        => SendAsync(DryRequest.Start(amsId, filament, tempC, hours, NextSequenceId()), ct);

    public Task<string> StopDryingAsync(int amsId, CancellationToken ct = default)
        => SendAsync(DryRequest.Stop(amsId, NextSequenceId()), ct);

    /// <summary>
    /// Ask the printer for a full status push. Necessary because regular updates
    /// are diff-only and frequently omit the AMS block.
    /// </summary>
    public Task<string> RequestPushAllAsync(CancellationToken ct = default)
    {
        const string body = """{"pushing":{"sequence_id":"0","command":"pushall"}}""";
        return PublishAsync(body, ct);
    }

    public async ValueTask DisposeAsync()
    {
        try { await DisconnectAsync().ConfigureAwait(false); } catch { /* swallow on dispose */ }
        _client.Dispose();
    }
}
