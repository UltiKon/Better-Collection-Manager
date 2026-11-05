# Better Collection Manager user guide

This guide starts after installation. To install the mod, use the
[documentation overview](README.md#how-to-install) to choose your package.

## Find the controls

- **Settings → Mods** opens the collection settings.
- The collection's **expansion selector** opens the extended filter menu.
- While browsing with a mouse, **left-click or right-click** opens a card preview.
  When editing a deck, left-click keeps its add-to-deck action.

## Understand card qualities

Normal, Golden, Signature and Diamond are different card qualities. The mod can
group them into one collection entry and show your preferred owned quality.
Each quality keeps its own inventory count and deck rules.

The default priority is **Diamond → Signature → Golden → Normal**. Change the four
priority dropdowns under **Settings → Mods → Preferred Quality** to reorder them.
Moving a quality to another priority shifts the others automatically.

Reprints can also share an entry when Hearthstone identifies them as equivalent.
Only editions allowed by your current filters participate; their quantities stay
separate.

### Split Normal/Golden cards

With the default split display, an eligible card with one Golden and one Normal
copy can appear as a single card with **Golden above the name banner** and
**Normal below it**.

- Click the upper or lower region to select that version.
- When dragging, the region where you first press selects the version. Moving
  across the seam does not switch it.
- While editing a deck, the drag setting can offer a quality choice after dropping.

You will not see a split on every card. Two owned copies of the preferred quality
can use a full tile, and preferred Signature or Diamond cards keep their own
display. See the [technical reference](TECHNICAL-REFERENCE.md#priority--ownership)
for the full eligibility rules.

### Ownership counters

A counter such as `1+2` means one copy of the displayed quality and two of the
next included quality, following your saved priority order. For example, a Golden
tile showing `1+2` can represent one Golden and two Normal copies.

Zeros can appear for existing but unowned qualities. Qualities that do not exist
for that card are omitted. A counter does not combine copies across reprints or
change the number you can put in a deck.

**Color code counter**, off by default, colors each quality's number and omits
zero quantities. With coloring enabled, a single remaining quantity above one
uses a label such as `x2`; a total of one hides the counter.

## Adjust the display and deck controls

Open **Settings → Mods**. Start with these controls:

| Control | What to use it for |
| --- | --- |
| **Collapse quality variants** | Group different qualities of a card together |
| Display mode | Choose how grouped cards appear, such as split tiles or stacking |
| **Only one copy at highest quality** | Choose whether the display should account for a second deck copy |
| **Add to deck by clicking** | Choose how a click selects a quality |
| **Add to deck by dragging** | Choose how a drag selects a quality, including a choice after dropping |
| **Hide redundant settings** | Hide controls that do not apply to the current choices |

Fresh installs use split display, preferred-quality order for clicks and a quality
choice after dropping. If a setting is missing, turn off **Hide redundant settings**
in **Other** to reveal the available overrides.

## Search and filters

Open the collection's **expansion selector** to browse the extended filter menu.
It includes expansions, keywords, card types and other groups, as well as
categories and packages. Start by selecting one filter; use **Clear All** to
remove mod filter selections. Clear the search box separately when needed.

Empty filter options are hidden by default. Turn off **Hide empty filters** in
the mod settings if you want to see them. Rows and columns are automatic on a
fresh installation.

Categories group related cards. Combos collect selected cards into a shared
entry, and packages organize groups of cards. For editing, import/export and
deck-building behavior, see the
[shared filter menu reference](TECHNICAL-REFERENCE.md#shared-filter-menu).

### Why does the collection open with a search?

Fresh installations use **New or Refund (Refund first)** under
**Crafting → Search on collection open**. The mod looks for new or refundable
cards using the game's search terms. Choose **Off** if you prefer to open the
collection without this automatic search.

### Why do I sometimes see separate qualities?

An explicit quality search such as `golden`, `diamond` or `signature` keeps the
separate results. Crafting also keeps separate qualities by default. You can
enable **Crafting → Collapse while crafting** to group crafting results while
keeping missing cards visible.

## Settings and updates

Use the in-game menu for normal changes. Saved preferences take precedence over
the documented defaults. You do not need to copy the supplied
`BetterCollectionManager.cfg` snapshot beside the mod DLL.

If you edit settings files yourself, close Hearthstone first:

| Loader | Main settings | JSON backups |
| --- | --- | --- |
| BepInEx | `BepInEx/config/BetterCollectionManager.cfg` | `BepInEx/config/BetterCollectionManager/` |
| MelonLoader | Shared loader preferences under `UserData/` | `Mods/BetterCollectionManager/` when the DLL is installed in `Mods/` |

Paths are relative to the Hearthstone game folder. Keep these files when updating.
Close the game, remove the old mod DLL, install the matching new one and restart.
If upgrading from the previous mod name, keep the old settings: the renamed mod
imports them when the new settings do not exist.

Switching loaders needs a separate settings migration; see
[BepInEx migration](README-BEPINEX.md#settings-and-migration).

## If something looks wrong

| Symptom | First thing to check |
| --- | --- |
| No **Mods** button | Open Collection Manager first, then Settings. Check that the loader log lists the mod. |
| Missing cards or a small collection | Clear the search box and mod filters, then check the selected format and expansion. |
| A card does not split | Check that both qualities are in the current results and the card meets the split rules above. |
| Separate qualities while crafting or searching | Check the crafting option and explicit quality search terms above. |
| A setting seems missing | Turn off **Hide redundant settings** under **Other**. |
| A change disappears after restart | Check the loader log for save errors and that the settings directory is writable. |
| The mod stopped working after an update | Check that the DLL matches your loader and that only one copy is installed. Game updates may require a new build. |

For BepInEx, open `BepInEx/LogOutput.log`. For MelonLoader, look in
`MelonLoader/Latest.log`. Search for `Better Collection Manager loading...` and
`Collection hooks ready`. If there is no loader log, resolve loader startup first.

When reporting a problem, include the mod version, loader version, operating
system, game version, steps to reproduce and the relevant log error. Review logs
for personal paths before sharing them. Disable the mod by closing the game and
moving its DLL out of the loader's plugin/mod folder.
