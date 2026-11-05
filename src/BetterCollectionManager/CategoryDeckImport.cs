using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace BetterCollectionManager
{
    /// <summary>Named deck exports become additive category membership, independent of deck legality.</summary>
    internal static class CategoryDeckImport
    {
        internal const int MaxTextLength = 262144;
        internal sealed class Deck
        {
            public string Name;
            public readonly HashSet<string> Cards = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> RawCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        internal static string NameKey(string name)
            => string.Join(" ", name.Normalize(NormalizationForm.FormC).Split((char[])null,
                StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

        internal static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Give the deck a category name first.");
            name = string.Join(" ", name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (name.Length > 80) throw new FormatException("Category names can have at most 80 characters.");
            foreach (char c in name) if (char.IsControl(c)) throw new FormatException("Invalid category name.");
            return name;
        }

        internal static Deck Parse(string text, Func<int, string> resolve)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
                throw new FormatException("Copy a named deck first.");
            string name = null, code = null;
            using (var reader = new StringReader(text.TrimStart('\uFEFF')))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("###", StringComparison.Ordinal))
                    {
                        if (name != null) throw new FormatException("Import one named deck at a time.");
                        name = string.Join(" ", line.Substring(3).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                    }
                    else if (line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                    {
                        if (code != null) throw new FormatException("Import one deck code at a time.");
                        code = line;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Copy the whole deck, including ### Category name.");
            name = CleanName(name);
            if (string.IsNullOrEmpty(code) || code.Length > 32768) throw new FormatException("Missing or oversized deck code.");
            var data = new Reader(Convert.FromBase64String(code));
            if (data.Number() != 0 || data.Number() != 1) throw new FormatException("Unsupported deck code version.");
            int format = data.Number();
            if (format < 1 || format > 4) throw new FormatException("Unsupported deck format.");
            int heroes = data.Length();
            if (heroes != 1) throw new FormatException("Expected one deck hero.");
            data.Positive(); // Hero/skin is deliberately not category membership.
            var dbfIds = new HashSet<int>();
            for (int group = 1; group <= 3; group++)
            {
                int count = data.Length();
                for (int i = 0; i < count; i++)
                {
                    int id = data.Positive();
                    if (group == 3) data.Positive(); // Arbitrary positive quantities are valid for membership.
                    dbfIds.Add(id);
                }
            }
            if (!data.End)
            {
                int sideboards = data.Number();
                if (sideboards != 0 && sideboards != 1) throw new FormatException("Unsupported deck code extension.");
                if (sideboards == 1)
                    for (int group = 1; group <= 3; group++)
                    {
                        int count = data.Length();
                        for (int i = 0; i < count; i++)
                        {
                            int id = data.Positive();
                            if (group == 3) data.Positive();
                            data.Positive(); // Sideboard owner; included separately if it is in the deck.
                            dbfIds.Add(id);
                        }
                    }
            }
            if (!data.End || dbfIds.Count == 0 || dbfIds.Count > 1000)
                throw new FormatException("Empty, oversized or unsupported deck code.");
            var deck = new Deck { Name = name };
            foreach (int id in dbfIds)
            {
                string card = resolve(id);
                if (string.IsNullOrWhiteSpace(card)) throw new FormatException("Unknown card DBF ID " + id + "; nothing imported.");
                deck.Cards.Add(CategoryCatalog.NormalizeCardId(card));
            }
            return deck;
        }

        internal static string ResolveCard(int dbfId)
        {
            MethodInfo method = typeof(GameUtils).GetMethod("GetCardIdFromDbfId",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            return method?.Invoke(null, new object[] { dbfId }) as string;
        }

        internal static string SerializeCards(string name, IEnumerable<string> cards)
        {
            var result = new StringBuilder("BCM-CATEGORY-DECK 1\n").Append(CleanName(name)).Append('\n');
            var sorted = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string card in cards)
            {
                sorted.Add(NormalizeSnapshotCard(card));
            }
            if (sorted.Count == 0) throw new FormatException("Add at least one card to the deck first.");
            if (sorted.Count > 1000) throw new FormatException("Too many cards in category import.");
            foreach (string card in sorted) result.Append(card).Append('\n');
            return result.ToString();
        }

        internal static string NormalizeSnapshotCard(string card)
        {
            if (string.IsNullOrWhiteSpace(card) || card.Length > 128) throw new FormatException("Invalid deck card ID.");
            foreach (char c in card) if (char.IsWhiteSpace(c) || char.IsControl(c)) throw new FormatException("Invalid deck card ID.");
            string normalized = CategoryCatalog.NormalizeCardId(card);
            if (normalized.Length == 0) throw new FormatException("Invalid deck card ID.");
            return normalized;
        }

        internal static Deck ParseCards(string text)
        {
            if (text == null || text.Length > MaxTextLength) throw new FormatException("Invalid category deck snapshot.");
            using (var reader = new StringReader(text))
            {
                if (reader.ReadLine() != "BCM-CATEGORY-DECK 1") throw new FormatException("Unsupported category deck snapshot.");
                var deck = new Deck { Name = CleanName(reader.ReadLine()) };
                string line;
                while ((line = reader.ReadLine()) != null) if (line.Length > 0) deck.Cards.Add(line);
                // Use the same validation as live snapshots before applying any membership.
                SerializeCards(deck.Name, deck.Cards);
                var normalized = new Deck { Name = deck.Name };
                foreach (string card in deck.Cards) normalized.Cards.Add(CategoryCatalog.NormalizeCardId(card));
                return normalized;
            }
        }

        private sealed class Reader
        {
            private readonly byte[] m_data;
            private int m_position;
            internal Reader(byte[] data) { m_data = data; }
            internal bool End => m_position == m_data.Length;
            internal int Number()
            {
                uint value = 0;
                for (int shift = 0; shift <= 28; shift += 7)
                {
                    if (End) throw new FormatException("Truncated deck code.");
                    byte part = m_data[m_position++];
                    if (shift == 28 && (part & 0xf8) != 0) throw new FormatException("Deck code integer overflow.");
                    value |= (uint)(part & 127) << shift;
                    if ((part & 128) == 0) return (int)value;
                }
                throw new FormatException("Invalid deck code integer.");
            }
            internal int Positive()
            { int n = Number(); if (n < 1) throw new FormatException("Invalid card ID or quantity."); return n; }
            internal int Length()
            { int n = Number(); if (n > 1000) throw new FormatException("Deck code contains too many cards."); return n; }
        }
    }

    internal static partial class CategoryCatalog
    {
        private static string s_userFilePath;
        internal static string DeckDirectory(string catalogPath)
            => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalogPath)), "CategoryDecks");

        private static bool FindCategory(List<Category> categories, string name, out Category result)
        {
            string key = CategoryDeckImport.NameKey(name);
            foreach (Category category in categories)
                if (CategoryDeckImport.NameKey(category.Name) == key || CategoryDeckImport.NameKey(category.Id) == key)
                { result = category; return true; }
            result = default; return false;
        }

        private static Category NewDeckCategory(List<Category> categories, string name)
        {
            string key = CategoryDeckImport.NameKey(name);
            uint hash = 2166136261;
            foreach (char c in key) unchecked { hash = (hash ^ c) * 16777619; }
            int value = 1000 + (int)(hash & 0x3fffffff);
            foreach (Category category in categories)
                if ((int)category.Value == value) throw new FormatException("Category name collision; rename the deck.");
            return new Category((CuratedCategory)value, "deck-" + value.ToString(CultureInfo.InvariantCulture), name);
        }

        private static void LoadDeckFiles(string catalogPath)
        {
            s_combos = Array.Empty<Combo>();
            PackageCatalog.ResetGroups();
            PackageCatalog.SetActive(new List<Combo>());
            try
            {
                var combos = PackageCatalog.BuiltIns(s_entries);
                PackageCatalog.SetActive(combos);
                s_userFilePath = Path.GetFullPath(catalogPath);
                string directory = DeckDirectory(catalogPath);
                Directory.CreateDirectory(directory);
                var sources = new List<string>(Directory.GetFiles(directory, "*.txt"));
                sources.AddRange(Directory.GetFiles(directory, "*.cards"));
                sources.AddRange(Directory.GetFiles(directory, "*.remove"));
                sources.AddRange(Directory.GetFiles(directory, "*.combo"));
                sources.AddRange(Directory.GetFiles(directory, "*.uncombo"));
                sources.AddRange(Directory.GetFiles(directory, "*.editcombo"));
                sources.AddRange(Directory.GetFiles(directory, "*.folder"));
                sources.AddRange(Directory.GetFiles(directory, "*.bundle"));
                string[] files = sources.ToArray();
                Array.Sort(files, CompareEdits);
                var categories = new List<Category>(BuiltInCategories);
                var entries = new List<Entry>(s_entries);
                var imports = new Dictionary<CuratedCategory, HashSet<string>>();
                var effective = new Dictionary<CuratedCategory, HashSet<string>>();
                foreach (var pair in s_members) effective[pair.Key] = new HashSet<string>(pair.Value, StringComparer.Ordinal);
                bool changed = false;
                var explicitFolders = new HashSet<CuratedCategory>();
                foreach (string sourceFile in files)
                foreach (var edit in ReadDeckEdits(sourceFile, categories))
                {
                    string file = edit.Key, text = edit.Value;
                    try
                    {
                        if (Path.GetExtension(file) == ".folder")
                        {
                            using (var reader = new StringReader(text))
                            {
                                if (reader.ReadLine() != "BCM-CATEGORY-FOLDER 1") throw new FormatException("Unknown folder format.");
                                string kind = reader.ReadLine(), name = CategoryDeckImport.CleanName(reader.ReadLine());
                                if (kind == "Packages") PackageCatalog.EnsureGroup(name);
                                else if (kind == "Categories" && IsComboDestinationName(name))
                                {
                                    if (!FindCategory(categories, name, out Category folder))
                                    {
                                        folder = NewDeckCategory(categories, name); categories.Add(folder);
                                        effective[folder.Value] = new HashSet<string>(StringComparer.Ordinal);
                                    }
                                    explicitFolders.Add(folder.Value);
                                }
                                else throw new FormatException("Choose a valid folder name.");
                            }
                            changed = true; continue;
                        }
                        if (Path.GetExtension(file) == ".editcombo")
                        {
                            int separator = text.IndexOf('\n');
                            if (separator < 1) throw new FormatException("Invalid combo edit.");
                            string id = text.Substring(0, separator).TrimEnd('\r');
                            int index = combos.FindIndex(combo => combo.Id == id);
                            if (index < 0) continue; // A stale edit cannot resurrect a deleted combo.
                            Combo replacement = ParseCombo(text.Substring(separator + 1), id, out string destination);
                            if (!replacement.IsPackage && !IsComboDestinationName(destination)) destination = "Value";
                            if (replacement.IsPackage)
                            {
                                replacement.Category = CuratedCategory.Packages;
                                replacement.PackageGroup = replacement.SavedPackageGroup == null
                                    ? combos[index].PackageGroup : PackageCatalog.EnsureGroup(replacement.SavedPackageGroup);
                                replacement.PackageClass = combos[index].PackageClass;
                                combos[index] = replacement; changed = true; continue;
                            }
                            if (!FindCategory(categories, destination, out Category destinationCategory))
                            {
                                destinationCategory = NewDeckCategory(categories, destination); categories.Add(destinationCategory);
                                effective[destinationCategory.Value] = new HashSet<string>(StringComparer.Ordinal);
                            }
                            replacement.Category = destinationCategory.Value; combos[index] = replacement;
                            changed = true; continue;
                        }
                        if (Path.GetExtension(file) == ".uncombo")
                        {
                            string id = text.Trim(); combos.RemoveAll(combo => combo.Id == id);
                            changed = true; continue;
                        }
                        if (Path.GetExtension(file) == ".combo")
                        {
                            Combo combo = ParseCombo(text, Path.GetFileNameWithoutExtension(file), out string categoryName);
                            if (!combo.IsPackage && !IsComboDestinationName(categoryName)) categoryName = "Value";
                            if (combo.IsPackage) { combo.Category = CuratedCategory.Packages; combo.PackageGroup = PackageCatalog.EnsureGroup(combo.SavedPackageGroup ?? "Packages"); combos.Add(combo); changed = true; continue; }
                            if (!FindCategory(categories, categoryName, out Category comboCategory))
                            {
                                comboCategory = NewDeckCategory(categories, categoryName); categories.Add(comboCategory);
                                effective[comboCategory.Value] = new HashSet<string>(StringComparer.Ordinal);
                            }
                            combo.Category = comboCategory.Value; combos.Add(combo); changed = true; continue;
                        }
                        bool removing = Path.GetExtension(file) == ".remove";
                        CategoryDeckImport.Deck deck = Path.GetExtension(file) == ".txt"
                            ? CategoryDeckImport.Parse(text, CategoryDeckImport.ResolveCard) : CategoryDeckImport.ParseCards(text);
                        if (!IsComboCategoryName(deck.Name)) throw new FormatException("Choose a card-role category for standalone cards.");
                        if (!FindCategory(categories, deck.Name, out Category category))
                        {
                            if (removing) continue;
                            category = NewDeckCategory(categories, deck.Name); categories.Add(category);
                            effective[category.Value] = new HashSet<string>(StringComparer.Ordinal);
                        }
                        if (removing)
                        {
                            effective[category.Value].ExceptWith(deck.Cards);
                            changed = true;
                            continue;
                        }
                        if (!imports.TryGetValue(category.Value, out HashSet<string> cards))
                            imports[category.Value] = cards = new HashSet<string>(StringComparer.Ordinal);
                        cards.UnionWith(deck.Cards);
                        effective[category.Value].UnionWith(deck.Cards);
                        changed = true;
                    }
                    catch (Exception ex) { DebugLog.Warning("Category deck ignored: " + file + ": " + ex.Message); }
                }
                foreach (Category category in categories)
                    if (imports.TryGetValue(category.Value, out HashSet<string> cards))
                        foreach (string card in cards)
                            entries.Add(new Entry("deck-card-" + (int)category.Value + "-" + card, card,
                                new[] { category.Value }, new[] { new CardRequirement(card, 1, Array.Empty<string>()) }));
                PackageCatalog.SetActive(combos);
                var savedEntries = new List<Combo>();
                foreach (Combo entry in combos) if (!entry.Id.StartsWith("builtin:", StringComparison.Ordinal)) savedEntries.Add(entry);
                s_combos = savedEntries.AsReadOnly();
                var visibleCategories = new List<Category>();
                foreach (Category category in categories)
                    if (category.Value != CuratedCategory.Packages && (category.Value == CuratedCategory.Combos || category.Value == CuratedCategory.Uncategorized || explicitFolders.Contains(category.Value) || effective[category.Value].Count > 0 || combos.Exists(combo => combo.Category == category.Value)))
                        visibleCategories.Add(category);
                if (changed || visibleCategories.Count != categories.Count)
                    Apply(entries.AsReadOnly(), visibleCategories.ToArray(), effective);
            }
            catch (Exception ex) { DebugLog.Warning("Category deck imports unavailable; keeping other filters usable. " + ex.Message); }
        }

        internal static string CreateFolder(string name, bool package)
        {
            if (s_userFilePath == null) throw new InvalidOperationException("Open the collection first.");
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Enter a folder name.");
            name = CategoryDeckImport.CleanName(name);
            if (package)
            {
                if (CategoryDeckImport.NameKey(name) == "PACKAGES") throw new FormatException("Choose a subfolder name.");
                foreach (var group in PackageCatalog.Groups)
                    if (CategoryDeckImport.NameKey(group.Name) == CategoryDeckImport.NameKey(name)) return group.Name;
            }
            else
            {
                if (!IsComboCategoryName(name)) throw new FormatException("Choose a card-role folder name.");
                var categories = new List<Category>(s_categories);
                if (FindCategory(categories, name, out Category existing)) name = existing.Name;
                else NewDeckCategory(categories, name); // Validate before persisting.
                // Persist an explicit folder even for an existing role, so it
                // remains visible when its last card or combo is removed.
            }
            WriteDeckEdit("BCM-CATEGORY-FOLDER 1\n" + (package ? "Packages" : "Categories") + "\n" + name + "\n", ".folder");
            LoadUserFile(s_userFilePath);
            return name;
        }

        public static string ImportDeckText(string text)
        {
            return ImportDeck(CategoryDeckImport.Parse(text, CategoryDeckImport.ResolveCard), text, ".txt");
        }

        public static string ImportDeckCards(string name, IEnumerable<string> cards)
        {
            string snapshot = CategoryDeckImport.SerializeCards(name, cards);
            return ImportDeck(CategoryDeckImport.ParseCards(snapshot), snapshot, ".cards");
        }

        private static string ImportDeck(CategoryDeckImport.Deck deck, string text, string extension)
        {
            if (!IsComboCategoryName(deck.Name)) throw new InvalidOperationException("Choose a card-role category for standalone cards.");
            if (s_userFilePath == null) throw new InvalidOperationException("Open the collection before importing categories.");
            EnsureLoaded();
            int added = deck.Cards.Count;
            if (FindCategory(new List<Category>(s_categories), deck.Name, out Category category))
            {
                foreach (string card in deck.Cards) if (s_members[category.Value].Contains(card)) added--;
            }
            else NewDeckCategory(new List<Category>(s_categories), deck.Name); // Validate before writing.
            if (added == 0) return deck.Name + ": no new cards";
            WriteDeckEdit(text, extension);
            LoadUserFile(s_userFilePath);
            return deck.Name + ": +" + added.ToString(CultureInfo.InvariantCulture) + " cards";
        }

        public static string RemoveDeckCards(CuratedCategory value, IEnumerable<string> cards)
        {
            if (value == CuratedCategory.Uncategorized) throw new InvalidOperationException("Assign these cards to a named category to remove them from Uncategorized.");
            if (s_userFilePath == null) throw new InvalidOperationException("Open the collection before editing categories.");
            EnsureLoaded();
            Category category = default;
            foreach (Category candidate in s_categories) if (candidate.Value == value) category = candidate;
            if (category.Name == null) throw new InvalidOperationException("Select an existing category first.");
            string snapshot = CategoryDeckImport.SerializeCards(category.Name, cards);
            CategoryDeckImport.Deck deck = CategoryDeckImport.ParseCards(snapshot);
            deck.Cards.IntersectWith(s_members[value]);
            if (deck.Cards.Count == 0) return category.Name + ": no matching cards";
            WriteDeckEdit(CategoryDeckImport.SerializeCards(category.Name, deck.Cards), ".remove");
            LoadUserFile(s_userFilePath);
            return category.Name + ": -" + deck.Cards.Count.ToString(CultureInfo.InvariantCulture) + " cards";
        }

        public static string RemoveDeckCards(string name, IEnumerable<string> cards)
        {
            EnsureLoaded();
            if (!FindCategory(new List<Category>(s_categories), name, out Category category))
                return CategoryDeckImport.CleanName(name) + ": no matching cards";
            return RemoveDeckCards(category.Value, cards);
        }

        private static bool EditSequence(string file, out long sequence)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            sequence = 0;
            return name.StartsWith("edit-", StringComparison.Ordinal)
                && long.TryParse(name.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence > 0;
        }

        private static int CompareEdits(string a, string b)
        {
            bool aEdit = EditSequence(a, out long aSequence), bEdit = EditSequence(b, out long bSequence);
            // Historical GUID-named exports precede the explicit add/remove history.
            if (aEdit != bEdit) return aEdit ? 1 : -1;
            int order = aSequence.CompareTo(bSequence);
            return order != 0 ? order : StringComparer.Ordinal.Compare(a, b);
        }

        private static void WriteDeckEdit(string text, string extension)
        {
            string directory = DeckDirectory(s_userFilePath);
            Directory.CreateDirectory(directory);
            long last = 0;
            foreach (string existing in Directory.GetFiles(directory, "edit-*"))
                if (EditSequence(existing, out long sequence)) last = Math.Max(last, sequence);
            string file = Path.Combine(directory, "edit-" + checked(last + 1).ToString("D20", CultureInfo.InvariantCulture) + extension);
            string temporary = file + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
                File.Move(temporary, file);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
