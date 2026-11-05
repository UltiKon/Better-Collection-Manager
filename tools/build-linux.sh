#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
GAME_DIR="${GAME_DIR:-$HOME/Games/battlenet/drive_c/Program Files (x86)/Hearthstone}"
MANAGED="$GAME_DIR/Hearthstone_Data/Managed"
ML_PATH=""
for candidate in "$GAME_DIR/MelonLoader/net35" "$GAME_DIR/MelonLoader" "$GAME_DIR/MelonLoader/net6"; do
    if [[ -f "$candidate/MelonLoader.dll" && -f "$candidate/0Harmony.dll" ]]; then
        ML_PATH="$candidate"
        break
    fi
done
if [[ -z "$ML_PATH" ]]; then
    echo "ERROR: Could not locate both MelonLoader.dll and 0Harmony.dll under $GAME_DIR/MelonLoader" >&2
    echo 'Search: find "$GAME_DIR/MelonLoader" -iname "MelonLoader.dll" -o -iname "0Harmony.dll"' >&2
    exit 1
fi
if [[ ! -d "$MANAGED" ]]; then
    echo "ERROR: Missing Hearthstone Managed directory: $MANAGED" >&2
    exit 1
fi
for ref in Assembly-CSharp Blizzard.T5.Game Blizzard.T5.AssetManager Blizzard.T5.AssetLoader Blizzard.T5.ServiceLocator Blizzard.T5.Core Blizzard.T5.UberText.Runtime blizzard.bgsclient UnityEngine UnityEngine.UI UnityEngine.CoreModule UnityEngine.IMGUIModule UnityEngine.AnimationModule UnityEngine.ImageConversionModule UnityEngine.TextRenderingModule UnityEngine.PhysicsModule UnityEngine.InputLegacyModule; do
    if [[ ! -f "$MANAGED/$ref.dll" ]]; then
        echo "ERROR: Missing game DLL: $MANAGED/$ref.dll" >&2
        exit 1
    fi
done
if ! command -v dotnet >/dev/null 2>&1; then
    echo 'ERROR: .NET SDK missing. Install a dotnet-sdk package, then rerun.' >&2
    exit 1
fi
printf 'Game:   %s\nMono:   %s\nSDK:    %s\n' "$GAME_DIR" "$ML_PATH" "$(dotnet --version)"
export GameInstallRoot="$GAME_DIR"
export MelonDllDir="$ML_PATH"
dotnet build src/BetterCollectionManager/BetterCollectionManager.Linux.csproj -c Release
printf '\nBuilt DLL (not installed): %s\n' "$(pwd)/artifacts/melonloader/BetterCollectionManager.dll"
echo "You can copy it to: $GAME_DIR/Mods/ after MelonLoader's own initialization succeeds."
