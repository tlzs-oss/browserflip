#!/usr/bin/env bash
# Packages the built prototype as a macOS .app bundle so the menu-bar item, the Dock entry and
# LaunchServices all behave as they do for a real application.
#
#   ./macos/bundle.sh [Release|Debug]
#
# Prints the bundle path on success.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
CONFIG="${1:-Release}"
TFM="net10.0"
# A macOS .app must not depend on a .NET install being discoverable from Finder's environment,
# so the bundle is self-contained - which is also how Avalonia applications normally ship.
case "$(uname -m)" in
  arm64) RID="osx-arm64" ;;
  x86_64) RID="osx-x64" ;;
  *) echo "ERROR: unsupported architecture $(uname -m)" >&2; exit 1 ;;
esac
PUBLISH="$ROOT/artifacts/publish-$CONFIG-$RID"
APP="$ROOT/artifacts/BrowserFlip Prototype.app"

find_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return 0; fi
  for c in "$HOME/.dotnet/dotnet" /usr/local/share/dotnet/dotnet /usr/share/dotnet/dotnet; do
    [ -x "$c" ] && { printf '%s\n' "$c"; return 0; }
  done
  return 1
}
DOTNET="$(find_dotnet)" || { echo "ERROR: .NET SDK not found" >&2; exit 127; }

rm -rf "$PUBLISH" "$APP"
"$DOTNET" publish "$ROOT/src/BrowserFlip.Prototype.Desktop/BrowserFlip.Prototype.Desktop.csproj" \
  -c "$CONFIG" -f "$TFM" -r "$RID" --self-contained true -o "$PUBLISH" --nologo

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$HERE/Info.plist" "$APP/Contents/Info.plist"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/BrowserFlipPrototype"

# An ad-hoc signature is enough for local running; it is not a distributable signature.
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || \
  echo "note: ad-hoc codesign unavailable; the bundle still runs locally" >&2

printf '%s\n' "$APP"
