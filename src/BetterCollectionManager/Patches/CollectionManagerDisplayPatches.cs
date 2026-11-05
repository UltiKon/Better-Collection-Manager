using System;
using System.Collections.Generic;
using HarmonyLib;
using PegasusShared;

namespace BetterCollectionManager.Patches
{
    /// <summary>
    /// Auto-enable preferred collapse when entering Collection Manager; disable on leave.
    /// Deck-building stays inside CM, so mode remains ON (collapse stays active).
    /// Also refreshes the keyword catalog when the set/format filter changes.
    /// </summary>
    internal static class CollectionManagerDisplayPatches
    {
        [HarmonyPatch(typeof(CollectionManagerDisplay), "DoEnterCollectionManagerEvents")]
        private static class DoEnter_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(CollectionManagerDisplay __instance)
            {
                try
                {
                    DebugLog.Msg("CM enter: DoEnterCollectionManagerEvents");
                    PreferredQualityMode.Enable("CM enter");
                    CollectionOpenSearch.Enter(__instance);
                    KeywordFilterUi.OnCollectionManagerEnter();
                }
                catch (Exception ex)
                {
                    DebugLog.Exception("DoEnterCollectionManagerEvents postfix failed", ex);
                }
            }
        }

        [HarmonyPatch(typeof(CollectionManagerDisplay), "Exit")]
        private static class Exit_Patch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                try
                {
                    CollectionOpenSearch.Leave();
                    DebugLog.Msg("CM leave: Exit");
                    KeywordFilterUi.OnCollectionManagerLeave();
                    PreferredQualityMode.Disable("CM Exit");
                }
                catch (Exception ex)
                {
                    DebugLog.Exception("Exit prefix failed", ex);
                }
            }
        }

        [HarmonyPatch(typeof(CollectionManagerDisplay), "Unload")]
        private static class Unload_Patch
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                try
                {
                    CollectionOpenSearch.Leave();
                    DebugLog.Msg("CM leave: Unload");
                    KeywordFilterUi.OnCollectionManagerLeave();
                    PreferredQualityMode.Disable("CM Unload");
                }
                catch (Exception ex)
                {
                    DebugLog.Exception("Unload prefix failed", ex);
                }
            }
        }

        [HarmonyPatch(typeof(CollectionManagerDisplay), "SetFilterCallback")]
        private static class SetFilterCallback_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                if (!KeywordSetTray.ShouldSuppressFilterSelectionForBack())
                    return true;
                CollectionFilterMenu.RestoreExpansionSelectionForBack();
                return false;
            }

            [HarmonyPostfix]
            public static void Postfix(
                List<TAG_CARD_SET> cardSets,
                List<int> specificCards,
                FormatType formatType,
                SetFilterItem item,
                bool transitionPage)
            {
                if (KeywordSetTray.ShouldSuppressFilterSelectionForBack())
                    return;
                try
                {
                    DebugLog.Msg($"SetFilterCallback format={formatType} item={item?.Text}");
                    KeywordFilterUi.OnSetFilterChanged(refreshPages: !transitionPage);
                }
                catch (Exception ex)
                {
                    DebugLog.Exception("SetFilterCallback postfix failed", ex);
                }
            }
        }

        /// <summary>
        /// Route the existing expansion button through the shared filter menu.
        /// </summary>
        [HarmonyPatch(typeof(SetFilterTray), "Show")]
        private static class SetFilterTrayShow_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(SetFilterTray __instance, bool show)
            {
                try
                {
                    if (CollectionFilterMenu.HandleNativeShow(__instance, show))
                        return false;
                    if (show)
                        KeywordSetTray.OnLiveSetsShown();
                }
                catch (Exception ex)
                {
                    DebugLog.Exception("SetFilterTray.Show prefix (filter menu) failed", ex);
                }
                return true;
            }
        }
    }
}
