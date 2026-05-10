# BambuDry — Windows port

C# / .NET 8 / Avalonia 11 port of the macOS app. Both implementations target
the same MQTT wire format documented in
[../docs/PROTOCOL.md](../docs/PROTOCOL.md).

## Status

| Layer | Status | Notes |
|---|---|---|
| `BambuDry.Core` | ✅ shipping | Pure C# port of the Swift core; no platform deps. |
| `BambuDry.Core.Tests` | ✅ 25/25 passing | xUnit port of the XCTest suite, including `ams_report.json` verbatim. |
| MQTTnet `LanTransport` | ✅ verified | Live-tested against P/X/A-series Bambu printers. |
| Settings storage | ✅ shipping | `%APPDATA%\BambuDry\config.json` matches macOS schema field-for-field. |
| Credential storage | ✅ shipping | Windows Credential Manager via `Meziantou.Framework.Win32.CredentialManager`, target name `dev.lcCode.BambuDry\<serial>`. |
| Launch at login | ✅ shipping | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry write, controlled from the Advanced tab. |
| Avalonia tray + dropdown | ✅ shipping | Borderless 380px popup, drag-by-header, anchored to the tray. State-driven tray icon (idle / warm / drying / offline). |
| Per-AMS controls | ✅ shipping | Auto toggle, threshold sliders, manual Stop — all wired to per-unit settings, persisted with debounced disk writes. |
| Tabbed Settings (Printer / Defaults / Advanced) | ✅ shipping | Includes editable defaults, dry-run toggle, launch-at-login toggle, recent-publishes log. |
| Inno Setup installer | ✅ shipping | [`installer/BambuDry.iss`](installer/README.md) — per-user install by default, optional desktop / startup shortcuts. |
| GitHub Actions CI | ✅ shipping | [`build-windows.yml`](../.github/workflows/build-windows.yml) — builds, tests, publishes, signs (Azure Trusted Signing), packages installer, attaches to rolling `latest-windows` pre-release on every push to `main`. |
| Code signing | 🟡 pending Azure setup | Workflow integrates Azure Trusted Signing via OIDC. Builds proceed unsigned until the `AZURE_*` secrets are set. |

## Layout

```
windows/
├── BambuDry.sln
├── installer/
│   ├── BambuDry.iss             ← Inno Setup script
│   └── README.md                 ← installer build notes
├── scripts/
│   └── generate-icons.ps1        ← regenerate tray PNGs + app.ico
├── src/
│   ├── BambuDry.Core/            ← pure-C# port of macOS BambuDryCore
│   │   ├── Models/Models.cs      ← AmsModel, DryStatus, AutoDrySettings, AmsSnapshot
│   │   ├── Net/Messages.cs       ← DryRequest, ReportEnvelope, AmsReport bitfield decode
│   │   ├── Net/LanTransport.cs   ← MQTTnet wrapper (TLS, bblp/<code>, pushall)
│   │   └── Control/              ← AutoDryController hysteresis, Orchestrator
│   └── BambuDry.App/             ← Avalonia 11 desktop app
│       ├── ViewModels/           ← AppViewModel, AmsRowViewModel, SettingsViewModel
│       ├── Views/                ← MainWindow, PrinterSetupWindow, SettingsWindow
│       ├── Storage/              ← AppConfig, ConfigStore, CredentialStore
│       ├── Services/             ← LaunchAtLoginService
│       └── Assets/               ← app.ico (multi-res) + tray-{state}.png
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

To build the signed installer the way CI does (requires Inno Setup 6+
installed locally):

```powershell
dotnet publish windows\src\BambuDry.App `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o windows\installer\publish

& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" `
    /DMyAppVersion=0.1.0 `
    /DPublishDir=publish `
    windows\installer\BambuDry.iss

# Output: windows\installer\output\BambuDry-0.1.0-Setup.exe
```

## Cross-platform parity

- Settings JSON schema matches macOS byte-for-byte (`printerName`, `serial`,
  `lanIP`, `amsSettings: { "<id>": {...} }`, `defaultSettings`, `dryRunMode`,
  `launchAtLogin`).
- Credential record naming matches the macOS Keychain (`dev.lcCode.BambuDry`,
  account = printer serial).
- The 13 `AutoDryControllerTests` and 9 `MessagesTests` mirror their Swift
  counterparts case-for-case, validating the same `ams_report.json` fixture.

## Code signing

CI uses [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/)
via OIDC federated credentials — no long-lived secret stored in GitHub.
Set the following repository secrets to enable signing:

| Secret | Where it comes from |
|---|---|
| `AZURE_TENANT_ID` | Microsoft Entra tenant hosting the Trusted Signing account |
| `AZURE_CLIENT_ID` | App registration with the *Trusted Signing Certificate Profile Signer* role on the account, with OIDC federation configured for this repo |
| `AZURE_SUBSCRIPTION_ID` | Subscription containing the Trusted Signing account |
| `AZURE_TS_ENDPOINT` | Region endpoint, e.g. `https://eus.codesigning.azure.net/` |
| `AZURE_TS_ACCOUNT` | Name of the Trusted Signing account |
| `AZURE_TS_PROFILE` | Name of the *Public Trust* certificate profile |

Identity validation in the Trusted Signing portal takes 1–3 business days.
Until then the workflow still builds and ships installers — they just
trip SmartScreen until enough downloads build reputation, or until
signing is wired in.
