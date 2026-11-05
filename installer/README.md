# Better Collection Manager setup

## Abstract

This installer sets up the **MelonLoader** version of the Hearthstone collection
mod. It does not install BepInEx. Use the complete
**`BetterCollectionManager-0.1.0-installer.zip`**, which contains `payload/`;
the setup scripts in a source checkout alone do not contain that payload.

This package contains the mod and the five reviewed Unity/Mono base libraries.
No separate Unity Editor installation or .NET SDK is required. You need
**Python 3.10 or newer**, Hearthstone, and an existing Battle.net installation.
Setup downloads MelonLoader 0.7.3 from GitHub and verifies its pinned SHA-256 checksum.

## How to install

**macOS has not been tested**, including CrossOver and the experimental native
launcher. The Mac instructions describe the available setup paths.

Extract the entire ZIP. Close Hearthstone and Battle.net completely. Under
Wine/CrossOver, also stop the prefix's Wine processes: setup edits its registry
file while Wine is stopped.

When upgrading from the previous mod name, remove the previous mod DLL from
`Mods/` before installing `BetterCollectionManager.dll`. Keep the existing
settings files; the renamed mod imports them on its first launch.

- **Windows:** run `install.cmd`. If Python is missing, install it from
  https://www.python.org/downloads/ first. If the game directory is not writable,
  open a command prompt with the required permissions.
- **Linux:** run `bash install.sh` in the extracted directory.
- **macOS:** run `install.command`, or run `bash install.sh` in Terminal.
  Python 3.10+ is required here too. If macOS restricts direct execution, use the
  Terminal command. Setup does not change Gatekeeper or code-signing settings.

Setup asks for the Hearthstone game directory: for Windows Hearthstone, this is
the folder containing `Hearthstone.exe` and `Hearthstone_Data`, including when
running through Wine/CrossOver. Select the installed game, not the extracted ZIP
or the Battle.net launcher folder. On macOS, you can also select
`Hearthstone.app`. It detects the operating system and Wine prefix where the
existing files allow an unambiguous choice. If multiple Battle.net profiles or
Hearthstone editions exist, it asks which one to configure.

To validate the configuration without downloading or changing anything:

```sh
python3 setup.py --game-dir "<Hearthstone directory>" --dry-run
```

On Windows, you can use `py -3 setup.py` instead. Replace example placeholders
with your own paths.

## Check your installation

After setup finishes, start the game through Battle.net. For native macOS, use
`Start-BCM.command` as described below. Open **Collection Manager**, then
**Settings → Mods**. The collection's **expansion selector** opens the extended
filters. In `MelonLoader/Latest.log`, look for `Better Collection Manager loading...`
and `Collection hooks ready`.

If the collection looks small, clear the search box and filters. Fresh installs
can search for new or refundable cards automatically.

| Problem | What to try |
| --- | --- |
| Python is missing or too old | Install Python 3.10 or newer, then rerun setup. |
| No Hearthstone entry in Battle.net configuration | Start Hearthstone through Battle.net once, close both, then retry. |
| More than one profile or installation is found | Select the intended installation; explicit path options are below. |
| No loader log after launching | Check the loader/bootstrap setup and launch route; native macOS needs its wrapper. |
| Setup reports a checksum mismatch | Use an unchanged official loader archive and a complete installer ZIP. |

## What setup installs

1. The appropriate official **MelonLoader 0.7.3 x64** release. Windows Hearthstone
   under Linux or CrossOver also uses the Windows loader.
2. Five base libraries in `MelonLoader/CustomCorlib-Unity6000/`.
3. The mod DLL in `Mods/BetterCollectionManager.dll`.
4. A corlib argument in the selected `Battle.net.config`, under
   `Games → <Hearthstone entry> → AdditionalLaunchArguments`. An existing corlib
   override is replaced; unrelated arguments and JSON settings are preserved.
   Setup does not invent a Games key. If no Hearthstone entry can be identified,
   launch the game through Battle.net once and retry, or supply the correct
   existing key with `--game-key`.
