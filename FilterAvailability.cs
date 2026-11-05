using System;
using System.Collections.Generic;
using PegasusShared;

namespace BetterCollectionManager
{
    /// <summary>Inverted card sets over native results retain search, class, mana and ownership restrictions.</summary>
    internal static class FilterAvailability
    {
        private static List<CollectibleCard> s_pool, s_scope;
        private static HashSet<string> s_ids = new HashSet<string>(StringComparer.Ordinal);
        private static List<CollectibleCard> s_capturePool = new List<CollectibleCard>();
        private static HashSet<string> s_captureIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> s_positions = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<FilterTrayKind, Dictionary<int, FilterBitSet>> s_index = new Dictionary<FilterTrayKind, Dictionary<int, FilterBitSet>>();
        private static readonly Dictionary<SetFilterItem, FilterBitSet> s_expansionIndex = new Dictionary<SetFilterItem, FilterBitSet>();
        private static readonly Dictionary<FilterTrayKind, FilterBitSet> s_categories = new Dictionary<FilterTrayKind, FilterBitSet>();
        private static readonly Dictionary<FilterTrayKind, FilterBitSet> s_candidatePools = new Dictionary<FilterTrayKind, FilterBitSet>();
        private static int s_revision = -1, s_catalogGeneration = -1, s_categoryGeneration = -1, s_customRefresh;
        private static FilterModeBehavior s_behavior;
        private static FilterBitSet s_current, s_live;
        public static event Action Changed;

        // Native searches and collection updates invalidate the index. During an
        // explicit custom-filter refresh, unchanged native input can reuse it.
        public static void BeginCustomRefresh() { s_customRefresh++; }
        public static void EndCustomRefresh() { if (s_customRefresh > 0) s_customRefresh--; }
        public static void Capture(IEnumerable<CollectibleCard> cards)
        {
            var ids = s_captureIds; ids.Clear();
            var pool = s_capturePool; pool.Clear();
            foreach (CollectibleCard card in cards)
                if (card != null && ids.Add(card.CardId)) pool.Add(card);
            bool changed = s_pool == null || !s_ids.SetEquals(ids);
            bool same = !changed && s_customRefresh > 0 && pool.Count == s_pool.Count;
            if (same)
                for (int i = 0; i < pool.Count; i++) if (!ReferenceEquals(pool[i], s_pool[i])) { same = false; break; }
            if (!same)
            {
                s_capturePool = s_pool ?? new List<CollectibleCard>(); s_captureIds = s_ids;
                s_pool = pool; s_ids = ids;
                s_capturePool.Clear(); s_captureIds.Clear();
                ClearIndex();
            }
            else { pool.Clear(); ids.Clear(); ClearComputed(); }
            if (changed) Changed?.Invoke();
        }

        public static void Clear()
        {
            s_pool?.Clear(); s_pool = null; s_ids.Clear(); s_capturePool.Clear(); s_captureIds.Clear(); ClearIndex();
        }
        private static void ClearIndex()
        {
            s_scope = null; s_live = null; s_positions.Clear(); s_index.Clear(); s_expansionIndex.Clear(); ClearComputed();
        }

