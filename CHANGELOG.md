# Changelog

Changes are grouped by UTC commit date. The [user guide](docs/USER-GUIDE.md) and
[technical reference](docs/TECHNICAL-REFERENCE.md) document the current behavior;
this file records the features and fixes delivered on each
date. Superseded intermediate layouts and experiments are not separate features.

## 0.1.0 — 2026-11-05

- Fixed release verification on machines with only a newer .NET runtime, such
  as .NET 10. The net8.0 verification tool can now use the installed newer runtime;
  the mod DLLs still target .NET Framework 4.7.2.

- Added a shared release command that builds and verifies both loader variants
  before packaging either one, retaining version 0.1.0.

- Organized the repository into `src/`, `docs/`, `tests/`, `tools/`, `data/`,
  `config/`, `installer/` and `diagnostics/`. Build outputs now go under
  `artifacts/`; project references, CI, documentation and release packaging use
  the same layout.

- Added Firestone mod-list metadata in the BepInEx CFG's General section. The
  plugin GUID and CFG filename now match `BetterCollectionManager.dll`; the
  previous lowercase CFG is imported when no current config exists.
- Reworked the English README around an abstract and installation steps, added
  a user guide and release guide, and preserved implementation details in a
  separate technical reference.
- Added clean-checkout CI, self-contained installer test payloads, documentation
  link checks and release-packaging regressions. Packaged READMEs now resolve
  source documentation links correctly; archive names use the central version.

- Renamed the project, namespaces, DLLs, loader entries and release archives to
  **Better Collection Manager**. New installs use the supplied CFG's preferences
  as compiled defaults. Existing settings take precedence, with first-launch
  import from the previous name. Updated MIT attribution to UltiKon, Imik and
  contributors.

- Added a separate BepInEx 5 Unity Mono plugin, build project and release ZIP
  layout. Both loaders share collection initialization and behavior; preference,
  logging and coroutine adapters use the selected loader. BepInEx uses its own
  CFG, with optional first-launch import of the existing JSON settings backups.
  Compilation and regression checks do not replace an in-game runtime test.
- Added the MIT License for the mod, installer and original project code,
  including its warranty and liability disclaimer. Both distribution archives
  include the license; third-party license notices remain separate.
- Fixed Packages jumping to page 1 when switching Standard/Wild. The pinned
  toggle and expansion format choices now follow the first surviving visible
  folder or nearest previous neighbor, using the saved overview position when
  leaving an open package.
- Fixed missing package hover highlights for unowned and newly acquired carrier
  cards by following the collection slot's mouse interaction. Leaving a slot now
  clears its glow even when the carrier retains a stale hover state. Regression
  checks cover all three cases.
- Renamed the automated installation archive to **BetterCollectionManager-0.1.0-installer.zip**.
- Restored the Linux build script's home-relative Battle.net installation default;
  `GAME_DIR` remains available for other locations without storing a private path.
- Added a saved **Disenchant popup** toggle under Crafting with an experimental
  warning. Turning it off restores native disenchant behavior and invalidates
  pending custom confirmations. The dependent **Never dust Signatures/Diamonds**
  checkbox becomes redundant (hidden or disabled), preserving its saved value.
- Fixed package hover feedback when the native selection glow is an inactive
  container with nested geometry. The first hover now activates the private
  effect hierarchy and its renderers; later native renderer resets are repaired.
  The shared recipe prefab remains unchanged.
- Added regression coverage for cold package glow hierarchies and renderer resets.
- Translated distribution/setup documentation, installer prompts and errors,
  diagnostic output, generated test plans, and historical audit notes into English.

## 2026-10-27

Commit: `ab79fb0` — Add configurable collection quality and deck input settings.

### Collection display and deck building

- Added four configurable ways to display a card when its highest owned quality
  has only one copy and lower qualities are also owned: Highest quality only,
  Split card, Two highest qualities, and Prefer two owned copies. Dropdown order
  follows that list; Split card remains the default.
