using System.Collections.Generic;

namespace BetterCollectionManager
{
    /// <summary>
    /// Selected keyword filters for Collection Manager. A keyword matches either
    /// its own marker or a reference, including all aliases in its logical family.
    /// The category mode combines selected keywords; no selections means no keyword filtering.
    /// </summary>
    internal static class KeywordFilterState
    {
        private static readonly FilterSelection<GAME_TAG> s_selectedTags = new FilterSelection<GAME_TAG>(FilterTrayKind.Keywords);

        public static int SelectedCount => s_selectedTags.Count;
        public static bool IsActive => s_selectedTags.Count > 0;
        public static IEnumerable<GAME_TAG> SelectedTags => s_selectedTags.Values;
        public static bool IsSelected(GAME_TAG tag) => s_selectedTags.Contains(tag);

        public static void SetSelected(GAME_TAG tag, bool enabled)
        {
            s_selectedTags.Set(tag, enabled);
        }

        public static void Toggle(GAME_TAG tag) => s_selectedTags.Toggle(tag);
        public static bool IsCurrent(GAME_TAG tag) => s_selectedTags.IsCurrent(tag);
        public static bool IsExcluded(GAME_TAG tag) => s_selectedTags.IsExcluded(tag);
        public static List<FilterTerm<GAME_TAG>> Capture() => s_selectedTags.Capture();
        public static void Restore(IEnumerable<FilterTerm<GAME_TAG>> terms) => s_selectedTags.Restore(terms);
        public static void Clear() => s_selectedTags.Clear();

        /// <summary>Retain available keywords and remap aliases after set/format changes.</summary>
        public static void PruneToAvailable(IReadOnlyList<KeywordCatalog.Entry> entries)
        {
            if (entries == null)
            {
                Clear();
                return;
            }

            var canonicalTags = new Dictionary<GAME_TAG, GAME_TAG>();
            for (int i = 0; i < entries.Count; i++)
            {
                KeywordCatalog.Entry entry = entries[i];
                if (!entry.HasCards && !entry.HasReferencedCards) continue;
                for (int t = 0; t < entry.Tags.Count; t++)
                    canonicalTags[entry.Tags[t]] = entry.Tag;
            }

            s_selectedTags.Remap(canonicalTags);
        }

        public static bool MatchesValue(CollectibleCard card, GAME_TAG tag)
        {
            EntityDef def = card?.GetEntityDef();
            return def != null && (KeywordCatalog.MatchesKeyword(def, tag, false) || KeywordCatalog.MatchesKeyword(def, tag, true));
        }
        public static FilterBitSet MatchSet(System.Func<GAME_TAG, FilterBitSet> lookup, int count) => s_selectedTags.MatchSet(lookup, count);
        public static FilterBitSet CandidateContextSet(System.Func<GAME_TAG, FilterBitSet> lookup, int count) => s_selectedTags.CandidateContextSet(lookup, count);

        public static bool MatchesCandidate(CollectibleCard card, GAME_TAG candidate)
        {
            EntityDef def = card?.GetEntityDef();
            return def != null && s_selectedTags.MatchesCandidate(candidate, tag => KeywordCatalog.MatchesKeyword(def, tag, false)
                || KeywordCatalog.MatchesKeyword(def, tag, true));
        }

        public static bool Matches(CollectibleCard card)
        {
            if (!IsActive) return true;
            if (card == null) return false;
            EntityDef def = card.GetEntityDef();
            if (def == null) return false;

            return s_selectedTags.Matches(tag => KeywordCatalog.MatchesKeyword(def, tag, referenced: false)
                || KeywordCatalog.MatchesKeyword(def, tag, referenced: true));
        }

        public static string Describe() => KeywordCatalog.FormatSelectedDebug(s_selectedTags.Values);
    }
}
