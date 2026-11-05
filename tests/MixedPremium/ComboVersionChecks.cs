using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckComboVersions(CategoryCatalog.Combo saved)
    {
        CollectionManager manager = CollectionManager.Get();
        var previousCards = manager.Cards.ToArray(); var previousCounts = manager.Counts.ToArray();
        var previousDeck = manager.EditedDeck; var previousTray = CollectionDeckTray.Instance;
        var previousTheme = manager.Theme;
        try
        {
            manager.Cards.Clear(); manager.Counts.Clear(); manager.Theme = PegasusShared.FormatType.FT_STANDARD;
            var vanilla = Card("VAN_CS2_093", TAG_PREMIUM.NORMAL, 2); vanilla.ManaCost = 4;
            var original = Card("CS2_093", TAG_PREMIUM.NORMAL, 2); original.ManaCost = 3;
            var core = Card("CORE_CS2_093", TAG_PREMIUM.NORMAL, 2); core.ManaCost = 3;
            var equality = Card("EX1_619", TAG_PREMIUM.NORMAL, 2); equality.ManaCost = 2;
            // Deliberately enumerate the historical version first, as GetAllCards
            // can do in the live client. All qualities share their raw edition ID.
            foreach (CollectibleCard card in new[] { vanilla, original, core, equality })
            { manager.Cards[(card.CardId, card.PremiumType)] = card; manager.Counts[(card.CardId, card.PremiumType)] = 2; }
            var legacy = new CategoryCatalog.Combo { Id = "legacy-versions", Name = "Legacy AoE", Category = saved.Category,
                Description = "", Cards = new[] {
                    new CategoryCatalog.CardRequirement("EX1_619", 2, Array.Empty<string>()),
                    new CategoryCatalog.CardRequirement("CS2_093", 1, Array.Empty<string>()) } };
            ComboCollectionCards.Prepare(new Dictionary<int, List<CollectibleCard>> { [1] = new List<CollectibleCard> { equality, core } })();
            Check(ComboCollectionCards.ComponentCards(legacy, "CS2_093").All(card => card.CardId == core.CardId)
                && ComboCollectionCards.ManaCost(legacy) == 7,
                "Legacy combos resolve the active three-mana Core Consecration instead of the first four-mana Vanilla catalog entry");
            ComboPopup.Inspect(legacy);
            Check(ButtonListMenu.Visible.GetComponentsInChildren<Actor>(false).Any(actor => actor.GetEntityDef().GetCardId() == core.CardId)
                && !ButtonListMenu.Visible.GetComponentsInChildren<Actor>(false).Any(actor => actor.GetEntityDef().GetCardId() == vanilla.CardId),
                "Combo inspect uses the same concrete edition as the mana display and collection artwork");
            ComboPopup.Close();
            manager.EditedDeck = new CollectionDeck();
            CollectionDeckTray.Instance = new CollectionDeckTray { Context = manager.EditedDeck };
            Check(ComboCollectionCards.AddToDeck(legacy) == 3 && manager.EditedDeck.LocalCount(core.CardId) == 1
                && manager.EditedDeck.LocalCount(vanilla.CardId) == 0 && manager.EditedDeck.LocalCount(original.CardId) == 0,
                "Bulk adding a legacy combo inserts the selected three-mana edition rather than a different normalized alias");
            var exact = new CategoryCatalog.Combo { Id = "exact-version", ExactVersions = true,
                Cards = new[] { new CategoryCatalog.CardRequirement(core.CardId, 1, Array.Empty<string>()) } };
            manager.Theme = PegasusShared.FormatType.FT_CLASSIC;
            ComboCollectionCards.Prepare(new Dictionary<int, List<CollectibleCard>> { [1] = new List<CollectibleCard> { vanilla } })();
            Check(ComboCollectionCards.ManaCost(exact) == 3 && ComboCollectionCards.ComponentCards(exact, core.CardId).Single() == core,
                "V2 combos retain the captured exact edition even when the currently browsed format changes");
            Check(ComboCollectionCards.ComponentCards(legacy, "CS2_093").Single() == vanilla,
                "V1 combos can still resolve the historical edition when it is the active Classic collection result");
            manager.EditedDeck = new CollectionDeck { Name = "Exact snapshot" };
            manager.EditedDeck.Add(core.CardId, TAG_PREMIUM.NORMAL); manager.EditedDeck.Add(vanilla.CardId, TAG_PREMIUM.NORMAL);
            CategoryDeckImport.Deck snapshot = EditedCategoryDeckImport.Capture("AoE");
            Check(snapshot.RawCounts[core.CardId] == 1 && snapshot.RawCounts[vanilla.CardId] == 1
                && snapshot.Counts["CS2_093"] == 2,
                "Live combo capture retains separate raw-edition quantities while ordinary category import still uses their canonical union");
        }
        finally
        {
            ComboPopup.Close(); manager.Cards.Clear(); manager.Counts.Clear();
            foreach (var pair in previousCards) manager.Cards.Add(pair.Key, pair.Value);
            foreach (var pair in previousCounts) manager.Counts.Add(pair.Key, pair.Value);
            manager.EditedDeck = previousDeck; manager.Theme = previousTheme; CollectionDeckTray.Instance = previousTray;
            ComboCollectionCards.Prepare(new Dictionary<int, List<CollectibleCard>> { [1] = manager.GetAllCards() })();
        }
    }
}