- Prefer two owned copies displays the highest quality with two owned copies,
  falling back to the highest owned quality if no pair exists. Legendary cards
  keep their highest quality.
- Split card merges only compatible Normal/Golden frames. Incompatible frames,
  including Signature and Diamond, retain separate tiles when a second version
  is needed. A failed split renderer also falls back to separate tiles.
- Added click policies: Clicked half, Quality order, and Highest fully available.
  Highest fully available prefers the highest owned/trial playset, rather than
  the lowest quality. Both additions can use the same pair; exhausted or illegal
  variants are skipped, with normal quality-order fallback if no playset is usable.
- Added drag policies in this order: Dragged half when available, Choose after
  dropping, and Same as click. Dragged half is independent of the click policy.
- Restricted Half choices to Split card. A saved drag-half preference uses Same
  as click outside Split and is restored on returning to Split.
- Same as click inherits the effective click policy and uses the grabbed half
  when the click policy selects a half.
- Choose after dropping cancels native insertion before showing the available
  qualities. Selection adds the chosen version exactly once; Cancel, Escape,
  clicking outside, or an invalidated deck context adds nothing. A single usable
  variant is added without an extra dialog.
- Fixed drag settings having no effect by capturing native pickup events and
  handling release before native deck insertion, with an update-loop fallback.
- Quality fallback uses actual owned/trial inventory minus deck and sideboard
  usage, respects the active collection result and destination, and keeps native
  copy limits, bans, playability checks, and deck validation.
- Suppressed the per-quality exhausted-copy frame for merged tiles with multiple
  owned/trial copies while preserving other native locks. Separate quality tiles
  retain their native availability frames and directly selected quality, including
  incompatible-frame fallback in Split mode.
- Kept quality routing limited to mouse gestures adding collection cards during
  deck editing. Browsing, crafting, touch, cosmetic selection, imports, and
  moving existing deck tiles retain their native behavior.
- Fixed missing white hover glow when leaving a split card and entering its
  opposite half: actor selection now transfers the current interaction state
  instead of taking the new half's stale idle state.

### Mods settings and shared UI

- Replaced the empty Mods submenu with native English dropdowns headed
  Only one copy at highest quality, Add to deck by clicking, and Add to deck by
  dragging.
- Removed the unnecessary Back button. Escape first closes an open dropdown,
  then returns to Settings; clicking outside also returns to Settings.
- Removed inherited Signature Appearance headings and favorite/help text from
  cloned controls. Fresh captions use native Graphics typography without stale
  glyph meshes, word wrapping, or overlapping words.
- Matched the heading-to-dropdown gap to Signature Card Appearance. Corrected
  row order, compact spacing, menu width around complete metal trim and arrows,
  side padding, and bottom padding. Layout adapts to resolution and visible rows.
- Added the persistent Hide redundant settings checkbox at the bottom, enabled
  by default. Highest quality only hides click settings and uses Quality order;
  Prefer two owned copies hides click settings and uses Highest fully available;
  Two highest qualities hides both input settings.
- Unchecking Hide redundant settings shows all dropdowns and allows manual click
  overrides in Highest quality only and Prefer two owned copies. Hidden settings
  preserve manual preferences, and Half options remain restricted to Split.
- Fixed accumulated modal input claims from repeated menu showing. Settings
  changes and layout updates reuse the open menu instead of reopening it; closing
  or destroying a menu releases its own claim once.
- Restored live collection refresh while Settings, Mods, or a dropdown remains
  open. Callbacks queue changes for the update loop and coalesce them into one
  refresh. Refresh waits during a drag or crafting.
- Fixed selected dropdown rows using unreadable dark text on dark selection
  backgrounds. The shared native DropdownMenuItem now uses white selected text,
  including after hover-out, throughout the game rather than only in Mods.
- Persisted preferences in MelonPreferences and
  `Mods/BetterCollectionManager/quality_settings.json`. Added legacy-value
  migration and defaults for missing or invalid values.
- Added UnityEngine.TextRenderingModule references and Linux dependency checks
  for native text layout.

