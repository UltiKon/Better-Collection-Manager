using System;
using System.Reflection;
using HarmonyLib;

namespace BetterCollectionManager
{
    // Keep the card position across native crafting rebuilds, even when the
    // first tile's quality changes or a missing card leaves the owned results.
    internal static class CraftingPageAnchor
    {
        internal sealed class Scope
        {
            internal CollectiblePageManager Manager;
            internal CollectionPageAnchor Anchor;
            internal bool Applied;
        }
        private static bool s_installed;
        private static Scope s_current;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (s_installed) return;
            try
            {
                var begin = new HarmonyMethod(typeof(CraftingPageAnchor).GetMethod(nameof(Begin)));
                var end = new HarmonyMethod(typeof(CraftingPageAnchor).GetMethod(nameof(End)));
                harmony.Patch(AccessTools.Method(typeof(CollectionPageManager), "ShowCraftingModeCards"), prefix: begin, finalizer: end);
                harmony.Patch(AccessTools.Method(typeof(CollectiblePageManager), "HideCraftingModeCards"), prefix: begin, finalizer: end);
                harmony.Patch(AccessTools.Method(typeof(BookPageManager), "TransitionPageWhenReady"),
                    prefix: new HarmonyMethod(typeof(CraftingPageAnchor).GetMethod(nameof(KeepCardPage))));
                s_installed = true;
            }
            catch (Exception ex) { DebugLog.Exception("Crafting page anchor unavailable", ex); }
        }

        public static void Begin(CollectiblePageManager __instance, bool updatePage, out Scope __state)
        {
            __state = s_current;
            s_current = null;
            try
            {
                if (!updatePage || !PreferredQualityMode.IsEnabled || !IsCurrent(__instance)) return;
                var anchor = CollectionPageAnchor.Capture(Collection(__instance), __instance.CurrentPageNum);
                if (anchor != null) s_current = new Scope { Manager = __instance, Anchor = anchor };
            }
            catch (Exception ex) { DebugLog.Exception("Crafting page capture failed; keeping native navigation", ex); }
        }

        public static Exception End(Exception __exception, Scope __state)
        { s_current = __state; return __exception; }

        public static void KeepCardPage(BookPageManager __instance, ref bool useCurrentPageNum)
        {
            Scope scope = s_current;
            if (scope == null || scope.Applied || !ReferenceEquals(scope.Manager, __instance) || !IsCurrent(scope.Manager)) return;
            try
            {
                object collection = Collection(scope.Manager);
                FieldInfo currentPage = AccessTools.Field(__instance.GetType(), "m_currentPageNum");
                if (collection == null || currentPage == null) return;
                int page = scope.Anchor.Resolve(collection);
                currentPage.SetValue(__instance, page);
                useCurrentPageNum = true;
                scope.Applied = true;
                DebugLog.Msg("Crafting page anchor: resolved previous visible cards to page " + page);
            }
            catch (Exception ex) { DebugLog.Exception("Crafting page restore failed; keeping native navigation", ex); }
        }

        private static object Collection(CollectiblePageManager manager)
            => AccessTools.Field(manager.GetType(), "m_cardsCollection")?.GetValue(manager);

        private static bool IsCurrent(CollectiblePageManager manager)
        {
            var display = CollectionManager.Get()?.GetCollectibleDisplay();
            return display != null && ReferenceEquals(display.GetPageManager(), manager)
                && display.GetViewMode() == CollectionUtils.ViewMode.CARDS
                && display.GetViewSubmode() == CollectionUtils.ViewSubmode.DEFAULT && !PackageViewState.IsActive;
        }
    }
}
