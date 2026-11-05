using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterCollectionManager
{
    internal static class ComboCardText
    {
        private static List<KeywordTextDbfRecord> s_records;
        private static int s_recordCount;
        private static GAME_TAG s_languageProbe;
        private static string s_probeName;
        private static Regex s_keywords;
        private static readonly Dictionary<string, string> s_formatted = new Dictionary<string, string>();
        private static readonly Regex s_markup = new Regex("<[^>]*>");

        internal static string Format(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            List<KeywordTextDbfRecord> records = GameDbf.KeywordText?.GetRecords();
            if (records == null || records.Count == 0) return text;
            // Read the full keyword table, independently of collection filters.
            // Retry while localization loads; rebuild after a table/language change.
            if (s_keywords == null || records != s_records || records.Count != s_recordCount
                || GameStrings.GetKeywordName(s_languageProbe) != s_probeName)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeywordTextDbfRecord record in records)
                {
                    if (record == null || record.Tag == 0 || !GameStrings.HasKeywordName((GAME_TAG)record.Tag)) continue;
                    string name = GameStrings.GetKeywordName((GAME_TAG)record.Tag)?.Trim();
                    if (string.IsNullOrEmpty(name) || name == "UNKNOWN"
                        || name.IndexOf('<') >= 0 || name.IndexOf('>') >= 0
                        || name.StartsWith("GLOBAL_", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("GAMEPLAY_", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("GLUE_", StringComparison.OrdinalIgnoreCase)
                        || name.IndexOf("KEYWORD_", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (names.Count == 0) { s_languageProbe = (GAME_TAG)record.Tag; s_probeName = GameStrings.GetKeywordName(s_languageProbe); }
                    names.Add(name);
                }
                var ordered = new List<string>(names);
                ordered.Sort((left, right) => right.Length.CompareTo(left.Length));
                for (int i = 0; i < ordered.Count; i++) ordered[i] = Regex.Escape(ordered[i]);
                s_keywords = ordered.Count == 0 ? null : new Regex(@"(?<![\p{L}\p{M}\p{N}_])(?:" + string.Join("|", ordered) + @")(?![\p{L}\p{M}\p{N}_])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                s_records = records; s_recordCount = records.Count; s_formatted.Clear();
            }
            if (s_keywords == null) return text;
            if (s_formatted.TryGetValue(text, out string formatted)) return formatted;

            // Only format visible text. Keep native material tags, attributes,
            // and existing bold spans intact, including on repeated application.
            var result = new StringBuilder();
            int start = 0, bold = 0;
            void AppendText(int end)
            {
                string part = text.Substring(start, end - start);
                result.Append(bold > 0 ? part : s_keywords.Replace(part, match => "<b>" + match.Value + "</b>"));
            }
            foreach (Match markup in s_markup.Matches(text))
            {
                AppendText(markup.Index);
                string tag = markup.Value;
                if (string.Equals(tag, "<b>", StringComparison.OrdinalIgnoreCase)) bold++;
                else if (string.Equals(tag, "</b>", StringComparison.OrdinalIgnoreCase)) bold = Math.Max(0, bold - 1);
                result.Append(tag); start = markup.Index + markup.Length;
            }
            AppendText(text.Length);
            formatted = result.ToString();
            if (s_formatted.Count >= 256) s_formatted.Clear();
            s_formatted[text] = formatted;
            return formatted;
        }
    }
}
