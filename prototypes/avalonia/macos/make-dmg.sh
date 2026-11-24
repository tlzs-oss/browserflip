#!/usr/bin/env bash
# Builds the .app (via bundle.sh) and wraps it in a distributable disk image.
#
#   ./macos/make-dmg.sh [Release|Debug]
#
# Prints the .dmg path on success. Uses only tools that ship with macOS.
#
# The image is NOT signed or notarised. See docs/macos-signing.md: without a Developer ID
# signature and an Apple notarisation ticket, Gatekeeper will refuse to open it on another
# Mac. This script therefore produces a build artefact for testing, not a public download.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
CONFIG="${1:-Release}"

APP_PATH="$("$HERE/bundle.sh" "$CONFIG" | tail -1)"
APP_NAME="$(basename "$APP_PATH")"
VOLUME_NAME="BrowserFlip Prototype"
STAGING="$ROOT/artifacts/dmg-staging"
DMG="$ROOT/artifacts/BrowserFlip-Prototype-$(uname -m).dmg"

rm -rf "$STAGING" "$DMG"
mkdir -p "$STAGING"
cp -R "$APP_PATH" "$STAGING/"
# The conventional drag-to-install target.
ln -s /Applications "$STAGING/Applications"

hdiutil create \
  -volname "$VOLUME_NAME" \
  -srcfolder "$STAGING" \
  -ov -format UDZO \
  -quiet \
  "$DMG"

rm -rf "$STAGING"

printf 'app: %s\n' "$APP_PATH" >&2
printf 'size: %s\n' "$(du -h "$DMG" | cut -f1)" >&2
printf '%s\n' "$DMG"
