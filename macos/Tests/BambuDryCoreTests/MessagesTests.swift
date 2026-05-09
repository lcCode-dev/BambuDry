import XCTest
@testable import BambuDryCore

final class MessagesTests: XCTestCase {

    // MARK: - Outbound encode

    func testStartCommandMatchesProtocolShape() throws {
        let req = DryRequest.start(amsId: 1,
                                   filament: "PETG",
                                   tempC: 65,
                                   hours: 8,
                                   sequenceId: "42")
        let data = try JSONEncoder().encode(req)
        let dict = try JSONSerialization.jsonObject(with: data) as! [String: Any]
        let print = dict["print"] as! [String: Any]
        XCTAssertEqual(print["command"] as? String, "ams_filament_drying")
        XCTAssertEqual(print["sequence_id"] as? String, "42")
        XCTAssertEqual(print["ams_id"] as? Int, 1)
        XCTAssertEqual(print["mode"] as? Int, DryCtrlMode.onTime.rawValue)
        XCTAssertEqual(print["filament"] as? String, "PETG")
        XCTAssertEqual(print["temp"] as? Int, 65)
        XCTAssertEqual(print["duration"] as? Int, 8)
        XCTAssertEqual(print["humidity"] as? Int, 0)
        XCTAssertEqual(print["rotate_tray"] as? Bool, false)
        // DryRequest.start defaults coolingTemp to 50°C — Bambu's recommended
        // post-dry cooldown setpoint. Pin to that default so the encoded shape
        // matches what the printer actually receives in production.
        XCTAssertEqual(print["cooling_temp"] as? Int, 50)
        XCTAssertEqual(print["close_power_conflict"] as? Bool, false)
    }

    func testStopCommandMatchesProtocolShape() throws {
        let req = DryRequest.stop(amsId: 0, sequenceId: "7")
        let data = try JSONEncoder().encode(req)
        let dict = try JSONSerialization.jsonObject(with: data) as! [String: Any]
        let print = dict["print"] as! [String: Any]
        XCTAssertEqual(print["command"] as? String, "ams_filament_drying")
        XCTAssertEqual(print["mode"] as? Int, DryCtrlMode.off.rawValue)
        XCTAssertEqual(print["filament"] as? String, "")
        XCTAssertEqual(print["temp"] as? Int, 0)
        XCTAssertEqual(print["duration"] as? Int, 0)
    }

    // MARK: - Bitfield decoding

    func testInfoBitsDecodeRealFieldLayout() {
        // Real printer info "10002003" → bits 0..3 = 3 (N3F model), bits 4..7 = 0 (DryStatus.off).
        XCTAssertEqual(AmsReport.bits(in: "10002003", start: 0, count: 4), 3)
        XCTAssertEqual(AmsReport.bits(in: "10002003", start: 4, count: 4), 0)
        XCTAssertEqual(AmsReport.bits(in: "10002004", start: 0, count: 4), 4)  // N3S
    }

    func testInfoBitsDryingExample() {
        // Synthesised "drying" state: model=N3F (3) in bits 0..3, DryStatus.drying (2) in bits 4..7.
        // 0x23 = 0010 0011
        XCTAssertEqual(AmsReport.bits(in: "23", start: 0, count: 4), 3)
        XCTAssertEqual(AmsReport.bits(in: "23", start: 4, count: 4), 2)
    }

    func testInfoBitsHandlesPrefix() {
        XCTAssertEqual(AmsReport.bits(in: "0x10002003", start: 0, count: 4), 3)
        XCTAssertEqual(AmsReport.bits(in: "0X10002004", start: 0, count: 4), 4)
    }

    func testInfoBitsRejectsNonHex() {
        XCTAssertNil(AmsReport.bits(in: "ZZZ", start: 0, count: 4))
    }

    // MARK: - Snapshot derivation from a real H2D fixture
    // Captured payload: 1× AMS 2 Pro (id=0) + 2× AMS HT (id=128, 129), all idle.

    func testFixtureDecodesAllThreeAmsUnits() throws {
        let url = Bundle.module.url(forResource: "ams_report", withExtension: "json")!
        let data = try Data(contentsOf: url)
        let envelope = try JSONDecoder().decode(ReportEnvelope.self, from: data)
        let amsList = envelope.print?.ams?.ams ?? []
        XCTAssertEqual(amsList.count, 3)
        XCTAssertEqual(amsList.map { $0.id }, ["0", "128", "129"])
    }

    func testFixtureMainAmsIsN3FIdle() throws {
        let ams = try loadAms(at: 0)
        XCTAssertEqual(ams.id, "0")
        XCTAssertEqual(ams.humidity_raw, "24")
        XCTAssertEqual(ams.info, "10002003")

        // Auto-detect model from info bits 0..3
        XCTAssertEqual(ams.amsModel(), .n3f)

        let snap = ams.snapshot()!
        XCTAssertEqual(snap.amsId, 0)
        XCTAssertEqual(snap.model, .n3f)
        XCTAssertEqual(snap.humidityPercent, 24)
        XCTAssertTrue(snap.supportsDrying)
        XCTAssertFalse(snap.isCurrentlyDrying)
        XCTAssertEqual(snap.leftDryTimeMinutes, 0)
        XCTAssertEqual(snap.cannotDryReasons, [])
        XCTAssertEqual(snap.dominantFilamentType, "PLA")
        XCTAssertEqual(snap.currentTempC, 27.2)
    }

    func testFixtureSecondaryAmsAreN3SIdle() throws {
        for index in 1...2 {
            let ams = try loadAms(at: index)
            XCTAssertEqual(ams.amsModel(), .n3s, "ams index=\(index)")
            let snap = ams.snapshot()!
            XCTAssertEqual(snap.model, .n3s)
            XCTAssertEqual(snap.humidityPercent, 28)
            XCTAssertFalse(snap.isCurrentlyDrying)
            XCTAssertEqual(snap.leftDryTimeMinutes, 0)
        }
        XCTAssertEqual(try loadAms(at: 1).id, "128")
        XCTAssertEqual(try loadAms(at: 2).id, "129")
    }

    private func loadAms(at index: Int) throws -> AmsReport {
        let url = Bundle.module.url(forResource: "ams_report", withExtension: "json")!
        let data = try Data(contentsOf: url)
        let envelope = try JSONDecoder().decode(ReportEnvelope.self, from: data)
        return envelope.print!.ams!.ams![index]
    }

    func testSnapshotInvalidHumidity() throws {
        // -1 is the device's "invalid" sentinel.
        let json = """
        {"id":"0","humidity_raw":"-1","temp":"24.5","info":"00","dry_time":0}
        """.data(using: .utf8)!
        let report = try JSONDecoder().decode(AmsReport.self, from: json)
        let snap = report.snapshot(model: .n3f)
        XCTAssertNil(snap.humidityPercent)
        XCTAssertFalse(snap.isCurrentlyDrying)
    }

    func testCannotDryReasonsParsed() throws {
        let json = """
        {"id":"1","humidity_raw":"55","info":"00","dry_time":0,"dry_sf_reason":[2,7]}
        """.data(using: .utf8)!
        let report = try JSONDecoder().decode(AmsReport.self, from: json)
        let snap = report.snapshot(model: .n3s)
        XCTAssertEqual(snap.cannotDryReasons, [.amsBusy, .upgrading])
    }
}
