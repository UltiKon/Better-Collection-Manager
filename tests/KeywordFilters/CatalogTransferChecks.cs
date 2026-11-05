using System;
using System.IO;
using System.Linq;
using System.Text;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckCatalogTransfers()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-share-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(directory, "source", "categories.json"), target = Path.Combine(directory, "target", "categories.json");
        CategoryCatalog.Combo Entry(string name) => CategoryCatalog.CollectionEntries.Single(c => c.Name == name);
        CategoryDeckImport.Deck Deck(params string[] cards)
        {
            var deck = new CategoryDeckImport.Deck();
            foreach (string card in cards) { deck.Cards.Add(card); deck.RawCounts[card] = 2; }
            return deck;
        }
        try
        {
            CategoryCatalog.LoadUserFile(source);
            CategoryCatalog.CreateFolder("Empty ü", false);
            CategoryCatalog.ImportDeckCards("Shared role", new[] { "CORE_CS2_032", "CS2_093" });
            CategoryCatalog.AddCombo("Shared role", "Shared combo", "Description\nwith ü", Deck("CORE_CS2_032", "CS2_093"),
                new CategoryCatalog.ComboAppearance { Frame = "Minion", ShowAttack = true, Attack = 7, ManaCost = 13, CardText = "<b>Text</b>" });
            CategoryCatalog.AddCombo("Combos", "Global shared combo", "", Deck("GLOBAL"));
            CategoryCatalog.CreateFolder("Shared packages", true);
            CategoryCatalog.AddCombo("Shared packages", "Shared package", "Package description", Deck("CORE_CS2_032", "CS2_093"), packageArtwork: "CS2_032");
            string categories = CategoryCatalog.ExportTransfer(false), packages = CategoryCatalog.ExportTransfer(true, "Shared packages");
            string single = CategoryCatalog.ExportTransfer(true, entryId: Entry("Shared package").Id);
            string role = CategoryCatalog.ExportTransfer(false, "Shared role");
            string builtin = CategoryCatalog.ExportTransfer(true, entryId: CategoryCatalog.CollectionEntries.First(c => c.IsPackage && c.Id.StartsWith("builtin:")).Id);
            string builtinName = CategoryCatalog.CollectionEntries.First(c => c.IsPackage && c.Id.StartsWith("builtin:")).Name;
            string builtinClass = Entry(builtinName).PackageClass;
            CategoryCatalog.LoadUserFile(target);
            CategoryCatalog.ImportDeckCards("Local only", new[] { "LOCAL" });
            CategoryCatalog.ImportDeckCards("Shared role", new[] { "LOCAL_ROLE" });
            CategoryCatalog.AddCombo("Shared role", "Local combo", "", Deck("LOCAL"));
            CategoryCatalog.AddCombo("Combos", "Global local combo", "", Deck("LOCAL_GLOBAL"));
            Check(CategoryCatalog.TransferNeedsChoice(categories, false), "Existing extra category members require a conflict decision");
            Check(!CategoryCatalog.TransferNeedsChoice(single, true), "A new single package requires no decision");
            CategoryCatalog.ImportTransfer(categories, false);
            var shared = CategoryCatalog.Categories.Single(c => c.Name == "Shared role");
            Check(CategoryCatalog.Matches(Card("LOCAL_ROLE"), shared.Value) && CategoryCatalog.Matches(Card("CS2_032"), shared.Value)
                && CategoryCatalog.Categories.Any(c => c.Name == "Empty ü") && Entry("Shared combo").Cards.Count == 2,
                "Clipboard category merge preserves local cards and empty folders and imports combos");
            var combo = Entry("Shared combo");
            Check(combo.ExactVersions && combo.Cards.Single(c => c.Id == "CORE_CS2_032").Count == 2 && combo.Description == "Description\nwith ü"
                && combo.Appearance.Attack == 7 && combo.Appearance.ManaCost == 13 && combo.Appearance.CardText == "<b>Text</b>",
                "Sharing preserves custom mana, editions, quantities, Unicode, multiline descriptions and card styling");
            int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(target)).Length;
            CategoryCatalog.ImportTransfer(categories, false);
            Check(Directory.GetFiles(CategoryCatalog.DeckDirectory(target)).Length == files, "Repeated category merge is idempotent");
            CategoryCatalog.ImportTransfer(role, false, overwrite: true);
            Check(!CategoryCatalog.Matches(Card("LOCAL_ROLE"), shared.Value) && CategoryCatalog.Matches(Card("CS2_093"), shared.Value)
                && !CategoryCatalog.CollectionEntries.Any(c => c.Name == "Local combo") && CategoryCatalog.Categories.Any(c => c.Name == "Local only"),
                "Overwriting a folder replaces membership and combos only within that folder");
            CategoryCatalog.ImportTransfer(categories, false, overwrite: true);
            Check(!CategoryCatalog.CollectionEntries.Any(c => c.Name == "Global local combo") && Entry("Global shared combo") != null,
                "Overwriting all exported categories also replaces the explicit global Combos folder");
            Check(!CategoryCatalog.TransferNeedsChoice(packages, true), "New package folders can merge automatically");
            CategoryCatalog.ImportTransfer(packages, true);
            Check(CategoryCatalog.FindTransferredEntry(single, true)?.Id == Entry("Shared package").Id,
                "A single-entry transfer resolves to its locally saved ID for navigation");
            Check(CategoryCatalog.FindTransferredEntry(role, false) == null,
                "Category membership snapshots are not treated as individual entry imports");
            Check(!CategoryCatalog.TransferNeedsChoice(single, true), "An identical single package requires no decision");
            CategoryCatalog.AddCombo("Shared packages", "Sibling package", "", Deck("SIBLING"), packageArtwork: "SIBLING");
            CategoryCatalog.UpdateCombo(Entry("Shared package").Id, "Shared package", "Local style", cards: Deck("CS2_032", "EXTRA"));
            Check(CategoryCatalog.TransferNeedsChoice(single, true), "Extra local cards and different metadata in a single package are a real conflict");
            CategoryCatalog.ImportTransfer(single, true);
            var package = Entry("Shared package");
            Check(CategoryCatalog.FindTransferredEntry(single, true)?.Id == package.Id,
                "Navigation follows a merged entry's new local ID rather than its old export identity");
            Check(package.Cards.Select(c => c.Id).OrderBy(c => c).SequenceEqual(new[] { "CORE_CS2_032", "CS2_032", "CS2_093", "EXTRA" }.OrderBy(c => c))
                && package.Description == "Local style", "Merging a named package unites cards while retaining local metadata");
            files = Directory.GetFiles(CategoryCatalog.DeckDirectory(target)).Length;
            CategoryCatalog.ImportTransfer(single, true);
            Check(Directory.GetFiles(CategoryCatalog.DeckDirectory(target)).Length == files, "Repeated package merge is idempotent after union");
            Check(CategoryCatalog.TransferNeedsChoice(packages, true), "An existing folder's unexported sibling requires a conflict decision");
            CategoryCatalog.ImportTransfer(single, true, overwrite: true);
            Check(Entry("Shared package").Cards.Count == 2 && Entry("Shared package").Description == "Package description"
                && Entry("Sibling package") != null, "Overwriting one exported package preserves its siblings and restores its metadata");
            CategoryCatalog.ImportTransfer(packages, true, overwrite: true);
            Check(!CategoryCatalog.CollectionEntries.Any(c => c.Name == "Sibling package"), "Overwriting a package folder removes entries absent from its snapshot");
            CategoryCatalog.ImportTransfer(builtin, true, overwrite: true);
            Check(Entry(builtinName).PackageClass == builtinClass, "Built-in package class restrictions survive transfer and replacement");
            string expectedCategories = CategoryCatalog.ExportTransfer(false), expectedPackages = CategoryCatalog.ExportTransfer(true);
            CategoryCatalog.LoadUserFile(target);
            Check(CategoryCatalog.ExportTransfer(false) == expectedCategories && CategoryCatalog.ExportTransfer(true) == expectedPackages,
                "All transfer operations persist identically across collection reloads");
            files = Directory.GetFiles(CategoryCatalog.DeckDirectory(target)).Length;
            int generation = CategoryCatalog.Generation;
            foreach (string invalid in new[] { "", "a deck code", categories.Replace("BCM-COLLECTION 1", "BCM-COLLECTION 99"),
                categories + ".combo\tbroken\n", categories + ".uncombo\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes("builtin:package-wildseeds")),
                new string('x', CategoryCatalog.MaxTransferLength + 1) })
            {
                bool rejected = false;
                try { CategoryCatalog.ImportTransfer(invalid, false, overwrite: true); } catch (FormatException) { rejected = true; }
                Check(rejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(target)).Length == files && CategoryCatalog.Generation == generation,
                    "Invalid exports, unsupported versions and injected removal commands never commit partial overwrites");
            }
            bool wrongKind = false;
            try { CategoryCatalog.ImportTransfer(packages, false); } catch (FormatException) { wrongKind = true; }
            Check(wrongKind, "The category menu rejects package imports with a destination hint");
            File.WriteAllText(Path.Combine(CategoryCatalog.DeckDirectory(target), "edit-00000000000000999999.bundle"), role + ".combo\tbroken\n");
            CategoryCatalog.LoadUserFile(target);
            Check(CategoryCatalog.ExportTransfer(false) == expectedCategories, "A corrupt saved bundle is ignored as a complete unit");
            string blocked = Path.Combine(CategoryCatalog.DeckDirectory(target), "edit-00000000000001000000.bundle.tmp");
            Directory.CreateDirectory(blocked);
            generation = CategoryCatalog.Generation;
            bool writeFailed = false;
            try { CategoryCatalog.ImportTransfer(role, false, overwrite: true); }
            catch (IOException) { writeFailed = true; }
            catch (UnauthorizedAccessException) { writeFailed = true; }
            Check(writeFailed && CategoryCatalog.Generation == generation && CategoryCatalog.ExportTransfer(false) == expectedCategories,
                "A failed atomic bundle write leaves every overwritten folder and entry unchanged");
            Directory.Delete(blocked);
            CategoryCatalog.ImportDeckCards("Large folder", Enumerable.Range(0, 1000).Select(i => "LARGE_" + i));
            CategoryCatalog.ImportDeckCards("Large folder", Enumerable.Range(1000, 100).Select(i => "LARGE_" + i));
            string large = CategoryCatalog.ExportTransfer(false, "Large folder");
            CategoryCatalog.LoadUserFile(source);
            CategoryCatalog.ImportTransfer(large, false, overwrite: true);
            var largeCategory = CategoryCatalog.Categories.Single(c => c.Name == "Large folder");
            Check(Enumerable.Range(0, 1100).All(i => CategoryCatalog.Matches(Card("LARGE_" + i), largeCategory.Value)),
                "Folders above one snapshot's card limit round-trip in validated chunks without losing members");

        }
        finally
        {
            ExtraFilterState.ClearAll(); FilterAvailability.Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
