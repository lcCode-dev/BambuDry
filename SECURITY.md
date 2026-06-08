# Security Policy

## Supported Versions

BambuDry is early-stage software. Security fixes target the current `main`
branch unless a tagged release is called out separately.

## Reporting a Vulnerability

Please do not open a public issue for security reports.

Use GitHub's private vulnerability reporting for this repository. Include:

- What you found and how it can be reproduced.
- The affected platform, commit, release, or workflow.
- Whether printer serial numbers, LAN access codes, signing credentials, or
  other secrets may be exposed.

If you need to share logs or MQTT payloads, remove printer serial numbers, LAN
access codes, IP addresses you do not want public, account tokens, and any
other identifying data first.

## Scope

Security-sensitive areas include:

- GitHub Actions workflows, release signing, notarization, and secrets.
- Printer authentication, LAN MQTT transport, and command generation.
- Parsing of printer or AMS status reports.
- Any behavior that can start, stop, or extend AMS heater operation.

Reports about unsupported hardware, missing features, or ordinary bugs can use
public GitHub issues as long as they do not include credentials or unsanitized
printer data.
