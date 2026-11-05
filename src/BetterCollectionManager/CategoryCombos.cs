using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BetterCollectionManager
{
    internal static partial class CategoryCatalog
    {
        internal class Combo
        {
            public string Id, Name, Description;
            public bool IsPackage;
            public PackageKind PackageGroup = PackageKind.Custom;
            internal string SavedPackageGroup;
            public string PackageClass = "";
            public string Artwork = "";
            // Transient collection action; never serialized.
            internal bool AddAll { get; set; }
            public CuratedCategory Category;
            public IReadOnlyList<CardRequirement> Cards;
            public bool ExactVersions;
            public ComboAppearance Appearance = new ComboAppearance();
        }
        internal sealed class ComboAppearance
        {
            public string Frame = "Spell", CardText = "";
            public bool Dragon, ShowAttack, ShowHealth;
            public int Attack, Health;
            public int? ManaCost;
            public TAG_RACE MinionType = (TAG_RACE)0;
            public TAG_SPELL_SCHOOL SpellSchool = (TAG_SPELL_SCHOOL)0;
            public bool HasAttack => Frame == "Minion" || Frame == "Weapon";
            public bool HasSecondary => Frame != "Spell";
            public bool AttackVisible => HasAttack && ShowAttack;
            public bool SecondaryVisible => HasSecondary && ShowHealth;
            // The secondary editor value is health, durability or armor for the selected frame.
            public string SecondaryCaption => Frame == "Minion" ? "Health" : Frame == "Hero" ? "Armor" : "Durability";
            public ComboAppearance Copy() => (ComboAppearance)MemberwiseClone();
            public static readonly string[] Frames = { "Spell", "Minion", "Weapon", "Hero", "Location" };
            internal void Validate()
            {
                if (Array.IndexOf(Frames, Frame) < 0) throw new FormatException("Unknown combo card frame.");
                if ((int)MinionType != 0 && !Enum.IsDefined(typeof(TAG_RACE), MinionType)) throw new FormatException("Unknown combo minion type.");
                if ((int)SpellSchool != 0 && !Enum.IsDefined(typeof(TAG_SPELL_SCHOOL), SpellSchool)) throw new FormatException("Unknown combo spell school.");
                if (CardText == null || CardText.Length > 2000) throw new FormatException("Card text is too long (maximum 2000).");
                if (Attack < 0 || Attack > 999 || Health < 0 || Health > 999) throw new FormatException("Card values must be between 0 and 999.");
                if (ManaCost < 0 || ManaCost > 999) throw new FormatException("Mana cost must be between 0 and 999, or blank for the sum.");
            }
        }
        private static string AppearanceLine(Combo combo)
        {
            combo.Appearance.Validate();
            return string.Join("\t", combo.ExactVersions ? "1" : "0", combo.Appearance.Frame,
                combo.Appearance.Dragon ? "1" : "0", combo.Appearance.ShowAttack ? "1" : "0", combo.Appearance.Attack.ToString(CultureInfo.InvariantCulture),
                combo.Appearance.ShowHealth ? "1" : "0", combo.Appearance.Health.ToString(CultureInfo.InvariantCulture), Encode(combo.Appearance.CardText),
                ((int)combo.Appearance.MinionType).ToString(CultureInfo.InvariantCulture),
                ((int)combo.Appearance.SpellSchool).ToString(CultureInfo.InvariantCulture))
                + (combo.IsPackage ? "" : "\t" + combo.Appearance.ManaCost?.ToString(CultureInfo.InvariantCulture));
        }
        internal static IEnumerable<Category> ComboCategories
        {
            get
            {
                EnsureLoaded();
                foreach (Category category in s_categories)
                    if (IsComboCategory(category.Value) || category.Value == CuratedCategory.Combos) yield return category;
            }
        }
        internal static bool IsComboDestinationName(string name)
            => CategoryDeckImport.NameKey(name ?? "") == "COMBOS" || IsComboCategoryName(name);
        internal static bool IsComboCategory(CuratedCategory value)
            => value != CuratedCategory.Uncategorized && value != CuratedCategory.Packages && value != CuratedCategory.Combos;
        internal static bool IsComboCategoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string key = CategoryDeckImport.NameKey(name);
            return key != "UNCATEGORIZED" && key != "PACKAGES" && key != "COMBOS";
        }
        private static IReadOnlyList<Combo> s_combos = Array.Empty<Combo>();
        public static IReadOnlyList<Combo> Combos { get { EnsureLoaded(); return s_combos; } }
        internal static IEnumerable<Combo> CollectionEntries
        {
            get
            {
                foreach (Combo entry in Combos) yield return entry;
                foreach (Combo entry in PackageCatalog.Packages)
                    if (entry.Id.StartsWith("builtin:", StringComparison.Ordinal)) yield return entry;
            }
        }
        private static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        private static string Decode(string text) => new UTF8Encoding(false, true).GetString(Convert.FromBase64String(text));
        private static Combo ParseCombo(string text, string id, out string category)
        {
            using (var reader = new StringReader(text))
            {
                string version = reader.ReadLine();
                if (version != "BCM-CATEGORY-COMBO 1" && version != "BCM-CATEGORY-COMBO 2" && version != "BCM-CATEGORY-COMBO 3"
                    && version != "BCM-CATEGORY-COMBO 4" && version != "BCM-CATEGORY-COMBO 5" && version != "BCM-CATEGORY-COMBO 6" && version != "BCM-CATEGORY-COMBO 7" && version != "BCM-CATEGORY-COMBO 8" && version != "BCM-CATEGORY-COMBO 9") throw new FormatException("Unsupported combo format.");
                category = CategoryDeckImport.CleanName(Decode(reader.ReadLine()));
                bool package = version == "BCM-CATEGORY-COMBO 6" || version == "BCM-CATEGORY-COMBO 7" || version == "BCM-CATEGORY-COMBO 8";
                Combo combo = package ? new PackageCatalog.Package() : new Combo();
                combo.Id = id; combo.Name = CategoryDeckImport.CleanName(Decode(reader.ReadLine())); combo.Description = Decode(reader.ReadLine());
                combo.ExactVersions = version == "BCM-CATEGORY-COMBO 2";
                if (version == "BCM-CATEGORY-COMBO 3" || version == "BCM-CATEGORY-COMBO 4" || version == "BCM-CATEGORY-COMBO 5" || version == "BCM-CATEGORY-COMBO 9" || package)
                {
                    string[] fields = (reader.ReadLine() ?? "").Split('\t');
                    bool Flag(int i) { if (fields[i] != "0" && fields[i] != "1") throw new FormatException("Invalid combo flag."); return fields[i] == "1"; }
                    if (fields.Length != (version == "BCM-CATEGORY-COMBO 9" ? 11 : (version == "BCM-CATEGORY-COMBO 5" || package) ? 10 : version == "BCM-CATEGORY-COMBO 4" ? 9 : 8)) throw new FormatException("Invalid combo appearance.");
                    combo.ExactVersions = Flag(0);
                    combo.Appearance = new ComboAppearance { Frame = fields[1], Dragon = Flag(2), ShowAttack = Flag(3),
                        Attack = int.Parse(fields[4], CultureInfo.InvariantCulture), ShowHealth = Flag(5), Health = int.Parse(fields[6], CultureInfo.InvariantCulture), CardText = Decode(fields[7]) };
                    if (fields.Length >= 9) combo.Appearance.MinionType = (TAG_RACE)int.Parse(fields[8], CultureInfo.InvariantCulture);
                    if (fields.Length >= 10) combo.Appearance.SpellSchool = (TAG_SPELL_SCHOOL)int.Parse(fields[9], CultureInfo.InvariantCulture);
                    if (fields.Length >= 11 && fields[10].Length > 0) combo.Appearance.ManaCost = int.Parse(fields[10], CultureInfo.InvariantCulture);
                    combo.Appearance.Validate();
                }
                else combo.Appearance.CardText = combo.Description;
                if (package)
                {
                    if (reader.ReadLine() != "Package") throw new FormatException("Unknown saved collection entry kind.");
                    combo.IsPackage = true;
                    combo.Artwork = reader.ReadLine() ?? "";
                    CategoryDeckImport.NormalizeSnapshotCard(combo.Artwork);
                    if (version == "BCM-CATEGORY-COMBO 7" || version == "BCM-CATEGORY-COMBO 8") combo.SavedPackageGroup = CategoryDeckImport.CleanName(Decode(reader.ReadLine()));
                }
                if (version == "BCM-CATEGORY-COMBO 8")
                {
                    combo.PackageClass = reader.ReadLine() ?? "";
                    if (combo.PackageClass.Length > 40) throw new FormatException("Invalid package class.");
                    foreach (char c in combo.PackageClass)
                        if (c < 'A' || c > 'Z') throw new FormatException("Invalid package class.");
                }
                if (combo.Description.Length > 2000) throw new FormatException("Description is too long (maximum 2000).");
                var cards = new List<CardRequirement>(); var seen = new HashSet<string>(StringComparer.Ordinal);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                        || count < 1 || count > 1000) throw new FormatException("Invalid combo card quantity.");
                    CategoryDeckImport.NormalizeSnapshotCard(parts[1]); // Validate without discarding the edition.
                    string card = combo.ExactVersions ? parts[1] : CategoryDeckImport.NormalizeSnapshotCard(parts[1]);
                    if (!seen.Add(card) || cards.Count >= 1000) throw new FormatException("Duplicate or oversized combo.");
                    cards.Add(new CardRequirement(card, count, Array.Empty<string>()));
                }
                if (cards.Count == 0) throw new FormatException("Add at least one card to the combo.");
                combo.Cards = cards.AsReadOnly(); return combo;
            }
        }
        public static void AddCombo(string category, string name, string description, CategoryDeckImport.Deck deck, ComboAppearance appearance = null, string packageArtwork = null)
        {
            if (packageArtwork != null) CategoryDeckImport.NormalizeSnapshotCard(packageArtwork);
            if (packageArtwork == null && !IsComboDestinationName(category)) throw new InvalidOperationException("Choose a card-role category for the combo.");
            if (s_userFilePath == null) throw new InvalidOperationException("Open the collection first.");
            var settings = new Combo { IsPackage = packageArtwork != null, ExactVersions = true, Appearance = appearance?.Copy() ?? new ComboAppearance { CardText = description ?? "" } };
            var text = new StringBuilder(packageArtwork != null ? "BCM-CATEGORY-COMBO 7\n" : "BCM-CATEGORY-COMBO 9\n");
            text.Append(Encode(CategoryDeckImport.CleanName(category))).Append('\n')
                .Append(Encode(CategoryDeckImport.CleanName(name))).Append('\n').Append(Encode(description ?? "")).Append('\n').Append(AppearanceLine(settings)).Append('\n');
            if (packageArtwork != null) text.Append("Package\n").Append(packageArtwork).Append('\n').Append(Encode(category)).Append('\n');
            var ids = new List<string>(deck.Cards); ids.Sort(StringComparer.Ordinal);
            var normalized = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in ids)
            {
                string card = CategoryDeckImport.NormalizeSnapshotCard(raw);
                normalized.Add(card);
                int count = deck.RawCounts.TryGetValue(raw, out int quantity) ? quantity
                    : deck.Counts.TryGetValue(card, out quantity) ? quantity : 1;
                text.Append(count.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(raw).Append('\n');
            }
            ParseCombo(text.ToString(), "new", out _);
            WriteDeckEdit(text.ToString(), ".combo"); LoadUserFile(s_userFilePath);
            DebugLog.Msg("Combo saved: category=" + category + "; name=" + name + "; cards=" + normalized.Count);
        }
        public static void DeleteCombo(string id)
        {
            bool found = false; foreach (Combo combo in CollectionEntries) if (combo.Id == id) found = true;
            if (!found) return;
            WriteDeckEdit(id, ".uncombo"); LoadUserFile(s_userFilePath);
        }
        public static void UpdateCombo(string id, string name, string description, ComboAppearance appearance = null, string packageArtwork = null,
            CategoryDeckImport.Deck cards = null, string categoryName = null)
        {
            Combo existing = null;
            foreach (Combo combo in CollectionEntries) if (combo.Id == id) existing = combo;
            if (existing == null) throw new InvalidOperationException("This combo no longer exists.");
            string category = null;
            foreach (Category item in Categories) if (item.Value == existing.Category) category = item.Name;
            if (categoryName != null) category = CategoryDeckImport.CleanName(categoryName);
            if (existing.IsPackage) category = categoryName ?? PackageCatalog.GroupName(existing.PackageGroup);
            if (category == null) throw new InvalidOperationException("The combo category no longer exists.");
            if (!existing.IsPackage && !IsComboDestinationName(category)) throw new InvalidOperationException("Choose a card-role category for the combo.");
            var settings = new Combo { IsPackage = existing.IsPackage, ExactVersions = cards != null || existing.ExactVersions, Appearance = appearance?.Copy() ?? existing.Appearance.Copy() };
            var text = new StringBuilder(existing.IsPackage ? "BCM-CATEGORY-COMBO 7\n" : "BCM-CATEGORY-COMBO 9\n");
            text.Append(Encode(category)).Append('\n').Append(Encode(CategoryDeckImport.CleanName(name))).Append('\n')
                .Append(Encode(description ?? "")).Append('\n').Append(AppearanceLine(settings)).Append('\n');
            if (existing.IsPackage) text.Append("Package\n").Append(packageArtwork ?? existing.Artwork).Append('\n').Append(Encode(category)).Append('\n');
            if (cards == null)
                foreach (CardRequirement card in existing.Cards)
                    text.Append(card.Count.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(card.Id).Append('\n');
            else
            {
                var ids = new List<string>(cards.Cards); ids.Sort(StringComparer.Ordinal);
                foreach (string card in ids)
                {
                    string normalized = CategoryDeckImport.NormalizeSnapshotCard(card);
                    int count = cards.RawCounts.TryGetValue(card, out int quantity) ? quantity
                        : cards.Counts.TryGetValue(normalized, out quantity) ? quantity : 1;
                    text.Append(count.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(card).Append('\n');
                }
            }
            ParseCombo(text.ToString(), id, out _); // Validate before creating a persistent edit.
            WriteDeckEdit(id + "\n" + text, ".editcombo"); LoadUserFile(s_userFilePath);
        }
    }
}
