# BepInEx version

Better Collection Manager also builds as a **BepInEx 5 Unity Mono** plugin.
The MelonLoader and BepInEx entries use the same collection logic, resources,
startup delay, patches and update handlers. The BepInEx build has no MelonLoader
dependency. It targets .NET Framework 4.7.2.

The port was compiled against the official
[BepInEx 5.4.23.6 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6)
and the supplied Hearthstone reference assemblies. It has not yet been tested in
a running Hearthstone client. BepInEx 6 and IL2CPP are not targets of this build.

## Installation

1. Close Hearthstone. Set up BepInEx 5 for the game's Mono runtime and platform
   using the [official instructions](https://docs.bepinex.dev/articles/user_guide/installation/index.html).
   For Windows Hearthstone running through Wine/CrossOver, use the Windows x64
   loader. The loader must initialize successfully before testing this plugin.
2. Use one mod loader per launch. Disable the existing MelonLoader bootstrap and
   remove its launch arguments when switching to BepInEx; keep a backup of that
   configuration. Do not load both versions of this mod together.
3. Remove the previous mod DLL from the loader's plugin/mod folder when upgrading
   to the renamed build, so only one copy runs. Copy the **BepInEx build** of `BetterCollectionManager.dll` into
   `BepInEx/plugins/BetterCollectionManager/`. The separate BepInEx ZIP already
   contains this folder layout. The DLL from `linux-build/`, `Mods/`, or the
   MelonLoader installer ZIP is a different build.
4. Launch the game, open Collection Manager and check `BepInEx/LogOutput.log` for
   the plugin's loading message and `Collection hooks ready`.

The existing automated installer installs MelonLoader only. Do not run it to
install this variant. The MelonLoader corlib overrides and MelonLoader command
line options are not BepInEx setup instructions.

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

The project, DLL and plugin now use the Better Collection Manager name.
On the first launch after the rename, the plugin imports the previous standalone
CFG and JSON settings directory when the new files do not exist. Existing new
settings take precedence; the previous files are retained. MelonLoader also
imports the renamed preference sections, keeping a `.before-bcm.bak` backup of
its shared CFG. Remove the previous mod DLL before installing the renamed one.


The existing in-game settings menu writes to
`BepInEx/config/bettercollectionmanager.cfg`. Sections and entry names match
the MelonLoader preferences. BepInEx saves are batched by the shared mod logic;
editing the CFG while the game is closed takes effect on the next launch.
JSON backups are written to `BepInEx/config/BetterCollectionManager/`.
Experimental Darkmoon artwork uses the BepInEx cache directory.

To migrate MelonLoader settings before the first BepInEx launch, copy
`premium_order.json` and `quality_settings.json` from
`Mods/BetterCollectionManager/` into
`BepInEx/config/BetterCollectionManager/`. JSON backups are imported only
when the BepInEx CFG does not exist; subsequent launches use the CFG. To import
again, close the game and back up and remove the CFG first. User catalog data
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

Hub experiments remain disabled in both loader variants.
