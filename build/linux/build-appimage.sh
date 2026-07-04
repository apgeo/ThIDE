#!/usr/bin/env bash
#
# Build a portable AppImage from a self-contained linux-x64 publish of TherionProc.
#
# Unlike the .deb (which installs system-wide), an AppImage is a single distro-agnostic executable
# the user can download and run directly — no install, no root. It carries the same self-contained
# .NET payload, so only base desktop libraries (glibc, X11, fontconfig, GL) are expected on the host.
# A desktop entry + icon are embedded so integrators like AppImageLauncher can add a menu shortcut
# and register the Therion file types (mirroring TherionProc.Services.FileAssociationCatalog, kept in
# sync by InstallerAssociationConsistencyTests).
#
# Usage:
#   build/linux/build-appimage.sh <publish-dir> <version> [out-dir]
#
# Example:
#   dotnet publish TherionProc/TherionProc.csproj -m:1 -c Release -r linux-x64 \
#       --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
#   build/linux/build-appimage.sh publish/linux-x64 0.3.0
#   # -> ./TherionProc-0.3.0-x86_64.AppImage
#
# Requires: appimagetool. If not on PATH it is downloaded from GitHub (override the URL with
# $APPIMAGETOOL_URL, or point $APPIMAGETOOL at an existing binary). Icon extraction uses icotool
# (icoutils) or ImageMagick if present.

set -euo pipefail

PUBLISH_DIR="${1:-}"
VERSION="${2:-0.0.0}"
OUT_DIR="${3:-$PWD}"

if [[ -z "$PUBLISH_DIR" || ! -d "$PUBLISH_DIR" ]]; then
  echo "error: publish dir '$PUBLISH_DIR' not found" >&2
  echo "usage: $0 <publish-dir> <version> [out-dir]" >&2
  exit 1
fi
if [[ ! -f "$PUBLISH_DIR/TherionProc" ]]; then
  echo "error: '$PUBLISH_DIR/TherionProc' (the published apphost) not found" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ICO="$REPO_ROOT/TherionProc/Assets/avalonia-logo.ico"

# The Therion file types, mirroring FileAssociationCatalog.Types ("ext|description").
EXTS=(
  "th|Therion survey source"
  "th2|Therion 2D map / scrap"
  "thconfig|Therion configuration"
  "thc|Therion configuration"
  "thl|Therion library"
  "xvi|Therion XVI scan"
)

APPDIR="$(mktemp -d)/TherionProc.AppDir"
trap 'rm -rf "$(dirname "$APPDIR")"' EXIT
mkdir -p "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
         "$APPDIR/usr/share/metainfo"

echo "==> Staging published files -> AppDir/usr/bin"
cp -a "$PUBLISH_DIR/." "$APPDIR/usr/bin/"
chmod 0755 "$APPDIR/usr/bin/TherionProc"

# Entry point. Resolves its own dir so the bundled apphost finds its sibling native libs.
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="$HERE/usr/bin:${LD_LIBRARY_PATH:-}"
exec "$HERE/usr/bin/TherionProc" "$@"
EOF
chmod 0755 "$APPDIR/AppRun"

# Build the MimeType list.
MIME_LIST=""
for pair in "${EXTS[@]}"; do
  ext="${pair%%|*}"
  MIME_LIST="${MIME_LIST}application/x-therion-${ext};"
done

# Desktop entry (embedded in the AppImage; Exec is the in-AppImage binary name, no absolute path).
DESKTOP="$APPDIR/therionproc.desktop"
cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=TherionProc
Comment=Therion survey editor
Exec=TherionProc %F
Icon=therionproc
Terminal=false
Categories=Science;Education;Utility;
MimeType=$MIME_LIST
EOF
cp "$DESKTOP" "$APPDIR/usr/share/applications/therionproc.desktop"

# Menu icon: extract the largest frame from the .ico if a converter is available.
PNG="$APPDIR/usr/share/icons/hicolor/256x256/apps/therionproc.png"
if command -v icotool >/dev/null 2>&1; then
  tmpico="$(mktemp -d)"
  icotool -x -o "$tmpico" "$ICO" >/dev/null 2>&1 || true
  biggest="$(ls -1S "$tmpico"/*.png 2>/dev/null | head -n1 || true)"
  [[ -n "$biggest" ]] && cp "$biggest" "$PNG"
  rm -rf "$tmpico"
elif command -v magick >/dev/null 2>&1; then
  magick "${ICO}[0]" -resize 256x256 "$PNG" || true
elif command -v convert >/dev/null 2>&1; then
  convert "${ICO}[0]" -resize 256x256 "$PNG" || true
fi
if [[ -f "$PNG" ]]; then
  # appimagetool expects the icon and a .DirIcon at the AppDir root, named per the desktop Icon= key.
  cp "$PNG" "$APPDIR/therionproc.png"
  cp "$PNG" "$APPDIR/.DirIcon"
else
  echo "warning: no icon converter (icotool/imagemagick) or conversion failed; using a placeholder icon" >&2
  # A 1x1 transparent PNG keeps appimagetool happy when no converter is available.
  printf '\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05\x00\x01\x0d\n-\xb4\x00\x00\x00\x00IEND\xaeB`\x82' > "$APPDIR/therionproc.png"
  cp "$APPDIR/therionproc.png" "$PNG"
  cp "$APPDIR/therionproc.png" "$APPDIR/.DirIcon"
fi

# Locate (or fetch) appimagetool.
APPIMAGETOOL_URL="${APPIMAGETOOL_URL:-https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage}"
declare -a TOOL
if [[ -n "${APPIMAGETOOL:-}" && -x "${APPIMAGETOOL:-}" ]]; then
  TOOL=("$APPIMAGETOOL" --appimage-extract-and-run)
elif command -v appimagetool >/dev/null 2>&1; then
  TOOL=(appimagetool)
else
  CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/therionproc-build"
  mkdir -p "$CACHE"
  BIN="$CACHE/appimagetool-x86_64.AppImage"
  if [[ ! -x "$BIN" ]]; then
    echo "==> Downloading appimagetool from $APPIMAGETOOL_URL"
    if command -v curl >/dev/null 2>&1; then curl -fsSL "$APPIMAGETOOL_URL" -o "$BIN"
    elif command -v wget >/dev/null 2>&1; then wget -qO "$BIN" "$APPIMAGETOOL_URL"
    else echo "error: need curl or wget to download appimagetool (or set \$APPIMAGETOOL)" >&2; exit 1; fi
    chmod +x "$BIN"
  fi
  # --appimage-extract-and-run avoids needing FUSE on the build host (e.g. CI).
  TOOL=("$BIN" --appimage-extract-and-run)
fi

mkdir -p "$OUT_DIR"
OUT="$OUT_DIR/TherionProc-${VERSION}-x86_64.AppImage"
echo "==> Building $OUT"
ARCH=x86_64 "${TOOL[@]}" "$APPDIR" "$OUT"
echo "==> Done: $OUT"
