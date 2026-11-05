using System;
using System.Collections.Generic;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        // Keep native objects, input bindings and mesh slices while only the visible set changes.
        private static bool TryRefreshFilterRows()
        {
            if (!CanReuseRowLayout(s_activeKind)) return false;
            Action populate;
            switch (s_activeKind)
            {
                case FilterTrayKind.Keywords:
                    if (!ReferenceEquals(s_keywordEntries, KeywordCatalog.GetEntries())) return false;
                    populate = () => PopulateKeywordRows(s_keywordFormat, false); break;
                case FilterTrayKind.Expansions: populate = PopulateExpansionRows; break;
                case FilterTrayKind.Type:
                case FilterTrayKind.Race:
                case FilterTrayKind.School:
                case FilterTrayKind.Rarity:
                case FilterTrayKind.Categories:
                case FilterTrayKind.Packages:
                    if (s_extraRows == null) return false;
                    populate = PopulateExtraRows; break;
                default: return false;
            }
            var previous = s_items.ToArray();
            int existing = PooledRowCount;
            float scroll = s_scroller.ScrollValue;
            s_items.Clear(); s_unfilteredHeaders.Clear();
            populate();
            bool same = previous.Length == s_items.Count;
            if (same)
                for (int i = 0; i < previous.Length; i++) if (previous[i] != s_items[i]) { same = false; break; }
            RefreshModeChrome();
            if (!same)
            {
                var visible = new HashSet<SetFilterItem>(s_items);
                foreach (SetFilterItem item in previous) if (!visible.Contains(item)) CounterTextLayout.SetRowVisible(item.gameObject, false);
                // Native scrolling moves Contents (and ContentsBone). Arrange at
                // zero before ResetScrollStartPosition captures the new origin;
                // otherwise restoring the fraction applies the old offset twice.
                ApplyMenuScroll(0f);
                LayoutItemsLikeSets();
                ApplyMenuScroll(scroll);
                ApplyVisibleAffectedClip();
                if (existing != PooledRowCount) ForceCameraMaskRefresh("availability-added-rows");
            }
            RememberBuiltRows();
            return true;
        }
        private static int PooledRowCount => s_keywordItems.Count + s_keywordHeaders.Count
            + s_expansionItems.Count + s_extraItems.Count + (s_extraHeader != null ? 1 : 0);
    }
}
