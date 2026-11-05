# Better Collection Manager

## Abstract

Better Collection Manager is a Hearthstone mod that makes your collection easier
to browse. It groups different qualities of a card, lets you choose which quality
to use in decks, and adds filters, categories, combos and packages.

For example, a card you own in both Normal and Golden can appear as one split
tile instead of two separate tiles. Your actual cards and ownership counts stay
separate.

Version: **0.1.0** · Documentation updated: **2026-11-05**

## How to install

**You do not need to compile the mod to use it.** Use a
[release ZIP](https://github.com/UltiKon/Better-Collection-Manager/releases) with a ready-made
`BetterCollectionManager.dll`. A mod loader is the separate tool that loads this
DLL when Hearthstone starts.

There are two loader versions of the mod. Choose the instructions for your package:

| What you have or need | Read this |
| --- | --- |
| A ZIP containing `BepInEx/plugins/BetterCollectionManager/BetterCollectionManager.dll` | [BepInEx installation](README-BEPINEX.md#how-to-install) |
| A ZIP containing `Mods/BetterCollectionManager.dll`, with MelonLoader already working | [Manual MelonLoader installation](DISTRIBUTION.md#installation) |
| Setup scripts and a `payload/` folder, or you want automated MelonLoader setup | [Installer guide](Installer/README.md) |
| The mod is installed and you want to learn its controls | [User guide](USER-GUIDE.md) |
| You want to change or compile the code | [Build from source](#build-from-source) |

The BepInEx and MelonLoader DLLs have the same filename but are different builds.
Use one loader and one copy of the mod per launch. The automated installer sets up
**MelonLoader only**.

The BepInEx port has been compiled against BepInEx 5.4.23.6 and the supplied game
references, but has not yet been tested in a running Hearthstone client. Native
macOS setup is experimental. Building successfully does not confirm compatibility
with your installed game version.

## Your first session

1. Install the matching loader and mod using the guide above, then restart the game.
2. Open **Collection Manager**, then **Settings → Mods** to find the mod's settings.
3. Open the collection's **expansion selector** to find the extended filters.
4. Try browsing and editing a deck, then restart once to check that settings persist.

Fresh installations prefer **Diamond → Signature → Golden → Normal**. Split
Normal/Golden tiles are enabled, and the collection can open with a search for
new or refundable cards. If the collection looks unexpectedly small, clear the
search and filters first. Existing saved settings take precedence over defaults.

See the [user guide](USER-GUIDE.md) for split-card controls, display options,
ownership counters and common questions.

## Build from source

This section is for developers. You need a .NET SDK, the reference DLLs from your
Hearthstone installation, and the core DLLs for the loader you are targeting.
Game and loader DLLs are not included here.

Run the commands below **from this source directory**: the folder containing the
`.csproj` files and build scripts. In a release ZIP, open a terminal in `Source/`.
Replace `/path/to/Hearthstone` with the folder containing `Hearthstone_Data`.

| Target | Bash command | Output |
| --- | --- | --- |
| BepInEx 5 Unity Mono | `GAME_DIR="/path/to/Hearthstone" bash build-bepinex.sh` | `bepinex-build/BetterCollectionManager.dll` |
| MelonLoader | `GAME_DIR="/path/to/Hearthstone" bash build-linux.sh` | `linux-build/BetterCollectionManager.dll` |

These scripts compile the mod; copy the resulting DLL to the matching loader's
folder yourself. For custom reference paths and direct build commands, see the
[BepInEx build guide](README-BEPINEX.md#build) or the
[Linux/MelonLoader build guide](README-LINUX.md).
Windows MSBuild instructions and the project file map are in the
[technical reference](TECHNICAL-REFERENCE.md#build).

### Run the managed checks

With a .NET 8 SDK, run these from the same source directory. These suites use
managed fixtures and do not need a running game:

```sh
dotnet run --project Tests/Defaults/Defaults.csproj -c Release
dotnet run --project Tests/MixedPremium/MixedPremium.csproj -c Release
dotnet run --project Tests/KeywordFilters/KeywordFilters.csproj -c Release
dotnet run --project Tests/HubMenu/HubMenu.csproj -c Release
dotnet run --project Tests/CollapseMode/CollapseMode.csproj -c Release
dotnet run --project Tests/Pagination/Pagination.csproj -c Release
python3 -m unittest discover -s Tests/Installer -v
```

Game API and BepInEx integration checks require additional references; see
[Native API checks](Tests/NativeApi/README.md) and
[BepInEx verification](README-BEPINEX.md#verification).
For packaging and release validation, see the [release guide](RELEASING.md).

## More documentation

| Document | What it covers |
| --- | --- |
| [User guide](USER-GUIDE.md) | Daily use, settings and troubleshooting |
| [Technical reference](TECHNICAL-REFERENCE.md) | Detailed behavior, implementation and in-game checks |
| [Default settings](BetterCollectionManager.cfg) | Compiled into both builds; no manual copy needed |
| [Changelog](CHANGELOG.md) | Historical changes |
| [Efficiency audit](EFFICIENCY-AUDIT.md) | Performance work and measurement limits |
| [Category definitions](Data/Categories.md) | Catalog coverage and classification |
| [Package manifest](Data/Packages.md) | Built-in packages |

The hub/Darkmoon experiments remain disabled in both loader builds.

## License and credits

By UltiKon, Imik and contributors. The mod, installer and original project code
use the [MIT License](LICENSE). Third-party components retain their own licenses;
see [third-party notices](Installer/ThirdParty/NOTICES.md).
