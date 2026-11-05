#!/usr/bin/env bash
# Read-only installation checks. This does not launch Wine or alter game files.
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -z "${GAME_DIR:-}" ]]; then
    echo 'Set GAME_DIR to your Hearthstone installation before running this script.' >&2
    exit 1
fi
game_dir="$GAME_DIR"
printf 'Game directory: %s\n\n' "$game_dir"
if [[ ! -d "$game_dir" ]]; then
    printf 'Directory missing. Usage: GAME_DIR="/path/to/Hearthstone" bash %q\n' "$0"
    exit 1
fi

check_file() {
    if [[ -s "$game_dir/$1" ]]; then
        printf 'OK      %s (%s Bytes)\n' "$1" "$(stat -c '%s' -- "$game_dir/$1")"
    elif [[ -f "$game_dir/$1" ]]; then
        printf 'EMPTY   %s\n' "$1"
    else
        printf 'MISSING %s\n' "$1"
    fi
}

printf 'Game and loader:\n'
for relative in Hearthstone.exe \
    MelonLoader/net35/MelonLoader.dll MelonLoader/net35/0Harmony.dll \
    MelonLoader/Dependencies/SupportModules/Mono.dll \
    MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll \
    Hearthstone_Data/Managed/Assembly-CSharp.dll Hearthstone_Data/Managed/UnityEngine.CoreModule.dll \
    Hearthstone_Data/Managed/mscorlib.dll; do
    check_file "$relative"
done

printf '\nChecksum comparison with the official Windows x64 MelonLoader 0.7.3 release:\n'
printf 'Differences are expected if you intentionally modified the loader.\n'
(
    cd -- "$game_dir"
    sha256sum --check <<'CHECKSUMS' || true
0ce7a4e530c7f83f172a2c44aed45eedb3bdcf06b83760f143a0fba6885fa929  version.dll
7c0e8c59a4ec02cbcaec91d0393e7caf9886f21d00ad35984b7013bca695225b  MelonLoader/net35/MelonLoader.dll
9710c782b13742fa23b9fc2c3608263ba6f1d08ef1342898fc26a8f539d3f247  MelonLoader/net35/0Harmony.dll
CHECKSUMS
)
printf '\nLoader entry point (version.dll or winhttp.dll, depending on setup):\n'
check_file version.dll
check_file winhttp.dll
printf '\nSeparate Dobby files (version-dependent; may be embedded in the bootstrap):\n'
for relative in dobby.dll MelonLoader/Dependencies/dobby.dll; do
    if [[ -f "$game_dir/$relative" ]]; then
        check_file "$relative"
    fi
done

printf '\nOptional Unity 6000 corlib:\n'
corlib_dir="$game_dir/MelonLoader/CustomCorlib-Unity6000"
if [[ -d "$corlib_dir" ]]; then
    find "$corlib_dir" -maxdepth 1 -type f -iname '*.dll' -printf '%f (%s Bytes)\n' | sort
    check_file MelonLoader/CustomCorlib-Unity6000/mscorlib.dll
else
    printf 'MISSING MelonLoader/CustomCorlib-Unity6000\n'
fi

printf '\nInstalled Mods and UserLibs (name, SHA256):\n'
for directory in Mods UserLibs; do
    if [[ -d "$game_dir/$directory" ]]; then
        while IFS= read -r -d '' dll; do
            sha256sum -- "$dll"
        done < <(find "$game_dir/$directory" -maxdepth 1 -type f -iname '*.dll' -print0)
    fi
done
if [[ -f "$repo_dir/artifacts/melonloader/BetterCollectionManager.dll" ]]; then
    printf '\nCurrent repository build (for hash comparison):\n'
    sha256sum -- "$repo_dir/artifacts/melonloader/BetterCollectionManager.dll"
fi

printf '\nLatest MelonLoader logs (time, bytes, path):\n'
if [[ -d "$game_dir/MelonLoader" ]]; then
    find "$game_dir/MelonLoader" -maxdepth 2 -type f -iname '*.log' \
        -printf '%TY-%Tm-%Td %TH:%TM:%TS %s %p\n' | sort -r | head -n 8 || true
fi

if command -v python3 >/dev/null 2>&1 && [[ -f "$repo_dir/hearthstone-build-refs.zip" ]]; then
    printf '\nComparison with the reference archive in the repository:\n'
    python3 - "$game_dir" "$repo_dir/hearthstone-build-refs.zip" <<'PY'
import hashlib
from pathlib import Path
import sys
import zipfile

game = Path(sys.argv[1])
with zipfile.ZipFile(sys.argv[2]) as archive:
    for name in ("Hearthstone_Data/Managed/Assembly-CSharp.dll",
                 "Hearthstone_Data/Managed/UnityEngine.CoreModule.dll",
                 "Hearthstone_Data/Managed/mscorlib.dll"):
        current = game / name
        if not current.is_file() or name not in archive.namelist():
            continue
        same = hashlib.sha256(current.read_bytes()).digest() == hashlib.sha256(archive.read(name)).digest()
        print(("SAME    " if same else "CHANGED ") + name)
print("CHANGED means a different file revision, not necessarily a damaged file.")
PY
fi
