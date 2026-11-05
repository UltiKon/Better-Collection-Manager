using System;
using System.Collections.Generic;
using PegasusShared;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static readonly Dictionary<KeywordGroup, SetFilterItem> s_keywordHeaders = new Dictionary<KeywordGroup, SetFilterItem>();
        private static IReadOnlyList<KeywordCatalog.Entry> s_keywordEntries;
        private static FormatType s_keywordFormat;
        private static void ResetKeywordRowCache() { s_keywordHeaders.Clear(); s_keywordEntries = null; }

        private static void AddFilterMenuItem(
            string label, FilterTrayKind kind, FormatType format, bool active)
        {
            SetFilterItem item = AddItem(active ? label + " *" : label, format,
                () => CollectionFilterMenu.ShowFilter(kind));
            SafeSetSelected(item, active);
        }

        private static void OnKeywordClicked(GAME_TAG tag)
        {
            NotifyItemClicked();
            KeywordFilterState.Toggle(tag);
            bool next = KeywordFilterState.IsSelected(tag);
            DebugLog.Msg($"Keyword tray {tag}={next}; {KeywordFilterState.Describe()}");
            KeywordFilterApplier.RefreshCollectionPages();
            KeywordFilterUi.NotifyFilterChangedFromChrome();
            RefreshAfterSelection();
        }

        private static void RefreshSelectionGlows()
        {
            foreach (var kv in s_keywordItems)
                if (kv.Value != null)
                    SetFilterSelected(kv.Value, KeywordFilterState.IsSelected(kv.Key), KeywordFilterState.IsCurrent(kv.Key), KeywordFilterState.IsExcluded(kv.Key));
        }

        private static string KeywordGroupLabel(KeywordGroup group)
        {
            switch (group)
            {
                case KeywordGroup.Evergreen: return "Evergreen";
                case KeywordGroup.Factions: return "Factions";
                default: return "Expansion-Keywords";
            }
        }

        private static void PopulateKeywordRows(FormatType format, bool refreshSelection = true)
        {
            IReadOnlyList<KeywordCatalog.Entry> entries = KeywordCatalog.GetEntries();
            var available = new List<KeywordCatalog.Entry>();
            foreach (var entry in entries) if (entry.Group != KeywordGroup.Factions) available.Add(entry);
            KeywordFilterState.PruneToAvailable(available);
            s_keywordEntries = entries; s_keywordFormat = format;
            foreach (KeywordGroup group in new[] { KeywordGroup.Evergreen,
                KeywordGroup.Expansion })
            {
                var sorted = new List<KeywordCatalog.Entry>();
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].Group == group && (entries[i].HasCards || entries[i].HasReferencedCards))
                        sorted.Add(entries[i]);
                sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                if (sorted.Count == 0) continue;

                if (!sorted.Exists(entry => FilterAvailability.Keyword(entry.Tag)))
                {
                    s_unfilteredHeaders.Add(true);
                    foreach (var entry in sorted) s_unfilteredHeaders.Add(false);
                    continue;
                }
                if (s_keywordHeaders.TryGetValue(group, out SetFilterItem heading) && heading != null)
                { s_unfilteredHeaders.Add(true); s_items.Add(heading); }
                else s_keywordHeaders[group] = AddHeader(KeywordGroupLabel(group), format);
                foreach (KeywordCatalog.Entry entry in sorted)
                {
                    GAME_TAG tag = entry.Tag;
                    if (!FilterAvailability.Keyword(tag)) { s_unfilteredHeaders.Add(false); continue; }
                    if (s_keywordItems.TryGetValue(tag, out SetFilterItem item) && item != null)
                    { s_unfilteredHeaders.Add(false); s_items.Add(item); }
                    else item = AddItem(entry.Name, format, () => OnKeywordClicked(tag));
                    if (refreshSelection) SetFilterSelected(item, KeywordFilterState.IsSelected(tag), KeywordFilterState.IsCurrent(tag), KeywordFilterState.IsExcluded(tag));
                    s_keywordItems[tag] = item;
                }
            }
        }
    }
}
