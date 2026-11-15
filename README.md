# Better Collection Manager

> [!WARNING]
> This is an unofficial community project and is not affiliated with, endorsed by,
> or supported by Blizzard Entertainment. Modifying Hearthstone is a grey area
> and may violate Blizzard's Terms of Service (ToS). Use at your own risk.

## Abstract

Better Collection Manager is a Hearthstone mod that makes your collection easier
to browse. It groups different qualities of a card, lets you choose which quality
to use in decks, and adds filters, categories, combos and packages. Export decks
as PNG images with native card artwork and optional statistics. Archive decks
locally, edit archived decks offline and restore them to a free saved slot.

For example, a card you own in both Normal and Golden can appear as one split
tile instead of two separate tiles. Your actual cards and ownership counts stay
separate.

Version: **0.3.1** · Documentation updated: **2026-11-15**

## How to install

**BepInEx already installed? Download the
[0.3.1 BepInEx DLL](https://github.com/UltiKon/Better-Collection-Manager/releases/download/0.3.1/BetterCollectionManager.dll)**
and place it in `BepInEx/plugins/BetterCollectionManager/` inside the game folder.

**You do not need to compile the mod to use it.** Both loader variants include a
ready-made `BetterCollectionManager.dll`. Full packages with source,
documentation and checksums are available for
[BepInEx](https://github.com/UltiKon/Better-Collection-Manager/releases/download/0.3.1/BetterCollectionManager-0.3.1-bepinex.zip) and
[MelonLoader](https://github.com/UltiKon/Better-Collection-Manager/releases/download/0.3.1/BetterCollectionManager-0.3.1-shareable.zip).
A mod loader is the separate tool that loads the DLL when Hearthstone starts.

There are two loader versions of the mod. Choose the instructions for your package:

| What you have or need | Read this |
| --- | --- |
| A ZIP containing `BepInEx/plugins/BetterCollectionManager/BetterCollectionManager.dll` | [BepInEx installation](docs/README-BEPINEX.md#how-to-install) |
| A ZIP containing `Mods/BetterCollectionManager.dll`, with MelonLoader already working | [Manual MelonLoader installation](docs/DISTRIBUTION.md#installation) |
| Setup scripts and a `payload/` folder, or you want automated MelonLoader setup | [Installer guide](installer/README.md) |
| The mod is installed and you want to learn its controls | [User guide](docs/USER-GUIDE.md) |
| You want to change or compile the code | [Build from source](#build-from-source) |

The BepInEx and MelonLoader DLLs have the same filename but are different builds.
Use one loader and one copy of the mod per launch. The automated installer sets up
**MelonLoader only**.

The standalone DLL is the BepInEx build; MelonLoader users need its separate ZIP.

### Mono runtime DLLs

**Full (unstripped) Unity/Mono libraries matching the game's Unity version are
also required for loader setup.** The reviewed set is `mscorlib.dll`, `System.dll`,
`System.Core.dll`, `Mono.Security.dll` and `Mono.Btls.Interface.dll`.

The [MelonLoader installer](installer/README.md#mono-runtime-dlls) includes these
files and configures their search path. The smaller mod ZIPs do not include them;
for BepInEx, follow the [Mono runtime setup](docs/README-BEPINEX.md#mono-runtime-libraries).
Keep them in the loader's override directory rather than replacing the game's
original DLLs.

## Operating systems and test status

| Operating system | Installation route | Status |
| --- | --- | --- |
| **Windows** | Windows x64 Hearthstone with BepInEx 5 Unity Mono, or the MelonLoader installer | Build and setup targets; verify with your installed client |
| **Linux** | Windows Hearthstone through **Wine**, using the Windows x64 loader | Wine setup is provided; there is no native Linux game build in this package |
| **macOS with CrossOver** | Windows Hearthstone using the Windows x64 loader | **Untested**; a Windows/Wine setup route is provided |
| **Native macOS** | Experimental MelonLoader x64 setup and its generated `Start-BCM.command` launcher | **Untested and experimental**; requires an x86_64 game executable. Apple Silicon also requires Rosetta. Native login is unverified |

**macOS has not been tested**, including both CrossOver and the native launcher.

Both 0.3.1 variants are built from the same source. The BepInEx build uses
BepInEx 5.4.23.6 references. Automated checks cover both DLLs' loader metadata,
shared version and embedded resources, along with settings, archive transactions
and UI behavior. A user has tested collection browsing, the local archive and
settings under Linux/Nobara with Wine and BepInEx. Windows, MelonLoader runtime
behavior and other platform combinations still need their own client checks;
building successfully does not confirm runtime compatibility everywhere.

## Your first session

1. Install the matching loader and mod using the guide above, then restart the game.
2. Open **Collection Manager**, then **Settings → Mods** to find the mod's settings.
3. Open the collection's **expansion selector** to find the extended filters.
4. Try browsing and editing a deck, then restart once to check that settings persist.

Fresh installations prefer **Diamond → Signature → Golden → Normal**. Split
Normal/Golden tiles are enabled, and the collection can open with a search for
new or refundable cards. If the collection looks unexpectedly small, clear the
search and filters first. Existing saved settings take precedence over defaults.

See the [user guide](docs/USER-GUIDE.md) for split-card controls, display options,
ownership counters and common questions.

## Local deck archive

Use **Show archived decks** / **Show saved decks** at the top of the collection's
deck list to switch views. In a deck's native delete confirmation, **Archive
instead** saves a local copy before deleting the saved deck. Archived decks can
be edited locally or restored to a free saved slot while online.

Toggle **Settings → Mods → Deck archive → Enable deck archive** to enable or
disable the feature (enabled by default). Disabling preserves your archives. They are stored per
account on this device; back them up when moving installations. See the
[archive guide](docs/USER-GUIDE.md#archive-saved-decks-locally) for details and paths.

To report a problem, use **Settings → Mods → Other → Report A Bug** to open the
[GitHub Issues page](https://github.com/UltiKon/Better-Collection-Manager/issues).

## PNG deck export

Open a deck, open its options menu, then choose **Copy as PNG**. When copying
finishes, paste the image into a chat or image editor.

Under **Settings → Mods → Deck image**, choose card quality, image resolution,
Portrait/Square/Landscape format and the background. Toggle the deck name and
class icon, crafting cost, mana curve, card types and rarities independently.
Sideboards are included. Fresh installs use **Normal**, **Medium** (1080 pixels
on the shorter edge), **Landscape** and **Native parchment**.

An empty information shield disappears. Exports go to the clipboard without
automatic file saving. See the [deck image guide](docs/USER-GUIDE.md#copy-a-deck-as-an-image)
for details.

## Repository layout

```text
src/BetterCollectionManager/   C# projects, mod code, patches and assembly metadata
config/                       Default settings snapshot
data/                         Embedded catalogs and catalog audit data
docs/                         Installation, user and developer guides
tests/                        Regression suites and fixtures
tools/                        Build, packaging and maintenance scripts
installer/                    Automated MelonLoader installer and license notices
artifacts/                    Local build outputs (ignored by Git)
dist/                         Generated release ZIPs (ignored by Git)
```

## Build from source

This section is for developers. You need a .NET SDK, the reference DLLs from your
Hearthstone installation, and the core DLLs for the loader you are targeting.
Game and loader DLLs are not included here.

Run the commands below **from the repository root**: the folder containing
`README.md` and `BetterCollectionManager.sln`. In a release ZIP, this is `Source/`.
Replace `/path/to/Hearthstone` with the folder containing `Hearthstone_Data`.

| Target | Bash command | Output |
| --- | --- | --- |
| BepInEx 5 Unity Mono | `GAME_DIR="/path/to/Hearthstone" bash tools/build-bepinex.sh` | `artifacts/bepinex/BetterCollectionManager.dll` |
| MelonLoader | `GAME_DIR="/path/to/Hearthstone" bash tools/build-linux.sh` | `artifacts/melonloader/BetterCollectionManager.dll` |

To build, verify and package **both** loader variants at the current version:

```sh
GAME_DIR="/path/to/Hearthstone" bash tools/build-release.sh
```

This needs a .NET SDK 8 or newer, Python 3.10+ and reference DLLs for both loaders.
The verifier uses .NET 8 when available, or a newer installed runtime such as .NET 10.
The two ZIPs are written to `dist/` only after both builds and metadata checks pass.

These scripts compile the mod; copy the resulting DLL to the matching loader's
folder yourself. For custom reference paths and direct build commands, see the
[BepInEx build guide](docs/README-BEPINEX.md#build) or the
[Linux/MelonLoader build guide](docs/README-LINUX.md).
Windows MSBuild instructions and the project file map are in the
[technical reference](docs/TECHNICAL-REFERENCE.md#build).

### Run the managed checks

With a .NET 8 SDK, run these from the repository root. These suites use
managed fixtures and do not need a running game:

```sh
dotnet run --project tests/Defaults/Defaults.csproj -c Release
dotnet run --project tests/MixedPremium/MixedPremium.csproj -c Release
dotnet run --project tests/KeywordFilters/KeywordFilters.csproj -c Release
dotnet run --project tests/HubMenu/HubMenu.csproj -c Release
dotnet run --project tests/CollapseMode/CollapseMode.csproj -c Release
dotnet run --project tests/Pagination/Pagination.csproj -c Release
dotnet run --project tests/DeckImages/DeckImages.csproj -c Release
dotnet run --project tests/DeckArchive/DeckArchive.csproj -c Release
python3 -m unittest discover -s tests/Installer -v
```

Game API and BepInEx integration checks require additional references; see
[Native API checks](tests/NativeApi/README.md) and
[BepInEx verification](docs/README-BEPINEX.md#verification).
For packaging and release validation, see the [release guide](docs/RELEASING.md).

## More documentation

| Document | What it covers |
| --- | --- |
| [User guide](docs/USER-GUIDE.md) | Daily use, settings and troubleshooting |
| [Technical reference](docs/TECHNICAL-REFERENCE.md) | Detailed behavior, implementation and in-game checks |
| [Default settings](config/BetterCollectionManager.cfg) | Compiled into both builds; no manual copy needed |
| [Changelog](CHANGELOG.md) | Historical changes |
| [Efficiency audit](docs/EFFICIENCY-AUDIT.md) | Performance work and measurement limits |
| [Category definitions](data/Categories.md) | Catalog coverage and classification |
| [Package manifest](data/Packages.md) | Built-in packages |

The hub/Darkmoon experiments remain disabled in both loader builds.

## License and credits

By UltiKon, Imik and contributors. The mod, installer and original project code
use the [MIT License](LICENSE). Third-party components retain their own licenses;
see [third-party notices](installer/ThirdParty/NOTICES.md).

Deck image quantity-label artwork comes from HearthstoneDeckView by H4X4D under
the [MIT License](data/DeckViewLabels/LICENSE). Update and release packages include
its notice under `ThirdParty/HearthstoneDeckView-LICENSE.txt`; the notice is also
embedded in the mod DLL.

Developed with AI assistance, including OpenAI Codex.
