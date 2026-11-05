using System.Collections.Generic;
using System.Text;

namespace BetterCollectionManager
{
    /// <summary>
    /// Collapses native deck-equivalent reprints to one edition, then selects
    /// that edition's preferred owned premium(s).
    /// Mixed Normal/Golden commons, rares and epics share a split tile only
    /// when a second premium would otherwise be needed for two deck copies.
    /// Stacking uses complete actors for the same second-copy rule across qualities.
    /// Other non-legendary cards retain up to two entries when the top preferred
    /// quality has only 1 owned copy and a lower preferred quality also has owned copies
    /// (so the collection can still supply 2 deck copies).
    /// </summary>
    internal static class PreferredQualityCollapse
    {
        private static readonly Dictionary<string, MixedPremiumPair> s_mixed =
            new Dictionary<string, MixedPremiumPair>();
        private static readonly HashSet<string> s_singleTiles = new HashSet<string>();
        private static readonly Dictionary<string, IReadOnlyList<CollectibleCard>> s_variants =
            new Dictionary<string, IReadOnlyList<CollectibleCard>>();

        public static void BeginUpdate()
        {
            s_mixed.Clear();
            s_singleTiles.Clear();
            s_variants.Clear();
            s_counterparts.Clear();
        }

        public static bool TryGetVariants(string cardId, out IReadOnlyList<CollectibleCard> variants)
        {
            variants = null;
            return cardId != null && s_variants.TryGetValue(cardId, out variants);
        }

        public static bool IsSingleTile(string cardId) => cardId != null && s_singleTiles.Contains(cardId);

        public static bool TryGetMixed(string cardId, out MixedPremiumPair pair)
        {
            pair = null;
            return cardId != null && s_mixed.TryGetValue(cardId, out pair);
        }

        // Inventory-only refreshes must leave all other cached tile decisions intact.
        internal static void RefreshCard(string cardId, List<CollectibleCard> variants)
        {
            s_mixed.Remove(cardId);
            s_singleTiles.Remove(cardId);
            s_variants.Remove(cardId);
            CollapseInPlace(variants, "inspected " + cardId, null);
        }

        public static int CollapseInPlace(List<CollectibleCard> cards, string listLabel, List<string> sampleOut)
        {
            if (cards == null || cards.Count == 0)
                return 0;

            int before = cards.Count;
            var groups = new Dictionary<string, List<CollectibleCard>>();
            var order = new List<string>();

            for (int i = 0; i < cards.Count; i++)
            {
                CollectibleCard card = cards[i];
                if (card == null)
                    continue;

                string cardId = card.CardId;
                if (string.IsNullOrEmpty(cardId))
                    continue;

                List<CollectibleCard> group;
                if (!groups.TryGetValue(cardId, out group))
                {
                    group = new List<CollectibleCard>(4);
                    groups[cardId] = group;
                    order.Add(cardId);
                }
                group.Add(card);
            }

            // Select whole editions: premium actors, counters and deck input all
            // refer to a real CardId. Mixing inventories from counterpart IDs
            // would report copies/qualities that this edition does not own.
            Dictionary<string, string> editions = SelectReprintEditions(groups, order);
            cards.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                string cardId = order[i];
                if (editions[cardId] != cardId)
                    continue;
                List<CollectibleCard> group = groups[cardId];
                CollectibleCard chosen = EditionQuality(group);
                if (chosen == null)
                    continue;

                s_variants[cardId] = group.AsReadOnly();
                QualityDisplayMode mode = CollectionQualitySettings.Display;
                CollectibleCard second = mode == QualityDisplayMode.HighestOnly || mode == QualityDisplayMode.PreferTwoCopies
                    ? null : PreferredPremiumHelper.ChooseSecondCopyForDeck(group, chosen);
                MixedPremiumPair mixed = mode == QualityDisplayMode.Stacking ? MixedPremiumPair.Stack(chosen, second)
                    : mode == QualityDisplayMode.Split ? MixedPremiumPair.Find(chosen, second) : null;
                if (mixed != null && MixedPremiumCard.CanMerge(cardId))
                {
                    s_mixed[cardId] = mixed;
                    s_singleTiles.Add(cardId);
                    cards.Add(chosen);
                    if (sampleOut != null && sampleOut.Count < 12)
                        sampleOut.Add($"{cardId}->{(mixed.IsStacking ? "stacked" : "Golden/Normal split")} tile");
                    continue;
                }

                // Dual-copy: lower preferred premium first, then higher (e.g. Normal then Golden).
                if (second != null)
                {
                    cards.Add(second);
                    cards.Add(chosen);
                    if (sampleOut != null && sampleOut.Count < 12)
                    {
                        sampleOut.Add(
                            $"{cardId}->{PreferredPremiumHelper.PremiumName(second.PremiumType)}+" +
                            $"{PreferredPremiumHelper.PremiumName(chosen.PremiumType)} (2-copy lower→higher)");
                    }
                }
                else
                {
                    s_singleTiles.Add(cardId);
                    cards.Add(chosen);
                    if (sampleOut != null && sampleOut.Count < 12 && group.Count > 1)
                    {
                        sampleOut.Add(
                            $"{cardId}->{PreferredPremiumHelper.PremiumName(chosen.PremiumType)} (from {group.Count} premiums)");
                    }
                    else if (sampleOut != null && DebugLog.VerboseDebug && sampleOut.Count < 8)
                    {
                        sampleOut.Add($"{cardId}->{PreferredPremiumHelper.PremiumName(chosen.PremiumType)}");
                    }
                }
            }

