using System;
using System.IO;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckCategoryRemoval()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-category-remove-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        const string baseline = "{\"version\":1,\"entries\":[{\"id\":\"shared\",\"name\":\"Shared cards\","
            + "\"categories\":[\"aoe\",\"value\"],\"cards\":[{\"id\":\"CS2_032\",\"count\":1},{\"id\":\"CS2_023\",\"count\":2}]}]}";
        try
        {
            Directory.CreateDirectory(directory); File.WriteAllText(path, baseline);
            CategoryCatalog.LoadUserFile(path);
            string imports = CategoryCatalog.DeckDirectory(path);
            string legacy = Path.Combine(imports, "zzz-legacy.cards");
            string legacyText = CategoryDeckImport.SerializeCards("AoE", new[] { "CS2_093" });
            File.WriteAllText(legacy, legacyText);
            CategoryCatalog.LoadUserFile(path);
            CategoryCatalog.ImportDeckCards("My Counters", new[] { "CS2_032", "CS2_093" });
            var custom = CategoryCatalog.Categories.Single(c => c.Name == "My Counters");
            ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Or);
            ExtraFilterState.SetCategory(CuratedCategory.AoE, true);
            var cards = new[] { Card("CS2_032"), Card("CS2_093"), Card("CS2_023") };
            FilterAvailability.Capture(cards);
            Check(cards.All(card => FilterAvailability.TryMatchCurrent(card, out bool match) && match),
                "Both base catalog cards and historical imports participate in the active category before removal");
            string result = CategoryCatalog.RemoveDeckCards(CuratedCategory.AoE,
                new[] { "CORE_CS2_032", "CS2_032", "CS2_093", "not-in-category" });
            Check(result.Contains("-2") && cards.All(card => CategoryCatalog.Matches(card, CuratedCategory.AoE) == (card.CardId == "CS2_023")),
                "Removal intersects the current category and normalizes duplicate aliases when reporting the exact removed count");
            Check(cards.All(card => FilterAvailability.TryMatchCurrent(card, out bool match) && match == (card.CardId == "CS2_023")),
                "Removing membership invalidates the active category bitset immediately");
            Check(CategoryCatalog.Matches(cards[0], CuratedCategory.Value)
                && CategoryCatalog.Matches(cards[0], custom.Value) && CategoryCatalog.Matches(cards[1], custom.Value)
                && File.ReadAllText(path) == baseline && File.ReadAllText(legacy) == legacyText,
                "Removing from one category preserves overlapping categories, grouped definitions and every source file");
            CategoryCatalog.LoadUserFile(path);
            Check(!CategoryCatalog.Matches(cards[0], CuratedCategory.AoE) && !CategoryCatalog.Matches(cards[1], CuratedCategory.AoE),
                "Removals survive reload and take precedence over historical exports regardless of filename order");
            int files = Directory.GetFiles(imports).Length;
            Check(CategoryCatalog.RemoveDeckCards(CuratedCategory.AoE, new[] { "CS2_032", "unrelated" }).Contains("no matching")
                && Directory.GetFiles(imports).Length == files, "A repeated or disjoint removal is idempotent");
            Check(CategoryCatalog.ImportDeckCards("AoE", new[] { "VAN_CS2_032" }).Contains("+1")
                && CategoryCatalog.Matches(cards[0], CuratedCategory.AoE) && !CategoryCatalog.Matches(cards[1], CuratedCategory.AoE),
                "A later import explicitly restores its cards without undoing unrelated removals");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Matches(cards[0], CuratedCategory.AoE) && !CategoryCatalog.Matches(cards[1], CuratedCategory.AoE),
                "Add-after-remove order is preserved across reloads without relying on timestamps");
            CategoryCatalog.RemoveDeckCards(custom.Value, new[] { "CS2_032", "CS2_093" });
            CategoryCatalog.LoadUserFile(path);
            Check(!CategoryCatalog.Categories.Any(c => c.Value == custom.Value)
                && !CategoryCatalog.Matches(cards[0], custom.Value) && !CategoryCatalog.Matches(cards[1], custom.Value),
                "Removing every custom category member deletes the empty category");
            CategoryCatalog.ImportDeckCards("my counters", new[] { "CS2_093" });
            Check(CategoryCatalog.Matches(cards[1], custom.Value) && !CategoryCatalog.Matches(cards[0], custom.Value),
                "Refilling an emptied category merges by normalized name and keeps all other deletions");
            File.WriteAllText(Path.Combine(imports, "edit-00000000000000001000.remove"), "broken");
            CategoryCatalog.LoadUserFile(path);
            Check(!CategoryCatalog.Matches(cards[1], CuratedCategory.AoE) && CategoryCatalog.Matches(cards[1], custom.Value),
                "A corrupt removal file cannot disable valid edits or other categories");
            string blocked = Path.Combine(imports, "edit-00000000000000001001.remove.tmp");
            Directory.CreateDirectory(blocked);
            files = Directory.GetFiles(imports).Length;
            int generation = CategoryCatalog.Generation;
            bool failed = false;
            try { CategoryCatalog.RemoveDeckCards(CuratedCategory.AoE, new[] { "CS2_032" }); }
            catch (IOException) { failed = true; }
            catch (UnauthorizedAccessException) { failed = true; }
            Check(failed && CategoryCatalog.Generation == generation && CategoryCatalog.Matches(cards[0], CuratedCategory.AoE)
                && Directory.GetFiles(imports).Length == files,
                "A failed persistent write leaves membership and saved edit history unchanged");
        }
        finally
        {
            ExtraFilterState.ClearAll(); FilterAvailability.Clear();
            foreach (string file in Directory.GetFiles(CategoryCatalog.DeckDirectory(path))) File.Delete(file);
            CategoryCatalog.LoadUserFile(path);
            Directory.Delete(directory, true);
        }
    }
}
