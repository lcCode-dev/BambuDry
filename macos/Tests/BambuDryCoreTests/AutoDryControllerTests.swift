import XCTest
@testable import BambuDryCore

final class AutoDryControllerTests: XCTestCase {

    private func makeSettings(
        enabled: Bool = true,
        high: Int = 35,
        low: Int = 20,
        temp: Int = 45,
        runDuringPrint: Bool = false,
        minOnMinutes: Int = 30,        // tests still assume long min-on for anti-cycle assertions
        minOffMinutes: Int = 5
    ) -> AutoDrySettings {
        AutoDrySettings(enabled: enabled,
                        highThreshold: high,
                        lowThreshold: low,
                        targetTemp: temp,
                        runDuringPrint: runDuringPrint,
                        minOnMinutes: minOnMinutes,
                        minOffMinutes: minOffMinutes)
    }

    private func makeSnapshot(
        rh: Int? = 30,
        drying: Bool = false,
        model: AMSModel = .n3f,
        cannotReasons: [CannotDryReason] = [],
        filament: String? = "PLA"
    ) -> AMSSnapshot {
        AMSSnapshot(amsId: 0,
                    model: model,
                    humidityPercent: rh,
                    isCurrentlyDrying: drying,
                    cannotDryReasons: cannotReasons,
                    dominantFilamentType: filament)
    }

    /// Mutable date sequencer so tests can advance "now" deterministically.
    private final class Clock: @unchecked Sendable {
        var now: Date = Date(timeIntervalSince1970: 1_700_000_000)
        var read: @Sendable () -> Date { { [self] in self.now } }
        func advance(_ seconds: TimeInterval) { now = now.addingTimeInterval(seconds) }
    }

    // MARK: - Rising edge

