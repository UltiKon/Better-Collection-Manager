# Better Collection Manager distribution

Updated: 2026-11-05 · Mod version: 0.1.0 · Target: .NET Framework 4.7.2

This Hearthstone mod combines collection cards by preferred quality. It also
provides split Normal/Golden tiles, ownership counters, collection filters,
categories, combos, packages, and a Mods settings menu. The experimental
hub/Darkmoon features are disabled in the current source.

## Installation

1. Prepare Hearthstone with a compatible, working MelonLoader installation.
2. Close Hearthstone completely.
3. Remove the previous mod DLL when upgrading to the renamed build.
   Copy `Mods/BetterCollectionManager.dll` from this package into your own
   Hearthstone installation's `Mods` directory. Create that directory if needed.
4. Restart Hearthstone. Settings are under **Settings → Mods**; the extended
   filter menu is available through the collection's expansion selector.

Only the mod DLL belongs in the game's `Mods` directory. Default categories and
packages are embedded in the DLL. Preferences and custom edits are stored locally
in the recipient's MelonLoader UserData directory.

This build uses the available client references. Hearthstone or MelonLoader
updates may require a new build. Successful compilation does not replace testing
in the intended game client.

For automated installation, use the separate `BetterCollectionManager-0.1.0-installer.zip`.
It contains the mod, five full Mono libraries, and setup scripts that download
MelonLoader and configure Battle.net. It requires Python 3.10+. See
`Source/Installer/README.md` for platform details and native macOS limitations.

## Contents

- `Mods/BetterCollectionManager.dll`: a fresh Release build without debug symbols.
- `Source/`: complete mod source, projects, tests, catalog data, documentation,
  and optional diagnostic tools.
- `Source/README.md`: detailed features and build instructions.
- `Source/README-LINUX.md`: building with the .NET SDK and an explicit game directory.
- `SHA256SUMS.txt`: SHA-256 checksums for every other file in the archive.

Game and loader DLLs are not included in this source distribution. Building
requires references from your own installation. The build scripts have no fixed
personal installation paths. Windows builds go to `bin/Release/`; SDK/Linux builds
go to `linux-build/`. Copy the result manually, or explicitly enable `DeployMod=true`
in the Windows project.

## Privacy preparation

This package excludes Git history, game logs, screenshots, recordings, personal
preferences, runtime caches, old builds, debug symbols, and local reference archives.
ZIP entries use relative paths and normalized timestamps and permissions. The mod
uses a neutral project name in its author field.

Optional diagnostics print details of the recipient's own installation when run.
Review any newly generated output before sharing it.

## License

The mod, installer and original project code are provided under the MIT License.
The included `LICENSE` contains the permissions, copyright notice, and warranty
and liability disclaimer. Keep it when redistributing the project.
Third-party components retain their own licenses; see
`Source/Installer/ThirdParty/NOTICES.md`.

## Build and package again

From `Source/`, using Bash and an installed .NET SDK:

```sh
./build-linux.sh
python3 Tools/package_release.py --dll linux-build/BetterCollectionManager.dll
```

The build script uses `$HOME/Games/battlenet/drive_c/Program Files (x86)/Hearthstone`
by default. For another installation, run
`GAME_DIR="/path/to/Hearthstone" ./build-linux.sh` instead.

The resulting ZIP is written under `dist/`. The packager selects source files and
checks for typical private paths and credentials. Review any new content before
distributing it.

## Package hover fix

Package hover now activates the native glow container and its nested renderers
from the first hover. It also restores private glow renderers after native state
changes. Collection hover follows the slot's mouse interaction even when the
carrier card is unowned or newly acquired. Opening a package editor first is no
longer needed for this initialization.
Automated regression checks cover cold glow hierarchies and renderer resets;
visual validation in Hearthstone remains necessary.
