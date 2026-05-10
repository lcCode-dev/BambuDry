using System.Text.Json;
using BambuDry.App.Storage;
using BambuDry.Core;

namespace BambuDry.App.Tests;

public class AppConfigTests
{
    private static readonly JsonSerializerOptions Opts = ConfigStore.JsonOpts;

    [Fact]
    public void RoundTripsThroughJson()
    {
        var config = new AppConfig
        {
            PrinterName = "lcPrint3d",
            Serial      = "01S00A2BCD012345",
            LanIp       = "192.168.1.42",
            DryRunMode  = false,
            LaunchAtLogin = true,
            DefaultSettings = new AutoDrySettings
            {
                Enabled        = true,
                HighThreshold  = 28,
                LowThreshold   = 16,
                MinOnMinutes   = 10,
                MinOffMinutes  = 7,
                RunDuringPrint = true,
                TargetTemp     = 45,
            },
            AmsSettings = new Dictionary<string, AutoDrySettings>
            {
                ["0"]   = new() { Enabled = true,  HighThreshold = 30, LowThreshold = 18, TargetTemp = 45, MinOnMinutes = 5, MinOffMinutes = 5, RunDuringPrint = false },
                ["128"] = new() { Enabled = false, HighThreshold = 35, LowThreshold = 20, TargetTemp = 45, MinOnMinutes = 5, MinOffMinutes = 5, RunDuringPrint = false },
            },
        };

        var json    = JsonSerializer.Serialize(config, Opts);
        var decoded = JsonSerializer.Deserialize<AppConfig>(json, Opts)!;

        Assert.Equal(config.PrinterName,           decoded.PrinterName);
        Assert.Equal(config.Serial,                decoded.Serial);
        Assert.Equal(config.LanIp,                 decoded.LanIp);
        Assert.Equal(config.DryRunMode,            decoded.DryRunMode);
        Assert.Equal(config.LaunchAtLogin,         decoded.LaunchAtLogin);
        Assert.Equal(config.DefaultSettings,       decoded.DefaultSettings);
        Assert.Equal(2,                            decoded.AmsSettings.Count);
        Assert.Equal(config.AmsSettings["0"],      decoded.AmsSettings["0"]);
        Assert.Equal(config.AmsSettings["128"],    decoded.AmsSettings["128"]);
    }

    [Fact]
    public void DeserializesMacSchemaWithoutWindowsOnlyFields()
    {
        // Sample of the JSON produced by the macOS app — no dryRunMode /
        // launchAtLogin keys. Both should fall back to their schema defaults
        // so a config file copied between platforms remains valid.
        const string macJson = """
        {
          "printerName": "lcPrint3d",
          "serial": "01S00A2BCD012345",
          "lanIP": "192.168.1.42",
          "amsSettings": {
            "0": { "enabled": true, "highThreshold": 30, "lowThreshold": 18,
                   "targetTemp": 45, "runDuringPrint": false,
                   "minOnMinutes": 5, "minOffMinutes": 5 }
          },
          "defaultSettings": {
            "enabled": false, "highThreshold": 30, "lowThreshold": 18,
            "targetTemp": 45, "runDuringPrint": false,
            "minOnMinutes": 5, "minOffMinutes": 5
          }
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(macJson, Opts)!;
        Assert.Equal("lcPrint3d",        config.PrinterName);
        Assert.Equal("01S00A2BCD012345", config.Serial);
        Assert.Equal("192.168.1.42",     config.LanIp);
        Assert.True(config.IsConfigured);
        Assert.True(config.DryRunMode);                  // defaults to true (safety)
        Assert.False(config.LaunchAtLogin);              // defaults to false
        Assert.Single(config.AmsSettings);
        Assert.True(config.AmsSettings["0"].Enabled);
        Assert.Equal(30, config.AmsSettings["0"].HighThreshold);
    }

    [Fact]
    public void IsConfiguredRequiresBothSerialAndLanIp()
    {
        Assert.False(AppConfig.Empty().IsConfigured);
        Assert.False((AppConfig.Empty() with { Serial = "abc" }).IsConfigured);
        Assert.False((AppConfig.Empty() with { LanIp  = "1.2.3.4" }).IsConfigured);
        Assert.True ((AppConfig.Empty() with { Serial = "abc", LanIp = "1.2.3.4" }).IsConfigured);
    }

    [Fact]
    public void SettingsForAmsIdReturnsOverrideElseDefault()
    {
        var config = AppConfig.Empty() with
        {
            DefaultSettings = new AutoDrySettings { HighThreshold = 30 },
            AmsSettings = new Dictionary<string, AutoDrySettings>
            {
                ["128"] = new() { HighThreshold = 22 },
            },
        };
        Assert.Equal(22, config.SettingsForAmsId(128).HighThreshold);
        Assert.Equal(30, config.SettingsForAmsId(0).HighThreshold);    // falls back to default
        Assert.Equal(30, config.SettingsForAmsId(999).HighThreshold);
    }

    [Fact]
    public void WithSettingsUpdatesOrInsertsByAmsId()
    {
        var config = AppConfig.Empty();
        var updated = config.WithSettings(new AutoDrySettings { Enabled = true, HighThreshold = 25 }, 0);
        Assert.True(updated.AmsSettings["0"].Enabled);
        Assert.Equal(25, updated.AmsSettings["0"].HighThreshold);
        // Original is unchanged (record immutability).
        Assert.Empty(config.AmsSettings);
    }
}
