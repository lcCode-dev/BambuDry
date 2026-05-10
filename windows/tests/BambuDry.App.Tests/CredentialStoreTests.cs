using BambuDry.App.Storage;

namespace BambuDry.App.Tests;

/// <summary>
/// These tests touch the real Windows Credential Manager. We use a unique
/// dummy serial per test (with cleanup in IDisposable) so we don't disturb
/// any actual BambuDry credential the dev box has cached.
/// </summary>
public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _serial;

    public CredentialStoreTests()
    {
        _serial = $"TEST{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        try { CredentialStore.Delete(_serial); } catch { /* best effort */ }
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        CredentialStore.Save("8charkey", _serial);
        Assert.Equal("8charkey", CredentialStore.Load(_serial));
    }

    [Fact]
    public void SaveOverwritesExistingCredential()
    {
        CredentialStore.Save("first", _serial);
        CredentialStore.Save("second", _serial);
        Assert.Equal("second", CredentialStore.Load(_serial));
    }

    [Fact]
    public void DeleteRemovesCredential()
    {
        CredentialStore.Save("doomed", _serial);
        CredentialStore.Delete(_serial);
        Assert.Null(CredentialStore.Load(_serial));
    }

    [Fact]
    public void LoadOnMissingSerialReturnsNull()
    {
        Assert.Null(CredentialStore.Load($"NEVERSEEN{Guid.NewGuid():N}"));
    }

    [Fact]
    public void DeleteOnMissingSerialIsSilent()
    {
        // Should not throw — we depend on idempotent cleanup paths.
        CredentialStore.Delete($"NEVERSEEN{Guid.NewGuid():N}");
    }

    [Fact]
    public void SaveRejectsEmptySerial()
    {
        Assert.Throws<ArgumentException>(() => CredentialStore.Save("anything", string.Empty));
    }
}
