import Foundation
import Security
import BambuDryCore

/// On-disk app config (printer identity + per-AMS settings).
/// Access code lives in Keychain, NOT in this file.
struct AppConfig: Codable, Equatable {
    var printerName: String
    var serial: String
    var lanIP: String
    /// Map AMS id (0, 128, 129…) → settings.
    var amsSettings: [String: AutoDrySettings]
    var defaultSettings: AutoDrySettings

    static let empty = AppConfig(
        printerName: "My Bambu Printer",
        serial: "",
        lanIP: "",
        amsSettings: [:],
        // Use AutoDrySettings's own defaults — single source of truth.
        defaultSettings: AutoDrySettings())
}

extension AppConfig {
    func settings(forAmsId id: Int) -> AutoDrySettings {
        amsSettings[String(id)] ?? defaultSettings
    }
    mutating func setSettings(_ s: AutoDrySettings, forAmsId id: Int) {
        amsSettings[String(id)] = s
    }
}

enum Storage {
    static let configURL: URL = {
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory,
                                                  in: .userDomainMask).first!
        let dir = appSupport.appendingPathComponent("BambuDry", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("config.json")
    }()

    static func loadConfig() -> AppConfig? {
        guard let data = try? Data(contentsOf: configURL) else { return nil }
        return try? JSONDecoder().decode(AppConfig.self, from: data)
    }

    static func saveConfig(_ config: AppConfig) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(config)
        try data.write(to: configURL, options: .atomic)
    }

    // MARK: - Keychain (access code)

    private static let keychainService = "dev.lcCode.BambuDry"

    static func saveAccessCode(_ code: String, forSerial serial: String) throws {
        let data = code.data(using: .utf8)!
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: keychainService,
            kSecAttrAccount as String: serial,
        ]
        SecItemDelete(query as CFDictionary)
        var attrs = query
        attrs[kSecValueData as String] = data
        attrs[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        let status = SecItemAdd(attrs as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw NSError(domain: "Keychain", code: Int(status))
        }
    }

    static func loadAccessCode(forSerial serial: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: keychainService,
            kSecAttrAccount as String: serial,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data,
              let code = String(data: data, encoding: .utf8) else { return nil }
        return code
    }

    static func deleteAccessCode(forSerial serial: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: keychainService,
            kSecAttrAccount as String: serial,
        ]
        SecItemDelete(query as CFDictionary)
    }
}
