# Better Collection Manager technical reference

This document preserves detailed behavior, implementation notes and manual
regression checks. Start with the [overview](README.md) for installation or the
[user guide](USER-GUIDE.md) for everyday use.

Some sections record earlier development states. For current defaults, use the
[default-settings snapshot](BetterCollectionManager.cfg): opening search defaults
to **New or Refund**, and quality order starts with **Diamond**, then **Signature**.

The mod, installer and original project code are licensed under the
[MIT License](LICENSE), including its warranty and liability disclaimer.
Third-party components retain their own licenses; see
[the third-party notices](Installer/ThirdParty/NOTICES.md).

Distribution documentation updated: 2026-11-05. Version: 0.1.0.

By UltiKon, Imik and contributors. Fresh installations use the values in the
[default-settings snapshot](BetterCollectionManager.cfg): Diamond before
Signature, quality-order clicks, quality selection after dropping, opening
search for new/refundable cards, automatic filter columns, hidden empty filters,
and the disenchant popup disabled. Saved user preferences take precedence.
The snapshot is included in source distributions; its values are compiled into
both loader builds and do not require a loose CFG beside the DLL.


An additional **BepInEx 5 Unity Mono** build is available. See
[BepInEx build and installation](README-BEPINEX.md) for its separate DLL, build
command and settings migration. Use one loader variant per launch. The automated
installer described below installs MelonLoader.

For the automated setup, see [Installer/README.md](Installer/README.md). The separate
`BetterCollectionManager-0.1.0-installer.zip` contains the mod and five full Mono libraries;
it installs the pinned MelonLoader release and configures Battle.net. Windows and
Wine/CrossOver use the Windows loader; native macOS has an experimental separate
launcher. The setup requires Python 3.10+ and downloads MelonLoader on installation.

The original shareable source ZIP contains a ready-to-install mod DLL in `Mods/`,
plus this source tree in `Source/`. That smaller package assumes MelonLoader and
any necessary runtime workarounds are already configured. Categories and packages
are embedded in the mod DLL. See the ZIP's root README for its quick start.

See [CHANGELOG.md](CHANGELOG.md) for historical changes; later behavior is
documented below and in the current source. This is not a new game-compatibility
certification. The bundled hub/Darkmoon experiments remain disabled.

MelonLoader / BepInEx mod for Hearthstone that de-clutters **Collection Manager** so each card appears **once**, as the highest owned quality:

1. Diamond
2. Signature
3. Golden
4. Normal

Collapse runs **before** page bucketing, so scrolling shows filled pages of up to **8** preferred tiles (no holes from hidden premiums).

Also active while **deck-building / deck-edit** inside Collection Manager.

Reprints linked by the client's deck-equivalence metadata are also deduplicated.
Only editions surviving the active set, format and search filters are eligible.
The edition with the preferred owned quality wins; **Prefer two copies** first
prefers an owned playset. Equal qualities favor a playset, then native result order.
The selected edition retains its own premium variants, split/stack behavior,
inventory counts and deck insertion ID; copies from different editions are not
added together. Identical names or Core/Classic ID prefixes alone do not establish
equivalence. Crafting, explicit premium searches and disabling preferred-quality
collapse retain the existing uncollapsed behavior.

By default, when the existing two-copy rule would show both Normal and Golden versions of a
Common, Rare, or Epic card, they share one tile split horizontally at the
name banner: **Golden above, Normal below**. The seam uses the banner's center
when available, with a fallback for other frames. The complete Golden name
banner is kept across the seam and the Normal banner is hidden. Two owned copies of the preferred
version keep the full preferred tile, even when the other version is also owned.
Click a region to use that version; dragging keeps the version chosen at the initial
press, even if the pointer crosses the seam. Native ownership counts and deck
rules remain separate for each premium. Deck locks follow the selected half.
A preferred Signature or Diamond retains its own display.
The projection camera determines which local card direction points upward on
screen; artwork clipping and pointer selection use that same orientation.
The secondary actor is positioned using the main frame mesh centers as well as
the actor transforms, keeping differently offset Normal/Golden prefab meshes
aligned horizontally. Alignment is reapplied after native actor state changes.

Collapsed tiles display an ownership breakdown such as `0+1+2+2`, in the saved
premium order. Existing premiums at or below the displayed version retain `0`
when unowned. Unowned versions above it are omitted; nonexistent versions are
omitted using the game's exact card/premium index. A Golden tile with one Golden
copy and no Normal copies shows `1+0`; a Normal tile with two copies and no Golden
copies uses the original single-quality counter. Selecting the lower half of a
split tile does not change this reference quality. This also applies to full preferred tiles. The label keeps a
constant native font size without auto-fitting or line wrapping. The counter tab
fits the measured rendered text with a fixed horizontal inset on each side,
independent of text length, and uses actual numbers instead of `9+` in breakdowns.
The `+` separators keep half the numeric glyph size and are positioned on the
measured vertical center of the numbers, with an optical downward offset of 8%
of the numeric ink height. Separate native text labels provide
that offset without changing the digits or relying on unsupported baseline tags.
The inline separator gaps remain in the numeric label; its original plus glyphs
are transparent. Private separator labels are removed when the tile is released.
Horizontal frame padding is fixed at 0.30 card-space units on each side.
Only the width and horizontal position follow the measured glyph bounds;
the native frame height and vertical position are preserved. Measurement projects the
native offscreen glyph bounds through the actual text camera and output surface,
then sizes and centers the frame around those visible glyphs with equal padding
on both sides, including native label/frame offsets. Unavailable first-frame geometry keeps a usable
frame until a later update can measure it. The native theme color and border
material are initialized even when native single-copy counts skip styling, so
split-card counters do not depend on hover to acquire their proper text color.
When only one quality remains in the breakdown, the original native text, frame,
font settings and visibility rules apply, including the native `9+` format.
It does not change native per-premium counts, inventory,
trial-card totals, or deck validation. Crafting/premium searches and unmerged
separate tiles retain their native counters.

The optional **Color code counter** checkbox in Mods colors the numbers in
multi-quality counters: Normal white (`#FFFFFF`), Golden amber (`#FFB300`),
Diamond cyan (`#00D5FF`), and Signature lilac (`#D291FF`). These more saturated
fills retain at least 7:1 luminance contrast against the opaque black outline.
With coloring enabled, zero-owned qualities are omitted. The tab, separators
and outline follow the shortened count; disabling coloring restores positional
zeros. A lone remaining quantity above one keeps its quality color and uses an
`x` prefix in the same quality color as the number (for example `x2`). A total of one owned copy hides the counter,
including when other existing qualities have zero copies.
The numeric label uses neutral white RGB tint and gradients so the native theme does not
darken or recolor these values; native transparency is preserved. A thin
black outline behind the digits improves contrast against
the counter tab without changing text size or frame measurements. The actual
visible text renderer (including the output quad in texture mode) is sorted
in front of the black copies, so the colored face covers their interiors.
Both the outline and the neutral tint are removed when counter coloring is disabled
or a tile is released. The plus separators retain their native theme color.
Outline copies use a 1.25-pixel offset in eight directions and full black opacity.
Native fading still scales their opacity.
It defaults to off and is saved with the other collection settings.

## Collection clicks

While browsing the collection with a mouse, left-click opens the same native
preview as right-click for cards, hero skins, card backs, coins, Battlegrounds
hero/guide skins, and pets. Editing a deck retains native add/equip actions.
Touch controls, dragging, crafting-tray interactions and specialized selection
views retain their native behavior. On split Normal/Golden tiles, left-click
inspects the version selected when pressing, even if the pointer crosses the seam
before release. Right-click remains available.

## Mods menu

The native Settings menu has a **Mods** button below the Signature settings in
the lower left. It clones the native Privacy button and replaces only its
separate metal surround with the Graphics field's metal decoration and screws
(quality, with FPS/resolution as fallbacks). The trim copies static meshes,
materials and renderer color overrides, excluding the dropdown's selected field,
arrow, menu, labels and feedback layers. Only the old surround's renderers are
hidden; the button's gold face, caption, input and native hover/sound behavior
remain. The decoration fits the old surround's bounds and has no control scripts
or colliders. Privacy supplies its horizontal row, and it centers beneath the
full Signature dropdown, including that dropdown's arrow. The initial 350/390
width calibration is refined by 354/351 to match the measured metal frames,
with a small rightward optical offset of 1.5/354 of the resulting width.
These corrections scale with the UI rather than adding fixed screen pixels.
Layout uses the controls' hit colliders rather than animated artwork and
runs on opening or resolution changes, leaving native hover/press animation
alone. The clone retains its own native highlight and sound listeners. The
caption is centered by its visible glyphs and retains its native proportions as
the frame width changes. It opens a native submenu titled **Collection Mod Settings**, with the main
heading typography and full-size banner copied from the original Options dialog.
Its text keeps the original vertical offset within the banner.
The submenu copies the native game-menu bone's position,
rotation and scale into its own hierarchy so it has the native menu's placement
without depending on that menu being active. One enlarged page groups all settings
under native-style **Card Display / Handling**, **Preferred Quality**, **Filter menu**,
**Crafting**, and **Other** headings, without page switching. Each group reuses the artwork and typography
of the native Graphics/Audio section banner; copies contain only mesh/material
data and no native input components. Both mirrored banner halves retain their
local transform hierarchy, and the white, bold section text is centered inside
the complete artwork. Long headings widen the banner's middle on private meshes,
keeping the original ornament sizes and clear space around the text. The required
width includes room inside the ornaments. Every adjoining mesh, including narrow
decoration pieces, follows the same horizontal vertex mapping so their shared
edges remain closed. White text gradients and an explicit drawing order in front
of the banner prevent its translucent artwork from tinting the heading gray.
Gold field captions are left-aligned at the native checkbox-column inset above
their dropdowns; section banners stay centered. Field captions are excluded from banner-template
selection. Banner bounds participate in the layout.
Card Display / Handling and Preferred Quality sit side by side. The Filter menu
group sits directly beneath the handling settings in the left column, with its
three fields arranged vertically, including **Filter mode control**. **Show Clear All**
also belongs to this group. Hiding redundant settings moves this group
up rather than leaving gaps. The background reserves the full layout height and
width so changing redundant-field visibility preserves the menu size, scale and
position. It scales only to fit the available viewport width and height. The complete dialog,
including its native header artwork, is centered in the full game viewport rather
than aligned to the game menu's upper anchor.
Card Display / Handling contains **Only one copy at highest quality**, **Add to deck by
clicking**, **Add to deck by dragging**, **Collapse quality variants**, and
**Color code counter**. Checkbox squares retain the native inset from the left edge
of the dropdowns, independent of caption length. Preferred Quality contains four
priority dropdowns, labeled Priority 1 through Priority 4. **Hide redundant settings**
has its own **Other** section at the bottom of the right column, below Crafting.
Crafting follows the quality priorities. In the display dropdown, **Highest quality only**
follows **Stacking**.
Selecting a quality at a different rank moves it there and shifts the remaining
qualities while preserving their relative order; every quality remains present
exactly once. This edits the existing saved premium order and collapse preference.
In combined or negative modes, next-filter mode and while hiding empty options, expansion selection
opens a broad native Wild card pool and applies the selected groups before pagination
and quality collapsing. Native restrictions remain available for checking other
expansions, including specific-card groups. Keyword availability follows the combined pool.
Multiple selected rows retain their native selection glows. Switching back to Single
restores the last selected row's original native callback arguments when empty-option hiding is off.

Outside dismissal uses the fitted visible panel boundary, excluding oversized
native shadow and feedback meshes. A press outside stays silent; releasing outside
closes the menu and plays the native panel slide-off sound. Toggle dismissal
uses the same slide-off sound alongside the button's press feedback. Dragging outside also
dismisses it when the gesture started outside. Scrollbar drags retain capture.
Duplicate close handlers cannot replay the sound.

The Mods settings panel keeps its established outer size, including when the
Crafting group is present. Scroll down to reach its search and dust-protection
options. When additional settings exceed that surface, use the mouse wheel or the native Hearthstone scrollbar, using the same rail, end caps,
textures and grip artwork as the filter menu. The grip keeps its original proportions.
Pressing anywhere on the track, including the grip, centres the grip at the pointer
within its travel limits. Dragging continues outside the track until release. A scrollbar release
cannot close Settings. The bar fits beside the existing controls inside the original
frame, and wheel scrolling keeps its grip synchronized. The title and frame remain
fixed. Only complete native controls are shown inside the viewport, and expanded
dropdowns keep their own scrolling and modal input. Scroll position survives live
preference changes; the bar disappears when all visible settings fit.
Long dropdowns in Mod Settings and the combo editor show at most eight native
option rows with a native-art scrollbar and wheel support. The current selection
starts in view. Lists created after the native show callback also receive their
scrollbar. Opening and pressing dropdown rows stay silent; actually closing a
dropdown plays one click. Text fields stay silent. Scrollbar copies include valid meshes even if native clipping
disabled the source renderer, and use private materials ordered above the dialog.

Filter menu sets **Visible filter rows** (1–16, or **Automatic**) and **Filter
columns** (1–4, or **Automatic**). Automatic is resolved separately for each menu
from its actual entries and headings: short lists shrink; longer lists extend
within a compact limit of twelve visible rows and three columns, at most 78% of
screen height and 52% of screen width. The opening button also limits height;
the original tray height does not. Fully automatic grids allow roughly a quarter
viewport of additional rows before leaving a single column, saving screen space
when only a little scrolling is needed. Automatic columns keep medium lists at
two columns, with scrolling; a third column requires at least 100 selectable
entries. Headings do
not count toward that threshold. The same rule applies to every submenu, so
ordinary expansion lists remain narrower while very large keyword lists can use three columns
and twelve rows within those screen limits. Further entries remain scrollable so
cards stay visible beside the menu. A fixed
row or column count constrains the other automatic dimension. All values are
saved immediately.
Only the selected section's fields are shown. Each dropdown has a plain-text
heading using the native Graphics caption's font, color and alignment, cloned
from the native Settings control. Only the dropdown's referenced parts
are retained, excluding the source section's heading and favorite/help text.
Headings are fresh single-line text components using the native Graphics font
and color. They do not clone old glyph meshes, wrap words or reuse offscreen
text surfaces. Layout measures visible glyphs and closed-field colliders in
screen space, avoiding the native text renderer's offscreen mesh bounds.
The menu width includes the closed dropdowns' full metal trim and arrow, with
padding on both sides; popup and text-render meshes do not enlarge the frame.
The heading/field gap is measured from Signature Card Appearance before hiding
Settings, then preserved relative to field height. Card display, Click quality
and Drag quality appear from top to bottom. Extra padding separates the last
field from the bottom border. The content fits the available
viewport and is measured again when the window size changes.
There is no Back button. Escape first closes an expanded
dropdown; otherwise Escape returns to Settings. Clicking outside returns
to Settings. Destroying the owning Options menu also removes its Mods submenu.
Dropdown changes reuse the open menu's single native input lock. Closing or
destroying the menu releases that lock. Collection changes are queued by the
settings callbacks and applied in the update loop, even while Settings, Mods
or a dropdown remains open. Multiple changes before that update share one
refresh. The refresh does not reopen menus or alter their modal input claims;
it waits while a card is held or crafting is active.

