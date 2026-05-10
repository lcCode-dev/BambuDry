# BambuDry — Windows port

C# / .NET 8 / Avalonia 11 port of the macOS app. Both implementations target
the same MQTT wire format documented in
[../docs/PROTOCOL.md](../docs/PROTOCOL.md).

## Status

| Layer | Status | Notes |
|---|---|---|
| `BambuDry.Core` | ✅ shipping | Pure-C# port of the Swift core. No platform deps. |
| `BambuDry.Core.Tests` | ✅ 25/25 passing | xUnit port of the XCTest suite, including `ams_report.json` verbatim. |
| `BambuDry.App.Tests` | ✅ 17/17 passing | Storage round-trips, cross-platform schema, real Credential Manager round-trips. |
| MQTTnet `LanTransport` | ✅ verified | Live-tested against P/X/A-series Bambu printers. |
| Settings storage | ✅ shipping | `%APPDATA%\BambuDry\config.json`. CamelCase + case-insensitive — the JSON file is interchangeable with the macOS app's. |
| Credential storage | ✅ shipping | Windows Credential Manager via `Meziantou.Framework.Win32.CredentialManager`. Target `dev.lcCode.BambuDry\<serial>` mirrors the macOS Keychain shape. |
| Launch at login | ✅ shipping | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, toggled from Settings → Advanced. |
| Avalonia tray + dropdown | ✅ shipping | Borderless 380 px popup, drag-by-header, bottom-right anchor that grows upward as AMS rows arrive. State-driven tray icon (idle / warm / drying / offline). |
| Pin / auto-hide | ✅ shipping | Mac-style: unpinned auto-hides on focus loss; pin keeps it open and remembers the dragged position across hide/show. |
| Embedded setup form | ✅ shipping | First-run UX inlines the Add-printer form in the dropdown body, no separate dialog. |
| Per-AMS controls | ✅ shipping | Auto toggle, threshold sliders, manual Stop — wired to per-unit settings (`Config.AmsSettings[<id>]`) with debounced 300 ms disk writes. |
| Tabbed Settings (Printer / Defaults / Advanced) | ✅ shipping | Editable defaults, dry-run toggle, launch-at-login toggle, recent-publishes log (50-entry ring buffer of `Sent` + `DryRunSkipped` events). |
| Inno Setup installer | ✅ shipping | [`installer/BambuDry.iss`](installer/README.md) — per-user install by default. End-to-end install/uninstall verified locally. ~32 MB compressed. |
| GitHub Actions CI | ✅ shipping | [`build-windows.yml`](../.github/workflows/build-windows.yml) — builds, runs both test suites, publishes, signs (Azure Trusted Signing), packages installer, attaches to rolling `latest-windows` pre-release on every push to `main`. Tagged `v*` pushes also create a versioned release. |
| Code signing | 🟡 pending Azure setup | Workflow integrates Azure Trusted Signing via OIDC federated credentials. Builds proceed unsigned (with a CI warning) until the `AZURE_*` secrets are populated. |

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
│       ├── Views/                ← MainWindow, PrinterSetupView (UserControl), SettingsWindow
│       ├── Storage/              ← AppConfig, ConfigStore, CredentialStore
│       ├── Services/             ← LaunchAtLoginService
│       └── Assets/               ← app.ico (multi-res) + tray-{state}.png
└── tests/
    ├── BambuDry.Core.Tests/      ← 25 cross-platform pure-logic tests
    │   ├── AutoDryControllerTests.cs   ← 13 tests, port of macOS suite
    │   ├── MessagesTests.cs            ← 9 tests incl. fixture decode
    │   └── Fixtures/ams_report.json    ← copied verbatim from macos/
    └── BambuDry.App.Tests/       ← 17 Windows-specific tests
        ├── AppConfigTests.cs           ← JSON round-trip + macOS schema parse
        ├── ConfigStoreTests.cs         ← Save/Load + corrupt-JSON recovery + atomic writes
        └── CredentialStoreTests.cs     ← Win32 Credential Manager round-trip with cleanup
```

## Building locally

```powershell
# from repo root
dotnet build windows\BambuDry.sln
dotnet test  windows\BambuDry.sln
dotnet run   --project windows\src\BambuDry.App
```

To build the installer locally (requires [Inno Setup 6+](https://jrsoftware.org/isinfo.php)):

```powershell
dotnet publish windows\src\BambuDry.App `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o windows\installer\publish

# iscc.exe lives under Program Files (x86) for system installs, or
# %LOCALAPPDATA%\Programs\Inno Setup 6 for per-user installs.
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" `
    /DMyAppVersion=0.1.0 `
    /DPublishDir=publish `
    windows\installer\BambuDry.iss

# Output: windows\installer\output\BambuDry-0.1.0-Setup.exe (~32 MB)
```

## Cross-platform parity

- **Wire protocol** — both apps target the same MQTT format documented in
  [../docs/PROTOCOL.md](../docs/PROTOCOL.md). The 13 `AutoDryControllerTests`
  and 9 `MessagesTests` mirror their Swift counterparts case-for-case and
  validate against the same `ams_report.json` fixture.
- **Settings JSON** — same schema (`printerName`, `serial`, `lanIP`,
  `amsSettings: { "<id>": {...} }`, `defaultSettings`, plus Windows-only
  `dryRunMode` / `launchAtLogin` that the Mac side ignores). CamelCase
  keys + case-insensitive reads, so a `config.json` is interchangeable
  between platforms.
- **Credentials** — Win32 Credential Manager target name and account-key
  shape (`dev.lcCode.BambuDry` / serial) mirrors the macOS Keychain.
- **App icon** — `windows/src/BambuDry.App/Assets/app.ico` is built from
  the macOS app's `Assets.xcassets/AppIcon.appiconset/` PNGs (see
  [`scripts/generate-icons.ps1`](scripts/generate-icons.ps1)), so any
  Mac-side art refresh propagates to Windows by re-running the script.

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
