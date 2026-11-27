#!/usr/bin/env bash
# Builds the macOS product icon (.icns) from the repository's existing PNG icon assets.
#
#   ./macos/make-icns.sh [output.icns]
#
# Prints the .icns path on success. Uses only tools that ship with macOS (sips, iconutil).
#
# Why this exists: the .app had no CFBundleIconFile and an empty Contents/Resources, so Finder
# and the Dock drew the blank generic application page.
#
# The sources are assets/icon/png/browserflip-{16,32,64,128,256,512}.png, which are generated
# from the single artwork definition in scripts/icon/IconGen/Design.cs. Each iconset slot is
# filled by the PNG that is already exactly that many pixels across - nothing is resampled
# here, so the icon in the bundle is bit-for-bit the icon in the repository.
#
# The 1024 px slot (icon_512x512@2x) is deliberately left out: there is no 1024 px source, the
# generator is Windows-only (it renders with WPF), and upscaling 512 px would only hand macOS a
# blurry image it can produce for itself. Finder's largest size therefore scales the 512 px
# entry, which is what it would do with an upscaled one anyway.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
REPO="$(cd "$ROOT/../.." && pwd)"
PNG_DIR="$REPO/assets/icon/png"
OUT="${1:-$ROOT/artifacts/BrowserFlip.icns}"

command -v iconutil >/dev/null 2>&1 || { echo "ERROR: iconutil not found (macOS only)" >&2; exit 127; }

# slot name : source pixel size
SLOTS=(
  "icon_16x16:16"
  "icon_16x16@2x:32"
  "icon_32x32:32"
  "icon_32x32@2x:64"
  "icon_128x128:128"
  "icon_128x128@2x:256"
  "icon_256x256:256"
  "icon_256x256@2x:512"
  "icon_512x512:512"
)

STAGING="$(mktemp -d)"
ICONSET="$STAGING/BrowserFlip.iconset"
mkdir -p "$ICONSET"
trap 'rm -rf "$STAGING"' EXIT

for entry in "${SLOTS[@]}"; do
  slot="${entry%%:*}"
  size="${entry##*:}"
  src="$PNG_DIR/browserflip-$size.png"
  [ -f "$src" ] || { echo "ERROR: missing source icon $src" >&2; exit 1; }
  # Confirm the source really is the size the slot claims, rather than trusting the file name.
  actual="$(sips -g pixelWidth "$src" | awk '/pixelWidth/ {print $2}')"
  [ "$actual" = "$size" ] || { echo "ERROR: $src is ${actual}px, expected ${size}px" >&2; exit 1; }
  cp "$src" "$ICONSET/$slot.png"
done

mkdir -p "$(dirname "$OUT")"
rm -f "$OUT"
iconutil --convert icns --output "$OUT" "$ICONSET"

printf 'entries: %s\n' "${#SLOTS[@]}" >&2
printf 'size: %s\n' "$(du -h "$OUT" | cut -f1)" >&2
printf '%s\n' "$OUT"
