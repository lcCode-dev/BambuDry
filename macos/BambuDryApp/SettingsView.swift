import SwiftUI
import BambuDryCore

struct SettingsView: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        TabView {
            PrinterTab()
                .environmentObject(model)
                .tabItem { Label("Printer", systemImage: "printer") }

            DefaultsTab()
                .environmentObject(model)
                .tabItem { Label("Defaults", systemImage: "slider.horizontal.3") }

            AdvancedTab()
                .environmentObject(model)
                .tabItem { Label("Advanced", systemImage: "wrench.and.screwdriver") }
        }
        .frame(width: 480, height: 420)
        .padding()
    }
}

private struct PrinterTab: View {
    @EnvironmentObject var model: AppModel
    @State private var name: String = ""
    @State private var serial: String = ""
    @State private var ip: String = ""
    @State private var accessCode: String = ""

    var body: some View {
        Form {
            Section("Printer") {
                TextField("Name", text: $name)
                TextField("LAN IP", text: $ip)
                TextField("Serial", text: $serial)
                SecureField("Access Code", text: $accessCode)
            }
            HStack {
                Spacer()
                Button("Save & Reconnect") {
                    var c = model.config
                    c.printerName = name
                    c.lanIP = ip.trimmingCharacters(in: .whitespacesAndNewlines)
                    c.serial = serial.trimmingCharacters(in: .whitespacesAndNewlines)
                    let code = accessCode.trimmingCharacters(in: .whitespacesAndNewlines)
                    if !code.isEmpty {
                        model.saveAndConnect(c, accessCode: code)
                    } else {
                        // No new code typed — re-use existing.
                        if let existing = Storage.loadAccessCode(forSerial: c.serial) {
                            model.saveAndConnect(c, accessCode: existing)
                        }
                    }
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .onAppear {
            name = model.config.printerName
            serial = model.config.serial
            ip = model.config.lanIP
            accessCode = ""    // never display existing code; user re-enters only on change
        }
    }
}

private struct DefaultsTab: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        Form {
            Section("Default per-AMS auto-dry settings") {
                Toggle("Enabled by default", isOn: Binding(
                    get: { model.config.defaultSettings.enabled },
                    set: { v in model.updateDefaultSettings { $0.enabled = v } }
                ))
                Toggle("Run during prints", isOn: Binding(
                    get: { model.config.defaultSettings.runDuringPrint },
                    set: { v in model.updateDefaultSettings { $0.runDuringPrint = v } }
                ))
                Stepper("Start ≥ \(model.config.defaultSettings.highThreshold)%",
                        value: Binding(
                            get: { model.config.defaultSettings.highThreshold },
                            set: { v in model.updateDefaultSettings { $0.highThreshold = v } }
                        ), in: 5...35)
                Stepper("Stop ≤ \(model.config.defaultSettings.lowThreshold)%",
                        value: Binding(
                            get: { model.config.defaultSettings.lowThreshold },
                            set: { v in model.updateDefaultSettings { $0.lowThreshold = v } }
                        ), in: 0...25)
                Text("Drying temperature is fixed at 45°C — the minimum the AMS heater will actually accept. 45°C is well below the glass transition of PLA, PETG, ABS, ASA, Nylon, and PC, so it's safe across all common filaments. TPU and PVA have lower Tg and shouldn't be auto-managed by this app.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Section("Anti-cycling guards") {
                Stepper("Min on time: \(model.config.defaultSettings.minOnMinutes) min",
                        value: Binding(
                            get: { model.config.defaultSettings.minOnMinutes },
                            set: { v in model.updateDefaultSettings { $0.minOnMinutes = v } }
                        ), in: 0...60)
                Stepper("Min off time: \(model.config.defaultSettings.minOffMinutes) min",
                        value: Binding(
                            get: { model.config.defaultSettings.minOffMinutes },
                            set: { v in model.updateDefaultSettings { $0.minOffMinutes = v } }
                        ), in: 0...60)
                Text("Once drying starts, the controller waits at least this long before issuing a stop, even if humidity has fallen below threshold. Bambu AMS heaters dry fast — a few minutes is plenty of debounce. Set to 0 to react immediately.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Section("Notes") {
                Text("Per-AMS overrides come from the menu bar dropdown (use the sliders under each AMS row when Auto is on).")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

private struct AdvancedTab: View {
    @EnvironmentObject var model: AppModel
    @State private var launchEnabled: Bool = LaunchAtLogin.isEnabled
    @State private var launchStatus: String = LaunchAtLogin.status.rawValue
    @State private var launchError: String?

    var body: some View {
        Form {
            Section("Startup") {
                Toggle("Launch at login", isOn: Binding(
                    get: { launchEnabled },
                    set: { newValue in
                        do {
                            try LaunchAtLogin.setEnabled(newValue)
                            launchEnabled = LaunchAtLogin.isEnabled
                            launchStatus = LaunchAtLogin.status.rawValue
                            launchError = nil
                        } catch {
                            launchEnabled = LaunchAtLogin.isEnabled
                            launchStatus = LaunchAtLogin.status.rawValue
                            launchError = error.localizedDescription
                        }
                    }
                ))
                HStack {
                    Text("Status:").font(.caption).foregroundStyle(.secondary)
                    Text(launchStatus).font(.caption)
                    Spacer()
                    if launchStatus.contains("approval") {
                        Button("Open Login Items…") {
                            LaunchAtLogin.openLoginItemsSettings()
                        }
                        .controlSize(.small)
                    }
                }
                if let err = launchError {
                    Text(err)
                        .font(.caption)
                        .foregroundStyle(.red)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Text("For this to work the app must be in /Applications. Drag BambuDry.app there from Xcode's Products folder.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Section("Diagnostics") {
                Toggle("Dry-run mode (don't actually publish commands)",
                       isOn: Binding(
                            get: { model.dryRun },
                            set: { model.setDryRun($0) }
                       ))
                Button("Reconnect") {
                    model.stop()
                    model.start()
                }
                Button("Disconnect") {
                    model.stop()
                }
            }
            Section("Recent publishes") {
                if model.publishLog.isEmpty {
                    Text("(none yet)").foregroundStyle(.secondary).font(.caption)
                } else {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 2) {
                            ForEach(Array(model.publishLog.enumerated()), id: \.offset) { _, line in
                                Text(line)
                                    .font(.caption.monospaced())
                                    .lineLimit(1)
                                    .truncationMode(.middle)
                            }
                        }
                    }
                    .frame(maxHeight: 120)
                }
            }
        }
    }
}
