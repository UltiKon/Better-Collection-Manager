using System;
using System.Collections.Generic;
using Blizzard.T5.Core;
using HarmonyLib;

namespace BetterCollectionManager.Patches
{
    /// <summary>
    /// Postfix on UpdateResults: after class bucketing, collapse each class list (and DK
    /// sibling lists) by CardId to the preferred owned premium. GetPageContents* / GetNumPagesForTab
    /// slice those lists with m_cardsPerPage (8), so collapsing here fills pages correctly.
    /// </summary>
    [HarmonyPatch(typeof(CollectibleCardClassFilter), "UpdateResults")]
    internal static class CollectibleCardClassFilter_UpdateResults_Patch
    {
        private static AccessTools.FieldRef<CollectibleCardClassFilter, Map<TAG_CLASS, List<CollectibleCard>>> s_resultsByClass;
        private static AccessTools.FieldRef<CollectibleCardClassFilter, List<CollectibleCard>> s_unfilteredDk;
        private static AccessTools.FieldRef<CollectibleCardClassFilter, List<CollectibleCard>> s_hiddenDk;
        private static bool s_fieldsReady;
        private static bool s_fieldsFailed;

        internal static void EnsureFields()
        {
            if (s_fieldsReady || s_fieldsFailed)
                return;

            try
            {
                s_resultsByClass = AccessTools.FieldRefAccess<CollectibleCardClassFilter, Map<TAG_CLASS, List<CollectibleCard>>>("m_currentResultsByClass");
                s_unfilteredDk = AccessTools.FieldRefAccess<CollectibleCardClassFilter, List<CollectibleCard>>("m_unfilteredDeathKnightCards");
                s_hiddenDk = AccessTools.FieldRefAccess<CollectibleCardClassFilter, List<CollectibleCard>>("m_hiddenDeathKnightCards");
                s_fieldsReady = true;
            }
            catch (Exception ex)
            {
                s_fieldsFailed = true;
                DebugLog.Exception("CollectibleCardClassFilter field accessor init failed", ex);
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(CollectibleCardClassFilter __instance, out bool __state)
        {
            __state = InspectCollectionRefresh.TryApply(__instance);
            if (__state) return false;
            InspectCollectionRefresh.Reset();
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(CollectibleCardClassFilter __instance, bool __state = false)
        {
            if (__state) return;
            Action appendCombos = null;
            bool collapsed = false;
            PreferredQualityCollapse.BeginUpdate();
            try
            {
                EnsureFields();
                if (!s_fieldsReady || __instance == null)
                    return;

                Map<TAG_CLASS, List<CollectibleCard>> byClass = s_resultsByClass(__instance);
                if (byClass == null)
                {
                    DebugLog.Warning("UpdateResults: m_currentResultsByClass is null");
                    return;
                }

                List<CollectibleCard> unfilteredDk = s_unfilteredDk(__instance);
                List<CollectibleCard> hiddenDk = s_hiddenDk(__instance);
                appendCombos = ComboCollectionCards.Prepare(byClass);

                if (PackageViewState.IsActive)
                {
                    unfilteredDk?.Clear(); hiddenDk?.Clear();
                    FilterAvailability.Clear();
                    return; // The browser replaces native/card-filter results in finally.
                }

                if (CollectionQualitySettings.HideEmptyFilters || KeywordFilterState.IsActive || ExtraFilterState.IsActive || ExpansionFilterState.IsActive)
                {
                    // Hidden/rune fallback lists are filtered below, but cannot make
                    // an option available when no visible native result matches it.
                    FilterAvailability.Capture(AvailabilityCandidates(byClass));
                }
                else FilterAvailability.Clear();

                // Keyword + Type/Race/School filters (independent of preferred collapse).
                if (KeywordFilterState.IsActive || ExtraFilterState.IsActive || ExpansionFilterState.IsActive)
                {
                    foreach (KeyValuePair<TAG_CLASS, List<CollectibleCard>> kvp in byClass)
                    {
                        if (kvp.Value != null && kvp.Value.Count > 0)
                            KeywordFilterApplier.ApplyInPlace(kvp.Value);
                    }
                    if (unfilteredDk != null && unfilteredDk.Count > 0)
                        KeywordFilterApplier.ApplyInPlace(unfilteredDk);
                    if (hiddenDk != null && hiddenDk.Count > 0)
                        KeywordFilterApplier.ApplyInPlace(hiddenDk);
                }

                if (!PreferredQualityMode.ShouldCollapse(__instance, out _, out _)) return;

                foreach (KeyValuePair<TAG_CLASS, List<CollectibleCard>> kvp in byClass)
                {
                    if (kvp.Value == null || kvp.Value.Count == 0)
                        continue;
                    PreferredQualityCollapse.CollapseInPlace(kvp.Value);
                }

                if (unfilteredDk != null && unfilteredDk.Count > 0)
                    PreferredQualityCollapse.CollapseInPlace(unfilteredDk);

                if (hiddenDk != null && hiddenDk.Count > 0)
                    PreferredQualityCollapse.CollapseInPlace(hiddenDk);

                collapsed = true;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("UpdateResults postfix failed", ex);
            }
            finally
            {
                try { appendCombos?.Invoke(); }
                catch (Exception ex) { collapsed = false; DebugLog.Exception("Combo page insertion failed", ex); }
                if (collapsed)
                {
                    try { InspectCollectionRefresh.RecordResults(__instance, ResultLists(s_resultsByClass(__instance), s_unfilteredDk(__instance), s_hiddenDk(__instance))); }
                    catch (Exception ex) { InspectCollectionRefresh.Reset(); DebugLog.Exception("Inspected-card baseline unavailable", ex); }
                }
                CollectionOpenSearch.ResultsUpdated(__instance);
            }
        }

        private static IEnumerable<List<CollectibleCard>> ResultLists(Map<TAG_CLASS, List<CollectibleCard>> byClass,
            List<CollectibleCard> unfiltered, List<CollectibleCard> hidden)
        {
            foreach (var entry in byClass) yield return entry.Value;
            yield return unfiltered; yield return hidden;
        }

        private static IEnumerable<CollectibleCard> AvailabilityCandidates(Map<TAG_CLASS, List<CollectibleCard>> byClass)
        {
            foreach (var entry in byClass)
                if (entry.Value != null)
                    foreach (CollectibleCard card in entry.Value) yield return card;
        }
    }
}
