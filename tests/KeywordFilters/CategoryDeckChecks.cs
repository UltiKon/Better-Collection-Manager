using System;
using System.IO;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckCategoryDeckImports()
    {
        // Fixed wire-format fixtures, including the HearthSim specification's n-copy example.
        const string first = "AAEBAQcCAQIAAAA=";
        const string second = "AAEBAQcBAgEDAAA=";
        const string custom = "AAEBAQcCAwQAAA==";
        const string customMore = "AAEBAQcCBAUAAAA=";
        string Export(string name, string code) => "### " + name + "\r\n# Class: Mage\r\n# 2x ignored localized text\r\n" + code + "\r\n# Copy this deck\r\n";
        bool Invalid(string text, Func<int, string> resolver = null)
        {
            try { CategoryDeckImport.Parse(text, resolver ?? (id => "dbf-" + id)); return false; }
            catch (FormatException) { return true; }
        }
        var decoded = CategoryDeckImport.Parse(Export("AoE", "AAEBAQcAAAQBAwIDAwMEAw=="), id => "dbf-" + id);
        Check(decoded.Cards.SetEquals(new[] { "dbf-1", "dbf-2", "dbf-3", "dbf-4" }),
            "The reference deckstring's n-copy section imports membership without a 30-card requirement");
        decoded = CategoryDeckImport.Parse(Export("  My   Counters  ", "AAEBAWQBAQECAQMEAQEEAwEFAwEGBQM="), id => "dbf-" + id);
        Check(decoded.Name == "My Counters" && decoded.Cards.SetEquals(Enumerable.Range(1, 6).Select(i => "dbf-" + i)),
            "Singletons, duplicates, n-copy cards and all three sideboard groups merge without including the initial hero");
        decoded = CategoryDeckImport.Parse(Export("AoE", first), id => id == 1 ? "CORE_CS2_032" : "VAN_CS2_032");
        Check(decoded.Cards.SetEquals(new[] { "CS2_032" }), "Core/Classic aliases collapse before importing category membership");
        foreach (string invalid in new[] { first, "### AoE", Export("", first), Export("x", "garbage"),
            Export("x", "AAEBAQcAAAAA"), Export("x", "AAIBAQcCAQIAAAA="),
            Export("x", Convert.ToBase64String(new byte[] { 0, 1, 1, 1, 7, 128 })),
            Export("x", Convert.ToBase64String(new byte[] { 0, 1, 1, 1, 7, 255, 255, 255, 255, 15 })),
            Export("x", Convert.ToBase64String(new byte[] { 0, 1, 1, 1, 7, 233, 7 })),
            Export("x", first) + Export("x", second), Export(new string('x', 81), first),
            new string('x', CategoryDeckImport.MaxTextLength + 1),
            Export("x", Convert.ToBase64String(Convert.FromBase64String(first).Concat(new byte[] { 1 }).ToArray())) })
            Check(Invalid(invalid), "Malformed, unnamed, empty, oversized, truncated or multiple deck exports are rejected atomically");
        Check(Invalid(Export("x", first), id => id == 2 ? null : "dbf-" + id),
            "A single unresolved DBF card rejects the whole import instead of silently dropping it");

        string directory = Path.Combine(Path.GetTempPath(), "bcm-category-decks-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        try
        {
            CategoryCatalog.LoadUserFile(path);
            string original = File.ReadAllText(path);
            string imports = CategoryCatalog.DeckDirectory(path);
            int generation = CategoryCatalog.Generation;
            Check(CategoryCatalog.ImportDeckText(Export("AoE", first)).Contains("+2")
                && CategoryCatalog.Generation > generation && CategoryCatalog.Categories.Length == 10
                && CategoryCatalog.Matches(Card("dbf-1"), CuratedCategory.AoE)
                && CategoryCatalog.Matches(Card("CS2_032"), CuratedCategory.AoE),
                "A named partial deck adds cards to the built-in role and retains every existing curated member");
            Check(CategoryCatalog.ImportDeckText(Export(" aoe ", second)).Contains("+1")
                && CategoryCatalog.Matches(Card("dbf-3"), CuratedCategory.AoE)
                && Directory.GetFiles(imports, "*.txt").Length == 2,
                "Same-name imports merge case-insensitively and ignore card quantities");
            Check(CategoryCatalog.ImportDeckText(Export("AOE", first)).Contains("no new")
                && Directory.GetFiles(imports, "*.txt").Length == 2,
                "Reimporting cards is idempotent and creates no redundant export file");
            CategoryCatalog.ImportDeckText(Export("My Counters", custom));
            var category = CategoryCatalog.Categories.Single(c => c.Name == "My Counters");
            Check(CategoryCatalog.Categories.Length == 11 && CategoryCatalog.Matches(Card("dbf-4"), category.Value)
                && !CategoryCatalog.Matches(Card("dbf-5"), category.Value),
                "An unknown deck name creates a selectable custom category instead of mislabeling an existing role");
            var selectedCards = new[] { Card("dbf-3"), Card("dbf-4"), Card("dbf-5") };
            ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
            CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Or);
            ExtraFilterState.SetCategory(category.Value, true);
            FilterAvailability.Capture(selectedCards);
            Check(FilterAvailability.TryMatchCurrent(selectedCards[2], out bool before) && !before,
                "The bitset initially excludes a card absent from the custom category");
            CategoryCatalog.ImportDeckText(Export("my   COUNTERS", customMore));
            Check(CategoryCatalog.Categories.Length == 11
                && FilterAvailability.TryMatchCurrent(selectedCards[2], out bool after) && after
                && ExtraFilterState.Matches(selectedCards[2]),
                "Merging invalidates indexed results immediately while keeping the selected custom category identity");
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.And);
            ExtraFilterState.SetCategory(CuratedCategory.AoE, true);
            foreach (var card in selectedCards)
                Check(ExtraFilterState.Matches(card) == (card.CardId == "dbf-3")
                    && FilterAvailability.TryMatchCurrent(card, out bool intersection) && intersection == (card.CardId == "dbf-3"),
                    "Custom and built-in categories intersect correctly in scalar and indexed filtering");
            ExtraFilterState.ClearAll();
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Categories.Single(c => c.Id == category.Id).Value == category.Value
                && CategoryCatalog.Matches(Card("dbf-3"), category.Value)
                && CategoryCatalog.Matches(Card("dbf-5"), category.Value)
                && CategoryCatalog.Entries.Select(e => e.Id).Distinct().Count() == CategoryCatalog.Entries.Count,
                "Reloading preserves category identities and the union of multiple deck exports without duplicate entries");
            File.WriteAllText(Path.Combine(imports, "broken.txt"), "### broken\ninvalid");
            Check(CategoryCatalog.LoadUserFile(path) && CategoryCatalog.Matches(Card("dbf-5"), category.Value),
                "A malformed exported deck cannot disable valid imports or the filter menu");
            int files = Directory.GetFiles(imports).Length;
            try { CategoryCatalog.ImportDeckText("### AoE\ninvalid"); }
            catch (FormatException) { }
            Check(Directory.GetFiles(imports).Length == files && File.ReadAllText(path) == original,
                "Imports preserve the editable source catalog and invalid input makes no persistent change");
            string live = CategoryDeckImport.SerializeCards("My Counters", new[] { "CORE_EX1_066", "EX1_066", "CS2_023" });
            Check(CategoryDeckImport.ParseCards(live).Cards.SetEquals(new[] { "EX1_066", "CS2_023" }),
                "Live snapshots round-trip canonical card IDs without a hero, deck code or complete deck");
            Check(CategoryCatalog.ImportDeckCards("My Counters", new[] { "CORE_EX1_066", "EX1_066", "CS2_023" }).Contains("+2")
                && CategoryCatalog.Matches(Card("EX1_066"), category.Value)
                && CategoryCatalog.Matches(Card("dbf-3"), category.Value)
                && Directory.GetFiles(imports, "*.cards").Length == 1,
                "Live snapshots merge with existing deck-text imports by category name and preserve both membership sources");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Matches(Card("CORE_EX1_066"), category.Value)
                && CategoryCatalog.Matches(Card("dbf-5"), category.Value),
                "Mixed live snapshots and historical export files survive a collection reload");
            files = Directory.GetFiles(imports).Length;
            foreach (var bad in new[] { new[] { "bad\ncard" }, Array.Empty<string>(), new[] { "" } })
            {
                try { CategoryCatalog.ImportDeckCards("My Counters", bad); Check(false, "Invalid snapshots must fail"); }
                catch (FormatException) { Check(Directory.GetFiles(imports).Length == files, "Invalid or empty snapshots make no persistent change"); }
            }
            foreach (string file in Directory.GetFiles(imports)) File.Delete(file);
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Categories.Length == 10 && !CategoryCatalog.Matches(Card("dbf-1"), CuratedCategory.AoE),
                "Removing exported files removes their additions on reload while retaining the built-in catalog");
        }
        finally
        {
            ExtraFilterState.ClearAll(); FilterAvailability.Clear();
            CategoryCatalog.LoadUserFile(path);
            Directory.Delete(directory, true);
        }
    }
}
