# Efficiency audit — November 2, 2026

This project-wide static review covered 109 production C# files and 29,723 lines,
plus targeted call-path analysis, build/catalog tools, existing performance tests,
synthetic measurements, and the supplied game logs. The findings and source links
below describe the original audit snapshot. The subsequent implementation is
recorded at the end; existing hitbox corrections are preserved.

Priorities reflect frequency, data volume, and avoidable work. They are not a
ranking measured in a running Unity profiler. P1 means investigate first, P2 next,
and P3 identifies smaller individual costs.

| Priority | Area | Trigger or avoidable work |
| --- | --- | --- |
| P1 | 1. Settings clipping | Recalculation while idle and duplicate work while scrolling |
| P1 | 2. Mesh clipping | Temporary lists/arrays per triangle despite reusing the output mesh |
| P1 | 3. Combo members | Complete collection scan for each requested card ID |
| P1 | 4. Preview text | Rebuilding the same text twice per frame |
| P1 | 5. Split/stack cards | Repeated hierarchy scans for each visible mixed card |
| P2 | 6. Popup geometry and filter labels | Measuring unchanged surfaces repeatedly per frame |
| P2 | 7. Input raycasts | New camera/hit arrays and queries through every active camera |
| P2 | 8. Combo artwork rotation | Rebuilding the actor every three seconds |
| P2 | 9. Artwork catalog cache | Unbounded retained member combinations |
| P2 | 10. Artwork search | Callers consume multiple budgeted steps synchronously |
| P2 | 11. Settings changes | Unrelated changes trigger the same collection refresh |
| P2 | 12. Native page mapping | Searching every page when the current page does not match |
| P2 | 13. Closed filter menu | Traversing/deactivating the input catcher every frame |
| P2 | 14. Category startup | Parsing the unchanged embedded catalog again |
| P3 | 15. Small allocations and reflection | Repeated snapshots, layout objects, method lookups, and bitset copies |

## 1. Settings clipping repeats while idle and twice while scrolling — P1

