using System.Text.Json;
using BambuDry.Core;
using BambuDry.Core.Net;

namespace BambuDry.Core.Tests;

public class MessagesTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ams_report.json");

    // MARK: - Outbound encode

    [Fact]
    public void StartCommandMatchesProtocolShape()
    {
        var req = DryRequest.Start(amsId: 1, filament: "PETG", tempC: 65, hours: 8, sequenceId: "42");
        var json = JsonSerializer.Serialize(req, WireJson.Options);
        var doc = JsonDocument.Parse(json);
        var print = doc.RootElement.GetProperty("print");

        Assert.Equal("ams_filament_drying", print.GetProperty("command").GetString());
        Assert.Equal("42",                  print.GetProperty("sequence_id").GetString());
        Assert.Equal(1,                     print.GetProperty("ams_id").GetInt32());
        Assert.Equal((int)DryCtrlMode.OnTime, print.GetProperty("mode").GetInt32());
        Assert.Equal("PETG",                print.GetProperty("filament").GetString());
        Assert.Equal(65,                    print.GetProperty("temp").GetInt32());
        Assert.Equal(8,                     print.GetProperty("duration").GetInt32());
        Assert.Equal(0,                     print.GetProperty("humidity").GetInt32());
        Assert.False(                       print.GetProperty("rotate_tray").GetBoolean());
        // DryRequest.Start defaults coolingTemp to 50°C — Bambu's recommended
        // post-dry cooldown setpoint. Pin to that default so the encoded shape
        // matches what the printer actually receives in production.
        Assert.Equal(50,                    print.GetProperty("cooling_temp").GetInt32());
        Assert.False(                       print.GetProperty("close_power_conflict").GetBoolean());
    }

    [Fact]
    public void StopCommandMatchesProtocolShape()
    {
        var req = DryRequest.Stop(amsId: 0, sequenceId: "7");
        var json = JsonSerializer.Serialize(req, WireJson.Options);
        var doc = JsonDocument.Parse(json);
        var print = doc.RootElement.GetProperty("print");

        Assert.Equal("ams_filament_drying",   print.GetProperty("command").GetString());
        Assert.Equal((int)DryCtrlMode.Off,    print.GetProperty("mode").GetInt32());
        Assert.Equal("",                      print.GetProperty("filament").GetString());
        Assert.Equal(0,                       print.GetProperty("temp").GetInt32());
        Assert.Equal(0,                       print.GetProperty("duration").GetInt32());
    }

    // MARK: - Bitfield decoding

    [Fact]
    public void InfoBitsDecodeRealFieldLayout()
    {
        // Real printer info "10002003" → bits 0..3 = 3 (N3F), bits 4..7 = 0 (DryStatus.Off).
        Assert.Equal(3, AmsReport.Bits("10002003", start: 0, count: 4));
        Assert.Equal(0, AmsReport.Bits("10002003", start: 4, count: 4));
        Assert.Equal(4, AmsReport.Bits("10002004", start: 0, count: 4));  // N3S
    }

    [Fact]
    public void InfoBitsDryingExample()
    {
        // Synthesised "drying" state: model=N3F (3) bits 0..3, DryStatus.Drying (2) bits 4..7.
        // 0x23 = 0010 0011
        Assert.Equal(3, AmsReport.Bits("23", start: 0, count: 4));
        Assert.Equal(2, AmsReport.Bits("23", start: 4, count: 4));
    }

    [Fact]
    public void InfoBitsHandlesPrefix()
    {
        Assert.Equal(3, AmsReport.Bits("0x10002003", start: 0, count: 4));
        Assert.Equal(4, AmsReport.Bits("0X10002004", start: 0, count: 4));
    }

    [Fact]
    public void InfoBitsRejectsNonHex()
    {
        Assert.Null(AmsReport.Bits("ZZZ", start: 0, count: 4));
    }

    // MARK: - Snapshot derivation from a real H2D fixture
    // Captured payload: 1× AMS 2 Pro (id=0) + 2× AMS HT (id=128, 129), all idle.

    [Fact]
    public void FixtureDecodesAllThreeAmsUnits()
    {
        var envelope = LoadFixture();
        var amsList = envelope.Print?.Ams?.Ams ?? new List<AmsReport>();
        Assert.Equal(3, amsList.Count);
        Assert.Equal(new[] { "0", "128", "129" }, amsList.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void FixtureMainAmsIsN3FIdle()
    {
        var ams = LoadAms(0);
        Assert.Equal("0", ams.Id);
        Assert.Equal("24", ams.HumidityRaw);
        Assert.Equal("10002003", ams.Info);

        Assert.Equal(AmsModel.N3F, ams.GetAmsModel());

        var snap = ams.Snapshot()!;
        Assert.Equal(0, snap.AmsId);
        Assert.Equal(AmsModel.N3F, snap.Model);
        Assert.Equal(24, snap.HumidityPercent);
        Assert.True(snap.SupportsDrying);
        Assert.False(snap.IsCurrentlyDrying);
        Assert.Equal(0, snap.LeftDryTimeMinutes);
        Assert.Empty(snap.CannotDryReasons);
        Assert.Equal("PLA", snap.DominantFilamentType);
        Assert.Equal(27.2, snap.CurrentTempC);
    }

    [Fact]
    public void FixtureSecondaryAmsAreN3SIdle()
    {
        for (var i = 1; i <= 2; i++)
        {
            var ams = LoadAms(i);
            Assert.Equal(AmsModel.N3S, ams.GetAmsModel());
            var snap = ams.Snapshot()!;
            Assert.Equal(AmsModel.N3S, snap.Model);
            Assert.Equal(28, snap.HumidityPercent);
            Assert.False(snap.IsCurrentlyDrying);
            Assert.Equal(0, snap.LeftDryTimeMinutes);
        }
        Assert.Equal("128", LoadAms(1).Id);
        Assert.Equal("129", LoadAms(2).Id);
    }

    [Fact]
    public void SnapshotInvalidHumidity()
    {
        // -1 is the device's "invalid" sentinel.
        const string json = """{"id":"0","humidity_raw":"-1","temp":"24.5","info":"00","dry_time":0}""";
        var report = JsonSerializer.Deserialize<AmsReport>(json, WireJson.Options)!;
        var snap = report.Snapshot(AmsModel.N3F);
        Assert.Null(snap.HumidityPercent);
        Assert.False(snap.IsCurrentlyDrying);
    }

    [Fact]
    public void CannotDryReasonsParsed()
    {
        const string json = """{"id":"1","humidity_raw":"55","info":"00","dry_time":0,"dry_sf_reason":[2,7]}""";
        var report = JsonSerializer.Deserialize<AmsReport>(json, WireJson.Options)!;
        var snap = report.Snapshot(AmsModel.N3S);
        Assert.Equal(new[] { CannotDryReason.AmsBusy, CannotDryReason.Upgrading }, snap.CannotDryReasons);
    }

    private static ReportEnvelope LoadFixture()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        return JsonSerializer.Deserialize<ReportEnvelope>(bytes, WireJson.Options)!;
    }

    private static AmsReport LoadAms(int index) => LoadFixture().Print!.Ams!.Ams![index];
}
