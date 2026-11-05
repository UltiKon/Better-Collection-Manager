using System;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager.Patches
{
    /// <summary>Keep the current choice readable on native dropdown selection backgrounds.</summary>
    [HarmonyPatch(typeof(DropdownMenuItem), nameof(DropdownMenuItem.UpdateTextColor))]
    internal static class DropdownReadabilityPatches
    {
        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.PatchAll(typeof(DropdownReadabilityPatches));
                harmony.PatchAll(typeof(DropdownScrollReleasePatch));
            }
            catch (Exception ex) { DebugLog.Exception("Dropdown readability patch unavailable", ex); }
        }

        [HarmonyPatch(typeof(DropdownControl), nameof(DropdownControl.onUserCancelled))]
        private static class DropdownScrollReleasePatch
        {
            [HarmonyPrefix]
            public static bool Prefix(DropdownControl __instance)
                => !(__instance.GetComponent<DropdownScrollbar>()?.ConsumeRelease() ?? false);
        }

        [HarmonyPostfix]
        public static void Postfix(DropdownMenuItem __instance)
        {
            CustomFpsControl.PrepareLabel(__instance);
            if (__instance.GetComponentInParent<DropdownScrollbar>() == null && !CustomFpsControl.OwnsRow(__instance)) return;
            // The selection background, rather than the containing dropdown's
            // value, identifies an expanded list's highlighted row. Closed
            // fields and ordinary rows retain their native text colors.
            if (__instance.m_text != null && __instance.m_selected != null
                && __instance.m_selected.activeInHierarchy)
                __instance.m_text.TextColor = Color.white;
        }
    }
}
