using Microsoft.Win32;

namespace BambuDry.App.Services;

/// <summary>
/// Register / unregister the app for launch at login via the per-user
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> registry key.
/// Mirrors the role of <c>SMAppService</c> on macOS but without the per-app
/// approval flow (HKCU writes don't require admin and don't surface a system
/// approval prompt).
/// </summary>
public static class LaunchAtLoginService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BambuDry";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
    }

    public static void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
