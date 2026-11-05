using System;
using System.IO;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckCategoryCombos()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-combos-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        try
        {
            Directory.CreateDirectory(directory); File.WriteAllText(path, "{\"version\":1,\"entries\":[]}");
            ExtraFilterState.ClearAll(); CollectionQualitySettings.RememberFilterModes = false; FilterModes.StartSession();
            Check(FilterModes.Get(FilterTrayKind.Categories) == FilterMode.Single, "Categories defaults to Single");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Categories.Length == 2, "Empty categories are absent from the menu even in an empty base catalog");
            var deck = new CategoryDeckImport.Deck { Name = "fallback" };
            deck.Cards.Add("EX1_619"); deck.Cards.Add("CS2_093"); deck.Counts["EX1_619"] = 2; deck.Counts["CS2_093"] = 1;
            const string description = "Equality first.\nThen Consecration — <literal> text.";
            CategoryCatalog.AddCombo("AoE", "Equality + Consecration", description, deck);
            var combo = CategoryCatalog.Combos.Single();
            Check(CategoryCatalog.Categories.Length == 3 && combo.Category == CuratedCategory.AoE
                && combo.Name == "Equality + Consecration" && combo.Description == description
                && (int)combo.Appearance.MinionType == 0 && (int)combo.Appearance.SpellSchool == 0
                && combo.Cards.Count == 2 && combo.Cards.Single(c => c.Id == "EX1_619").Count == 2,
                "A combo persists its category, literal multilingual description, component identities and quantities");
            Check(!CategoryCatalog.Matches(Card("EX1_619"), CuratedCategory.AoE),
                "Adding one combo entry does not classify each individual component as standalone AoE");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Combos.Single().Id == combo.Id && CategoryCatalog.Combos.Single().Description == description,
                "Combo IDs, text and quantities survive collection reloads");
            CategoryCatalog.UpdateCombo(combo.Id, "Renamed combo", "Edited description");
            CategoryCatalog.LoadUserFile(path);
            var edited = CategoryCatalog.Combos.Single();
            Check(edited.Id == combo.Id && edited.Name == "Renamed combo" && edited.Description == "Edited description"
                && edited.Category == combo.Category && edited.ExactVersions == combo.ExactVersions
                && edited.Cards.Select(card => (card.Id, card.Count)).SequenceEqual(combo.Cards.Select(card => (card.Id, card.Count))),
                "Persistent metadata edits retain combo identity, category, exact editions and captured quantities");
            var appearance = new CategoryCatalog.ComboAppearance { Frame = "Weapon", Dragon = true, ShowAttack = true, ShowHealth = true,
                Attack = 12, Health = 3, ManaCost = 0, MinionType = TAG_RACE.DRAGON, SpellSchool = TAG_SPELL_SCHOOL.FIRE, CardText = "Independent card rules.\nSecond line." };
            CategoryCatalog.UpdateCombo(combo.Id, "Renamed combo", "Edited description", appearance);
            appearance.Frame = "Spell"; appearance.CardText = "Changed caller object"; appearance.MinionType = TAG_RACE.BEAST; appearance.SpellSchool = TAG_SPELL_SCHOOL.FROST; appearance.ManaCost = 12;
            CategoryCatalog.LoadUserFile(path);
            var customized = CategoryCatalog.Combos.Single();
            Check(customized.Appearance.Frame == "Weapon" && customized.Appearance.Dragon && customized.Appearance.ShowAttack
                && customized.Appearance.Attack == 12 && customized.Appearance.ShowHealth && customized.Appearance.Health == 3
                && customized.Appearance.MinionType == TAG_RACE.DRAGON
                && customized.Appearance.SpellSchool == TAG_SPELL_SCHOOL.FIRE
                && customized.Appearance.ManaCost == 0
                && customized.Appearance.CardText == "Independent card rules.\nSecond line." && customized.Description == "Edited description"
                && customized.Id == combo.Id && customized.ExactVersions == combo.ExactVersions,
                "V9 files retain custom zero mana, spell school, minion type and independent presentation options without depending on the caller's mutable appearance object");
            int savedFiles = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            foreach (var invalid in new[] { new CategoryCatalog.ComboAppearance { Frame = "Unknown" },
                new CategoryCatalog.ComboAppearance { Attack = -1 }, new CategoryCatalog.ComboAppearance { Health = 1000 },
                new CategoryCatalog.ComboAppearance { MinionType = (TAG_RACE)int.MaxValue },
                new CategoryCatalog.ComboAppearance { SpellSchool = (TAG_SPELL_SCHOOL)int.MaxValue },
                new CategoryCatalog.ComboAppearance { ManaCost = -1 }, new CategoryCatalog.ComboAppearance { ManaCost = 1000 },
                new CategoryCatalog.ComboAppearance { CardText = new string('x', 2001) } })
            {
                bool rejected = false;
                try { CategoryCatalog.UpdateCombo(combo.Id, "Renamed combo", "Edited description", invalid); } catch (FormatException) { rejected = true; }
                Check(rejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == savedFiles
                    && CategoryCatalog.Combos.Single().Appearance.Frame == "Weapon",
                    "Invalid appearance settings cannot create an edit or replace a valid saved appearance");
            }
            ExtraFilterState.SetCategory(CuratedCategory.AoE, true);
            CollectionQualitySettings.HideEmptyFilters = true; FilterAvailability.Capture(new[] { Card("EX1_619"), Card("CS2_093") });
            Check(FilterAvailability.Extra(FilterTrayKind.Categories, (int)CuratedCategory.AoE, false),
                "A combo-only category remains available when ordinary card results are empty");
            int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            foreach (var bad in new[] { new[] { "", "ok" }, new[] { "Name", new string('x', 2001) } })
            {
                bool rejected = false;
                try { CategoryCatalog.AddCombo("AoE", bad[0], bad[1], deck); } catch (FormatException) { rejected = true; }
                Check(rejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                    "Invalid combo metadata is rejected before creating any persistent edit");
                rejected = false;
                try { CategoryCatalog.UpdateCombo(combo.Id, bad[0], bad[1]); } catch (FormatException) { rejected = true; }
                Check(rejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files
                    && CategoryCatalog.Combos.Single().Name == "Renamed combo",
                    "Invalid combo updates neither create a file nor replace previously saved metadata");
            }
            File.WriteAllText(Path.Combine(CategoryCatalog.DeckDirectory(path), "invalid.combo"), "broken");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Combos.Count == 1, "A malformed optional combo does not disable valid combos or filters");
            CategoryCatalog.DeleteCombo(combo.Id);
            Check(CategoryCatalog.Combos.Count == 0 && CategoryCatalog.Categories.Length == 2
                && !ExtraFilterState.HasCategory(CuratedCategory.AoE)
                && !CategoryCatalog.ComboCategories.Any(category => category.Value == CuratedCategory.AoE),
                "Deleting the last combo removes its empty category and clears the stale selection");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Combos.Count == 0 && CategoryCatalog.Categories.Length == 2,
                "Combo deletion remains effective after replaying its original add file");
            File.WriteAllText(Path.Combine(CategoryCatalog.DeckDirectory(path), "edit-999999999999999999.editcombo"),
                combo.Id + "\nBCM-CATEGORY-COMBO 2\ninvalid\n");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Combos.Count == 0, "A stale edit cannot resurrect a deleted combo");
            var editions = new CategoryDeckImport.Deck { Name = "AoE" };
            editions.Cards.Add("CORE_CS2_093"); editions.Cards.Add("VAN_CS2_093");
            editions.Counts["CS2_093"] = 2;
            editions.RawCounts["CORE_CS2_093"] = 1; editions.RawCounts["VAN_CS2_093"] = 1;
            CategoryCatalog.AddCombo("AoE", "Exact editions", "", editions);
            combo = CategoryCatalog.Combos.Single();
            Check(combo.ExactVersions && combo.Cards.Count == 2
                && combo.Cards.Single(card => card.Id == "CORE_CS2_093").Count == 1
                && combo.Cards.Single(card => card.Id == "VAN_CS2_093").Count == 1,
                "V2 combo files preserve exact Core and Vanilla editions and their separate captured quantities");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Combos.Single().ExactVersions && CategoryCatalog.Combos.Single().Cards.Any(card => card.Id == "CORE_CS2_093"),
                "Reloading a V2 combo does not normalize its edition prefixes away");
            CategoryCatalog.DeleteCombo(combo.Id);
            string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
            foreach (int version in new[] { 2, 3, 4, 5 })
            {
                string id = "legacy-v" + version;
                File.WriteAllText(Path.Combine(CategoryCatalog.DeckDirectory(path), id + ".combo"),
                    "BCM-CATEGORY-COMBO " + version + "\n" + Encode("AoE") + "\n" + Encode("Legacy appearance") + "\n" + Encode("Inspect text") + "\n"
                    + (version >= 3 ? "1\tMinion\t1\t1\t7\t1\t9\t" + Encode("Saved rules")
                        + (version >= 4 ? "\t" + (int)TAG_RACE.DRAGON : "") + (version >= 5 ? "\t0" : "") + "\n" : "") + "1\tCORE_CS2_093\n");
                CategoryCatalog.LoadUserFile(path);
                var legacy = CategoryCatalog.Combos.Single();
                Check(legacy.ExactVersions && legacy.Cards.Single().Id == "CORE_CS2_093"
                    && legacy.Appearance.ManaCost == null
                    && (int)legacy.Appearance.SpellSchool == 0 && (int)legacy.Appearance.MinionType == (version >= 4 ? (int)TAG_RACE.DRAGON : 0)
                    && legacy.Appearance.CardText == (version >= 3 ? "Saved rules" : "Inspect text")
                    && (version < 3 || (legacy.Appearance.Frame == "Minion" && legacy.Appearance.Dragon
                        && legacy.Appearance.ShowAttack && legacy.Appearance.Attack == 7 && legacy.Appearance.ShowHealth && legacy.Appearance.Health == 9)),
                    "Existing V" + version + " files default spell school to None and retain their editions, types, appearance and text");
                CategoryCatalog.UpdateCombo(legacy.Id, legacy.Name, legacy.Description);
                CategoryCatalog.LoadUserFile(path);
                Check(CategoryCatalog.Combos.Single().Appearance.MinionType == legacy.Appearance.MinionType
                    && (int)CategoryCatalog.Combos.Single().Appearance.SpellSchool == 0
                    && CategoryCatalog.Combos.Single().Appearance.CardText == legacy.Appearance.CardText,
                    "Editing V" + version + " files preserves their type, school and card text in V9");
                CategoryCatalog.DeleteCombo(legacy.Id);
            }
            File.WriteAllText(Path.Combine(CategoryCatalog.DeckDirectory(path), "legacy.combo"),
                "BCM-CATEGORY-COMBO 1\n" + Encode("AoE") + "\n" + Encode("Legacy combo") + "\n" + Encode("") + "\n1\tCS2_093\n");
            CategoryCatalog.LoadUserFile(path);
            Check(!CategoryCatalog.Combos.Single().ExactVersions && CategoryCatalog.Combos.Single().Cards.Single().Id == "CS2_093",
                "Existing V1 combos remain readable without claiming to know a discarded edition");
            CategoryCatalog.UpdateCombo("legacy", "Edited legacy", "Still the same edition policy");
            CategoryCatalog.LoadUserFile(path);
            Check(!CategoryCatalog.Combos.Single().ExactVersions && CategoryCatalog.Combos.Single().Name == "Edited legacy",
                "Editing a legacy combo preserves its version-resolution policy");
            var replacement = new CategoryDeckImport.Deck();
            replacement.Cards.Add("CORE_EX1_619"); replacement.RawCounts["CORE_EX1_619"] = 2;
            replacement.Cards.Add("VAN_EX1_619"); replacement.RawCounts["VAN_EX1_619"] = 3;
            var beforeMembers = CategoryCatalog.Combos.Single();
            CategoryCatalog.UpdateCombo(beforeMembers.Id, beforeMembers.Name, beforeMembers.Description, cards: replacement);
            CategoryCatalog.LoadUserFile(path);
            var afterMembers = CategoryCatalog.Combos.Single();
            Check(afterMembers.Id == beforeMembers.Id && afterMembers.Category == beforeMembers.Category
                && afterMembers.Name == beforeMembers.Name && afterMembers.Description == beforeMembers.Description
                && afterMembers.Appearance.CardText == beforeMembers.Appearance.CardText && afterMembers.ExactVersions
                && afterMembers.Cards.Count == 2 && afterMembers.Cards.Single(card => card.Id == "CORE_EX1_619").Count == 2
                && afterMembers.Cards.Single(card => card.Id == "VAN_EX1_619").Count == 3,
                "Replacing legacy members keeps identity and presentation, removes former cards and persists exact editions and quantities");
            int memberFiles = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            foreach (int count in new[] { -1, 0, 1001 })
            {
                replacement.RawCounts["CORE_EX1_619"] = count;
                bool rejected = false;
                try { CategoryCatalog.UpdateCombo(afterMembers.Id, afterMembers.Name, afterMembers.Description, cards: replacement); }
                catch (FormatException) { rejected = true; }
                Check(rejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == memberFiles
                    && CategoryCatalog.Combos.Single().Cards.Single(card => card.Id == "CORE_EX1_619").Count == 2,
                    "Invalid replacement quantities leave the saved members and edit files intact");
            }
            bool emptyRejected = false;
            try { CategoryCatalog.UpdateCombo(afterMembers.Id, afterMembers.Name, afterMembers.Description, cards: new CategoryDeckImport.Deck()); }
            catch (FormatException) { emptyRejected = true; }
            Check(emptyRejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == memberFiles,
                "An empty replacement cannot create an edit");
        }
        finally
        {
            ExtraFilterState.ClearAll(); CollectionQualitySettings.HideEmptyFilters = false; FilterAvailability.Clear();
            foreach (string file in Directory.GetFiles(CategoryCatalog.DeckDirectory(path))) File.Delete(file);
            CategoryCatalog.LoadUserFile(path); Directory.Delete(directory, true);
        }
    }
}
