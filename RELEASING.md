# Preparing a release

Run these commands from the source repository root (the directory containing the
`.csproj` files). In an extracted release ZIP, that is `Source/`.

## Version and identity

`ModMetadata.cs` defines the plugin name, GUID, version and repository URL. Both
loader attributes and assembly versions use those constants. Archive filenames
read the version from the same file.

For Firestone integration, keep these values aligned:

| Field | Value for this release |
| --- | --- |
| DLL basename / BepInEx GUID / General.Guid | `BetterCollectionManager` |
| BepInEx config filename | `BetterCollectionManager.cfg` |
| Plugin version / General.Version / GitHub release tag | `0.1.0` |
| General.DownloadLink | `https://github.com/UltiKon/Better-Collection-Manager` |

Use the exact tag `0.1.0` for this version; the metadata requirement uses the tag
string. When changing the version, update the CFG snapshot, changelog, release
examples and assembly-verification expectations too. The plugin refreshes its
General metadata on startup so an older saved version does not survive an upgrade.

## Validate the source

Use a .NET 8 SDK and Python 3.10 or newer:

```sh
python3 Tools/check_docs.py
python3 -m unittest discover -s Tests/Release -v
python3 -m unittest discover -s Tests/Installer -v
dotnet run --project Tests/Defaults/Defaults.csproj -c Release
dotnet run --project Tests/MixedPremium/MixedPremium.csproj -c Release
dotnet run --project Tests/KeywordFilters/KeywordFilters.csproj -c Release
dotnet run --project Tests/HubMenu/HubMenu.csproj -c Release
dotnet run --project Tests/CollapseMode/CollapseMode.csproj -c Release
dotnet run --project Tests/Pagination/Pagination.csproj -c Release
```

The GitHub Actions workflow runs these checks on clean checkouts and verifies
CFG persistence and Firestone metadata against a checksum-pinned BepInEx core. Installer
transaction tests create their own payload fixtures. Tests that need the official
Windows or macOS loader archive are skipped when it is absent; skipping them does
not verify a real installer bundle.

## Build and package BepInEx

With BepInEx 5 core DLLs in the game's `BepInEx/core/`, use Bash:

```sh
GAME_DIR="/path/to/Hearthstone" bash build-bepinex.sh
python3 Tools/package_release.py --loader bepinex --dll bepinex-build/BetterCollectionManager.dll
```

For a separate core directory, add
`BEPINEX_CORE_DIR="/path/to/BepInEx/core"` before `bash` in the build command.

On Windows, use these PowerShell commands, replacing the paths:

```powershell
dotnet build BetterCollectionManager.BepInEx.csproj -c Release -p:GameInstallRoot="<Hearthstone directory>" -p:BepInExCoreDir="<BepInEx core directory>"
py -3 Tools/package_release.py --loader bepinex --dll bepinex-build/BetterCollectionManager.dll
```

Output: `dist/BetterCollectionManager-0.1.0-bepinex.zip`.

## Build and package MelonLoader

```sh
GAME_DIR="/path/to/Hearthstone" bash build-linux.sh
python3 Tools/package_release.py --dll linux-build/BetterCollectionManager.dll
```

Output: `dist/BetterCollectionManager-0.1.0-shareable.zip`.
See the [Linux build guide](README-LINUX.md) for loader references and the
[technical reference](TECHNICAL-REFERENCE.md#build) for Windows MSBuild.

After building both variants, run the [BepInEx backend and assembly checks](README-BEPINEX.md#verification).
Game API checks require the installed client's assemblies; see
[Native API checks](Tests/NativeApi/README.md).

## Optional automated installer bundle

The installer is a separate **MelonLoader-only** artifact. It requires the five
reviewed full Mono libraries in `CustomCorlib-Unity6000/` and a fresh DLL in
`linux-build/`. These are intentionally absent from Git.

`Installer/payload.json` pins their hashes. After rebuilding, review the DLL and
update its hash without changing the reviewed third-party entries:

```sh
python3 - <<'PY'
import hashlib
import json
from pathlib import Path
manifest = Path('Installer/payload.json')
data = json.loads(manifest.read_text())
data['files']['payload/Mods/BetterCollectionManager.dll'] = hashlib.sha256(
    Path('linux-build/BetterCollectionManager.dll').read_bytes()).hexdigest()
manifest.write_text(json.dumps(data, indent=2) + '\n')
PY
python3 Tools/package_installer.py
```

Output: `dist/BetterCollectionManager-0.1.0-installer.zip`.
Only publish it after validating the actual bundled payload and setup on the
intended platforms. A manifest update is not evidence of runtime compatibility.

## Check before publishing

1. Unpack each ZIP into an empty directory. Follow its root README as a new user.
2. Verify `SHA256SUMS.txt` from that directory, for example with
   `sha256sum --check SHA256SUMS.txt` on Linux.
3. Check that the DLL matches the loader, both catalogs are embedded, and source
   documentation links work from the ZIP's root README.
4. Test loading, collection browsing, split tiles, filtering, deck editing,
   saving across a restart and a clean shutdown in the actual game.
5. Check Firestone's mod list and the generated `General` metadata. Confirm the
   lowercase CFG migrates without overriding an existing current CFG.
6. Include the compatibility limits and test results in the release notes.

Packaging excludes build caches, Git history, game/loader references, personal
preferences and logs. It creates normalized ZIP entries and regenerates checksums.
Do not reuse a prebuilt DLL after changing plugin code. Building and packaging
are local operations; publishing a GitHub release is a separate step.
