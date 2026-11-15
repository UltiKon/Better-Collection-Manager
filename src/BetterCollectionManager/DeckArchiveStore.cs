using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterCollectionManager
{
    // Independent of Unity: the archive is a versioned, lossless local snapshot.
    internal sealed class ArchivedDeck
    {
        internal sealed class Card
        {
            internal string Id, Owner = "";
            internal int Premium, Count;
        }
        internal string Key;
        internal readonly SortedDictionary<string, string> Metadata = new SortedDictionary<string, string>(StringComparer.Ordinal);
        internal readonly List<Card> Cards = new List<Card>();
        internal string Name => Metadata["name"];
        internal const int MaxBytes = 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static string Encode(string text) => Convert.ToBase64String(Utf8.GetBytes(text));
        private static string Decode(string text) => Utf8.GetString(Convert.FromBase64String(text));
        internal string Serialize()
        {
            Validate();
            var text = new StringBuilder("BCM-DECK-ARCHIVE 1\n");
            foreach (var field in Metadata) text.Append("meta\t").Append(field.Key).Append('\t').Append(Encode(field.Value)).Append('\n');
            foreach (Card card in Cards.OrderBy(c => c.Owner, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ThenBy(c => c.Premium))
                text.Append("card\t").Append(Encode(card.Owner)).Append('\t').Append(Encode(card.Id)).Append('\t')
                    .Append(card.Premium.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(card.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (Utf8.GetByteCount(text.ToString()) > MaxBytes) throw new FormatException("Deck archive is too large.");
            return text.ToString();
        }
        internal static ArchivedDeck Parse(string text)
        {
            if (text == null || Utf8.GetByteCount(text) > MaxBytes) throw new FormatException("Deck archive is too large.");
            var deck = new ArchivedDeck();
            using (var reader = new StringReader(text))
            {
                if (reader.ReadLine() != "BCM-DECK-ARCHIVE 1") throw new FormatException("Unsupported deck archive version.");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length == 3 && parts[0] == "meta") deck.Metadata.Add(parts[1], Decode(parts[2]));
                    else if (parts.Length == 5 && parts[0] == "card") deck.Cards.Add(new Card { Owner = Decode(parts[1]), Id = Decode(parts[2]),
                        Premium = int.Parse(parts[3], CultureInfo.InvariantCulture), Count = int.Parse(parts[4], CultureInfo.InvariantCulture) });
                    else throw new FormatException("Invalid deck archive entry.");
                }
            }
            deck.Validate(); return deck;
        }
        private static void Identifier(string value, bool optional = false)
        {
            if (value == null || value.Length > 128 || !optional && value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
                throw new FormatException("Invalid archived card ID.");
        }
        private void Validate()
        {
            if (!Metadata.ContainsKey("name") || string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || Name.Any(char.IsControl)
                || !Metadata.ContainsKey("hero") || !Metadata.ContainsKey("format") || Metadata.Count > 32)
                throw new FormatException("Invalid deck archive header.");
            Identifier(Metadata["hero"]);
            if (!int.TryParse(Metadata["format"], out int format) || format < 1 || format > 4) throw new FormatException("Unsupported archived deck format.");
            foreach (var pair in Metadata)
                if (pair.Key.Length > 64 || pair.Key.Any(c => !char.IsLetterOrDigit(c)) || pair.Value.Length > 1024)
                    throw new FormatException("Invalid archived deck metadata.");
            if (Cards.Count > 1000 || Cards.Sum(card => (long)card.Count) > 10000) throw new FormatException("Too many archived cards.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Card card in Cards)
            {
                Identifier(card.Id); Identifier(card.Owner, true);
                if (card.Premium < 0 || card.Premium > 3 || card.Count <= 0 || card.Count > 1000
                    || !keys.Add(card.Owner + "\t" + card.Id + "\t" + card.Premium)) throw new FormatException("Invalid archived card count or quality.");
            }
        }
    }

    internal sealed class DeckArchiveStore
    {
        private readonly string m_directory;
        internal DeckArchiveStore(string directory) { m_directory = Path.GetFullPath(directory); }
        private string FileFor(string key)
        {
            if (!Guid.TryParseExact(key, "N", out _)) throw new FormatException("Invalid archive key.");
            return Path.Combine(m_directory, key + ".deck");
        }
        internal ArchivedDeck Read(string key)
        {
            string path = FileFor(key);
            if (new FileInfo(path).Length > ArchivedDeck.MaxBytes) throw new FormatException("Deck archive is too large.");
            ArchivedDeck deck = ArchivedDeck.Parse(File.ReadAllText(path, Encoding.UTF8)); deck.Key = key; return deck;
        }
        internal List<ArchivedDeck> ReadAll(Action<string, Exception> invalid)
        {
            var result = new List<ArchivedDeck>();
            if (!Directory.Exists(m_directory)) return result;
            foreach (string file in Directory.GetFiles(m_directory, "*.deck").OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(f => f, StringComparer.Ordinal))
                try { result.Add(Read(Path.GetFileNameWithoutExtension(file))); }
                catch (Exception ex) { invalid(file, ex); }
            return result;
        }
        internal ArchivedDeck Save(ArchivedDeck deck)
        {
            string text = deck.Serialize(), key = Guid.NewGuid().ToString("N");
            WriteNew(FileFor(key), text);
            ArchivedDeck verified = Read(key);
            if (verified.Serialize() != text) throw new IOException("Could not verify the local archive.");
            return verified;
        }
        internal ArchivedDeck Update(string key, ArchivedDeck deck)
        {
            string path = FileFor(key), text = deck.Serialize();
            // Verify the new snapshot before atomically replacing the current
            // archive. Keep the previous version as a recoverable backup.
            string staged = path + ".update";
            try
            {
                WriteNew(staged, text);
                if (ArchivedDeck.Parse(File.ReadAllText(staged, Encoding.UTF8)).Serialize() != text)
                    throw new IOException("Could not verify the edited archive.");
                File.Replace(staged, path, path + ".bak");
                File.Delete(path + ".restore");
                return Read(key);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        private static void WriteNew(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal void Delete(string key)
        {
            File.Delete(FileFor(key));
            File.Delete(FileFor(key) + ".restore");
            File.Delete(FileFor(key) + ".bak");
        }
        // Retain the target across restarts, so retrying an acknowledged create
        // does not silently consume another normal deck slot.
        internal long? RestoreTarget(string key)
        {
            string receipt = FileFor(key) + ".restore";
            return File.Exists(receipt) ? long.Parse(File.ReadAllText(receipt), CultureInfo.InvariantCulture) : (long?)null;
        }
        internal void RememberRestore(string key, long id)
        {
            string receipt = FileFor(key) + ".restore";
            if (File.Exists(receipt)) File.Delete(receipt);
            WriteNew(receipt, id.ToString(CultureInfo.InvariantCulture));
        }
    }
}
