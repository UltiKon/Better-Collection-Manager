#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
GAME_DIR="${GAME_DIR:-$HOME/Games/battlenet/drive_c/Program Files (x86)/Hearthstone}"
export GameInstallRoot="$GAME_DIR"
export BepInExCoreDir="${BEPINEX_CORE_DIR:-$GAME_DIR/BepInEx/core}"
if [[ ! -f "$GAME_DIR/Hearthstone_Data/Managed/Assembly-CSharp.dll" ]]; then
    echo "ERROR: Missing Hearthstone Managed assemblies under $GAME_DIR" >&2
    exit 1
fi
if [[ ! -f "$BepInExCoreDir/BepInEx.dll" || ! -f "$BepInExCoreDir/0Harmony.dll" ]]; then
    echo "ERROR: BepInEx 5 Unity Mono core DLLs missing under $BepInExCoreDir" >&2
    echo 'Set BEPINEX_CORE_DIR to the directory containing BepInEx.dll and 0Harmony.dll.' >&2
    exit 1
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo 'ERROR: .NET SDK missing. Install a dotnet-sdk package, then rerun.' >&2
    exit 1
fi
dotnet build src/BetterCollectionManager/BetterCollectionManager.BepInEx.csproj -c Release
printf '\nBuilt DLL (not installed): %s\n' "$(pwd)/artifacts/bepinex/BetterCollectionManager.dll"
printf 'Copy to: %s\n' "$GAME_DIR/BepInEx/plugins/BetterCollectionManager/"
