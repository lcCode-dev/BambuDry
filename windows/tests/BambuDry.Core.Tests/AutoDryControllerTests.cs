using BambuDry.Core;
using BambuDry.Core.Control;

namespace BambuDry.Core.Tests;

public class AutoDryControllerTests
{
    private static AutoDrySettings MakeSettings(
        bool enabled = true,
        int high = 35,
        int low = 20,
        int temp = 45,
        bool runDuringPrint = false,
        int minOnMinutes = 30,        // tests still assume long min-on for anti-cycle assertions
        int minOffMinutes = 5)
        => new()
        {
            Enabled = enabled,
            HighThreshold = high,
            LowThreshold = low,
            TargetTemp = temp,
            RunDuringPrint = runDuringPrint,
            MinOnMinutes = minOnMinutes,
            MinOffMinutes = minOffMinutes,
        };

    private static AmsSnapshot MakeSnapshot(
        int? rh = 30,
        bool drying = false,
        AmsModel model = AmsModel.N3F,
        IReadOnlyList<CannotDryReason>? cannotReasons = null,
        string? filament = "PLA")
        => new(
            AmsId: 0,
            Model: model,
            HumidityPercent: rh,
            IsCurrentlyDrying: drying,
            CannotDryReasons: cannotReasons,
            DominantFilamentType: filament);

    /// <summary>Mutable date sequencer so tests can advance "now" deterministically.</summary>
    private sealed class TestClock
    {
        public DateTime Now { get; set; } = DateTime.UnixEpoch.AddSeconds(1_700_000_000);
        public Func<DateTime> Read => () => Now;
        public void Advance(TimeSpan span) => Now = Now.Add(span);
        public void Advance(double seconds) => Advance(TimeSpan.FromSeconds(seconds));
    }

    // MARK: - Rising edge

    [Fact]
    public void StartsDryingWhenAboveHighThreshold()
    {
        var clock = new TestClock();
        var c = new AutoDryController(amsId: 0, clock: clock.Read);
        // First call is at DateTime.MinValue cooldown, so minOffTime is satisfied.
        var decision = c.Evaluate(MakeSnapshot(rh: 40), MakeSettings(), printerIsPrinting: false);
        Assert.Equal(new Decision.Start(AmsId: 0, Filament: "PLA", TempC: 45, Hours: 1), decision);
    }

    [Fact]
    public void WithinBandIsNoop()
    {
        var c = new AutoDryController(amsId: 0);
        var decision = c.Evaluate(MakeSnapshot(rh: 25), MakeSettings(), printerIsPrinting: false);
        Assert.Equal(new Decision.Noop(new NoopReason.WithinHysteresisBand()), decision);
    }

    [Fact]
    public void StartFiresExactlyAtThreshold()
    {
        var c = new AutoDryController(amsId: 0);
        var decision = c.Evaluate(MakeSnapshot(rh: 35), MakeSettings(high: 35, low: 20), printerIsPrinting: false);
        Assert.IsType<Decision.Start>(decision);
    }

    // MARK: - Falling edge

    [Fact]
    public void StopsDryingWhenBelowLowThreshold()
    {
        var clock = new TestClock();
        var c = new AutoDryController(amsId: 0, clock: clock.Read);
        // Pretend we started drying 1 hour ago.
        c.SetLastStartIssuedAt(clock.Now.AddSeconds(-3600));
        var decision = c.Evaluate(MakeSnapshot(rh: 18, drying: true), MakeSettings(), printerIsPrinting: false);
        Assert.Equal(new Decision.Stop(AmsId: 0), decision);
    }

    // MARK: - Anti-cycling

    [Fact]
    public void MinOnTimeBlocksEarlyStop()
    {
        var clock = new TestClock();
        var c = new AutoDryController(amsId: 0, clock: clock.Read);
        c.SetLastStartIssuedAt(clock.Now);    // Just turned on.
        clock.Advance(60);                     // Only 1 minute elapsed.
        var decision = c.Evaluate(MakeSnapshot(rh: 18, drying: true), MakeSettings(), printerIsPrinting: false);
        // Default minOnTime = 30 min (1800s), 60s elapsed → 1740s remaining.
        Assert.Equal(new Decision.Noop(new NoopReason.WaitingAfterStart(SecondsRemaining: 1740)), decision);
    }

