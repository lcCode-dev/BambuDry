import Foundation
import ServiceManagement

/// Thin wrapper around SMAppService.mainApp for the "Launch at login" toggle.
///
/// Notes:
/// - macOS 13+ only; project deployment target matches.
/// - For `register()` to succeed in a meaningful way the app must be installed
///   in /Applications and signed (Apple Development cert is enough). Running
///   directly from Xcode's DerivedData often fails with `Operation not
///   permitted` — that's expected. Drag BambuDry.app to /Applications first.
enum LaunchAtLogin {

    enum Status: String {
        case notRegistered  = "Not registered"
        case enabled        = "Enabled"
        case requiresApproval = "Awaiting approval in System Settings"
        case notFound       = "App not in /Applications"

        init(_ smStatus: SMAppService.Status) {
            switch smStatus {
            case .enabled:           self = .enabled
            case .requiresApproval:  self = .requiresApproval
            case .notFound:          self = .notFound
            case .notRegistered:     self = .notRegistered
            @unknown default:        self = .notRegistered
            }
        }
    }

    static var status: Status { Status(SMAppService.mainApp.status) }

    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func setEnabled(_ enabled: Bool) throws {
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }

    /// Open System Settings → Login Items so the user can grant approval if
    /// status is `.requiresApproval`.
    static func openLoginItemsSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
