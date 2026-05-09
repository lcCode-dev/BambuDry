import Foundation
import SwiftUI
import BambuDryCore

@MainActor
final class AppModel: ObservableObject {

    enum Status: Equatable {
        case notConfigured
        case connecting
        case connected
        case disconnected(reason: String)
    }

    @Published var config: AppConfig
    @Published var status: Status = .notConfigured
    @Published var snapshots: [Int: AMSSnapshot] = [:]   // keyed by amsId
    @Published var lastDecisions: [Int: AutoDryController.Decision] = [:]
    @Published var publishLog: [String] = []             // last few sent commands
    @Published var dryRun: Bool = false                   // exposed in Settings; default LIVE

    private var transport: LANTransport?
    private var orchestrator: Orchestrator?
    private var runTask: Task<Void, Never>?

    init() {
        var loaded = Storage.loadConfig() ?? .empty
        // Bring any out-of-range values from older app versions into the
        // current UI ranges so labels and slider positions always agree.
        loaded.defaultSettings.sanitize()
        for (key, var s) in loaded.amsSettings {
            s.sanitize()
            loaded.amsSettings[key] = s
        }
        self.config = loaded
        // Re-save once if sanitize changed anything (cheap; idempotent).
        try? Storage.saveConfig(loaded)

        if !config.serial.isEmpty,
           !config.lanIP.isEmpty,
           Storage.loadAccessCode(forSerial: config.serial) != nil {
            self.status = .connecting
            // Kick off the connection right now, on app launch — don't wait
            // for the user to click the menu bar icon. The dropdown's onAppear
            // only fires when the dropdown is opened.
            Task { @MainActor [weak self] in
                self?.start()
            }
        }
    }

    var isConfigured: Bool {
        !config.serial.isEmpty
            && !config.lanIP.isEmpty
            && Storage.loadAccessCode(forSerial: config.serial) != nil
    }

    /// Persist config + access code, then (re)connect.
    func saveAndConnect(_ updated: AppConfig, accessCode: String) {
        do {
            try Storage.saveAccessCode(accessCode, forSerial: updated.serial)
            try Storage.saveConfig(updated)
            self.config = updated
            stop()
            start()
        } catch {
            status = .disconnected(reason: "save failed: \(error.localizedDescription)")
        }
    }

    /// Persist settings only (no reconnect).
    func saveConfig() {
        try? Storage.saveConfig(config)
    }

    /// Replace settings for one AMS unit and persist.
    func updateSettings(amsId: Int, _ block: (inout AutoDrySettings) -> Void) {
        var s = config.settings(forAmsId: amsId)
        block(&s)
        config.setSettings(s, forAmsId: amsId)
        saveConfig()
        // Push the change to the live orchestrator so the controller sees it
        // on the next tick — without rebuilding the whole pipeline.
        if let orch = orchestrator {
            Task { await orch.setSettings(s, forAmsId: amsId) }
        }
    }

    /// Replace default settings (applied to AMS units without per-id overrides).
    func updateDefaultSettings(_ block: (inout AutoDrySettings) -> Void) {
        var s = config.defaultSettings
        block(&s)
        config.defaultSettings = s
        saveConfig()
        if let orch = orchestrator {
            Task { await orch.setDefaultSettings(s) }
        }
    }

    func setDryRun(_ dryRun: Bool) {
        self.dryRun = dryRun
        if let orch = orchestrator {
            Task { await orch.setDryRun(dryRun) }
        }
    }

    /// Immediately publish a STOP for the given AMS, bypassing the controller
    /// and anti-cycling gates. Also disables auto-dry for that AMS so the
    /// controller doesn't re-fire a START on the next tick when humidity is
    /// still above threshold. User intent: "stop it and stop managing it."
    func stopDryingNow(amsId: Int) {
        guard let transport = transport else { return }
        updateSettings(amsId: amsId) { $0.enabled = false }
        do {
            let json = try transport.stopDrying(amsId: amsId)
            publishLog.append("[\(timestamp())] manual stop ams=\(amsId) → \(json)")
            publishLog = Array(publishLog.suffix(20))
        } catch {
            publishLog.append("[\(timestamp())] manual stop FAILED ams=\(amsId): \(error)")
        }
    }

    /// Immediately publish a START for the given AMS, bypassing anti-cycling.
    /// Useful for "test now" buttons.
    func startDryingNow(amsId: Int, filament: String, tempC: Int, hours: Int) {
        guard let transport = transport else { return }
        do {
            let json = try transport.startDrying(amsId: amsId,
                                                 filament: filament,
                                                 tempC: tempC,
                                                 hours: hours)
            publishLog.append("[\(timestamp())] manual start ams=\(amsId) → \(json)")
            publishLog = Array(publishLog.suffix(20))
        } catch {
            publishLog.append("[\(timestamp())] manual start FAILED ams=\(amsId): \(error)")
        }
    }

