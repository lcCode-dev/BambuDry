# BambuDry — Windows port (planned)

The Windows port hasn't started yet. Likely stack: **Avalonia UI** (C#/.NET 8)
so the same codebase can also be a viable cross-platform alternative to the
SwiftUI macOS version long-term.

## Why Avalonia

- Excellent Windows-native feel (XAML / DataTemplates similar to WPF)
- Cross-platform — same codebase could replace the SwiftUI Mac version eventually
- `MQTTnet` is the canonical .NET MQTT library, supports TLS with custom cert
  validation (matches our self-signed-cert requirement)
- System tray support via `TrayIcon`, similar UX to `MenuBarExtra`
- Microsoft Store and direct-download distribution both straightforward

## Scope

The plan is to mirror the macOS feature set 1:1:

- Menu/system-tray icon with humidity status
- Click → dropdown showing one row per AMS with humidity bar, Auto toggle,
  thresholds, manual Stop
- Pinnable window (standard WPF/Avalonia window suffices — no special tricks
  needed since Windows doesn't have macOS's `MenuBarExtra` IPC quirks)
- Settings window with Printer / Defaults / Advanced tabs
- Run-at-login via Task Scheduler or HKCU `Run` registry key
- Credential storage in DPAPI (`ProtectedData.Protect`)
- Config in `%APPDATA%/BambuDry/config.json`

## What's portable from `macos/Sources/BambuDryCore`

The pure-Swift core is small (~700 lines) and contains no Apple-specific APIs:

- `AutoDryController` — hysteresis state machine
- `Models` — settings, snapshots, enums
- `Messages` — Codable wire format (start/stop/report)
- `Orchestrator` — ties them together

A C# port of the above should be a near-mechanical translation. The protocol
shapes are documented in [../docs/PROTOCOL.md](../docs/PROTOCOL.md), so any
future implementation can target that spec directly without diving into the
Swift code.

## Estimated effort

3–4 weeks part-time for a polished Windows release including code signing
and an installer (Inno Setup or WiX).