    [Fact]
    public void MinOffTimeBlocksEarlyStart()
    {
        var clock = new TestClock();
        var c = new AutoDryController(amsId: 0, clock: clock.Read);
        c.SetLastStopIssuedAt(clock.Now);
        clock.Advance(60);
        var decision = c.Evaluate(MakeSnapshot(rh: 80), MakeSettings(), printerIsPrinting: false);
        // Default minOffTime = 5 min (300s), 60s elapsed → 240s remaining.
        Assert.Equal(new Decision.Noop(new NoopReason.WaitingAfterStop(SecondsRemaining: 240)), decision);
    }

    [Fact]
    public void StartInFlightDebouncesDuplicateStarts()
    {
        var clock = new TestClock();
        var c = new AutoDryController(amsId: 0, clock: clock.Read);
        var first = c.Evaluate(MakeSnapshot(rh: 80, drying: false), MakeSettings(), printerIsPrinting: false);
        Assert.IsType<Decision.Start>(first);

        clock.Advance(5);
        var second = c.Evaluate(MakeSnapshot(rh: 80, drying: false), MakeSettings(), printerIsPrinting: false);
        // 30s retry window, 5s elapsed → 25s remaining.
        Assert.Equal(new Decision.Noop(new NoopReason.StartInFlight(SecondsRemaining: 25)), second);

        clock.Advance(31);
        var third = c.Evaluate(MakeSnapshot(rh: 80, drying: false), MakeSettings(), printerIsPrinting: false);
        Assert.IsType<Decision.Start>(third);
    }

    // MARK: - Gates

    [Fact]
    public void CannotDryReasonsBlockStart()
    {
        var c = new AutoDryController(amsId: 0);
        var decision = c.Evaluate(
            MakeSnapshot(rh: 80, cannotReasons: new[] { CannotDryReason.AmsBusy }),
            MakeSettings(),
            printerIsPrinting: false);
        Assert.Equal(new Decision.Noop(new NoopReason.CannotDry(new[] { CannotDryReason.AmsBusy })), decision);
    }

    [Fact]
    public void RunDuringPrintGate()
    {
        var c = new AutoDryController(amsId: 0);
        var snap = MakeSnapshot(rh: 80);

        var blocked = c.Evaluate(snap, MakeSettings(runDuringPrint: false), printerIsPrinting: true);
        Assert.Equal(new Decision.Noop(new NoopReason.Printing()), blocked);

        var allowed = c.Evaluate(snap, MakeSettings(runDuringPrint: true), printerIsPrinting: true);
        Assert.IsType<Decision.Start>(allowed);
    }

    [Fact]
    public void UnsupportedAmsIsNoop()
    {
        var c = new AutoDryController(amsId: 0);
        var snap = MakeSnapshot(rh: 80, model: AmsModel.Ams);   // Original AMS — no drying.
        var decision = c.Evaluate(snap, MakeSettings(), printerIsPrinting: false);
        Assert.Equal(new Decision.Noop(new NoopReason.UnsupportedAms()), decision);
    }

    [Fact]
    public void HumidityUnknownIsNoop()
    {
        var c = new AutoDryController(amsId: 0);
        var snap = MakeSnapshot(rh: null);
        var decision = c.Evaluate(snap, MakeSettings(), printerIsPrinting: false);
        Assert.Equal(new Decision.Noop(new NoopReason.HumidityUnknown()), decision);
    }

    [Fact]
    public void DisabledIsNoop()
    {
        var c = new AutoDryController(amsId: 0);
        var decision = c.Evaluate(MakeSnapshot(rh: 80), MakeSettings(enabled: false), printerIsPrinting: false);
        Assert.Equal(new Decision.Noop(new NoopReason.Disabled()), decision);
    }

    [Fact]
    public void InvalidSettingsIsNoop()
    {
        var c = new AutoDryController(amsId: 0);
        // low >= high → invalid
        var decision = c.Evaluate(MakeSnapshot(rh: 80), MakeSettings(high: 20, low: 35), printerIsPrinting: false);
        Assert.Equal(new Decision.Noop(new NoopReason.SettingsInvalid()), decision);
    }

    // MARK: - Filament fallback

    [Fact]
    public void StartsWithDefaultFilamentWhenUnknown()
    {
        var c = new AutoDryController(amsId: 0);
        var snap = MakeSnapshot(rh: 80, filament: null);
        var decision = c.Evaluate(snap, MakeSettings(), printerIsPrinting: false);
        Assert.Equal(new Decision.Start(AmsId: 0, Filament: "PLA", TempC: 45, Hours: 1), decision);
    }
}
