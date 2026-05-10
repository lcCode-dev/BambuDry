using Meziantou.Framework.Win32;

namespace BambuDry.App.Storage;

/// <summary>
/// Stores the printer's LAN access code in Windows Credential Manager —
/// the Win32 equivalent of the macOS Keychain. Target name and account-key
/// shape mirror the macOS Keychain entry so the credential record reads
/// the same on both platforms (target = <c>dev.lcCode.BambuDry</c>,
/// account = printer serial).
/// </summary>
public static class CredentialStore
{
    /// <summary>Matches macOS Keychain <c>kSecAttrService</c> = "dev.lcCode.BambuDry".</summary>
    private const string TargetName = "dev.lcCode.BambuDry";

    public static void Save(string accessCode, string serial)
    {
        if (string.IsNullOrEmpty(serial)) throw new ArgumentException("serial must not be empty", nameof(serial));
        CredentialManager.WriteCredential(
            applicationName: ScopedTarget(serial),
            userName: serial,
            secret: accessCode,
            comment: "BambuDry — Bambu printer LAN access code",
            persistence: CredentialPersistence.LocalMachine);
    }

    public static string? Load(string serial)
    {
        if (string.IsNullOrEmpty(serial)) return null;
        var cred = CredentialManager.ReadCredential(ScopedTarget(serial));
        return cred?.Password;
    }

    public static void Delete(string serial)
    {
        if (string.IsNullOrEmpty(serial)) return;
        try { CredentialManager.DeleteCredential(ScopedTarget(serial)); }
        catch { /* ignore: best-effort cleanup */ }
    }

    /// <summary>
    /// Credential Manager target names are unique per (target, type). To allow
    /// multiple printers in the future, scope by serial: <c>{TargetName}\{serial}</c>.
    /// macOS uses (service, account) pairs that already discriminate by serial;
    /// Windows needs the discriminator baked into the target name.
    /// </summary>
    private static string ScopedTarget(string serial) => $"{TargetName}\\{serial}";
}
