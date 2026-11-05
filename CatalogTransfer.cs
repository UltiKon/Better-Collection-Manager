using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BetterCollectionManager
{
    internal static partial class CategoryCatalog
    {
        internal const int MaxTransferLength = 4 * 1024 * 1024;
        private const string TransferHeader = "BCM-COLLECTION 1";
        private sealed class TransferEdit
        {
            internal string Extension, Text, Destination;
            internal bool Packages;
            internal Combo Combo;
            internal CategoryDeckImport.Deck Deck;
        }

        // Snapshots contain effective membership and active definitions, never edit
        // history, machine-specific paths or transient collection actions.
        internal static string ExportTransfer(bool packages, string folder = null, string entryId = null)
        {
            EnsureLoaded();
            var edits = new List<TransferEdit>();
            if (packages)
            {
                foreach (var group in PackageCatalog.Groups)
                    if (entryId == null && (folder == null || group.Name == folder)) AddFolder(group.Name);
            }
            else
            {
                foreach (Category category in Categories)
                {
                    if (entryId != null || (!IsComboCategory(category.Value) && category.Value != CuratedCategory.Combos) || folder != null && category.Name != folder) continue;
                    AddFolder(category.Name);
                    if (category.Value == CuratedCategory.Combos) continue;
                    var cards = new List<string>(s_members[category.Value]);
                    cards.Sort(StringComparer.Ordinal);
                    for (int offset = 0; offset < cards.Count; offset += 1000)
                        edits.Add(new TransferEdit { Extension = ".cards", Text = CategoryDeckImport.SerializeCards(category.Name,
                            cards.GetRange(offset, Math.Min(1000, cards.Count - offset))) });
                }
            }
            foreach (Combo combo in CollectionEntries)
                if (combo.IsPackage == packages && (entryId == null || combo.Id == entryId) && (folder == null || ComboDestination(combo) == folder))
                    edits.Add(new TransferEdit { Extension = ".combo", Text = TransferComboText(combo, ComboDestination(combo)) });
            string result = TransferText(edits, packages);
            ParseTransfer(result, packages); // Export only a format we can import.
            return result;

            void AddFolder(string name) => edits.Add(new TransferEdit { Extension = ".folder",
                Text = "BCM-CATEGORY-FOLDER 1\n" + (packages ? "Packages" : "Categories") + "\n" + name + "\n" });
        }

        private static string ComboDestination(Combo combo)
        {
            if (combo.IsPackage) return PackageCatalog.GroupName(combo.PackageGroup);
            foreach (Category category in Categories) if (category.Value == combo.Category) return category.Name;
            throw new InvalidOperationException("The combo category no longer exists.");
        }

        private static string TransferComboText(Combo combo, string destination)
        {
            var text = new StringBuilder(combo.IsPackage ? "BCM-CATEGORY-COMBO 8\n" : "BCM-CATEGORY-COMBO 9\n");
            text.Append(Encode(destination)).Append('\n').Append(Encode(combo.Name)).Append('\n')
                .Append(Encode(combo.Description ?? "")).Append('\n').Append(AppearanceLine(combo)).Append('\n');
            if (combo.IsPackage)
                text.Append("Package\n").Append(combo.Artwork).Append('\n').Append(Encode(destination)).Append('\n')
                    .Append(combo.PackageClass).Append('\n');
            var cards = new List<CardRequirement>(combo.Cards);
            cards.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var card in cards)
                text.Append(card.Count.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(card.Id).Append('\n');
            return text.ToString();
        }

        private static string TransferText(List<TransferEdit> edits, bool packages)
        {
            if (edits.Count > 4096) throw new FormatException("Too many entries to share at once.");
            var text = new StringBuilder(TransferHeader + "\n" + (packages ? "Packages" : "Categories") + "\n");
            foreach (var edit in edits)
            {
                text.Append(edit.Extension).Append('\t').Append(Encode(edit.Text)).Append('\n');
                if (text.Length > MaxTransferLength) throw new FormatException("The shared collection is too large.");
            }
            return text.ToString();
        }

        private static List<TransferEdit> ParseTransfer(string text, bool? expectedPackages = null, bool allowEdits = false)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTransferLength)
                throw new FormatException("Copy a BCM export first.");
            var edits = new List<TransferEdit>();
            using (var reader = new StringReader(text.TrimStart('\uFEFF')))
            {
                if (reader.ReadLine() != TransferHeader) throw new FormatException("Copy a BCM export first.");
                string kind = reader.ReadLine();
                if (kind != "Packages" && kind != "Categories") throw new FormatException("Unknown collection export kind.");
                bool packages = kind == "Packages";
                if (expectedPackages.HasValue && packages != expectedPackages.Value)
                    throw new FormatException("Import this export from the " + kind + " menu.");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int separator = line.IndexOf('\t');
                    if (separator < 1 || edits.Count >= 4096) throw new FormatException("Invalid collection export.");
                    var edit = new TransferEdit { Extension = line.Substring(0, separator), Text = Decode(line.Substring(separator + 1)), Packages = packages };
                    if (edit.Text.Length > CategoryDeckImport.MaxTextLength) throw new FormatException("A shared entry is too large.");
                    if (edit.Extension == ".folder")
                    {
                        using (var folder = new StringReader(edit.Text))
                        {
                            if (folder.ReadLine() != "BCM-CATEGORY-FOLDER 1" || folder.ReadLine() != kind)
                                throw new FormatException("Invalid shared folder.");
                            edit.Destination = CategoryDeckImport.CleanName(folder.ReadLine());
                            if (!string.IsNullOrWhiteSpace(folder.ReadToEnd())) throw new FormatException("Invalid shared folder.");
                        }
                        if (packages ? CategoryDeckImport.NameKey(edit.Destination) == "PACKAGES" : !IsComboDestinationName(edit.Destination))
                            throw new FormatException("Invalid shared folder name.");
                    }
                    else if ((edit.Extension == ".cards" || allowEdits && edit.Extension == ".remove") && !packages)
                    {
                        edit.Deck = CategoryDeckImport.ParseCards(edit.Text);
                        edit.Destination = edit.Deck.Name;
                        if (!IsComboCategoryName(edit.Destination)) throw new FormatException("Invalid shared card category.");
                    }
                    else if (edit.Extension == ".combo")
                    {
                        edit.Combo = ParseCombo(edit.Text, "import", out edit.Destination);
                        if (edit.Combo.IsPackage != packages || !packages && !IsComboDestinationName(edit.Destination))
                            throw new FormatException("Invalid shared entry destination.");
                        if (packages && (edit.Combo.SavedPackageGroup == null || edit.Combo.SavedPackageGroup != edit.Destination))
                            throw new FormatException("Invalid shared package folder.");
                    }
                    else if (allowEdits && edit.Extension == ".uncombo")
                    {
                        if (string.IsNullOrWhiteSpace(edit.Text) || edit.Text.Length > 200 || edit.Text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                            throw new FormatException("Invalid shared entry removal.");
                    }
                    else throw new FormatException("Unsupported shared entry.");
                    edits.Add(edit);
                }
            }
            if (edits.Count == 0) throw new FormatException("The collection export is empty.");
            return edits;
        }

        private static void ValidateTransferDestinations(List<TransferEdit> edits, List<Category> categories)
        {
            var groups = new Dictionary<int, string>();
            foreach (var group in PackageCatalog.Groups) groups[(int)group.Kind] = CategoryDeckImport.NameKey(group.Name);
            foreach (var edit in edits)
            {
                if (edit.Extension == ".uncombo") continue;
                bool packages = edit.Packages;
                if (packages)
                {
                    string key = CategoryDeckImport.NameKey(edit.Destination);
                    if (groups.ContainsValue(key)) continue;
                    if (key == "PACKAGES") continue; // Legacy destination maps to Custom.
                    uint hash = 2166136261;
                    foreach (char c in key) unchecked { hash = (hash ^ c) * 16777619; }
                    int value = 1000 + (int)(hash & 0x3fffffff);
                    if (groups.ContainsKey(value)) throw new FormatException("Folder name collision; choose another name.");
                    groups.Add(value, key);
                }
                else if (!FindCategory(categories, edit.Destination, out _))
                    categories.Add(NewDeckCategory(categories, edit.Destination));
            }
        }

        internal static Combo FindTransferredEntry(string text, bool packages)
        {
            Combo incoming = null; string destination = null;
            foreach (var edit in ParseTransfer(text, packages))
            {
                if (edit.Deck != null) return null;
                if (edit.Combo == null) continue;
                if (incoming != null) return null;
                incoming = edit.Combo; destination = edit.Destination;
            }
            if (incoming == null) return null;
            string key = TransferEntryKey(incoming, destination);
            foreach (Combo entry in CollectionEntries)
                if (entry.IsPackage == packages && TransferEntryKey(entry, ComboDestination(entry)) == key) return entry;
            return null;
        }

        internal static bool TransferNeedsChoice(string text, bool packages)
        {
            var edits = ParseTransfer(text, packages);
            var folders = new HashSet<string>(StringComparer.Ordinal);
            var cards = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var entries = new Dictionary<string, Combo>(StringComparer.Ordinal);
            foreach (var edit in edits)
            {
                string destination = CategoryDeckImport.NameKey(edit.Destination);
                if (edit.Extension == ".folder") folders.Add(destination);
                else if (edit.Deck != null)
                {
                    if (!cards.TryGetValue(destination, out var members)) cards[destination] = members = new HashSet<string>(StringComparer.Ordinal);
                    members.UnionWith(edit.Deck.Cards);
                }
                else if (edit.Combo != null)
                {
                    string key = TransferEntryKey(edit.Combo, edit.Destination);
                    if (entries.ContainsKey(key)) throw new FormatException("Duplicate shared entry name in the same folder.");
                    entries.Add(key, edit.Combo);
                }
            }
            if (!packages)
                foreach (Category category in Categories)
                {
                    string key = CategoryDeckImport.NameKey(category.Name);
                    if (!folders.Contains(key)) continue;
                    if (!cards.TryGetValue(key, out var members)) members = new HashSet<string>(StringComparer.Ordinal);
                    if (!s_members[category.Value].IsSubsetOf(members)) return true;
                }
            foreach (Combo local in CollectionEntries)
            {
                if (local.IsPackage != packages) continue;
                string destination = ComboDestination(local), key = TransferEntryKey(local, destination);
                if (!entries.TryGetValue(key, out Combo incoming))
                {
                    if (folders.Contains(CategoryDeckImport.NameKey(destination))) return true;
                    continue;
                }
                if (TransferComboText(MergeTransferCombo(local, incoming), destination) != TransferComboText(incoming, destination)) return true;
            }
            return false;
        }

        private static string TransferEntryKey(Combo combo, string destination)
            => CategoryDeckImport.NameKey(destination) + "\t" + CategoryDeckImport.NameKey(combo.Name);

        private static Combo MergeTransferCombo(Combo local, Combo incoming)
        {
            bool exact = local.ExactVersions || incoming.ExactVersions;
            var cards = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Combo source in new[] { local, incoming })
                foreach (var card in source.Cards)
                {
                    string id = exact ? card.Id : NormalizeCardId(card.Id);
                    cards.TryGetValue(id, out int count);
                    cards[id] = Math.Max(count, card.Count);
                }
            var requirements = new List<CardRequirement>();
            foreach (var card in cards) requirements.Add(new CardRequirement(card.Key, card.Value, Array.Empty<string>()));
            return new Combo { IsPackage = local.IsPackage, Name = local.Name, Description = local.Description,
                Artwork = local.Artwork, PackageClass = local.PackageClass, Appearance = local.Appearance.Copy(),
                ExactVersions = exact, Cards = requirements.AsReadOnly() };
        }

        // Commit the complete import, including overwrite removals, as one file.
        // Folder snapshots replace their contents; single entries replace only
        // entries of the same name in the same folder. Merge retains local styling.
        internal static string ImportTransfer(string text, bool packages, bool overwrite = false)
        {
            if (s_userFilePath == null) throw new InvalidOperationException("Open the collection first.");
            var incoming = ParseTransfer(text, packages);
            ValidateTransferDestinations(incoming, new List<Category>(Categories));
            var selected = new List<TransferEdit>();
            var replacedFolders = new HashSet<string>(StringComparer.Ordinal);
            if (overwrite)
                foreach (var edit in incoming)
                    if (edit.Extension == ".folder") replacedFolders.Add(CategoryDeckImport.NameKey(edit.Destination));
            if (!packages)
                foreach (Category category in Categories)
                    if (replacedFolders.Contains(CategoryDeckImport.NameKey(category.Name)))
                    {
                        var cards = new List<string>(s_members[category.Value]);
                        cards.Sort(StringComparer.Ordinal);
                        for (int offset = 0; offset < cards.Count; offset += 1000)
                            selected.Add(new TransferEdit { Extension = ".remove", Text = CategoryDeckImport.SerializeCards(category.Name,
                                cards.GetRange(offset, Math.Min(1000, cards.Count - offset))) });
                    }
            var knownCombos = new Dictionary<string, List<Combo>>(StringComparer.Ordinal);
            foreach (Combo combo in CollectionEntries)
            {
                if (combo.IsPackage != packages) continue;
                string destination = ComboDestination(combo);
                if (replacedFolders.Contains(CategoryDeckImport.NameKey(destination)))
                    selected.Add(new TransferEdit { Extension = ".uncombo", Text = combo.Id });
                else
                {
                    string key = TransferEntryKey(combo, destination);
                    if (!knownCombos.TryGetValue(key, out var matches)) knownCombos[key] = matches = new List<Combo>();
                    matches.Add(combo);
                }
            }
            var seenIncoming = new HashSet<string>(StringComparer.Ordinal);
            var knownFolders = new HashSet<string>(StringComparer.Ordinal);
            if (packages) foreach (var group in PackageCatalog.Groups) knownFolders.Add(CategoryDeckImport.NameKey(group.Name));
            else foreach (Category category in Categories) knownFolders.Add(CategoryDeckImport.NameKey(category.Name));
            foreach (var edit in incoming)
            {
                if (edit.Combo != null)
                {
                    string key = TransferEntryKey(edit.Combo, edit.Destination);
                    if (!seenIncoming.Add(key)) throw new FormatException("Duplicate shared entry name in the same folder.");
                    if (knownCombos.TryGetValue(key, out var matches))
                    {
                        Combo replacement = edit.Combo;
                        if (!overwrite)
                        {
                            replacement = MergeTransferCombo(matches[0], replacement);
                            foreach (Combo other in matches) replacement = MergeTransferCombo(replacement, other);
                        }
                        string replacementText = TransferComboText(replacement, edit.Destination);
                        if (matches.Count == 1 && replacementText == TransferComboText(matches[0], edit.Destination)) continue;
                        foreach (Combo match in matches) selected.Add(new TransferEdit { Extension = ".uncombo", Text = match.Id });
                        edit.Text = replacementText;
                    }
                }
                else if (edit.Deck != null)
                {
                    if (!replacedFolders.Contains(CategoryDeckImport.NameKey(edit.Destination))
                        && FindCategory(new List<Category>(Categories), edit.Destination, out Category category)
                        && edit.Deck.Cards.IsSubsetOf(s_members[category.Value])) continue;
                }
                else if (!overwrite && !knownFolders.Add(CategoryDeckImport.NameKey(edit.Destination))) continue;
                selected.Add(edit);
            }
            if (selected.Count == 0) return "No new entries";
            string saved = TransferText(selected, packages);
            ParseTransfer(saved, packages, allowEdits: true);
            WriteDeckEdit(saved, ".bundle");
            LoadUserFile(s_userFilePath);
            return overwrite ? "Imported (overwrite)" : "Imported (merge)";
        }

        private static List<KeyValuePair<string, string>> ReadDeckEdits(string file, List<Category> categories)
        {
            var result = new List<KeyValuePair<string, string>>();
            try
            {
                bool bundle = Path.GetExtension(file) == ".bundle";
                if (new FileInfo(file).Length > (bundle ? MaxTransferLength * 4L : CategoryDeckImport.MaxTextLength * 4L))
                    throw new FormatException("Deck export is too large.");
                string text = File.ReadAllText(file, Encoding.UTF8);
                if (!bundle) result.Add(new KeyValuePair<string, string>(file, text));
                else
                {
                    var edits = ParseTransfer(text, allowEdits: true);
                    ValidateTransferDestinations(edits, new List<Category>(categories));
                    for (int i = 0; i < edits.Count; i++)
                        result.Add(new KeyValuePair<string, string>(file + "-" + i.ToString(CultureInfo.InvariantCulture) + edits[i].Extension, edits[i].Text));
                }
            }
            catch (Exception ex) { DebugLog.Warning("Category deck ignored: " + file + ": " + ex.Message); }
            return result;
        }
    }
}
