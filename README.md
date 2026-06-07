# BambuDry

> A small utility that automatically dehumidifies your Bambu printer's AMS
> units using the built-in heater, controlled over LAN. Free and open source.

<p align="center">
  <img src="docs/screenshots/dropdown.png" alt="BambuDry menu bar dropdown" width="380">
</p>

When humidity creeps up, the heater turns on. When humidity drops back to your
target, the heater turns off. Anti-cycling guards prevent rapid toggling.
That's it.

## Status

| Platform | Status | Download |
|----------|--------|----------|
| **macOS** (13 Ventura+) | Working | [Build from source](macos/README.md) |
| **Windows** (10/11) | Planned | — |

The macOS version is a SwiftUI menu bar app, daily-driver tested against an
AMS 2 Pro and two AMS HT units. Windows port is in planning — likely Avalonia
(C#/.NET) so the UI matches Windows native conventions while sharing logic
with the Mac version.

## Why it exists

Bambu's AMS HT and AMS 2 Pro have built-in heaters that can keep filament
spools dry. The official software supports manual drying for hours at a time
(rescue-wet-spool mode), but doesn't continuously maintain low humidity.
BambuDry is the missing piece: humidity-threshold-driven, set-and-forget,
gentle and brief instead of long and hot.

## Requirements

- A Bambu printer with at least one AMS 2 Pro or AMS HT
- **Developer Mode enabled on the printer** (Settings → General → Developer Mode).
  Without this the firmware silently rejects remote commands. See
  [docs/DEVELOPER_MODE.md](docs/DEVELOPER_MODE.md).
- The printer's LAN access code (Settings → WLAN on the touchscreen)

## Repository layout

```
bambudry/
├── docs/                     ← shared protocol documentation, screenshots
├── macos/                    ← SwiftUI menu bar app (current)
└── windows/                  ← Avalonia port (planned)
```

The macOS version's `BambuDryCore` package contains the pure-Swift hysteresis
controller and MQTT message types — when the Windows port lands, those same
shapes will be implemented in C#, both targeting the wire protocol documented
in [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Tip jar

If BambuDry saves you a wet-filament print, you can buy me a coffee:

- ☕ [Buy Me a Coffee](https://www.buymeacoffee.com/lccode)
- 💖 [GitHub Sponsors](https://github.com/sponsors/lcCode-dev)

Tips are appreciated but never required. The app is and will remain free.

## Contributing / issues

Issues and PRs welcome. Two things to keep in mind:

1. **Don't paste your printer's serial or access code** in issue text or logs.
   Both can be used to control your printer over LAN.
2. **Test fixtures should be sanitized** — see `macos/Tests/.../Fixtures/ams_report.json`
   for an example with placeholder values.

## License

[MIT](LICENSE) — do whatever you want with the code, no warranty.

BambuDry is not affiliated with or endorsed by Bambu Lab. "Bambu Lab" and
"AMS" are trademarks of Bambu Lab Co. Used here under nominative fair use to
describe what hardware this software works with.
