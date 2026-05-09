import SwiftUI
import BambuDryCore

struct MenuBarView: View {
    enum Context { case dropdown, pinned }

    @EnvironmentObject var model: AppModel
    @Environment(\.openWindow) private var openWindow

    let context: Context

    init(context: Context = .dropdown) {
        self.context = context
    }

    /// Close any visible MenuBarExtra popover. SwiftUI doesn't expose a clean
    /// API for this on macOS 13+, so we walk NSApp's window list and close any
    /// menu-bar panel currently displaying our content.
    private func dismissMenuBarPopover() {
        for window in NSApplication.shared.windows {
            if window.className.contains("NSPopoverWindow")
                || window.className.contains("MenuBarExtraWindow")
                || window.level == NSWindow.Level.popUpMenu {
                window.orderOut(nil)
            }
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header

            Divider().padding(.vertical, 4)

            if !model.isConfigured {
                PrinterSetupView()
                    .padding(12)
            } else if model.snapshots.isEmpty {
                emptyState
            } else {
                ForEach(model.sortedSnapshots, id: \.amsId) { snap in
                    AmsRowView(snapshot: snap,
                               settings: bindingForAms(snap.amsId),
                               decision: model.lastDecisions[snap.amsId])
                    if snap.amsId != model.sortedSnapshots.last?.amsId {
                        Divider().padding(.horizontal, 12)
                    }
                }
            }

            Divider().padding(.vertical, 4)
            footer
        }
        .padding(.vertical, 6)
    }

    private var header: some View {
        HStack(spacing: 8) {
            Text(model.config.printerName.isEmpty ? "BambuDry" : model.config.printerName)
                .font(.headline)
                .lineLimit(1)
            Spacer(minLength: 8)
            statusBadge
                .lineLimit(1)
                .truncationMode(.tail)
            if context == .dropdown {
                Button {
                    NSApp.activate(ignoringOtherApps: true)
                    openWindow(id: "main-window")
                    DispatchQueue.main.async { dismissMenuBarPopover() }
                } label: {
                    Image(systemName: "pin")
                }
                .buttonStyle(.borderless)
                .help("Pin window")
            }
        }
        .padding(.horizontal, 12)
        .padding(.top, 4)
    }

    private var statusBadge: some View {
        let (text, color): (String, Color) = {
            switch model.status {
            case .notConfigured:           return ("Not configured", .secondary)
            case .connecting:              return ("Connecting…", .orange)
            case .connected:               return ("Connected", .green)
            case .disconnected(let r):     return ("Offline — \(r.prefix(40))", .red)
            }
        }()
        return Text(text)
            .font(.caption)
            .foregroundStyle(color)
    }

    private var emptyState: some View {
        HStack {
            ProgressView().controlSize(.small)
            Text("Waiting for AMS data…")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(12)
    }

    private var footer: some View {
        HStack {
            if model.dryRun {
                Label("DRY-RUN", systemImage: "eye")
                    .font(.caption.monospaced())
                    .foregroundStyle(.orange)
            }
            Spacer()

            Button("Settings…") {
                NSApp.activate(ignoringOtherApps: true)
                if #available(macOS 14.0, *) {
                    EnvironmentValues().openSettings()
                } else {
                    NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil)
                }
            }
            .buttonStyle(.borderless)
            .font(.caption)

            Button("Quit") { NSApp.terminate(nil) }
                .buttonStyle(.borderless)
                .font(.caption)
        }
        .padding(.horizontal, 12)
        .padding(.bottom, 4)
    }

    private func bindingForAms(_ id: Int) -> Binding<AutoDrySettings> {
        Binding(
            get: { model.config.settings(forAmsId: id) },
            set: { newValue in
                model.updateSettings(amsId: id) { $0 = newValue }
            }
        )
    }
}
