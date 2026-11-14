# Installed-client API checks

Run from the repository root with the actual client's `Assembly-CSharp.dll`:

```sh
dotnet run --project tests/NativeApi/NativeApi.csproj -- /path/to/Assembly-CSharp.dll
```

This reads assembly metadata without loading Unity or executing game code. It
checks the native recipe fields, token relationship API, portrait texture API and
deck-animation entry points, overlay ancestry and keyword-tooltip API used by
packages, as well as native rune banners, package lock plates, the collection
drag entry point. The generic collection-page loader and its native ready-callback
signature are checked for package preparation before a page transition.
It also checks the hub button fields, private menu hooks,
Lucky Draw controller/offer API and Modes navigation/state lookup used by the
experimental menu changes, including the native widget event and initialization
API used by the static entry's ready callbacks. Native DFT model getters,
entry-library device paths, Pet icon references and asynchronous HeroSkin
portrait resolution are checked as well. It complements the managed behavior
checks in `tests/MixedPremium` and `tests/HubMenu`; these suites do not replace building the mod with all
client references or checking rendering and animation in the game.

Settings edge capture also checks the private view/projection/target fields and public capture methods in the adjacent `Blizzard.T5.UberText.Runtime.dll`. Filter chrome checks include `EnableIfNeeded` and native highlight press/release sound fields.

Press-time feedback also verifies native checkbox sound fields and the owned-dropdown button, selection, cancel and item-creation APIs.

Package navigation checks also verify the native deck-entry/exit methods and page
transition signature used to preserve an open folder's page without an automatic flip.

Inspected-card disenchant choices also check the native release hook, craftability,
pending transaction identity/count APIs, per-quality pricing and inspect-close
submission methods. The custom flow uses only the inspected card's transaction.

Collection opening searches and stable New paging also verify native result
generation/search setters, the scoped new-collectible getter, acquisition-date
metadata, book readiness, page transitions and refund-event pricing metadata.

The metadata checks also verify `Navigation.CanGoBack` and `Navigation.GoBack`.
Batch disenchant closes the preview through its registered native Back handler,
so its callback runs and its entry is removed.

Crafting page preservation checks the native entry/exit filter methods, the
page-number field and card-filter field used before native page assembly.

Deck image export checks the deck menu's copy button and layout anchors, slot and
sideboard enumeration, dynamic definitions and native actor readiness. It also
checks full premium portrait materials, class symbols, quantity shields, Arcane
Dust, rarity meshes, mana-fill bars, collection mana crystals and the native
menu surface for the title and statistics panel. The sideboard renderer applies
the native incompatible-rune materials directly to its private export actors.

Run `dotnet run --project tests/DeckImages/DeckImages.csproj -c Release` for managed
snapshot, layout, compositor, blur and Windows bitmap-format checks. These cover
Portrait, Square and Landscape, automatic columns and canvas dimensions, fixed card
resolution, every statistics combination, bounds,
non-overlap, centered rows, single/multiple-copy labels and settings changes
mid-export. Artwork checks include bundled class backgrounds and neutral fallback,
moderate blur with preserved broad forms, flat portrait-margin removal, centered cover cropping, opaque pixels and
cancellation while background assets load. Card fixtures verify equal body size
with larger legendary ornaments, fixed tiles, streaming capture/paint ordering, reference quantity labels and soft silhouette shadows.
Native graphics fixtures verify mesh reuse, private materials, class selection,
regular/wide quantity borders, inactive-currency filtering and graphics-only
copies without UI controllers. They do not execute Unity rendering.

For visual blur review, set `BCM_DECK_IMAGE_BLUR_FIXTURES` to a directory of `.rgb`
files (little-endian width/height followed by top-down RGB) when running DeckImages.
The production background painter and PNG encoder render each fixture in all
three formats. This preview mode does not run the behavioral checks.

In the client, verify the button below Copy, incomplete decks, mixed qualities,
long localized text, sideboards (including customized Zilliax), cancellation on
leaving the editor and pasting into both a chat and image editor. Check native
macOS and Windows/Wine clipboard paths separately. Review real exports at 1080
pixels wide in all three formats, with class backgrounds, parchment, blurred artwork and decoration
disabled. Check class/Wild symbols, native shield/text alignment, sideboard red
rendering, rarity gems, native mana fill and saved settings after restart.

Include Astral Vigilante as the first card, all quality choices, material-only
Golden cards and unavailable Diamond/Signature variants. The RGB compositor
keeps native layered Golden materials. Its capture helper renders opaque
black/white camera backgrounds and derives coverage from their RGB difference.
Tests execute the helper against a camera that discards pre-render seeds,
checking empty areas, zero-alpha layered artwork, real opaque black details,
antialiased glyphs, gamma and linear composition, and state restoration on
success and failure. Managed checks cannot verify native shaders or GPU output.
