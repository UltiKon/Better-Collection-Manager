# Startup isolation

This diagnostic uses the same net472 reference package, assembly search rules
and MelonLoader `net35` reference as the Linux mod build. It has no game/Unity
assembly references and installs no Harmony patches.

Build from the repository root:

```sh
dotnet build Diagnostics/StartupProbe/StartupProbe.csproj -c Release -p:GameInstallRoot="<Hearthstone directory>"
```

Always supply your own game directory. Override `MelonDllDir` if needed.

For one startup test, move `BetterCollectionManager.dll` out of the game's
`Mods` directory and put `linux-build/startup-probe/BCM.StartupProbe.dll` there.
The expected MelonLoader message is `BCM_STARTUP_PROBE_OK`. Remove the probe after
this comparison and restore the collection mod from its backup.

The marker establishes that this minimal mod reached managed initialization;
it does not establish that the full collection mod or all loader dependencies
are compatible. If startup also fails with the probe, capture the launcher's
terminal output; a stale Unity Player.log cannot identify a pre-Unity failure.
