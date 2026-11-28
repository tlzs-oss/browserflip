#!/usr/bin/env bash
# Runs the prototype's capability self-test on a REAL Linux desktop session.
#
#   ./linux/run-selftest.sh <output-dir> [Release|Debug]
#
# A GUI toolkit needs a display server. On a CI runner there is no physical screen, so this
# starts Xvfb - a real X11 server that renders to memory. That is genuinely X11: the app opens
# a real X11 window through the same Avalonia backend a user's machine would use. It is NOT a
# headless stub, and it is NOT a substitute for testing on Wayland or on a real desktop, which
# this script cannot do and does not claim to.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
OUT="${1:?usage: run-selftest.sh <output-dir> [config]}"
CONFIG="${2:-Release}"

find_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return 0; fi
  for c in "$HOME/.dotnet/dotnet" /usr/share/dotnet/dotnet /usr/local/share/dotnet/dotnet; do
    [ -x "$c" ] && { printf '%s\n' "$c"; return 0; }
  done
  return 1
}
DOTNET="$(find_dotnet)" || { echo "ERROR: .NET SDK not found" >&2; exit 127; }

command -v Xvfb >/dev/null 2>&1 || {
  echo "ERROR: Xvfb is required (apt-get install -y xvfb)" >&2
  exit 127
}

PUBLISH="$ROOT/artifacts/linux-$CONFIG"
rm -rf "$PUBLISH"
"$DOTNET" publish "$ROOT/src/BrowserFlip.Prototype.Desktop/BrowserFlip.Prototype.Desktop.csproj" \
  -c "$CONFIG" -f net10.0 -o "$PUBLISH" --nologo

mkdir -p "$OUT"

# A real X11 server rendering to memory.
export DISPLAY=":99"
Xvfb "$DISPLAY" -screen 0 1280x1024x24 -nolisten tcp >"$OUT/xvfb.log" 2>&1 &
XVFB_PID=$!
cleanup() { kill "$XVFB_PID" 2>/dev/null || true; wait "$XVFB_PID" 2>/dev/null || true; }
trap cleanup EXIT

# Wait for the server to accept connections rather than sleeping a fixed amount.
for _ in $(seq 1 50); do
  if xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then break; fi
  sleep 0.2
done
xdpyinfo -display "$DISPLAY" >/dev/null 2>&1 || { echo "ERROR: Xvfb never came up" >&2; exit 1; }

# Report what session the adapter will see, so the capability matrix in the output is explicable.
export XDG_SESSION_TYPE="${XDG_SESSION_TYPE:-x11}"
printf 'session: XDG_SESSION_TYPE=%s XDG_CURRENT_DESKTOP=%s\n' \
  "$XDG_SESSION_TYPE" "${XDG_CURRENT_DESKTOP:-<unset>}" | tee "$OUT/session.txt"

set +e
"$PUBLISH/BrowserFlipPrototype" --selftest "$OUT"
STATUS=$?
set -e

printf 'self-test exit code: %s\n' "$STATUS"
exit "$STATUS"
