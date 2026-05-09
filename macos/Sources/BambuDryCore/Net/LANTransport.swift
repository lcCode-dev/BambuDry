import Foundation
import CocoaMQTT

/// CocoaMQTT-backed transport for a single Bambu printer over LAN.
///
/// Wire format (verified against firmware):
/// - Host: `<printer_ip>:8883`
/// - TLS with self-signed cert (we accept it via the delegate trust callback)
/// - MQTT v3.1.1, username "bblp", password = LAN access code
/// - Subscribe `device/<serial>/report`, publish `device/<serial>/request`
public final class LANTransport: @unchecked Sendable {

    public struct Config: Sendable {
        public let host: String
        public let port: UInt16
        public let serial: String
        public let accessCode: String
        public let clientID: String
        public init(host: String,
                    port: UInt16 = 8883,
                    serial: String,
                    accessCode: String,
                    clientID: String = "bambudry-\(UUID().uuidString.prefix(8))") {
            self.host = host
            self.port = port
            self.serial = serial
            self.accessCode = accessCode
            self.clientID = clientID
        }
    }

    public enum TransportError: Error, CustomStringConvertible {
        case connectFailed(CocoaMQTTConnAck)
        case connectTimedOut
        case socketDisconnected(String)
        case notConnected
        case publishFailed
        case decodeFailed(Error)
        public var description: String {
            switch self {
            case .connectFailed(let ack): return "MQTT connect failed: \(ack)"
            case .connectTimedOut:        return "MQTT connect timed out"
            case .socketDisconnected(let s): return "Socket disconnected during connect: \(s)"
            case .notConnected:           return "MQTT not connected"
            case .publishFailed:          return "MQTT publish failed"
            case .decodeFailed(let err):  return "Report decode failed: \(err)"
            }
        }
    }

    public let config: Config

    /// Verbose logging hook. Set this to `print` for stdout debug output.
    public var debugLog: (@Sendable (String) -> Void)?

    private let mqtt: CocoaMQTT
    private let queue = DispatchQueue(label: "BambuDry.LANTransport", qos: .utility)
    private let bridge: MQTTBridge

    private var connectContinuation: CheckedContinuation<Void, Error>?
    private var reportContinuation: AsyncStream<ReportEnvelope>.Continuation?
    private var connected: Bool = false

    private let decoder = JSONDecoder()

    /// Seed sequence_id from current Unix time so it stays monotonically
    /// increasing across app restarts. Some Bambu firmwares appear to dedupe
    /// commands based on (recent) sequence_id values — starting fresh from 1
    /// every launch could cause the printer to silently ignore commands that
    /// look like ones it just processed.
    private var sequenceID: Int = Int(Date().timeIntervalSince1970)
    private func nextSequenceID() -> String {
        queue.sync {
            defer { sequenceID &+= 1 }
            return String(sequenceID)
        }
    }

    public init(config: Config) {
        self.config = config
        self.mqtt = CocoaMQTT(clientID: config.clientID,
                              host: config.host,
                              port: config.port)
        self.bridge = MQTTBridge()

        mqtt.username = "bblp"
        mqtt.password = config.accessCode
        mqtt.keepAlive = 60
        mqtt.cleanSession = true
        mqtt.autoReconnect = true
        mqtt.autoReconnectTimeInterval = 5
        mqtt.enableSSL = true
        mqtt.allowUntrustCACertificate = true
        mqtt.delegate = bridge
        bridge.owner = self
    }

    fileprivate func log(_ msg: String) { debugLog?("[LANTransport] \(msg)") }

    /// Connect and subscribe. Throws on bad creds, TLS failure, or timeout.
    public func connect(timeout: TimeInterval = 15) async throws {
        let connectTask: () async throws -> Void = {
            try await withCheckedThrowingContinuation { (cont: CheckedContinuation<Void, Error>) in
                self.queue.async {
                    self.connectContinuation = cont
                    self.log("calling mqtt.connect(timeout: \(timeout))")
                    let ok = self.mqtt.connect(timeout: timeout)
                    self.log("mqtt.connect returned \(ok)")
                    if !ok {
                        self.connectContinuation = nil
                        cont.resume(throwing: TransportError.notConnected)
                    }
                }
            }
        }
        // Belt-and-suspenders timeout in case CocoaMQTT silently drops the
        // socket without firing didConnectAck or didDisconnect.
        try await withThrowingTaskGroup(of: Void.self) { group in
            group.addTask { try await connectTask() }
            group.addTask {
                try await Task.sleep(nanoseconds: UInt64(timeout + 5) * 1_000_000_000)
                throw TransportError.connectTimedOut
            }
            try await group.next()
            group.cancelAll()
        }
    }

    /// AsyncStream of decoded report envelopes. Call exactly once per transport.
    public func reportStream() -> AsyncStream<ReportEnvelope> {
        AsyncStream { continuation in
            self.queue.async {
                self.reportContinuation = continuation
            }
            continuation.onTermination = { [weak self] _ in
                self?.queue.async { self?.reportContinuation = nil }
            }
        }
    }

