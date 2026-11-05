using System;
using HarmonyLib;

namespace BetterCollectionManager.Patches
{
    internal static class QualityDeckInputPatches
    {
        public static void Install(HarmonyLib.Harmony harmony)
        {
            foreach (Type type in typeof(QualityDeckInputPatches).GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
            {
                try { harmony.PatchAll(type); }
                catch (Exception ex) { DebugLog.Exception("Collection quality patch unavailable: " + type.Name, ex); }
            }
        }

        [HarmonyPatch(typeof(CollectionInputMgr), "UpdateHeldCardVisual")]
        private static class HeldReleasePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(CollectionInputMgr __instance)
            {
                try { return !ComboCollectionCards.TryHandleDragRelease(__instance) && !QualityDeckInput.TryHandleRelease(__instance); }
                catch (Exception ex) { DebugLog.Exception("Collection held-card release failed", ex); return false; }
            }
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "OnRelease")]
        private static class ClickPatch
        {
            [HarmonyPrefix, HarmonyPriority(Priority.Low)]
            public static void Prefix(CollectionCardVisual __instance, out QualityAddRequest __state)
            {
                __state = null;
                try { __state = QualityDeckInput.BeginClick(__instance); }
                catch (Exception ex) { DebugLog.Exception("Collection quality click setup failed", ex); }
            }
            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception, QualityAddRequest __state)
            { QualityDeckInput.EndClick(__state); return __exception; }
        }

        [HarmonyPatch(typeof(CollectionInputMgr), "GrabCardTile")]
        private static class DeckTileGrabPatch
        {
            [HarmonyPostfix] public static void Postfix(bool __result) { if (__result) QualityDeckInput.ClearDrag(); }
        }

        [HarmonyPatch(typeof(CollectionInputMgr), "GrabCard")]
        private static class OtherGrabPatch
        {
            [HarmonyPostfix] public static void Postfix(bool __result) { if (__result) QualityDeckInput.ClearDrag(); }
        }

        [HarmonyPatch(typeof(CollectionInputMgr), "DropCard")]
        private static class DropPatch
        {
            private static readonly System.Reflection.PropertyInfo s_overDeck = AccessTools.Property(typeof(CollectionInputMgr), "MouseIsOverDeck");
            [HarmonyPrefix]
            public static void Prefix(CollectionInputMgr __instance, ref bool dragCanceled, out QualityAddRequest __state)
            {
                __state = QualityDeckInput.BeginDrop(dragCanceled);
                try
                {
                    bool overDeck = s_overDeck != null && (bool)s_overDeck.GetValue(__instance, null);
                    if (ComboCollectionCards.PrepareComboDrop(dragCanceled, overDeck)) dragCanceled = true;
                    if (QualityDeckInput.PrepareDrop(dragCanceled, overDeck)) dragCanceled = true;
                }
                catch (Exception ex) { DebugLog.Exception("Collection quality drop setup failed", ex); }
            }
            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception, QualityAddRequest __state)
            { QualityDeckInput.EndDrop(__state, __exception != null); return __exception; }
        }

        [HarmonyPatch(typeof(CollectionDeckTray), "AddCard", new[] { typeof(EntityDef), typeof(TAG_PREMIUM),
            typeof(bool), typeof(Actor), typeof(DeckRule.RuleType[]) })]
        private static class AddPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(EntityDef cardEntityDef, ref TAG_PREMIUM premium, bool playSound,
                ref Actor animateActor, DeckRule.RuleType[] ignoreRules, ref bool __result)
            {
                try { return QualityDeckInput.RouteAdd(cardEntityDef, ref premium, playSound, ref animateActor, ignoreRules, ref __result); }
                catch (Exception ex) { DebugLog.Exception("Collection quality add selection failed", ex); return true; }
            }
        }

        [HarmonyPatch(typeof(CollectionCardVisual), "ShowLock", new[] { typeof(CollectionCardVisual.LockType),
            typeof(string), typeof(bool), typeof(CollectionDeck) })]
        private static class LockPatch
        {
            [HarmonyPrefix]
            public static void Prefix(CollectionCardVisual __instance, ref CollectionCardVisual.LockType lockType, ref string reason)
            {
                try { if (!ComboCollectionCards.TryAdjustComboLock(__instance, ref lockType, ref reason)) QualityDeckInput.AdjustLock(__instance, ref lockType, ref reason); }
                catch (Exception ex) { DebugLog.Exception("Collection quality lock adjustment failed", ex); }
            }
        }
    }
}
