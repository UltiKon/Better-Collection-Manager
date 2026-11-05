# Better Collection Manager

## Abstract

Better Collection Manager is a Hearthstone mod that groups card qualities and
adds split Normal/Golden cards, ownership counters, filters, categories, combos
and packages. Each quality keeps its own inventory count and deck rules.

This package contains the **MelonLoader** mod DLL and source code. For the separate
BepInEx build, use the [BepInEx guide](README-BEPINEX.md). Hub/Darkmoon experiments
are disabled. Game or loader updates may require a new build; compilation does
not confirm compatibility with your client.

Version: **0.1.0** · Documentation updated: **2026-11-05**

## Installation

### If you have never installed a mod loader

Use the separate **`BetterCollectionManager-0.1.0-installer.zip`** and follow the
[installer guide](../installer/README.md). It contains the mod, setup scripts and
five reviewed Mono libraries, downloads the pinned MelonLoader release and
configures Battle.net. It requires **Python 3.10+**. Native macOS setup is
experimental. **macOS is untested, including CrossOver.** Windows Hearthstone
under Wine/CrossOver uses the Windows loader.

This smaller shareable ZIP assumes that MelonLoader already starts successfully.
It does not install the loader or its runtime workarounds.

### If MelonLoader is already working

1. Close Hearthstone completely.
2. Find the game folder containing `Hearthstone.exe` and `Hearthstone_Data` for
   Windows Hearthstone, including Wine/CrossOver installations.
3. Extract this mod ZIP. Copy **only** `Mods/BetterCollectionManager.dll` into the
   game's `Mods/` directory. Create `Mods/` if needed.
4. Remove any older copy of the mod, including one with its previous name. Keep
   your settings files. Run one loader and one copy of this mod per launch.
5. Restart the game, open **Collection Manager**, then **Settings → Mods**.
6. Check `MelonLoader/Latest.log` for `Better Collection Manager loading...` and
   `Collection hooks ready`. Change a setting and restart once to check saving.

For Windows Hearthstone, the installed mod belongs here:

```text
Hearthstone/
├── Hearthstone.exe
├── Hearthstone_Data/
├── MelonLoader/
└── Mods/
    └── BetterCollectionManager.dll
```

The BepInEx DLL has the same filename but cannot be used in `Mods/`.
Default categories and packages are embedded; you do not need loose catalog
files, source code or a .NET SDK for installation.

## First use and troubleshooting

Fresh installs prefer **Diamond → Signature → Golden → Normal**. Use the
collection's **expansion selector** for the extended filters. If cards appear to
be missing, clear the search box and filters: the mod can open with a search for
new or refundable cards.

No loader log means the loader has not initialized. A log with no mention of
this mod usually means the wrong DLL or folder; also check for load errors.
If the plugin starts but its UI is missing, open Collection Manager before
Settings and look for initialization errors.

See the [user guide](USER-GUIDE.md) for display controls and more troubleshooting.

## Update or remove

Close the game before replacing the DLL; hot reload is unsupported. Keep the
loader's preferences under `UserData/` and the mod's JSON backups under
`Mods/BetterCollectionManager/`. Existing preferences take precedence over defaults.
Disable the mod by moving its DLL out of `Mods/`.

## Included files

| Location in the ZIP | Contents |
| --- | --- |
| `Mods/BetterCollectionManager.dll` | MelonLoader Release DLL |
| `Source/` | Source code, tests, documentation and catalogs |
| `LICENSE` | MIT License for the original project code |
| `SHA256SUMS.txt` | SHA-256 checksums of the other archive entries |

Build instructions are in the [source overview](../README.md#build-from-source);
packaging instructions are in the [release guide](RELEASING.md). Game and loader
reference DLLs are not included. Personal settings, logs, caches and debug symbols
are excluded from release packages.

By UltiKon, Imik and contributors. See the [MIT License](../LICENSE) and
[third-party notices](../installer/ThirdParty/NOTICES.md).
