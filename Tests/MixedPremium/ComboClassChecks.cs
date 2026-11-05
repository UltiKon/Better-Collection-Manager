using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckComboClassBuckets(CollectibleCard a, CollectibleCard b, CategoryCatalog.Combo combo)
    {
        var manager = CollectionManager.Get();
        bool editing = manager.Editing;
        CollectionDeck deck = manager.EditedDeck;
        var classRule = manager.Rules.ClassAllowed;
        try
        {
            ExtraFilterState.ClearAll(); ExtraFilterState.ToggleCategory(CuratedCategory.Combos);
            manager.Editing = false;
            var buckets = new Dictionary<TAG_CLASS, List<CollectibleCard>> {
                [TAG_CLASS.MAGE] = new List<CollectibleCard> { a },
                [TAG_CLASS.PALADIN] = new List<CollectibleCard> { a, b },
                [TAG_CLASS.NEUTRAL] = new List<CollectibleCard>()
            };
            var append = ComboCollectionCards.Prepare(buckets);
            foreach (var list in buckets.Values) list.Clear(); append();
            Check(buckets[TAG_CLASS.MAGE].Count == 0 && buckets[TAG_CLASS.PALADIN].Any(card => ComboCollectionCards.StableId(card).Contains(combo.Id)),
                "A combo follows the common component class rather than the first dual-class carrier bucket");
            buckets[TAG_CLASS.MAGE] = new List<CollectibleCard> { a };
            buckets[TAG_CLASS.PALADIN] = new List<CollectibleCard> { b };
            append = ComboCollectionCards.Prepare(buckets); foreach (var list in buckets.Values) list.Clear(); append();
            Check(buckets.Values.SelectMany(list => list).Any(card => ComboCollectionCards.StableId(card).Contains(combo.Id)),
                "Cross-class combos remain discoverable in Combos while browsing outside a deck");
            manager.Editing = true;
            manager.EditedDeck = new CollectionDeck { Classes = new List<TAG_CLASS> { TAG_CLASS.MAGE } };
            buckets[TAG_CLASS.MAGE] = new List<CollectibleCard> { a };
            buckets[TAG_CLASS.PALADIN] = new List<CollectibleCard> { a, b };
            ComboCollectionCards.Prepare(buckets)();
            Check(buckets[TAG_CLASS.PALADIN].Count == 0 && !buckets[TAG_CLASS.MAGE].Any(card => ComboCollectionCards.StableId(card).Contains(combo.Id)),
                "Deck editing excludes foreign class buckets and combos needing a foreign component");
            manager.EditedDeck.TouristClasses.Add(TAG_CLASS.PALADIN);
            buckets[TAG_CLASS.MAGE] = new List<CollectibleCard> { a };
            buckets[TAG_CLASS.PALADIN] = new List<CollectibleCard> { a, b };
            manager.Rules.ClassAllowed = definition => definition.GetCardId() != b.CardId;
            append = ComboCollectionCards.Prepare(buckets); foreach (var list in buckets.Values) list.Clear(); append();
            Check(buckets.Values.All(list => list.Count == 0), "Native class restrictions still reject a combo inside a Tourist class bucket");
            manager.Rules.ClassAllowed = _ => true;
            buckets[TAG_CLASS.MAGE] = new List<CollectibleCard> { a };
            buckets[TAG_CLASS.PALADIN] = new List<CollectibleCard> { a, b };
            append = ComboCollectionCards.Prepare(buckets); foreach (var list in buckets.Values) list.Clear(); append();
            Check(buckets[TAG_CLASS.PALADIN].Count > 0, "Legal Tourist-class combos remain available without applying ownership or deck-size limits to visibility");
        }
        finally { manager.Editing = editing; manager.EditedDeck = deck; manager.Rules.ClassAllowed = classRule; ExtraFilterState.ClearAll(); }
    }
}
