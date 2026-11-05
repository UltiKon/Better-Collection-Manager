using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private static AccessTools.FieldRef<CollectibleCardClassFilter, int> s_cardsPerPage;
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
                s_cardsPerPage = AccessTools.FieldRefAccess<CollectibleCardClassFilter, int>("m_cardsPerPage");
                s_fieldsReady = true;
                DebugLog.Msg("CollectibleCardClassFilter field accessors ready (m_currentResultsByClass, DK lists, m_cardsPerPage)");
            }
            catch (Exception ex)
            {
                s_fieldsFailed = true;
                DebugLog.Exception("CollectibleCardClassFilter field accessor init failed", ex);
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(CollectibleCardClassFilter __instance, out long __state)
        {
            __state = Stopwatch.GetTimestamp();
            if (InspectCollectionRefresh.TryApply(__instance)) { __state = -1; return false; }
            InspectCollectionRefresh.Reset();
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(CollectibleCardClassFilter __instance, long __state = 0)
        {
            if (__state == -1) return;
            long started = Stopwatch.GetTimestamp(), prepared = started, filtered = started;
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
                prepared = Stopwatch.GetTimestamp();
                filtered = prepared;

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
                    int kwRemoved = 0;
                    foreach (KeyValuePair<TAG_CLASS, List<CollectibleCard>> kvp in byClass)
                    {
                        if (kvp.Value != null && kvp.Value.Count > 0)
                            kwRemoved += KeywordFilterApplier.ApplyInPlace(kvp.Value);
                    }
                    if (unfilteredDk != null && unfilteredDk.Count > 0)
                        kwRemoved += KeywordFilterApplier.ApplyInPlace(unfilteredDk);
                    if (hiddenDk != null && hiddenDk.Count > 0)
                        kwRemoved += KeywordFilterApplier.ApplyInPlace(hiddenDk);
                    DebugLog.Msg(
                        $"UpdateResults filters removed ~{kwRemoved}; {KeywordFilterState.Describe()} {ExtraFilterState.Describe()}");
                }

                filtered = Stopwatch.GetTimestamp();
                PreferredQualityMode.SkipReason skipReason;
                string skipDetail;
                if (!PreferredQualityMode.ShouldCollapse(__instance, out skipReason, out skipDetail))
                {
                    DebugLog.Msg($"UpdateResults collapse SKIPPED: {skipReason} — {skipDetail}");
                    return;
                }

                int cardsPerPage = s_cardsPerPage != null ? s_cardsPerPage(__instance) : 8;

                int inputTotal = 0;
                foreach (KeyValuePair<TAG_CLASS, List<CollectibleCard>> kvp in byClass)
                {
                    if (kvp.Value != null)
                        inputTotal += kvp.Value.Count;
                }

                int dkUnfilteredBefore = unfilteredDk != null ? unfilteredDk.Count : 0;
                int dkHiddenBefore = hiddenDk != null ? hiddenDk.Count : 0;

                DebugLog.Msg($"UpdateResults collapse RUN (mode ON). Input class-bucket cards={inputTotal}, DK unfiltered={dkUnfilteredBefore}, DK hidden={dkHiddenBefore}, cardsPerPage={cardsPerPage}");

                var sample = new List<string>();
                int removed = 0;

                foreach (KeyValuePair<TAG_CLASS, List<CollectibleCard>> kvp in byClass)
                {
                    if (kvp.Value == null || kvp.Value.Count == 0)
                        continue;
                    removed += PreferredQualityCollapse.CollapseInPlace(kvp.Value, kvp.Key.ToString(), sample);
                }

                if (unfilteredDk != null && unfilteredDk.Count > 0)
                    removed += PreferredQualityCollapse.CollapseInPlace(unfilteredDk, "DK_unfiltered", sample);

                if (hiddenDk != null && hiddenDk.Count > 0)
                    removed += PreferredQualityCollapse.CollapseInPlace(hiddenDk, "DK_hidden", sample);

                int outputTotal = 0;
                foreach (KeyValuePair<TAG_CLASS, List<CollectibleCard>> kvp in byClass)
                {
                    if (kvp.Value != null)
                        outputTotal += kvp.Value.Count;
                }

                DebugLog.Msg($"UpdateResults collapse DONE: class-bucket {inputTotal} -> {outputTotal} (removed ~{removed}). Sample: {PreferredQualityCollapse.FormatSample(sample)}");
                DebugLog.Verbose($"  paging note: GetPageContents* uses Skip/Take(m_cardsPerPage={cardsPerPage}) on these collapsed lists, so pages fill densely.");
                collapsed = true;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("UpdateResults postfix failed", ex);
            }
            finally
            {
                long inserting = Stopwatch.GetTimestamp();
                try { appendCombos?.Invoke(); }
                catch (Exception ex) { collapsed = false; DebugLog.Exception("Combo page insertion failed", ex); }
                if (collapsed)
                {
                    try { InspectCollectionRefresh.RecordResults(__instance, ResultLists(s_resultsByClass(__instance), s_unfilteredDk(__instance), s_hiddenDk(__instance))); }
                    catch (Exception ex) { InspectCollectionRefresh.Reset(); DebugLog.Exception("Inspected-card baseline unavailable", ex); }
                }
                CollectionOpenSearch.ResultsUpdated(__instance);
                long finished = Stopwatch.GetTimestamp();
                double ms = 1000.0 / Stopwatch.Frequency;
                double native = __state > 0 ? (started - __state) * ms : 0;
                double mod = (finished - started) * ms;
                if (native + mod >= 25.0)
                    DebugLog.Msg($"Collection results timing: native={native:F1}ms mod={mod:F1}ms "
                        + $"prepare={(prepared - started) * ms:F1}ms filters={(filtered - prepared) * ms:F1}ms "
                        + $"quality={(inserting - filtered) * ms:F1}ms insert={(finished - inserting) * ms:F1}ms");
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
