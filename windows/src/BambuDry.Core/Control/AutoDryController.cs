namespace BambuDry.Core.Control;

public abstract record Decision
{
    public sealed record Start(int AmsId, string Filament, int TempC, int Hours) : Decision;
    public sealed record Stop(int AmsId) : Decision;
    public sealed record Noop(NoopReason Reason) : Decision;
}

public abstract record NoopReason
{
    public sealed record Disabled : NoopReason;
    public sealed record UnsupportedAms : NoopReason;
    public sealed record HumidityUnknown : NoopReason;
    public sealed record Printing : NoopReason;
    public sealed record WithinHysteresisBand : NoopReason;
    /// <summary>Anti-cycling: just stopped, can't start yet.</summary>
    public sealed record WaitingAfterStop(int SecondsRemaining) : NoopReason;
    /// <summary>Anti-cycling: just started, can't stop yet.</summary>
    public sealed record WaitingAfterStart(int SecondsRemaining) : NoopReason;
    /// <summary>Sent a START recently; waiting for printer to ack.</summary>
    public sealed record StartInFlight(int SecondsRemaining) : NoopReason;
    /// <summary>Sent a STOP recently; waiting for printer to ack.</summary>
    public sealed record StopInFlight(int SecondsRemaining) : NoopReason;
    public sealed record SettingsInvalid : NoopReason;

    public sealed record CannotDry(IReadOnlyList<CannotDryReason> Reasons) : NoopReason
    {
        public bool Equals(CannotDry? other) =>
            other is not null && Reasons.SequenceEqual(other.Reasons);

        public override int GetHashCode()
        {
            var hc = new HashCode();
            foreach (var r in Reasons) hc.Add(r);
            return hc.ToHashCode();
        }
    }
}

/// <summary>
/// Hysteresis state machine for one AMS unit.
///
/// Behaviour:
/// - Issue START when humidity ≥ HighThreshold, AMS isn't already drying,
///   no CannotDryReasons, anti-cycling time elapsed, no recent in-flight START.
/// - Issue STOP when humidity ≤ LowThreshold, AMS is currently drying,
///   MinOnTime elapsed since the last START, no recent in-flight STOP.
/// - Skip everything if RunDuringPrint is false and the printer is printing.
/// </summary>
public sealed class AutoDryController
{
    public int AmsId { get; }
    /// <summary>Re-issue same command if this elapses without the printer acking it.</summary>
    public TimeSpan CommandRetryInterval { get; }
    public int DryHours { get; }

    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();
    private DateTime _lastStartIssuedAt = DateTime.MinValue;
    private DateTime _lastStopIssuedAt  = DateTime.MinValue;

    public AutoDryController(int amsId, TimeSpan? commandRetryInterval = null, int dryHours = 1, Func<DateTime>? clock = null)
    {
        AmsId = amsId;
        CommandRetryInterval = commandRetryInterval ?? TimeSpan.FromSeconds(30);
        DryHours = dryHours;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public Decision Evaluate(AmsSnapshot snapshot, AutoDrySettings settings, bool printerIsPrinting)
    {
        if (!settings.Enabled)         return new Decision.Noop(new NoopReason.Disabled());
        if (!settings.IsValid)         return new Decision.Noop(new NoopReason.SettingsInvalid());
        if (!snapshot.SupportsDrying)  return new Decision.Noop(new NoopReason.UnsupportedAms());
        if (snapshot.HumidityPercent is not int rh) return new Decision.Noop(new NoopReason.HumidityUnknown());
        if (printerIsPrinting && !settings.RunDuringPrint) return new Decision.Noop(new NoopReason.Printing());

        lock (_lock)
        {
            var now        = _clock();
            var minOnTime  = TimeSpan.FromMinutes(settings.MinOnMinutes);
            var minOffTime = TimeSpan.FromMinutes(settings.MinOffMinutes);

            // If the AMS reports it's already drying, anchor lastStart to now so future
            // stop-time gating works from the actual drying-start moment.
            if (snapshot.IsCurrentlyDrying && _lastStartIssuedAt == DateTime.MinValue)
                _lastStartIssuedAt = now;

            // Rising edge: humidity high, AMS not drying → consider START
            if (rh >= settings.HighThreshold && !snapshot.IsCurrentlyDrying)
            {
                if (snapshot.CannotDryReasons.Count > 0)
                    return new Decision.Noop(new NoopReason.CannotDry(snapshot.CannotDryReasons));

                var sinceStop = now - _lastStopIssuedAt;
                if (sinceStop < minOffTime)
                    return new Decision.Noop(new NoopReason.WaitingAfterStop(CeilSeconds(minOffTime - sinceStop)));

                var sinceStart = now - _lastStartIssuedAt;
                if (sinceStart < CommandRetryInterval)
                    return new Decision.Noop(new NoopReason.StartInFlight(CeilSeconds(CommandRetryInterval - sinceStart)));

                _lastStartIssuedAt = now;
                return new Decision.Start(
                    AmsId: AmsId,
                    Filament: snapshot.DominantFilamentType ?? "PLA",
                    TempC: settings.TargetTemp,
                    Hours: DryHours);
            }

            // Falling edge: humidity low, AMS drying → consider STOP
            if (rh <= settings.LowThreshold && snapshot.IsCurrentlyDrying)
            {
                var sinceStart = now - _lastStartIssuedAt;
                if (sinceStart < minOnTime)
                    return new Decision.Noop(new NoopReason.WaitingAfterStart(CeilSeconds(minOnTime - sinceStart)));

                var sinceStop = now - _lastStopIssuedAt;
                if (sinceStop < CommandRetryInterval)
                    return new Decision.Noop(new NoopReason.StopInFlight(CeilSeconds(CommandRetryInterval - sinceStop)));

                _lastStopIssuedAt = now;
                return new Decision.Stop(AmsId);
            }

            return new Decision.Noop(new NoopReason.WithinHysteresisBand());
        }
    }

    private static int CeilSeconds(TimeSpan t) => (int)Math.Ceiling(t.TotalSeconds);

    // Test / persistence helpers
    public void SetLastStartIssuedAt(DateTime t) { lock (_lock) _lastStartIssuedAt = t; }
    public void SetLastStopIssuedAt(DateTime t)  { lock (_lock) _lastStopIssuedAt = t; }
    public DateTime GetLastStartIssuedAt() { lock (_lock) return _lastStartIssuedAt; }
    public DateTime GetLastStopIssuedAt()  { lock (_lock) return _lastStopIssuedAt; }
}
