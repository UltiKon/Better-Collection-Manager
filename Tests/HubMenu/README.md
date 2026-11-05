# Hub menu checks

```sh
dotnet run --project Tests/HubMenu/HubMenu.csproj
dotnet run --project Tests/NativeApi/NativeApi.csproj -- /path/to/Assembly-CSharp.dll
```

The first command compiles the production hub hooks, source resolver and cache against managed
scene/input doubles. It covers suppressing the animated DFT entry load, native
ready callbacks, current offer IDs, reconnect, visibility and input guards,
repeated show/hide, source-caption ownership, Brawl routing, native BG downloads
and tutorial routing, retained Modes visual states, missing-state fallbacks,
teardown, configuration switches and restoration after a failed replacement.
Regression cases include the live client's unwired sibling caption, source text
still faded out during loading, alternate templates, and Back moving from an
offscreen loading pose into view with changing artwork fades and render layers.
The ready-callback regression reproduces Hub's actual
`GetWidget().RegisterEventListener(...)` call, checks the empty widget's reuse
and ownership, delivers `LUCKY_DRAW_OPENED`, and removes the listener on teardown.
Caption regressions cover source RTT surfaces, cached glyphs and reveal scripts:
the static label must have a fresh direct renderer, preserve localized native
typography and source state, and recover from a delayed hide while visible.
Automatic artwork checks use the production source resolver and immutable
selection/request objects: native desktop/mobile custom references, redirected
addresses with an unchanged offer ID, current Pet icons, asynchronous HeroSkin
portrait resolution, release of the definition handle, stable cache keys and
invalidation for new offers/items, addresses and client builds. Late results
are rejected after a newer selection or teardown. Cold library resolution waits
for asynchronous availability and then rediscovers the current device reference.
The real RGBA cache is checked
for exact color/alpha preservation, format version, dimension/length limits,
truncated data and complete file replacement. GPU extraction and the runtime
render/asset-loader integration require the game; the fixtures do not simulate
those native graphics calls.

The second command verifies actual field and method signatures in the supplied
client assembly without executing it. The fixtures do not run Unity or Harmony
detours and cannot establish that the old BG textures are present, validate
button rendering, or measure the startup stall. Build with the actual installed
game/loader references and compare fresh game starts as described in the
[technical reference](../../TECHNICAL-REFERENCE.md#experimental-hub-menu-and-static-darkmoon-entry).
