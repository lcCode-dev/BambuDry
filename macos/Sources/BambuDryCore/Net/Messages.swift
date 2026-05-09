import Foundation

// MARK: - Outbound: ams_filament_drying request

public enum DryCtrlMode: Int, Codable, Sendable {
    case off = 0
    case onTime = 1
    case onHumidity = 2
}

/// Mirrors `device/<serial>/request` envelope for AMS drying.
/// Reference: lagos/src/slic3r/GUI/DeviceCore/DevFilaSystemCtrl.cpp:19-57
public struct DryRequest: Codable, Sendable, Equatable {
    public struct Print: Codable, Sendable, Equatable {
        public let command: String         // "ams_filament_drying"
        public let sequence_id: String
        public let ams_id: Int
        public let mode: Int               // DryCtrlMode raw
        public let filament: String
        public let temp: Int
        public let duration: Int           // hours
        public let humidity: Int           // 0 unless mode == onHumidity
        public let rotate_tray: Bool
        public let cooling_temp: Int
        public let close_power_conflict: Bool
    }
    public let print: Print

    public static func start(amsId: Int,
                             filament: String,
                             tempC: Int,
                             hours: Int,
                             sequenceId: String,
                             rotateTray: Bool = false,
                             coolingTemp: Int = 50,
                             closePowerConflict: Bool = false) -> DryRequest {
        DryRequest(print: Print(
            command: "ams_filament_drying",
            sequence_id: sequenceId,
            ams_id: amsId,
            mode: DryCtrlMode.onTime.rawValue,
            filament: filament,
            temp: tempC,
            duration: hours,
            humidity: 0,
            rotate_tray: rotateTray,
            cooling_temp: coolingTemp,
            close_power_conflict: closePowerConflict
        ))
    }

    public static func stop(amsId: Int, sequenceId: String) -> DryRequest {
        DryRequest(print: Print(
            command: "ams_filament_drying",
            sequence_id: sequenceId,
            ams_id: amsId,
            mode: DryCtrlMode.off.rawValue,
            filament: "",
            temp: 0,
            duration: 0,
            humidity: 0,
            rotate_tray: false,
            cooling_temp: 0,
            close_power_conflict: false
        ))
    }
}

// MARK: - Inbound: device/<serial>/report (AMS subset)
//
// Reference: lagos/src/slic3r/GUI/DeviceCore/DevFilaSystem.cpp:630-700.
// Only the fields we need for auto-dry are decoded; the printer publishes many
// more we ignore.

public struct ReportEnvelope: Decodable, Sendable {
    public let print: PrintReport?
}

public struct PrintReport: Decodable, Sendable {
    public let ams: AmsContainer?
    public let print_type: String?       // "idle" / "running" / etc.
    public let gcode_state: String?      // "PRINTING" / "IDLE" / "PAUSE" / "FINISH"
}

public struct AmsContainer: Decodable, Sendable {
    public let ams: [AmsReport]?
}

public struct AmsReport: Decodable, Sendable {
    public let id: String                 // numeric string, may be 128+ for switchers
    public let humidity: String?          // 0..5 humidity-level (legacy AMS only)
    public let humidity_raw: String?      // "47" → 47%, "-1" → invalid (N3F/N3S)
    public let temp: String?              // float as string
    public let info: String?              // hex bitfield, status decoded from bits
    public let dry_time: Int?             // remaining dry minutes
    public let dry_setting: DrySetting?
    public let dry_sf_reason: [Int]?      // CannotDryReason raw values
    public let tray: [TrayReport]?

    public struct DrySetting: Decodable, Sendable {
        public let dry_filament: String?
        public let dry_temperature: Int?
        public let dry_duration: Int?
    }

    public struct TrayReport: Decodable, Sendable {
        public let id: String
        public let tray_type: String?
        public let tray_sub_brands: String?
    }
}

// MARK: - Snapshot derivation

extension AmsReport {
    /// Decode `info` hex string, extract `count` bits starting at `start`.
    /// Reference: lagos/src/slic3r/GUI/DeviceCore/DevUtil.cpp:7-26
    static func bits(in hexString: String, start: Int, count: Int) -> Int? {
        let trimmed = hexString.trimmingCharacters(in: .whitespacesAndNewlines)
        let cleaned: String
        if trimmed.hasPrefix("0x") || trimmed.hasPrefix("0X") {
            cleaned = String(trimmed.dropFirst(2))
        } else {
            cleaned = trimmed
        }
        guard let value = UInt64(cleaned, radix: 16) else { return nil }
        let mask: UInt64 = (1 << count) - 1
        return Int((value >> start) & mask)
    }

    public func dryStatus() -> DryStatus? {
        guard let info, let bits = Self.bits(in: info, start: 4, count: 4) else { return nil }
        return DryStatus(rawValue: bits)
    }

    /// AMS model is encoded in bits 0..3 of the `info` hex string.
    /// Reference: lagos/src/slic3r/GUI/DeviceCore/DevFilaSystem.cpp:585
    public func amsModel() -> AMSModel? {
        guard let info, let bits = Self.bits(in: info, start: 0, count: 4) else { return nil }
        return AMSModel(rawValue: bits)
    }

    /// Derive an AMSSnapshot, auto-detecting the model from the `info` bitfield.
    /// Returns nil if the model can't be decoded.
    public func snapshot() -> AMSSnapshot? {
        guard let model = amsModel() else { return nil }
        return snapshot(model: model)
    }

    /// Derive an AMSSnapshot for the controller using an explicit model.
    public func snapshot(model: AMSModel) -> AMSSnapshot {
        let amsIdInt = Int(id) ?? -1
        let humidity: Int? = humidity_raw.flatMap { Int($0) }.flatMap { $0 < 0 ? nil : $0 }
        let dryStatus = dryStatus()
        let cannotReasons = (dry_sf_reason ?? []).compactMap { CannotDryReason(rawValue: $0) }
        let dominant = tray?.compactMap { $0.tray_type }
            .filter { !$0.isEmpty }
            .first
        let temp = self.temp.flatMap { Double($0) }
        return AMSSnapshot(
            amsId: amsIdInt,
            model: model,
            humidityPercent: humidity,
            isCurrentlyDrying: dryStatus?.isActive ?? false,
            leftDryTimeMinutes: dry_time ?? 0,
            cannotDryReasons: cannotReasons,
            dominantFilamentType: dominant,
            currentTempC: temp
        )
    }
}
