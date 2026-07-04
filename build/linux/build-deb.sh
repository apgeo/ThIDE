#!/usr/bin/env bash
#
# Build a Debian package (.deb) from a self-contained linux-x64 publish of TherionProc.
#
# The .deb is the Linux counterpart to the Windows setup.exe: it installs the app under
# /opt/therionproc, drops a launcher on PATH, adds an application-menu entry with an icon, and
# registers the Therion file types (mirroring TherionProc.Services.FileAssociationCatalog, kept in
# sync by InstallerAssociationConsistencyTests). Install with `apt install ./file.deb`; remove with
# `apt remove therionproc`.
#
# Usage:
#   build/linux/build-deb.sh <publish-dir> <version> [out-dir]
#
# Example:
#   dotnet publish TherionProc/TherionProc.csproj -m:1 -c Release -r linux-x64 \
#       --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
#   build/linux/build-deb.sh publish/linux-x64 0.3.0
#
# Requires: dpkg-deb (preinstalled on Debian/Ubuntu). For the menu icon, one of icotool (icoutils)
# or ImageMagick (magick/convert) is used if present; otherwise the entry falls back to a generic icon.

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

# Resolve the repo root from this script's location so relative asset paths work from anywhere.
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

PKG="$(mktemp -d)"
trap 'rm -rf "$PKG"' EXIT

APPDIR="$PKG/opt/therionproc"
BINDIR="$PKG/usr/bin"
DESKTOPDIR="$PKG/usr/share/applications"
ICONDIR="$PKG/usr/share/icons/hicolor/256x256/apps"
MIMEDIR="$PKG/usr/share/mime/packages"
DOCDIR="$PKG/usr/share/doc/therionproc"
mkdir -p "$APPDIR" "$BINDIR" "$DESKTOPDIR" "$ICONDIR" "$MIMEDIR" "$DOCDIR" "$PKG/DEBIAN"

echo "==> Staging published files -> /opt/therionproc"
cp -a "$PUBLISH_DIR/." "$APPDIR/"
chmod 0755 "$APPDIR/TherionProc"

# Launcher on PATH.
cat > "$BINDIR/therionproc" <<'EOF'
#!/bin/sh
exec /opt/therionproc/TherionProc "$@"
EOF
chmod 0755 "$BINDIR/therionproc"

# Build the MimeType list + shared-mime-info package.
MIME_LIST=""
{
  echo '<?xml version="1.0" encoding="UTF-8"?>'
  echo '<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">'
  for pair in "${EXTS[@]}"; do
    ext="${pair%%|*}"; desc="${pair#*|}"
    mime="application/x-therion-$ext"
    MIME_LIST="${MIME_LIST}${mime};"
    echo "  <mime-type type=\"$mime\">"
    echo "    <comment>$desc</comment>"
    echo "    <glob pattern=\"*.$ext\"/>"
    echo "  </mime-type>"
  done
  echo '</mime-info>'
} > "$MIMEDIR/therionproc.xml"

# Desktop entry (mirrors the app's runtime one; Exec/Icon resolve via PATH + icon theme).
cat > "$DESKTOPDIR/therionproc.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=TherionProc
Comment=Therion survey editor
Exec=therionproc %F
TryExec=therionproc
Icon=therionproc
Terminal=false
Categories=Science;Education;Utility;
MimeType=$MIME_LIST
EOF

# Menu icon: extract the largest frame from the .ico if a converter is available.
PNG="$ICONDIR/therionproc.png"
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
if [[ ! -f "$PNG" ]]; then
  echo "warning: no icon converter (icotool/imagemagick) or conversion failed; menu entry uses a generic icon" >&2
  rmdir -p "$ICONDIR" 2>/dev/null || true
fi

# Ship the licence with the package.
cp "$REPO_ROOT/LICENSE" "$DOCDIR/copyright"

# Debian control metadata. Installed-Size is in KiB.
INSTALLED_KB="$(du -ks "$PKG" | cut -f1)"
cat > "$PKG/DEBIAN/control" <<EOF
Package: therionproc
Version: $VERSION
Section: science
Priority: optional
Architecture: amd64
Maintainer: TherionProc <noreply@example.com>
Installed-Size: $INSTALLED_KB
Depends: libc6, libgcc-s1, libstdc++6, zlib1g
Recommends: libx11-6, libice6, libsm6, libfontconfig1, libgl1, libwebkit2gtk-4.1-0
Homepage: https://github.com/apgeo/TherionProc
Description: Therion survey editor and processor
 TherionProc is a cross-platform editor for Therion cave-survey source: syntax
 intelligence, live preview, 2D/3D map rendering, data analytics and more.
 This package bundles a self-contained .NET runtime, so no system .NET is required.
EOF

# Refresh the freedesktop caches after install/removal.
cat > "$PKG/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q /usr/share/applications || true; fi
if command -v update-mime-database    >/dev/null 2>&1; then update-mime-database /usr/share/mime || true; fi
if command -v gtk-update-icon-cache   >/dev/null 2>&1; then gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true; fi
exit 0
EOF
cat > "$PKG/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q /usr/share/applications || true; fi
if command -v update-mime-database    >/dev/null 2>&1; then update-mime-database /usr/share/mime || true; fi
if command -v gtk-update-icon-cache   >/dev/null 2>&1; then gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true; fi
exit 0
EOF
chmod 0755 "$PKG/DEBIAN/postinst" "$PKG/DEBIAN/postrm"

mkdir -p "$OUT_DIR"
DEB="$OUT_DIR/therionproc_${VERSION}_amd64.deb"
echo "==> Building $DEB"
dpkg-deb --build --root-owner-group "$PKG" "$DEB"
echo "==> Done: $DEB"
