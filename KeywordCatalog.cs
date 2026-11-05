using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using PegasusShared;

namespace BetterCollectionManager
{
    /// <summary>
    /// Keywords available for filtering, scoped to Constructed cards in the CM's
    /// currently selected set/format filter. Grouped by mechanic and deduped by
    /// display name (including normalized keyword families); each row has
    /// at least one own-keyword or reference match in the scanned pool. Same-name tags remain aliases so deduping
    /// the menu does not discard cards or keyword references.
    /// </summary>
    internal static class KeywordCatalog
    {
        public readonly struct Entry
        {
            public readonly GAME_TAG Tag;
            public readonly string Name;
            public readonly IReadOnlyList<GAME_TAG> Tags;
            public readonly bool HasCards;
            public readonly bool HasReferencedCards;
            public readonly KeywordGroup Group;
            public readonly bool UsesReferencesAsHas;

            public Entry(GAME_TAG tag, string name, IReadOnlyList<GAME_TAG> tags,
                bool hasCards, bool hasReferencedCards, bool usesReferencesAsHas = false)
            {
                Tag = tag;
                Name = name;
                Tags = tags;
                HasCards = hasCards;
                HasReferencedCards = hasReferencedCards;
                Group = KeywordGroups.GetGroup(tags);
                UsesReferencesAsHas = usesReferencesAsHas;
            }
        }

        public static int Generation { get; private set; }
        private static List<Entry> s_cached;
        private static List<CollectibleCard> s_indexedCards, s_indexedSource;
        private static readonly Dictionary<GAME_TAG, FilterBitSet> s_ownIndex = new Dictionary<GAME_TAG, FilterBitSet>();
        private static readonly Dictionary<GAME_TAG, FilterBitSet> s_referenceIndex = new Dictionary<GAME_TAG, FilterBitSet>();
        private static FormatType s_cachedFormat = FormatType.FT_UNKNOWN;
        private static string s_cachedSetKey = "";
        private static readonly Dictionary<GAME_TAG, Entry> s_entriesByTag =
            new Dictionary<GAME_TAG, Entry>();
        // Numeric IDs also work with game assemblies whose GAME_TAG enum has
        // not yet named these tags. Source: the client's CardDefs metadata.
        private const GAME_TAG PrepareTag = (GAME_TAG)4354;
        private const GAME_TAG DeckActionCostTag = (GAME_TAG)1743;

        private static AccessTools.FieldRef<SetFilterTray, SetFilterItem> s_selectedItem;
        private static AccessTools.FieldRef<CollectibleDisplay, SetFilterTray> s_setFilterTray;
        private static bool s_fieldsReady;

        public static void EnsureFields()
        {
            if (s_fieldsReady)
                return;
            try
            {
                s_selectedItem = AccessTools.FieldRefAccess<SetFilterTray, SetFilterItem>("m_selected");
                s_setFilterTray = AccessTools.FieldRefAccess<CollectibleDisplay, SetFilterTray>("m_setFilterTray");
                s_fieldsReady = true;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordCatalog.EnsureFields failed", ex);
            }
        }

        public static void Invalidate()
        {
            FilterModes.InvalidateResults();
            unchecked { Generation++; }
            s_indexedCards = null; s_indexedSource = null; s_ownIndex.Clear(); s_referenceIndex.Clear(); s_entriesByTag.Clear();
            InvalidateScope();
        }

        // Expansion edits change scope, not card metadata or same-name alias semantics.
        // Preserve both inverted indexes until a native collection/metadata update.
        public static void InvalidateScope()
        {
            s_cached = null;
            s_cachedFormat = FormatType.FT_UNKNOWN;
            s_cachedSetKey = "";
        }

        public static IReadOnlyList<Entry> GetEntries()
        {
            EnsureFields();

            FormatType format;
            HashSet<TAG_CARD_SET> activeSets;
            string setKey;
            ResolveActiveFilter(out format, out activeSets, out setKey);

            if (s_cached != null && s_cachedFormat == format && s_cachedSetKey == setKey)
                return s_cached;

            var list = BuildFromConstructedCards(format, activeSets);
            s_entriesByTag.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                Entry entry = list[i];
                for (int t = 0; t < entry.Tags.Count; t++)
                    s_entriesByTag[entry.Tags[t]] = entry;
            }
            s_cached = list;
            s_cachedFormat = format;
            s_cachedSetKey = setKey;
            DebugLog.Msg(
                $"KeywordCatalog: {list.Count} constructed keywords for format={format} sets={setKey}");
            return list;
        }

