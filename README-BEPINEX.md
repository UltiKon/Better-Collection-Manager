# Better Collection Manager

## Abstract

Better Collection Manager makes Hearthstone's collection easier to browse. It
groups card qualities, offers split Normal/Golden cards and adds filters,
categories, combos and packages. Choose your preferred quality in
**Settings → Mods**; each quality keeps its own ownership count and deck rules.

This is the **BepInEx 5 Unity Mono** version, mod **0.1.0**. It was compiled against
BepInEx **5.4.23.6** and the supplied Hearthstone references, but has **not yet been
tested in a running Hearthstone client**. BepInEx 6 and IL2CPP are not supported
by this build. Hub/Darkmoon experiments are disabled.

## How to install

You need Hearthstone, a working **BepInEx 5** loader and this mod's **BepInEx ZIP**.
A loader is the tool that starts mods with the game. The ZIP contains the mod,
not the loader. **You do not need Python, a .NET SDK or the source code to install
this ready-made DLL.**

### 1. Find your Hearthstone folder

For Windows Hearthstone, use the folder containing **`Hearthstone.exe`** and
**`Hearthstone_Data`**, including when running it through Wine or CrossOver.
This is the **game folder** used in the paths below. It is not your Downloads
folder or the Battle.net launcher folder.

### 2. Set up BepInEx first

