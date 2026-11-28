#!/usr/bin/env bash
# Checks what a built .app actually is: architecture, signature state, and whether Gatekeeper
# would let it run on another Mac. Reports facts; it never changes anything.
#
#   ./macos/verify-bundle.sh ["path/to/App.app"]

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
APP="${1:-$ROOT/artifacts/BrowserFlip Prototype.app}"

[ -d "$APP" ] || { echo "not found: $APP" >&2; exit 1; }

BIN="$APP/Contents/MacOS/BrowserFlipPrototype"
rule() { printf '%s\n' "----------------------------------------------------------------------"; }

rule
printf ' Bundle report: %s\n' "$APP"
rule
printf ' size              : %s\n' "$(du -sh "$APP" | cut -f1)"
printf ' architectures     : %s\n' "$(lipo -archs "$BIN" 2>/dev/null || echo unknown)"
printf ' minimum macOS     : %s\n' "$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$APP/Contents/Info.plist" 2>/dev/null || echo unset)"
printf ' bundle identifier : %s\n' "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Contents/Info.plist" 2>/dev/null || echo unset)"

# The product icon: the plist key AND the file it names, because either one missing leaves
# Finder and the Dock drawing the blank generic application page.
ICON_KEY="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$APP/Contents/Info.plist" 2>/dev/null || echo unset)"
printf ' CFBundleIconFile  : %s\n' "$ICON_KEY"
ICON_PATH="$APP/Contents/Resources/${ICON_KEY%.icns}.icns"
if [ "$ICON_KEY" != "unset" ] && [ -s "$ICON_PATH" ]; then
  printf ' icon file         : present, %s, largest %s px\n' \
    "$(du -h "$ICON_PATH" | cut -f1)" \
    "$(sips -g pixelWidth "$ICON_PATH" 2>/dev/null | awk '/pixelWidth/ {print $2}')"
else
  printf ' icon file         : MISSING (Finder and the Dock will show a blank page)\n'
fi

rule
printf ' Code signature\n'
rule
# --verbose=2 lists every nested file; only the verdict is interesting here.
if codesign --verify --deep --strict "$APP" >/dev/null 2>&1; then
  printf '   verify           : OK\n'
else
  printf '   verify           : FAILED (unsigned or broken)\n'
fi
AUTHORITY="$(codesign -dv --verbose=4 "$APP" 2>&1 | grep '^Authority=' | head -1 | cut -d= -f2-)"
printf '   authority         : %s\n' "${AUTHORITY:-none (ad-hoc or unsigned)}"

rule
printf ' Gatekeeper (would another Mac open it?)\n'
rule
spctl --assess --type execute --verbose=4 "$APP" 2>&1 | sed 's/^/   /'
printf '\n'
printf '   A "rejected" verdict here is EXPECTED for an unsigned local build.\n'
printf '   Shipping requires a Developer ID signature plus notarisation:\n'
printf '   see docs/macos-signing.md.\n'
rule