        public static bool MatchesKeyword(EntityDef def, GAME_TAG tag, bool referenced)
        {
            if (def == null)
                return false;

            if (s_entriesByTag.TryGetValue(tag, out Entry entry))
            {
                if (referenced && entry.UsesReferencesAsHas)
                    return false;
                for (int i = 0; i < entry.Tags.Count; i++)
                {
                    GAME_TAG alias = entry.Tags[i];
                    if (MatchesTag(def, alias, referenced || entry.UsesReferencesAsHas))
                        return true;
                }
                return false;
            }

            return MatchesTag(def, tag, referenced);
        }

        private static bool MatchesTag(EntityDef def, GAME_TAG tag, bool referenced)
        {
            // Some Prepare cards (e.g. Black Market Auctioneer) encode their
            // own deck action as a referenced Prepare tag plus DECK_ACTION_COST.
            // That marker belongs in Has, rather than effect references such
            // as Wanted Poster or Jailbird. Other keywords keep native semantics.
            if (tag == PrepareTag && def.HasReferencedTag(tag) && def.GetTag(DeckActionCostTag) > 0)
                return !referenced;
            return referenced ? def.HasReferencedTag(tag) : def.HasTag(tag);
        }

        private static void ResolveActiveFilter(
            out FormatType format,
            out HashSet<TAG_CARD_SET> activeSets,
            out string setKey)
        {
            format = FormatType.FT_STANDARD;
            activeSets = new HashSet<TAG_CARD_SET>();
            setKey = "default";

            try
            {
                CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
                CollectionManagerDisplay cmd = display as CollectionManagerDisplay;
                if (cmd != null)
                    format = cmd.CurrentSetFilterFormatType;

                SetFilterTray tray = null;
                if (s_setFilterTray != null && display != null)
                    tray = s_setFilterTray(display);

                SetFilterItem selected = null;
                if (tray != null && s_selectedItem != null)
                    selected = s_selectedItem(tray);

                if (ExpansionFilterState.IsActive)
                {
                    format = FormatType.FT_WILD;
                    activeSets.UnionWith(ExpansionFilterState.NativeCardSets());
                    setKey = "multi-expansion";
                    return;
                }

                if (selected != null)
                {
                    if (selected.FormatType != FormatType.FT_UNKNOWN)
                        format = selected.FormatType;

                    List<TAG_CARD_SET> cardSets = selected.CardSets;
                    if (cardSets != null && cardSets.Count > 0)
                    {
                        for (int i = 0; i < cardSets.Count; i++)
                            activeSets.Add(cardSets[i]);
                        setKey = $"item:{selected.Text}:{cardSets.Count}";
                    }
                    else if (selected.IsAllStandard)
                    {
                        AddSets(activeSets, GameUtils.GetStandardSets());
                        setKey = "all-standard";
                        format = FormatType.FT_STANDARD;
                    }
                }

                if (activeSets.Count == 0)
                {
                    TAG_CARD_SET[] formatSets = GameUtils.GetCardSetsInFormat(format);
                    AddSets(activeSets, formatSets);
                    setKey = $"format:{format}:{activeSets.Count}";
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordCatalog.ResolveActiveFilter failed", ex);
                AddSets(activeSets, GameUtils.GetStandardSets());
                setKey = "fallback-standard";
                format = FormatType.FT_STANDARD;
            }
        }

        private static void AddSets(HashSet<TAG_CARD_SET> dst, TAG_CARD_SET[] src)
        {
            if (src == null)
                return;
            for (int i = 0; i < src.Length; i++)
                dst.Add(src[i]);
        }

        private static List<Entry> BuildFromConstructedCards(FormatType format, HashSet<TAG_CARD_SET> activeSets)
        {
            var list = new List<Entry>();

            List<KeywordTextDbfRecord> records = GameDbf.KeywordText?.GetRecords();
            if (records == null || records.Count == 0)
            {
                DebugLog.Warning("KeywordCatalog: KeywordText DBF empty");
                return list;
            }

            // Candidate tags → display name (skip raw keys / excluded modes).
            var candidates = new List<(GAME_TAG tag, string name)>();
            var seenTag = new HashSet<GAME_TAG>();
            for (int i = 0; i < records.Count; i++)
            {
                KeywordTextDbfRecord rec = records[i];
                if (rec == null || rec.Tag == 0)
                    continue;
                GAME_TAG tag = (GAME_TAG)rec.Tag;
                if (!seenTag.Add(tag))
                    continue;
                if (IsExcludedTag(tag))
                    continue;
                if (!GameStrings.HasKeywordName(tag))
                    continue;

                string name = GameStrings.GetKeywordName(tag);
                if (!IsUsableDisplayName(name))
                    continue;

                candidates.Add((tag, KeywordGroups.GetDisplayName(tag, name.Trim())));
            }

            List<CollectibleCard> cards = CollectionManager.Get()?.GetAllCards();
            // A collection refresh can replace cards without changing the selected set.
            // Checking references is cheap and keeps newly added/replaced cards visible.
            bool sameSource = cards != null && s_indexedSource != null && cards.Count == s_indexedSource.Count;
            if (sameSource)
                for (int i = 0; i < cards.Count; i++)
                    if (!ReferenceEquals(cards[i], s_indexedSource[i])) { sameSource = false; break; }
            if (s_indexedCards != null && !sameSource) Invalidate();
            if (cards == null || cards.Count == 0)
            {
                DebugLog.Warning("KeywordCatalog: GetAllCards empty");
                return list;
            }
            if (s_indexedCards == null)
            {
                s_indexedSource = new List<CollectibleCard>(cards);
                s_indexedCards = new List<CollectibleCard>();
                var definitions = new List<EntityDef>();
                foreach (CollectibleCard card in cards)
                    if (IsConstructedCardInScope(card, FormatType.FT_WILD, null))
                    { s_indexedCards.Add(card); definitions.Add(card.GetEntityDef()); }
                foreach (var candidate in candidates)
                {
                    GAME_TAG tag = candidate.tag;
                    s_ownIndex[tag] = FilterBitSet.Build(definitions.Count,
                        i => definitions[i] != null && MatchesTag(definitions[i], tag, false));
                    s_referenceIndex[tag] = FilterBitSet.Build(definitions.Count,
                        i => definitions[i] != null && MatchesTag(definitions[i], tag, true));
                }
            }
            FilterBitSet scope = FilterBitSet.Build(s_indexedCards.Count, i =>
                IsConstructedCardInScope(s_indexedCards[i], format, activeSets) && ExpansionFilterState.Matches(s_indexedCards[i]));
            FilterBitSet all = FilterBitSet.Full(s_indexedCards.Count);
            int scanned = scope.IntersectionCount(all);
            var hasCounts = new Dictionary<GAME_TAG, int>();
            var refCounts = new Dictionary<GAME_TAG, int>();
            var globalOwnTags = new HashSet<GAME_TAG>();
            foreach (var candidate in candidates)
            {
                GAME_TAG tag = candidate.tag;
                hasCounts[tag] = s_ownIndex[tag].IntersectionCount(scope);
                refCounts[tag] = s_referenceIndex[tag].IntersectionCount(scope);
                if (s_ownIndex[tag].Intersects(all)) globalOwnTags.Add(tag);
            }

            int beforeDedupe = 0;
            // Keep every same-name/family alias, including aliases with no direct hits:
            // those can still be used by HasReferencedTag. Pick a representative
            // only for selection identity and menu ordering, never for matching.
            var bestByName = new Dictionary<string, (GAME_TAG tag, string name, int count)>(
                StringComparer.CurrentCultureIgnoreCase);
            var aliasesByName = new Dictionary<string, List<GAME_TAG>>(
                StringComparer.CurrentCultureIgnoreCase);

            for (int k = 0; k < candidates.Count; k++)
            {
                GAME_TAG tag = candidates[k].tag;
                string name = candidates[k].name;
                int count = hasCounts[tag];
                if (!aliasesByName.TryGetValue(name, out List<GAME_TAG> aliases))
                {
                    aliases = new List<GAME_TAG>();
                    aliasesByName[name] = aliases;
                }
                aliases.Add(tag);
                if (count < 1 && refCounts[tag] < 1)
                    continue;
                beforeDedupe++;

                string key = name;
                if (!bestByName.TryGetValue(key, out var existing))
                {
                    bestByName[key] = (tag, name, count);
                    continue;
                }

                if (PreferTag(tag, count, existing.tag, existing.count))
                    bestByName[key] = (tag, name, count);
            }

            foreach (var kv in bestByName)
            {
                bool hasCards = false;
                bool hasReferencedCards = false;
                bool hasGlobalOwnCards = false;
                List<GAME_TAG> aliases = aliasesByName[kv.Key];
                for (int i = 0; i < aliases.Count; i++)
                {
                    hasCards |= hasCounts[aliases[i]] > 0;
                    hasReferencedCards |= refCounts[aliases[i]] > 0;
                    hasGlobalOwnCards |= globalOwnTags.Contains(aliases[i]);
                }
                // Some mechanics (e.g. Adapt) only use reference metadata.
                // Offer those under Has; a set lacking ordinary keyword cards
                // must not change the meaning of true reference filters.
                bool usesReferencesAsHas = !hasGlobalOwnCards && hasReferencedCards;
                list.Add(new Entry(kv.Value.tag, kv.Value.name,
                    aliases.AsReadOnly(), hasCards || usesReferencesAsHas,
                    hasReferencedCards && !usesReferencesAsHas, usesReferencesAsHas));
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            DebugLog.Msg(
                $"KeywordCatalog: scanned={scanned} candidates={candidates.Count} " +
                $"withMatches≥1={beforeDedupe} afterDedupe={list.Count}");
            return list;
        }

        /// <summary>Prefer higher card count; break ties by dropping TOOLTIP / REF enum names.</summary>
        private static bool PreferTag(GAME_TAG candidate, int candCount, GAME_TAG existing, int existCount)
        {
            if (candCount != existCount)
                return candCount > existCount;

            int candScore = TagPreferenceScore(candidate);
            int existScore = TagPreferenceScore(existing);
            if (candScore != existScore)
                return candScore > existScore;

            // Stable: lower enum int wins.
            return (int)candidate < (int)existing;
        }

        private static int TagPreferenceScore(GAME_TAG tag)
        {
            string n = tag.ToString();
            int score = 0;
            if (n.IndexOf("TOOLTIP", StringComparison.OrdinalIgnoreCase) >= 0)
                score -= 100;
            if (n.IndexOf("REFERENCED", StringComparison.OrdinalIgnoreCase) >= 0)
                score -= 50;
            if (n.StartsWith("KEYWORD_", StringComparison.OrdinalIgnoreCase)
                || n.IndexOf("_KEYWORD", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 10;
            return score;
        }

        private static bool IsUsableDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string t = name.Trim();
            if (t.Length == 0 || t == "UNKNOWN")
                return false;
            // Raw localization keys
            if (t.StartsWith("GLOBAL_", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("GAMEPLAY_", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("GLUE_", StringComparison.OrdinalIgnoreCase))
                return false;
            if (t.IndexOf("KEYWORD_", StringComparison.OrdinalIgnoreCase) >= 0
                && t.IndexOf(' ') < 0)
                return false;
            if (IsExcludedKeywordName(t))
                return false;
            return true;
        }

        internal static bool IsConstructedCardInScope(
            CollectibleCard card,
            FormatType format,
            HashSet<TAG_CARD_SET> activeSets)
        {
            if (card == null)
                return false;
            if (card.IsHeroSkin || card.IsMercenaryAbility)
                return false;

            TAG_CARDTYPE type = card.CardType;
            if (type == TAG_CARDTYPE.ENCHANTMENT
                || type == TAG_CARDTYPE.GAME_MODE_BUTTON
                || type == TAG_CARDTYPE.MOVE_MINION_HOVER_TARGET
                || type == TAG_CARDTYPE.LETTUCE_ABILITY
                || type == TAG_CARDTYPE.BATTLEGROUND_HERO_BUDDY
                || type == TAG_CARDTYPE.BATTLEGROUND_QUEST_REWARD
                || type == TAG_CARDTYPE.BATTLEGROUND_ANOMALY
                || type == TAG_CARDTYPE.BATTLEGROUND_SPELL
                || type == TAG_CARDTYPE.BATTLEGROUND_TRINKET
                || type == TAG_CARDTYPE.HERO_POWER)
                return false;

            TAG_CARD_SET set = card.Set;
            if (activeSets != null && activeSets.Count > 0 && !activeSets.Contains(set))
                return false;

            if (format != FormatType.FT_UNKNOWN && format != FormatType.FT_WILD)
            {
                EntityDef def = card.GetEntityDef();
                if (def != null && !GameUtils.IsCardValidForFormat(format, def))
                    return false;
            }

            return true;
        }

        private static bool IsExcludedTag(GAME_TAG tag)
        {
            // Uldir's encounter tracker is not a card mechanic (Runes Required).
            if ((int)tag == 4947)
                return true;
            string n = tag.ToString();
            if (string.IsNullOrEmpty(n))
                return true;
            if (n.StartsWith("BACON", StringComparison.Ordinal)
                || n.StartsWith("LETTUCE", StringComparison.Ordinal)
                || n.StartsWith("MERCENARY", StringComparison.Ordinal)
                || n.StartsWith("GEAR", StringComparison.Ordinal)
                || n.Contains("BATTLEGROUNDS"))
                return true;
            return false;
        }

        private static bool IsExcludedKeywordName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return true;
            if (name.StartsWith("BGs ", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("BG ", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("Battleground", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Mercenar", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public static string FormatSelectedDebug(IEnumerable<GAME_TAG> tags)
        {
            var sb = new StringBuilder("keywords=[");
            bool first = true;
            if (tags != null)
                foreach (GAME_TAG tag in tags)
                {
                    if (!first) sb.Append(',');
                    sb.Append(tag);
                    first = false;
                }
            return sb.Append(']').ToString();
        }
    }
}
