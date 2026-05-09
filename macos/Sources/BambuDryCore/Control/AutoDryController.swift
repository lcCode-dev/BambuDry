import Foundation

/// Hysteresis state machine for one AMS unit.
///
/// Behaviour:
/// - Issue START when humidity ≥ `highThreshold`, AMS isn't already drying,
///   nothing in `cannotDryReasons`, anti-cycling time elapsed, and we haven't
///   already sent a START in the last `commandRetryInterval`.
/// - Issue STOP when humidity ≤ `lowThreshold`, AMS is currently drying,
///   `minOnTime` has elapsed since the last START, and we haven't already
///   sent a STOP in the last `commandRetryInterval`.
/// - Skip everything if `runDuringPrint` is false and the printer is printing.
///
/// The "in-flight retry" window means: if a START is published but the printer
/// hasn't begun reporting `isCurrentlyDrying = true` yet, we won't spam more
/// STARTs every 5s. After the retry window we *will* re-issue, in case the
/// command was lost.
public actor AutoDryController {

    public enum Decision: Sendable, Equatable {
        case start(amsId: Int, filament: String, tempC: Int, hours: Int)
        case stop(amsId: Int)
        case noop(reason: NoopReason)
    }

    public enum NoopReason: Sendable, Equatable {
        case disabled
        case unsupportedAMS
        case humidityUnknown
        case printing
        case withinHysteresisBand
        /// Anti-cycling: we just stopped, not allowed to start again yet.
        /// `secondsRemaining` counts down to when we'd permit a start.
        case waitingAfterStop(secondsRemaining: Int)
        /// Anti-cycling: we just started, not allowed to stop again yet.
        case waitingAfterStart(secondsRemaining: Int)
        /// We sent a START recently and are waiting for the printer to ack.
        case startInFlight(secondsRemaining: Int)
        /// We sent a STOP recently and are waiting for the printer to ack.
        case stopInFlight(secondsRemaining: Int)
        case cannotDry([CannotDryReason])
        case settingsInvalid
    }

    public let amsId: Int
    /// Re-issue same command if this elapses without the printer acking it.
    /// Not user-facing; protects against lost commands.
    public let commandRetryInterval: TimeInterval
    public let dryHours: Int

    private var lastStartIssuedAt: Date
    private var lastStopIssuedAt: Date
    private let clock: @Sendable () -> Date

    public init(amsId: Int,
                commandRetryInterval: TimeInterval = 30,
                dryHours: Int = 1,
                clock: @Sendable @escaping () -> Date = { Date() }) {
        self.amsId = amsId
        self.commandRetryInterval = commandRetryInterval
        self.dryHours = dryHours
        self.clock = clock
        self.lastStartIssuedAt = .distantPast
        self.lastStopIssuedAt = .distantPast
    }

    public func evaluate(snapshot: AMSSnapshot,
                         settings: AutoDrySettings,
                         printerIsPrinting: Bool) -> Decision {

        guard settings.enabled else { return .noop(reason: .disabled) }
        guard settings.isValid else { return .noop(reason: .settingsInvalid) }
        guard snapshot.supportsDrying else { return .noop(reason: .unsupportedAMS) }
        guard let rh = snapshot.humidityPercent else { return .noop(reason: .humidityUnknown) }
        if printerIsPrinting && !settings.runDuringPrint { return .noop(reason: .printing) }

        let now = clock()
        let minOnTime  = TimeInterval(settings.minOnMinutes  * 60)
        let minOffTime = TimeInterval(settings.minOffMinutes * 60)

        // If the AMS reports it's already drying, the printer has acted on (or
        // beat us to) a start command — anchor lastStart to now so future
        // stop-time gating works from the actual drying-start moment.
        if snapshot.isCurrentlyDrying && lastStartIssuedAt == .distantPast {
            lastStartIssuedAt = now
        }

        // Rising edge: humidity high, AMS not drying → consider START
        if rh >= settings.highThreshold, !snapshot.isCurrentlyDrying {
            if !snapshot.cannotDryReasons.isEmpty {
                return .noop(reason: .cannotDry(snapshot.cannotDryReasons))
            }
            // Don't restart immediately after a stop (anti-cycling)
            let sinceStop = now.timeIntervalSince(lastStopIssuedAt)
            if sinceStop < minOffTime {
                return .noop(reason: .waitingAfterStop(secondsRemaining: Int(ceil(minOffTime - sinceStop))))
            }
            // If we already issued a START recently, wait for printer to ack
            // (or for retry window to elapse, in case the command was lost).
            let sinceStart = now.timeIntervalSince(lastStartIssuedAt)
            if sinceStart < commandRetryInterval {
                return .noop(reason: .startInFlight(secondsRemaining: Int(ceil(commandRetryInterval - sinceStart))))
            }
            lastStartIssuedAt = now
            return .start(amsId: amsId,
                          filament: snapshot.dominantFilamentType ?? "PLA",
                          tempC: settings.targetTemp,
                          hours: dryHours)
        }

        // Falling edge: humidity low, AMS drying → consider STOP
        if rh <= settings.lowThreshold, snapshot.isCurrentlyDrying {
            // Don't stop immediately after a start (let it actually dry)
            let sinceStart = now.timeIntervalSince(lastStartIssuedAt)
            if sinceStart < minOnTime {
                return .noop(reason: .waitingAfterStart(secondsRemaining: Int(ceil(minOnTime - sinceStart))))
            }
            // Don't re-issue STOP if we just sent one
            let sinceStop = now.timeIntervalSince(lastStopIssuedAt)
            if sinceStop < commandRetryInterval {
                return .noop(reason: .stopInFlight(secondsRemaining: Int(ceil(commandRetryInterval - sinceStop))))
            }
            lastStopIssuedAt = now
            return .stop(amsId: amsId)
        }

        return .noop(reason: .withinHysteresisBand)
    }

    // MARK: - Test / persistence helpers

    public func setLastStartIssuedAt(_ date: Date) { lastStartIssuedAt = date }
    public func setLastStopIssuedAt(_ date: Date)  { lastStopIssuedAt = date }
    public func getLastStartIssuedAt() -> Date { lastStartIssuedAt }
    public func getLastStopIssuedAt() -> Date { lastStopIssuedAt }
}