    func testStartsDryingWhenAboveHighThreshold() async {
        let clock = Clock()
        let c = AutoDryController(amsId: 0, clock: clock.read)
        // First call is at distantPast cooldown, so minOffTime is satisfied.
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 40),
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .start(amsId: 0, filament: "PLA", tempC: 45, hours: 1))
    }

    func testWithinBandIsNoop() async {
        let c = AutoDryController(amsId: 0)
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 25),
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .noop(reason: .withinHysteresisBand))
    }

    func testStartFiresExactlyAtThreshold() async {
        let c = AutoDryController(amsId: 0)
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 35),
                                        settings: makeSettings(high: 35, low: 20),
                                        printerIsPrinting: false)
        if case .start = decision { /* ok */ } else {
            XCTFail("expected start, got \(decision)")
        }
    }

    // MARK: - Falling edge

    func testStopsDryingWhenBelowLowThreshold() async {
        let clock = Clock()
        let c = AutoDryController(amsId: 0, clock: clock.read)
        // Pretend we started drying 1 hour ago.
        await c.setLastStartIssuedAt(clock.now.addingTimeInterval(-3600))
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 18, drying: true),
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .stop(amsId: 0))
    }

    // MARK: - Anti-cycling

    func testMinOnTimeBlocksEarlyStop() async {
        let clock = Clock()
        let c = AutoDryController(amsId: 0, clock: clock.read)
        await c.setLastStartIssuedAt(clock.now)    // Just turned on.
        clock.advance(60)                          // Only 1 minute elapsed.
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 18, drying: true),
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        // Default minOnTime = 30 min (1800s), 60s elapsed → 1740s remaining.
        guard case .noop(reason: .waitingAfterStart(let s)) = decision else {
            return XCTFail("expected waitingAfterStart, got \(decision)")
        }
        XCTAssertEqual(s, 1740)
    }

    func testMinOffTimeBlocksEarlyStart() async {
        let clock = Clock()
        let c = AutoDryController(amsId: 0, clock: clock.read)
        await c.setLastStopIssuedAt(clock.now)
        clock.advance(60)
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 80),
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        // Default minOffTime = 5 min (300s), 60s elapsed → 240s remaining.
        guard case .noop(reason: .waitingAfterStop(let s)) = decision else {
            return XCTFail("expected waitingAfterStop, got \(decision)")
        }
        XCTAssertEqual(s, 240)
    }

    func testStartInFlightDebouncesDuplicateStarts() async {
        let clock = Clock()
        let c = AutoDryController(amsId: 0, clock: clock.read)
        let first = await c.evaluate(snapshot: makeSnapshot(rh: 80, drying: false),
                                     settings: makeSettings(),
                                     printerIsPrinting: false)
        if case .start = first { /* ok */ } else { XCTFail("expected start, got \(first)") }
        clock.advance(5)
        let second = await c.evaluate(snapshot: makeSnapshot(rh: 80, drying: false),
                                      settings: makeSettings(),
                                      printerIsPrinting: false)
        guard case .noop(reason: .startInFlight(let s)) = second else {
            return XCTFail("expected startInFlight, got \(second)")
        }
        XCTAssertEqual(s, 25)  // 30s retry window, 5s elapsed
        clock.advance(31)
        let third = await c.evaluate(snapshot: makeSnapshot(rh: 80, drying: false),
                                     settings: makeSettings(),
                                     printerIsPrinting: false)
        if case .start = third { /* ok */ } else { XCTFail("expected start retry, got \(third)") }
    }

    // MARK: - Gates

    func testCannotDryReasonsBlockStart() async {
        let c = AutoDryController(amsId: 0)
        let decision = await c.evaluate(
            snapshot: makeSnapshot(rh: 80, cannotReasons: [.amsBusy]),
            settings: makeSettings(),
            printerIsPrinting: false)
        XCTAssertEqual(decision, .noop(reason: .cannotDry([.amsBusy])))
    }

    func testRunDuringPrintGate() async {
        let c = AutoDryController(amsId: 0)
        let snap = makeSnapshot(rh: 80)
        let blocked = await c.evaluate(snapshot: snap,
                                       settings: makeSettings(runDuringPrint: false),
                                       printerIsPrinting: true)
        XCTAssertEqual(blocked, .noop(reason: .printing))

        let allowed = await c.evaluate(snapshot: snap,
                                       settings: makeSettings(runDuringPrint: true),
                                       printerIsPrinting: true)
        if case .start = allowed { /* ok */ } else {
            XCTFail("expected start when runDuringPrint=true, got \(allowed)")
        }
    }

    func testUnsupportedAMSIsNoop() async {
        let c = AutoDryController(amsId: 0)
        let snap = makeSnapshot(rh: 80, model: .ams)   // Original AMS — no drying.
        let decision = await c.evaluate(snapshot: snap,
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .noop(reason: .unsupportedAMS))
    }

    func testHumidityUnknownIsNoop() async {
        let c = AutoDryController(amsId: 0)
        let snap = makeSnapshot(rh: nil)
        let decision = await c.evaluate(snapshot: snap,
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .noop(reason: .humidityUnknown))
    }

    func testDisabledIsNoop() async {
        let c = AutoDryController(amsId: 0)
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 80),
                                        settings: makeSettings(enabled: false),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .noop(reason: .disabled))
    }

    func testInvalidSettingsIsNoop() async {
        let c = AutoDryController(amsId: 0)
        // low >= high → invalid
        let decision = await c.evaluate(snapshot: makeSnapshot(rh: 80),
                                        settings: makeSettings(high: 20, low: 35),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .noop(reason: .settingsInvalid))
    }

    // MARK: - Filament fallback

    func testStartsWithDefaultFilamentWhenUnknown() async {
        let c = AutoDryController(amsId: 0)
        let snap = makeSnapshot(rh: 80, filament: nil)
        let decision = await c.evaluate(snapshot: snap,
                                        settings: makeSettings(),
                                        printerIsPrinting: false)
        XCTAssertEqual(decision, .start(amsId: 0, filament: "PLA", tempC: 45, hours: 1))
    }
}
