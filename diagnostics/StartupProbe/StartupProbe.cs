using MelonLoader;

[assembly: MelonInfo(typeof(BCM.Diagnostics.StartupProbe), "BCM Startup Probe", "0.1.0", "Better Collection Manager")]
[assembly: HarmonyDontPatchAll]

namespace BCM.Diagnostics
{
    // No Unity/game references, collection state, preferences or native patches.
    public sealed class StartupProbe : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("BCM_STARTUP_PROBE_OK: mod initialization reached; collection hooks are absent.");
        }
    }
}
