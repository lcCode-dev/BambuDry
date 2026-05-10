# BambuDry — Windows port

C# / .NET 8 / Avalonia 11 port of the macOS app. **In progress** on the
`windows-port` branch — a working foundation is in place but the UI is
deliberately minimal pending iteration.

## Status

| Layer | Status | Notes |
|---|---|---|
| `BambuDry.Core` | ✅ done | Pure-C# port of macOS `BambuDryCore`. No platform deps. |
| `BambuDry.Core.Tests` | ✅ 25/25 passing | xUnit port of the XCTest suite, including the `ams_report.json` fixture verbatim. |
| MQTTnet `LanTransport` | ✅ compiles | Untested against a real printer yet; structure mirrors the Swift `LANTransport`. |
| Settings storage | ✅ done | `%APPDATA%\BambuDry\config.json`, schema matches Mac `AppConfig`. |
| Credential storage | ✅ done | Windows Credential Manager via `Meziantou.Framework.Win32.CredentialManager`, target name `dev.lcCode.BambuDry\<serial>`. |
| Launch at login | ✅ done | `HKCU\…\Run` registry write. |
| Avalonia app shell | 🟡 minimal | Setup window works; main window shows live AMS snapshots; settings UI is read-only placeholder. |
| System tray icon | 🔴 not yet | Will replace `MainWindow` as the primary entry point. |
| CI workflow | 🔴 not yet | `build-windows.yml` to mirror `build-dmg.yml` once a working build exists. |

## Layout

```
windows/
├── BambuDry.sln
├── src/
│   ├── BambuDry.Core/          ← pure-C# port of macOS BambuDryCore
│   │   ├── Models/Models.cs    ← AmsModel, DryStatus, AutoDrySettings, AmsSnapshot
│   │   ├── Net/Messages.cs     ← DryRequest, ReportEnvelope, AmsReport bitfield decode
│   │   ├── Net/LanTransport.cs ← MQTTnet wrapper (TLS, bblp/<code>, pushall)
│   │   └── Control/            ← AutoDryController hysteresis, Orchestrator
│   └── BambuDry.App/           ← Avalonia desktop app
│       ├── ViewModels/         ← AppViewModel (CommunityToolkit.Mvvm)
│       ├── Views/              ← MainWindow, PrinterSetupWindow, SettingsWindow
│       ├── Storage/            ← AppConfig, ConfigStore, CredentialStore
│       └── Services/           ← LaunchAtLogin
└── tests/
    └── BambuDry.Core.Tests/
        ├── AutoDryControllerTests.cs   ← 13 tests, port of macOS suite
        ├── MessagesTests.cs            ← 9 tests incl. fixture decode
        └── Fixtures/ams_report.json    ← copied verbatim from macos/
```

## Building locally

```powershell
# from repo root
dotnet build windows\BambuDry.sln
dotnet test  windows\tests\BambuDry.Core.Tests
dotnet run   --project windows\src\BambuDry.App
```

## Cross-platform parity

The protocol shapes in `BambuDry.Core` are a near-mechanical translation of
`macos/Sources/BambuDryCore/`. Both implementations target the wire protocol
documented in [../docs/PROTOCOL.md](../docs/PROTOCOL.md).

- Settings JSON schema matches macOS byte-for-byte (`printerName`, `serial`,
  `lanIP`, `amsSettings: { "<id>": {...} }`, `defaultSettings`).
- Credential record naming matches macOS Keychain (`dev.lcCode.BambuDry`,
  account = printer serial).
- The 8 `MessagesTests` and 13 `AutoDryControllerTests` mirror their Swift
  counterparts case-for-case.

## Next steps

1. SettingsViewModel for editable two-way bindings (Core stays immutable; UI gets a mutable wrapper).
2. System tray icon + borderless dropdown popup (Avalonia `TrayIcon`).
3. End-to-end test against a real printer on LAN (reconnect logic, dry-run mode).
4. `.github/workflows/build-windows.yml` mirroring the macOS DMG workflow.