        internal static void InspectedCardOwnershipChanged(string cardId, IReadOnlyList<CollectibleCard> nativeVariants)
        {
            if (s_pool == null) return;
            int position = s_pool.FindIndex(card => card.CardId == cardId);
            if (position < 0) return;
            if (nativeVariants.Count > 0)
            {
                s_pool[position] = nativeVariants[0];
                if (s_scope != null && s_positions.TryGetValue(cardId, out int index)) s_scope[index] = nativeVariants[0];
                return;
            }
            s_pool.RemoveAt(position); s_ids.Remove(cardId);
            // Keep metadata indexes for every other card. A cleared live bit
            // removes this ID even from negative filter candidate sets.
            if (s_scope != null && s_positions.TryGetValue(cardId, out int removed)) s_live = s_live.Without(removed);
            ClearComputed();
            Changed?.Invoke();
        }
        private static void ClearComputed()
        {
            s_categories.Clear(); s_candidatePools.Clear(); s_current = null; s_revision = -1;
        }
        private static bool Prepare()
        {
            if (s_catalogGeneration != KeywordCatalog.Generation || s_categoryGeneration != CategoryCatalog.Generation)
            {
                ClearIndex(); s_catalogGeneration = KeywordCatalog.Generation;
                s_categoryGeneration = CategoryCatalog.Generation;
            }
            if (s_revision != FilterModes.ResultsRevision || s_behavior != CollectionQualitySettings.FilterModeBehavior)
            {
                ClearComputed(); s_revision = FilterModes.ResultsRevision; s_behavior = CollectionQualitySettings.FilterModeBehavior;
            }
            if (s_scope != null) return true;
            List<CollectibleCard> cards = s_pool ?? CollectionManager.Get()?.GetAllCards();
            if (cards == null) return false;
            s_scope = new List<CollectibleCard>();
            foreach (CollectibleCard card in cards)
                if (KeywordCatalog.IsConstructedCardInScope(card, FormatType.FT_WILD, null))
                {
                    if (!string.IsNullOrEmpty(card.CardId) && !s_positions.ContainsKey(card.CardId)) s_positions.Add(card.CardId, s_scope.Count);
                    s_scope.Add(card);
                }
            s_live = FilterBitSet.Full(s_scope.Count);
            return true;
        }
        private static FilterBitSet Lookup(FilterTrayKind kind, int value)
        {
            if (!s_index.TryGetValue(kind, out Dictionary<int, FilterBitSet> values))
                s_index[kind] = values = new Dictionary<int, FilterBitSet>();
            if (!values.TryGetValue(value, out FilterBitSet set))
                values[value] = set = FilterBitSet.Build(s_scope.Count, index => kind == FilterTrayKind.Keywords
                    ? KeywordFilterState.MatchesValue(s_scope[index], (GAME_TAG)value)
                    : ExtraFilterState.MatchesValue(s_scope[index], kind, value));
            return set;
        }
        private static FilterBitSet ExpansionLookup(SetFilterItem item)
        {
            if (!s_expansionIndex.TryGetValue(item, out FilterBitSet set))
                s_expansionIndex[item] = set = FilterBitSet.Build(s_scope.Count, index => ExpansionFilterState.MatchesValue(s_scope[index], item));
            return set;
        }
        private static FilterBitSet Category(FilterTrayKind kind)
        {
            if (!s_categories.TryGetValue(kind, out FilterBitSet set))
                s_categories[kind] = set = kind == FilterTrayKind.Keywords
                    ? KeywordFilterState.MatchSet(value => Lookup(kind, (int)value), s_scope.Count)
                    : kind == FilterTrayKind.Expansions ? ExpansionFilterState.MatchSet(ExpansionLookup, s_scope.Count)
                    : ExtraFilterState.MatchSet(kind, value => Lookup(kind, value), s_scope.Count);
            return set;
        }
        private static FilterBitSet CandidatePool(FilterTrayKind kind)
        {
            if (!s_candidatePools.TryGetValue(kind, out FilterBitSet set))
            {
                set = kind == FilterTrayKind.Keywords
                    ? KeywordFilterState.CandidateContextSet(value => Lookup(kind, (int)value), s_scope.Count)
                    : kind == FilterTrayKind.Expansions ? ExpansionFilterState.CandidateContextSet(ExpansionLookup, s_scope.Count)
                    : ExtraFilterState.CandidateContextSet(kind, value => Lookup(kind, value), s_scope.Count);
                foreach (FilterTrayKind other in FilterModes.Kinds) if (other != kind) set = set.And(Category(other));
                set = set.And(s_live);
                s_candidatePools[kind] = set;
            }
            return set;
        }
        public static bool Keyword(GAME_TAG tag)
            => !CollectionQualitySettings.HideEmptyFilters || KeywordFilterState.IsSelected(tag) || !Prepare()
                || CandidateIntersects(FilterTrayKind.Keywords, Lookup(FilterTrayKind.Keywords, (int)tag));
        public static bool Extra(FilterTrayKind kind, int value, bool selected)
            => !CollectionQualitySettings.HideEmptyFilters || selected
                || (kind == FilterTrayKind.Categories && value == (int)CuratedCategory.Uncategorized)
                || (kind == FilterTrayKind.Packages && HasPackage((PackageKind)value))
                || (kind == FilterTrayKind.Categories && CategoryCatalog.Combos.Count > 0 && HasCombo((CuratedCategory)value)) || !Prepare()
                || CandidateIntersects(kind, Lookup(kind, value));
        private static bool HasCombo(CuratedCategory value)
        { foreach (CategoryCatalog.Combo combo in CategoryCatalog.Combos) if (!combo.IsPackage && ExtraFilterState.MatchesComboCandidate(value, combo.Category)) return true; return false; }
        private static bool HasPackage(PackageKind kind)
        { foreach (var package in PackageCatalog.Packages) if (PackageCatalog.Matches(package, kind)) return true; return false; }
        public static bool Expansion(SetFilterItem item, bool selected)
            => !CollectionQualitySettings.HideEmptyFilters || selected || !Prepare()
                || CandidateIntersects(FilterTrayKind.Expansions, ExpansionLookup(item));
        private static bool CandidateIntersects(FilterTrayKind kind, FilterBitSet set)
        {
            int count = kind == FilterTrayKind.Keywords ? KeywordFilterState.SelectedCount
                : kind == FilterTrayKind.Expansions ? ExpansionFilterState.SelectedCount : ExtraFilterState.SelectedCount(kind);
            return set.Intersects(CandidatePool(kind), FilterModes.IsNegative(FilterModes.Operation(kind, count)));
        }

        public static bool TryMatchCurrent(CollectibleCard card, out bool matches)
        {
            matches = false;
            // Rune fallback lists and other native views can contain cards
            // outside this captured universe; those keep scalar evaluation.
            if (s_pool == null || card == null
                || string.IsNullOrEmpty(card.CardId) || !Prepare() || !s_positions.TryGetValue(card.CardId, out int index)) return false;
            if (s_current == null)
            {
                s_current = s_live;
                foreach (FilterTrayKind kind in FilterModes.Kinds) s_current = s_current.And(Category(kind));
            }
            matches = s_current.Contains(index);
            return true;
        }
    }
}