Dropdown readability is corrected globally through the shared native
`DropdownMenuItem.UpdateTextColor`: entries with a visible selected background
use white text, matching native hover text. This also applies to dropdowns
outside Mods and remains white after moving the pointer away. Unselected rows,
hover behavior and closed value fields retain their native colors.

**Only one copy at highest quality** applies to the case where the highest owned quality has one
copy and lower qualities are also owned. All modes use the existing preferred
quality order:

| Option | Behavior |
| --- | --- |
| Highest quality only | Show the highest owned quality alone. |
| Split card (default) | Share a Normal/Golden Common, Rare or Epic tile. Incompatible frames, such as Signature/Golden, remain two separate tiles. |
| Stacking | Show one Imik-style stack when the preferred quality has only one owned copy and another owned quality can supply the second deck copy. The full preferred card sits in front, with the second quality exposed on the left. The rear artwork mirrors about the vertical card axis so its mana cutout sits beneath the front card. Supports special frames without cutting their meshes. |
| Two highest qualities | Show the two highest owned qualities as separate tiles. |
| Prefer two owned copies | Show the highest quality with at least two owned copies, otherwise the highest owned quality alone. Legendary cards keep their highest quality. |

Two owned copies of the highest quality retain one complete tile. Display
selection does not change inventory or the configured quality order.

**Hide redundant settings**, a native checkbox under **Other**, is enabled by
default. It hides **Add to deck by clicking** in **Highest quality only**,
**Stacking**, and **Prefer two owned copies**. Highest quality only and Stacking
automatically use **Quality order**; Prefer two owned copies uses **Highest fully available**. **Two highest qualities** hides both
input dropdowns. Hidden rows compact the left-hand content while the outer dialog keeps its full
size. The Other checkbox remains available by scrolling. Unchecking it reveals all three dropdowns and makes
manual click overrides editable and effective in Highest quality only, Stacking,
and Prefer two owned copies. Previous manual preferences are retained while hidden; the
checkbox persists in both MelonPreferences and the JSON backup.

**Add to deck by clicking** offers **Clicked half**, **Quality order** (default),
and **Highest fully available**. **Add to deck by dragging** lists
**Dragged half** (with **Split card** or **Stacking**), **Choose after dropping** (default), then
**Same as click**.
Dragged half uses the half picked up independently of the click policy. Outside
Split card and Stacking it is hidden and uses Same as click, retaining the saved preference
for returning to either merged display. Same as click inherits the effective click policy,
including automatic policies while redundant settings are hidden. With Clicked half, dragging uses
the half picked up at the start of the drag. Choose after dropping remains an
explicit quality choice even when the click policy is locked.
Highest fully available follows the quality order but first considers qualities
with an owned/trial playset: two copies for non-Legendary cards, one for Legendary
cards. It checks the owned total rather than unused copies, so the second addition
still prefers the same playset. Exhausted or illegal qualities are skipped; if
no complete playset is usable, it falls back to the normal quality order.
Saved click LowestFirst preferences migrate to HighestFullyAvailable. Previous
quality-order/playset drag preferences migrate to Same as click; Dragged half and Choose after dropping are
preserved, including its saved numeric value.
Clicked half appears with **Split card** and, when redundant settings are shown,
**Stacking**. Stacking defaults to Quality order for clicks on either part of the
stack. An explicit Clicked half override selects the lower priority quality from
the exposed left frame and the preferred quality from the front card. A saved half preference uses
**Quality order** in Highest quality only. The saved half preference is restored
when returning to Split card or showing manual overrides in Stacking. The Half options remain restricted to those two modes even when redundant
settings are shown. Two highest qualities uses native direct quality selection,
including when two copies at the highest quality mean only one tile is needed.
Split card also uses native direct selection for incompatible frames or a failed
split renderer that requires separate tiles. These input settings apply only to
mouse gestures adding collection cards while editing a deck. Browsing previews,
cosmetics, touch, crafting, specialized pickers, deck imports and moving existing
deck tiles retain their native input behavior.

For a merged tile, an exhausted requested quality falls back to the next usable quality. Selection
uses current owned/trial counts minus copies already in the deck and sideboards,
and validates the active destination's native rules. Removing a card therefore
makes that quality available again. Only variants admitted by the current
collapsed result are eligible. A fallback changes the added quality without
changing the collection tile's artwork or inspection actor.

Drag input uses the native collection pickup event to capture the card. A mouse
release over the deck is intercepted before the held-card update can insert it.
The native drag is canceled to clean up the held visual, then the chosen quality
is added on the next frame. Choose after dropping shows the available qualities
instead, with no insertion until the user selects one. Both a held-card update
prefix and the mod update observe release; handling it once clears the capture.
This path works independently of pickup and internal AddCard Harmony hooks.
Canceled drops, drops outside the deck and moving existing deck tiles keep
their native behavior.

Choose after dropping opens a native modal picker after drag cleanup when more
than one quality can be used. Nothing is inserted before confirmation. A single
usable quality is added without a dialog; Cancel, Escape or an outside click
adds nothing. Confirmation rechecks the deck, destination, inventory and native
rules. Changing decks, leaving the collection or changing these settings cancels
an outstanding choice. Copy-limit, banned and not-playable locks remain active.
On merged tiles, the per-quality "no more instances" frame is suppressed unless
only one total owned/trial copy exists; native copy-limit locks are shown instead
when needed. Separate quality tiles and Two highest qualities keep their native
inventory frames, matching their direct quality selection.

Choices persist in MelonPreferences under `BetterCollectionManagerBehavior`
and in `Mods/BetterCollectionManager/quality_settings.json`. The existing
premium-order preferences and backup remain separate. Display changes refresh
the collection; refresh waits until a held card has been released.

## Shared filter menu

The collection's existing expansion-selection button now opens a **Filters** menu:
Expansions, Keywords, Categories, Card Type, Minion Type, Spell School, and Rarity.
Preferred Quality is now under Settings → Mods → Preferred Quality.
The separate mod plaques above the friends list are no longer created.

**Filters → Categories** provides an explicit card catalog for **AoE**,
**Single-Target Removal**, **SMOrc**, **Value**, **Win Conditions**, **Draw**,
**Unique Effects**, **Combos** and **TECHW**, in that order. TECHW covers targeted
counterplay: weapon/secret hate, enemy silence and hand/deck/spell disruption;
ordinary removal and healing are not automatically tech. The runtime uses fixed
card IDs and does not classify localized card text. Roles may overlap: for
example, Coldlight Oracle is listed under both Draw and Value. Categories defaults
to Single and also supports OR, AND, NOT, AND NOT, OR NOT and Next filter expressions. It intersects
the existing expansion, keyword, type, race, school, rarity and native filters.
Hide-empty, selection glows, submenu Clear All and global Clear All work as usual.

**Uncategorized** is a computed category for cards without any effective category
assignment, including imported custom categories. Adding a card to a category
removes it from Uncategorized; removing its last assignment returns it there.
Core and Classic aliases share that status. A combo's captured cards do not gain
standalone category membership merely by being part of the combo. Uncategorized
is never stored as an assignment. Importing while it is selected uses the deck
name as the target category; its Remove action explains how to classify those
cards instead. Its menu entry stays visible even when empty, including with
Hide empty filters enabled or a native search that returns no cards.

The [catalog](Categories.json) covers a complete data-assisted pass over the pinned
Constructed card pool: 7,538 printings, or 6,277 canonical IDs. It assigns 4,225 IDs
to one or more roles; 2,052 have no match under the current role definitions.
These include ordinary bodies, healing and buffs. Rules are supplemented by 490
explicit editorial corrections; this is not a claim that every card received an
individual manual expert review. [Coverage and definitions](Data/Categories.md)
and the [per-card audit](Data/CategoryAudit.tsv) retain every considered ID, its
roles and the evidence/method, including cards with no role.

