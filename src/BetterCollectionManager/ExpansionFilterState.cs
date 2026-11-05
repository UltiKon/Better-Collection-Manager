using System;
using System.Collections.Generic;
using System.Reflection;
using PegasusShared;

namespace BetterCollectionManager
{
    /// <summary>Multi-selection refines a broad native expansion pool before pagination.</summary>
    internal static class ExpansionFilterState
    {
        private sealed class Choice
        {
            public readonly HashSet<TAG_CARD_SET> Sets = new HashSet<TAG_CARD_SET>();
            public readonly HashSet<TAG_CARD_SET> SpecificCardSets = new HashSet<TAG_CARD_SET>();
            public readonly HashSet<string> Cards = new HashSet<string>(StringComparer.Ordinal);
        }
        private sealed class State
        {
            internal readonly FilterSelection<SetFilterItem> Selected;
            internal readonly Dictionary<SetFilterItem, Choice> Choices = new Dictionary<SetFilterItem, Choice>();
            internal bool Active;
            internal State(FilterTrayKind kind) { Selected = new FilterSelection<SetFilterItem>(kind); }
        }
        private static readonly State s_cards = new State(FilterTrayKind.Expansions), s_packages = new State(FilterTrayKind.PackageExpansions);
        private static State Current => PackageViewState.IsActive ? s_packages : s_cards;
        private static FilterSelection<SetFilterItem> s_selected => Current.Selected;
        private static Dictionary<SetFilterItem, Choice> s_choices => Current.Choices;
        public static int SelectedCount => s_selected.Count;
        public static bool IsActive { get => Current.Active; private set => Current.Active = value; }
        public static IEnumerable<SetFilterItem> SelectedItems => s_selected.Values;
        public static bool IsSelected(SetFilterItem item) => s_selected.Contains(item);
        public static bool IsCurrent(SetFilterItem item) => s_selected.IsCurrent(item);
        public static bool IsExcluded(SetFilterItem item) => s_selected.IsExcluded(item);
        public static List<FilterTerm<SetFilterItem>> Capture() => s_selected.Capture();
        public static void Restore(IEnumerable<FilterTerm<SetFilterItem>> terms)
        {
            foreach (var term in terms) if (!s_choices.ContainsKey(term.Value)) s_choices[term.Value] = ReadChoice(term.Value);
            s_selected.Restore(terms); IsActive = true;
        }
        public static void Toggle(SetFilterItem item)
        {
            if (!s_choices.ContainsKey(item)) s_choices[item] = ReadChoice(item);
            s_selected.Toggle(item); IsActive = true;
        }
        public static void Activate() => IsActive = true;

        public static void Clear()
        {
            IsActive = false;
            s_selected.Clear();
            s_choices.Clear();
        }

        public static void SetSelected(SetFilterItem item, bool selected)
        {
            if (selected && !s_choices.ContainsKey(item)) s_choices[item] = ReadChoice(item);
            s_selected.Set(item, selected);
            IsActive = true;
        }

        public static bool MatchesValue(CollectibleCard card, SetFilterItem item)
        {
            if (card == null) return false;
            if (!s_choices.TryGetValue(item, out Choice choice)) s_choices[item] = choice = ReadChoice(item);
            return choice.Sets.Contains(card.Set) || choice.Cards.Contains(card.CardId);
        }
        public static FilterBitSet MatchSet(Func<SetFilterItem, FilterBitSet> lookup, int count)
            => IsActive ? s_selected.MatchSet(lookup, count) : FilterBitSet.Full(count);
        public static FilterBitSet CandidateContextSet(Func<SetFilterItem, FilterBitSet> lookup, int count) => s_selected.CandidateContextSet(lookup, count);

