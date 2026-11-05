using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static readonly List<SetFilterItem> s_expansionRows = new List<SetFilterItem>();
        private static readonly Dictionary<SetFilterItem, SetFilterItem> s_expansionItems = new Dictionary<SetFilterItem, SetFilterItem>();

        private static bool ExpansionRowSelected(SetFilterItem source, SetFilterItem selected)
            => PackageViewState.IsActive && PackageBrowser.IsFormatChoice(source, out var format)
                ? PackageViewState.Format == format
                : ExpansionFilterState.IsActive ? ExpansionFilterState.IsSelected(source) : source == selected;

        private static void ClearExpansionItems()
        {
            s_expansionItems.Clear();
        }

        public static void CaptureExpansionRows(SetFilterTray live)
        {
            s_expansionRows.Clear();
            // Read the initialized native list without opening its tray.
            // Keep original row objects as callback targets and data sources.
            IEnumerable native = AccessTools.Field(typeof(SetFilterTray), "m_items")?.GetValue(live) as IEnumerable;
            if (native != null)
                foreach (object row in native)
                    if (row is SetFilterItem item && item != null && item.gameObject.name != BackItemName)
                        s_expansionRows.Add(item);
            if (s_expansionRows.Count == 0 && live.m_contents != null)
            {
                foreach (SetFilterItem row in live.m_contents.GetComponentsInChildren<SetFilterItem>(true))
                    if (row != null && row.gameObject.name != BackItemName) s_expansionRows.Add(row);
                s_expansionRows.Sort((a, b) => b.transform.position.z.CompareTo(a.transform.position.z));
            }
            if (s_expansionRows.Count == 0) throw new InvalidOperationException("Native expansion rows are unavailable");
        }

        public static void RefreshExpansionSelection()
        {
            if (!IsShown || ActiveKind != FilterTrayKind.Expansions) return;
            SetFilterItem selected = AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
            if (PackageViewState.IsActive) selected = null;
            foreach (var pair in s_expansionItems)
                if (!pair.Key.IsHeader) SetFilterSelected(pair.Value, ExpansionRowSelected(pair.Key, selected),
                    !ExpansionFilterState.IsActive || ExpansionFilterState.IsCurrent(pair.Key),
                    ExpansionFilterState.IsActive && ExpansionFilterState.IsExcluded(pair.Key));
        }

        private static void PopulateExpansionRows()
        {
            SetFilterItem selected = AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
            if (PackageViewState.IsActive) selected = null;
            foreach (SetFilterItem source in s_expansionRows)
            {
                if (source == null) continue;
                // Live rows can be selected, pressed or mid-animation, with
                // scaled transforms and native input/animation components.
                // Render our ordinary template and keep only the original
                // row as the native callback's data/identity source.
                if (source.IsHeader)
                {
                    if (s_expansionItems.TryGetValue(source, out SetFilterItem header) && header != null)
                    { s_unfilteredHeaders.Add(true); s_items.Add(header); }
                    else s_expansionItems[source] = AddHeader(source.Text, source.FormatType);
                    continue;
                }
                bool selectedRow = ExpansionRowSelected(source, selected);
                s_unfilteredHeaders.Add(false);
                if (!PackageViewState.IsActive && !FilterAvailability.Expansion(source, selectedRow)) continue;
                if (!s_expansionItems.TryGetValue(source, out SetFilterItem item) || item == null)
                    s_expansionItems[source] = item = CreateNavigationItem(s_contents, source.Text, source.FormatType, () => {
                        NotifyItemClicked();
                        CollectionFilterMenu.SelectExpansion(source);
                        KeywordFilterUi.NotifyFilterChangedFromChrome();
                        RefreshAfterSelection();
                    });
                SetFilterSelected(item, selectedRow,
                    !ExpansionFilterState.IsActive || ExpansionFilterState.IsCurrent(source),
                    ExpansionFilterState.IsActive && ExpansionFilterState.IsExcluded(source));
                s_expansionItems[source] = item;
                s_items.Add(item);
            }
        }
    }
}
