#!/usr/bin/env bash
# Builds a single-file AppImage for the BrowserFlip Avalonia prototype (linux-arm64).
#
# An AppImage is just the official type-2 runtime with a squashfs image appended, so the
# only external piece is that runtime. appimagetool is not in the Ubuntu archive, so the
# runtime is built from the upstream source (github.com/AppImage/type2-runtime); see
# docs/linux-packaging-fonts.md for why and what that costs.
#
#   make-appimage.sh <publish-dir> <output.AppImage> [runtime]
set -euo pipefail

PUBLISH="${1:?usage: make-appimage.sh <publish-dir> <output.AppImage> [runtime]}"
OUT="${2:?usage: make-appimage.sh <publish-dir> <output.AppImage> [runtime]}"
RUNTIME="${3:-$HOME/src/type2-runtime/src/runtime/runtime}"
APPDIR="$(mktemp -d)/BrowserFlip.AppDir"

[ -x "$RUNTIME" ] || { echo "FAIL: no AppImage runtime at $RUNTIME" >&2; exit 1; }

mkdir -p "$APPDIR"/usr/{bin,share/{applications,fonts,licenses/noto-cjk,icons/hicolor/256x256/apps}}
cp -r "$PUBLISH"/. "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/BrowserFlipPrototype"

# CJK fonts are deliberately NOT bundled. Bundling NotoSansCJK-Regular.ttc makes the
# application abort at startup with "free(): invalid pointer" (SIGABRT) -- measured, with
# the decisive control being an otherwise identical directory that is simply empty. See
# docs/known-defect-font-bundling.md for the full A/B table and dependency versions. The
# cause is not established, so this is not attributed to any particular library.
#
# The code path is kept, opt-in, so the defect can be re-tested after a toolkit upgrade:
#   BROWSERFLIP_BUNDLE_CJK=1 make-appimage.sh ...
# Expect it to produce an AppImage that does not start.
FONT=/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc
if [ "${BROWSERFLIP_BUNDLE_CJK:-0}" != "1" ]; then
    echo "note: CJK font not bundled (known defect; see docs/known-defect-font-bundling.md)." >&2
    echo "      Japanese and Simplified Chinese need a system CJK font, e.g. fonts-noto-cjk." >&2
    FONT=""
fi
if [ -n "$FONT" ] && [ -r "$FONT" ]; then
    cp "$FONT" "$APPDIR/usr/share/fonts/"
    # SIL OFL 1.1 requires the copyright notice and the licence text to travel with the font.
    awk '/^License: SIL-1.1/{f=1} f' /usr/share/doc/fonts-noto-cjk/copyright \
        > "$APPDIR/usr/share/licenses/noto-cjk/OFL.txt"
    printf 'Copyright 2010-2012 Google Corporation\nNoto Sans CJK, licensed under the SIL Open Font License 1.1.\nFull licence text: OFL.txt\n' \
        > "$APPDIR/usr/share/licenses/noto-cjk/COPYRIGHT.txt"
elif [ -n "$FONT" ]; then
    echo "WARN: $FONT not found; the AppImage will rely on system CJK fonts" >&2
fi

cat > "$APPDIR/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"

# The bundled font must NOT be served to fontconfig straight off the AppImage's squashfs
# mount. Measured on Ubuntu 26.04 arm64: pointing XDG_DATA_DIRS at the mounted
# usr/share aborts the process with "free(): invalid pointer" 6 times out of 6, whether
# the path is prepended or appended, while the identical font on an ordinary filesystem
# works 3 times out of 3. FreeType/fontconfig mmap font files and the FUSE mount does
# not give them what they expect. So stage the font on a real filesystem once and point
# fontconfig at that copy instead.
FONT_SRC="$HERE/usr/share/fonts/NotoSansCJK-Regular.ttc"
if [ -r "$FONT_SRC" ]; then
    FONT_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/browserflip/fonts"
    if [ ! -r "$FONT_DIR/NotoSansCJK-Regular.ttc" ]; then
        mkdir -p "$FONT_DIR" 2>/dev/null \
            && cp "$FONT_SRC" "$FONT_DIR/NotoSansCJK-Regular.ttc.tmp$$" 2>/dev/null \
            && mv "$FONT_DIR/NotoSansCJK-Regular.ttc.tmp$$" "$FONT_DIR/NotoSansCJK-Regular.ttc" 2>/dev/null
    fi
    if [ -r "$FONT_DIR/NotoSansCJK-Regular.ttc" ]; then
        # Building fontconfig's cache for this .ttc inside the application aborts it with
        # "free(): invalid pointer" (measured: 7/7 with a cold cache, 0/5 once warm). Do
        # that indexing in a separate fc-cache process first, so the application always
        # starts against a cache that already exists.
        if [ ! -d "$FONT_DIR/.fc" ]; then
            if command -v fc-cache >/dev/null 2>&1; then
                fc-cache -f "$FONT_DIR" >/dev/null 2>&1 && mkdir -p "$FONT_DIR/.fc"
            fi
        fi
        # Appended, so a CJK font already installed on the host wins; this copy is only
        # the fallback for a machine that has none.
        export XDG_DATA_DIRS="${XDG_DATA_DIRS:-/usr/local/share:/usr/share}:$(dirname "$FONT_DIR")"
    fi
fi
exec "$HERE/usr/bin/BrowserFlipPrototype" "$@"
RUN
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/browserflip.desktop" <<'DSK'
[Desktop Entry]
Type=Application
Name=BrowserFlip
Comment=Show installed browsers and change the default one
Exec=BrowserFlipPrototype
Icon=browserflip
Categories=Utility;Settings;
Terminal=false
DSK
cp "$APPDIR/browserflip.desktop" "$APPDIR/usr/share/applications/"

ICON=$(find "$PUBLISH" -iname '*.png' 2>/dev/null | head -1)
if [ -n "$ICON" ]; then
    cp "$ICON" "$APPDIR/browserflip.png"
    cp "$ICON" "$APPDIR/usr/share/icons/hicolor/256x256/apps/browserflip.png"
fi
command -v desktop-file-validate >/dev/null && desktop-file-validate "$APPDIR/browserflip.desktop"

SQFS="$(mktemp -u).squashfs"
mksquashfs "$APPDIR" "$SQFS" -root-owned -noappend -comp zstd -Xcompression-level 19 -quiet
cat "$RUNTIME" "$SQFS" > "$OUT"
chmod +x "$OUT"
rm -f "$SQFS"; rm -rf "$(dirname "$APPDIR")"

echo "built : $OUT"
echo "size  : $(stat -c %s "$OUT") bytes ($(du -h "$OUT" | cut -f1))"
echo "sha256: $(sha256sum "$OUT" | cut -d' ' -f1)"
