using System;
using HarmonyLib;

namespace BetterCollectionManager.Patches
{
    internal static class MixedPremiumPatches
    {
        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                foreach (Type patch in typeof(MixedPremiumPatches).GetNestedTypes(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    harmony.PatchAll(patch);
                MixedPremiumCard.Ready = true;
                DebugLog.Msg("Mixed collection tiles enabled (Split card or Stacking, two-version cases only)");
            }
            catch (Exception ex)
            {
                MixedPremiumCard.Ready = false;
                DebugLog.Exception("Mixed premium patches unavailable; keeping separate variants", ex);
            }
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "Show")]
        private static class ShowPatch
        {
            [HarmonyPrefix] public static void Prefix(CollectionCardVisual __instance) => ReleaseVisual(__instance);
            [HarmonyPostfix]
            public static void Postfix(CollectionCardVisual __instance)
            {
                if (ComboCollectionCards.Apply(__instance)) return;
                MixedPremiumCard.Attach(__instance);
                CollapsedPremiumCount.Attach(__instance);
            }
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "Hide")]
        private static class HidePatch
        {
            [HarmonyPrefix] public static void Prefix(CollectionCardVisual __instance) { ReleaseVisual(__instance); ComboCollectionCards.Forget(__instance); }
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "SetActors")]
        private static class SetActorsPatch
        {
            [HarmonyPrefix] public static void Prefix(CollectionCardVisual __instance) => ReleaseVisual(__instance);
            [HarmonyPostfix] public static void Postfix(CollectionCardVisual __instance) => ComboCollectionCards.Apply(__instance);
        }

        private static void ReleaseVisual(CollectionCardVisual visual)
        {
            ComboCollectionCards.RestoreArtwork(visual);
            CollapsedPremiumCount.Release(visual);
            MixedPremiumCard.Release(visual);
        }

        [HarmonyPatch(typeof(CollectionCardCount), "UpdateVisibility")]
        private static class CountVisibilityPatch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectionCardCount __instance) => CollapsedPremiumCount.Refresh(__instance);
        }

        [HarmonyPatch(typeof(CollectionCardActors), "GetPreferredActor")]
        private static class PreferredActorPatch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectionCardActors __instance, ref Actor __result)
            {
                if (MixedPremiumCard.TryGetSelected(__instance, out Actor actor)) __result = actor;
            }
        }

        [HarmonyPatch(typeof(PegUIElement), "TriggerPress")]
        private static class PressPatch
        {
            [HarmonyPrefix]
            public static void Prefix(PegUIElement __instance) => MixedPremiumCard.Choose(__instance as CollectionCardVisual, true, false);
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "OnRelease")]
        private static class ReleasePatch
        {
            [HarmonyPrefix]
            public static void Prefix(CollectionCardVisual __instance) => MixedPremiumCard.Choose(__instance, false, true);
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "OnDrag")]
        private static class DragPatch
        {
            [HarmonyPrefix]
            public static void Prefix(CollectionCardVisual __instance) => MixedPremiumCard.Choose(__instance, false, true);
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "OnRightClick")]
        private static class RightClickPatch
        {
            [HarmonyPrefix]
            public static void Prefix(CollectionCardVisual __instance)
            {
                if (!CollectionClickInspect.IsRoutingLeftClick)
                    MixedPremiumCard.Choose(__instance, false, false);
            }
        }
    }
}
