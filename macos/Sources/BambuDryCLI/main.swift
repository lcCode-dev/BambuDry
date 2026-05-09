import Foundation
import BambuDryCore

// MARK: - Argv parsing
//
// Usage:
//   bambudry-cli --ip <printer_ip> --serial <serial> --code <access_code>
//                [--dry-run | --live]
//                [--high <RH%>] [--low <RH%>] [--temp <°C>]
//                [--enabled] [--during-print]
//                [--minutes <N>]   # exit after N minutes (default: run forever)

struct Args {
    var ip: String?
    var serial: String?
    var code: String?
    var dryRun: Bool = true
    var enabled: Bool = false
    var high: Int = 35
    var low: Int = 20
    var temp: Int = 45
    var runDuringPrint: Bool = false
    var minutes: Int? = nil

    static func parse(_ argv: [String]) -> Args {
        var a = Args()
        var i = 1
        while i < argv.count {
            let arg = argv[i]
            func next() -> String {
                guard i + 1 < argv.count else {
                    FileHandle.standardError.write(Data("missing value for \(arg)\n".utf8))
                    exit(2)
                }
                i += 1
                return argv[i]
            }
            switch arg {
            case "--ip":            a.ip = next()
            case "--serial":        a.serial = next()
            case "--code":          a.code = next()
            case "--dry-run":       a.dryRun = true
            case "--live":          a.dryRun = false
            case "--enabled":       a.enabled = true
            case "--during-print":  a.runDuringPrint = true
            case "--high":          a.high = Int(next()) ?? a.high
            case "--low":           a.low  = Int(next()) ?? a.low
            case "--temp":          a.temp = Int(next()) ?? a.temp
            case "--minutes":       a.minutes = Int(next())
            case "-h", "--help":
                print(usage)
                exit(0)
            default:
                FileHandle.standardError.write(Data("unknown arg: \(arg)\n\n\(usage)\n".utf8))
                exit(2)
            }
            i += 1
        }
        guard a.ip != nil, a.serial != nil, a.code != nil else {
            FileHandle.standardError.write(Data("--ip, --serial, --code are required\n\n\(usage)\n".utf8))
            exit(2)
        }
        return a
    }
}

let usage = """
Usage:
  bambudry-cli --ip <printer_ip> --serial <serial> --code <access_code>
               [--dry-run | --live]    (default: --dry-run)
               [--enabled]             (default: off — auto-dry disabled, observation only)
               [--high <RH%>]          (default: 35)
               [--low <RH%>]           (default: 20)
               [--temp <°C>]           (default: 45)
               [--during-print]        (default: off)
               [--minutes <N>]         (default: run until Ctrl-C)
"""

let args = Args.parse(CommandLine.arguments)

let transport = LANTransport(config: .init(
    host: args.ip!,
    serial: args.serial!,
    accessCode: args.code!
))
transport.debugLog = { msg in print(msg) }

let settings = AutoDrySettings(enabled: args.enabled,
                               highThreshold: args.high,
                               lowThreshold: args.low,
                               targetTemp: args.temp,
                               runDuringPrint: args.runDuringPrint)

let orchestrator = Orchestrator(transport: transport, config: .init(
    defaultSettings: settings,
    dryRun: args.dryRun
))

let modeBadge = args.dryRun ? "DRY-RUN" : "LIVE"
let enabledBadge = args.enabled ? "auto-dry: ON" : "auto-dry: OFF (observe only)"
print("[bambudry] \(modeBadge) — \(enabledBadge) — high=\(args.high)% low=\(args.low)% temp=\(args.temp)°C")
print("[bambudry] connecting to \(args.ip!) serial=\(args.serial!)…")

let formatter: DateFormatter = {
    let f = DateFormatter()
    f.dateFormat = "HH:mm:ss"
    return f
}()

func describe(_ d: AutoDryController.Decision) -> String {
    switch d {
    case .start(let id, let f, let t, let h):
        return "START ams=\(id) filament=\(f) temp=\(t)°C hours=\(h)"
    case .stop(let id):
        return "STOP  ams=\(id)"
    case .noop(let r):
        return "noop  (\(r))"
    }
}

let consoleTask = Task { @Sendable in
    for await event in await orchestrator.events() {
        let ts = formatter.string(from: event.timestamp)
        switch event.kind {
        case .observed(let snap):
            let rh = snap.humidityPercent.map { "\($0)%" } ?? "n/a"
            let drying = snap.isCurrentlyDrying ? "DRYING" : "idle"
            let temp = snap.currentTempC.map { String(format: "%.1f°C", $0) } ?? "—"
            let model = snap.model
            print("[\(ts)] ams=\(snap.amsId) [\(model)] rh=\(rh) \(drying) temp=\(temp) tray=\(snap.dominantFilamentType ?? "—")")
        case .decision(let id, let d):
            print("[\(ts)] decide ams=\(id) → \(describe(d))")
        case .sent(let id, let json):
            print("[\(ts)] PUBLISH ams=\(id): \(json)")
        case .dryRunSkipped(let id, let d):
            print("[\(ts)] dry-run would have published ams=\(id) → \(describe(d))")
        case .error(let msg):
            print("[\(ts)] ERROR \(msg)")
        }
    }
}

if let minutes = args.minutes {
    Task { @Sendable in
        try? await Task.sleep(nanoseconds: UInt64(minutes) * 60 * 1_000_000_000)
        print("[bambudry] reached --minutes \(minutes), shutting down")
        consoleTask.cancel()
        transport.disconnect()
        exit(0)
    }
}

do {
    try await orchestrator.run()
} catch {
    FileHandle.standardError.write(Data("[bambudry] fatal: \(error)\n".utf8))
    exit(1)
}
