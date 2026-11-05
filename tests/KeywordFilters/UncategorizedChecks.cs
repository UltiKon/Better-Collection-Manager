using System;
using System.IO;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckUncategorized()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-uncategorized-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"version\":1,\"entries\":[{\"id\":\"known\",\"name\":\"Known\",\"categories\":[\"value\"],\"cards\":[{\"id\":\"EX1_619\",\"count\":1}]}]}");
            CategoryCatalog.LoadUserFile(path); ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
            var known = Card("CORE_EX1_619"); var free = Card("CS2_093"); var another = Card("Unassigned");
            Check(!CategoryCatalog.Matches(known, CuratedCategory.Uncategorized) && CategoryCatalog.Matches(free, CuratedCategory.Uncategorized)
                && CategoryCatalog.Categories.Last().Value == CuratedCategory.Uncategorized,
                "Uncategorized is a computed final menu category and respects normalized Core/Classic category membership");
            int generation = CategoryCatalog.Generation;
            CategoryCatalog.ImportDeckCards("Custom role", new[] { free.CardId });
            Check(!CategoryCatalog.Matches(free, CuratedCategory.Uncategorized) && CategoryCatalog.Generation > generation,
                "Importing a card into any custom category immediately removes it from Uncategorized");
            var custom = CategoryCatalog.Categories.Single(category => category.Name == "Custom role");
            CategoryCatalog.RemoveDeckCards(custom.Value, new[] { free.CardId });
            Check(CategoryCatalog.Matches(free, CuratedCategory.Uncategorized), "Removing the last category assignment returns a card to Uncategorized");
            var deck = new CategoryDeckImport.Deck(); deck.Cards.Add(another.CardId);
            CategoryCatalog.AddCombo("Value", "Combo only", "", deck);
            Check(CategoryCatalog.Matches(another, CuratedCategory.Uncategorized),
                "A combo's captured components do not acquire standalone category assignments");
            CollectionQualitySettings.HideEmptyFilters = true;
            FilterAvailability.Capture(new[] { known });
            Check(FilterAvailability.Extra(FilterTrayKind.Categories, (int)CuratedCategory.Uncategorized, false),
                "Uncategorized stays visible with hide-empty enabled when every native result has a category");
            FilterAvailability.Capture(Array.Empty<CollectibleCard>());
            Check(FilterAvailability.Extra(FilterTrayKind.Categories, (int)CuratedCategory.Uncategorized, false),
                "Uncategorized stays visible even when the native search has no results");
            Check(!FilterAvailability.Extra(FilterTrayKind.Categories, (int)CuratedCategory.Draw, false),
                "Ordinary empty categories still obey hide-empty");
            FilterAvailability.Capture(new[] { known });
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Not);
            Check(!FilterAvailability.Extra(FilterTrayKind.Categories, (int)CuratedCategory.Value, false),
                "A combo inside Value cannot make NOT Value available when no card or combo lies outside that category");
            FilterAvailability.Capture(new[] { free });
            Check(FilterAvailability.Extra(FilterTrayKind.Categories, (int)CuratedCategory.Uncategorized, false),
                "Uncategorized stays visible in NOT mode even when its complement is empty");
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Single);
            FilterAvailability.Capture(new[] { known, free });
            Check(FilterAvailability.Extra(FilterTrayKind.Categories, (int)CuratedCategory.Uncategorized, false),
                "Uncategorized availability uses the captured native search scope");
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Not); ExtraFilterState.SetCategory(CuratedCategory.Uncategorized, true);
            Check(ExtraFilterState.Matches(known) && !ExtraFilterState.Matches(free), "NOT Uncategorized selects categorized cards");
            CategoryCatalog.LoadUserFile(path);
            Check(ExtraFilterState.HasCategory(CuratedCategory.Uncategorized) && CategoryCatalog.Matches(free, CuratedCategory.Uncategorized),
                "The computed category and its selection survive catalog reloads");
            int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            bool rejected = false;
            try { CategoryCatalog.ImportDeckCards("Uncategorized", new[] { free.CardId }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                "The computed category cannot receive a persistent assignment that would contradict its definition");
        }
        finally
        {
            ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
            CollectionQualitySettings.HideEmptyFilters = false; FilterAvailability.Clear(); FilterModes.StartSession();
            foreach (string file in Directory.GetFiles(CategoryCatalog.DeckDirectory(path))) File.Delete(file);
            CategoryCatalog.LoadUserFile(path); Directory.Delete(directory, true);
        }
    }
}
