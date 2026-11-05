using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BetterCollectionManager
{
    internal enum CuratedCategory
    {
        AoE = 1, SingleTargetRemoval, SMOrc, Value, WinConditions, Draw, UniqueEffects, Packages, TECHW, Uncategorized, Combos
    }

    /// <summary>Explicit card lists, independent of language, with named multi-card entries.</summary>
    internal static partial class CategoryCatalog
    {
        internal readonly struct Category
        {
            public readonly CuratedCategory Value;
            public readonly string Id, Name;
            public Category(CuratedCategory value, string id, string name) { Value = value; Id = id; Name = name; }
        }

        internal sealed class CardRequirement
        {
            public string Id { get; }
            public int Count { get; }
            public IReadOnlyList<string> Aliases { get; }
            public CardRequirement(string id, int count, string[] aliases)
            { Id = id; Count = count; Aliases = Array.AsReadOnly(aliases); }
        }

        internal sealed class Entry
        {
            public string Id { get; }
            public string Name { get; }
            public PackageKind PackageGroup { get; }
            public string PackageClass { get; }
            public IReadOnlyList<CuratedCategory> Categories { get; }
            // Keep a package/combo as one entry. Membership uses its union;
            // a future deck action can use the complete list and copy counts.
            public IReadOnlyList<CardRequirement> Cards { get; }
            public Entry(string id, string name, CuratedCategory[] categories, CardRequirement[] cards, PackageKind? packageGroup = null, string packageClass = "")
            { Id = id; Name = name; Categories = Array.AsReadOnly(categories); Cards = Array.AsReadOnly(cards); PackageGroup = packageGroup ?? PackageCatalog.LegacyKind(id); PackageClass = packageClass; }
        }

        private static readonly Category[] BuiltInCategories = {
            new Category(CuratedCategory.AoE, "aoe", "AoE"),
            new Category(CuratedCategory.SingleTargetRemoval, "single-target-removal", "Single-Target Removal"),
            new Category(CuratedCategory.SMOrc, "smorc", "SMOrc"),
            new Category(CuratedCategory.Value, "value", "Value"),
            new Category(CuratedCategory.WinConditions, "win-conditions", "Win Conditions"),
            new Category(CuratedCategory.Draw, "draw", "Draw"),
            new Category(CuratedCategory.UniqueEffects, "unique-effects", "Unique Effects"),
            new Category(CuratedCategory.Packages, "packages", "Packages"), // Legacy catalog identifier; not a Categories row.
            new Category(CuratedCategory.Combos, "combos", "Combos"),
            new Category(CuratedCategory.TECHW, "techw", "TECHW"),
            new Category(CuratedCategory.Uncategorized, "uncategorized", "Uncategorized")
        };
        private static Category[] s_categories = BuiltInCategories;
        public static Category[] Categories { get { EnsureLoaded(); return s_categories; } }
        private const string ResourceName = "BetterCollectionManager.Categories.json";
        private static IReadOnlyList<Entry> s_entries;
        private static Dictionary<CuratedCategory, HashSet<string>> s_members;
        private static HashSet<string> s_categorized = new HashSet<string>(StringComparer.Ordinal);
        public static int Generation { get; private set; }
        public static IReadOnlyList<Entry> Entries { get { EnsureLoaded(); return s_entries; } }

        private static string BundledJson()
        {
            using (Stream stream = typeof(CategoryCatalog).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) throw new InvalidOperationException("Missing bundled category catalog");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
        private static void EnsureLoaded()
        {
            if (s_entries == null) Apply(LoadDefaults(out _));
        }
        private static IReadOnlyList<Entry> s_defaults;
        private static string s_bundledJson;
        private static IReadOnlyList<Entry> LoadDefaults(out string json)
        {
            json = s_bundledJson;
            if (s_defaults != null) return s_defaults;
            json = null;
            try
            {
                string bundled = BundledJson();
                IReadOnlyList<Entry> entries = Parse(bundled);
                json = s_bundledJson = bundled;
                return s_defaults = entries;
            }
            catch (Exception ex)
            {
                DebugLog.Warning("Categories: bundled selection unavailable; keeping other filters usable. " + ex.Message);
                return Array.Empty<Entry>();
            }
        }

        // Reload once on collection entry. Never overwrite an existing user's file,
        // including a malformed one; keep the bundled selection usable on failure.
        public static bool LoadUserFile(string path)
        {
            bool loaded = LoadBaseUserFile(path);
            LoadDeckFiles(path);
            var available = new List<CuratedCategory>();
            foreach (Category category in s_categories) available.Add(category.Value);
            ExtraFilterState.PruneCategories(available);
            return loaded;
        }

        private static bool LoadBaseUserFile(string path)
        {
            IReadOnlyList<Entry> defaults = LoadDefaults(out string bundled);
            try
            {
                if (File.Exists(path))
                {
                    string userJson = File.ReadAllText(path, Encoding.UTF8);
                    IReadOnlyList<Entry> userEntries = Parse(userJson);
                    if (bundled != null && IsOriginalStarter(userEntries))
                    {
                        // Only the exact original selection is upgraded. Keep a
                        // backup and never replace an edited/custom catalog.
                        string backup = path + ".starter-v1.bak";
                        if (!File.Exists(backup))
                            using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write))
                            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(userJson);
                        File.WriteAllText(path, bundled, new UTF8Encoding(false));
                        Apply(defaults);
                        DebugLog.Msg("Categories: upgraded original starter selection; backup=" + backup);
                    }
                    else Apply(userEntries);
                }
                else
                {
                    Apply(defaults);
                    if (bundled == null) return false;
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(bundled);
                }
                return true;
            }
            catch (Exception ex)
            {
                Apply(defaults);
                DebugLog.Warning("Categories: could not load " + path + "; using bundled selection. " + ex.Message);
                return false;
            }
        }

        public static bool Matches(CollectibleCard card, CuratedCategory category)
        {
            if (card == null || string.IsNullOrEmpty(card.CardId)) return false;
            EnsureLoaded();
            string id = NormalizeCardId(card.CardId);
            if (category == CuratedCategory.Combos || category == CuratedCategory.Packages) return false;
            return category == CuratedCategory.Uncategorized ? !s_categorized.Contains(id)
                : s_members.TryGetValue(category, out HashSet<string> ids) && ids.Contains(id);
        }

        internal static string NormalizeCardId(string id)
        {
            // Core and Classic versions retain the original ID after the prefix.
            while (id.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("VAN_", StringComparison.Ordinal)) id = id.Substring(id.IndexOf('_') + 1);
            return id;
        }

        private static bool IsOriginalStarter(IReadOnlyList<Entry> entries)
        {
            if (entries.Count != 110) return false;
            // Fingerprint the parsed selection, so harmless JSON whitespace or
            // line-ending changes do not prevent upgrading the shipped v1 list.
            ulong hash = 14695981039346656037UL;
            var signature = new StringBuilder();
            foreach (Entry entry in entries)
            {
                signature.Length = 0;
                signature.Append(entry.Id).Append('\u001f').Append(entry.Name).Append('\u001f');
                for (int i = 0; i < entry.Categories.Count; i++)
                {
                    if (i > 0) signature.Append(',');
                    signature.Append(((int)entry.Categories[i]).ToString(CultureInfo.InvariantCulture));
                }
                signature.Append('\u001f');
                for (int i = 0; i < entry.Cards.Count; i++)
                {
                    if (i > 0) signature.Append(';');
                    CardRequirement card = entry.Cards[i];
                    signature.Append(card.Id).Append('@').Append(card.Count.ToString(CultureInfo.InvariantCulture)).Append('[');
                    for (int a = 0; a < card.Aliases.Count; a++)
                    { if (a > 0) signature.Append(','); signature.Append(card.Aliases[a]); }
                    signature.Append(']');
                }
                signature.Append('\u001e');
                foreach (char c in signature.ToString()) unchecked { hash = (hash ^ c) * 1099511628211UL; }
            }
            return hash == 0x49d4c73c61edb4c9UL;
        }

        private static void Apply(IReadOnlyList<Entry> entries, Category[] categories = null,
            Dictionary<CuratedCategory, HashSet<string>> effectiveMembers = null)
        {
            categories = categories ?? BuiltInCategories;
            var members = effectiveMembers ?? new Dictionary<CuratedCategory, HashSet<string>>();
            if (effectiveMembers == null)
            {
                foreach (Category category in categories) members[category.Value] = new HashSet<string>(StringComparer.Ordinal);
                foreach (Entry entry in entries)
                    foreach (CuratedCategory category in entry.Categories)
                        foreach (CardRequirement card in entry.Cards)
                        {
                            members[category].Add(NormalizeCardId(card.Id));
                            foreach (string alias in card.Aliases) members[category].Add(NormalizeCardId(alias));
                        }
            }
            // Entries preserve source definitions/packages; explicit user edits
            // determine effective membership without rewriting those definitions.
            var visible = new List<Category>();
            foreach (Category category in categories) if (category.Value != CuratedCategory.Packages) visible.Add(category);
            s_categories = visible.ToArray(); s_entries = entries; s_members = effectiveMembers ?? members;
            s_categorized = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in s_members) if (pair.Key != CuratedCategory.Uncategorized && pair.Key != CuratedCategory.Packages && pair.Key != CuratedCategory.Combos) s_categorized.UnionWith(pair.Value);
            unchecked { Generation++; }
            FilterModes.InvalidateResults();
        }

        internal static IReadOnlyList<Entry> Parse(string json)
        {
            var document = ObjectValue(JsonReader.Parse(json), "catalog");
            if (IntegerValue(Member(document, "version"), "version") != 1)
                throw new FormatException("Expected category catalog version 1");
            var entries = new List<Entry>();
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (object value in ArrayValue(Member(document, "entries"), "entries"))
            {
                var source = ObjectValue(value, "entry");
                string entryId = StringValue(Member(source, "id"), "entry id");
                string name = StringValue(Member(source, "name"), "entry name");
                string[] categoryIds = StringArray(Member(source, "categories"), "categories");
                var cardData = ArrayValue(Member(source, "cards"), "cards");
                if (!entryIds.Add(entryId) || categoryIds.Length == 0 || cardData.Count == 0)
                    throw new FormatException("Every category entry needs a unique id, name, categories and cards");
                var categories = new List<CuratedCategory>();
                foreach (string id in categoryIds)
                {
                    if (id == "uncategorized") throw new FormatException("Uncategorized is calculated automatically and cannot be assigned.");
                    bool found = false;
                    foreach (Category category in BuiltInCategories)
                        if (category.Id == id) { found = true; if (!categories.Contains(category.Value)) categories.Add(category.Value); break; }
                    if (!found) throw new FormatException("Unknown category: " + id);
                }
                var cards = new List<CardRequirement>();
                var cardIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (object cardValue in cardData)
                {
                    var card = ObjectValue(cardValue, "card");
                    string id = StringValue(Member(card, "id"), "card id");
                    int count = IntegerValue(Member(card, "count"), "card count");
                    if (!cardIds.Add(NormalizeCardId(id)) || count < 1 || count > 2)
                        throw new FormatException("Duplicate card or invalid count (expected 1 or 2) in " + entryId);
                    string[] aliases = card.TryGetValue("aliases", out object aliasValue) && aliasValue != null
                        ? StringArray(aliasValue, "aliases") : Array.Empty<string>();
                    cards.Add(new CardRequirement(id, count, aliases));
                }
                PackageKind? group = null;
                if (source.TryGetValue("packageKind", out object groupValue))
                {
                    if (!Enum.TryParse(StringValue(groupValue, "package kind"), out PackageKind parsed) || !Enum.IsDefined(typeof(PackageKind), parsed))
                        throw new FormatException("Unknown package kind");
                    group = parsed;
                }
                entries.Add(new Entry(entryId, name, categories.ToArray(), cards.ToArray(), group, source.TryGetValue("packageClass", out object packageClass) ? StringValue(packageClass, "package class") : ""));
            }
            return entries.AsReadOnly();
        }

        private static object Member(Dictionary<string, object> value, string name)
        {
            if (!value.TryGetValue(name, out object member)) throw new FormatException("Missing category field: " + name);
            return member;
        }
        private static Dictionary<string, object> ObjectValue(object value, string name)
            => value as Dictionary<string, object> ?? throw new FormatException("Expected object for " + name);
        private static List<object> ArrayValue(object value, string name)
            => value as List<object> ?? throw new FormatException("Expected array for " + name);
        private static string StringValue(object value, string name)
        {
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Expected nonempty string for " + name);
            return text;
        }
        private static string[] StringArray(object value, string name)
        {
            var list = ArrayValue(value, name);
            var result = new string[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = StringValue(list[i], name);
            return result;
        }
        private static int IntegerValue(object value, string name)
        {
            if (value is JsonReader.Number number && int.TryParse(number.Text, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out int result)) return result;
            throw new FormatException("Expected integer for " + name);
        }

        // Hearthstone's stripped Mono profile has no DataContractJsonSerializer.
        // Keep this reader on mscorlib types, including when parsing ignored metadata.
        private sealed class JsonReader
        {
            internal readonly struct Number
            {
                public readonly string Text;
                public Number(string text) { Text = text; }
            }
            private readonly string m_text;
            private int m_position;
            private JsonReader(string text) { m_text = text ?? throw new FormatException("Missing JSON text"); }
            public static object Parse(string text)
            {
                var reader = new JsonReader(text);
                object value = reader.ReadValue(0);
                reader.White();
                if (reader.m_position != reader.m_text.Length) throw reader.Error("Trailing JSON content");
                return value;
            }
            private FormatException Error(string message) => new FormatException(message + " at JSON position " + m_position);
            private void White()
            {
                while (m_position < m_text.Length)
                {
                    char c = m_text[m_position];
                    if (c != ' ' && c != '\t' && c != '\r' && c != '\n') break;
                    m_position++;
                }
            }
            private bool Eat(char c)
            {
                White();
                if (m_position >= m_text.Length || m_text[m_position] != c) return false;
                m_position++; return true;
            }
            private void Expect(char c) { if (!Eat(c)) throw Error("Expected '" + c + "'"); }
            private object ReadValue(int depth)
            {
                if (depth > 64) throw Error("JSON nesting is too deep");
                White();
                if (m_position >= m_text.Length) throw Error("Unexpected end of JSON");
                char c = m_text[m_position];
                if (c == '{') return ReadObject(depth + 1);
                if (c == '[') return ReadArray(depth + 1);
                if (c == '"') return ReadString();
                if (c == 't') return Literal("true", true);
                if (c == 'f') return Literal("false", false);
                if (c == 'n') return Literal("null", null);
                if (c == '-' || Digit(c)) return ReadNumber();
                throw Error("Invalid JSON value");
            }
            private object Literal(string word, object value)
            {
                if (m_text.Length - m_position < word.Length
                    || string.CompareOrdinal(m_text, m_position, word, 0, word.Length) != 0) throw Error("Invalid JSON literal");
                m_position += word.Length; return value;
            }
            private Dictionary<string, object> ReadObject(int depth)
            {
                Expect('{');
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Eat('}')) return values;
                do
                {
                    string key = ReadString();
                    Expect(':');
                    if (values.ContainsKey(key)) throw Error("Duplicate JSON member '" + key + "'");
                    values.Add(key, ReadValue(depth));
                    if (Eat('}')) return values;
                    Expect(',');
                } while (true);
            }
            private List<object> ReadArray(int depth)
            {
                Expect('[');
                var values = new List<object>();
                if (Eat(']')) return values;
                do
                {
                    values.Add(ReadValue(depth));
                    if (Eat(']')) return values;
                    Expect(',');
                } while (true);
            }
            private string ReadString()
            {
                Expect('"');
                var text = new StringBuilder();
                while (m_position < m_text.Length)
                {
                    char c = m_text[m_position++];
                    if (c == '"') return text.ToString();
                    if (c < ' ') throw Error("Unescaped control character");
                    if (c != '\\') { text.Append(c); continue; }
                    if (m_position >= m_text.Length) throw Error("Incomplete JSON escape");
                    c = m_text[m_position++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': text.Append(c); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            int code = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                if (m_position >= m_text.Length) throw Error("Incomplete Unicode escape");
                                char hex = m_text[m_position++];
                                int digit = hex >= '0' && hex <= '9' ? hex - '0'
                                    : hex >= 'a' && hex <= 'f' ? hex - 'a' + 10
                                    : hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
                                if (digit < 0) throw Error("Invalid Unicode escape");
                                code = code * 16 + digit;
                            }
                            text.Append((char)code); break;
                        default: throw Error("Invalid JSON escape");
                    }
                }
                throw Error("Unterminated JSON string");
            }
            private static bool Digit(char c) => c >= '0' && c <= '9';
            private void Digits()
            {
                int start = m_position;
                while (m_position < m_text.Length && Digit(m_text[m_position])) m_position++;
                if (start == m_position) throw Error("Expected a digit");
            }
            private Number ReadNumber()
            {
                int start = m_position;
                if (m_text[m_position] == '-') m_position++;
                if (m_position < m_text.Length && m_text[m_position] == '0') m_position++;
                else Digits();
                if (m_position < m_text.Length && m_text[m_position] == '.') { m_position++; Digits(); }
                if (m_position < m_text.Length && (m_text[m_position] == 'e' || m_text[m_position] == 'E'))
                {
                    m_position++;
                    if (m_position < m_text.Length && (m_text[m_position] == '+' || m_text[m_position] == '-')) m_position++;
                    Digits();
                }
                return new Number(m_text.Substring(start, m_position - start));
            }
        }
    }
}
