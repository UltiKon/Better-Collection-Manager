using System;
using System.Collections.Generic;
using PegasusShared;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static SetFilterItem s_modeItem;
        private static SetFilterItem s_notItem;
        private static readonly List<Action> s_extraSelectionRefresh = new List<Action>();
        private static readonly List<bool> s_unfilteredHeaders = new List<bool>();
        private static readonly Dictionary<SetFilterItem, Action> s_pinnedActions = new Dictionary<SetFilterItem, Action>();
        private static Action s_pendingPinnedAction;
        private static readonly Dictionary<Renderer, MaterialPropertyBlock> s_originalGlow = new Dictionary<Renderer, MaterialPropertyBlock>();
        private sealed class ExcludedCaption { internal string Text; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SetFilterItem, ExcludedCaption> s_excludedCaptions
            = new System.Runtime.CompilerServices.ConditionalWeakTable<SetFilterItem, ExcludedCaption>();
        private static bool IsFullWidthRow(SetFilterItem item) => item != null && item.IsHeader;
        private static bool s_pinAllControls = true;
        private static bool ShowRootClearAll => FilterModes.HasNonSingleModes || (CollectionQualitySettings.ShowLogicControls && CollectionQualitySettings.ShowFilterClearAll
            && (KeywordFilterState.IsActive || ExtraFilterState.SelectedCount(FilterTrayKind.Type) > 0
                || ExtraFilterState.SelectedCount(FilterTrayKind.Race) > 0 || ExtraFilterState.SelectedCount(FilterTrayKind.School) > 0
                || ExtraFilterState.SelectedCount(FilterTrayKind.Rarity) > 0
                || ExtraFilterState.SelectedCount(FilterTrayKind.Categories) > 0 || ExtraFilterState.IsActiveForKind(FilterTrayKind.Packages) || CollectionFilterMenu.HasExpansionFilter));
        private static int PinnedHeaderRows(int columns) => s_activeKind == FilterTrayKind.Menu
            ? (s_pinAllControls && ShowRootClearAll ? 1 : 0)
            : PinnedRows(PinnedControls(s_pinAllControls, false), columns);

        private static int ActiveSelectionCount => s_activeKind == FilterTrayKind.Keywords ? KeywordFilterState.SelectedCount
            : s_activeKind == FilterTrayKind.Expansions ? ExpansionFilterState.SelectedCount : ExtraFilterState.SelectedCount(s_activeKind);

        private static string ModeLabel()
        {
            FilterMode mode = FilterModes.Operation(s_activeKind, ActiveSelectionCount);
            return (FilterModes.IsExpression ? "Next: " : "Mode: ") + FilterModes.Label(FilterModes.WithoutNot(mode));
        }
        private static bool NotEnabled => FilterModes.IsNegative(FilterModes.Operation(s_activeKind, ActiveSelectionCount));
        private static string NotLabel() => NotEnabled ? "NOT: On" : "NOT: Off";

        private static void ChangeFilterMode()
        {
            NotifyItemClicked();
            ApplyFilterMode(FilterModes.Next(s_activeKind, ActiveSelectionCount));
        }

        private static void ToggleFilterNot()
        {
            NotifyItemClicked();
            ApplyFilterMode(FilterModes.ToggleNot(s_activeKind, ActiveSelectionCount));
        }

        private static void ApplyFilterMode(FilterMode next)
        {
            if (next == FilterModes.Get(s_activeKind)) return;
            if (s_activeKind == FilterTrayKind.Expansions) CollectionFilterMenu.ChangeExpansionMode(next);
            else
            {
                FilterModes.Set(s_activeKind, next);
                if (!FilterModes.IsExpression) KeywordFilterApplier.RefreshCollectionPages();
            }
            KeywordFilterUi.NotifyFilterChangedFromChrome();
            RefreshAfterSelection();
        }

        internal static void RefreshAfterSelection()
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (s_rowsControlShape != ControlShape) { RebuildRows(); return; }
                if (CollectionQualitySettings.HideEmptyFilters)
                {
                    if (!TryRefreshFilterRows()) RebuildRows();
                    return;
                }
                RefreshModeChrome();
                RememberBuiltRows();
            }
            finally
            {
                double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (elapsed >= 25.0) DebugLog.Msg($"Filter menu update timing: kind={s_activeKind} elapsed={elapsed:F1}ms");
            }
        }

        private static void RefreshModeChrome()
        {
            void Label(SetFilterItem item, bool not = false)
            {
                if (item == null) return;
                item.Text = not ? NotLabel() : ModeLabel();
                if (not) SafeSetSelected(item, NotEnabled);
                item.m_uberText?.UpdateNow(true);
                if (s_wideRows.TryGetValue(item, out FilterTrayGeometry geometry) && geometry.FrameRenderer != null)
                    CenterRowText(item, geometry.FrameRenderer.bounds);
            }
            Label(s_modeItem);
            Label(s_notItem, true);
            foreach (SetFilterItem item in s_pinnedActions.Keys)
            {
                if (item != null && item.gameObject.name == "BCM_PinnedFilterMode") Label(item);
                if (item != null && item.gameObject.name == "BCM_PinnedFilterNot") Label(item, true);
            }
            RefreshSelectionGlows();
            foreach (Action refresh in s_extraSelectionRefresh) refresh();
            if (s_activeKind == FilterTrayKind.Expansions) RefreshExpansionSelection();
        }

        // Kept as the same factory for small menu fixtures; production pins this control outside Contents.
        private static void AddModeRow(FormatType format)
        {
            if (s_activeKind == FilterTrayKind.Menu || CollectionQualitySettings.FilterModeBehavior == FilterModeBehavior.Hidden
                || PackageViewState.IsActive && s_activeKind != FilterTrayKind.Expansions) return;
            s_modeItem = AddItem(ModeLabel(), format, ChangeFilterMode);
            s_modeItem.gameObject.name = "BCM_FilterMode";
            s_notItem = AddItem(NotLabel(), format, ToggleFilterNot);
            s_notItem.gameObject.name = "BCM_FilterNot";
            SafeSetSelected(s_notItem, NotEnabled);
        }

        private static void RequestPinnedControl(SetFilterItem item)
        {
            if (item == null || !s_pinnedActions.TryGetValue(item, out Action action)) { RequestPinnedBackNavigation(); return; }
            if (!CanReleasePinnedControl(item) || s_backNavigationPending) return;
            if (Input.GetMouseButtonUp(0) && FindPinnedBackUnderPointer() != item) return;
            NotifyItemClicked();
            s_pendingPinnedAction = action;
            s_backNavigationPending = true;
            s_backNavigationFrame = Time.frameCount;
        }

        private static void ClearFilters()
        {
            NotifyItemClicked();
            switch (s_activeKind)
            {
                case FilterTrayKind.Menu:
                    KeywordFilterState.Clear();
                    ExtraFilterState.ClearAll();
                    CollectionFilterMenu.ClearExpansions();
                    break;
                case FilterTrayKind.Keywords: KeywordFilterState.Clear(); break;
                case FilterTrayKind.Type: ExtraFilterState.ClearTypes(); break;
                case FilterTrayKind.Race: ExtraFilterState.ClearRaces(); break;
                case FilterTrayKind.School: ExtraFilterState.ClearSchools(); break;
                case FilterTrayKind.Rarity: ExtraFilterState.ClearRarities(); break;
                case FilterTrayKind.Categories: ExtraFilterState.ClearCategories(); break;
                case FilterTrayKind.Packages: ExtraFilterState.ClearPackages(); break;
                case FilterTrayKind.Expansions: CollectionFilterMenu.ClearExpansions(); break;
            }
            if (s_activeKind == FilterTrayKind.Menu)
                foreach (FilterTrayKind kind in FilterModes.Kinds) FilterModes.Set(kind, FilterMode.Single);
            else FilterModes.Set(s_activeKind, FilterMode.Single);
            if (s_activeKind != FilterTrayKind.Expansions && s_activeKind != FilterTrayKind.Menu)
                KeywordFilterApplier.RefreshCollectionPages();
            KeywordFilterUi.NotifyFilterChangedFromChrome();
            if (s_activeKind == FilterTrayKind.Menu) RebuildRows();
            else RefreshAfterSelection();
        }

        private static void SetFilterSelected(SetFilterItem item, bool selected, bool current, bool excluded = false)
        {
            SafeSetSelected(item, selected);
            if (item != null)
            {
                const string prefix = "NOT · ";
                string text = item.Text ?? "";
                if (s_excludedCaptions.TryGetValue(item, out ExcludedCaption saved) && text == prefix + saved.Text) text = saved.Text;
                s_excludedCaptions.Remove(item);
                if (selected && excluded) s_excludedCaptions.Add(item, new ExcludedCaption { Text = text });
                string caption = selected && excluded ? prefix + text : text;
                if (item.Text != caption) { item.Text = caption; item.m_uberText?.UpdateNow(true); }
            }
            if (item == null || item.m_selectedGlow == null) return;
            foreach (Renderer renderer in item.m_selectedGlow.GetComponentsInChildren<Renderer>(true))
            {
                if (!s_originalGlow.TryGetValue(renderer, out MaterialPropertyBlock original))
                {
                    original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original); s_originalGlow[renderer] = original;
                }
                if (selected && (excluded || (FilterModes.IsExpression && !current)))
                {
                    var tint = new MaterialPropertyBlock(); renderer.GetPropertyBlock(tint);
                    Color color = excluded ? new Color(1f, .3f, .2f, 1f) : new Color(1f, 0.78f, 0.12f, 1f);
                    tint.SetColor("_Color", color); tint.SetColor("_TintColor", color);
                    renderer.SetPropertyBlock(tint);
                }
                else renderer.SetPropertyBlock(original);
            }
        }
    }
}