        public static bool MatchesCandidate(CollectibleCard card, SetFilterItem candidate)
        {
            if (card == null) return false;
            if (!s_choices.ContainsKey(candidate)) s_choices[candidate] = ReadChoice(candidate);
            return s_selected.MatchesCandidate(candidate, item => s_choices[item].Sets.Contains(card.Set)
                || s_choices[item].Cards.Contains(card.CardId));
        }

        public static bool Matches(CollectibleCard card)
        {
            if (!IsActive) return true;
            if (card == null) return false;
            return s_selected.Matches(item => s_choices[item].Sets.Contains(card.Set)
                || s_choices[item].Cards.Contains(card.CardId));
        }

        internal static bool FitsStandard(SetFilterItem item, HashSet<TAG_CARD_SET> standard)
        {
            if (!s_choices.TryGetValue(item, out Choice choice)) s_choices[item] = choice = ReadChoice(item);
            if (choice.Sets.Count == 0 && choice.SpecificCardSets.Count == 0) return false;
            return standard.IsSupersetOf(choice.Sets) && standard.IsSupersetOf(choice.SpecificCardSets)
                && (choice.Cards.Count == 0 || choice.SpecificCardSets.Count > 0);
        }

        // Theme and logo reflect the user's selection, independently of the
        // broad native pool kept warm for availability and fast switching.
        internal static FormatType DisplayFormat
        {
            get
            {
                if (SelectedCount == 0) return FormatType.FT_WILD;
                var standard = new HashSet<TAG_CARD_SET>(GameUtils.GetStandardSets());
                foreach (SetFilterItem item in SelectedItems)
                    if (!FitsStandard(item, standard)) return FormatType.FT_WILD;
                return FormatType.FT_STANDARD;
            }
        }

        // Include every selected set, even if a special native group lies outside Wild.
        public static List<TAG_CARD_SET> NativeCardSets()
        {
            var sets = new HashSet<TAG_CARD_SET>(GameUtils.GetCardSetsInFormat(FormatType.FT_WILD));
            foreach (SetFilterItem item in s_selected.Values)
            {
                sets.UnionWith(s_choices[item].Sets);
                sets.UnionWith(s_choices[item].SpecificCardSets);
            }
            return new List<TAG_CARD_SET>(sets);
        }

        private static Choice ReadChoice(SetFilterItem item)
        {
            var result = new Choice();
            List<int> specific = ReadSpecificCards(item);
            if (item.CardSets != null && item.CardSets.Count > 0) result.Sets.UnionWith(item.CardSets);
            if (specific != null)
                foreach (int id in specific) result.Cards.Add(ResolveCardId(id));
            if (result.Cards.Count > 0)
            {
                List<CollectibleCard> cards = CollectionManager.Get()?.GetAllCards();
                if (cards != null)
                    foreach (CollectibleCard card in cards)
                        if (card != null && result.Cards.Contains(card.CardId)) result.SpecificCardSets.Add(card.Set);
            }
            if (result.Sets.Count == 0 && result.Cards.Count == 0)
            {
                FormatType format = item.FormatType == FormatType.FT_UNKNOWN ? FormatType.FT_WILD : item.FormatType;
                result.Sets.UnionWith(item.IsAllStandard ? GameUtils.GetStandardSets() : GameUtils.GetCardSetsInFormat(format));
            }
            return result;
        }

        internal static List<int> ReadSpecificCards(SetFilterItem item)
        {
            PropertyInfo property = typeof(SetFilterItem).GetProperty("SpecificCards",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null) return property.GetValue(item, null) as List<int>;
            return typeof(SetFilterItem).GetField("m_specificCards",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item) as List<int>;
        }

        private static string ResolveCardId(int id)
        {
            MethodInfo method = typeof(GameUtils).GetMethod("GetCardIdFromDbfId",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null);
            string cardId = method?.Invoke(null, new object[] { id }) as string;
            if (string.IsNullOrEmpty(cardId)) throw new InvalidOperationException("Cannot resolve expansion card DBF id " + id);
            return cardId;
        }
    }
}
