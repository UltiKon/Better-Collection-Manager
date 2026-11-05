using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckComboManaSorting(CategoryCatalog.Combo combo)
    {
        combo = CategoryCatalog.Combos.Single(entry => entry.Id == combo.Id);
        CollectionManager manager = CollectionManager.Get();
        CollectibleCard first = ComboCollectionCards.ComponentCards(combo, combo.Cards.Single(card => card.Count == 2).Id)[0];
        CollectibleCard second = ComboCollectionCards.ComponentCards(combo, combo.Cards.Single(card => card.Count == 1).Id)[0];
        int firstCost = first.ManaCost, secondCost = second.ManaCost;
        int? savedMana = combo.Appearance.ManaCost;
        bool selected = ExtraFilterState.HasCategory(combo.Category);
        string extraCombo = null;
        if (!selected) ExtraFilterState.ToggleCategory(combo.Category);
        try
        {
            foreach (var costs in new[] { (0, 0, 0), (2, 4, 8), (7, 4, 18) })
            foreach (int? custom in new int?[] { null, 0, 3, 12 })
            {
                combo.Appearance.ManaCost = custom;
                int expected = custom ?? costs.Item3;
                first.ManaCost = costs.Item1; second.ManaCost = costs.Item2;
                CollectibleCard At(string id, int mana)
                { CollectibleCard card = Card(id, TAG_PREMIUM.NORMAL, 2); card.ManaCost = mana; card.Name = id; return card; }
                var sameName = At("SameName", 8); sameName.Name = combo.Name;
                var ordinary = new List<CollectibleCard> { first, second, At("BeforeCombo", 7), At("A first", 8),
                    sameName, At("Z last", 8), At("AfterCombo", 9), At("TwelveMana", 12) };
                ordinary = ordinary.OrderBy(card => card.ManaCost).ThenBy(card => card.Name ?? card.CardId, StringComparer.CurrentCultureIgnoreCase).ToList();
                var result = new List<CollectibleCard>(ordinary);
                var buckets = new Dictionary<int, List<CollectibleCard>> { [1] = result };
                ComboCollectionCards.Prepare(buckets)();
                CollectibleCard proxy = result.Single(card => ComboCollectionCards.StableId(card) == "BCM_COMBO_" + combo.Id);
                int index = result.IndexOf(proxy);
                Check(proxy.ManaCost == expected && first.ManaCost == costs.Item1 && second.ManaCost == costs.Item2,
                    "The installed native ManaCost getter uses custom costs, including zero, or the sum without changing component cards");
                Check(result.Take(index).All(card => card.ManaCost < expected || (card.ManaCost == expected
                        && StringComparer.CurrentCultureIgnoreCase.Compare(card.Name ?? card.CardId, combo.Name) <= 0))
                    && result.Skip(index + 1).All(card => card.ManaCost > expected || (card.ManaCost == expected
                        && StringComparer.CurrentCultureIgnoreCase.Compare(card.Name ?? card.CardId, combo.Name) > 0))
                    && result.Where(card => card != proxy).SequenceEqual(ordinary),
                    "Combos sort alphabetically among cards of equal summed mana while preserving native order and existing name ties");
                Check(proxy.Name == combo.Name && sameName.Name == combo.Name,
                    "The installed native Name getter exposes the combo title without replacing an ordinary card name");
                Check(result.Count == ordinary.Count + 1 && result.Distinct().Count() == result.Count,
                    "Sorted combo insertion fills a real result slot without replacing a component or duplicating an existing card");
                int scans = manager.GetAllCardsCalls;
                for (int read = 0; read < 20; read++) { int ignored = proxy.ManaCost; }
                Check(manager.GetAllCardsCalls == scans,
                    "Native sorting and repeated mana reads use cached combo totals instead of scanning the collection");
                ComboCollectionCards.Prepare(buckets)();
                Check(result.Count == ordinary.Count + 1 && result.Count(card => ComboCollectionCards.StableId(card) == "BCM_COMBO_" + combo.Id) == 1,
                    "Refreshing a sorted class bucket replaces its old combo proxy rather than accumulating duplicate slots");
            }
            combo.Appearance.ManaCost = null;
            first.ManaCost = 2; second.ManaCost = 4;
            var snapshot = new CategoryDeckImport.Deck();
            snapshot.Cards.Add(first.CardId); snapshot.Cards.Add(second.CardId);
            snapshot.RawCounts[first.CardId] = 2; snapshot.RawCounts[second.CardId] = 1;
            CategoryCatalog.AddCombo(CategoryCatalog.Categories.Single(category => category.Value == combo.Category).Name, "A Combo", "", snapshot);
            extraCombo = CategoryCatalog.Combos.Single(entry => entry.Id != combo.Id).Id;
            var before = Card("BeforeAlpha", TAG_PREMIUM.NORMAL, 2); before.ManaCost = 8; before.Name = "B ordinary";
            var after = Card("AfterAlpha", TAG_PREMIUM.NORMAL, 2); after.ManaCost = 8; after.Name = "Z ordinary";
            var together = new List<CollectibleCard> { first, second, before, after };
            ComboCollectionCards.Prepare(new Dictionary<int, List<CollectibleCard>> { [1] = together })();
            Check(together.Where(card => card.ManaCost == 8).Select(card => card.Name)
                .SequenceEqual(new[] { "A Combo", "B ordinary", combo.Name, "Z ordinary" }),
                "Several combos interleave alphabetically with ordinary cards at equal mana regardless of save order");
        }
        finally
        {
            if (extraCombo != null) CategoryCatalog.DeleteCombo(extraCombo);
            first.ManaCost = firstCost; second.ManaCost = secondCost;
            combo.Appearance.ManaCost = savedMana;
            ExtraFilterState.ClearCategories(); if (selected) ExtraFilterState.ToggleCategory(combo.Category);
            ComboCollectionCards.Prepare(new Dictionary<int, List<CollectibleCard>> { [1] = manager.GetAllCards() })();
        }
    }
}
