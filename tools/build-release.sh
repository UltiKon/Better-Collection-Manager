#!/usr/bin/env bash
# Build and verify both loader variants before packaging either one.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
export GAME_DIR="${GAME_DIR:-$HOME/Games/battlenet/drive_c/Program Files (x86)/Hearthstone}"

bash tools/build-bepinex.sh
bash tools/build-linux.sh

dotnet run --project tests/BepInEx/BepInEx.csproj -c Release \
  -p:BepInExCoreDir="${BEPINEX_CORE_DIR:-$GAME_DIR/BepInEx/core}" -- \
  artifacts/bepinex/BetterCollectionManager.dll \
  artifacts/melonloader/BetterCollectionManager.dll

python3 tools/package_release.py --loader bepinex --dll artifacts/bepinex/BetterCollectionManager.dll
python3 tools/package_release.py --loader melonloader --dll artifacts/melonloader/BetterCollectionManager.dll