Evidence: [ModsSettingsMenu.cs:1079](ModsSettingsMenu.cs#L1079),
[ModsSettingsMenu.Scroll.cs:82](ModsSettingsMenu.Scroll.cs#L82),
[ModsSettingsMenu.Clip.cs:90](ModsSettingsMenu.Clip.cs#L90),
[SettingsTextClip.cs:37](SettingsTextClip.cs#L37).

With scrollable mod settings open, LateUpdate runs the full UpdateSettingsClipping
path every frame: restore previous clipping, find renderers/text, and cut edge
geometry again. Direct-rendered edge text may also be rendered into a texture;
skinned meshes may run BakeMesh. ApplySettingsScroll has already performed the
same work after a scroll change before LateUpdate repeats it.

Recommendation: mark clipping dirty and update once after layout, scrolling,
text/mesh changes, or camera changes. Reuse unchanged clipped surfaces while still
invalidating them for late glyphs and native animation.

## 2. The mesh clipper allocates many temporary objects — P1

Evidence: [FilterTrayGeometry.cs:344](FilterTrayGeometry.cs#L344).

ClipMesh allocates input/output arrays, UV lists, and index lists per invocation.
The triangle loop adds a polygon list, up to four edge-clipping lists, and temporary
triangle arrays. The optional result parameter reuses the Unity mesh but not these
working buffers.

Recommendation: reuse two polygon buffers and output/UV/index buffers, retain
capacities, and append three indices directly instead of allocating triangle arrays.
This complements reducing how often clipping runs in item 1.

## 3. Combo members repeatedly scan the whole collection — P1

Evidence: [ComboCollectionCards.Deck.cs:42](ComboCollectionCards.Deck.cs#L42),
[ComboCollectionCards.Deck.cs:69](ComboCollectionCards.Deck.cs#L69),
[ComboCollectionCards.Input.cs:27](ComboCollectionCards.Input.cs#L27).

ComponentCards visits every supplied collection card for one ID. CommonPremium,
ManaCost, LockVariants, and package preparation repeat this for several members.
Even when the card list is read once, K members and N cards require O(K × N) work.
Older-edition selection also searches the scope linearly through s_componentScope.Exists.

Recommendation: index exact and normalized IDs per collection generation and use
a HashSet for scope membership. Apply edition, quality, and format rules to the
small candidate list. Inventory/metadata changes must invalidate the index.

## 4. Package previews rebuild unchanged text each frame — P1

Evidence: [ComboPopup.cs:1079](ComboPopup.cs#L1079),
[ComboPopup.Packages.cs:102](ComboPopup.Packages.cs#L102),
[ComboPopup.cs:865](ComboPopup.cs#L865),
[ComboPopup.Cards.cs:262](ComboPopup.Cards.cs#L262).

LateUpdate calls UpdateArtworkPreview. The unchanged artwork caption passes through
PlaceEditorText, which sets "Ag", calls UpdateNow(true), sets the real text, and
calls UpdateNow(true) again. Wrapping, measurements, and transform writes add work.
The member view also fits previews with three passes every frame, even for an
unchanged actor.

Recommendation: cache text layout by caption, target rectangle, font, and scale.
Refit previews on selection/window changes or genuinely new geometry. Skip stable
frames after loading completes.

## 5. Split/stack cards scan their hierarchies each frame — P1

Evidence: [MixedPremiumCard.cs:248](MixedPremiumCard.cs#L248),
[MixedPremiumCard.cs:529](MixedPremiumCard.cs#L529),
[MixedPremiumCard.cs:547](MixedPremiumCard.cs#L547),
[MixedPremiumCard.cs:613](MixedPremiumCard.cs#L613).

Each visible mixed card calls ConfigureStack or ClipActors per LateUpdate.
Helpers repeatedly collect transforms, renderers, text, projected shadows, mesh
filters, and skinned renderers for both actors. SuppressObject traverses some
subtrees again. Multiple mixed cards multiply the work and allocated result arrays.

ClipMesh already guards the assigned clipped mesh: unchanged cards are not
necessarily retriangulated every frame. The avoidable work is primarily searching
and resetting state.

Recommendation: capture components when binding and update them on actor, mesh,
or state changes. Preserve transform tracking and repair paths for native reactivation.

## 6. Popup geometry and filter labels are measured too often — P2

Evidence: [ComboPopup.cs:1083](ComboPopup.cs#L1083),
[ComboSceneGeometry.cs:68](ComboSceneGeometry.cs#L68),
[ComboSceneGeometry.cs:90](ComboSceneGeometry.cs#L90),
[ComboEditorOptions.cs:157](ComboEditorOptions.cs#L157),
[CounterTextLayout.cs:285](CounterTextLayout.cs#L285),
[KeywordSetTray.Grid.cs:157](KeywordSetTray.Grid.cs#L157).

Editor LateUpdate positions each label against the same paper surface. Helpers
repeatedly collect its renderers and reread materials/geometry. Open dropdowns
measure the dialog too. Each active filter caption independently recenters in
LateUpdate, rediscovering renderers, glyphs, and camera references.

Recommendation: share surface geometry and drawing priorities per frame or layout
generation. Retain renderer lists and camera references with controlled invalidation.
Realign captions only when their position, glyphs, or font change.

## 7. Modal input repeatedly allocates raycast and camera arrays — P2

Evidence: [KeywordSetTray.Input.cs:25](KeywordSetTray.Input.cs#L25),
[KeywordSetTray.Back.cs:38](KeywordSetTray.Back.cs#L38).

While the filter menu is visible, Camera.allCameras and Physics.RaycastAll query
every active camera, checking layers only afterward. Pinned controls can query the
same cameras again, even with an unchanged pointer.

Recommendation: limit relevant UI cameras and physics masks, share hits per frame,
and use reusable nonallocating buffers. Handle overflow. Camera/scene movement and
new colliders still require checks without pointer movement; preserve modality.

## 8. Automatic combo artwork rotation rebuilds actors — P2

Evidence: [ComboCollectionCards.Native.cs:29](ComboCollectionCards.Native.cs#L29),
[ComboCollectionCards.Native.cs:326](ComboCollectionCards.Native.cs#L326),
[ComboCollectionCards.Native.cs:273](ComboCollectionCards.Native.cs#L273).

Visible multi-card combos instantiate and configure a new actor every three seconds,
bind it to a new container, then destroy the old actor. Similar switch times can
make several tiles perform this work in the same frame.

Recommendation: stagger switches first, then investigate portrait/definition changes
without rebuilding the same template, or a small bounded actor pool. Retain native
show/hide, portrait, and ownership checks.

## 9. Artwork catalogs grow for the process lifetime — P2

Evidence: [PackageArtwork.cs:16](PackageArtwork.cs#L16),
[PackageArtwork.cs:101](PackageArtwork.cs#L101).

s_catalogs retains a catalog, dictionaries, search sets, and queues for each sorted
member combination without an eviction path. Editing members keeps obsolete
combinations alive. This is a cache lifetime issue, not evidence of a GPU/texture leak.

Recommendation: bound the cache or its collection-session lifetime. Open editors
may retain their catalog. Assess the separate definition cache independently.

## 10. Budgeted artwork search can still block a frame — P2

Evidence: [PackageArtwork.cs:69](PackageArtwork.cs#L69),
[PackageArtwork.cs:108](PackageArtwork.cs#L108),
[ComboPopup.Packages.cs:18](ComboPopup.Packages.cs#L18).

Catalog.Advance budgets roughly four milliseconds or 100 entries per step, but
ResolveFor and SearchArtwork call it in synchronous while loops for saved token
artwork. A late or missing selection can exhaust the catalog in one invocation.

Recommendation: enforce a shared calling-frame budget and resume next frame.
Keep the existing preview or a pending state until the selection is resolved.

## 11. Settings changes invalidate more than necessary — P2

Evidence: [BetterCollectionManagerMod.cs:66](BetterCollectionManagerMod.cs#L66),
[CollectionQualitySettings.cs:277](CollectionQualitySettings.cs#L277),
[QualityDeckInput.cs:282](QualityDeckInput.cs#L282),
[QualityDeckInput.cs:319](QualityDeckInput.cs#L319).

Most setters send the same Changed event. Input/menu-only changes therefore mark
the collection and filter layout for refresh. Each saved change also synchronously
writes MelonPreferences and JSON, including remembered filter-mode switches.

Recommendation: distinguish card display, input, filter logic, and menu layout.
Refresh only affected behavior and batch successive saves while preserving promised
persistence and saving on close/exit.

## 12. Fallback page mapping can search every page — P2

Evidence: [ComboCollectionCards.Native.cs:205](ComboCollectionCards.Native.cs#L205).

NativePageSlots checks the current page first. A sequence mismatch, such as during
a page turn, then examines all other pages with reflection and temporary slot lists.
Cost grows with collection page count. This affects the fallback, not a successful
direct match.

Recommendation: check current, known destination, and neighboring pages first.
Cache validated mappings per result generation while preserving full sequence
validation so ordinary cards are never bound as combos.

## 13. The input catcher is processed while the menu is closed — P2

Evidence: [KeywordFilterUi.cs:89](KeywordFilterUi.cs#L89),
[KeywordSetTray.Input.cs:13](KeywordSetTray.Input.cs#L13),
[KeywordSetTray.Input.cs:294](KeywordSetTray.Input.cs#L294).

The global collection update calls PollOutsideClick for a closed menu. If s_hideArea
exists, the closed path calls SetKeywordHideAreaActive(false), recollects colliders,
and reapplies the already inactive state before the early !s_shown return.

Recommendation: return immediately once fully closed and cleaned up. Limit recovery
from unexpected native Hide/Disable to state transitions or targeted integrity checks.

## 14. The unchanged embedded catalog is parsed on every entry — P2

Evidence: [KeywordFilterUi.FilterChanges.cs:11](KeywordFilterUi.FilterChanges.cs#L11),
[CategoryCatalog.cs:81](CategoryCatalog.cs#L81),
[CategoryCatalog.cs:110](CategoryCatalog.cs#L110).

LoadBaseUserFile always parses embedded defaults before loading an existing user
file. The roughly 549 KB embedded catalog never changes within a loaded mod version,
but each collection entry repeats parsing/object work on the UI path.

Recommendation: cache immutable parsed defaults. Continue rereading the user file
on collection entry to retain external edits, fallback behavior, and migration.

## 15. Smaller recurring allocations and reflection — P3

- [ComboCollectionCards.Native.cs:334](ComboCollectionCards.Native.cs#L334): a new
  owned-actor snapshot list is allocated before checking whether any rotation is due.
  Reuse a snapshot while retaining protection against dictionary mutation.
- [ComboPopup.cs:1100](ComboPopup.cs#L1100) and
  [ComboInspectLayout.cs:15](ComboInspectLayout.cs#L15): Arrange allocates a page
  object and rectangle array with unchanged window size, page, and card count.
  Cache by those inputs.
- [CounterTextLayout.cs:84](CounterTextLayout.cs#L84): GlyphRenderer and
  RemoveClonedGlyphs repeatedly find GetTextMeshRenderer through GetMethod.
  Cache the method/accessor per runtime type, but still detect renderer replacement.
- [FilterAvailability.cs:138](FilterAvailability.cs#L138): Not() allocates a word
  array per negative candidate. Direct negated intersection or a per-generation
  cache avoids copying while retaining the fast bitset path.

## Measurements and limits

Local measurements use .NET 8 and managed Unity/game doubles. They establish call
counts and allocation patterns, not Unity/Mono frame times, GPU times, or promised FPS.

| Check | Result |
| --- | --- |
| ClipMesh, synthetic 1,024-triangle mesh, 100 runs, reused output mesh | About 8.0 MB managed allocation per run; about 547 ms total |
| ComponentCards, prepared list of 10,000 cards | Confirmed: 10,000 cards visited for one requested ID |
| ComboInspectLayout.Arrange, four visible cards | 168 bytes per call; small individual cost |
| Negated bitset, 10,000 cards | 1,312 bytes per Not() |
| Existing filter benchmark, 12 changes with 4,096 cards each | Scalar: 41.3 ms / 4,249 KiB; indexed: 7.4 ms / 139 KiB, no repeated definition access |
| Existing expansion catalog benchmark | Full rebuild: 233.9 ms; existing index: 26.8 ms, no repeated definition access |
| Filter regression suite with benchmark enabled | 156,568 checks passed |

The clipping mesh is a synthetic stress case, not the actual settings mesh size.
Unity doubles can influence results. ComponentCards timing/allocation values are
not transferred to the game because stubbed card properties introduce extra work;
the verified full-scan count is the reliable finding.

Older game logs contain 145 timed slow filter refreshes. The slowest totals
502.8 ms: 354.9 ms in contents and 146.8 ms in native filtering. Only slow refreshes
were timed, and these are older builds. They are neither average action costs nor
proof that one finding caused a delay. Profile those two phases first in the game.

## Already bounded or lower-priority areas

- Filters already use reusable capture buffers, inverted indexes, computed result
  sets, and separate scope invalidation. Existing benchmarks support this design.
- ModsSettingsUi recalculates button layout only for pending layout/window changes.
  CustomFrameRate writes native values only when they differ.
- Package tiles retain fits to unchanged collection slots. Portrait mapping and
  split meshes check existing assignments before rebuilding; preserve those guards.
- Darkmoon/hub experiments are disabled at the mod entry point and do not explain
  current ongoing update load.
- Catalog/archive tools run outside the game; their maintenance work has lower
  priority than frame paths.
- Popup hitbox adjustment runs from layout rather than an independent permanent
  update and does not change visible elements.

## Recommended order

Avoid duplicate/unchanged clipping and reuse its working buffers first. Then index
card IDs and avoid rebuilding stable preview text. Next cache actor/renderer
references, stagger rotation, and bound catalog lifetimes. For every change, check
late glyphs, page turns, native hover states, clipping, and visible/hitbox alignment.

Raw measurements, the harness, and file inventory remain local under
`artifacts/efficiency-audit/` and are excluded from distribution packages.

## Implementation on November 2, 2026

All 15 findings were addressed without changing visible sizes or positions. Native
loading, hover, page transition, and input paths retain checks for new geometry
and changed state.

| Items | Change |
| --- | --- |
| 1 | Settings clipping runs once in settings LateUpdate after scroll/layout. Stable surfaces and text captures reuse geometry/camera/text data; moving skinned meshes remain live. |
| 2 | Reusable input, polygon, UV, index, and output buffers; homogeneous edge distances computed once per input vertex. |
| 3 | Exact editions use the native card/quality index. Older editions use a normalized index and scope HashSet, invalidated by native results changes. |
| 4 | Text layout cached by content, font, rectangle, transform, and glyphs. Stable previews return before further fit passes. |
| 5 | Split/stack cards retain component lists and check structure, native state, and replacements before recapturing. |
| 6 | Paper/frame measurements shared within an editor pass. Filter labels skip recentering when geometry/glyph/camera inputs are stable. |
| 7 | Reusable camera/raycast buffers, culling masks, and shared hits per input pass. Full buffers grow and are queried again. |
| 8 | At most one due actor rotation per update. Combos with up to three members retain at most two inactive private actors; larger combos retain no extra inactive models. Release/error rollback disposes private actors. |
| 9 | At most 32 member catalogs; leaving the collection clears caches. An open editor may retain an evicted instance. |
| 10 | Saved token artwork resolves in bounded steps. Popup/tile retains the selection while pending and resumes later. |
| 11 | Separate display, input, and filter layout/results change categories. Native preference saves batch per frame and flush on exit; JSON backup remains immediately persistent. |
| 12 | Current page, validated hint, and neighbors checked before full fallback. Hints are bounded, revalidated, and discarded when results change. |
| 13 | Fully disabled input catchers are not traversed while closed. Unexpected Hide/Disable still triggers recovery. |
| 14 | Embedded defaults parsed once; user files still read on collection entry. |
| 15 | Only due rotations take reusable snapshots; inspect pages cached per popup; glyph method lookups cached per type; negative candidates avoid bitset copying. |

For the same synthetic 1,024-triangle case, managed allocation fell from
**8,001,635 to 749,976 bytes per run**, approximately **91% less**. Remaining
allocations include copying and helper work in Unity doubles. Cached inspect pages
and direct negative intersection each allocate 0 bytes per call in the local run.
Exact and warmed older editions perform no complete inventory scans across 1,000 lookups.

New regression checks cover stable text captures, late glyphs, more than 64 overlapping
colliders, actor reuse/release/rollback, index invalidation, bounded catalogs,
incremental token search, and separate settings events. Actual Unity/Mono frame
times and rendering still require an in-game check; local figures are not FPS data.
Post-change measurements remain local in `artifacts/efficiency-audit/microbenchmarks-after.txt`.
