using System;
using System.IO;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckCategories()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bcm-categories-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "categories.json");
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        CollectionQualitySettings.RememberFilterModes = false;
        FilterModes.StartSession();
        try
        {
            Check(!typeof(CategoryCatalog).Assembly.GetReferencedAssemblies().Any(assembly =>
                assembly.Name.StartsWith("System.Runtime.Serialization", StringComparison.Ordinal)),
                "The catalog must load without the serialization assembly missing from Hearthstone's Mono profile");
            Check(CategoryCatalog.LoadUserFile(path) && File.Exists(path), "First use creates the editable starter catalog");
            string bundled = File.ReadAllText(path);
            CheckCompleteCategoryCoverage();
            Check(CategoryCatalog.Categories.Select(c => c.Name).SequenceEqual(new[] {
                "AoE", "Single-Target Removal", "SMOrc", "Value", "Win Conditions", "Draw", "Unique Effects", "Combos", "TECHW", "Uncategorized"
            }), "The curated categories retain their requested order with Uncategorized last");
            Check(CategoryCatalog.Categories.Where(category => category.Value != CuratedCategory.Uncategorized && category.Value != CuratedCategory.Combos).All(category => CategoryCatalog.Entries.Any(entry => entry.Categories.Contains(category.Value))),
                "Every starter category contains curated cards");
            Check(PackageCatalog.Packages.Single(entry => entry.Name == "Wildseeds").Cards.Count == 4
                && PackageCatalog.Packages.Single(entry => entry.Name == "Rituals").Cards.Count == 4,
                "Wildseeds and Rituals keep their component cards in one named package each");
            Check(CategoryCatalog.LoadUserFile(path) && File.ReadAllText(path) == bundled,
                "Subsequent loads preserve the user's file");
            CheckStarterUpgrade(path, bundled);
            Check(!CategoryCatalog.Matches(Card("CORE_REV_361"), CuratedCategory.Packages)
                && PackageCatalog.Packages.Single(entry => entry.Name == "Wildseeds").Cards.Any(card => card.Id == "REV_361")
                && CategoryCatalog.Matches(Card("VAN_CS2_032"), CuratedCategory.AoE)
                && CategoryCatalog.Matches(Card("Core_UNG_072"), CuratedCategory.Value)
                && CategoryCatalog.Matches(Card("WON_135"), CuratedCategory.WinConditions),
                "Core, Classic and explicitly listed alternative printings share curated membership");
            Check(!CategoryCatalog.Matches(null, CuratedCategory.AoE)
                && !CategoryCatalog.Matches(Card("unlisted-card"), CuratedCategory.AoE),
                "Missing and unlisted cards never acquire a category by text guessing");
            Check(CategoryCatalog.Matches(Card("EX1_066"), CuratedCategory.TECHW)
                && CategoryCatalog.Matches(Card("SW_072"), CuratedCategory.TECHW)
                && CategoryCatalog.Matches(Card("CFM_790"), CuratedCategory.TECHW)
                && CategoryCatalog.Matches(Card("EX1_544"), CuratedCategory.TECHW),
                "TECHW includes weapon/secret hate and deliberate hand disruption");
            foreach (string id in new[] { "CS2_029", "EX1_011", "DAL_735", "GVG_016", "NEW1_029" })
                Check(!CategoryCatalog.Matches(Card(id), CuratedCategory.TECHW),
                    "Ordinary burn, healing, self-silence, self-mill and giving the opponent discounts are not TECHW: " + id);
            foreach (string id in new[] { "AT_122", "BOT_544", "BT_126", "SCH_514", "WORK_030" })
                Check(!CategoryCatalog.Matches(Card(id), CuratedCategory.AoE),
                    "Conditional single-target damage and friendly-only effects are not mislabeled as AoE: " + id);

            var flame = Card("CS2_032"); var draw = Card("CS2_023"); var oracle = Card("EX1_050");
            var leeroy = Card("EX1_116"); var package = Card("REV_361"); var unrelated = Card("unlisted-card");
            flame.CardType = flame.Definition.CardType = TAG_CARDTYPE.SPELL;
            draw.CardType = draw.Definition.CardType = TAG_CARDTYPE.SPELL;
            var cards = new[] { flame, draw, oracle, leeroy, package, unrelated };
            CollectionManager.Get().Cards.Clear(); CollectionManager.Get().Cards.AddRange(cards);
            CollectionManager.Get().Display.m_setFilterTray = null;
            KeywordCatalog.Invalidate(); FilterAvailability.Capture(cards);
            foreach (FilterModeBehavior behavior in new[] { FilterModeBehavior.Category, FilterModeBehavior.Expression })
            foreach (FilterMode mode in new[] { FilterMode.Single, FilterMode.Or, FilterMode.And })
            {
                ExtraFilterState.ClearAll(); CollectionQualitySettings.FilterModeBehavior = behavior;
                FilterModes.Set(FilterTrayKind.Categories, mode);
                ExtraFilterState.SetCategory(CuratedCategory.Draw, true);
                ExtraFilterState.SetCategory(CuratedCategory.Value, true);
                CollectionQualitySettings.HideEmptyFilters = true;
                foreach (var card in cards)
                {
                    bool expected = mode == FilterMode.And ? card == oracle
                        : mode == FilterMode.Or ? card == oracle || card == draw
                        : card == oracle;
                    Check(ExtraFilterState.Matches(card) == expected,
                        "Overlapping curated categories respect " + behavior + "/" + mode + ": " + card.CardId);
                    Check(FilterAvailability.TryMatchCurrent(card, out bool indexed) && indexed == expected,
                        "Indexed category matches agree with scalar filtering");
                }
                foreach (var category in CategoryCatalog.Categories)
                {
                    bool selected = ExtraFilterState.HasCategory(category.Value);
                    bool expected = category.Value == CuratedCategory.Uncategorized || selected || cards.Any(card => ExtraFilterState.MatchesCandidate(card,
                        FilterTrayKind.Categories, (int)category.Value));
                    Check(FilterAvailability.Extra(FilterTrayKind.Categories, (int)category.Value, selected) == expected,
                        "Hide-empty keeps Uncategorized visible and uses candidate semantics for other categories");
                }
                ExtraFilterState.SetType(TAG_CARDTYPE.SPELL, true);
                foreach (var card in cards)
                    Check(FilterAvailability.TryMatchCurrent(card, out bool indexed) && indexed == ExtraFilterState.Matches(card),
                        "Categories intersect native card-type selections in both matching paths");
            }

            ExtraFilterState.ClearAll(); CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Or);
            ExtraFilterState.SetCategory(CuratedCategory.AoE, true);
            var equality = Card("EX1_619"); var consecration = Card("CS2_093");
            var comboCards = new[] { equality, consecration, flame };
            FilterAvailability.Capture(comboCards);
            Check(FilterAvailability.TryMatchCurrent(equality, out bool before) && !before,
                "Equality alone is not in the curated AoE starter selection");
            const string custom = "{\"version\":1,\"entries\":[{\"id\":\"equality-consecration\","
                + "\"name\":\"Equality + Consecration\",\"categories\":[\"aoe\"],\"cards\":["
                + "{\"id\":\"EX1_619\",\"count\":1},{\"id\":\"CS2_093\",\"count\":2}]}]}";
            File.WriteAllText(path, custom);
            Check(CategoryCatalog.LoadUserFile(path) && File.ReadAllText(path) == custom,
                "Reloading applies edited membership without overwriting the curated file");
            var combo = CategoryCatalog.Entries.Single();
            Check(combo.Name == "Equality + Consecration" && combo.Cards.Count == 2
                && combo.Cards[1].Count == 2, "A combo retains one identity, all cards and per-card copy counts");
            CheckCategoryJsonReader(custom);
            foreach (var card in comboCards)
                Check(FilterAvailability.TryMatchCurrent(card, out bool indexed) && indexed == (card != flame),
                    "Reload invalidates the bitset index without changing the native result pool");

            foreach (string invalid in new[] { "{broken", custom.Replace("\"version\":1", "\"version\":2"),
                custom.Replace("\"aoe\"", "\"typo\""), custom.Replace("\"count\":2", "\"count\":0"),
                custom.Replace("CS2_093", "CORE_EX1_619"), custom.Replace("\"count\":2", "\"count\":3") })
            {
                File.WriteAllText(path, invalid);
                Check(!CategoryCatalog.LoadUserFile(path) && File.ReadAllText(path) == invalid
                    && CategoryCatalog.Matches(flame, CuratedCategory.AoE),
                    "Invalid data falls back to the bundled catalog and leaves the user's file intact");
            }
            File.WriteAllText(path, "{\"version\":1,\"entries\":[]}");
            Check(CategoryCatalog.LoadUserFile(path) && !CategoryCatalog.Matches(flame, CuratedCategory.AoE),
                "An explicitly empty curated catalog stays empty rather than silently restoring defaults");
            Check(!ExtraFilterState.IsActiveForKind(FilterTrayKind.Categories), "Deleting an empty category clears its active filter state");
            ExtraFilterState.ClearAll();
            Check(!ExtraFilterState.IsActive && ExtraFilterState.Matches(unrelated), "Clear All removes curated selections");
        }
        finally
        {
            ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
            CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
            CollectionQualitySettings.HideEmptyFilters = false; FilterModes.StartSession(); FilterAvailability.Clear();
            File.Delete(path); CategoryCatalog.LoadUserFile(path); Directory.Delete(dir, true);
        }
    }

    private static void CheckCompleteCategoryCoverage()
    {
        var canonical = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        int printings = 0;
        using (var stream = typeof(CategoryCatalog).Assembly.GetManifestResourceStream("BetterCollectionManager.Tests.CategoryAudit.tsv"))
        using (var reader = new StreamReader(stream))
        {
            reader.ReadLine();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] row = line.Split('\t');
                canonical.Add(row[1]); printings++;
                var expected = new System.Collections.Generic.HashSet<string>(row[6].Split(';'), StringComparer.Ordinal);
                var card = Card(row[0]);
                foreach (var category in CategoryCatalog.Categories)
                    Check(CategoryCatalog.Matches(card, category.Value) == (category.Value == CuratedCategory.Uncategorized
                            ? !expected.Any(id => !string.IsNullOrWhiteSpace(id) && id != "packages") : expected.Contains(category.Id)),
                        "Every audited printing agrees with runtime membership: " + row[0] + "/" + category.Id);
            }
        }
        Check(printings == 7538 && canonical.Count == 6277,
            "The pinned full Constructed catalog covers all playable printings and canonical IDs");
    }

    private static void CheckStarterUpgrade(string path, string bundled)
    {
        string original;
        using (var stream = typeof(CategoryCatalog).Assembly.GetManifestResourceStream("BetterCollectionManager.Tests.CategoriesStarterV1.json"))
        using (var reader = new StreamReader(stream)) original = reader.ReadToEnd();
        File.WriteAllText(path, original.Replace("\n", "\r\n"));
        Check(CategoryCatalog.LoadUserFile(path) && File.ReadAllText(path) == bundled
            && CategoryCatalog.Entries.Count > 1000 && File.Exists(path + ".starter-v1.bak"),
            "An unchanged original starter catalog upgrades to the full selection, with a backup, including on CRLF files");
        string backup = File.ReadAllText(path + ".starter-v1.bak");
        string edited = original.Replace("Flamestrike", "My Flamestrike");
        File.WriteAllText(path, edited);
        Check(CategoryCatalog.LoadUserFile(path) && File.ReadAllText(path) == edited && CategoryCatalog.Entries.Count == 110
            && File.ReadAllText(path + ".starter-v1.bak") == backup,
            "A user's edited starter catalog remains intact and an existing backup is preserved");
        File.WriteAllText(path, bundled); CategoryCatalog.LoadUserFile(path);
    }

    private static void CheckCategoryJsonReader(string custom)
    {
        string escaped = custom.Replace("Equality + Consecration",
            "\\u00c4oE \\\"quoted\\\" \\\\ \\/ \\b\\f\\n\\r\\t \\uD83C\\uDF1F");
        Check(CategoryCatalog.Parse(escaped).Single().Name == "ÄoE \"quoted\" \\ / \b\f\n\r\t \ud83c\udf1f",
            "Editable JSON accepts Unicode escapes, surrogate pairs and escaped string characters");
        string metadata = custom.Replace("\"version\":1", "\"version\":1,\"metadata\":{\"on\":true,\"off\":false,"
            + "\"optional\":null,\"number\":-0.125e+2,\"nested\":[true,null,{\"text\":\"ok\"}]}");
        Check(CategoryCatalog.Parse(metadata).Single().Cards.Count == 2,
            "Unknown metadata can contain normal JSON literals, fractional numbers and nested containers");
        foreach (string invalid in new[] {
            custom + "garbage", custom.Replace("\"version\":1", "\"version\":1,\"version\":1"),
            custom.Replace("\"version\":1", "\"version\":\"1\""), custom.Replace("\"version\":1", "\"version\":01"),
            custom.Replace("\"version\":1", "\"version\":+1"), custom.Replace("\"version\":1", "\"version\":1."),
            custom.Replace("\"version\":1", "\"version\":1e"), custom.Replace("\"count\":2", "\"count\":1.5"),
            custom.Replace("\"count\":2", "\"count\":2147483648"), custom.Replace("\"count\":2", "\"count\":true"),
            custom.Replace("Equality + Consecration", "bad\\q"), custom.Replace("Equality + Consecration", "bad\\u12xy"),
            custom.Replace("Equality + Consecration", "bad\nname"), custom.Replace("\"aoe\"", "\"aoe\","),
            custom.Replace("\"entries\":[", "\"entries\":[,"), custom.Replace("\"entries\":[", "\"entries\":[null,"),
            custom.Substring(0, custom.Length - 1), custom.Replace("\"version\":1", "\"version\":1,\"deep\":"
                + new string('[', 65) + "null" + new string(']', 65))
        })
        {
            bool rejected = false;
            try { CategoryCatalog.Parse(invalid); } catch (FormatException) { rejected = true; }
            Check(rejected, "Invalid editable JSON is rejected clearly without optional serializer types");
        }
    }
}
