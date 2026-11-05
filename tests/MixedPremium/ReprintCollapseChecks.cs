using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckReprintCollapse()
    {
        var savedManager = CollectionManager.Instance;
        var savedOrder = PreferredPremiumPrefs.Order;
        var savedMode = CollectionQualitySettings.Display;
        try
        {
            var manager = new CollectionManager();
            CollectionManager.Instance = manager;
            PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
            var original = Owned("original", TAG_PREMIUM.NORMAL, 2);
            var reprintNormal = Owned("reprint", TAG_PREMIUM.NORMAL, 1);
            var reprintGolden = Owned("reprint", TAG_PREMIUM.GOLDEN, 1);
            var unrelated = Owned("unrelated", TAG_PREMIUM.NORMAL, 2);
            // Equivalence comes from client metadata, not names or ID prefixes.
            original.Name = reprintNormal.Name = reprintGolden.Name = unrelated.Name = "Same name";
            GameUtils.CounterpartCardIds["original"] = new List<string> { "reprint" };
            GameUtils.CounterpartCardIds["reprint"] = new List<string> { "original" };
            foreach (QualityDisplayMode mode in Enum.GetValues(typeof(QualityDisplayMode)))
            {
                CollectionQualitySettings.Display = mode;
                PreferredQualityCollapse.BeginUpdate();
                var source = new[] { original, unrelated, reprintNormal, reprintGolden };
                var cards = source.ToList();
                int removed = PreferredQualityCollapse.CollapseInPlace(cards, "reprints", null);
                Check(removed == source.Length - cards.Count && cards.Contains(unrelated),
                    "Reprint collapse reports removals and retains unrelated cards with the same name");
                bool preferPair = mode == QualityDisplayMode.PreferTwoCopies;
                string selected = preferPair ? "original" : "reprint";
                Check(cards.Where(card => card != unrelated).All(card => card.CardId == selected)
                    && cards.Contains(preferPair ? original : reprintGolden),
                    "One native edition survives according to quality/playset preference in " + mode);
                Check(PreferredQualityCollapse.TryGetVariants(selected, out var variants)
                    && variants.All(card => card.CardId == selected)
                    && !PreferredQualityCollapse.TryGetVariants(preferPair ? "reprint" : "original", out _),
                    "Deck input caches only real variants belonging to the chosen edition");
                if (mode == QualityDisplayMode.Split || mode == QualityDisplayMode.Stacking)
                    Check(PreferredQualityCollapse.TryGetMixed("reprint", out var pair)
                        && (mode == QualityDisplayMode.Split
                            ? pair.Golden == reprintGolden && pair.Normal == reprintNormal
                            : pair.Preferred == reprintGolden && pair.Secondary == reprintNormal),
                        "Reprint selection preserves the selected edition's split/stacked deck copies");
                int calls = GameUtils.CounterpartCardIdCalls;
                var sibling = source.ToList();
                PreferredQualityCollapse.CollapseInPlace(sibling, "rune sibling", null);
                Check(cards.SequenceEqual(sibling) && GameUtils.CounterpartCardIdCalls == calls,
                    "Class/rune sibling buckets reuse counterpart metadata and produce the same edition");
            }
            Check(original.OwnedCount == 2 && reprintNormal.OwnedCount == 1 && reprintGolden.OwnedCount == 1,
                "Reprint deduplication never combines or mutates inventory counts");

            CollectionQualitySettings.Display = QualityDisplayMode.HighestOnly;
            PreferredQualityCollapse.BeginUpdate();
            var filtered = new List<CollectibleCard> { original };
            PreferredQualityCollapse.CollapseInPlace(filtered, "single expansion", null);
            Check(filtered.SequenceEqual(new[] { original }),
                "Counterpart metadata never introduces editions excluded by a set/format/search filter");

            // A missing intermediate edition can connect a transitive family.
            GameUtils.CounterpartCardIds["original"] = new List<string> { "filtered-out" };
            GameUtils.CounterpartCardIds["filtered-out"] = new List<string> { "original", "reprint" };
            GameUtils.CounterpartCardIds["reprint"] = new List<string>();
            PreferredQualityCollapse.BeginUpdate();
            var transitive = new List<CollectibleCard> { original, unrelated, reprintGolden };
            PreferredQualityCollapse.CollapseInPlace(transitive, "transitive", null);
            Check(transitive.SequenceEqual(new[] { unrelated, reprintGolden }),
                "Directed/transitive native links deduplicate even with an intermediate edition filtered out");

            PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
            PreferredQualityCollapse.BeginUpdate();
            var normalFirst = new List<CollectibleCard> { reprintNormal, reprintGolden, original };
            PreferredQualityCollapse.CollapseInPlace(normalFirst, "normal first", null);
            Check(normalFirst.SequenceEqual(new[] { original }),
                "Custom premium order is honored and a complete playset wins within the same quality");
            reprintNormal.OwnedCount = manager.Counts[("reprint", TAG_PREMIUM.NORMAL)] = 2;
            PreferredQualityCollapse.BeginUpdate();
            normalFirst = new List<CollectibleCard> { reprintNormal, original };
            PreferredQualityCollapse.CollapseInPlace(normalFirst, "tied editions", null);
            Check(normalFirst.SequenceEqual(new[] { reprintNormal }), "Equal editions retain native ordering");

            PreferredPremiumPrefs.Order = savedOrder;
            var unowned = Owned("reprint", TAG_PREMIUM.SIGNATURE, 0);
            PreferredQualityCollapse.BeginUpdate();
            var ownership = new List<CollectibleCard> { unowned, original };
            PreferredQualityCollapse.CollapseInPlace(ownership, "owned edition", null);
            Check(ownership.SequenceEqual(new[] { original }), "An unowned reprint cannot displace an owned edition");

            // New native metadata is observed on the next full result rebuild.
            GameUtils.CounterpartCardIds.Clear();
            PreferredQualityCollapse.BeginUpdate();
            var independent = new List<CollectibleCard> { original, reprintGolden,
                Owned("CORE_original", TAG_PREMIUM.NORMAL, 2), Owned("VAN_original", TAG_PREMIUM.NORMAL, 2) };
            Check(PreferredQualityCollapse.CollapseInPlace(independent, "independent", null) == 0,
                "Names and Core/Classic-looking prefixes alone never merge cards without native equivalence");

            CollectibleCard Owned(string id, TAG_PREMIUM premium, int count)
            {
                var card = Card(id, premium, count);
                manager.Cards[(id, premium)] = card;
                manager.Counts[(id, premium)] = count;
                return card;
            }
        }
        finally
        {
            GameUtils.CounterpartCardIds.Clear();
            CollectionManager.Instance = savedManager;
            PreferredPremiumPrefs.Order = savedOrder;
            CollectionQualitySettings.Display = savedMode;
            PreferredQualityCollapse.BeginUpdate();
        }
    }
}