            int removed = before - cards.Count;
            DebugLog.Verbose($"  collapse [{listLabel}]: {before} -> {cards.Count} (removed {removed})");
            return removed;
        }

        private static Dictionary<string, string> SelectReprintEditions(
            Dictionary<string, List<CollectibleCard>> groups, List<string> order)
        {
            var parents = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var pending = new Queue<string>(order);
            var visited = new HashSet<string>(System.StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id)) continue;
                string root = Root(id);
                // Follow metadata through editions excluded by the current
                // query, but only choose cards actually in the filtered list.
                foreach (string counterpart in Counterparts(id))
                {
                    if (string.IsNullOrEmpty(counterpart)) continue;
                    string other = Root(counterpart);
                    if (root != other) parents[other] = root;
                    pending.Enqueue(counterpart);
                }
            }

            var selected = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (string id in order)
            {
                string root = Root(id);
                if (!selected.TryGetValue(root, out string previous)
                    || PreferEdition(groups[id], groups[previous])) selected[root] = id;
            }
            var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (string id in order) result[id] = selected[Root(id)];
            return result;

            string Root(string id)
            {
                if (!parents.TryGetValue(id, out string parent)) return parents[id] = id;
                if (parent != id) parents[id] = Root(parent);
                return parents[id];
            }
        }

        // Native counterpart metadata is shared by class/rune result buckets.
        // Scope the cache to one update so client metadata changes are observed.
        private static readonly Dictionary<string, List<string>> s_counterparts =
            new Dictionary<string, List<string>>(System.StringComparer.Ordinal);

        private static List<string> Counterparts(string id)
        {
            if (!s_counterparts.TryGetValue(id, out List<string> counterparts))
            {
                try { counterparts = GameUtils.GetCounterpartCardIds(id); }
                catch (System.Exception ex)
                {
                    DebugLog.Exception("Reprint metadata unavailable for " + id, ex);
                }
                s_counterparts[id] = counterparts ?? new List<string>();
            }
            return s_counterparts[id];
        }

        private static bool PreferEdition(List<CollectibleCard> candidate, List<CollectibleCard> previous)
        {
            CollectibleCard next = EditionQuality(candidate), current = EditionQuality(previous);
            CollectionManager manager = CollectionManager.Get();
            int nextOwned = PreferredPremiumHelper.GetOwnedCount(manager, next, next.CardId);
            int currentOwned = PreferredPremiumHelper.GetOwnedCount(manager, current, current.CardId);
            // An unowned catalog entry must never displace a usable edition.
            if ((nextOwned > 0) != (currentOwned > 0)) return nextOwned > 0;
            bool preferPairs = CollectionQualitySettings.Display == QualityDisplayMode.PreferTwoCopies
                && !PreferredPremiumHelper.IsLegendary(next);
            if (preferPairs && (nextOwned >= 2) != (currentOwned >= 2)) return nextOwned >= 2;
            int nextRank = PremiumRank(next.PremiumType), currentRank = PremiumRank(current.PremiumType);
            if (nextRank != currentRank) return nextRank < currentRank;
            // A playset wins ties, but excess copies do not reorder editions.
            // Otherwise retain the native result order for stable page slots.
            return System.Math.Min(2, nextOwned) > System.Math.Min(2, currentOwned);
        }

        private static CollectibleCard EditionQuality(List<CollectibleCard> group)
        {
            CollectibleCard chosen = PreferredPremiumHelper.ChoosePreferred(group);
            if (CollectionQualitySettings.Display != QualityDisplayMode.PreferTwoCopies
                || PreferredPremiumHelper.IsLegendary(chosen)) return chosen;
            var pairs = new List<CollectibleCard>();
            foreach (CollectibleCard variant in group)
                if (PreferredPremiumHelper.GetOwnedCount(CollectionManager.Get(), variant, variant.CardId) >= 2)
                    pairs.Add(variant);
            return pairs.Count > 0 ? PreferredPremiumHelper.ChoosePreferred(pairs) : chosen;
        }

        private static int PremiumRank(TAG_PREMIUM premium)
        {
            PreferredPremiumPrefs.EnsureLoaded();
            IReadOnlyList<TAG_PREMIUM> order = PreferredPremiumPrefs.Order;
            for (int i = 0; i < order.Count; i++) if (order[i] == premium) return i;
            return order.Count;
        }

        public static string FormatSample(List<string> sample)
        {
            if (sample == null || sample.Count == 0)
                return "(no sample)";
            var sb = new StringBuilder();
            for (int i = 0; i < sample.Count; i++)
            {
                if (i > 0)
                    sb.Append("; ");
                sb.Append(sample[i]);
            }
            return sb.ToString();
        }
    }
}
