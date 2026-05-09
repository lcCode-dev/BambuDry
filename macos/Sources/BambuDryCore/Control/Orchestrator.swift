import Foundation

/// Connects a LANTransport to one AutoDryController per AMS unit on a printer.
///
/// On each report:
///   1. Decode the AMS array
///   2. For each AMS, derive an AMSSnapshot (auto-detecting model from `info`)
///   3. Throttle per-AMS evaluation to once per `tickInterval`
///   4. Call AutoDryController.evaluate()
///   5. Publish resulting DryRequest (or just log it, in dry-run mode)
///
/// One-orchestrator-per-printer; multiple printers run independent instances.
public actor Orchestrator {

    public struct Config: Sendable {
        public var settingsByAmsId: [Int: AutoDrySettings]   // applies to AMS by id (0, 128, 129…)
        public var defaultSettings: AutoDrySettings           // for AMS not in the map
        public var dryRun: Bool
        public var tickInterval: TimeInterval
        public var pushAllInterval: TimeInterval               // re-request full status periodically
        public init(settingsByAmsId: [Int: AutoDrySettings] = [:],
                    defaultSettings: AutoDrySettings = .init(),
                    dryRun: Bool = true,
                    tickInterval: TimeInterval = 5,
                    pushAllInterval: TimeInterval = 60) {
            self.settingsByAmsId = settingsByAmsId
            self.defaultSettings = defaultSettings
            self.dryRun = dryRun
            self.tickInterval = tickInterval
            self.pushAllInterval = pushAllInterval
        }
    }

    public struct Event: Sendable {
        public enum Kind: Sendable {
            case observed(AMSSnapshot)
            case decision(amsId: Int, AutoDryController.Decision)
            case sent(amsId: Int, json: String)
            case dryRunSkipped(amsId: Int, AutoDryController.Decision)
            case error(String)
        }
        public let timestamp: Date
        public let kind: Kind
    }

    private let transport: LANTransport
    private var config: Config

    private var controllers: [Int: AutoDryController] = [:]
    private var lastEvalAt: [Int: Date] = [:]
    private var lastPushAllAt: Date = .distantPast

    private var eventContinuation: AsyncStream<Event>.Continuation?

    public init(transport: LANTransport, config: Config) {
        self.transport = transport
        self.config = config
    }

    // MARK: - Live config mutation
    //
    // The orchestrator is long-lived; the user can change settings from the
    // menu bar UI after we're already running. These setters let the host
    // app push updates without rebuilding the whole pipeline.

    public func setSettings(_ settings: AutoDrySettings, forAmsId id: Int) {
        config.settingsByAmsId[id] = settings
    }

    public func setDefaultSettings(_ settings: AutoDrySettings) {
        config.defaultSettings = settings
    }

    public func setDryRun(_ dryRun: Bool) {
        config.dryRun = dryRun
    }

    public func events() -> AsyncStream<Event> {
        AsyncStream { continuation in
            self.eventContinuation = continuation
            continuation.onTermination = { [weak self] _ in
                Task { await self?.clearEventContinuation() }
            }
        }
    }

    private func clearEventContinuation() {
        eventContinuation = nil
    }

    private func emit(_ kind: Event.Kind) {
        eventContinuation?.yield(Event(timestamp: Date(), kind: kind))
    }

    /// Drive the loop: connect, request pushall, then process every report.
    public func run() async throws {
        try await transport.connect()
        try transport.requestPushAll()
        lastPushAllAt = Date()

        for await envelope in transport.reportStream() {
            await ingest(envelope)
            await maybeRefresh()
        }
    }

    private func maybeRefresh() async {
        let now = Date()
        if now.timeIntervalSince(lastPushAllAt) >= config.pushAllInterval {
            do { try transport.requestPushAll() } catch {
                emit(.error("pushAll failed: \(error)"))
            }
            lastPushAllAt = now
        }
    }

    private func ingest(_ envelope: ReportEnvelope) async {
        guard let amsList = envelope.print?.ams?.ams else { return }
        let isPrinting = (envelope.print?.gcode_state ?? "") == "RUNNING"
            || (envelope.print?.gcode_state ?? "") == "PRINTING"
            || (envelope.print?.gcode_state ?? "") == "PAUSE"

        for ams in amsList {
            guard let snapshot = ams.snapshot() else { continue }
            emit(.observed(snapshot))

            // Throttle per-AMS evaluation
            let now = Date()
            if let last = lastEvalAt[snapshot.amsId],
               now.timeIntervalSince(last) < config.tickInterval { continue }
            lastEvalAt[snapshot.amsId] = now

            // One controller per AMS, lazily created
            let controller = controllers[snapshot.amsId] ?? AutoDryController(amsId: snapshot.amsId)
            controllers[snapshot.amsId] = controller

            let settings = config.settingsByAmsId[snapshot.amsId] ?? config.defaultSettings
            let decision = await controller.evaluate(snapshot: snapshot,
                                                     settings: settings,
                                                     printerIsPrinting: isPrinting)
            emit(.decision(amsId: snapshot.amsId, decision))

            switch decision {
            case .start(let amsId, let filament, let tempC, let hours):
                if config.dryRun {
                    emit(.dryRunSkipped(amsId: amsId, decision))
                } else {
                    do {
                        let json = try transport.startDrying(amsId: amsId,
                                                             filament: filament,
                                                             tempC: tempC,
                                                             hours: hours)
                        emit(.sent(amsId: amsId, json: json))
                    } catch {
                        emit(.error("start publish failed: \(error)"))
                    }
                }
            case .stop(let amsId):
                if config.dryRun {
                    emit(.dryRunSkipped(amsId: amsId, decision))
                } else {
                    do {
                        let json = try transport.stopDrying(amsId: amsId)
                        emit(.sent(amsId: amsId, json: json))
                    } catch {
                        emit(.error("stop publish failed: \(error)"))
                    }
                }
            case .noop:
                break
            }
        }
    }
}
