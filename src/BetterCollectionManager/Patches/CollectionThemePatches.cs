using System;
using HarmonyLib;
using PegasusShared;

namespace BetterCollectionManager.Patches
{
    [HarmonyPatch(typeof(CollectionManager), "GetThemeShowing")]
    internal static class CollectionThemePatches
    {
        [HarmonyPostfix]
        internal static void Postfix(CollectionManager __instance, object[] __args, ref FormatType __result)
        {
            try
            {
                if (PackageViewState.IsActive && __instance != null && !__instance.IsInEditMode())
                { __result = PackageViewState.Format; return; }
                // Preserve deck-specific and special-mode themes. Only correct
                // the Wild theme caused by the cached availability pool.
                if (__result != FormatType.FT_WILD || __instance == null
                    || !CollectionFilterMenu.IsAttached || !ExpansionFilterState.IsActive
                    || (__args != null && __args.Length > 0 && __args[0] != null)
                    || __instance.IsInEditMode() || __instance.GetEditedDeck() != null
                    || __instance.GetCollectibleDisplay()?.GetViewMode() != CollectionUtils.ViewMode.CARDS) return;
                if (ExpansionFilterState.DisplayFormat == FormatType.FT_STANDARD)
                    __result = FormatType.FT_STANDARD;
            }
            catch (Exception ex) { DebugLog.Exception("Expansion collection theme refresh failed", ex); }
        }
    }
}
