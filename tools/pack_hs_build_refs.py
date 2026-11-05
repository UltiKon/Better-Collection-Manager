#!/usr/bin/env python3
"""Package installed client DLLs for a build; leaves the game installation unchanged."""
import argparse
from pathlib import Path
import zipfile


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=repo / "hearthstone-build-refs.zip")
    args = parser.parse_args()
    managed = args.game_dir / "Hearthstone_Data/Managed"
    dlls = sorted(managed.glob("*.dll"))
    if not (managed / "Assembly-CSharp.dll").is_file():
        parser.error(f"Client assembly not found: {managed / 'Assembly-CSharp.dll'}")
    loader = next((args.game_dir / "MelonLoader" / name for name in ("net35", "", "net6")
                   if all((args.game_dir / "MelonLoader" / name / dll).is_file()
                          for dll in ("MelonLoader.dll", "0Harmony.dll"))), None)
    if loader is None:
        parser.error("MelonLoader.dll and 0Harmony.dll were not found; use --game-dir for your installation")
    files = [(path, f"Hearthstone_Data/Managed/{path.name}") for path in dlls]
    files += [(loader / name, f"MelonLoader/net35/{name}") for name in ("MelonLoader.dll", "0Harmony.dll")]
    with zipfile.ZipFile(args.output, "w", zipfile.ZIP_DEFLATED) as archive:
        for path, relative in files:
            archive.write(path, relative)
    print(f"Packaged {len(files)} DLLs: {args.output}")


if __name__ == "__main__":
    main()
