#!/usr/bin/env bash
# Builds the mod and prepares the files of a GitHub release in mod/dist/release-assets/:
#   AdofaiOnlineMod.dll, Install-ADOFAI-Online.bat/.ps1, SHA256SUMS.txt and AdofaiOnline-<version>.zip
# The installer downloads AdofaiOnlineMod.dll and SHA256SUMS.txt from ".../releases/latest/download/", so upload
# these files individually (not only the zip).
#
# Usage:  bash mod/make-release.sh
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/dist/release-assets"

bash "$HERE/build.sh"
VERSION="$(grep -oE 'Version = "[0-9]+\.[0-9]+\.[0-9]+"' "$HERE/src/Plugin.cs" | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')"

rm -rf "$OUT"
mkdir -p "$OUT/zip"
cp "$HERE/dist/AdofaiOnlineMod.dll" "$HERE/installer/Install-ADOFAI-Online.bat" "$HERE/installer/Install-ADOFAI-Online.ps1" "$OUT/"
cp "$HERE/dist/AdofaiOnlineMod.dll" "$HERE/installer/Install-ADOFAI-Online.bat" "$HERE/installer/Install-ADOFAI-Online.ps1" "$HERE/README.md" "$HERE/../LICENSE" "$OUT/zip/"
(cd "$OUT/zip" && powershell -NoProfile -Command "Compress-Archive -Path * -DestinationPath ../AdofaiOnline-$VERSION.zip -Force")
rm -rf "$OUT/zip"

# checksums (the installer verifies the DLL against this file)
(cd "$OUT" && sha256sum AdofaiOnlineMod.dll Install-ADOFAI-Online.bat Install-ADOFAI-Online.ps1 "AdofaiOnline-$VERSION.zip" > SHA256SUMS.txt)
echo "OK: version $VERSION"
ls -l "$OUT"
