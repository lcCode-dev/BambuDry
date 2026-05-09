# Developer Mode

BambuDry won't actually heat your AMS unless **Developer Mode** is enabled on
the printer. The firmware accepts our MQTT connection, lets us read humidity
state, and even acknowledges our drying commands at the protocol level — but
silently no-ops them when this flag is off.

## How to enable

On the printer's touchscreen:

1. Settings (gear icon)
2. General → **Developer Mode**
3. Toggle it on
4. Re-enter the LAN access code in BambuDry's setup if asked

## How to verify it's working

After enabling Developer Mode and connecting BambuDry:

1. In BambuDry's menu bar dropdown, drag a Slot's **Start ≥** slider below the
   current humidity reading
2. Within ~30 seconds you should see:
   - Status changes to "Heat command sent — retry in 30s"
   - Then "Dehumidifying — can stop in 5m" (or similar)
   - The AMS row's `temp` reading climbs from ambient (~25°C) toward 45°C

If the status keeps cycling between "Heat command sent" → "Heat command sent"
without the AMS ever reporting back as drying, **Developer Mode is off**.

## Why does Bambu gate it like this?

Best guess: protecting users from arbitrary network attackers issuing remote
commands once they have the LAN access code. Developer Mode is an explicit
"yes I know what I'm doing" toggle that can only be flipped from physical
access to the printer's screen. It's not a bad design — it just costs setup
clarity.