5. Under Wine/CrossOver, an **application-specific** override in `user.reg`:
   `Software\Wine\AppDefaults\Hearthstone.exe\DllOverrides → version=native,builtin`.
   Other overrides and applications are preserved. A conflicting
   `WINEDLLOVERRIDES` explicitly supplied by a launcher has higher priority;
   set `version=n,b` there too, or remove its conflicting value.

The game's original files are not replaced. Existing loader, mod, and configuration
files that setup replaces are backed up first. Setup does not terminate processes
or automatically launch the game.

## Native macOS: experimental

The native Mac path installs the macOS loader and creates **`Start-BCM.command`**
next to `Hearthstone.app`. This wrapper starts the inner executable with the
MelonLoader bootstrap. The app files and their signature are not modified.
The corlib argument is also recorded in Battle.net, but **the normal Battle.net
Play button does not load the native loader automatically**. Use the generated
wrapper to start the mod.

If Hearthstone requires additional launcher arguments for authentication, those
still need to be established for this native launch path. Setup does not establish
that native Mac login works. Do not copy temporary login tokens into scripts or
distribution packages.

MelonLoader 0.7.3 provides an x64 Mac release, so setup requires an x86_64 slice in
the Hearthstone executable. Apple Silicon also requires Rosetta. An arm64-only app
is rejected before changes are made. Native Hearthstone compatibility, including
the supplied Mono libraries, still needs to be tested on a Mac. CrossOver instead
uses the Windows/Wine path, its Battle.net configuration, and the Wine override.

## Explicit paths and offline installation

If the game is outside its prefix, or multiple profiles exist:

```sh
python3 setup.py --game-dir "<game directory>" \
  --wine-prefix "<Wine prefix>" \
  --battle-config "<Battle.net.config>" --game-key hs_beta
```

`hs_beta` is an example; use the key for the desired edition. Wine drive letters
are read from the selected prefix's `dosdevices`. No personal installation path
is embedded in the scripts.

For installation without a download, supply an unchanged official release archive:

```sh
python3 setup.py --game-dir "<game directory>" \
  --loader-archive "<MelonLoader release ZIP>"
```

Use `MelonLoader.x64.zip` for Windows/Wine or `MelonLoader.macOS.x64.zip` for native
macOS, both from release `v0.7.3`. Offline archives must pass the same checksum check.

## Backup and restore

Backups are created locally under
`<game directory>/BCM-Setup-Backups/<timestamp>/`. They can contain personal
Battle.net settings and absolute local paths. Do not distribute these backups.
If installation fails, setup attempts to roll back its changes immediately.
Rerunning setup skips identical files and does not duplicate its launch argument.

To restore a backup, close the game, launcher, and Wine, then run:

```sh
python3 setup.py --restore "<full backup directory>"
```

If a target file has changed since installation, for example through Battle.net
or an update, automatic restore refuses to overwrite it. Restore the relevant
original values manually from the backup instead.

## License

The mod and installer are provided under the MIT License. The included `LICENSE`
contains the permissions, copyright notice, and warranty and liability disclaimer.
Keep it when redistributing this package. The bundled Mono libraries and downloaded
MelonLoader retain their own licenses; see `ThirdParty/NOTICES.md` and the supplied
third-party license notices.

## Validation and sources

Automated checks cover configuration merging, path mapping, architecture selection,
installation, and rollback. The actual Windows and Mac loader archives are verified
against their published digests. No running Hearthstone client was used here to
test Windows, Wine/CrossOver, or macOS installation.

- MelonLoader 0.7.3: https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3
- Corlib override: https://github.com/LavaGang/MelonLoader/blob/v0.7.3/README.md#launch-options
- Native Mac launch path: https://github.com/LavaGang/MelonLoader/blob/v0.7.3/MelonLoader.Bootstrap/OSXEntry/melonloader-launch.sh
- Battle.net JSON in Lutris: https://lutris.net/games/install/37360/view
- Wine override precedence: https://github.com/wine-mirror/wine/blob/master/dlls/ntdll/unix/loadorder.c
