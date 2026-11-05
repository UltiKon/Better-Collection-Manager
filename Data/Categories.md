The catalog uses [HearthstoneJSON build 255790](https://api.hearthstonejson.com/v1/255790/enUS/cards.collectible.json),
pinned for this catalog revision. The input SHA-256 is
`71be4da8bada839392c8b823af12ddd9e771a05fc6b974db8cea64b00c30f805`.
Selectable Titan/Old God and reward effects were also checked in the corresponding
[full entity data](https://api.hearthstonejson.com/v1/255790/enUS/cards.json).
HearthstoneJSON is [extracted from the game data](https://hearthsim.info/docs/cards/).

All 8,328 input rows are accounted for: 790 hero skins are excluded and 7,538
playable Constructed printings remain. Core/Classic prefixes yield 6,277 canonical
IDs. Reprints with independent IDs, including Wonders versions, stay independent.
Historical Classic text can differ; the current base card's Constructed text
governs the normalized group. The audit records each physical printing explicitly.
Tokens, hero powers, Battlegrounds/Mercenaries units and adventure-only cards are
outside the collectible Constructed input; named collectible promotions such as
Bob the Bartender remain included.

| Category | Canonical IDs | Meaning |
| --- | ---: | --- |
| AoE | 408 | Damage/removal affecting multiple opposing minions, board resets and repeatable multi-target damage engines. Friendly-only damage and a conditional single hit do not qualify. |
| Single-Target Removal | 1,119 | Single-minion damage/removal, neutralization, immediate minion combat and usable weapons. Conditions, random targets and selectable modes can qualify. |
| SMOrc | 719 | Face damage, Charge, hero attacks/weapons and aggressive repeated attacks; not every cheap minion. |
| Value | 2,529 | Generated resources, multiple bodies/abilities, recursion, draw on a body, mana advantages and repeated effects. Plain healing or a single stat buff is not enough. |
| Win Conditions | 154 | Alternate endings, quests, archetype payoffs, scalable finishers and established combo engines. Not every Legendary or large body. |
| Draw | 602 | Drawing/tutoring cards from the deck into hand. Giving only the opponent cards is excluded. |
| Unique Effects | 252 | Special deck/game rules, nonstandard resource conversions, Titan/Old God abilities and distinct transformations. |
| TECHW | 119 | Targeted weapon/secret/tribe hate, enemy silence and deliberate hand, deck, spell or hero-power disruption. Generic defense, friendly silence and self-mill do not qualify. |

Roles overlap: these counts do not add up to the pool size. 4,225 canonical IDs
have at least one role. 2,052 have no current role match; the audit keeps these
instead of forcing vanilla bodies, generic healing or buffs into a catch-all.
Conditional effects are included for what the card can do, rather than promising
that its activation condition is met by the current deck or game state.

This is a complete **data-assisted pass**, with 490 explicit editorial overrides,
not 6,277 individual manual expert reviews. The text/metadata rules are offline
maintenance tools; the mod only loads the generated explicit ID list. Subjective
roles and complex interactions can still need correction. Each row's `method`
and `evidence` identify how it was assigned, including editorial notes and rows
outside the definitions. No game language or runtime text matching is involved.

The outputs are:

- [Categories.json](../Categories.json): the embedded card-role catalog.
- [Packages.json](../Packages.json): the separate package catalog with 44 class-scoped Parasitic and 20 Factions entries; [manifest](Packages.md).
- [CategoryAudit.tsv](CategoryAudit.tsv): every considered printing, canonical ID, roles and evidence.
- [CategoryCoverage.json](CategoryCoverage.json): pool, assignment, set and role totals.
- [CategoryOverrides.json](CategoryOverrides.json): explicit corrections and package definitions.

Rebuild and verify against the pinned raw input:

```sh
python3 Tools/curate_categories.py --input /path/to/cards.collectible.json
python3 Tools/curate_categories.py --input /path/to/cards.collectible.json --check
```

Without `--input`, the maintenance tool downloads the pinned snapshot; the game
never downloads card data. A mismatching input hash is rejected rather than
silently labeling another build as this snapshot. Change the pinned source/hash,
review the diff and update the coverage tests when deliberately moving builds.

An actively edited deck can add membership through **Categories → Import current
deck**. The selected category is the target; without a selection, the deck name
supplies the fallback target and creates a category when unmatched. Its current
cards supply the members. No export, clipboard or complete deck is required. Existing category
names merge, including the built-in roles,
and new names create custom filter rows. Repeated cards and quantities do not
duplicate membership. Sideboard cards are included; the starting hero is not.
Card-ID snapshots (`.cards`) persist in `CategoryDecks/` next to `categories.json`;
previous named deck exports (`.txt`) remain supported alongside them.
These are local additions and are not included in the bundled audit/counts above.
They are loaded again on collection entry, and deleting a file removes its
additions on the next entry. Imports never rewrite the base catalog.

**Remove deck cards from category** removes the edited deck's cards from exactly
one selected category, with the deck name as fallback if none is selected.
It removes only current members, including bundled cards;
other category memberships and the deck remain intact. The selected category
determines the removal target, rather than the deck name. New local add/remove
edits are stored in a numbered sequence (`.cards`/`.txt` additions, `.remove`
removals). A later import can restore removed members, and replay preserves that
order on reload. Original catalogs/exports and grouped source definitions are not
rewritten. Empty categories and their stale selections are removed. Categories
defaults to Single. The bundled audit/counts above describe the source catalog before edits.

**Add Combo** saves the edited deck's identities and quantities as one named card
with an editable description. These are separate `.combo` entries, visible between
ordinary collection cards only inside the selected category. They do not classify
their components as standalone members. Inspect uses full native card actors in
bounded, separated cells, with paging for larger combos; `.uncombo` records an
explicit deletion. No synthetic card is added to the player's inventory/deck.

**Cards…** in Edit Combo or Edit Package edits the saved members without needing
an open deck. Search by card name or exact ID to add cards; clear the search to see
current members. Results scroll with a native rail, draggable grip and mouse wheel.
**− 1** / **+ 1** change quantities, with zero removing a member; legendary cards
are capped at one, other rarities at two, across alternate editions. The separate
card dialog's **Save** stages the selection in the parent editor; **Cancel** or
clicking outside returns without those changes. It has no Back button. Saving
the parent combo/package editor commits the staged selection to disk.
Edits retain the entry's ID, category and presentation, record exact card editions
in `.editcombo`, and never change the active deck. At least one member is required.
New combos and packages can adjust their captured snapshot through the same view.
Package artwork choices update to the remaining cards and their token families;
Previous/Next shows only the selected artwork's name, with no search text field.

**Packages → Show Edit Controls → Add Package** captures the edited deck as a folder with a name and independent
artwork. Artwork is limited to captured package cards and their noncollectible
tokens, following the native collection-related-card database. Unrelated cards, referenced
collectibles and their token families are excluded. Artwork does not change package contents. Left-click opens the folder's collection
page with Add All first; the Back button returns to the previous page. Dragging the
closed folder adds its contents through the same native per-copy deck rules as combos.
A native book arrow for Back uses the overlay UI camera independently of collection
card models, matching the book's left-arrow orientation within the camera clipping
range. The package body fits the native card slot with an 8% enlargement, with its
card-count badge hanging below like collection counters. Right-click opens Edit/Delete directly;
the Packages Edit Controls group contains Add Package and Add Folder, with no separate editing list. Packages use version 7 `.combo` records with an explicit
destination group; version 6 packages and older combos remain supported. The folder artwork clones the native recipe
button from the page manager's prefab asset without opening the recipe page. Token
portraits use the same texture/material path as native recipes. Add All starts native
deck-row animations after inserting and sorting the whole package.

The enabled package-folder dropdown can move saved packages between Parasitic,
Factions, Custom and user-created groups. **Add New** beside the destination selector
creates a named category/package folder and selects it while retaining the editor
draft. **Add Folder** in either filter's Edit Controls also creates empty category/package
folders without importing a named deck.
Their `.folder` edits keep empty folders available
after reload. The Packages entry is an independent browser: it initially inherits
Cards' Standard/Wild format, with no expansion or group restriction. Its direct
format toggle avoids opening the expansion menu. Native class groups and recipe
banner colors remain visible, with names sorted
within each class. Custom packages derive their class from their members. The
expansion submenu uses its existing Standard/Wild choices without a second toggle;
the edited deck's format and
class legality always apply to every member. Its independent expansion selection
also checks every member, with logic available only in that submenu. Native legal
counterparts resolve reprints without rewriting saved member IDs. Open packages
show their complete contents. Back returns to the package overview, then to Cards
with its saved filters, expansion operators and page anchor. Starting or ending
deck editing refreshes a compatible open package on the same page without a book
flip; query changes close the folder in the existing result refresh.

An unchanged original starter file is upgraded with a backup. An edited user
catalog remains a full replacement and is never silently merged or overwritten;
use the new bundled catalog as a starting point when updating such a file.
Built-in packages preserve their source identity and per-card quantities. They appear
as folders under Packages → Parasitic or Factions, alongside saved Custom packages.
Opening a folder shows its members through the existing package UI; no standalone
Packages category or individual package-card results are added. Package membership
does not count as a card-role assignment for Uncategorized. Factions are peer
packages rather than keyword filters. Combo destinations exclude Uncategorized,
Packages and Combos; Categories → Combos selects saved non-package combos.
