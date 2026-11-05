using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace BetterCollectionManager
{
    /// <summary>
    /// Removes cards that do not match the active keyword filter from result lists.
    /// </summary>
    internal static class KeywordFilterApplier
    {
        private static MethodInfo s_updateFilteredCards;
        private static MethodInfo s_animatedRefresh;
        private static MethodInfo s_preparedRefresh;
        private static object s_pageRight, s_pageLeft, s_pageNone;
        private static bool s_refreshResolved;

        public static int ApplyInPlace(List<CollectibleCard> cards)
        {
            if (cards == null || cards.Count == 0)
                return 0;
            if (!KeywordFilterState.IsActive && !ExtraFilterState.IsActive && !ExpansionFilterState.IsActive)
                return 0;

            // Compact once: RemoveAt for every rejected card repeatedly
            // shifted the remaining collection and became quadratic.
            int write = 0;
            for (int read = 0; read < cards.Count; read++)
            {
                CollectibleCard card = cards[read];
                bool matches = FilterAvailability.TryMatchCurrent(card, out bool indexed) ? indexed
                    : KeywordFilterState.Matches(card) && ExtraFilterState.Matches(card) && ExpansionFilterState.Matches(card);
                if (matches)
                    cards[write++] = card;
            }
            int removed = cards.Count - write;
            if (removed > 0) cards.RemoveRange(write, removed);
            return removed;
        }

        internal static CollectionPageAnchor CapturePageAnchor()
        {
            try
            {
                CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
                CollectiblePageManager manager = display?.GetPageManager();
                if (manager == null || display.GetViewMode() != CollectionUtils.ViewMode.CARDS) return null;
                object collection = AccessTools.Field(manager.GetType(), "m_cardsCollection")?.GetValue(manager);
                return CollectionPageAnchor.Capture(collection, manager.CurrentPageNum);
            }
            catch (System.Exception ex)
            {
                DebugLog.Exception("Collection page anchor capture failed", ex);
                return null;
            }
        }

        public static void RefreshCollectionPages(CollectionPageAnchor anchor = null, int? page = null, CollectionPageAnchor transitionFrom = null)
        {
            CollectionPageDiagnostics.Mark("refresh requested");
            try
            {
                CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
                CollectiblePageManager pageManager = display?.GetPageManager();
                if (pageManager == null)
                {
                    DebugLog.Warning("KeywordFilterApplier.RefreshCollectionPages: page manager null");
                    return;
                }

                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                int previousPage = pageManager.CurrentPageNum;
                anchor = anchor ?? CapturePageAnchor();
                // Restoring a saved location can navigate from a different
                // browser. Compare the artwork currently on screen, while the
                // saved anchor continues to determine the destination page.
                transitionFrom = transitionFrom ?? anchor;
                long captured = System.Diagnostics.Stopwatch.GetTimestamp();
                if (s_updateFilteredCards == null)
                    s_updateFilteredCards = AccessTools.Method(typeof(CollectiblePageManager), "UpdateFilteredCards");

                FilterAvailability.BeginCustomRefresh();
                try
                {
                    if (s_updateFilteredCards != null) s_updateFilteredCards.Invoke(pageManager, null);
                    else pageManager.NotifyOfCollectionChanged();
                }
                finally { FilterAvailability.EndCustomRefresh(); }
                long filtered = System.Diagnostics.Stopwatch.GetTimestamp();
                CollectionPageDiagnostics.Mark("native results updated");
                object collection = AccessTools.Field(pageManager.GetType(), "m_cardsCollection")?.GetValue(pageManager);

                if (page.HasValue) AccessTools.Field(pageManager.GetType(), "m_currentPageNum")?.SetValue(pageManager, System.Math.Max(1, page.Value));
                else if (anchor != null)
                {
                    AccessTools.Field(pageManager.GetType(), "m_currentPageNum")?.SetValue(pageManager, anchor.Resolve(collection));
                }
                long resolved = System.Diagnostics.Stopwatch.GetTimestamp();
                CollectionPageDiagnostics.Mark("refresh page contents");
                bool animate = FilterButtonFeedback.IsFilterAction && display.GetViewMode() == CollectionUtils.ViewMode.CARDS
                    && (pageManager.CurrentPageNum != previousPage
                        || (transitionFrom != null && !transitionFrom.MatchesVisiblePage(collection, pageManager.CurrentPageNum)));
                FilterButtonFeedback.RunPageRefresh(() => RefreshPageContents(pageManager, animate,
                    pageManager.CurrentPageNum < previousPage));
                long finished = System.Diagnostics.Stopwatch.GetTimestamp();
                double milliseconds = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if ((finished - started) * milliseconds >= 25.0)
                    DebugLog.Msg($"Filter refresh timing: snapshot={(captured - started) * milliseconds:F1}ms "
                        + $"native={(filtered - captured) * milliseconds:F1}ms resolve={(resolved - filtered) * milliseconds:F1}ms "
                        + $"contents={(finished - resolved) * milliseconds:F1}ms");
                DebugLog.Msg(
                    $"Filter refresh kw={KeywordFilterState.IsActive} extra={ExtraFilterState.IsActive} " +
                    $"{KeywordFilterState.Describe()} {ExtraFilterState.Describe()}");
            }
            catch (System.Exception ex)
            {
                DebugLog.Exception("KeywordFilterApplier.RefreshCollectionPages failed", ex);
            }
        }

        // The in-game trace exposes this overload and the BookPageManager enum.
        // Use reflection because a stripped game may expose it non-publicly.
        internal static MethodInfo FindAnimatedRefresh(System.Type type)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "RefreshCurrentPageContents" || method.ReturnType != typeof(void)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1) continue;
                System.Type transition = parameters[0].ParameterType;
                if (transition.IsEnum && transition.FullName == "BookPageManager+PageTransitionType"
                    && System.Enum.IsDefined(transition, "SINGLE_PAGE_RIGHT")
                    && System.Enum.IsDefined(transition, "SINGLE_PAGE_LEFT")) return method;
            }
            return null;
        }

        internal static MethodInfo FindPreparedRefresh(System.Type type)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "TransitionPageWhenReady" || method.ReturnType != typeof(void)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 4 && parameters[0].ParameterType.FullName == "BookPageManager+PageTransitionType"
                    && parameters[1].ParameterType == typeof(bool)
                    && typeof(System.Delegate).IsAssignableFrom(parameters[2].ParameterType)
                    && parameters[3].ParameterType == typeof(object)) return method;
            }
            return null;
        }

        private static void RefreshPageContents(CollectiblePageManager manager, bool animate, bool backwards)
        {
            if (!s_refreshResolved)
            {
                s_refreshResolved = true;
                s_preparedRefresh = FindPreparedRefresh(typeof(CollectiblePageManager));
                s_animatedRefresh = FindAnimatedRefresh(typeof(CollectiblePageManager));
                MethodInfo refresh = s_preparedRefresh ?? s_animatedRefresh;
                if (refresh != null)
                {
                    System.Type transition = refresh.GetParameters()[0].ParameterType;
                    s_pageRight = System.Enum.Parse(transition, "SINGLE_PAGE_RIGHT");
                    s_pageLeft = System.Enum.Parse(transition, "SINGLE_PAGE_LEFT");
                    s_pageNone = System.Enum.Parse(transition, "NONE");
                }
            }
            if (s_preparedRefresh != null)
            {
                // RefreshCurrentPageContents calls UpdateFilteredCards again,
                // after we have resolved the anchor. Assemble the already
                // filtered page directly, preserving its resolved page number.
                s_preparedRefresh.Invoke(manager, new[] { animate ? backwards ? s_pageLeft : s_pageRight : s_pageNone, (object)true, null, null });
                return;
            }
            if (animate)
            {
                if (s_animatedRefresh != null)
                {
                    CollectionPageDiagnostics.Mark("animated refresh: " + (backwards ? "SINGLE_PAGE_LEFT" : "SINGLE_PAGE_RIGHT"));
                    s_animatedRefresh.Invoke(manager, new[] { backwards ? s_pageLeft : s_pageRight });
                    return;
                }
            }
            manager.RefreshCurrentPageContents();
        }
    }
}