On first collection entry, the embedded catalog is
copied to **Mods/BetterCollectionManager/categories.json**, next to
`quality_settings.json`. Edit that file and leave/re-enter the collection to
reload it. The unchanged original 110-entry starter selection upgrades to the new
catalog with a `categories.json.starter-v1.bak` backup. Edited/custom catalogs keep
their contents and remain full replacements for the bundled list. Invalid JSON or invalid entries
produce a log warning and use the bundled selection for that session; the edited
file is preserved for correction. An empty `entries` array deliberately matches
no curated cards. The category reader uses no optional JSON/serialization runtime
library, and category initialization errors do not stop the other filter controls
from being built. Card IDs and alternative printings were checked
against [HearthstoneJSON build 255790](https://api.hearthstonejson.com/v1/255790/enUS/cards.collectible.json),
which is [extracted from the game data](https://hearthsim.info/docs/cards/).

To add cards using a deck, select the target category, add at least one card to a
deck, and keep that deck open for editing. With no category selected, a popup asks
for the target category name. A name that matches no existing category creates a
new category; Cancel leaves membership unchanged.
In **Categories**, choose **Add deck cards to category**. The mod reads the edited deck's
live card counts directly; no export, clipboard or complete
30-card deck is needed. It reads the complete collection catalog rather than
the currently visible filter results. Selecting a sideboard still includes both
main-deck and sideboard cards.
Existing names merge case-insensitively, with repeated spaces ignored. New names
create custom category rows. Imports add membership; they never replace or remove
existing cards, and importing the same cards again creates no duplicates. Copy
counts and deck legality are irrelevant; starting heroes are excluded and
sideboard cards are included. The button reports the number of newly added cards.

To remove membership, select the target category, keep a deck containing the
cards to remove open for editing, and choose **Remove deck cards from category**.
Only the intersection of that deck and the selected category is removed. This
also works for bundled category members; overlapping categories and the deck
itself remain unchanged. The target is the selected category, independent of the
deck's name. With no selection, a popup asks for the category name. Multiple selected
categories require selecting one before editing membership. Repeating a removal
never adds cards back. Use **Add deck cards to category** to restore removed cards.
Empty categories disappear from filters and combo destinations, and their obsolete
filter selections are cleared. Combos remains available as a destination.

**Turn Deck Into Combo** captures the edited deck as one entry in the selected category
(or **Combos** with no single category selected). The editor asks for the combo name.
**Edit** and **Turn Deck Into Combo** use the native Product Information parchment,
header and shared Save/Cancel footer. Its shop controller is removed
while the clone is inactive, before any shop callbacks can run.
Popup action hit boxes, including Save and Cancel, are fitted to the visible
button meshes after layout and draw ordering. Inherited child colliders cannot
extend their clickable area. This changes only input geometry; button artwork,
position and scale retain their existing layout.

The editor has a name, optional **Mana cost**, independent **Card text** and **Description (inspect)** fields,
a native frame dropdown (**Spell**, **Minion**, **Weapon**, **Hero**, **Location**),
and native controls for the selected card type:

| Frame | Options |
| --- | --- |
| Minion | Minion Type, Attack, Health |
| Spell | Spell School |
| Weapon | Attack, Durability |
| Location | Durability |
| Hero | Armor |

Mana cost accepts 0–999. Leave it blank (**Sum**) to use the sum of all captured
copies; zero is an explicit override. The value is saved and included in clipboard
sharing. Existing combos without an override continue to use the sum.

**Dragon Ornament** is available for every frame. Hidden options retain their draft
values when changing frames, but only applicable tags and stat elements reach the
card. Values accept 0–999. Minion Type and Spell School include **None**; minion types
also include **All**. These choices persist with the combo; older files default
new options to **None**. The open dropdown, including rows created after opening,
renders and receives input in front of the editor's artwork, fields and captions.
The dragon can be borrowed from the native minion template when another frame
lacks it. Text fields reuse the actual
`InputFieldUI` background sprite from the native overlay. Atlas UVs and
nine-slice borders are preserved, with fixed pixel corners at every field size.
Light parchment tint and a thin inset border keep dark input text legible. Field
fonts are measured at their final screen position instead of borrowing another
font's scale, and the fields remain inset from the panel edges.
They contain no baked dropdown-arrow region or stretched button decoration.
The two multiline fields each allow 2000 characters and edit directly in the form.
Frame controls remain visible while typing. Full input survives focus changes and
resizing; inactive fields show a readable excerpt. Clicking outside the editor
discards unsaved changes and returns to the combo inspect; its dismissal sound
plays when the editor closes. Dropdowns, checkboxes and fields share aligned column edges
inside a centered form. Enter
inserts a newline in either multiline field and completes the name. Editors open
with a short scale punch and close with a short shrink animation. Layout and hit
areas are fitted at full size before applying the visual animation; text focus
finishes the opening motion. Closing artwork has no active hitboxes, and a new
view cancels the previous exit before reusing its controls. All text fields
disable the hidden menu button's punch rotation, depth offset and flip animations.
Action buttons retain their native feedback.

The name becomes the card title; Card text appears on the combo card, while
Description appears on inspect's native flavor parchment. Save persists these
settings without changing component identities, editions or quantities; Cancel
discards all edits. Captured components do not automatically become individual
members of the category.

Inspect descriptions use the original flavor-text component and its native font
metrics on a larger pane. Edit/Delete retain complete native dialog buttons and
their regular click sound; switching to Edit does not play a card transition.
Filter captions explicitly hide their glyph renderer when a row is culled, even
if native text geometry has become detached from the row, and reattach it on reveal.

Combo class placement prefers the common classes of all components; mixed-class
combos remain discoverable while browsing. While editing a
deck, collection and member-search results retain only native legal classes,
including the deck's Tourist classes. Copy counts and ownership do not change class
visibility. Refreshing an unchanged page does not animate another page turn.
Filter actions compare the visible cards, their order, quality and class tab,
so changed contents turn the page even when its number stays the same. Empty
results participate in that comparison too. Expansion changes compare against
the snapshot taken before the native filter callback; background refreshes and
changes confined to other pages stay immediate.

Package Back uses the same complete native arrow button as Inspect, including
its idle face and native hover lighting. The resting face determines placement,
the root collider and the screen-space hit area. Hover lighting never changes
those dimensions, so a pointer at the edge stays over the same button.
The package Back arrow is displayed 25% larger, with its hit area scaled to match.

Combo cards occupy ordinary collection page slots **only while their category
is selected**. They are absent from the general collection. Their mana cost uses
the saved override, or the sum of all captured copies when blank. Collection
ordering uses that same value. Their counter shows complete combos in total
ownership: sum the owned qualities of each component, divide by its required
quantity, round down, and use the smallest result. For **2× A + 1× B**, owning
5 A and 3 B gives **2 combos**. Cards already in a deck do not reduce this count;
trial copies do not increase permanent ownership. Zero remains visible when a
component is missing, while partial deck additions remain possible. The counter
uses the native wide **9+** display for larger totals and restores the original
counter when the collection slot is reused. They sort by mana and then alphabetically by
their name among cards of the same cost within their class. The mana digit keeps its native size
and visibility even on card prefabs without a text-fitting region. They have no
rarity gem. They use a stable neutral native frame with the selected
ornament, text and values. The free/no-gem frame omits the rarity surround and uses
the native infill where available. Artwork cycles through the components every three
seconds, retaining the current hover glow after the native model is shown again.
Template and artwork use the highest quality actually owned for
**every** component (Signature, Diamond, Golden, then Normal). Individual display
preferences do not lower this shared template. A Signature on one component does not override a common Golden
template. With no shared owned quality the template falls back to Normal. The
chosen quality stays constant throughout artwork rotation. The native actor owns
loading, premium shaders and animation. Matching portrait templates retain their
native UVs. Different templates map the full source portrait into the selected
frame, adapting its aspect ratio and clamping uncovered corners to the native
boundary instead of zooming into an inscribed rectangle. Only the private
portrait mesh changes; imported assets and frame geometry remain untouched.
Component class, rarity, race and watermark cannot change the chosen frame.
Combo rune banners show the maximum Blood, Frost and Unholy requirements among
their components. Copies do not multiply rune costs; changing artwork does not
change the banner. Cards without rune costs contribute zero.
Keywords in combo **Card text** are automatically bolded using the game's localized
keyword names, regardless of the active collection filters. Matching ignores case
and respects word boundaries, existing bold spans and native rich-text tags.
Saved text remains unchanged and editable without added formatting tags.
Clicking to inspect animates the component previews from the clicked combo's
screen position and size to their large preview cells. Late layout passes retain
the zoom; closing or changing pages cancels it cleanly.
Large component previews also retain the preferred owned quality. Combo captions preserve native rich-text rendering, fit the existing text
regions, and are reapplied after late native actor-text updates. Click additions
give each inserted component its own native flight animation from the combo tile,
using the actual component identity and selected quality. Temporary animation sources
are hidden and released after each add, including successful adds, so original card
frames and captions cannot remain stacked over the combo. Each left-click
or drag onto the deck while editing adds the captured component quantities, honoring the saved quality preference and
native ownership, format, class, copy and deck-size limits. A repeated click can
add another bundle up to those limits; unavailable or prohibited copies are skipped.
When no component can be added, the native lock banner shows the applicable
deck-size, copy-limit, ownership or legality reason. Reaching that limit through
an addition plays the native lock sound once; ordinary refreshes remain silent.
It clears when additions
become possible again. Dragging uses the native held-card visual; a successful
drop adds the bundle after native cleanup, while dropping outside the deck,
canceling or switching decks adds no cards.
Combo dragging follows **Add to deck by dragging**: **Same as click** uses the
click policy, **Dragged half** prefers the grabbed model's displayed quality,
and **Choose after dropping** opens one quality popup for the complete combo or
package. The selected quality is preferred for every captured copy, with owned,
legal fallback for components unavailable in that quality. No components are added
before confirmation; Cancel discards the whole pending bundle. A bundle with only
one available quality adds automatically. Every insertion rechecks native limits
and the edited deck.
The quality picker uses its own overlay anchor, facing the UI camera at a visible
depth, independently of the Escape menu's hidden pose or scale. The complete menu
fits within the viewport while preserving native button text positions and the
opening animation on the child rig.
Its native modal blocker follows the fitted menu behind all visible button hit
areas. Button colliders stay enabled on the same input layer; decorative menu
colliders cannot cover them. The blocker continues to catch outside clicks.
Right-click (or left-click while browsing) opens large component-card previews,
with Previous/Next when necessary to keep them readable, disjoint and within the
screen. Inspect draws no enclosing popup body or header frame. The description
uses the native card flavor-text parchment, including the current Standard/Wild
framing and a category band; rarity gems, artist information and trial-card controls
are omitted. On wide windows the description sits beside the component cards;
on narrow windows it sits below them. The flavor pane remains visible on every
page, including for empty descriptions. The action row centers beneath the middle
item or middle pair, counting the flavor pane alongside the component cards.
Cards, the title and native actions sit over the same blur/vignette used by ordinary
card inspection. Opening uses `CraftingManager.FinishBigCardMove`'s
`Card_Transition_Out` sound; closing uses `CancelCraftMode`'s `Card_Transition_In`.
Pagination and layout changes do not replay either sound. The editor also uses a
native screen-effect handle. Closing, disabling or destroying the popup releases
only its own effect, preserving effects owned by other game views. Clicking anywhere
except a component card or an action button closes inspect. Background dismissal
keeps the input blocker through release dispatch to avoid clicking through to the
collection; the release that opened inspect is ignored. There is no Close button.
Inspect has no duplicate name above the cards. Edit and Delete combo are larger,
aligned actions. Edit uses the plain native menu button without a checkmark;
Delete uses the game's red Concede material. Multipage inspect uses the native
hero-picker triangle buttons, beside the card row at its vertical center. The
installed dialog mapping supplies their asset references; no hero popup is shown.
Arrow graphics sharing an object with native UberText stay enabled; their mesh
plane determines facing independently of an empty button caption.
Unavailable directions are hidden. Single-page
combos omit the arrows. The view refits late-loaded
card/text geometry and window-size changes. **Edit** opens all saved presentation controls without requiring an edited deck. **Edit cards** opens the member editor:
clear the search to see current cards, or search the collection by name or exact card
ID to add another card. The shop's native **− / quantity / +** control changes
quantities; reducing a quantity to zero removes the card. Each unavailable action
disappears together with its hit area at the minimum or maximum quantity. Legendary cards are limited to one copy, other rarities to
two, including alternate editions of the same card. Search results use a native
scrollbar with a draggable grip and mouse-wheel scrolling. One search label identifies
the search field. Selecting a row highlights its name and shows a larger native card
preview beside the compact quantity controls. The separate card dialog has **Save** and **Cancel**:
Save stages the selection in the parent editor; Cancel or an outside click returns
without those selection changes. There is no Back button in the card dialog.
**Save combo** commits both presentation and member changes, preserving identity
and category, then returns to inspect; Cancel
returns without saving. **Delete combo** removes the combo;
deleting the last category entry removes an automatically created empty category.
Explicitly added folders remain available when empty. Combo tiles open their
own inspect flow and bundle insertion; native single-card crafting remains disabled.

**Packages → Show Edit Controls → Turn Deck Into Package** captures the edited deck's exact editions and quantities.
It initially selects the active package group, or **Custom** when no single group is selected.
The **Packages** entry opens a separate browser with **All Packages**, **Parasitic**,
**Factions**, **Custom** and user-created folders. Card searches and custom card
filters stay saved and do not restrict this browser. On entry, Packages inherits
the Cards Standard/Wild format and starts without an expansion restriction.
Packages retain the native class groups, with alphabetical names inside each class
and the same class-colored banners as native deck recipes. Custom packages derive
their class from their members, ignoring neutral cards and intersecting dual classes.
The pinned **Format: Standard / Wild** button switches formats directly in the overview;
the expansion submenu uses its existing Standard and Wild choices. Both paths
keep the first previously visible package that still qualifies. If none survives,
they follow the nearest successor or predecessor in the old order, retaining the
class group when possible. Switching from inside a package uses the saved folder
overview position. An empty result falls back to the first page.
The edited deck's format, classes and native
legality rules remain mandatory. Every member must fit; one incompatible member
excludes the whole package. A format-legal native counterpart can supply a reprinted
member without changing the saved package definition.

**Expansions** has a separate package selection and logic controls. Every package
member must match that selection; OR combines the selected expansions so mixed-set
packages can qualify. Package groups have no logic controls. Changing the package
query returns to its overview; an opened package always shows its complete contents.
Back inside a package returns to the overview. Back from its expansion submenu also
returns to the overview. Back from the overview restores Cards with the prior card
filters, expansion terms/operators and page anchor. Returning to Cards uses the
normal page-turn animation even when its page number matches the package page.
Package queries never invoke
the native card expansion callbacks or overwrite remembered card modes.
Its reviewed catalog contains 44 class-scoped parasitic packages and 20 faction packages;
see [the package manifest](Data/Packages.md). Package membership does not show
individual component cards in Categories. **Categories → Combos** selects saved
combos across their assigned card-role categories. Uncategorized, Packages and
Uncategorized and Packages are excluded from combo destinations. **Combos** is a
valid destination, and every saved role combo also appears in the Combos filter.
Choose artwork from its native dropdown, including a scrollbar for long lists.
Unchanged choices do not reset the selector during background token discovery.
Delete package uses a compact native menu button below the selector, aligned to
the same left edge, with no oversized cancel icon. The fields sit nearer the title.
The enlarged preview, including its count badge, stays above the footer and includes the package
name with the native white title fill and one card-count caption. The selected card or token is shown without a separate search field, card ID
or position counter.
Candidates are limited to cards captured in this
package and their noncollectible tokens, including token transformations reached
through the native collection-related-card database (for example Wildseeds). Referenced collectible
cards do not expand the selection.
Token artwork does not become part of the package or the player's inventory. The
package's token index fills incrementally while the editor is open. The editor also
uses the native deck-recipe frame for the package preview, independently of the
carrier card's animation and visibility. Delete package aligns with the artwork
actions above the Save/Cancel footer. The larger Back arrow returns from an open
package; starting or ending deck editing keeps a compatible package open on its
current page without an automatic book flip and updates its Add All/Edit Package
action. Changing the package query closes the folder during that result rebuild.
Moving or deleting the open package also removes its Back arrow when it no longer
belongs to the current group. A new deck that cannot use the package
closes the folder automatically. Ownership, copy limits and deck capacity do not
close a compatible folder. Native deck transitions that already refreshed the
folder do not request another refresh. Custom refreshes assemble the already
filtered results directly instead of filtering them again after resolving the page.

The package editor also offers **Edit cards** with the same member search and quantity
controls. Changes affect only the draft until **Save package**; the current deck
remains unchanged. Artwork choices follow the draft's cards and their tokens. If
the selected artwork loses its last owning card, choose from the remaining family
choices; an available remaining card is selected automatically. Combos and packages
must contain at least one card before saving. New entries can also adjust their
captured deck snapshot before saving.

Left-click opens a dedicated collection page with **Add All** in the first slot,
followed by the package's cards. A native left arrow button for **Back** lives directly
in the overlay and stays available across pages, independently of Add All's model
or the editor's visibility. It retains the native arrow template's left-facing orientation
and stays inside the UI camera clipping range. Editing is available through
right-click and the Categories menu. Back returns to the previous collection page. Dragging the closed folder onto the deck inserts its captured quantities
without opening it. Both bulk actions use the same per-copy ownership, preferred
quality, class/format, copy-limit and deck-limit rules as combos; legal copies can
be added even when other copies cannot. Right-click opens the package editor
directly, including Delete; Cancel returns to the collection. **Edit Package** in the
Categories menu also lists saved packages, so editing does not depend on tile rendering. Version 6 records retain package kind and independent artwork;
versions 1–5 remain readable.

Package frames clone `DeckTemplatePicker.m_pickerButtonTpl` from the installed
client's recipe-page asset, using the page manager's prefab reference. No visit to
the native recipe page is needed. Portraits use the recipe's art material and the
selected card/token's raw portrait texture. Each tile owns its materials; native
pack-flight controllers and click handlers are removed from the graphic clone.
The package body fits the full native collection-card input slot with an 8%
enlargement in the collection. Text, glow,
shadows and the hanging card-count badge are excluded from its size measurement.
Fitting follows later layout changes; editor previews use their explicit preview
area. Package left/right clicks sound on mouse down, before navigation or editor setup.
Picker buttons and the Back arrow likewise sound on PRESS; their release actions
do not add another click. Folder hover
shows the package highlight with the carrier's keyword help suppressed. Collection
hover feedback follows the slot's native mouse interaction, independently of the
carrier's ownership or newly acquired actor state. Leaving the slot clears the
highlight even if the carrier retains an old hover state. The native
selection-glow container and its nested geometry are prepared before the first
hover, including when the source prefab starts inactive. Each tile restores its
private glow renderers after native renderer resets, without warming the shared
prefab or requiring a visit to the package editor. Save/Cancel
keep the native footer slots; Delete sits inside the editor content.
Package limit messages reuse the native lock plate, fitted above the recipe title
and drawn in front of the folder without a card-shaped backdrop. Invalid packages
do not show or clone the redundant red banned ribbon, including ribbons anchored
outside the native lock hierarchy. Package limit
messages omit per-card copy numbers. Opening a package and clicking Back each play
one native Small Click. Native class-tab, page and card sounds remain available.
Add All starts the native deck-row flights after all additions and sorting finish,
so each flight uses its matching component row. Live rendering still needs game
verification.

Version 9 combo files add optional mana cost to the presentation settings and independent card text,
alongside exact card editions (including `CORE_` and `VAN_`) and
their separate quantities. Earlier combo versions remain supported with summed mana;
in Version 1 and 2 files the former description initializes card
text as well. Legacy files resolve their
discarded edition from the native collection results and active format. Mana,
inspect, artwork and deck insertion use the same selected edition.

Categories and Packages support sharing through the clipboard. Open **Show Edit
Controls** in the respective filter menu:

- **Copy categories / Copy packages** opens a popup with **All categories/packages**
  and the currently selected folder. Select exactly one folder before choosing the latter.
  The export includes its cards, combos/packages, quantities, editions, artwork
  and appearance settings. Empty folders are preserved. Uncategorized is calculated
  locally and is not exported.
- **Paste categories / Paste packages** merges automatically when the clipboard
  contains new or identical content, including a single combo/package. A conflict
  popup appears only if **Merge** and **Replace** would
  produce different contents (for example extra local cards/entries or different
  styling). Cancel leaves the local catalog unchanged. Merge
  unions standalone membership and combines same-name combos/packages in the same
  folder, taking the higher quantity per card while retaining local metadata and
  appearance. Overwrite replaces the contents of folders included in the export;
  an individual entry export replaces only its same-name entry in that folder.
  Other folders remain intact. Names match without case or repeated whitespace.
- **Copy combo / Copy package** in an inspect view or editor copies just
  that saved entry. Save pending editor changes before exporting them.
  After a single-entry import, the collection selects that entry's category/package
  folder and turns to its page. This also applies to an unchanged existing entry.
  Search/mana/set restrictions are reset so they cannot hide the destination;
  package browsing uses Wild and all expansions. The filter tray closes to show
  the imported entry.

Exports use versioned `BCM-COLLECTION 1` text and can be shared or saved as text.
Import from the matching Categories or Packages menu. The clipboard is captured
when the paste action starts; Cancel leaves the catalog unchanged. Invalid or
oversized exports are rejected before saving any edits. Each successful import
is committed as one numbered `.bundle` file in `CategoryDecks`, including any
explicit overwrite removals, and survives collection reloads and restarts.
Managed checks cover merge/overwrite scope, repeat imports, metadata, corrupt
bundles, conditional conflict choices, import navigation, popup cancellation and
clipboard capture. Clipboard sharing has been confirmed in-game. The updated
editor layout, hover cleanup and import navigation still need an in-game check.

The combo editor places optional mana beside the name; package names keep the
full width. Both editors use a common action row
for **Edit cards**, **Copy combo/package** and **New category/package folder**.
Folder dropdowns have the same space as their peer dropdowns; the package editor
uses a more compact panel. Opening a popup clears hover and press feedback from
the filter tray while retaining selection highlights. **Add deck cards to category**
adds the edited deck's membership; it is separate from clipboard structure imports.

Imported card-ID snapshots are stored as `.cards` files in
**Mods/BetterCollectionManager/CategoryDecks/**. They are additive to
`categories.json`, which the importer never rewrites. Clicking Import again reads
later deck edits and adds only new members; it never edits or completes the deck.
Previous `.txt` deck exports remain supported. You can also put a full named deck
export in that folder yourself. Leave/re-enter the collection to reload files;
remove a snapshot/export to remove its additions on the next reload.
New edits use numbered `edit-…` files: `.cards`/`.txt` add membership and `.remove`
records explicit removals. They are replayed in sequence after older exports, so
removals and later re-additions persist across collection entries/restarts.
`.combo` stores combo metadata/components, `.editcombo` updates an existing combo's
metadata while keeping its identity, and `.uncombo` records a combo deletion.
The base JSON and original imports are preserved; source/package definitions in
`CategoryCatalog.Entries` remain available while `Matches` uses the edited membership.
Invalid exports or unresolved card IDs are logged and skipped as a whole while
valid imports and the rest of the menu remain usable. The reader implements the
[HearthSim deckstring format](https://hearthsim.info/docs/deckstrings/) and its
[reference sideboard extension](https://github.com/HearthSim/python-hearthstone/blob/master/hearthstone/deckstrings.py).

Each object in `entries` has a stable `id`, a display `name`, `categories` and
`cards`. Category IDs are `aoe`, `single-target-removal`, `smorc`, `value`,
`win-conditions`, `draw`, `unique-effects`, `packages` and `techw`. Each card requires its
case-sensitive `id` and a `count` of 1 or 2. Optional `aliases` lists alternative
printings of that card, rather than additional cards. Core/Classic prefixes are
handled automatically. For example, this can be appended to `entries`:

```json
{
  "id": "equality-consecration",
  "name": "Equality + Consecration",
  "categories": ["aoe"],
  "cards": [
    { "id": "EX1_619", "count": 1 },
    { "id": "CS2_093", "count": 1 }
  ]
}
```

Thirty named packages include Wildseeds, Rituals, Jade Golems, Relics, Soul Fragments,
Librams and the StarCraft factions. Filtering currently shows
their individual component cards, including when only some are in the native
result pool. Built-in packages are browsed in the independent Packages view; use **Turn Deck Into Package** or **Turn Deck Into Combo** to
create a custom category tile with the bulk deck action described above.

**Page-turn diagnostics:** press **F8** in the collection to capture native page,
filter and sound calls in the normal MelonLoader log (`[Page trace]`). Turn one
page with the native arrows, then apply a custom filter. F8 stops capture; it
also stops after 20 seconds, 200 events or leaving the collection. The trace
includes available method signatures, arguments, sound callers and whether the
filter sound guard permits playback, plus enum names and values for transition
parameters. Per-frame status probes such as `IsPlaying` and `CanUserTurnPages`
are excluded so idle frames do not exhaust the event budget.
It observes calls without invoking page
operations. If the stripped input API exposes no compatible F8 binding, capture
starts on collection entry instead. Native page/card sounds are permitted during
the visible result refresh while duplicate filter-row click sounds stay blocked.
Filter clicks use the traced `RefreshCurrentPageContents(PageTransitionType)`
overload with a single-page transition, without navigating away from the resolved
card anchor. Startup/background refreshes keep the immediate refresh. Native
animation and sound behavior still needs in-game validation after this change.

Each submenu pins **Back** and the available **Show Logic Controls** /
**Show Edit Controls** switches above the list. Only one group is expanded at a
time. Logic contains Mode/Next, NOT and Clear All. Categories Edit contains
Add Folder, deck import/removal and Turn Deck Into Combo. Packages Edit contains **Turn Deck Into Package**
and **Add Folder**. There is no separate Edit Package list; right-click a package to edit it.

Clear All removes the submenu selections and returns its mode to Single. The
top-level Clear All clears every group, expansion selection and suspended choice,
and returns all modes to Single while keeping the menu open. When a mode differs
from Single, Clear All stays visible even with Logic collapsed or hidden and
**Show Clear All** disabled. Otherwise its visibility follows those settings.

**Filter mode control** in Filter menu has three settings:

- **Hidden** hides the Mode and NOT buttons and uses category-wide filtering.
- **Whole category** applies Single, AND, OR, NOT, AND NOT or OR NOT
  to all selected entries in the category. Single immediately filters by the latest
  entry and shows only its selection glow, while keeping the other choices suspended.
  NOT is the negated Single view. AND NOT excludes every selected value; OR NOT
  accepts cards lacking at least one selected value. Cycling out of a unary view
  restores suspended choices automatically. Changing or
  removing a filter in Single commits the exclusive selection; Clear All clears
  the suspended choices as well. Merely re-selecting the active expansion is not an edit.
- **Next filter** records the chosen operator for each new selection. AND joins
  the current group; OR starts another group. AND NOT and OR NOT negate the new
  term: `A AND NOT B OR NOT C` means `(A AND NOT B) OR (NOT C)`.
  AND binds more tightly than OR, including their negated variants.
  Single and unary NOT are available with zero or one selected filter and are
  skipped once two or more terms are connected.
  It becomes available again after removing terms or clearing the category.
  This restriction applies only to Next filter; whole-category unary modes stay available.
  For example, select Battlecry,
  AND Immune, OR Battlecry, AND Stealth to match
  `(Battlecry AND Immune) OR (Battlecry AND Stealth)`.

The separate **NOT: Off / NOT: On** button toggles negation and glows when enabled.
Mode/Next cycles Single, AND and OR while keeping the NOT setting. Together they
produce NOT, AND NOT and OR NOT. In Whole category this updates the selected
entries immediately; in Next filter it affects the next selection and leaves
existing terms intact. With NOT enabled, AND is also available in categories
whose positive values are mutually exclusive; switching NOT off there uses OR.
Back, Mode/Next, NOT and Clear All stay fixed above the scrolling list and wrap
to match the configured column count.

Excluded rows show **NOT ·** before their name and use a red selection glow.
Positive terms in the current expression group and standalone OR branches keep the native blue
selection glow. A filter glows yellow only when all its occurrences belong to
earlier AND groups. Blue takes priority when a filter occurs in several groups.
After choosing AND, an earlier filter outside the current group turns yellow so
its next click adds it to the current group. Choosing OR similarly marks filters
already joined with AND for reuse in a new branch. Unrelated standalone OR filters
remain blue. The next actual filter edit consumes that operator choice.
Clicking a selected entry again removes it. Choosing an operator first lets
you reuse an already selected entry in another group. Clicking a selected entry
removes its latest occurrence and its preceding operator; removing the last
branch returns to the preceding group. Changing Next never rewrites the expression
already built. Keyword family remapping and pruning preserve these operators.

Keywords, Minion Type and the curated Categories list support positive AND.
Expansions, Card Type, Spell School and Rarity omit positive AND because their
values are mutually exclusive, but all filter categories offer NOT, AND NOT and
OR NOT. Previously saved positive AND modes in exclusive categories become OR next session.
Categories always combine with AND, and an empty category adds no restriction.

Reopening an unchanged menu reuses its rows and pinned buttons. With empty-option
hiding enabled, the keyword menu reconciles only its visible rows: hidden buttons
and group headings remain in a local pool, pinned controls and the frame stay intact,
and unchanged sliced meshes are reused. When availability changes while scrolled,
the list is arranged at its unscrolled origin before restoring the scrollbar
fraction, so NOT toggles cannot add a second content offset.
Other selection changes update labels and
glows in place when hiding is disabled.

Availability uses an inverted index of immutable card bitsets. AND/OR/NOT combines
64 card positions per word instead of repeatedly inspecting every card for each
option. The same index supplies result membership during filter application;
cards outside the captured native pool keep the scalar predicate fallback.
Explicit custom-filter refreshes reuse an unchanged native input pool. Native
searches, collection/metadata changes and catalog invalidation rebuild the index.
Capture buffers are reused, and class buckets stream into them without a temporary
combined card list. Result compaction preserves the order of surviving cards.
Refreshes or menu updates taking at least 25 ms log their stage timings for diagnosis.

**Settings → Mods → Filter menu → Hide filters without results** is off by default.
It hides options that cannot contribute a match under the other active filters,
including native search, class, mana and ownership restrictions. Selected entries
stay visible for removal. A new OR branch must have matches of its own; existing
matching branches cannot keep an otherwise empty option visible. Negative options
use the complement within that captured pool, so NOT never adds cards excluded by
the native search, class, mana or ownership restrictions.
Automatic layout is calculated from the complete available list: hiding options
compacts the visible entries without changing rows, columns or frame dimensions,
even when every option is hidden. The fixed header remains available.

Every card filter starts in Single, including Keywords and Minion Type.
Package groups use a single folder selection; only their independent expansion
submenu offers logic controls.
**Settings → Mods → Filter menu → Remember filter modes** is off by default.
Enabling it saves the current modes and subsequent mode changes across collection
sessions and restarts. Disabling it keeps this session's modes and restores the
defaults next session. Selected filter values are always cleared when leaving the
collection, independently of this checkbox.

The custom filter lists use the row/column settings from Mods. Entries fill
left to right, then continue on the next row; section headings occupy a full row.
Column backgrounds share the heading's inner width, with touching horizontal
and vertical edges. Their native shading spans the complete menu width rather
than restarting on each button. Vertex gradients are sampled per column;
baked texture gradients remap only the face interior, preserving the atlas
coordinates of the gold borders and corners. Text keeps its native size, with
a uniform native midpoint tint so wrapped labels do not darken on either line.
Additional entries remain available through the native vertical scroller.
Filter rows and Back keep SetFilterItem's native hover/pressed/selected artwork.
Dragging a held press between pinned controls keeps the current button's hover
available. A pinned control requires pressing and releasing the same button, including when
the pointer moves away and returns. Releasing over another button cancels the click;
releasing an actual scrollbar drag does not activate a button underneath it.
Resizing keeps these owned effects enabled and applies the same row-wide gradient
to their interiors, preserving border/corner atlas coordinates. Only unrelated
broad menu shadows are suppressed. Visibility updates do not erase
a held press, and selection changes update the latched pressed shadow separately.
No synthetic transform movement is added. Optional UIBHighlight listeners are
restored after replacing selection callbacks; headings remain noninteractive.
A press anywhere in the configured scrollbar area starts the native
UIBScrollable/ScrollBarThumb drag and updates the thumb immediately. Continuing
to hold drags from there, including outside the track. The native scroller owns
normalized scroll values (0..1), content range, thumb travel and input locks;
input does not infer the rail from decorative mesh sizes or names. Releasing
outside keeps the menu open, and closing stops the native drag.
The frame and wooden background use private nine-sliced meshes, preserving
border thickness and texture corners. The scroll track extends while the thumb
and other small parts keep their native size. Broad inner menu shadows stay
suppressed. The left edge follows the native tray seam, and the opening button
stays visible below the menu. Opening and closing slides move the complete
resized frame beyond the screen edge. Rapid toggles reverse the one active slide
from its current position and retain its existing buttons. Hidden positions are
calculated from the fully shown anchor, so interrupting an opening cannot reverse
the exit direction. Superseded animations cannot hide a newer panel, and repeated
close requests do not restart the animation or deactivate its buttons early.

Each submenu has a **Back** button at normal row height pinned inside the
tray's top edge, above the scrollable list. Back and section headings extend
their textures and hit areas across the full row, preserving border strips and
native text proportions, including render-to-texture captions. Captions center
their actual visible glyphs, including separate render-to-texture output surfaces.
Alignment refreshes after opening and when scrolling reveals rows, so delayed
font geometry also centers correctly. Back retains the
full texture width and insets only its hit area; the horizontal inset is smaller
than before, retaining 95.2% of the viewport width. One thin button-local collider
replaces inherited hit areas; pointer detection intersects that local plane
instead of projecting an enclosing world box. Offscreen font meshes do not
contribute to layout or pointer capture. Hovering Back only blocks
pointer hits directly behind it, preserving other menu controls and the scroll
viewport. A separate clipping mask keeps scrolling rows below Back. Only its top edge is
trimmed: the original native bottom edge and visual width are preserved, even
when the scroll hit collider is smaller than the visible tray. The native
source Expansions tray remains hidden while its entries are displayed in the
shared tray. Its scroll hit area follows the visible pixel mask, including the
space reserved for Back, and its scroll origin stays at the arranged list start. The list start also accounts
for the first heading's visible bounds so its artwork clears Back. Back stays
visible and clickable even after scrolling to the bottom. Unrelated oversized shadow planes remain hidden on Back; its native
hover/press feedback is retained. Its mouse capture takes priority over underlying filter
rows, the native expansion button, and outside-click targets; returning happens
after the release frame so the same click cannot select an item behind it. Returning to the
menu keeps current filter selections; active keyword/type/race/school/rarity and curated categories
show `*`. Selecting an expansion also updates the opening button's native logo
and texture offset. Multiple selected expansions show only a centered `*`, replacing the
Standard/Wild logo. The marker is a mathematical, six-rayed asterisk made from
three antialiased strokes. Its ink comes from the native logo's pixels, and it
uses the same native quad, material, tint, texture offset, scale and camera.
The private texture clears the selected logo region before drawing the star;
no text object or installed font is needed. Atlas-backed
icons retain their mesh UV region and material texture scale/offset. Copies are
cached for the collection session; selecting a single expansion or clearing the
filter immediately restores the original native texture. Original game textures
are never modified, and private copies are released when the menu is detached.
The GPU copy selects an available native `Blit` signature at runtime, including
stripped builds without the two-argument wrapper. It uses an explicit sRGB render
target without requiring `GL.sRGBWrite`; material-only bindings receive a private
copy material. The chosen native signature is included in the diagnostic log.
Standard-only selections retain the light collection page theme even while
filter availability uses a cached Wild pool; deck-specific themes remain native.
If the marker is missing, the normal MelonLoader log includes `[Expansion star]`
selection and `star-only-v4` copy/assignment diagnostics. Texture creation failures
are reported with their exception; the original native icon stays in place.
Reopening restores the last submenu and its scroll position. Each
visited submenu retains its own position, including the category list reached
with Back. This state stays in memory and resets when leaving the collection;
it is not written to preferences. Click outside or click the
expansion-selection button again to close the menu. Outside-close requires a
fresh press and release outside the frame. A drag that starts in the menu keeps
capture through release, even beyond the frame or over the opening button; this
applies to both the custom hide area and native close callbacks.
The open menu blocks background collection hits across its UI cameras while
preserving its own controls, shared parent input controllers and the expansion-selection toggle.
Regular column buttons retain the native collider objects, input depth, thickness
and enabled states; only their width and row height follow the resized artwork. Closing clears
pending Back navigation and disables menu entries immediately; the outside-click
blocker and captured background hits remain blocked through the closing slide.
Only after the panel is hidden are those colliders restored and the blocker
disabled. Immediate hides perform the complete cleanup at once. Reopening restores
saved input states before rebuilding entries. Outside-hit checks use the visible
frame so clipped rows do not intercept clicks beside the menu. Interrupted
animations use the same cleanup so a hidden menu cannot retain collection input.
Cleanup also runs if the animation is cancelled, native cleanup throws, or the
game deactivates the tray hierarchy directly.

**Keywords** opens a single list with **Evergreen** and **Expansion-Keywords**
as noninteractive section headings. Keywords are alphabetized within
each section and appear only once, combining direct keyword and reference matches.
Sections with no matches in the selected set/format are hidden. Back returns
directly to the main filter menu. Closing and reopening restores the list and its
scroll position. Selections can span both sections.
Scroll restoration runs again after opening and native scroller initialization;
interrupting an opening animation preserves the requested position.

The classification is curated in `KeywordGroups.cs` using game tag IDs, independent
of language, set selection, and reprint counts. Evergreen covers regularly reused
mechanics, including class mechanics and later evergreen additions such as Reborn
and Tradeable, as well as Bonus Effect. Adapt and Magnetic stay Expansion-Keywords despite occasional
returns. Grimy Goons, Jade Lotus, Kabal, Zerg, Terran, Protoss, Alliance and Horde
are package folders under the separate **Packages → Factions** filter.
Unknown mechanics default to Expansion-Keywords until classified.
[Blizzard's evergreen explanation](https://news.blizzard.com/en-us/article/23921495/year-of-the-wolf-core-set-update)
distinguishes ongoing use across expansions from a returning mechanic.

Keyword rows combine all tags with the same localized keyword name. **Quest**
combines Quest and Questline under Evergreen, **Fabled** combines Fabled and
Fabled+, **Tourist** combines the generic keyword and every class-specific
Tourist, and **Casts When Drawn** also includes Summoned When Drawn under Evergreen.
The family uses the base keyword's localized
name, even when only a variant has matches. Selecting
one row matches any of those tag variants, so deduplication does not discard
cards. Trailing parameter placeholders are removed from labels: Colossal +X →
Colossal, Manathirst (X) → Manathirst, and Overload: X → Overload. This also lets
parameterized and plain aliases share one row. The encounter tracker **Runes
Required** is excluded.

Each keyword is available when at least one Constructed card in the selected
set/format either has that keyword or references it. There is one shared selection
for both kinds of metadata. For example, **Prepare** finds Sawbones and Black Market
Auctioneer as well as Wanted Poster and Jailbird. Reference-only mechanics such as
Adapt are included in the same list.
Keywords start in **Single**, matching the latest selected family through either a direct
marker or a reference satisfying each keyword. The Mode button can switch this
category to AND or OR; both headings share the same mode and selection.
Set/format changes retain a selection while either kind of match remains available,
including when a family changes its representative tag; only unavailable keywords
are removed.

Expansions reads Hearthstone's initialized expansion rows without opening the
native tray or acquiring its animation/input capture. It renders the entries
and headings with ordinary templates, so a live selected or pressed row's
animated scale cannot shrink its grid button. In Single mode, selecting an entry forwards the
native callback's card sets, specific cards, format and original row, with page
transition animation disabled. The native selected-row pointer is set before
filtering. After the callback sets filter state, selection calls the same
`RefreshCollectionPages()` path as the other filters exactly once to update the
visible cards, then updates selection glows in place,
preserving the other buttons, layout, hit areas and scroll position. Active
keyword filters are checked against the new expansion before the native callback
runs, so filtering uses their updated state once. An unused keyword catalog is
rebuilt when its menu is next opened. Selection glow meshes never determine
button dimensions. The native source frame stays hidden; Back remains pinned
above the shared scrolling contents. If the required tray cannot be initialized,
the mod logs a warning and leaves the original expansion button available.

Validation in Hearthstone after rebuilding:

1. Open the collection and click the expansion-selection button: the category menu opens.
2. Select a mod filter, go Back, and reopen it: its selection remains active.
3. In Keywords and Minion Type, cycle Mode through Single, AND and OR; the other categories must omit AND. Check that Single immediately retains the latest choice, then cycle onward: all previous choices must return unless a filter was edited in Single. Clear the root menu and verify every category, including suspended choices, is reset. Choose Next filter in Mods and build Battlecry AND Immune OR Battlecry AND Stealth; older groups should glow yellow and removing the last group should restore the earlier one. Scroll each list and verify Back, Mode, NOT and Clear All remain fixed. Toggle NOT on and off with Single, AND and OR; Mode must preserve that toggle. In Next filter, toggling NOT must leave existing terms intact. Hide Mode and Clear All in Mods, then show them again; NOT follows the Mode visibility setting. Enable Hide filters without results and verify impossible options disappear while the menu keeps its row and column counts. Enable Remember filter modes in Mods, leave and reopen the collection, then restart: modes should persist while filter selections clear. Disable remembering and repeat: the next collection session should use the original defaults.
4. In Keywords and Expansions, check that Back is readable, fully inside the tray, and does not overlap list rows. Scroll slowly in both directions: partially visible edge rows should remain visible without blank gaps. Close and reopen each subgroup: the visible rows and scrollbar thumb should return to their previous position, including after interrupting an opening animation. Back stays fixed and returns to Filters. Reopen Expansions and select an expansion: native filtering still works.
5. Close by clicking outside and by clicking the expansion-selection button, then reopen.
6. Leave/reenter the collection and repeat while editing a deck; there should be no duplicate Back buttons or mod plaques. The category menu has no Back button. Closing an unchanged submenu retains its rows and private list mask inside the inactive tray for reuse; changing menus or leaving the collection releases them and restores the source clipping state.
7. Change filter rows/columns, including a wide grid and a short grid. Borders, scrollbar width and thumb size should stay native; the track should follow the viewport height. Check that no cards show through on the left, text stays unobscured, and opening/closing moves the complete panel smoothly into/out of view.

## Install

1. Install and initialize MelonLoader for your own Hearthstone client.
2. Close Hearthstone and copy `Mods/BetterCollectionManager.dll` from the
   distribution ZIP into the game's `Mods/` directory.
3. Restart Hearthstone. A hot reload is not supported.
4. Open Settings → Mods for collection settings, and the collection's expansion
   selector for the shared filter menu.

Do not copy `Source/`, tests, reference assemblies or diagnostic tools into
`Mods/`. The DLL embeds the default Categories and Packages catalogs. Personal
settings and custom catalog edits are created locally in MelonLoader's UserData;
none are included in the distribution. Updating the mod does not require copying
someone else's preferences. A build writes to a local output folder; Windows
installation copying is optional via `DeployMod=true`.

## When it is active

- **ON** automatically when you enter Collection Manager (`DoEnterCollectionManagerEvents`).
- **OFF** when you leave CM (`Exit` / `Unload`).
- Stays **ON** during deck building / deck edit in CM.

## When collapse is disabled (even if mode is ON)

Collapse is skipped (original multi-premium lists kept) when:

- Crafting tray is shown (`InCraftingMode`), unless **Collapse while crafting** is enabled
- `CraftingManager` crafting mode is active, unless **Collapse while crafting** is enabled
- Craftability / crafting-style `FilterByMask` filters are active, unless **Collapse while crafting** is enabled
- Search text includes a premium token: golden / diamond / signature (localized `GLUE_COLLECTION_MANAGER_SEARCH_*`)

Crafting counts and other systems that call `CollectionManager.FindCards` directly are **not** patched.

Mod Settings → **Crafting** → **Collapse while crafting** is off by default.
It lets the existing display policy also summarize the native crafting results.
Wholly missing cards keep a real zero-owned variant (Normal when available),
so they remain visible and can still be inspected/crafted. Owned cards use their
preferred owned quality. Native missing/owned and quality filters still restrict
the candidates; collapsing does not introduce filtered-out variants or change
inventory counts. Explicit premium searches and the global collapse switch keep
their existing behavior. Toggling the option refreshes an open crafting tray;
refresh still waits for card inspection or a held card to finish. The option is
saved as `collapseInCrafting` in the JSON backup and `CollapseInCrafting` in
MelonPreferences.

Entering or leaving Crafting keeps the page containing the previously first
visible card, using its card ID independently of premium changes. The current
class tab is retained where possible. If that card disappears (for example, a
missing card when returning to owned-only results), another card from the old
page or its nearest surviving neighbor provides the fallback. The anchor is
captured before native filtering and applied to the existing native page
transition, with one rebuild and no second page refresh. Background crafting
filter updates do not navigate.

## Priority / ownership

Owned premium is chosen via `CollectionManager.GetOwnedCount` (not `GetBestCardPremium`, which is Diamond-first).

If nothing is owned for that CardId in the current result set: keep **Normal** if present, else the first available premium in the set.

Mixed tiles use the actual two variants selected by the existing deck-copy rule:
the preferred premium has exactly one owned copy, and its next eligible premium
is Normal/Golden. Both must be present in the current result set. Custom premium
orders still apply. The feature follows the existing collapse toggle and skip rules.
Legendary and Free cards retain the existing collapse behavior. The split uses
private copies of native meshes and keeps their materials, including golden
animation. The Golden name-banner mesh stays whole and its Normal duplicate is
suppressed. Native text meshes remain whole: Golden supplies the name and mana
cost; Normal supplies rules text, attack, health, race, and other labels. The
duplicate labels and Golden's attack/health decorations are suppressed on every
update, including after hover/text rebuilding. Native visibility is restored when
the tile is released. Decorative particle/trail effects are suppressed. One
complete native Golden contact shadow extends under both halves without being
clipped at the seam. Duplicate Normal and projected shadows are suppressed to
avoid double-darkening. Frames without a static contact shadow retain the soft
projected shadows of both actors; their shadow planes remain uncut. Original
shadow visibility is restored when the tile is released.
Shared meshes, inventory counts, and real card premium values are never merged.
Imported meshes with discarded CPU data (such as `pPlane2`) are copied from their
GPU vertex/index buffers into a private readable mesh using Unity's retained
readback API. The snapshot preserves vertex layout, index width, submesh offsets
and base vertices, is cached per shared mesh for the collection session, and is
released on exit. Original read/write flags and buffer targets stay unchanged.
If a card's mesh still cannot be split (for example, a GPU readback failure), the
mod logs the failure and refreshes that card using the previous presentation,
including separate tiles when needed for the second deck copy. Retrying is
enabled on the next collection session.

## Collection opening searches and New paging

Mod Settings → **Crafting** → **Search on collection open** offers **Off**
(the default), **New**, **Refund**, and **New or Refund (Refund first)**.
The search uses the client's localized native search terms and respects the
current native and mod filters. It waits for collection data, the UI, book/pages,
account identity, and startup filter changes to settle. Pending startup work is
retried for up to 30 seconds; initially empty results get a short observation
window, and a query reset by native initialization is retried. It does
not replace an existing query, act in deck editing/crafting or a package browser,
or keep changing the search after collection entry.

An automatic search is applied only when its matching owned variants include a
new observation: a different card/quality, additional copies, a later acquisition,
a changed refund value, or a different refund event. Searches save history only
after the native query remains applied and its page transition has completed.
Successful searches save
per-game-account and region history in `BetterCollectionManager/auto_search/`
next to the JSON settings. Fewer copies and unchanged matches do not retrigger
on reopening or restarting. The search preference is saved as
`collectionOpenSearch`. Combined mode tries novel Refund matches before New.
No cards are crafted or disenchanted by this feature.
The log records the configured mode, readiness waits, probe counts, and the final
outcome, including disabled settings, existing queries, already recorded matches,
and timeouts. A probe uses the real unread state without changing the stable
New-search snapshot used for browsing.

While collapse is active, positive **New** searches retain their original matching
variants through seen-only result rebuilds. Marking cards seen during browsing
therefore does not compact earlier pages and skip later matches. New glows and
server seen acknowledgements still use the actual seen state, and cards with no
owned copies are excluded. Rerunning the search, changing native filters, or
leaving the collection starts a fresh snapshot. Negative New searches and queries
where collapse is disabled retain native behavior.

## Disenchant choices for the inspected card

Under Mod Settings → Crafting, **Disenchant popup** enables the custom amount
picker and shows **Experimental: use with care**. It is disabled by default;
review quantities and qualities before confirming. Turning it off restores the
native disenchant flow, closes the picker and invalidates pending custom
confirmations, including after the setting is enabled again. The setting is
saved in the behavior preferences and JSON backup (`disenchantPopup`).

With this setting enabled and Preferred Quality active, the native Disenchant
button opens a picker:

- **All copies:** all disenchantable owned copies of this card across qualities.
- **Keep highest 2:** retain two owned copies using the configured quality
  preference order, and disenchant the remaining eligible copies. This is
  literally two, including for Legendary cards.
- **Only 1:** one copy of the currently inspected quality, using the native
  warning and animation flow.

Each option shows its copy count and dust value. Trial copies are excluded;
non-disenchantable copies remain owned and can count toward the retained two.
Options with no eligible copies are hidden. Options removing exactly the same
counts of each quality appear only once; the native single-copy flow takes
precedence when equivalent. Equal amounts affecting different qualities remain
distinct choices. With one distinct action, the amount picker is skipped;
native single-copy warnings or the batch confirmation still apply. With no
eligible action, no picker opens. Cancel/Escape closes the picker.
Craft/upgrade Undo retains its native behavior.

**Never dust Signatures/Diamonds** is enabled by default under Mod Settings →
Crafting. While **Disenchant popup** is enabled, it excludes both qualities from all three options,
including **Only 1**, and guards the final native single-card action even when
Preferred Quality is inactive. Protected owned copies count toward the retained
two; all protected copies remain owned. Changing the setting invalidates an
open picker or batch confirmation. The switch is saved in the behavior
preferences and JSON backup (`neverDustSignaturesDiamonds`).
Its native checkbox caption uses two lines: **Never dust** and
**Signatures/Diamonds**. Row spacing and scrolling include both lines.
When **Disenchant popup** is off, this protection is inactive and its checkbox
is redundant: **Hide redundant settings** hides it; otherwise it remains visible
but disabled. Its saved value is preserved and restored when the popup is enabled.

The two batch options require a confirmation listing qualities and dust, then
close the preview to submit the inspected card's native pending transaction.
Closing runs through native Back navigation, including the preview callback and
removal of its navigation entry. This prevents a later Back from closing an
already-closed preview again, including after dusting the last Refund match.
If native navigation is locked, confirmation leaves the transaction unchanged.
They may leave decks incomplete. Changing the inspected card, inventory, quality
order, pending transaction, eligibility or price invalidates the choice.
**The collection-wide extra-copy / Mass Disenchant command is never invoked.**
Every new sale operation belongs to the current inspected card's transaction.

The mod builds against the supplied client references. Managed checks cover
quantity selection, retained qualities, cancellation, stale confirmations and
single-card transaction scope. Popup appearance and live server reconciliation
still need an in-game check.

## How it works (paging)

Harmony postfix on `CollectibleCardClassFilter.UpdateResults` collapses:

- each `List<CollectibleCard>` in `m_currentResultsByClass`
- Death Knight sibling lists `m_unfilteredDeathKnightCards` / `m_hiddenDeathKnightCards`

`GetPageContents*` / `GetNumPagesForTab` then `Skip`/`Take` with `m_cardsPerPage` (8) on those collapsed lists, so pages 1..N are dense.

On collection change while mode is ON, the mod calls `RefreshCurrentPageContents`.

For an ordinary owned-card view, removals of the card opened in native Inspect
take a dedicated path: only that CardId's remaining qualities are collapsed
again. Existing native result/class lists and other cards' tile caches are kept;
the changed group is replaced in place, and native page assembly fills any gaps.
Removing the last copy also updates filter availability without rebuilding the
other cards' keyword indexes. Server disenchant confirmation reuses the same path.
The fast path requires unchanged filters/settings and a known removal delta for
the inspected ID. Native text searches, Core/Classic counterpart dependencies,
crafting, deck editing, recipe/package views, and unrelated collection events
retain the complete update. Native dust/deck reconciliation still runs normally.

## Logs

MelonLoader log (typically under the game install `MelonLoader\Latest.log` or the console).

Look for `[Better Collection Manager]` / messages from this mod:

| Log | Meaning |
|-----|---------|
| Harmony patches applied successfully | Patches registered |
| CM enter / Preferred mode ON | Collapse armed |
| CM leave / Preferred mode OFF | Collapse disarmed |
| UpdateResults collapse RUN | Collapse executed |
| UpdateResults collapse SKIPPED: … | Skipped + reason |
| Collection changed … refreshing | Page refresh after collection change |
| Inspected-card refresh: … | Scoped quality/result update after an inspected-card removal |
| Exceptions with stack traces | Failures |

### Turning down verbosity

In `DebugLog.cs`:

```csharp
public static bool VerboseDebug = false; // default; set to true only for diagnostics
```

With `VerboseDebug = false`, per-list / sample CardId→premium dumps are quiet; enter/leave/skip/exception messages still log.

## Build

The Linux build script defaults to
`$HOME/Games/battlenet/drive_c/Program Files (x86)/Hearthstone`, using the current
user's home directory. Set `GAME_DIR` to override it for another installation.
No personal username or absolute home path is stored. The DLL targets .NET Framework
4.7.2 and requires references from your installed Hearthstone and MelonLoader.
Game and loader DLLs are not shipped in this package.

On Linux (or another system with Bash), install a .NET SDK and run from the
source directory:

```sh
./build-linux.sh
# For another installation:
# GAME_DIR="/path/to/Hearthstone" ./build-linux.sh
```

The result is `linux-build/BetterCollectionManager.dll`; installation is
manual. See [README-LINUX.md](README-LINUX.md) for details.

On Windows, use Visual Studio / Build Tools MSBuild with the .NET Framework
4.7.2 targeting pack:

```powershell
msbuild BetterCollectionManager.csproj /p:Configuration=Release /p:GameInstallRoot="<Hearthstone directory>" /v:minimal /nologo
```

The result is `bin/Release/BetterCollectionManager.dll`. If the loader DLLs
are in a different directory, pass `/p:GameMelonLoaderPath="<loader directory>/"`.
`/p:DeployMod=true` additionally copies the DLL to the configured existing game
installations. Optional `LiveGameInstallRoot` and `TheorycraftingGameInstallRoot`
select installation roots; Theorycrafting configurations select the latter's
references. Use the client matching your intended installation.

To create the shareable ZIP after a Release build:

```sh
python3 Tools/package_release.py --dll linux-build/BetterCollectionManager.dll
```

The packager includes source, tests, documentation and the single mod DLL, checks
for local paths, and excludes personal data, logs, media, debug symbols, game DLLs,
Git history and build caches. Release builds disable debug symbols. Review the
root README and `SHA256SUMS.txt` in the resulting archive before distributing it.

## Project layout

| File | Role |
|------|------|
| `BetterCollectionManagerMod.cs` | MelonMod entry, Harmony apply |
| `PreferredQualityMode.cs` | Enable/disable, skip rules, collection-changed refresh |
| `PreferredPremiumHelper.cs` | Signature→…→Normal ownership pick |
| `PreferredQualityCollapse.cs` | Collapse lists by CardId |
| `MixedPremiumPair.cs` | Eligible second deck-copy variants and horizontal name-banner seam |
| `MixedPremiumMesh.cs` | Private mesh clipping with UV, material and skinning preservation |
| `MixedPremiumCard.cs` | Split tile lifecycle, half selection and rendering fallback |
| `CollapsedPremiumCount.cs` | Ordered ownership display for existing premium variants |
| `CounterTextLayout.cs` | Native text projection and glyph/frame bounds |
| `CounterSeparators.cs` | Half-size separators centered vertically among native digits |
| `CounterOutline.cs` | Screen-space black strokes behind colored counter digits |
| `ModsSettingsUi.cs` | Native Settings button, metal trim and Settings/Escape hooks |
| `ModsSettingsMenu.cs` | Collection, preferred quality and filter layout settings sections |
| `CollectionQualitySettings.cs` | Validated collection preferences, filter grid dimensions and JSON backup |
| `QualityOptions.cs` | Display/input modes, English labels and ordered quality candidates |
| `QualityDeckInput.cs` | Gesture scopes, live inventory fallback, lock adjustments and deferred drops |
| `QualityChoiceMenu.cs` | Native post-drop quality picker and cancellation |
| `Patches/QualityDeckInputPatches.cs` | Scoped collection gestures, AddCard routing and inventory-frame hooks |
| `KeywordGroups.cs` | Stable mechanic groups and localized Fabled/Tourist families |
| `KeywordSetTray.Keywords.cs` | Shared keyword selection and mechanic-group section headings |
| `KeywordSetTray.Grid.cs` | Responsive filter tray grid, frame sizing and scroll registration |
| `KeywordSetTray.Back.cs` | Pinned Back layout, clipping, bounded hit areas and pointer priority |
| `KeywordSetTray.Visibility.cs` | Menu animation, session scroll memory and input cleanup |
| `KeywordSetTray.Input.cs` | Outside clicks, modal background capture and hide-area registration |
| `KeywordSetTray.Interaction.cs` | Shared scrollbar rail clicks and drag capture |
| `FilterButtonFeedback.cs` | Preserve native row feedback artwork and optional highlight listeners |
| `FilterGridLayout.cs` | Per-menu automatic dimensions, row-major placement, row scroll contributions and hidden slide distance |
| `FilterPointerGesture.cs` | Press ownership and drag-aware outside-close decisions |
| `FilterModes.cs` | Per-category modes, supported operations, grouped expressions and reversible terms |
| `FilterBitSet.cs` | Immutable native card sets and word-wise AND/OR/intersection operations |
| `FilterAvailability.cs` | Inverted candidate index, category intersections and indexed result membership |
| `ModsSettingsMenu.Scroll.cs` | Settings overflow scrolling within the established panel size |
| `ModsSettingsMenu.Scrollbar.cs` | Visible track and grip, wheel/drag synchronization, input capture and resource cleanup |
| `ExpansionFilterState.cs` | Expansion unions/intersections and native specific-card groups |
| `KeywordSetTray.Modes.cs` | Pinned mode/clear controls and selection glow updates |
| `KeywordSetTray.Rows.cs` | Incremental visible-row updates preserving native objects, layout and scroll |
| `KeywordSetTray.Extras.cs` | Pooled card type, minion type, spell school and rarity menu rows |
| `KeywordSetTray.Expansions.cs` | Native expansion data and callback bridge for the shared grid |
| `FilterTrayGeometry.cs` | Private frame nine-slicing, native wooden background and fixed-size scrollbar parts |
| `CollectionClickInspect.cs` | Native left-click previews outside deck editing |
| `Patches/CollectionClickInspectPatches.cs` | Collection release hook after pressed-premium selection |
| `Patches/MixedPremiumPatches.cs` | Native actor, pointer and tile lifecycle hooks |
| `Patches/CollectibleCardClassFilterPatches.cs` | `UpdateResults` postfix |
| `Patches/CollectionManagerDisplayPatches.cs` | CM enter/leave |
| `DebugLog.cs` | Verbose MelonLogger helper |

## Keyword regression checks

Run `dotnet run --project Tests/KeywordFilters/KeywordFilters.csproj` with .NET 8.
Set `BCM_FILTER_BENCHMARK=1` to additionally compare availability updates and scalar
versus indexed filtering over 4,096 cards and 96 options, including expansion
catalog scope changes and every filter category with empty-option hiding off. These managed timings
measure the filter algorithms; they do not include Hearthstone rendering or native
page refresh costs. Differential checks cover bitset boundaries, mixed AND/OR
groups, all categories, native metadata invalidation and scalar fallback cards.
Expansion changes retain immutable keyword metadata while recomputing scoped
counts and canonical aliases; native collection changes discard that metadata.
All submenus pool native rows, including initially hidden options. The expansion
bridge reuses its native pool only while the actual broad card-set arguments match;
special groups outside Wild and external native set changes prepare a new pool.
These checks compile the production catalog and filter state against small game
stubs. They cover same-name tag variants, reference-only aliases, Sawbones'
direct Prepare tag, combined selections, deselection, and preserving selections
when the representative tag changes with the card pool. They also check unified
direct/reference matching, Tourist without references, reference-only pools, mixed
metadata combinations across selected keywords, and pruning selections after set
changes. Mode checks cover each category's Single/AND/OR truth tables, empty
selections, replacing selections, multi-race and All-type cards, the Common/basic
alias, expansion unions and intersections, specific-card groups and keyword availability.
Grouping checks cover localized Fabled and Tourist variants, Quest/Questline,
OR matching within a family, AND matching between families, stable classification
across card pools, all six factions, and Adapt remaining an expansion mechanic after
reuse in another set. Presentation checks cover parameter suffixes, the excluded
rune tracker, and reference-only mechanics. The collection UI checks run the
production keyword menu code to verify a single list with ordered category headings,
unique rows, selection across sections, direct Back navigation, hiding empty sections,
and reopening after late native scroll resets or interrupted opening animations.
Synthetic aliases test the deduplication failure independently of the installed
game's keyword DBF.
The Sawbones/Vanessa/Black Market Auctioneer/Wanted Poster/Jailbird tag fixtures are based on
[HearthSim's extracted CardDefs.xml](https://github.com/HearthSim/hsdata/blob/master/CardDefs.xml).
These checks do not replace rebuilding the mod against your installed game DLLs
and validating its collection UI in Hearthstone.

## Mixed premium regression checks

Run `dotnet run --project Tests/MixedPremium/MixedPremium.csproj` with .NET 8.
The checks compile the production pair selection, collapse, mesh clipper and
tile controller against managed game/Unity doubles. Grid fixtures also compile the
production frame resizer and tray adapter, checking sliced border UVs, scrollbar
width and track length, scroll registration, repeat rebuilds and fully hidden
slide endpoints. Tilted camera fixtures check the native left frame seam and
reuse of native artwork without an added background plane. Oversized native hit
boxes and short grids check the clipped viewport. Row contributions check that
parallel columns count once, full visibility bounds survive, mixed-height rows
use their tallest item, and fitted heights do not inflate the native scroll range.
The production expansion bridge is exercised with native-row doubles, including
original callback targets and selected-state refresh. These checks run the actual
row selection/chrome and filter-change handler, including an active glow larger
than its button, callbacks which do not update native selection, retained button
identities/scroll position, immediate page filtering, active-keyword pruning and
rollback after callback errors. Additional checks cover
per-menu Automatic values, full-width heading/back textures, outside drag capture,
and keeping the opening button clear at different viewport heights. The production
interaction checks exercise native SetFilterItem press/hover/release behavior without
a UIBHighlight component, preserved feedback renderers after one-/two-/three-column
resizing and on Back, row-wide texture/vertex gradients across idle, hover,
selected and pressed layers, visibility refresh during a press, rollout cancellation,
no added transform movement, and disabled headings.
Scrollbar checks cover click-to-position, continued dragging, both endpoints,
outside releases, native input locks, the full configured rail collider, normalized
values and drag cleanup across one to three columns and angled projections. Automatic-column checks cover medium lists with many
headings, the 99/100-entry boundary and explicit column settings. The production
Back layout and hover handling run with oversized inherited colliders and
offscreen font meshes, checking that Back and the first heading stay visible,
the scrollbar remains reachable, and unrelated controls retain input.
Production visibility checks exercise completed, immediate, repeated and
interrupted closes, both with Back capture and in the category menu without Back.
They verify that the collection's colliders are restored, pending capture is
cleared and the invisible outside-click area is disabled after closing completes.
Additional checks cover registered child colliders, shared input ancestors,
an externally hidden tray, cancelled animation disposal and native cleanup exceptions.
Outside-close tests also dispatch the production PegUI release and poll handlers
in both orders, with clipped row hits beneath the visible frame. They check
background blocking while open and closing, input unregistration, restored
reopening state and a subsequent collection click. Session checks cover independent
submenu scroll positions, resumed expansion selection and reset on a new session.
Back tests use tilted camera projections and oversized child
colliders to check its independent visual and interactive widths.
Row artwork checks cover joined column/row edges, preserved text size, glyph
bearings, separate text output surfaces and late font readiness. Gradient checks
exercise vertex tints and baked shading across three columns, including rotated
atlas coordinates, unchanged border UVs and untouched native source meshes.
These are geometry checks, not live rendering. They cover ownership and
rarity boundaries, custom premium order, partial result sets, both horizontal
halves, UV/skin/material preservation, initial-press drag selection, native
count/lock refresh requests, cleanup, tile reuse, and rendering failure recovery.
They also check full preferred tiles when two preferred copies are owned and a
single uncut Normal rules-text mesh after hover/rebuilding, for both premium orders.
Camera fixtures test both screen directions with a translated/scaled card:
Golden's mesh vertices stay above the screen seam and Normal's below it, and
clicks select the corresponding actual premium. Label tests check one visible
name, mana cost, rules text, attack, health, and race after native reactivation,
plus restoring label visibility on release.
Banner fixtures place the seam through the banner in both orientations, keep
the complete Golden banner without mesh readback, suppress the Normal duplicate
after hover, and restore native banner visibility on release.
Counter checks include unowned lower versions, omitted unowned higher versions, missing special versions,
custom order, full preferred tiles, native count/trial preservation, large
counts with a constant font size and fixed padding around measured glyphs,
single-quality native counters (including trials, hidden quantities and `9+`),
transitions between native and breakdown counters, deck-lock hiding, and
restoring native font/layout settings on release. Additional fixtures exercise
render-to-texture measurements at different label/card scales, delayed glyph
readiness without hover, styling after native single-copy early returns in all
three themes, projected glyph bearings and independent frame/text offsets,
custom output surfaces, half-size vertically centered separators and their
cleanup, maintaining frame alignment with different prefab origins, and one
complete uncut native shadow with visibility restored on tile release.
GPU-only fixtures reject CPU vertex/triangle access and test multiple vertex
streams, packed colors, 16/32-bit indices, submesh gaps/base vertices, cached
readback reuse, buffer-handle disposal, and selecting both imported-card halves.
Click checks cover native preview routing for cards and cosmetics, virtual
preview overrides, deck-edit/selection transitions, touch, dragging, input locks,
specialized pickers, pending transactions, exception cleanup, and preserving the
pressed premium when releasing on the other half of a split tile.
Settings checks compile the production preferences, display modes, deck-input
controller, Mods menu and drop picker against managed game/UI doubles. They
exercise frame compatibility, pair-mode fallback, all four qualities, custom
orders, live deck removal, native limits/bans, trials, sideboard destinations,
excluded input contexts, temporarily hidden drags, native pickup events and release interception,
delayed insertion and picker opening, confirmation/cancellation, stale inventory/decks,
native dropdown selection, removed section captions, conditional half choices,
automatic display policies, hidden redundant fields and freely editable manual
overrides, the persistent native checkbox, restoration of saved manual
preferences, inherited drag policies, balanced modal claims, and JSON
round trips and invalid enum values.
Layout fixtures check projected heading/frame bounds for both camera
orientations and multiple viewport heights. They check the native reference
gap, simultaneous section visibility, non-overlapping fields and checkboxes,
complete border containment at 1080p, 720p, 480p and 4:3, and clean single-line captions even when source text has old glyph meshes and field
text has enormous offscreen render bounds.
They do not exercise Unity's renderer or Harmony inside the game.

After rebuilding, left-click cards, skins, card backs, coins and pets while
browsing: each should open its existing preview. Repeat while editing a deck to
check native add/equip actions, and verify right-click still opens previews.

After rebuilding, inspect a Common/Rare/Epic with one Normal and one Golden
copy: it should occupy one slot, split horizontally at the name banner with
a single readable rules box. The complete name banner should remain Golden.
Add Golden from above the seam, then add Normal from below it. With two
preferred copies owned, check that the complete preferred tile is displayed.
Check the same operations in the other order,
and drag across the seam to verify that the initial half determines the version.
Also inspect by right-clicking each half, turn pages, change filters, and reopen
the collection. Premium searches and crafting views should retain their native
separate versions. Check `Mixed premium tile failed` in the log if a particular
card falls back to the previous presentation.
Check the `+` counter against owned quantities, including an existing version
with zero copies; reorder premiums and check the label updates accordingly.
Compare short and long totals: the font size should remain constant as the
counter background widens with constant padding. On first opening, inspect the
counter color before hovering and check the left/right frame edges at the seam.
With counter coloring enabled, one owned quality shows `x2` (or its quantity)
and a total of one owned copy shows no counter on either background.

## Open risks

- Jump-to-card by a specific non-preferred premium may miss if that premium was collapsed away (anchors from the UI should already be preferred tiles).
- Localized premium search tokens must match `GameStrings`; English fallbacks exist if strings fail to load.
- Premium collapsing applies only to the cards view; cosmetic views use their native inventory lists.

After rebuilding, open Settings and check **Mods** below Signature. Open the
submenu, return with Escape and by clicking outside, then open Settings again: there should be
one Mods button and no extra menu left over. Hover Privacy and verify Mods does
not resize or move. Hover Mods to check native highlighting, sound and animation;
only its metal surround and screws should match the Graphics fields. The gold
button face and its hover behavior should remain, with its caption centered and
the calibrated width preserved. Repeat after a
resolution change. Check the contextual display heading and the two Add to deck
headings, absence of a Back button and inherited
Signature/favorite captions, and all eleven settings dropdowns across the five
groups. Check the eleven settings dropdowns and ten checkboxes, scrolling
to reach options beyond the original panel surface. Confirm the title and frame
stay fixed while scrolling, offscreen controls cannot receive input, and an open
dropdown keeps its own scrolling. Repeat when changing display modes or resolution. Verify that half options
appear only for Split card or Stacking, that Highest quality only and Stacking
hide click settings and apply Quality order, that Prefer two owned copies hides
click settings and applies Highest fully available, that
dragging additionally offers an independent Dragged half in Split card or Stacking,
and that both input settings disappear in Two highest qualities. Uncheck Hide
redundant settings: every dropdown must reappear and manual overrides must work.
Check the Display order (Highest, Split, Stacking, Two highest, Two owned), Drag order
(Dragged half if available, Choose, Same as click), and that saved choices
persist after restarting the game.
Hover one split half, leave the card and enter only its opposite half: both
actors must retain the native white hover glow in either direction. Moving out
must clear hover on both halves.
With one high-quality and two lower-quality copies, test all five display
modes, add twice by click, remove the high-quality copy from the deck, and add
again. Check the actual premiums in the deck. Repeat with a split tile and
each click/drag priority. Enable Choose after dropping, verify the deck stays
unchanged until a choice, and test Cancel, Escape, outside-click cancellation,
and a drop with only one usable quality. Copy-limit and banned-card frames
should remain, while an exhausted half with usable alternatives stays unlocked.
Separate tiles must keep their exhausted-quality frame and add exactly the
clicked/dragged version, including incompatible-frame fallback in Split mode.
With one Signature, two Golden and more Normal copies, Highest fully available
must add Golden twice; without any pair it must use the usual quality order.

## Stacking input and effects

Mixed cards are built immediately, including while the collection book opens.
The front mana digit follows its card's drawing layer. Its visible glyph or texture
surface is also lowered to just above the mana gem's camera depth; queue changes
alone do not prevent elevated native text from intersecting the book cover.
The horizontal spread is ten percent of the card
width, with the preferred card shifted four percent right and the rear six percent left.

Both actors' colliders are disabled while stacked, including colliders rebuilt by
native hover states. The collection visual retains its original hit collider so
native click, drag, hover and preview events can reach it. Original collider
states are restored on release. The rear actor is mirrored horizontally about
the vertical card axis while preserving its frame center; the full preferred
front card keeps its native scale and readable text.

The rear actor's rune decorations, duplicate labels, mana gem and shadows are
suppressed. Duplicate labels use native `UberText.Hide()` as well as object hiding,
so text rendered onto a separate surface is hidden too. Warm/rune hover effects are suppressed on both actors while the
ordinary white card highlight remains under native actor-state control. These
suppressions are reapplied after state updates and reverted on release.

## Filter page anchoring

Custom filters retain the nearest available card from the previous view rather than
retaining its numeric page. A surviving visible card is preferred, followed by the
nearest surviving neighbor in the previous order (successor first on ties). The
current class tab is retained while it has results, including dual-class cards.
If a new expansion has no cards in common, its first page in the same class is used;
if that class is empty, navigation falls back to another available result.
Expansion selection captures the old position before the native callback rebuilds
the buckets. Cosmetics keep their native refresh behavior.

Run `dotnet run --project Tests/Pagination/Pagination.csproj` for page anchoring
checks. In game, apply and clear filters while viewing later Druid pages, switch
expansions, and check empty results and dual-class cards. For Stacking, check the
left exposed frame and front card with click, drag and right-click inspection,
including Signature/Golden cards, then change display mode and turn pages to check
that actors, counters and locks reset correctly.

## Startup and collection scope

The bulk of hook installation waits for Hearthstone's native
`SplashScreenShown` event. The mod lets the native 0.25-second fade finish,
waits for an end-of-frame and the following frame, then initializes once and
logs its duration. Collection entry completes initialization first if that
startup path was skipped. Collection update and late-update work return
immediately outside the collection. Custom FPS and access to Mod Settings remain
available globally. Dropdown color changes are restricted to mod controls and
the custom FPS control.

Package recipe artwork belongs to the collection page slot, independently of
the unused hand-card actor's startup transforms and visibility. Only loading the
actual native page preloads its recipe template and portraits; building the entire
collection result list performs no portrait loading. Folders have no aggregate
mana cost, premium quality or rune cost, avoiding full-inventory scans per member. Recipe skins are
baked into static meshes with immediate bounds, including while the template is
inactive. `ComboCollectionCards.Loading` prepares private package actors, recipe
graphics and their real captions before the native page-ready callback. The
callback executes synchronously, even if portrait or offscreen text geometry is
not ready. The former readiness coroutine caused five-second stalls in the
20:39 game log and has been removed. The same prepared actor and artwork are
transferred into their slot at `SetActors`; late portraits complete on that object.
When returning to a package, preparation transfers its existing hidden slot's
private actor, recipe and captions instead of allocating another full model at
the page transition. Visible packages are never taken. The old slot loses the
transferred binding; the destination owns cleanup. Transfers also reparent the
package lock. Unused prepared objects are released after the callback or when
the collection resets. Ordinary-card pages keep their immediate callback.
Collection package fitting uses the slot's local X/Z geometry; page movement,
rotation and camera projection no longer resize the folder every frame. Only a
changed slot size or center triggers another fit. A transferred recipe is fitted
while hidden, before activating it in its destination. Popup previews retain their
separate camera-based layout.
A newly available page manager or prefab
bypasses an earlier template retry delay. Pending portrait handles remain attached
to their prepared folder instead of triggering a five-second reconstruction delay.
A portrait load failure retains the recipe shell and retries its texture in place.
Each folder has its own short construction retry; one failed folder cannot block
the other recipes. Native carrier artwork is hidden while a recipe is pending.
The carrier comes from the actual page, even if the first saved member is unavailable.
Preloaded assets are released when the collection resets.
Native prefab behaviors are removed before the recipe clone
activates. Recipe artwork and captions hide on the collection `Hide` boundary
and return on `Show`.

Settings scrolling keeps edge rows active until they leave the viewport entirely.
The attempted `CameraMask` pass made all settings content disappear in the live
client and has been removed. Settings never changes row render layers or creates
a camera-mask pass. `FilterTrayGeometry.ClipMesh` cuts private copies of the
actual edge triangles against the screen viewport, preserving UV0–3, tint,
normals, tangents and material slots. Uncut native meshes remain unchanged.
Skinned edge artwork uses a current baked copy with the same materials, shader
properties, sorting and rendering-layer mask. Native renderer enable state is
preserved; `forceRenderingOff` only suppresses the temporarily replaced skin.
Direct native text keeps its mode and layout at the edge. `SettingsTextClip`
captures the existing glyph renderer with the UI camera's cropped projection,
including its outline/bold passes, at twice its measured screen resolution.
Only the private output quad is cut; inherited UberText fit dimensions never
resize or regenerate the font. The same direct glyph returns when fully inside.
Popups and cancel catchers are excluded. Partially visible hit boxes intersect
the same viewport. Original geometry, hit boxes and renderer flags return before
relayout/hide. Private meshes and captures are released on close.

Filter submenu tools share one pinned area. Initially only Back and the available
`Show Logic Controls` / `Show Edit Controls` switches are visible. Logic contains
the mode, NOT and optional Clear All; Categories Edit contains Add Folder, deck import,
deck removal and Turn Deck Into Combo. Packages Edit contains **Turn Deck Into Package** and **Add Folder**;
editing uses the package's right-click menu. Packages has a pinned Standard/Wild
toggle and Expansions entry; logic is confined to its expansion submenu.
The Edit Controls switch follows the navigation controls, with its actions directly underneath.
Opening either group closes
the other, and clicking the active switch closes it. Its label becomes `Hide …`
and its native selection glow marks the open group. The choices underneath retain
their own scroll viewport, and opening/closing tools recalculates the reserved
height without putting edit actions into the scrolling filter rows.

The separate `Show Logic Controls` and `Show Edit Controls` checkboxes in Mod
Settings → Filter menu hide their respective switch and contents without changing
filter modes or selections. Both default to enabled in older configurations and
persist independently in Melon preferences and the JSON backup. Expanded groups
are remembered per submenu for the current collection session and start collapsed
in a new session. Any non-Single mode forces the corresponding Clear All and
the root Clear All to remain visible, independently of these visibility settings.

Interior tool slices meet without per-button padding. Ordinary mode controls
occupy one grid cell. The native rail cover is reasserted after activation,
including after native `UIBScrollable.Start`, so a menu that needs no scrolling
closes its rail on its first opening too. Filter chrome and custom combo/picker
actions sound on PRESS; PegUI and the pinned-control physics fallback share one
sound per press.

Combo and package editors include a native Category dropdown with all named
categories, initially selecting the captured/saved category. Changing it updates
the saved category while preserving entry identity and card editions/counts;
Uncategorized remains calculated and cannot own saved combos. Creation also
allows choosing a category when multiple category filters are selected.
The package folder selector is enabled for both new and saved packages, including
moves between Custom, Parasitic, Factions and user-created groups. **Add New** beside
either destination dropdown creates and selects a named folder without losing the
current entry's draft. Enter confirms the folder name; Escape cancels it. **Add Folder**
in either filter's Edit Controls creates an empty category/package folder without an
edited deck. **Add New** in the editor provides the same folder creation alongside
the destination dropdown.
Folders persist independently of cards as `.folder` edits. Package version 7 records
store the selected group explicitly; older version 6 entries and edits retain their
previous Custom or built-in destination.

Inspect explicitly activates component-card prefabs and resets native cached
visibility through `Actor.Hide`/`Show`. Card fitting uses the actual card face
and mapped text ink, excluding unrelated effect geometry and cached font planes.
A late native visibility reset is repaired while the inspect modal owns the card.
The log includes `[Combo preview]` entries with card identity, face bounds, scale
and camera depth to diagnose client-only rendering problems.

Both Settings and editor scrollbar extraction use `NativeScrollbarArt`: XY and
XZ track planes, separate grip renderer branches and baked skinned meshes are
supported. Source discovery checks the Sets tray and native vertical list/deck
scrollers, including loaded inactive objects and prefabs. A native source with
both rail and grip artwork is preferred over a grip-only source. This restores
the original source fallback needed when Mod Settings opens without a suitable
Sets controller. Settings requires both visible rail and grip artwork. If loaded
scrollers provide only a grip, or no usable artwork at all (the supplied 21:22 log
reported `loaded scrollers=0` in the hub), discovery loads the installed client's
desktop collection prefab as asset data. Its reference is read from the native
`CollectionManagerScene` constructor. Nested deck-tray/scrollbar references,
including platform overrides, are followed without instantiating the collection
or running its scene controllers. Asset handles retain the native materials across
scene changes and are released when the mod unloads. Horizontal bars are excluded. Connected pieces
are extracted from batched tray meshes with their original UVs, vertex colors and
material slots, excluding unrelated frame geometry and the disabled-track cover.
The grip is measured from compact triangle geometry rather than imported mesh
bounds, which can include unused vertices or the entire prefab. Its actual width
sets the scale independently of the rail and input collider. Candidate rail pieces
must align with that grip; wide tray borders are excluded even when the track
collider covers the list. Settings allocates 105% of a dropdown's height to the
grip width, and the member editor allocates 1.6 font units. Native grip aspect
ratios remain intact. The editor keeps the native track's X axis. Settings aligns
the principal rail and grip graphics on one axis, excluding shadow, glow and
highlight bounds when finding their centres. Native hit bones can be offset
from that artwork; shadows retain their offset from the visible grip. Settings
places the bar in front of the actual
background slice renderers, including when they are children of a common rig.
The 21:35 log exposed a null root renderer in the previous lookup; it now
traverses the background's child slices. Native `ButtonListMenu.Show` acquires
its dialog claim before `LayoutMenu` and sets `IsShown` only afterwards. If
layout throws, Settings calls the native base `Hide` directly to release that
claim before the Options error path destroys the failed menu. Repeated failures
therefore cannot accumulate invisible input blockers.
`Native scrollbar:` log entries include the selected
renderer names and measured grip/rail sizes.
The supplied 20:15 game log exposed the actual rejected case:
`rail=False; grip=True; usable=False`. A separate rail renderer remains optional
for the editor:
the native Sets grip remains valid when its track is only a collider in the tray
background. The collider supplies travel/input bounds without inventing another
rail graphic or rejecting the original grip. The regression fixture includes
this exact shape and checks native-grip rendering and track dragging.
Native track collider width is not used as the grip's visual width when no rail
renderer exists: a broad input area must not shrink the grip to a dot. Common
source ancestry includes the track and thumb references, so sibling art remains
available. `Mod Settings scroll:` reports the viewport, actual scroll range and
whether its bar was created; early missing-source/track errors are logged too.
Managed regressions cover these shapes, inactive inspect prefabs,
late visibility changes and package independence from the hand-card actor.
Verify actual rendering and transition timing in the game.

## Custom FPS in the original Options menu

The original Graphics / Frame Rate dropdown has an extra **Custom** choice.
Select it, enter an integer from 1 to 1000 in the field, and press Enter to apply
and save it. Zero is invalid and leaves the previous target unchanged; it does
not mean unlimited FPS or the native default. Escape cancels; clicking outside
accepts valid input. The selected
custom target and its number are saved in MelonPreferences and the existing
`BetterCollectionManager/quality_settings.json`, then restored automatically
on the next game launch. Selecting a native FPS preset disables the override
while retaining the last custom number for later use.

The custom target applies while Hearthstone has focus and preserves native
background throttling. It disables VSync while active because desktop Unity
otherwise ignores `Application.targetFrameRate` ([Unity documentation](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/application/targetframerate)).
Numeric editing uses Hearthstone's own `InputFieldUI` through
`UniversalInputManager.UseTextInput`, the same input path as the pack quantity
prompt. The input has a transparent background to preserve the native parchment
and follows the caption's left inset and text height. Managed regression checks cover persistence, startup restore,
validation, cancellation, native dropdown rebuilds, callback forwarding and
returning to the original FPS presets; actual game rendering remains an in-game
check.

Native filter captions discard inherited runtime glyph meshes after cloning a live
row template. The new UberText keeps its own generated glyph renderer; stale cloned
TextMesh renderers stay disabled across selection, reflow and scroll visibility
changes so old captions cannot draw on top of updated labels.

## Experimental hub menu and static Darkmoon entry

These experiments are currently disabled: their installation and update entry
points in `BetterCollectionManagerMod.cs` are commented out. DFT and the
Battlegrounds/Tavern Brawl menu entries therefore use the game's original behavior.
The implementation and preference documentation below are retained for future
work; saved preference values do not activate these experiments.

The implementation uses the MelonPreferences category `BetterCollectionManagerHub`:

```toml
[BetterCollectionManagerHub]
StaticDarkmoonButton = true
TavernBrawlInMainMenu = true
```

These switches only apply when the experimental entry points are enabled in code.

**StaticDarkmoonButton** replaces the animated Darkmoon Faire Treasures entry in
the main menu with a plain native button labeled **Darkmoon Faire**. The patch
intercepts the original controller before it instantiates the animated entry
prefab, including its hero/pet/custom visuals. Normal offer-data initialization
continues. The replacement uses the current preferred offer when clicked,
honors native visibility, interaction and login restrictions, and opens the
usual offer page. It reuses its button across main-menu visits. A missing native
button template restores the original entry and records a diagnostic. Drawer
captions can be unwired from `BoxMenuButton`: the replacement also resolves child
text and the native sibling `ButtonName`, then tries Arena/Modes templates if needed.
The caption is created as a fresh native text owner with the source's typography
and pose. It renders directly, without copied widget scripts, cached glyphs or
render-to-texture surfaces. While the entry is visible, a delayed caption hide
is repaired without toggling the whole button. The copied Collection icon is
a placeholder while the current artwork is being resolved or is unavailable.
An empty native event widget supplies `GetWidget()` for Hub's ready callbacks;
it contains no animated presentation. The Hub can register/remove its listener
normally, and opening the static button sends `LUCKY_DRAW_OPENED` to dismiss the
native notification. Successful callbacks log
`Hub: static Darkmoon ready callbacks completed with empty event widget; animated entry suppressed`.

The button follows the **current native DFT offer model automatically**. Custom
entries resolve through the native asset library and its device-specific asset
address; HeroSkin entries resolve the current card's static portrait through the
asynchronous definition API; Pet entries use the current variant's icon address.
If the library is not loaded yet, its root address is discovered from the
client's own loader and loaded asynchronously too, without calling its
synchronous lazy-loading getter or pinning the library GUID in the mod.
No offer IDs, custom prefab paths or images are baked into the mod DLL. A new
offer, featured item or redirected native Custom/Pet asset reference refreshes
the image without user action or recompilation.

The referenced artwork asset is loaded asynchronously as an asset. The mod never
instantiates the original animated DFT entry to make its image. Textures are copied
from the GPU; Sprite/custom-prefab artwork is rendered from copies of renderer,
mesh and material data only, excluding scripts, animators, clickables, text and
particles. Static renderer data cannot reproduce presentation generated only by
client scripts; an unsupported/empty asset retains the usable static entry and
records its offer, item type and source address for diagnosis. Failed loads retry
with a delay, and an offer change retries immediately.

Images are cached under
`UserData/BetterCollectionManager/DarkmoonLogos/<key>.rgba` with a PNG preview.
The key includes the live offer/featured-item identity, resolved asset address,
client assembly build identity and snapshot format version. This client's Unity
build strips PNG decoding; the RGBA file supplies its texture directly. The
unversioned image files from the former manual export are ignored. Image changes
clear the preceding offer's art immediately. Late asynchronous results are
rejected after offer/reference changes or UI teardown and their asset handles
are released. Unchanged sources do not reload the image on every frame.
Graphics extraction, transparency and rendering of live Custom/Sprite assets
still require an in-game check. Managed tests cover native source resolution,
cache identity/validation and stale-result rejection; the complete mod is built
against the supplied game/loader DLLs.

This targets the reported pause when DFT appears after startup. It does not
change the reward-draw animation. Improvement of the pause still needs to be
measured in the actual game; asynchronous data initialization and other startup
work remain in place.

**TavernBrawlInMainMenu** gives the Battlegrounds main-menu slot the Tavern Brawl
caption, click target, availability checks, tooltip and tutorial/railroad identity.
Its visibility and interaction no longer depend on downloading Battlegrounds.
Modes searches for the old `BACON` DBF row and its visual state in the instantiated
native widgets. When both are found, it restores that native tile. Otherwise it
adds a native **Battlegrounds** text button beside Back. The log reports the row,
state and chosen route. The fallback follows Back's current reveal state, render
layers and position every frame and is placed using its actual UI camera. This
avoids retaining the offscreen or hidden pose present during widget loading.
The log includes projected button bounds and visible renderer/caption state.
A missing visual state in the loaded widgets does not
prove that all historical art was removed from the game's asset archives.

The native BG download, reconnect and tutorial entry handler is retained. The
patch completes its scene request from Modes, where Hub's original event
listener is no longer registered. Only the current Modes list is changed; global
DBF rows, event status and feature unlocks are preserved. If neither a native tile
nor a usable fallback can be prepared, the original Battlegrounds main-menu slot
is restored.

Run `dotnet run --project Tests/HubMenu/HubMenu.csproj` for the managed behavior
checks and `Tests/NativeApi` against the installed client's assembly for signature
checks. For an in-game comparison, build and install the updated mod using the
normal instructions above. Compare fresh starts with only
`StaticDarkmoonButton` changed between runs. Check for
`Hub: static Darkmoon button created; animated entry prefab was not loaded` in
the log, then open DFT, return to the hub, and repeat. Check both game modes,
Modes/Back, locked or downloading BG, and the chosen tile/fallback diagnostic.
Rendering, old asset availability and startup performance require this game test.

The supplied 01.11.2026 14:23 log confirms that the empty-widget ready callbacks
completed without falling back to the animated entry. The user confirmed that
the startup stall disappeared. The accompanying drawer screenshot showed a
missing caption and the placeholder Collection icon; the fresh direct caption
change above still requires a new in-game rendering check.

Project-owned button-click feedback is bound to PRESS, before any release action,
menu construction or collection refresh. This includes mod settings, their
checkboxes and dropdowns, the custom FPS field, combo controls, package buttons,
native arrows and cloned Hub controls. Checkbox sounds retain their native on/off
assets, chosen from the state the press will activate. Owned dropdowns play once
on each actual open/close transition; row presses and repeated shown callbacks
do not add another click. Native dropdown Small Click callbacks are suppressed
without suppressing transition effects. Text fields stay silent; selecting a
card row sounds once. Outside dismissals sound when the menu closes.
Rebinding after native listener cleanup
restores the press listener; repeated binding and physics fallback do not duplicate
it. Card/page transition and lock effects keep their action-related timing.

Inside a package, the first action is **Edit Package** when no deck is being
edited, and opens its editor directly. Its click sounds on mouse down. With an
edited deck, the first action remains **Add All** and uses native deck insertion.


Repeated UI work is bounded: settings reuse unchanged clipping meshes and text
captures, and the mesh clipper retains its CPU work buffers. Exact combo members
resolve through the native quality index; legacy editions share a normalized
index invalidated by native result changes. Stable preview text and inspect
layouts reuse their previous geometry. Filter pointer handling shares growing
non-allocating raycast buffers while preserving modal input and collider restore.
Artwork catalogs retain at most 32 membership combinations and clear on collection
exit. Token searches yield between bounded steps without losing a saved artwork
selection. Simultaneously due combo image changes run on separate updates;
combos with two or three members reuse at most two parked private models, all
released with their owning tile. Larger combos do not retain parked models.
Settings distinguish input/layout changes from card-result changes, and native
preferences saves are coalesced within each frame while the JSON backup remains
immediate. Managed efficiency regressions are included in `Tests/MixedPremium`;
see [EFFICIENCY-AUDIT.md](EFFICIENCY-AUDIT.md) for the original findings, completed
changes and the limits of the synthetic measurements.

Detached book arrows supply a concrete disabled `BoxCollider` before adding their
native `UIBButton` animation driver. The button's required base `Collider` is
abstract and cannot be created automatically on an empty object. A navigation
failure leaves the package closed and collection results intact; the open-folder
state is committed only after navigation is ready. This also prevents repeated
update failures from interrupting package press sounds. Regression checks cover
failed navigation followed by a normal opening through the update/input path.

Mod Settings dismisses on a release immediately outside its visible frame and
header, including when oversized native frame colliders intercept the normal
blocker event. Opening releases, dropdown closing releases and scrollbar drags
remain consumed. An open dropdown closes before Settings; clicks within an
expanded dropdown or inside the panel keep the settings page open.

Full native collection rebuilds after inventory changes also run the mod's result
processing. Recipe preparation retains edition scope and removes old proxies,
but skips per-card carrier indexes when no saved combo/package matches the
current selection. In the managed 10,000-card fixture this reduces one idle
preparation from 8,332,632 to 800,808 allocated bytes (about 90%). Local timings
were 14.1 ms before and 2.3 ms after; these are synthetic results, not live game
frame measurements. Filtered recipes, folder contents, legal deck classes and
edition invalidation remain covered by the integration regressions.

The inspected-card removal path avoids that full search/sort and mod result pass
when its guards hold. Differential fixtures compare its output with a full
collapse in every display mode, including quality loss, the final owned copy,
trial copies, sibling class lists and native page slicing. A managed fixture with
10,000 unrelated cards used about 2 KB for one scoped update; this does not measure
native actor loading, dust/deck updates or live game frame time.

`Collection results timing:` records updates lasting at least 25 ms, separating
the original native `UpdateResults` time from the mod's preparation, filtering,
quality collapse and insertion phases. This helps investigate pauses after
crafting/disenchanting without assuming that all inspect-close time belongs to
the mod. Diagnosing a particular pause still needs a current game log.
