# Contributing to BambuDry

Thanks for taking the time to improve BambuDry. This project controls local
printer hardware, so the main rule is simple: keep reports and patches useful
without exposing printer credentials or serial numbers.

## Issues

- Check existing issues before opening a new one.
- Include your platform, app version or commit, printer model, AMS model, and a
  short description of what happened.
- Do not paste printer serial numbers, LAN access codes, account tokens, or
  unsanitized MQTT payloads.
- If the issue involves a security concern, use the process in
  [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Pull Requests

- Keep changes focused. Small PRs are easier to review and safer to merge.
- Include tests for behavior changes in `macos/Tests/BambuDryCoreTests` when
  the change touches parsing, command generation, or auto-dry decisions.
- Update docs when setup, protocol behavior, supported hardware, or user-facing
  workflows change.
- Sanitize fixtures before committing them.

## Local Checks

From the repository root:

```bash
cd macos
swift test
xcodebuild build -project BambuDryApp.xcodeproj -scheme BambuDryApp -destination 'platform=macOS' CODE_SIGNING_ALLOWED=NO
```

If you changed `macos/project.yml`, regenerate the Xcode project with
`xcodegen generate` before building locally.

## Hardware Safety

BambuDry should remain conservative by default. Changes that control heaters,
interpret AMS state, or bypass guardrails need extra care and tests. Prefer
fail-closed behavior when printer state is missing, ambiguous, or unsupported.

## License

By contributing, you agree that your contribution is provided under the
repository's MIT license.