    func start() {
        guard isConfigured else { return }
        runTask?.cancel()
        runTask = Task { [weak self] in
            await self?.runConnectionLoop()
        }
    }

    /// Connection loop with exponential-backoff retries. Bambu's printer broker
    /// will sometimes drop the first connect after fresh credentials are
    /// entered (broker hasn't fully released a prior session, transient TLS
    /// stutter, etc). We rebuild a fresh transport + orchestrator on each
    /// retry rather than trusting CocoaMQTT's internal autoReconnect, since
    /// that doesn't propagate failures back through our async run() call.
    private func runConnectionLoop() async {
        let backoffSeconds = [2, 5, 10, 20, 30, 60]
        var attempt = 0

        while !Task.isCancelled {
            guard let code = Storage.loadAccessCode(forSerial: config.serial) else { return }

            let transport = LANTransport(config: .init(
                host: config.lanIP,
                serial: config.serial,
                accessCode: code))
            let orch = Orchestrator(transport: transport, config: .init(
                settingsByAmsId: Dictionary(uniqueKeysWithValues:
                    config.amsSettings.compactMap { kv -> (Int, AutoDrySettings)? in
                        guard let id = Int(kv.key) else { return nil }
                        return (id, kv.value)
                    }),
                defaultSettings: config.defaultSettings,
                dryRun: dryRun))

            await MainActor.run {
                self.transport = transport
                self.orchestrator = orch
                self.status = .connecting
            }

            let eventTask = Task { [weak self] in
                guard let self else { return }
                for await event in await orch.events() {
                    await self.handle(event)
                }
            }

            do {
                try await orch.run()
                // Clean stream end (rare — usually means we're shutting down).
                eventTask.cancel()
                return
            } catch {
                eventTask.cancel()
                if Task.isCancelled { return }

                attempt += 1
                let delay = backoffSeconds[min(attempt - 1, backoffSeconds.count - 1)]
                await MainActor.run {
                    self.status = .disconnected(reason: "\(error) — retry in \(delay)s")
                }
                try? await Task.sleep(nanoseconds: UInt64(delay) * 1_000_000_000)
            }
        }
    }

    func stop() {
        runTask?.cancel()
        runTask = nil
        transport?.disconnect()
        transport = nil
        orchestrator = nil
        snapshots.removeAll()
        lastDecisions.removeAll()
    }

    private func handle(_ event: Orchestrator.Event) async {
        await MainActor.run {
            switch event.kind {
            case .observed(let snap):
                self.snapshots[snap.amsId] = snap
                if self.status != .connected { self.status = .connected }
            case .decision(let id, let d):
                self.lastDecisions[id] = d
            case .sent(let id, let json):
                self.publishLog.append("[\(self.timestamp())] ams=\(id) → \(json)")
                self.publishLog = Array(self.publishLog.suffix(20))
            case .dryRunSkipped(let id, _):
                self.publishLog.append("[\(self.timestamp())] dry-run ams=\(id)")
                self.publishLog = Array(self.publishLog.suffix(20))
            case .error(let msg):
                self.status = .disconnected(reason: msg)
            }
        }
    }

    private func timestamp() -> String {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f.string(from: Date())
    }

    var sortedSnapshots: [AMSSnapshot] {
        snapshots.values.sorted { $0.amsId < $1.amsId }
    }

    /// Aggregate state for the menu bar icon.
    var menuBarState: MenuBarState {
        let snaps = snapshots.values
        if snaps.contains(where: { $0.isCurrentlyDrying }) { return .drying }
        if snaps.contains(where: { snap in
            let s = config.settings(forAmsId: snap.amsId)
            guard s.enabled, let rh = snap.humidityPercent else { return false }
            return rh >= s.highThreshold
        }) { return .humidAlert }
        if status == .connecting { return .connecting }
        if case .disconnected = status { return .offline }
        return .ok
    }
}

enum MenuBarState {
    case ok, drying, humidAlert, connecting, offline

    var systemImage: String {
        switch self {
        case .ok:          return "humidity"
        case .drying:      return "flame.fill"
        case .humidAlert:  return "humidity.fill"
        case .connecting:  return "ellipsis.circle"
        case .offline:     return "wifi.slash"
        }
    }
}
