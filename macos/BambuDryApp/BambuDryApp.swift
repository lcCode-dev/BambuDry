import SwiftUI

@main
struct BambuDryApp: App {
    @StateObject private var model = AppModel()

    var body: some Scene {
        MenuBarExtra {
            MenuBarView()
                .environmentObject(model)
                .frame(minWidth: 360)
                .onAppear {
                    if model.isConfigured, model.status != .connected, model.status != .connecting {
                        model.start()
                    }
                }
        } label: {
            MenuBarIcon(state: model.menuBarState)
        }
        .menuBarExtraStyle(.window)

        Settings {
            SettingsView()
                .environmentObject(model)
        }

        // Pinnable window — same content as the menu bar dropdown, but
        // persistent, movable, and survives clicks elsewhere. Default position
        // is the top-right of the screen, roughly where the menu bar icon is,
        // so it appears near the user's mouse rather than centered.
        Window("BambuDry", id: "main-window") {
            MenuBarView(context: .pinned)
                .environmentObject(model)
                .frame(width: 380)
                .fixedSize(horizontal: true, vertical: false)
        }
        .defaultPosition(.topTrailing)
        .windowResizability(.contentSize)
    }
}

/// Menu bar status icon.
///
/// Static rendering — no animation. We tried both `withAnimation(.repeatForever)`
/// and `TimelineView(.periodic)` to pulse during drying; both interact badly with
/// MenuBarExtra's IPC layer to the system menu bar process and lock up the
/// main thread within seconds. Visual signaling is done via symbol + colour
/// changes only.
private struct MenuBarIcon: View {
    let state: MenuBarState

    var body: some View {
        Image(systemName: state.systemImage)
            .foregroundStyle(tint)
    }

    private var tint: Color {
        switch state {
        case .drying:     return .red
        case .humidAlert: return .orange
        case .connecting: return .secondary
        case .offline:    return .secondary
        case .ok:         return .primary
        }
    }
}
