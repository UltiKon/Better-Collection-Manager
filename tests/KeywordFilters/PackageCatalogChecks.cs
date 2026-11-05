using System;
using System.IO;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckPackageCatalog()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-packages-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        try
        {
            CheckPackageViewIsolation();
            ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
            CollectionQualitySettings.RememberFilterModes = false; FilterModes.StartSession();
            CategoryCatalog.LoadUserFile(path);
            Check(PackageCatalog.Packages.Count == 64
                && PackageCatalog.Packages.Count(p => p.PackageGroup == PackageKind.Parasitic) == 44
                && PackageCatalog.Packages.Count(p => p.PackageGroup == PackageKind.Factions) == 20,
                "The pinned catalog has 44 reviewed class-scoped parasitic and 20 independent faction folders");
            Check(!CategoryCatalog.Categories.Any(c => c.Value == CuratedCategory.Packages)
                && CategoryCatalog.Categories.Any(c => c.Value == CuratedCategory.Combos)
                && !CategoryCatalog.Entries.Any(e => e.Categories.Contains(CuratedCategory.Packages)),
                "Categories contains Combos and ordinary roles, with package components in their separate catalog");
            Check(PackageCatalog.Packages.All(p => !string.IsNullOrEmpty(p.PackageClass) && p.Cards.Count >= 2)
                && !PackageCatalog.Packages.Any(p => p.Name == "Sigils" || p.Name == "Spellstones" || p.Name == "Prime Legendaries" || p.Name == "Alliance" || p.Name == "Horde"),
                "Bundled folders require an explicit playable class and multiple members; keyword and flavour buckets are absent");
            Check(PackageCatalog.Packages.Single(p => p.Name == "Relics").Cards.All(c => c.Id != "TLC_334")
                && PackageCatalog.Packages.Where(p => p.Name.StartsWith("Galakrond")).Count() == 5
                && !PackageCatalog.Packages.Any(p => p.Name == "Galakrond"),
                "Name-only Relic false positives and mixed-class Galakrond folders cannot return");
            var wildseeds = PackageCatalog.Packages.Single(p => p.Name == "Wildseeds");
            var faction = PackageCatalog.Packages.First(p => p.PackageGroup == PackageKind.Factions);
            var deck = new CategoryDeckImport.Deck { Name = "Value" };
            deck.Cards.Add("REV_361"); deck.RawCounts["REV_361"] = 2;
            CategoryCatalog.AddCombo("Value", "Saved combo", "", deck);
            CategoryCatalog.AddCombo("Packages", "Saved package", "", deck, packageArtwork: "REV_361");
            CategoryCatalog.LoadUserFile(path);
            var combo = CategoryCatalog.Combos.Single(p => !p.IsPackage);
            var custom = PackageCatalog.Packages.Single(p => p.PackageGroup == PackageKind.Custom);
            Check(PackageCatalog.Packages.Count == 65 && custom.ExactVersions
                && custom.Cards.Single().Count == 2 && CategoryCatalog.CollectionEntries.Count() == 66,
                "Saved custom packages and combos survive beside the bundled entries without duplication");
            ExtraFilterState.ToggleCategory(CuratedCategory.Combos);
            Check(ExtraFilterState.MatchesEntry(combo) && !ExtraFilterState.MatchesEntry(custom)
                && !ExtraFilterState.Matches(Card("REV_361")),
                "Categories Combos selects saved combos across roles without standalone cards or package folders");
            ExtraFilterState.TogglePackage(PackageKind.Factions);
            Check(!ExtraFilterState.IsActiveForKind(FilterTrayKind.Categories)
                && ExtraFilterState.MatchesEntry(faction) && !ExtraFilterState.MatchesEntry(wildseeds)
                && !ExtraFilterState.MatchesEntry(custom) && !ExtraFilterState.MatchesEntry(combo),
                "Selecting Factions clears Categories and selects only the peer Factions folder group");
            var card = Card("REV_361"); FilterAvailability.Capture(new[] { card });
            foreach (FilterMode mode in new[] { FilterMode.Single, FilterMode.Or, FilterMode.Not, FilterMode.OrNot })
            {
                FilterModes.Set(FilterTrayKind.Packages, mode);
                Check(!ExtraFilterState.Matches(card)
                    && FilterAvailability.TryMatchCurrent(card, out bool match) && !match,
                    "Package filters keep ordinary cards out in scalar and indexed paths, including NOT: " + mode);
            }
            FilterModes.Set(FilterTrayKind.Packages, FilterMode.Single);
            ExtraFilterState.TogglePackage(PackageKind.Custom);
            Check(ExtraFilterState.MatchesEntry(custom) && !ExtraFilterState.MatchesEntry(faction),
                "Single changes package groups exclusively");
            ExtraFilterState.ToggleCategory(CuratedCategory.Value);
            Check(!ExtraFilterState.IsActiveForKind(FilterTrayKind.Packages) && ExtraFilterState.MatchesEntry(combo),
                "Returning to a card-role category clears package filtering and restores matching combos");
            int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            foreach (string reserved in new[] { "Uncategorized", "Packages" })
            {
                bool addRejected = false, editRejected = false;
                try { CategoryCatalog.AddCombo(reserved, "Invalid target", "", deck); }
                catch (InvalidOperationException) { addRejected = true; }
                try { CategoryCatalog.UpdateCombo(combo.Id, "Invalid target", "", categoryName: reserved); }
                catch (InvalidOperationException) { editRejected = true; }
                Check(addRejected && editRejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                    "Reserved combo destinations are rejected before writing any files: " + reserved);
            }
            CategoryCatalog.AddCombo("Combos", "Global combo", "", deck);
            var global = CategoryCatalog.Combos.Single(entry => entry.Name == "Global combo");
            ExtraFilterState.ClearAll(); ExtraFilterState.ToggleCategory(CuratedCategory.Combos);
            Check(global.Category == CuratedCategory.Combos && ExtraFilterState.MatchesEntry(global) && ExtraFilterState.MatchesEntry(combo),
                "Combos accepts new entries directly and includes every role combo");
            CategoryCatalog.DeleteCombo(global.Id);
            CategoryCatalog.UpdateCombo(faction.Id, "Edited faction", ""); CategoryCatalog.LoadUserFile(path);
            Check(PackageCatalog.Packages.Single(p => p.Id == faction.Id).PackageGroup == PackageKind.Factions
                && PackageCatalog.Packages.Single(p => p.Id == faction.Id).Name == "Edited faction",
                "Editing a bundled faction keeps its independent folder group on reload");
            CategoryCatalog.DeleteCombo(faction.Id); CategoryCatalog.LoadUserFile(path);
            Check(!PackageCatalog.Packages.Any(p => p.Id == faction.Id)
                && PackageCatalog.Packages.Any(p => p.Id == custom.Id),
                "Bundled package deletions persist without removing custom packages");
            const string legacy = "{\"version\":1,\"entries\":[{\"id\":\"package-wildseeds\","
                + "\"name\":\"My legacy seeds\",\"categories\":[\"packages\"],\"cards\":[{\"id\":\"REV_361\",\"count\":2}]}]}";
            File.WriteAllText(path, legacy); CategoryCatalog.LoadUserFile(path);
            Check(File.ReadAllText(path) == legacy
                && PackageCatalog.Packages.Single(p => p.Id == wildseeds.Id).Name == "Wildseeds"
                && PackageCatalog.Packages.Single(p => p.Id == wildseeds.Id).Cards.Count == 4
                && PackageCatalog.Packages.Any(p => p.Name == "My legacy seeds" && p.PackageGroup == PackageKind.Custom)
                && PackageCatalog.Packages.Any(p => p.Id == custom.Id)
                && !CategoryCatalog.Categories.Any(c => c.Value == CuratedCategory.Packages),
                "Legacy generated buckets cannot override the reviewed source definitions or alter saved custom packages");
            foreach (string destination in new[] { "Parasitic", "Factions", "Custom" })
            {
                CategoryCatalog.UpdateCombo(custom.Id, custom.Name, "", categoryName: destination);
                CategoryCatalog.LoadUserFile(path);
                var moved = PackageCatalog.Packages.Single(entry => entry.Id == custom.Id);
                Check(PackageCatalog.GroupName(moved.PackageGroup) == destination
                    && moved.Cards.Select(card => (card.Id, card.Count)).SequenceEqual(custom.Cards.Select(card => (card.Id, card.Count))),
                    "Custom packages can move to " + destination + " and retain their exact members after reload");
            }
            CategoryCatalog.CreateFolder("Empty role folder", false);
            CategoryCatalog.CreateFolder("Empty package folder", true);
            var emptyRole = CategoryCatalog.Categories.Single(folder => folder.Name == "Empty role folder");
            var emptyPackage = PackageCatalog.Groups.Single(folder => folder.Name == "Empty package folder");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Categories.Any(folder => folder.Name == emptyRole.Name && folder.Value == emptyRole.Value)
                && PackageCatalog.Groups.Any(folder => folder.Name == emptyPackage.Name && folder.Kind == emptyPackage.Kind),
                "Explicit empty folders survive reload with stable selection identities");
            files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            Check(CategoryCatalog.CreateFolder(" empty PACKAGE folder ", true) == "Empty package folder"
                && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                "Adding an existing package folder selects it without a duplicate file or group");
            CategoryCatalog.UpdateCombo(custom.Id, custom.Name, "", categoryName: emptyPackage.Name);
            CategoryCatalog.UpdateCombo(combo.Id, combo.Name, "", categoryName: emptyRole.Name);
            CategoryCatalog.LoadUserFile(path);
            var grouped = PackageCatalog.Packages.Single(entry => entry.Id == custom.Id);
            ExtraFilterState.ClearAll(); ExtraFilterState.TogglePackage(emptyPackage.Kind);
            Check(grouped.PackageGroup == emptyPackage.Kind && ExtraFilterState.MatchesEntry(grouped)
                && !ExtraFilterState.MatchesEntry(PackageCatalog.Packages.Single(entry => entry.Id == wildseeds.Id)),
                "New package folders participate in real package filtering independently of built-in groups");
            var roleCombo = CategoryCatalog.Combos.Single(entry => entry.Id == combo.Id);
            ExtraFilterState.ToggleCategory(emptyRole.Value);
            Check(roleCombo.Category == emptyRole.Value && ExtraFilterState.MatchesEntry(roleCombo),
                "New empty category folders can receive saved combos and filter them after reload");

            // Version 6 used its category header for source metadata and did
            // not store a package destination. Preserve that legacy meaning.
            string sourceFile = Path.Combine(CategoryCatalog.DeckDirectory(path), custom.Id + ".combo");
            var legacyLines = File.ReadAllLines(sourceFile).ToList();
            Check(legacyLines[0] == "BCM-CATEGORY-COMBO 7", "New packages explicitly serialize their destination in version 7");
            legacyLines[0] = "BCM-CATEGORY-COMBO 6";
            legacyLines[1] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Old arbitrary deck name"));
            legacyLines[2] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Legacy version 6"));
            legacyLines.RemoveAt(7);
            File.WriteAllLines(Path.Combine(CategoryCatalog.DeckDirectory(path), "legacy-v6.combo"), legacyLines);
            File.WriteAllText(Path.Combine(CategoryCatalog.DeckDirectory(path), "edit-999999999999999999.editcombo"),
                wildseeds.Id + "\n" + string.Join("\n", legacyLines));
            CategoryCatalog.LoadUserFile(path);
            Check(PackageCatalog.Packages.Single(entry => entry.Id == "legacy-v6").PackageGroup == PackageKind.Custom
                && PackageCatalog.Packages.Single(entry => entry.Id == wildseeds.Id).PackageGroup == PackageKind.Parasitic,
                "Old version 6 packages stay Custom and old built-in edits retain their previous group");
        }
        finally
        {
            ExtraFilterState.ClearAll(); FilterModes.StartSession(); FilterAvailability.Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            CategoryCatalog.LoadUserFile(path); Directory.Delete(directory, true);
        }
    }

    private static void CheckPackageViewIsolation()
    {
        PackageViewState.Leave(); ExpansionFilterState.Clear();
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Or);
        var standard = new SetFilterItem { CardSets = new System.Collections.Generic.List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
        var wild = new SetFilterItem { CardSets = new System.Collections.Generic.List<TAG_CARD_SET> { TAG_CARD_SET.JAIL } };
        ExpansionFilterState.Toggle(standard); ExpansionFilterState.Toggle(wild);
        var cards = ExpansionFilterState.Capture(); string modes = FilterModes.Serialize();
        PackageViewState.Enter(PegasusShared.FormatType.FT_STANDARD);
        Check(PackageViewState.IsActive && !PackageViewState.Group.HasValue && PackageViewState.Format == PegasusShared.FormatType.FT_STANDARD
            && !ExpansionFilterState.IsActive && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Single,
            "Entering Packages starts an independent all-groups query with the supplied format and no expansion restriction");
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Not); ExpansionFilterState.Toggle(standard);
        PackageViewState.SelectGroup(PackageKind.Factions); PackageViewState.SetFormat(PegasusShared.FormatType.FT_WILD);
        Check(ExpansionFilterState.SelectedCount == 1 && FilterModes.Serialize() == modes,
            "Package expansion terms, logic, groups and format never overwrite remembered card modes");
        PackageViewState.Leave();
        Check(FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Or && ExpansionFilterState.IsActive
            && ExpansionFilterState.Capture().Select(term => (term.Value, term.Operator)).SequenceEqual(cards.Select(term => (term.Value, term.Operator))),
            "Leaving Packages restores the exact card expansion selections and their operators");
        PackageViewState.Enter(PegasusShared.FormatType.FT_WILD);
        Check(PackageViewState.Format == PegasusShared.FormatType.FT_WILD && !PackageViewState.Group.HasValue
            && !ExpansionFilterState.IsActive && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Single,
            "Re-entering Packages seeds Wild and clears only the previous package query");
        PackageViewState.Leave(); ExpansionFilterState.Clear(); FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Single);
    }
}