    public func disconnect() {
        mqtt.disconnect()
    }

    // MARK: - Bridge callbacks (called from MQTTBridge on CocoaMQTT's queue)

    fileprivate func handleConnectAck(_ ack: CocoaMQTTConnAck) {
        queue.async {
            self.log("CONNACK \(ack)")
            let cont = self.connectContinuation
            self.connectContinuation = nil
            if ack == .accept {
                self.connected = true
                self.mqtt.subscribe("device/\(self.config.serial)/report")
                cont?.resume(returning: ())
            } else {
                cont?.resume(throwing: TransportError.connectFailed(ack))
            }
        }
    }

    fileprivate func handleDisconnect(error: Error?) {
        queue.async {
            let msg = error?.localizedDescription ?? "no-error"
            self.log("DISCONNECT \(msg)")
            self.connected = false
            // If we're mid-connect, surface the disconnect as a failure.
            if let cont = self.connectContinuation {
                self.connectContinuation = nil
                cont.resume(throwing: TransportError.socketDisconnected(msg))
            }
        }
    }

    fileprivate func handleMessage(_ message: CocoaMQTTMessage) {
        let bytes = Data(message.payload)
        do {
            let envelope = try decoder.decode(ReportEnvelope.self, from: bytes)
            queue.async {
                self.reportContinuation?.yield(envelope)
            }
        } catch {
            log("decode failed: \(error)")
        }
    }

    // MARK: - Publish

    @discardableResult
    public func send(_ request: DryRequest) throws -> String {
        let data = try JSONEncoder().encode(request)
        guard let json = String(data: data, encoding: .utf8) else {
            throw TransportError.publishFailed
        }
        let topic = "device/\(config.serial)/request"
        let mid = mqtt.publish(topic, withString: json, qos: .qos1, retained: false)
        if mid < 0 { throw TransportError.publishFailed }
        return json
    }

    public func startDrying(amsId: Int, filament: String, tempC: Int, hours: Int) throws -> String {
        let req = DryRequest.start(amsId: amsId,
                                   filament: filament,
                                   tempC: tempC,
                                   hours: hours,
                                   sequenceId: nextSequenceID())
        return try send(req)
    }

    public func stopDrying(amsId: Int) throws -> String {
        let req = DryRequest.stop(amsId: amsId, sequenceId: nextSequenceID())
        return try send(req)
    }

    /// Ask the printer for a full status push. Necessary because regular updates
    /// are diff-only and frequently omit the AMS block.
    @discardableResult
    public func requestPushAll() throws -> String {
        let body = #"{"pushing":{"sequence_id":"0","command":"pushall"}}"#
        let topic = "device/\(config.serial)/request"
        let mid = mqtt.publish(topic, withString: body, qos: .qos1, retained: false)
        if mid < 0 { throw TransportError.publishFailed }
        return body
    }
}

// MARK: - MQTTBridge
//
// CocoaMQTT's trust callback (for self-signed certs) is only available via the
// delegate protocol. We implement the protocol with no-ops for everything we
// already handle via block callbacks, plus the trust callback that auto-accepts
// the printer's self-signed cert.

private final class MQTTBridge: NSObject, CocoaMQTTDelegate {
    weak var owner: LANTransport?

    func mqtt(_ mqtt: CocoaMQTT, didReceive trust: SecTrust, completionHandler: @escaping (Bool) -> Void) {
        // Bambu printers ship with self-signed certs. Accept any cert presented
        // by the host we explicitly typed in. We trust the LAN endpoint, not
        // the cert chain.
        owner?.log("TLS trust auto-accepted")
        completionHandler(true)
    }

    func mqtt(_ mqtt: CocoaMQTT, didConnectAck ack: CocoaMQTTConnAck) {
        owner?.handleConnectAck(ack)
    }

    func mqttDidDisconnect(_ mqtt: CocoaMQTT, withError err: Error?) {
        owner?.handleDisconnect(error: err)
    }

    func mqtt(_ mqtt: CocoaMQTT, didReceiveMessage message: CocoaMQTTMessage, id: UInt16) {
        owner?.handleMessage(message)
    }

    func mqtt(_ mqtt: CocoaMQTT, didPublishMessage message: CocoaMQTTMessage, id: UInt16) {}
    func mqtt(_ mqtt: CocoaMQTT, didPublishAck id: UInt16) {}
    func mqtt(_ mqtt: CocoaMQTT, didSubscribeTopics success: NSDictionary, failed: [String]) {
        if !failed.isEmpty {
            owner?.log("SUBSCRIBE failed for: \(failed)")
        } else {
            owner?.log("SUBSCRIBE ok: \(success)")
        }
    }
    func mqtt(_ mqtt: CocoaMQTT, didUnsubscribeTopics topics: [String]) {}
    func mqttDidPing(_ mqtt: CocoaMQTT) {}
    func mqttDidReceivePong(_ mqtt: CocoaMQTT) {}
}
