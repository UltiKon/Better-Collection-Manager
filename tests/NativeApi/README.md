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
