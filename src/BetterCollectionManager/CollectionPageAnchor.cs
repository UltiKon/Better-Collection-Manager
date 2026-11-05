using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace BetterCollectionManager
{
    // Snapshot the old order before filtering mutates the class buckets.
    internal sealed class CollectionPageAnchor
    {
        internal static Func<CollectibleCard, string> CardKey = card => card.CardId;
        private sealed class Entry
        {
            public string CardId, Tab;
            public int Page;
            public int Premium;
        }

        private readonly List<Entry> m_old;
        private readonly int m_start;
        private readonly string m_tab;

        private CollectionPageAnchor(List<Entry> old, int start, string tab)
        { m_old = old; m_start = start; m_tab = tab; }

        internal static CollectionPageAnchor ForCardKey(string key)
            => new CollectionPageAnchor(new List<Entry> { new Entry { CardId = key, Page = 1 } }, 0, null);

        internal static CollectionPageAnchor Capture(object collection, int currentPage)
        {
            if (collection == null) return null;
            List<Entry> entries = Read(collection);
            int start = entries.FindIndex(e => e.Page == currentPage);
            if (entries.Count == 0) return new CollectionPageAnchor(entries, 0, null);
            return start < 0 ? null : new CollectionPageAnchor(entries, start, entries[start].Tab);
        }

        internal bool MatchesVisiblePage(object collection, int page)
        {
            var current = new List<Entry>();
            if (collection != null)
            {
                Type type = collection.GetType();
                var total = AccessTools.Method(type, "GetTotalNumPages");
                if (total != null && page >= 1 && page <= (int)total.Invoke(collection, null))
                    ReadPage(collection, page, AccessTools.Method(type, "GetPageContents"),
                        AccessTools.Method(type, "GetCurrentTabInfoFromPage"), current);
            }
            int end = m_start;
            while (end < m_old.Count && m_old[end].Page == m_old[m_start].Page) end++;
            if (current.Count != end - m_start) return false;
            for (int i = 0; i < current.Count; i++)
            {
                Entry old = m_old[m_start + i], next = current[i];
                if (old.CardId != next.CardId || old.Premium != next.Premium || old.Tab != next.Tab) return false;
            }
            return true;
        }

        internal int Resolve(object collection)
        {
            List<Entry> entries = Read(collection);
            if (entries.Count == 0) return 1;
            if (m_old.Count == 0) return entries[0].Page;
            // A card can appear in several class tabs. Keep the original tab
            // whenever it still contains results, even if every old card vanished.
            bool sameTab = entries.Exists(e => e.Tab == m_tab);
            var pages = new Dictionary<string, int>();
            int fallback = entries[0].Page;
            foreach (Entry entry in entries)
            {
                if (sameTab && entry.Tab != m_tab) continue;
                if (pages.Count == 0) fallback = entry.Page;
                if (!pages.ContainsKey(entry.CardId)) pages.Add(entry.CardId, entry.Page);
            }
            // Prefer a still-visible card, then the closest successor/predecessor
            // in the old collection order. Premium changes keep the same anchor.
            int visibleEnd = m_start;
            while (visibleEnd < m_old.Count && m_old[visibleEnd].Page == m_old[m_start].Page)
            {
                if (TryPage(m_old[visibleEnd], pages, sameTab, out int page)) return page;
                visibleEnd++;
            }
            for (int distance = 1; distance < m_old.Count; distance++)
            {
                int right = visibleEnd + distance - 1, left = m_start - distance;
                if (right < m_old.Count && TryPage(m_old[right], pages, sameTab, out int page)) return page;
                if (left >= 0 && TryPage(m_old[left], pages, sameTab, out page)) return page;
            }
            return fallback;
        }

        private bool TryPage(Entry entry, Dictionary<string, int> pages, bool sameTab, out int page)
        {
            page = 0;
            return (!sameTab || entry.Tab == m_tab) && pages.TryGetValue(entry.CardId, out page);
        }

        private static List<Entry> Read(object collection)
        {
            var entries = new List<Entry>();
            if (collection == null) return entries;
            Type type = collection.GetType();
            var total = AccessTools.Method(type, "GetTotalNumPages");
            var contents = AccessTools.Method(type, "GetPageContents");
            var tab = AccessTools.Method(type, "GetCurrentTabInfoFromPage");
            if (total == null || contents == null || tab == null) return entries;
            int count = (int)total.Invoke(collection, null);
            for (int page = 1; page <= count; page++)
                ReadPage(collection, page, contents, tab, entries);
            return entries;
        }

        private static void ReadPage(object collection, int page, System.Reflection.MethodInfo contents,
            System.Reflection.MethodInfo tab, List<Entry> entries)
        {
            if (contents == null || tab == null) return;
            object context = tab.Invoke(collection, new object[] { page });
            string key = context == null ? "" : AccessTools.Field(context.GetType(), "tagClass")?.GetValue(context)?.ToString();
            if (!(contents.Invoke(collection, new object[] { page }) is IEnumerable cards)) return;
            foreach (object value in cards)
                if (value is CollectibleCard card && !string.IsNullOrEmpty(card.CardId))
                    entries.Add(new Entry { CardId = CardKey(card), Tab = key, Page = page, Premium = (int)card.PremiumType });
        }
    }
}
