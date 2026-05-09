# BambuDry — macOS

The macOS implementation of BambuDry. SwiftUI menu bar app.

For the project overview, motivation, and tip jar links see the top-level
[README](../README.md).

## Requirements

- macOS 13 (Ventura) or later
- Bambu printer with at least one AMS 2 Pro (`N3F`) or AMS HT (`N3S`)
- **Developer Mode enabled on the printer** (Settings → General → Developer Mode).
  Without this, the firmware silently rejects remote heat commands — humidity
  will display, but auto-dehumidify won't actually start the heater.
- The printer's LAN access code (Settings → WLAN on the printer's touchscreen,
  the 8-character code)

## Build

```bash
brew install xcodegen
git clone <this-repo> BambuDry
cd BambuDry
xcodegen generate
open BambuDryApp.xcodeproj
```

In Xcode:

1. Select the BambuDryApp target → Signing & Capabilities → set your Team
2. Press ⌘R

The app launches as a menu bar item (no Dock icon). Click the humidity-drop
icon to set up the printer on first run.

## How it works

```
┌────────────┐        MQTT/TLS         ┌──────────┐
│ BambuDry   │ ◄──── reports ────────  │ Printer  │
│ (this app) │ ────  start/stop ────►  │ + AMS    │
└────────────┘                         └──────────┘
        │
        ▼  every 5s per AMS
   ┌─────────────────┐
   │ AutoDryController│  hysteresis state machine
   └─────────────────┘
```

- **`Sources/BambuDryCore/`** — Pure Swift package. Hysteresis controller,
  MQTT message types, `LANTransport` (CocoaMQTT-backed), Orchestrator that
  ties them together. No SwiftUI, no AppKit.
- **`Sources/BambuDryCLI/`** — Command-line tool for testing the full pipeline
  against a real printer without the UI:
  ```bash
  swift run bambudry-cli --ip 192.168.1.X --serial XXX --code XXXXXXXX --dry-run
  ```
- **`Tests/BambuDryCoreTests/`** — XCTest unit tests. Includes a captured
  (sanitized) report fixture from a real H2D printer.
- **`BambuDryApp/`** — SwiftUI menu bar app. Imports BambuDryCore.
- **`project.yml`** — xcodegen spec. Source of truth for the Xcode project.

## Settings

- **Per-AMS** (in the dropdown): Auto on/off, Start ≥ humidity, Stop ≤ humidity,
  Stop button (manual abort)
- **Defaults** (Settings → Defaults): default thresholds for any AMS without an
  override, plus anti-cycling guard times
- **Advanced** (Settings → Advanced): launch-at-login, dry-run mode, reconnect,
  recent-publish log

## Data

- Config: `~/Library/Application Support/BambuDry/config.json`
- Access code: macOS Keychain (service `dev.lcCode.BambuDry`,
  account = printer serial)

## Releasing a signed `.app`

For distribution to non-developer users (drag-to-Applications, no Gatekeeper
warnings), use the included release script:

```bash
# One-time setup — store notarytool credentials in your Keychain:
xcrun notarytool store-credentials "bambudry-notary" \
  --apple-id "your@email.com" \
  --team-id "YOUR_TEAM_ID" \
  --password "your-app-specific-password"

# Then for each release:
export BAMBUDRY_TEAM_ID="YOUR_TEAM_ID"
./scripts/release.sh
```

Output: `build/BambuDry.app` (signed + notarized + stapled) and
`build/BambuDry.zip` (ready to upload to a GitHub Release).

The script handles archive → Developer ID signing → Apple notary submission
→ stapling → Gatekeeper verification → final zip. Takes 3–10 minutes
depending on Apple's notary queue.

## Limitations / known issues

- **One printer per app.** Data model supports multi-printer, UI doesn't.
- **No auto-retry on connect failure.** If the printer reboots or Wi-Fi blips
  during initial connect, click Settings → Advanced → Reconnect.
- **`swift test` fails locally** on Xcode 26.4 SDK due to a clang module-map
  issue compiling CocoaMQTT's Obj-C dependency. Use `xcodebuild test` instead,
  or test through the CLI / live app.
- **Menu bar icon doesn't pulse while heating** — `MenuBarExtra`'s IPC layer
  to the system menu bar process can't sustain animation rates. Static red
  flame icon is what we landed on.

## Why does it need Developer Mode?

Bambu's firmware has a separate gate for remote control commands beyond just
LAN MQTT auth. Developer Mode unlocks this gate. The setup view warns about
this; if your AMS displays humidity but never actually heats when auto-dry
fires, this is almost certainly why.

## License

[MIT](../LICENSE) — see the top-level repo.
