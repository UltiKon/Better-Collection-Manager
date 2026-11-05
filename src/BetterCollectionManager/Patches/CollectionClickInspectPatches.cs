using System;
using HarmonyLib;

namespace BetterCollectionManager.Patches
{
    [HarmonyPatch(typeof(CollectionCardVisual), "OnRelease")]
    internal static class CollectionClickInspectPatches
    {
        public static void Install(HarmonyLib.Harmony harmony)
        {
            try { harmony.PatchAll(typeof(CollectionClickInspectPatches)); }
            catch (Exception ex) { DebugLog.Exception("Collection left-click inspect patch unavailable", ex); }
        }

        // MixedPremiumPatches.ReleasePatch first retains the premium chosen on
        // press. The native preview must open that exact real actor.
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        public static bool Prefix(CollectionCardVisual __instance) => !CollectionClickInspect.TryInspect(__instance);
    }
}