1. Close Hearthstone completely.
2. Install BepInEx 5 for the game's Mono runtime and platform using the
   [official installation guide](https://docs.bepinex.dev/articles/user_guide/installation/index.html).
   The build reference is the [5.4.23.6 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6).
   Windows Hearthstone through Wine/CrossOver needs the **Windows x64** loader.
   Follow the [Wine/Proton loader guide](https://docs.bepinex.dev/articles/advanced/proton_wine.html)
   for that environment.
3. Start the game once, then close it. Check that `BepInEx/LogOutput.log` was
   created inside the game folder. This confirms that the loader started.

If you currently use MelonLoader, back up its configuration and disable its
bootstrap and launch arguments before switching. Run **one loader per launch**.
The separate automated mod installer installs MelonLoader; it does not set up
BepInEx.

### 3. Copy the mod DLL

1. Extract the entire mod ZIP to a temporary folder.
2. Inside the extracted ZIP, find
   `BepInEx/plugins/BetterCollectionManager/BetterCollectionManager.dll`.
3. Copy it into the **game folder** at the same relative location. Create the
   `plugins/BetterCollectionManager/` folders if they do not exist.
4. Remove any older copy of this mod from your plugin/mod folders, including a
   copy using the previous mod name. Keep your settings files.

Your installed files should look like this for Windows Hearthstone:

```text
Hearthstone/                         ← your installed game, not the extracted ZIP
├── Hearthstone.exe
├── Hearthstone_Data/
└── BepInEx/
    ├── core/                        ← installed by BepInEx
    └── plugins/
        └── BetterCollectionManager/
            └── BetterCollectionManager.dll
```

The DLLs from `Mods/`, `linux-build/` and the MelonLoader installer are different
builds despite having the same filename. Use the DLL from the BepInEx ZIP.
Default categories and packages are already embedded in it.

### 4. Check that it works

1. Start Hearthstone and open **Collection Manager**.
2. Open **Settings → Mods**. Use the collection's **expansion selector** for the
   extended filters.
3. In `BepInEx/LogOutput.log`, look for `Better Collection Manager loading...`
   followed by `Collection hooks ready`.
4. Change a setting and restart the game once to check that it is saved.

Fresh installations prefer **Diamond → Signature → Golden → Normal** and use
split Normal/Golden tiles. A search for new or refundable cards may appear when
you open the collection. Clear the search and filters to see your full collection.
Read the [user guide](USER-GUIDE.md) for the controls and display options.

## Troubleshooting

| What you see | What to check |
| --- | --- |
| No `BepInEx/LogOutput.log` | The loader has not started. Recheck its platform, installation and launch setup before changing the mod. |
| The log exists but never mentions the mod | Check the DLL location above, the BepInEx build and any load errors in the log. |
| The mod loads but there is no **Mods** button | Open Collection Manager first, then Settings. Look for `Collection hooks ready` or an initialization error. |
| Missing cards | Clear the search box and filters; check the selected format and expansion. |
| Errors after an update | Keep one matching mod DLL. A changed game or loader may require a new build. |

For a problem report, include mod, loader and game versions, operating system,
steps to reproduce and the relevant log error. Review logs for personal paths
before sharing them.

## Update or remove the mod

Close the game before replacing the DLL. Keep the configuration files described
below; saved preferences take precedence over defaults. Hot reload is unsupported.
To disable or remove this mod, move its DLL out of `BepInEx/plugins/`. You can keep
its settings for a later reinstall.

For other loader packages, see the [documentation overview](README.md).
The remaining sections cover building and migrating settings.

## Build

Install a .NET SDK and supply the game's `Hearthstone_Data/Managed` DLLs and the
official BepInEx 5 core DLLs. From this source directory:

```sh
GAME_DIR="/path/to/Hearthstone" ./build-bepinex.sh
```

For a direct build on Windows, macOS or Linux:

```sh
dotnet build BetterCollectionManager.BepInEx.csproj -c Release \
  -p:GameInstallRoot="/path/to/Hearthstone" \
  -p:BepInExCoreDir="/path/to/BepInEx/core"
```

In PowerShell, put the command on one line or use PowerShell's continuation
syntax. `BepInExCoreDir` defaults to the game's `BepInEx/core`; the shell script
also accepts `BEPINEX_CORE_DIR`. No MelonLoader installation is required.
The output is `bepinex-build/BetterCollectionManager.dll`. The build does not
copy files into the game. Categories and packages are embedded in the DLL.

## Settings and migration

The plugin advertises its name, GUID, version, repository URL and description in
the CFG's **General** section so Firestone can identify it in its mod list.
The plugin GUID and CFG filename now match the DLL: **BetterCollectionManager**.
The repository's release tag must match the version string exactly: **0.1.0**.

On the first launch with this filename, an existing lowercase
`bettercollectionmanager.cfg` is imported if `BetterCollectionManager.cfg` does
not exist. The lowercase file takes precedence over settings from the previous
mod name. Existing current settings always win, and the original files remain.

The project, DLL and plugin now use the Better Collection Manager name.
On the first launch after the rename, the plugin imports the previous standalone
CFG and JSON settings directory when the new files do not exist. Existing new
settings take precedence; the previous files are retained. MelonLoader also
imports the renamed preference sections, keeping a `.before-bcm.bak` backup of
its shared CFG. Remove the previous mod DLL before installing the renamed one.


The existing in-game settings menu writes to
`BepInEx/config/BetterCollectionManager.cfg`. Collection preference sections and
entry names match the MelonLoader preferences; **General** contains the additional
Firestone metadata. BepInEx saves are batched by the shared mod logic;
editing the CFG while the game is closed takes effect on the next launch.
JSON backups are written to `BepInEx/config/BetterCollectionManager/`.
Experimental Darkmoon artwork uses the BepInEx cache directory.

To migrate MelonLoader settings before the first BepInEx launch, copy
`premium_order.json` and `quality_settings.json` from
`Mods/BetterCollectionManager/` into
`BepInEx/config/BetterCollectionManager/`. JSON backups are imported only
when the BepInEx CFG does not exist; subsequent launches use the CFG. To import
again, close the game and back up and move both the current CFG and any retained
lowercase CFG out of the config directory first, so migration does not restore
the older CFG instead. User catalog data
remains embedded or managed through the existing import/export menus.

## Verification

After a successful loader start, check collection opening, preferred quality
and split tiles, filters, deck editing, settings persistence after a restart,
and closing the game. Confirm that hooks initialize once after the splash (or
before the first native collection page when that fallback is needed).

The shared regression suites can be run independently of a game installation:

```sh
dotnet run --project Tests/MixedPremium/MixedPremium.csproj -c Release
dotnet run --project Tests/HubMenu/HubMenu.csproj -c Release
dotnet run --project Tests/Defaults/Defaults.csproj -c Release
```

After building both variants, verify real BepInEx CFG persistence and loader
metadata with the BepInEx core DLLs:

```sh
dotnet run --project Tests/BepInEx/BepInEx.csproj -c Release \
  -p:BepInExCoreDir="/path/to/BepInEx/core" -- \
  bepinex-build/BetterCollectionManager.dll linux-build/BetterCollectionManager.dll
```

To package the BepInEx DLL and source:

```sh
python3 Tools/package_release.py --loader bepinex --dll bepinex-build/BetterCollectionManager.dll
```

To check only real CFG persistence and Firestone metadata before building the
game-dependent DLLs:

```sh
dotnet run --project Tests/BepInEx/BepInEx.csproj -c Release \
  -p:BepInExCoreDir="/path/to/BepInEx/core" -- --config-only
```

Hub experiments remain disabled in both loader variants.
