import SwiftUI
import BambuDryCore

struct AmsRowView: View {
    @EnvironmentObject var model: AppModel
    let snapshot: AMSSnapshot
    @Binding var settings: AutoDrySettings
    let decision: AutoDryController.Decision?

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(title)
                    .font(.system(.body, design: .rounded).weight(.semibold))
                Text("(\(modelLabel))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Toggle("Auto", isOn: $settings.enabled)
                    .toggleStyle(.switch)
                    .controlSize(.mini)
                    .labelsHidden()
                Text(settings.enabled ? "Auto on" : "Auto off")
                    .font(.caption)
                    .foregroundStyle(settings.enabled ? Color.accentColor : Color.secondary)
            }

            humidityBar

            HStack {
                statusLabel
                Spacer()
                if snapshot.isCurrentlyDrying {
                    Button {
                        model.stopDryingNow(amsId: snapshot.amsId)
                    } label: {
                        Label("Stop", systemImage: "stop.circle")
                            .labelStyle(.titleAndIcon)
                    }
                    .buttonStyle(.borderless)
                    .controlSize(.small)
                    .foregroundStyle(.red)
                }
                if let temp = snapshot.currentTempC {
                    Text(String(format: "%.1f°C", temp))
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }

            if settings.enabled {
                thresholdControls
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var title: String {
        snapshot.amsId < 128 ? "AMS \(snapshot.amsId + 1)" : "Slot \(snapshot.amsId - 127)"
    }

    private var modelLabel: String {
        switch snapshot.model {
        case .n3f: return "AMS 2 Pro"
        case .n3s: return "AMS HT"
        case .ams: return "AMS"
        case .amsLite: return "AMS Lite"
        case .extSpool: return "Ext"
        }
    }

    private var humidityText: String {
        snapshot.humidityPercent.map { "\($0)%" } ?? "—"
    }

    private var humidityFraction: Double {
        guard let rh = snapshot.humidityPercent else { return 0 }
        return min(max(Double(rh) / 100.0, 0), 1)
    }

    private var humidityColor: Color {
        guard let rh = snapshot.humidityPercent else { return .gray }
        // Red: heater is firing, or humidity is at/above the start trigger.
        if snapshot.isCurrentlyDrying || rh >= settings.highThreshold { return .red }
        // Yellow: upper half of the dead band — drifting toward "needs heat".
        let midpoint = Double(settings.lowThreshold + settings.highThreshold) / 2.0
        if Double(rh) >= midpoint { return .yellow }
        // Green: at/below stop, or in lower half of dead band — comfortable zone.
        return .green
    }

    private var humidityBar: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text("Humidity")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Text(humidityText)
                    .font(.caption.monospacedDigit().bold())
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 3)
                        .fill(Color.secondary.opacity(0.2))
                    RoundedRectangle(cornerRadius: 3)
                        .fill(humidityColor)
                        .frame(width: geo.size.width * humidityFraction)
                    if settings.enabled {
                        // High/low markers
                        Rectangle().fill(Color.red.opacity(0.6))
                            .frame(width: 1)
                            .offset(x: geo.size.width * Double(settings.highThreshold) / 100.0)
                        Rectangle().fill(Color.green.opacity(0.6))
                            .frame(width: 1)
                            .offset(x: geo.size.width * Double(settings.lowThreshold) / 100.0)
                    }
                }
            }
            .frame(height: 6)
        }
    }

    private var statusLabel: some View {
        Group {
            if snapshot.isCurrentlyDrying {
                HStack(spacing: 4) {
                    Image(systemName: "thermometer.high")
                    Text("Dehumidifying")
                    if let d = decision,
                       case .noop(.waitingAfterStart(let s)) = d {
                        Text("· can stop in \(formatDuration(s))")
                            .foregroundStyle(.secondary)
                    }
                }
                .foregroundStyle(.orange)
                .font(.caption)
            } else if let d = decision, case .noop(let reason) = d, settings.enabled {
                Text(noopText(reason))
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                Text("Idle")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private func noopText(_ reason: AutoDryController.NoopReason) -> String {
        switch reason {
        case .disabled:                return "Auto off"
        case .unsupportedAMS:          return "Heater not supported"
        case .humidityUnknown:         return "Humidity unknown"
        case .printing:                return "Idle (printing)"
        case .withinHysteresisBand:    return "Idle (in band)"
        case .waitingAfterStop(let s): return "Cooldown — can heat in \(formatDuration(s))"
        case .waitingAfterStart(let s):return "Dehumidifying — can stop in \(formatDuration(s))"
        case .startInFlight(let s):    return "Heat command sent — retry in \(formatDuration(s))"
        case .stopInFlight(let s):     return "Stop command sent — retry in \(formatDuration(s))"
        case .cannotDry(let reasons):  return "Blocked: \(reasons.first.map { String(describing: $0) } ?? "—")"
        case .settingsInvalid:         return "Bad settings"
        }
    }

    private func formatDuration(_ seconds: Int) -> String {
        if seconds < 60 { return "\(seconds)s" }
        let m = seconds / 60
        let s = seconds % 60
        if m < 10 && s != 0 { return "\(m)m \(s)s" }
        return "\(m)m"
    }

    private var thresholdControls: some View {
        VStack(spacing: 4) {
            HStack {
                Text("Start ≥")
                    .font(.caption)
                    .frame(width: 50, alignment: .leading)
                Slider(value: Binding(
                    get: { Double(settings.highThreshold) },
                    set: { settings.highThreshold = Int($0) }
                ), in: Double(settings.lowThreshold + 1)...35, step: 1)
                Text("\(settings.highThreshold)%")
                    .font(.caption.monospacedDigit())
                    .frame(width: 36, alignment: .trailing)
            }
            HStack {
                Text("Stop ≤")
                    .font(.caption)
                    .frame(width: 50, alignment: .leading)
                Slider(value: Binding(
                    get: { Double(settings.lowThreshold) },
                    set: { settings.lowThreshold = Int($0) }
                ), in: 0...Double(settings.highThreshold - 1), step: 1)
                Text("\(settings.lowThreshold)%")
                    .font(.caption.monospacedDigit())
                    .frame(width: 36, alignment: .trailing)
            }
        }
    }
}
