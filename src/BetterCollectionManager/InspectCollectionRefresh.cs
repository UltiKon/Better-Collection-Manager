using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace BetterCollectionManager
{
    /// <summary>
    /// Reuse the current result lists for removals of the inspected card. No
    /// native search/sort or collapse of unrelated IDs is needed in this case.
    /// All other collection changes and query changes use the normal rebuild.
    /// </summary>
    internal static class InspectCollectionRefresh
    {
        private static CollectibleCardClassFilter s_filter;
        private static List<List<CollectibleCard>> s_lists;
        private static Session s_session;
        private static bool s_knownRemoval, s_refreshing, s_confirmation, s_installed;
        private static readonly TAG_PREMIUM[] Premiums =
            { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE };
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (s_installed) return;
            try
            {
                Patch(typeof(CraftingManager), "EnterCraftMode", nameof(InspectPrefix));
                Patch(typeof(CraftingManager), "OnCardDisenchanted", nameof(ConfirmPrefix));
                MethodInfo changes = AccessTools.Method(typeof(CollectionManager), "OnCardsModified");
                if (changes == null) throw new MissingMethodException("CollectionManager.OnCardsModified");
                harmony.Patch(changes, prefix: Hook(nameof(ChangesPrefix)), finalizer: Hook(nameof(ChangesFinalizer)));
                s_installed = true;
                DebugLog.Msg("Inspected-card removal refresh ready");

                void Patch(Type type, string method, string prefix)
                {
                    MethodInfo original = AccessTools.Method(type, method);
                    if (original == null) throw new MissingMethodException(type.Name, method);
                    harmony.Patch(original, prefix: Hook(prefix));
                }
            }
            catch (Exception ex) { DebugLog.Exception("Inspected-card refresh unavailable; keeping full updates", ex); }
        }

        private static HarmonyMethod Hook(string name)
            => new HarmonyMethod(typeof(InspectCollectionRefresh).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        private static void InspectPrefix(Actor __0)
        {
            try { BeginInspect(__0?.GetEntityDef()?.GetCardId()); }
            catch (Exception ex) { s_session = null; DebugLog.Exception("Inspected-card capture failed", ex); }
        }

        private static void ConfirmPrefix(Network.CardSaleResult __0)
        {
            s_confirmation = s_session != null && __0 != null && __0.AssetName == s_session.Id;
        }

        private static void ChangesPrefix(List<CollectionManager.CardModification> __0, out bool __state)
        {
            __state = s_knownRemoval;
            s_knownRemoval = IsInspectedRemoval(__0);
            if (!s_knownRemoval) s_session = null;
        }

        private static Exception ChangesFinalizer(Exception __exception, bool __state)
        {
            s_knownRemoval = __state;
            if (__exception != null) s_session = null;
            return __exception;
        }

        internal static bool IsInspectedRemoval(List<CollectionManager.CardModification> changes)
        {
            if (s_session == null || changes == null || changes.Count == 0) return false;
            foreach (var change in changes)
                if (change.cardID != s_session.Id || change.count <= 0
                    || Array.IndexOf(Premiums, change.premium) < 0
                    || change.modificationType != CollectionManager.CardModification.ModificationType.Remove) return false;
            return true;
        }

        internal static void Reset()
        {
            s_filter = null; s_lists = null; s_session = null; s_confirmation = false;
        }

        internal static void RecordResults(CollectibleCardClassFilter filter, IEnumerable<List<CollectibleCard>> lists)
        {
            Reset();
            if (!ReferenceEquals(CurrentFilter(), filter)) return;
            s_filter = filter; s_lists = new List<List<CollectibleCard>>();
            foreach (var list in lists) if (list != null && !s_lists.Contains(list)) s_lists.Add(list);
        }

        private static object CurrentFilter()
        {
            var page = CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager();
            return page == null ? null : AccessTools.Field(page.GetType(), "m_cardsCollection")?.GetValue(page);
        }

        internal static bool BeginInspect(string cardId)
        {
            s_session = null; s_confirmation = false;
            CollectionManager manager = CollectionManager.Get();
            if (!s_installed || string.IsNullOrEmpty(cardId) || s_filter == null || s_lists == null
                || CraftingManager.GetIsInCraftingMode()
                || !CanReuse(manager) || !ReferenceEquals(CurrentFilter(), s_filter)
                || !PreferredQualityCollapse.TryGetVariants(cardId, out _)
                // Core/Classic counterparts can change the visibility of a
                // different card ID; those need the native counterpart pass.
                || GameUtils.IsClassicCard(cardId)
                || (GameUtils.GetCounterpartCards(GameUtils.TranslateCardIdToDbId(cardId))?.Count ?? 0) > 0
                || !IsOwnershipOnlyQuery(s_filter)) return false;
            var session = new Session { Id = cardId, Manager = manager, Result = s_filter.FindCardsResult,
                Query = new QuerySnapshot(s_filter), Revision = FilterModes.ResultsRevision,
                Display = CollectionQualitySettings.Display, Format = manager.GetThemeShowing(),
                Order = new List<TAG_PREMIUM>(PreferredPremiumPrefs.Order) };
            if (session.Result?.m_cards == null) return false;
            session.Raw = Slice.Capture(session.Result.m_cards, cardId);
            if (session.Raw == null) return false;
            foreach (var list in s_lists)
            {
                Slice slice = Slice.Capture(list, cardId);
                if (slice != null) session.Slices.Add(slice);
                else if (list.Exists(card => card?.CardId == cardId)) return false; // Noncontiguous foreign result list.
            }
            if (session.Slices.Count == 0) return false;
            session.ReadCounts(); s_session = session;
            return true;
        }

        private static bool CanReuse(CollectionManager manager)
        {
            var display = manager?.GetCollectibleDisplay();
            return PreferredQualityMode.IsEnabled && PreferredPremiumPrefs.CollapseEnabled
                && manager != null && !manager.IsInEditMode() && manager.GetEditedDeck() == null
                && display != null && display.GetViewMode() == CollectionUtils.ViewMode.CARDS
                && display.GetViewSubmode() == CollectionUtils.ViewSubmode.DEFAULT
                && !(CraftingTray.Get()?.IsShown() ?? false) && ComboCollectionCards.CanRefreshInspectedCard();
        }

        private static bool IsOwnershipOnlyQuery(CollectibleCardFilter filter)
        {
            object Read(string name) => typeof(CollectibleCardFilter).GetField(name, Fields)?.GetValue(filter);
            if (Read("m_filterOwnedMinimum") as int? != 1 || Read("m_craftableFilterValue") != null
                || !string.IsNullOrWhiteSpace(Read("m_filterText") as string)) return false;
            // Named searches may depend on other IDs, dust, favorites or counts.
            // The usual owned-only view has no search and an ALL premium mask.
            if (Read("m_filterMasks") is IEnumerable masks)
                foreach (object mask in masks) if (Convert.ToInt32(mask) != -1) return false;
            return true;
        }

        internal static void BeginCollectionRefresh()
        {
            s_refreshing = s_knownRemoval || s_confirmation;
            s_confirmation = false;
            if (!s_refreshing) s_session = null;
        }
        internal static void EndCollectionRefresh() { s_refreshing = false; }

        internal static bool TryApply(CollectibleCardClassFilter filter)
        {
            Session session = s_session;
            if (!s_refreshing || session == null || !ReferenceEquals(filter, s_filter)) return false;
            try
            {
                if (!session.IsCurrent(filter)) return false;
                long started = Stopwatch.GetTimestamp();
                var native = session.Raw.OwnedVariants();
                var collapsed = new List<CollectibleCard>(native);
                KeywordFilterApplier.ApplyInPlace(collapsed);
                PreferredQualityCollapse.RefreshCard(session.Id, collapsed);
                session.Raw.Replace(native);
                foreach (Slice slice in session.Slices) slice.Replace(collapsed);
                session.ReadCounts();
                ComboCollectionCards.InspectedCardOwnershipChanged(session.Id, native.Count > 0);
                FilterAvailability.InspectedCardOwnershipChanged(session.Id, native);
                DebugLog.Msg($"Inspected-card refresh: {session.Id}, variants={native.Count}, tiles={collapsed.Count}, "
                    + $"elapsed={(Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency:F1}ms");
                return true;
            }
            catch (Exception ex)
            {
                s_session = null;
                DebugLog.Exception("Inspected-card refresh failed; rebuilding results", ex);
                return false;
            }
        }

        private sealed class Session
        {
            internal string Id;
            internal CollectionManager Manager;
            internal CollectionManager.FindCardsResult Result;
            internal QuerySnapshot Query;
            internal int Revision;
            internal QualityDisplayMode Display;
            internal PegasusShared.FormatType Format;
            internal List<TAG_PREMIUM> Order;
            internal Slice Raw;
            internal readonly List<Slice> Slices = new List<Slice>();
            private readonly int[] m_counts = new int[Premiums.Length];
            internal void ReadCounts()
            {
                for (int i = 0; i < Premiums.Length; i++) m_counts[i] = Manager.GetOwnedCount(Id, Premiums[i]);
            }
            internal bool IsCurrent(CollectibleCardClassFilter filter)
            {
                if (!ReferenceEquals(Manager, CollectionManager.Get()) || !CanReuse(Manager)
                    || !ReferenceEquals(filter, CurrentFilter()) || !ReferenceEquals(Result, filter.FindCardsResult)
                    || Revision != FilterModes.ResultsRevision || Display != CollectionQualitySettings.Display
                    || Format != Manager.GetThemeShowing() || !Query.Matches(filter) || !Raw.IsCurrent(Id)) return false;
                for (int i = 0; i < Premiums.Length; i++) if (Manager.GetOwnedCount(Id, Premiums[i]) > m_counts[i]) return false;
                IReadOnlyList<TAG_PREMIUM> order = PreferredPremiumPrefs.Order;
                if (Order.Count != order.Count) return false;
                for (int i = 0; i < Order.Count; i++) if (Order[i] != order[i]) return false;
                foreach (Slice slice in Slices) if (!slice.IsCurrent(Id)) return false;
                return true;
            }
        }

        // Only these short ranges are replaced. List shifting is left to the
        // native List implementation; unrelated cards are neither read nor classified.
        private sealed class Slice
        {
            private List<CollectibleCard> m_list;
            private int m_start, m_count, m_length;
            internal static Slice Capture(List<CollectibleCard> list, string id)
            {
                int start = list.FindIndex(card => card?.CardId == id);
                if (start < 0) return null;
                int end = start;
                while (end < list.Count && list[end]?.CardId == id) end++;
                for (int i = end; i < list.Count; i++) if (list[i]?.CardId == id) return null;
                return new Slice { m_list = list, m_start = start, m_count = end - start, m_length = list.Count };
            }
            internal bool IsCurrent(string id)
            {
                if (m_list.Count != m_length || m_start + m_count > m_list.Count) return false;
                for (int i = m_start; i < m_start + m_count; i++) if (m_list[i]?.CardId != id) return false;
                return true;
            }
            internal List<CollectibleCard> OwnedVariants()
            {
                var result = new List<CollectibleCard>(m_count);
                for (int i = m_start; i < m_start + m_count; i++)
                {
                    CollectibleCard card = m_list[i];
                    if (card.OwnedCount + card.TrialCount > 0) result.Add(card);
                }
                return result;
            }
            internal void Replace(List<CollectibleCard> cards)
            {
                m_list.RemoveRange(m_start, m_count); m_list.InsertRange(m_start, cards);
                m_count = cards.Count; m_length = m_list.Count;
            }
        }

        // Compare copied collection contents as well as scalar values, so an
        // in-place set/class/mask edit cannot accidentally reuse old results.
        private sealed class QuerySnapshot
        {
            private readonly FieldInfo[] m_fields = typeof(CollectibleCardFilter).GetFields(Fields | BindingFlags.DeclaredOnly);
            private readonly object[] m_values;
            internal QuerySnapshot(CollectibleCardFilter filter)
            {
                m_values = new object[m_fields.Length];
                for (int i = 0; i < m_fields.Length; i++) m_values[i] = Copy(m_fields[i].GetValue(filter));
            }
            private static object Copy(object value)
            {
                if (value is string || !(value is IEnumerable items)) return value;
                var copy = new List<object>(); foreach (object item in items) copy.Add(Copy(item));
                return copy;
            }
            private static bool Equal(object snapshot, object value)
            {
                if (!(snapshot is List<object> list)) return Equals(snapshot, value);
                if (!(value is IEnumerable sequence)) return false;
                int i = 0;
                foreach (object item in sequence) if (i >= list.Count || !Equal(list[i++], item)) return false;
                return i == list.Count;
            }
            internal bool Matches(CollectibleCardFilter filter)
            {
                for (int i = 0; i < m_fields.Length; i++) if (!Equal(m_values[i], m_fields[i].GetValue(filter))) return false;
                return true;
            }
        }
    }
}
