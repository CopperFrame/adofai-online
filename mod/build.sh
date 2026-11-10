#!/usr/bin/env bash
# Builds AdofaiOnlineMod.dll against your installed copy of the game (nothing from the game is redistributed).
#
# Requirements:
#   - A Dance of Fire and Ice (Steam) with BepInEx 5 (x64, Mono) installed in the game folder
#   - .NET SDK 8 or newer (for the C# compiler)
#   - Git Bash / WSL-style shell on Windows (uses cygpath)
#
# Usage:
#   bash build.sh
# Override the defaults with environment variables if needed:
#   ADOFAI_GAME="C:/Program Files (x86)/Steam/steamapps/common/A Dance of Fire and Ice"
#   ROSLYN_CSC="C:/path/to/csc.dll"        (defaults to the csc.dll shipped with the .NET SDK)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="${ADOFAI_GAME:-C:/Program Files (x86)/Steam/steamapps/common/A Dance of Fire and Ice}"
MANAGED="$GAME/A Dance of Fire and Ice_Data/Managed"
OUT="$HERE/dist"
mkdir -p "$OUT"

if [ -z "${ROSLYN_CSC:-}" ]; then
  SDK_DIR="$(dotnet --list-sdks | tail -1 | sed -E 's/^[^[]*\[(.*)\]$/\1/')"
  SDK_VER="$(dotnet --list-sdks | tail -1 | awk '{print $1}')"
  ROSLYN_CSC="$SDK_DIR/$SDK_VER/Roslyn/bincore/csc.dll"
fi

# references: the game's own assemblies (mscorlib, netstandard, UnityEngine...), BepInEx and Harmony
RSP="$OUT/refs.rsp"
{
  for f in "$(cygpath -u "$MANAGED")"/*.dll; do
    b=$(basename "$f")
    case "$b" in
      I18N*|Interop*|Mono.Security.dll) ;;
      *) echo "\"-r:$MANAGED/$b\"" ;;
    esac
  done
  echo "\"-r:$GAME/BepInEx/core/0Harmony.dll\""
  echo "\"-r:$GAME/BepInEx/core/BepInEx.dll\""
} > "$RSP"

dotnet "$ROSLYN_CSC" -nologo -target:library -out:"$OUT/AdofaiOnlineMod.dll" \
  -langversion:latest -define:ADOFAI_MOD -nowarn:0618,0649,0169,0414,0219 @"$RSP" \
  "$HERE/src/Plugin.cs" "$HERE/src/ModShims.cs" "$HERE/src/GlobalUsings.cs" \
  "$HERE/../shared/"*.cs

echo "OK: $OUT/AdofaiOnlineMod.dll"
