# Feasibility: main-menu Tavern Brawl and Darkmoon animations

Inspected on 2026-11-01 using ILSpy and the available `Assembly-CSharp.dll`.
SHA-256: `2408c5eae3d5ab083b8b290a7278147159e79a3c6e72e1d922ef44028ed16afb`.
This is a historical static feasibility review. An experiment was subsequently
implemented; see "Experimental hub menu and static Darkmoon entry" in README.md.
Those experiments are currently disabled at the mod entry point. No in-game test
or performance profile was performed for this review; API findings apply to this DLL.

The requested target was DFT's animated appearance during startup and the resulting
multi-second hub stall. The draw animation is outside this fix's scope.

| Request | Assessment | Main work |
| --- | --- | --- |
| Replace main-menu Battlegrounds with Tavern Brawl | Feasible | Update caption, click handler, availability, and tooltip together |
| Swap Battlegrounds into the Modes menu | Feasible with more work | Extend mode list, visuals, and Battlegrounds entry point |
| Avoid animated DFT appearance during startup | Specific interception points exist | Intercept loading before instantiation and preserve a static entry |
| Eliminate the multi-second startup stall | Not established | Suppress the suspected loading path experimentally and compare startup times |

## Tavern Brawl instead of Battlegrounds

`Box.m_BattleGroundsButton` is a `BoxScrollButton`. `Box.SetupButtonText()` assigns
its caption, and `Box.OnButtonPressed(UIEvent)` routes by the actual button instance
to `OnBattleGroundsButtonPressed()`. Changing only `m_buttonType` is insufficient.

Public `Box.OnTavernBrawlButtonPressed(UIEvent)` remains available. It checks login,
`TavernBrawlManager.CanEnterStandardTavernBrawl()`, SceneMgr, and open dialogs before
entering `SceneMgr.Mode.TAVERN_BRAWL` and playing its greeting. Harmony can reuse it.

Additional changes are required:

- `Box.UpdateBoxButtons()`: the slot currently uses Battlegrounds unlock/download
  state; Brawl needs its own availability rules.
- `Box.OnButtonMouseOver()`: tooltip selection identifies the Battlegrounds instance.
- `BoxScrollButton`: IsFeatureActive, input, download icons, and hover still use the
  Battlegrounds feature. `m_moduleTag` is established in Awake; changing only the
  feature field later does not fully switch behavior.
- Tutorials, navigation, and any new badge must match the destination. Preserve
  locks during transitions.

For a full swap, `GameModeDisplay.InitializeGameModeSceneData()` builds the list
from active GameModeDbfRecord entries and GameModeButtonDataModel objects. The DLL
alone cannot establish which Battlegrounds records and artwork are available at
runtime. A custom entry can reuse the existing handler while preserving its
login/download/tutorial paths. Swapping only text and LinkedScene on a Brawl tile
would leave its other data inconsistent.

Battlegrounds originally appeared in Modes, as documented in
[Blizzard's announcement](https://hearthstone.blizzard.com/en-us/news/23156373/introducing-hearthstone-battlegrounds).
Its old tile may therefore have reusable assets. This review did not establish
that they are missing: available client DLLs do not include installed CASC game
data or extracted UI prefabs. Logic and data models cannot prove that historic
artwork is still shipped.

Before rebuilding the tile, inspect the installed client's Battlegrounds record
in `GameDbf.GameMode`, event state, GameModeButtonState, and available widget-state
artwork. A record alone does not prove that the old tile works.

## Darkmoon Faire Treasures

The client names the feature LuckyDraw. When entering the hub, Box calls
LuckyDrawButtonController.Show(), which instantiates its button prefab through
AssetLoader and displays the widget.

LuckyDrawButton.OnLoadCustomButton(string assetId) also loads offer-specific
ENTRY_BUTTON artwork into m_buttonVisualContainer. It can include its own Clickable.
These provide two separate targets: the base button prefab and the offer artwork.

To prevent loading stalls, intercept expensive visuals before loading or
instantiation. Stopping an Animator or hiding an already loaded object does not
remove incurred loading, instantiation, or shader work. The actual cause of the
stall has not been measured.

A first experiment can suppress OnLoadCustomButton artwork and compare the pause.
If the base prefab itself causes the pause, replace it earlier at Show(). A static
button can retain access without constructing the original. LuckyDrawButton.ShowLuckyDraw()
uses the luckydraw deep link. Replacing the whole original requires preserving
login, preferred offer ID, and availability through manager logic; calling
Box.OnLuckyDrawButtonPressed() without an initialized original is insufficient.

## Required in-game checks

- Reopen hub and Modes repeatedly; reach both modes and check tooltips, locked
  modes, downloads, and active transitions.
- Compare fresh launches with and without DFT visuals, then test returning to
  the hub and receiving new offers.
- Test static DFT access, login, locked/expired offers, and opening an offer.
  The draw animation remains outside this fix.

The review establishes useful patch points. Final appearance and measurable stall
improvements remain unverified until game testing.

## Historical experiment status

- StaticDarkmoonButton suppresses the animated hub prefab before loading and
  retains access through a static native button.
- TavernBrawlInMainMenu replaces the hub slot and looks for the old Battlegrounds
  widget state. Without a matching loaded state, it uses a native text button in
  Modes. Logs distinguish the paths.
- Settings are in MelonPreferences category BetterCollectionManagerHub and
  take effect after restarting. Current entry-point code disables these experiments.
- At that snapshot, 78 managed behavior checks and 93 metadata API checks passed.
  Managed checks do not run Unity or Harmony detours.
- The initial full build attempt lacked Unity/Blizzard and loader dependencies.
  That historical failure did not produce an installable DLL. Later builds use
  local game/loader references through `./build-linux.sh`.

An earlier runtime log showed a DFT fallback because the caption template was not
wired. Resolution was extended to the native sibling ButtonName. The replacement
Battlegrounds button was reported invisible; it now follows the Back button's
position/fade and logs camera, screen rectangle, and visible renderers. Whether
that fixes the observed appearance still requires an in-game check.