Validation: successful Linux net472 build against game reference assemblies and
2,183 managed quality, input, geometry, UI, and lifecycle checks. These checks
do not exercise live Unity rendering or Harmony integration inside Hearthstone.

## 2026-10-26

Commits: `00d7336` — Add collection filter menu with pinned back navigation;
`badb63b` — Improve collection filters, premium tiles, and Mods settings.

### Initial repository snapshot

- Captured the preferred-quality collection mod, build projects, Linux build
  script, installation instructions, and shared filter-menu foundation.
- Collection entries are collapsed before page bucketing in preferred quality
  order: Signature, Diamond, Golden, Normal. Deck editing remains supported;
  crafting and explicit premium searches retain their native views.
- The existing expansion-selection button opens the Filters menu with
  Expansions, Keywords, Card Type, Minion Type, Spell School, Rarity, and Preferred
  Quality. Expansion selection retains native game callbacks.

### Filter matching and navigation fixes

- Preserved all underlying tag variants when localized keyword names are merged,
  preventing cards from disappearing because only one alias was retained.
- Populated Has Keyword and Referenced Keyword independently, offering only
  entries with matching Constructed cards in the selected set/format. Removed
  empty reference choices such as Tourist when no card references that keyword.
- Corrected Prepare classification: cards whose own Prepare is encoded as a
  reference plus deck-action cost count as Has Keyword. Effect references such
  as Wanted Poster and Jailbird remain references.
- Preserved active filter selections when navigating Back, marked active filter
  categories, and removed selections no longer available after set changes.
- Fixed the pinned Back button's size, position, hit area, clipping, shadow, and
  input priority over rows behind it. Prevented the same release from selecting
  an underlying filter or expansion.
- Kept the first heading below Back, preserved native tray width and bottom
  clipping, and fixed prematurely hidden edge rows and blank areas while
  scrolling Keywords and Expansions.
- Restored native clipping and row positions on leaving submenus, including the
  dark strip that covered part of the bottom row.

### Split cards, ownership counters, and previews

- Merged eligible Normal/Golden Common, Rare, and Epic pairs only when the
  previous two-copy rule would otherwise show two versions. Two copies of the
  preferred quality retain a complete preferred tile.
- Settled on a horizontal split at the name banner: Golden artwork above,
  Normal below, with the complete Golden name banner retained across the seam.
- Kept native text intact and removed duplicate labels and decorations: Golden
  supplies name and mana cost; Normal supplies rules text, attack, health, and
  remaining labels.
- Aligned different prefab mesh origins, preserved alignment through state
  changes, supported both camera orientations, and added GPU-only mesh handling
  with per-card fallback to separate tiles.
- Used real actors for both halves, preserving the quality chosen at press time
  across a drag or release over the other half without altering inventory.
- Preserved a complete shared native shadow beneath both halves and suppressed
  duplicates to avoid a clipped shadow or double darkening.
- Added ordered ownership breakdowns such as `0+1+2+2`. Existing zero-count
  qualities at or below the displayed quality remain visible; nonexistent
  qualities and unowned qualities above it are omitted.
- Kept counter font size constant, used half-size vertically centered plus
  signs, and sized the frame to actual rendered glyphs with fixed side padding.
  Preserved original counter appearance for a single quality.
- Initialized the counter theme and border on first display, fixing text that
  only acquired the correct color after hover.
- Added left-click inspection while browsing cards, skins, pets, and other
  supported cosmetics. Native deck-edit actions and right-click inspection remain.

### Settings entry and validation

- Added the Mods button below Signature settings, aligned with Privacy's row and
  the Signature field's width. Preserved native hover animation and sound without
  inheriting Privacy's animated size.
- Matched Graphics' metal surround and screws while retaining the native gold
  button face. Calibrated width and centering against measured frame edges.
- Added an initially empty native Mods submenu, later populated on October 27.
- Added managed regression checks and documented build and in-game verification.

Validation recorded in `badb63b`: 40 keyword checks, 1,413 mixed-premium and click
checks, and a successful Linux net472 build against game reference assemblies.
