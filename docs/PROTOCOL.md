# Bambu LAN Protocol Reference

This is a reverse-engineered reference for the LAN MQTT control surface used
by Bambu printers, focused on the AMS drying commands that BambuDry uses.
Compatible with X1, P1, A1, and (apparently) H-series firmware as of mid-2026.

Sourced by reading the Bambu Studio open-source slicer's `DeviceCore` and
`DeviceManager` modules and corroborating with live MQTT capture from a real
P2S printer.

## Connection

- **Host**: the printer's LAN IP (find on touchscreen → Settings → WLAN)
- **Port**: `8883`
- **TLS**: required, with a **self-signed printer certificate** — clients
  must accept the cert via manual trust evaluation
- **MQTT**: version 3.1.1 (the printer rejects v5)
- **Auth**: username `bblp`, password = LAN access code (8-char hex string
  shown on touchscreen → Settings → WLAN → Access Code)
- **Keepalive**: 60 seconds is fine
- **clean session**: yes

## Topics

| Topic | Direction | Purpose |
|-------|-----------|---------|
| `device/<serial>/report` | printer → client | Status push (humidity, temp, drying state, print state, etc.) |
| `device/<serial>/request` | client → printer | Commands (drying, push-all status, etc.) |

The serial number is the printer's S/N (16 hex characters, on touchscreen →
Settings → Device).

## Reports — what BambuDry reads

Reports come in two forms:

- **Diff updates** (most common): partial JSON; we ignore most fields
- **Full status**: sent on connect, after `pushall`, or after some state changes

Full status contains a `print.ams.ams` array with one entry per AMS unit:

```json
{
  "print": {
    "ams": {
      "ams": [
        {
          "id": "0",                                         // numeric string
          "humidity": "4",                                   // legacy AMS humidity level (0-5); ignore for AMS 2 Pro/HT
          "humidity_raw": "24",                              // actual humidity %
          "temp": "27.2",                                    // °C as string-encoded float
          "info": "10002003",                                // hex bitfield, see below
          "dry_time": 0,                                     // remaining minutes if drying
          "dry_setting": {
            "dry_filament": "",
            "dry_temperature": -1,
            "dry_duration": -1
          },
          "dry_sf_reason": [],                               // array of "cannot dry" reason codes
          "tray": [...]                                      // per-slot filament info
        }
      ]
    }
  }
}
```

### The `info` bitfield

`info` is a hex string (no `0x` prefix); decode to an integer and read bits:

| Bits | Meaning | Values |
|------|---------|--------|
| 0–3  | AMS model | `3` = N3F (AMS 2 Pro), `4` = N3S (AMS HT) |
| 4–7  | Dry status | `0` Off, `1` Checking, `2` Drying, `3` Cooling, `4` Stopping, `5` Error, `6` CannotStopHeatOutOfControl, `7` PrdTesting |
| 18–19 | Fan 1 status | `0` Off, `1` On |
| 20–21 | Fan 2 status | `0` Off, `1` On |
| 22–23 | Dry sub-status | `0` Off, `1` Heating, `2` Dehumidify |

Example: `info = "10002003"` → `0x10002003` → bits 0–3 = `3` (N3F), bits 4–7 = `0` (off).

### `dry_sf_reason` — why drying is blocked

When the printer refuses a dry command for a stateful reason, this array
populates with one or more codes:

| Code | Meaning |
|------|---------|
| `0` | Task occupied |
| `1` | Insufficient power |
| `2` | AMS busy |
| `3` | Consumable at AMS outlet |
| `4` | Initiating AMS drying |
| `5` | Not supported in 2D mode |
| `6` | Drying in progress |
| `7` | Upgrading |
| `8` | Insufficient power, plug in adapter |
| `10` | Filament at AMS outlet, manual unload required |

If your dry commands are silently failing and `dry_sf_reason` is `[]`, the
issue is **not** one of these — it's almost certainly Developer Mode being
off. See [DEVELOPER_MODE.md](DEVELOPER_MODE.md).

## Capability flag (`fun2`)

Top-level `print.fun2` is a hex string of capability bits. The relevant one:

| Bit | Capability |
|-----|------------|
| 5 | `is_support_remote_dry` — must be 1 for the firmware to even consider remote dry commands |

If bit 5 is 0 the printer doesn't support remote drying at all (older firmware
or non-N3F/N3S AMS), and no client can drive it.

## Commands BambuDry sends

### Start dehumidify

```json
{
  "print": {
    "command": "ams_filament_drying",
    "sequence_id": "<monotonic int as string>",
    "ams_id": <int>,
    "mode": 1,
    "filament": "PLA",
    "temp": 45,
    "duration": 1,
    "humidity": 0,
    "rotate_tray": false,
    "cooling_temp": 50,
    "close_power_conflict": false
  }
}
```

Notes:
- `temp` minimum the AMS heater accepts is **45°C** despite the Bambu Studio
  UI allowing values down to 35. Values below 45 are silently dropped.
- `cooling_temp = 50` matches Bambu Studio's UI default. Sending `0` works
  too but seems risky.
- `duration` is in hours. Auto-stop fires on humidity, not duration, so any
  small value works.

### Stop dehumidify

```json
{
  "print": {
    "command": "ams_filament_drying",
    "sequence_id": "<monotonic int as string>",
    "ams_id": <int>,
    "mode": 0,
    "filament": "",
    "temp": 0,
    "duration": 0,
    "humidity": 0,
    "rotate_tray": false,
    "cooling_temp": 0,
    "close_power_conflict": false
  }
}
```

### Force a full status push

```json
{ "pushing": { "sequence_id": "0", "command": "pushall" } }
```

Useful at startup since regular updates are diff-only and frequently omit the
AMS block entirely.

## sequence_id

A monotonic counter unique per command. The printer appears to dedupe based on
recently-seen sequence_ids. Restarting your client at sequence_id=1 right
after sending sequence_id=15 may cause the next several commands to be
ignored. **Seed from `Date().timeIntervalSince1970`** (current Unix time) to
guarantee monotonicity across client restarts.

## QoS

Bambu Studio publishes commands at QoS 0. Either 0 or 1 works in practice.

## Self-signed certificate handling

The printer's TLS cert is self-signed. Your MQTT client must:

1. Enable TLS
2. Disable strict CA validation
3. Provide a callback that **accepts any cert** the printer presents (we are
   trusting the LAN endpoint, not the certificate chain)

Skipping step 3 results in silent TLS handshake failures with no useful error
message.

## Sample report fixture

A sanitized real-printer capture lives at
`../macos/Tests/BambuDryCoreTests/Fixtures/ams_report.json`. Useful for
testing decoders without needing an actual printer running.
