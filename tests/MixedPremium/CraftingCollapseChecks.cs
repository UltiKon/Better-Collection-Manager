using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckCraftingMissingCards()
    {
        var savedManager = CollectionManager.Instance;
        var savedDisplay = CollectionQualitySettings.Display;
        try
        {
            var manager = CollectionManager.Instance = new CollectionManager();
            var source = new List<CollectibleCard>();
            void Add(string id, TAG_PREMIUM premium, int owned)
            {
                var card = Card(id, premium, owned); source.Add(card);
                manager.Cards[(id, premium)] = card;
                manager.Counts[(id, premium)] = owned;
            }
            Add("owned-craft", TAG_PREMIUM.NORMAL, 0);
            Add("owned-craft", TAG_PREMIUM.GOLDEN, 2);
            for (int i = 0; i < 17; i++)
            {
                Add("missing-craft-" + i, TAG_PREMIUM.NORMAL, 0);
                Add("missing-craft-" + i, TAG_PREMIUM.GOLDEN, 0);
            }
            foreach (QualityDisplayMode mode in Enum.GetValues(typeof(QualityDisplayMode)))
            {
                CollectionQualitySettings.Display = mode;
                var results = source.ToList();
                PreferredQualityCollapse.BeginUpdate();
                PreferredQualityCollapse.CollapseInPlace(results, "crafting owned and missing", null);
                Check(results.Count == 18 && results.Single(card => card.CardId == "owned-craft").PremiumType == TAG_PREMIUM.GOLDEN,
                    "Crafting collapse keeps the preferred owned quality and every missing card in " + mode);
                var missing = results.Where(card => card.CardId.StartsWith("missing-craft-")).ToArray();
                Check(missing.Length == 17 && missing.All(card => card.PremiumType == TAG_PREMIUM.NORMAL && card.OwnedCount == 0),
                    "Wholly missing cards retain their real native zero-owned Normal variant");
                Check(missing.Skip(16).Single().CardId == "missing-craft-16" && source.Sum(card => card.OwnedCount) == 2,
                    "Missing cards fill later pages without hiding the final card or changing inventory");
                var premiumOnly = source.Where(card => card.OwnedCount == 0 && card.PremiumType == TAG_PREMIUM.GOLDEN).ToList();
                PreferredQualityCollapse.BeginUpdate();
                PreferredQualityCollapse.CollapseInPlace(premiumOnly, "native missing premium filter", null);
                Check(premiumOnly.Count == 17 && premiumOnly.All(card => card.PremiumType == TAG_PREMIUM.GOLDEN),
                    "Native missing-quality filters never gain excluded Normal or owned variants");
            }
        }
        finally
        {
            CollectionManager.Instance = savedManager;
            CollectionQualitySettings.Display = savedDisplay;
            PreferredQualityCollapse.BeginUpdate();
        }
    }
}
