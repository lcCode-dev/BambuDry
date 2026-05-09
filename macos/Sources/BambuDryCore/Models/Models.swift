import Foundation

public enum AMSModel: Int, Codable, Sendable {
    case extSpool = 0
    case ams = 1
    case amsLite = 2
    case n3f = 3   // AMS 2 Pro
    case n3s = 4   // AMS HT

    public var supportsDrying: Bool { self == .n3f || self == .n3s }

    public var supportsHumidityPercent: Bool { self == .n3f || self == .n3s }

    public var dryTempRangeC: ClosedRange<Int> {
        switch self {
        case .n3f: return 35...65
        case .n3s: return 35...85
        default:   return 35...65
        }
    }
}

public enum DryStatus: Int, Sendable {
    case off = 0
    case checking = 1
    case drying = 2
    case cooling = 3
    case stopping = 4
    case error = 5
    case cannotStopHeatOutOfControl = 6
    case prdTesting = 7

    /// Is the AMS heater "in use" in any way that should stop us from issuing
    /// a fresh START? Includes cooling and stopping phases so we don't
    /// accidentally re-fire while the AMS is still winding down.
    public var isActive: Bool {
        switch self {
        case .checking, .drying, .cooling, .stopping, .error, .cannotStopHeatOutOfControl:
            return true
        case .off, .prdTesting:
            return false
        }
    }
}

public enum CannotDryReason: Int, Sendable, Codable {
    case taskOccupied = 0
    case insufficientPower = 1
    case amsBusy = 2
    case consumableAtAmsOutlet = 3
    case initiatingAmsDrying = 4
    case notSupportedIn2dMode = 5
    case dryingInProgress = 6
    case upgrading = 7
    case insufficientPowerNeedPluginPower = 8
    case filamentAtAmsOutletManualUnload = 10
}

public struct Printer: Codable, Identifiable, Sendable, Hashable {
    public enum Transport: String, Codable, Sendable { case lan, cloud }

    public let id: UUID
    public var name: String
    public var serial: String
    public var lanIP: String?
    public var transport: Transport

    public init(id: UUID = UUID(),
                name: String,
                serial: String,
                lanIP: String? = nil,
                transport: Transport = .lan) {
        self.id = id
        self.name = name
        self.serial = serial
        self.lanIP = lanIP
        self.transport = transport
    }
}

public struct AutoDrySettings: Codable, Sendable, Equatable {
    public var enabled: Bool
    public var highThreshold: Int   // start drying at >= this RH%
    public var lowThreshold: Int    // stop drying at <= this RH%
    public var targetTemp: Int      // °C
    public var runDuringPrint: Bool
    /// Minimum minutes of drying before the controller will issue a STOP, even
    /// if humidity has fallen below `lowThreshold`. Anti-cycling protection.
    /// Bambu AMS heaters dry fast, so 5 min is plenty of debounce.
    public var minOnMinutes: Int
    /// Minimum minutes after a STOP before the controller will issue a START.
    public var minOffMinutes: Int

    public init(enabled: Bool = false,
                highThreshold: Int = 30,
                lowThreshold: Int = 18,
                targetTemp: Int = 45,
                runDuringPrint: Bool = false,
                minOnMinutes: Int = 5,
                minOffMinutes: Int = 5) {
        self.enabled = enabled
        self.highThreshold = highThreshold
        self.lowThreshold = lowThreshold
        self.targetTemp = targetTemp
        self.runDuringPrint = runDuringPrint
        self.minOnMinutes = minOnMinutes
        self.minOffMinutes = minOffMinutes
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled       = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? false
        highThreshold = try c.decodeIfPresent(Int.self, forKey: .highThreshold) ?? 30
        lowThreshold  = try c.decodeIfPresent(Int.self, forKey: .lowThreshold) ?? 18
        targetTemp    = try c.decodeIfPresent(Int.self, forKey: .targetTemp) ?? 45
        runDuringPrint = try c.decodeIfPresent(Bool.self, forKey: .runDuringPrint) ?? false
        minOnMinutes  = try c.decodeIfPresent(Int.self, forKey: .minOnMinutes) ?? 5
        minOffMinutes = try c.decodeIfPresent(Int.self, forKey: .minOffMinutes) ?? 5
    }

    public var isValid: Bool { lowThreshold < highThreshold && targetTemp > 0 }

    /// Clamp every field to the ranges the UI exposes (or to a fixed value if
    /// no UI exists). Call after decoding a stored config so older saved
    /// values from earlier app versions are silently brought into spec.
    public mutating func sanitize() {
        targetTemp    = 45                                             // AMS heater silently rejects below this; no UI to vary it
        highThreshold = max(5,  min(35, highThreshold))                // up to "acceptable storage" boundary
        lowThreshold  = max(0,  min(min(25, highThreshold - 1), lowThreshold))
        minOnMinutes  = max(0,  min(60, minOnMinutes))
        minOffMinutes = max(0,  min(60, minOffMinutes))
    }
}

public struct AMSSnapshot: Sendable, Equatable {
    public let amsId: Int
    public let model: AMSModel
    public let humidityPercent: Int?      // nil if invalid (-1 sentinel from device)
    public let isCurrentlyDrying: Bool
    public let leftDryTimeMinutes: Int
    public let cannotDryReasons: [CannotDryReason]
    public let dominantFilamentType: String?
    public let currentTempC: Double?

    public var supportsDrying: Bool { model.supportsDrying }

    public init(amsId: Int,
                model: AMSModel,
                humidityPercent: Int?,
                isCurrentlyDrying: Bool,
                leftDryTimeMinutes: Int = 0,
                cannotDryReasons: [CannotDryReason] = [],
                dominantFilamentType: String? = nil,
                currentTempC: Double? = nil) {
        self.amsId = amsId
        self.model = model
        self.humidityPercent = humidityPercent
        self.isCurrentlyDrying = isCurrentlyDrying
        self.leftDryTimeMinutes = leftDryTimeMinutes
        self.cannotDryReasons = cannotDryReasons
        self.dominantFilamentType = dominantFilamentType
        self.currentTempC = currentTempC
    }
}
