using BambuDry.App.Storage;
using BambuDry.Core;

namespace BambuDry.App.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BambuDry.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void LoadFromMissingPathReturnsEmpty()
    {
        var loaded = ConfigStore.LoadFrom(Path.Combine(_tempDir, "does-not-exist.json"));
        Assert.False(loaded.IsConfigured);
        Assert.Empty(loaded.AmsSettings);
        Assert.Equal("My Bambu Printer", loaded.PrinterName);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var original = new AppConfig
        {
            PrinterName = "P1S Lab Unit",
            Serial      = "01S00ABCDEF12345",
            LanIp       = "10.0.0.50",
            DryRunMode  = false,
            DefaultSettings = new AutoDrySettings { Enabled = true, HighThreshold = 32, LowThreshold = 17 },
        };
        ConfigStore.SaveTo(original, _path);
        Assert.True(File.Exists(_path));

        var loaded = ConfigStore.LoadFrom(_path);
        Assert.Equal(original.PrinterName,     loaded.PrinterName);
        Assert.Equal(original.Serial,          loaded.Serial);
        Assert.Equal(original.LanIp,           loaded.LanIp);
        Assert.Equal(original.DryRunMode,      loaded.DryRunMode);
        Assert.Equal(original.DefaultSettings, loaded.DefaultSettings);
    }

    [Fact]
    public void LoadFromCorruptJsonReturnsEmpty()
    {
        // Truncated payload — must not crash the app on launch.
        File.WriteAllText(_path, "{\"printerName\": \"lab\", \"serial\":");
        var loaded = ConfigStore.LoadFrom(_path);
        Assert.False(loaded.IsConfigured);
        Assert.Empty(loaded.AmsSettings);
    }

    [Fact]
    public void SaveCreatesParentDirectory()
    {
        var nested = Path.Combine(_tempDir, "nested", "deeper", "config.json");
        ConfigStore.SaveTo(AppConfig.Empty() with { PrinterName = "x" }, nested);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void SaveOverwritesExistingFile()
    {
        ConfigStore.SaveTo(AppConfig.Empty() with { PrinterName = "first" }, _path);
        ConfigStore.SaveTo(AppConfig.Empty() with { PrinterName = "second" }, _path);
        Assert.Equal("second", ConfigStore.LoadFrom(_path).PrinterName);
    }

    [Fact]
    public void SaveAtomicityCleansUpTempFile()
    {
        ConfigStore.SaveTo(AppConfig.Empty(), _path);
        var siblings = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(siblings);   // tmp file should have been moved into place
    }
}
