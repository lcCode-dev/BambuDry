#!/bin/bash
#
# Build, sign, notarize, and staple a distributable BambuDry.app.
#
# Prerequisites (run once):
#
#   1. Apple Developer Program membership (active).
#
#   2. Developer ID Application certificate installed:
#        Xcode → Settings → Accounts → your Apple ID → Manage Certificates
#        Click +, choose "Developer ID Application".
#
#   3. App-specific password for notarytool:
#        appleid.apple.com → Sign-In and Security → App-Specific Passwords
#        Create one (label it "bambudry-notary").
#
#   4. Store the notarytool credentials in your Keychain:
#        xcrun notarytool store-credentials "bambudry-notary" \
#          --apple-id "your@email.com" \
#          --team-id "YOUR_TEAM_ID" \
#          --password "the-app-specific-password-from-step-3"
#
#   5. Set BAMBUDRY_TEAM_ID either in the environment or below:
#
#        export BAMBUDRY_TEAM_ID="YOUR_TEAM_ID"
#
# Then from the repo's macos/ directory:
#
#   ./scripts/release.sh
#
# Output: ./build/BambuDry.app  (signed + notarized + stapled)
#         ./build/BambuDry.zip  (ready for GitHub Release upload)
#
set -euo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# Configuration

TEAM_ID="${BAMBUDRY_TEAM_ID:-}"
NOTARY_PROFILE="${BAMBUDRY_NOTARY_PROFILE:-bambudry-notary}"

if [[ -z "$TEAM_ID" ]]; then
    echo "ERROR: BAMBUDRY_TEAM_ID is not set."
    echo "Find your team ID at developer.apple.com → Membership Details, or run:"
    echo "  xcrun altool --list-providers -u 'your@email.com' -p '@keychain:bambudry-notary'"
    exit 1
fi

# Move to script's parent directory (macos/)
cd "$(dirname "$0")/.."

BUILD_DIR="$(pwd)/build"
ARCHIVE_PATH="$BUILD_DIR/BambuDry.xcarchive"
EXPORT_PATH="$BUILD_DIR/export"
APP_PATH="$EXPORT_PATH/BambuDry.app"
FINAL_APP_PATH="$BUILD_DIR/BambuDry.app"
FINAL_ZIP_PATH="$BUILD_DIR/BambuDry.zip"
EXPORT_OPTS="$BUILD_DIR/ExportOptions.plist"

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

# ─────────────────────────────────────────────────────────────────────────────
# 1. Regenerate Xcode project from project.yml (in case it's stale)

echo "==> Regenerating Xcode project from project.yml…"
if ! command -v xcodegen >/dev/null; then
    echo "ERROR: xcodegen not found. Install with: brew install xcodegen"
    exit 1
fi
xcodegen generate

# ─────────────────────────────────────────────────────────────────────────────
# 2. Archive (Release configuration with Developer ID signing)

echo "==> Archiving…"
xcodebuild \
    -project BambuDryApp.xcodeproj \
    -scheme BambuDryApp \
    -configuration Release \
    -destination "platform=macOS" \
    -archivePath "$ARCHIVE_PATH" \
    DEVELOPMENT_TEAM="$TEAM_ID" \
    CODE_SIGN_STYLE=Automatic \
    CODE_SIGN_IDENTITY="Developer ID Application" \
    archive | xcbeautify

# ─────────────────────────────────────────────────────────────────────────────
# 3. Export with Developer ID

cat > "$EXPORT_OPTS" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>method</key>
    <string>developer-id</string>
    <key>teamID</key>
    <string>$TEAM_ID</string>
    <key>signingStyle</key>
    <string>automatic</string>
    <key>signingCertificate</key>
    <string>Developer ID Application</string>
</dict>
</plist>
EOF

echo "==> Exporting signed .app…"
xcodebuild \
    -exportArchive \
    -archivePath "$ARCHIVE_PATH" \
    -exportPath "$EXPORT_PATH" \
    -exportOptionsPlist "$EXPORT_OPTS" | xcbeautify

# ─────────────────────────────────────────────────────────────────────────────
# 4. Notarize (requires internet; takes 1–10 minutes)

echo "==> Submitting to Apple notary service (this can take a few minutes)…"
NOTARY_ZIP="$BUILD_DIR/BambuDry-for-notary.zip"
ditto -c -k --keepParent "$APP_PATH" "$NOTARY_ZIP"

xcrun notarytool submit "$NOTARY_ZIP" \
    --keychain-profile "$NOTARY_PROFILE" \
    --wait

# ─────────────────────────────────────────────────────────────────────────────
# 5. Staple the notarization ticket onto the .app

echo "==> Stapling notarization ticket…"
xcrun stapler staple "$APP_PATH"

# ─────────────────────────────────────────────────────────────────────────────
# 6. Verify Gatekeeper accepts it

echo "==> Verifying with Gatekeeper…"
spctl --assess --verbose --type install "$APP_PATH"

# ─────────────────────────────────────────────────────────────────────────────
# 7. Move to final location and produce distribution zip

mv "$APP_PATH" "$FINAL_APP_PATH"
ditto -c -k --keepParent "$FINAL_APP_PATH" "$FINAL_ZIP_PATH"

# Cleanup intermediates
rm -rf "$ARCHIVE_PATH" "$EXPORT_PATH" "$NOTARY_ZIP" "$EXPORT_OPTS"

echo ""
echo "==> Done."
echo "    Signed app:        $FINAL_APP_PATH"
echo "    Distribution zip:  $FINAL_ZIP_PATH"
echo ""
echo "Upload BambuDry.zip to a GitHub Release. Users can download, unzip,"
echo "and drag BambuDry.app to /Applications without macOS Gatekeeper warnings."
