using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PegasusShared;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>
    /// Uses the collection's existing expansion button as the entry point for all
    /// filters. Multi-selection refines a broad native expansion pool.
    /// </summary>
    internal static class CollectionFilterMenu
    {
        private static SetFilterTray s_live;
        private static HashSet<TAG_CARD_SET> s_widePoolSets;
        public static void InvalidateNativeExpansionPool() { s_widePoolSets = null; }
        private static CollectionSetFilterDropdownToggle s_toggle;
        private static SetFilterItem s_backItem;
        private static readonly ExpansionIconMarker s_expansionIconMarker = new ExpansionIconMarker();
        private static SetFilterItem s_expansionMarkerIcon;
        private static bool s_expansionMarkerPending;
        private static string s_expansionStarDiagnosticStage, s_expansionStarSelectionDiagnostic;
        private static bool s_nativeShowInProgress;
        private static int s_lastToggleFrame = -1;
        private static FieldInfo s_selectedItemField;
        private static SetFilterItem s_expansionSelectionBeforeBack;
        private static bool s_expansionSelectionCaptured;

        public static bool IsAttached => s_live != null && s_toggle != null;
        public static bool IsSelectingExpansion { get; private set; }
        internal static FormatType CardsFormat
        {
            get
            {
                if (ExpansionFilterState.IsActive) return ExpansionFilterState.DisplayFormat;
                var selected = s_live == null ? null : AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
                if (selected != null) return ExpansionFilterState.FitsStandard(selected, new HashSet<TAG_CARD_SET>(GameUtils.GetStandardSets()))
                    ? FormatType.FT_STANDARD : FormatType.FT_WILD;
                return CollectionManager.Get()?.GetThemeShowing() ?? FormatType.FT_STANDARD;
            }
        }

        internal static bool HasExpansionFilter
        {
            get
            {
                if (ExpansionFilterState.IsActive)
                {
                    foreach (SetFilterItem item in ExpansionFilterState.SelectedItems)
                        if (RestrictsExpansions(item)) return true;
                    return false;
                }
                return RestrictsExpansions(s_live == null ? null
                    : AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem);
            }
        }
        private static bool RestrictsExpansions(SetFilterItem item)
        {
            if (item == null) return false;
            if (item.IsAllStandard || (item.FormatType != FormatType.FT_WILD && item.FormatType != FormatType.FT_UNKNOWN)) return true;
            if (ExpansionFilterState.ReadSpecificCards(item)?.Count > 0) return true;
            return item.CardSets != null && item.CardSets.Count > 0
                && !new HashSet<TAG_CARD_SET>(item.CardSets).SetEquals(GameUtils.GetCardSetsInFormat(FormatType.FT_WILD));
        }

        internal static string ButtonPressSound
        {
            get
            {
                UIBHighlight highlight = s_toggle != null ? s_toggle.GetComponent<UIBHighlight>() : null;
                return !string.IsNullOrEmpty(highlight?.m_MouseDownSound)
                    ? highlight.m_MouseDownSound : "Small_Click.prefab:2a1c5335bf08dc84eb6e04fc58160681";
            }
        }

        public static bool Attach(SetFilterTray tray, CollectionSetFilterDropdownToggle toggle)
        {
            if (tray == null || toggle == null || !KeywordSetTray.IsBuilt)
            {
                DebugLog.Warning("CollectionFilterMenu: tray unavailable; keeping expansion button unchanged");
                return false;
            }

            FilterButtonFeedback.BindPressSound(toggle.GetComponent<PegUIElement>());
            if (s_live != tray || s_toggle != toggle)
            {
                InvalidateNativeExpansionPool();
                ClearExpansionMarker();
            }
            s_live = tray;
            s_toggle = toggle;
            if (CollectionQualitySettings.HideEmptyFilters) EnsureWideExpansionPool();
            RefreshExpansionIcon();
            return true;
        }

        // True means the custom tray handled this request and vanilla Show must be skipped.
        public static bool HandleNativeShow(SetFilterTray tray, bool show)
        {
            if (!IsAttached || tray != s_live || s_nativeShowInProgress)
                return false;

            if (KeywordSetTray.ShouldSuppressFilterSelectionForBack())
            {
                KeywordSetTray.RequestPinnedBackNavigation();
                return true;
            }

            if (!show)
            {
                // The native full-screen hide area can also receive the end
                // of a scroll drag after the pointer leaves the shared tray.
                if (KeywordSetTray.ShouldKeepOpenForScrollRelease()) return true;
                if (KeywordSetTray.IsVisible)
                {
                    KeywordSetTray.SetShown(false, "outside");
                    RemoveBackItem();
                    return true;
                }
                KeywordSetTray.HideImmediately();
                RemoveBackItem();
                return false;
            }

            try
            {
                Toggle();
                return true;
            }
            catch (Exception ex)
            {
                // Leave native expansion filtering usable if opening the hub fails.
                DebugLog.Exception("CollectionFilterMenu: opening filter menu failed", ex);
                KeywordSetTray.HideImmediately();
                return false;
            }
        }

        public static void Toggle()
        {
            if (!IsAttached || s_lastToggleFrame == Time.frameCount)
                return;
            s_lastToggleFrame = Time.frameCount;
            KeywordSetTray.NotifyToggleClicked();
            if (KeywordSetTray.IsShown)
                KeywordSetTray.SetShown(false, "filter button");
            else
                Reopen();
        }

        public static void Reopen()
        {
            FilterTrayKind kind = KeywordSetTray.LastMenuKind;
            if (kind == FilterTrayKind.Expansions) ShowExpansions();
            else if (kind == FilterTrayKind.Menu) ShowMenu();
            else ShowFilter(kind);
        }

        internal static bool IsToggleCollider(Collider collider) => s_toggle != null && collider != null
            && (collider.transform.IsChildOf(s_toggle.transform)
                || s_toggle.transform.IsChildOf(collider.transform));

        public static void ShowMenu()
        {
            if (!IsAttached)
                return;
            if (PackageViewState.IsActive) PackageBrowser.Leave();
            KeywordSetTray.NotifyItemClicked();
            if (s_live.IsShown())
                ShowNative(false);
            RemoveBackItem();
            KeywordSetTray.SetShown(true, FilterTrayKind.Menu, "filter menu");
        }

        public static void ShowFilter(FilterTrayKind kind)
        {
            if (!IsAttached)
                return;
            if (kind == FilterTrayKind.Packages) PackageBrowser.Enter();
            else if (PackageViewState.IsActive && kind != FilterTrayKind.Expansions) PackageBrowser.Leave();
            KeywordSetTray.NotifyItemClicked();
            KeywordSetTray.SetShown(true, kind, "filter submenu");
        }

        public static void GoBack()
        {
            if (PackageViewState.IsActive && KeywordSetTray.ActiveKind == FilterTrayKind.Expansions)
            { ShowFilter(FilterTrayKind.Packages); return; }
            ShowMenu();
        }

        public static void ShowExpansions()
        {
            if (!IsAttached)
                return;

            try
            {
                KeywordSetTray.NotifyItemClicked();
                RemoveBackItem();
                // Collection initialization has already populated the native
                // rows. Opening its animated tray just to read them acquires a
                // second input capture; deactivating it during its closing tween
                // prevents the native tray from releasing that capture.
                KeywordSetTray.CaptureExpansionRows(s_live);
                s_selectedItemField = AccessTools.Field(typeof(SetFilterTray), "m_selected");
                s_expansionSelectionCaptured = s_selectedItemField != null;
                s_expansionSelectionBeforeBack = s_selectedItemField?.GetValue(s_live) as SetFilterItem;
                KeywordSetTray.SetShown(true, FilterTrayKind.Expansions, "expansion submenu");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("CollectionFilterMenu.ShowExpansions failed", ex);
                // Do not strand the user in a submenu without a working Back button.
                ShowMenu();
            }
        }

        public static void ChangeExpansionMode(FilterMode mode)
        {
            if (!IsAttached || IsSelectingExpansion) return;
            if (PackageViewState.IsActive)
            { FilterModes.Set(FilterTrayKind.Expansions, mode); PackageBrowser.QueryChanged(); return; }
            if (FilterModes.IsExpression) { FilterModes.Set(FilterTrayKind.Expansions, mode); return; }
            FilterMode previousMode = FilterModes.Get(FilterTrayKind.Expansions);
            var previousItems = ExpansionFilterState.Capture();
            bool wasActive = ExpansionFilterState.IsActive;
            SetFilterItem current = AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
            SetFilterItem last = previousItems.Count > 0 ? previousItems[previousItems.Count - 1].Value : current;
            try
            {
                FilterModes.Set(FilterTrayKind.Expansions, mode);
                if (mode == FilterMode.Single)
                {
                    if (CollectionQualitySettings.HideEmptyFilters || (wasActive && previousItems.Count == 0))
                        ApplyMultipleExpansions(last);
                    else SelectExpansion(last, force: true, modeChange: true);
                }
                else
                {
                    if (!wasActive && current != null) ExpansionFilterState.SetSelected(current, true);
                    ApplyMultipleExpansions(last);
                }
            }
            catch (Exception ex)
            {
                FilterModes.Set(FilterTrayKind.Expansions, previousMode);
                RestoreMultipleExpansions(previousItems, wasActive);
                KeywordSetTray.RefreshExpansionSelection();
                DebugLog.Exception("CollectionFilterMenu.ChangeExpansionMode failed", ex);
            }
        }

        public static void SelectExpansion(SetFilterItem source) => SelectExpansion(source, false);

        private static void SelectExpansion(SetFilterItem source, bool force, bool modeChange = false)
        {
            if (!IsAttached || source == null || source.IsHeader || IsSelectingExpansion) return;
            if (PackageViewState.IsActive)
            { PackageBrowser.SelectExpansion(source); KeywordSetTray.RefreshExpansionSelection(); return; }
            if (CollectionQualitySettings.HideEmptyFilters || FilterModes.IsExpression || FilterModes.Get(FilterTrayKind.Expansions) != FilterMode.Single)
            {
                var previousItems = ExpansionFilterState.Capture();
                bool wasActive = ExpansionFilterState.IsActive;
                try
                {
                    if (!wasActive)
                    {
                        SetFilterItem current = AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
                        if (current != null) ExpansionFilterState.SetSelected(current, true);
                    }
                    if (!FilterModes.IsExpression && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Single)
                        ExpansionFilterState.SetSelected(source, true);
                    else ExpansionFilterState.Toggle(source);
                    ApplyMultipleExpansions(source);
                }
                catch (Exception ex)
                {
                    RestoreMultipleExpansions(previousItems, wasActive);
                    KeywordSetTray.RefreshExpansionSelection();
                    DebugLog.Exception("CollectionFilterMenu.SelectExpansion multi-selection failed", ex);
                }
                return;
            }
            bool propagateFailure = force;
            var previousExpansion = ExpansionFilterState.Capture();
            bool wasCustom = ExpansionFilterState.IsActive;
            s_selectedItemField = s_selectedItemField ?? AccessTools.Field(typeof(SetFilterTray), "m_selected");
            SetFilterItem previous = s_selectedItemField?.GetValue(s_live) as SetFilterItem;
            if (previous == source && !force) return;
            if (wasCustom && !modeChange) { ExpansionFilterState.Clear(); force = true; }
            var previousKeywords = KeywordFilterState.Capture();
            var pageAnchor = KeywordFilterApplier.CapturePageAnchor();
            try
            {
                if (s_selectedItemField == null) throw new MissingFieldException(typeof(SetFilterTray).FullName, "m_selected");
                Delegate callback = source.Callback;
                if (callback == null) throw new InvalidOperationException("Native expansion callback is missing");
                // The display callback applies filtering, but does not perform
                // the native tray's own selection bookkeeping. Set it before
                // filtering so keyword/format lookups see the new selection.
                IsSelectingExpansion = true;
                s_selectedItemField.SetValue(s_live, source);
                KeywordFilterUi.PrepareExpansionFilterChange();
                InvalidateNativeExpansionPool();
                int count = callback.Method.GetParameters().Length;
                if (count == 0) callback.DynamicInvoke();
                else if (count == 1) callback.DynamicInvoke(source);
                else if (count == 5)
                    callback.DynamicInvoke(source.CardSets, GetSpecificCards(source), source.FormatType, source, false);
                else throw new InvalidOperationException("Unsupported native expansion callback signature");
                s_selectedItemField.SetValue(s_live, source);
                s_expansionSelectionBeforeBack = source;
                // With transitionPage=false the callback changes filter state;
                // refresh the visible cards through the same path as the other filters.
                KeywordFilterApplier.RefreshCollectionPages(pageAnchor);
                KeywordSetTray.RefreshExpansionSelection();
                RefreshExpansionIcon();
            }
            catch (Exception ex)
            {
                s_selectedItemField?.SetValue(s_live, previous);
                KeywordFilterState.Restore(previousKeywords);
                if (wasCustom) ExpansionFilterState.Restore(previousExpansion);
                KeywordCatalog.Invalidate();
                if (KeywordFilterState.IsActive) KeywordCatalog.GetEntries();
                KeywordSetTray.RefreshExpansionSelection();
                RefreshExpansionIcon();
                DebugLog.Exception("CollectionFilterMenu.SelectExpansion failed", ex);
                if (propagateFailure) throw;
            }
            finally { IsSelectingExpansion = false; }
        }

        private static void RestoreMultipleExpansions(List<FilterTerm<SetFilterItem>> items, bool active)
        {
            ExpansionFilterState.Clear();
            if (active)
            {
                // Empty multi-selection is a valid unfiltered pool.
                ExpansionFilterState.Activate();
                ExpansionFilterState.Restore(items);
            }
            KeywordCatalog.Invalidate();
            if (KeywordFilterState.IsActive) KeywordCatalog.GetEntries();
            RefreshExpansionIcon();
        }

        public static bool EnsureWideExpansionPool()
        {
            if (PackageViewState.IsActive) return false;
            if (!IsAttached || IsSelectingExpansion || !CollectionQualitySettings.HideEmptyFilters) return false;
            SetFilterItem current = AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
            if (current == null) return false;
            try
            {
                if (!ExpansionFilterState.IsActive) ExpansionFilterState.SetSelected(current, true);
                ApplyMultipleExpansions(current);
                return true;
            }
            catch (Exception ex) { DebugLog.Exception("Preparing filter availability failed", ex); return false; }
        }

        public static void ClearExpansions()
        {
            if (!IsAttached || IsSelectingExpansion) return;
            if (PackageViewState.IsActive)
            { ExpansionFilterState.Clear(); PackageBrowser.QueryChanged(); return; }
            SetFilterItem source = AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
            var previous = ExpansionFilterState.Capture();
            bool active = ExpansionFilterState.IsActive;
            try
            {
                ExpansionFilterState.Clear();
                ExpansionFilterState.Activate();
                ApplyMultipleExpansions(source);
            }
            catch (Exception ex)
            {
                RestoreMultipleExpansions(previous, active);
                DebugLog.Exception("Clear expansions failed", ex);
            }
        }

        private static void ApplyMultipleExpansions(SetFilterItem source)
        {
            if (source == null) return;
            Delegate callback = source.Callback;
            if (callback == null || callback.Method.GetParameters().Length != 5)
                throw new InvalidOperationException("Native expansion callback does not support multiple selections");
            s_selectedItemField = s_selectedItemField ?? AccessTools.Field(typeof(SetFilterTray), "m_selected");
            if (s_selectedItemField == null) throw new MissingFieldException(typeof(SetFilterTray).FullName, "m_selected");
            SetFilterItem previous = s_selectedItemField.GetValue(s_live) as SetFilterItem;
            var previousKeywords = KeywordFilterState.Capture();
            var pageAnchor = KeywordFilterApplier.CapturePageAnchor();
            try
            {
                IsSelectingExpansion = true;
                ExpansionFilterState.Activate();
                s_selectedItemField.SetValue(s_live, source);
                KeywordFilterUi.PrepareExpansionFilterChange();
                List<TAG_CARD_SET> requiredSets = ExpansionFilterState.NativeCardSets();
                // Special native groups can include sets outside Wild. Only reuse
                // the pool when its actual native arguments are unchanged.
                if (s_widePoolSets == null || !s_widePoolSets.SetEquals(requiredSets))
                {
                    callback.DynamicInvoke(requiredSets, null, FormatType.FT_WILD, source, false);
                    s_widePoolSets = new HashSet<TAG_CARD_SET>(requiredSets);
                }
                s_selectedItemField.SetValue(s_live, source);
                s_expansionSelectionBeforeBack = source;
                KeywordFilterApplier.RefreshCollectionPages(pageAnchor);
                KeywordSetTray.RefreshExpansionSelection();
                RefreshExpansionIcon();
            }
            catch
            {
                InvalidateNativeExpansionPool();
                s_selectedItemField.SetValue(s_live, previous);
                KeywordFilterState.Restore(previousKeywords);
                throw;
            }
            finally { IsSelectingExpansion = false; }
        }

        internal static void RefreshExpansionIcon()
        {
            if (!IsAttached) return;
            try
            {
                SetFilterItem selected = AccessTools.Field(typeof(SetFilterTray), "m_selected")?.GetValue(s_live) as SetFilterItem;
                var choices = new HashSet<SetFilterItem>();
                if (ExpansionFilterState.IsActive)
                    foreach (SetFilterItem item in ExpansionFilterState.SelectedItems) choices.Add(item);
                else if (selected != null && !PackageViewState.IsActive) choices.Add(selected);
                bool multiple = choices.Count > 1;
                bool standard = (PackageViewState.IsActive ? PackageViewState.Format : ExpansionFilterState.DisplayFormat) == FormatType.FT_STANDARD;
                SetFilterItem icon = null;
                if (choices.Count == 1) foreach (SetFilterItem item in choices) icon = item;
                else
                {
                    var native = AccessTools.Field(typeof(SetFilterTray), "m_items")?.GetValue(s_live) as System.Collections.IEnumerable;
                    if (native != null)
                        foreach (object value in native)
                            if (value is SetFilterItem item && !item.IsHeader && (standard
                                ? item.IsAllStandard : item.FormatType == FormatType.FT_WILD
                                    && !RestrictsExpansions(item))) { icon = item; break; }
                }
                if (icon == null) icon = selected;
                string selectionDiagnostic = "selected=" + choices.Count + "; active=" + ExpansionFilterState.IsActive
                    + "; requested=" + multiple;
                if (s_expansionStarSelectionDiagnostic != selectionDiagnostic)
                {
                    s_expansionStarSelectionDiagnostic = selectionDiagnostic;
                    DebugLog.Msg("[Expansion star] diagnostics=star-only-v4; " + selectionDiagnostic);
                }
                if (icon != null) s_toggle.SetToggleIcon(icon.IconTexture, icon.IconOffset ?? UnityEngine.Vector2.zero);
                UpdateExpansionStar(multiple, icon);
            }
            catch (Exception ex) { DebugLog.Exception("Expansion logo refresh failed", ex); }
        }

        private static void UpdateExpansionStar(bool visible, SetFilterItem icon)
        {
            s_expansionMarkerIcon = visible ? icon : null;
            s_expansionMarkerPending = visible;
            if (!visible)
            {
                ReportExpansionStar("hidden", "selection does not require a multiple-expansion marker");
                return;
            }
            if (icon?.IconTexture == null || s_toggle?.m_currentIconQuad == null)
            {
                ReportExpansionStar("waiting-icon", "native texture or logo renderer is unavailable");
                return;
            }
            try
            {
                Texture2D marked = s_expansionIconMarker.Get(icon.IconTexture, s_toggle.m_currentIconQuad);
                if (marked == null)
                {
                    ReportExpansionStar("waiting-texture", "native texture has not finished loading");
                    return;
                }
                // Use the same quad, material, UV offset, tint and camera as
                // the visible native logo. No separate text object is drawn.
                s_toggle.SetToggleIcon(marked, icon.IconOffset ?? UnityEngine.Vector2.zero);
                s_expansionMarkerPending = false;
                ReportExpansionStar("texture-ready", "source=" + icon.IconTexture.name
                    + "; marked=" + marked.name + "; size=" + marked.width + "x" + marked.height);
            }
            catch (Exception ex)
            {
                s_expansionMarkerPending = false;
                DebugLog.Exception("Expansion star texture creation failed", ex);
            }
        }

        internal static void UpdateExpansionMarker()
        {
            if (s_expansionMarkerPending && s_toggle != null)
                UpdateExpansionStar(true, s_expansionMarkerIcon);
        }

        private static void ReportExpansionStar(string stage, string details)
        {
            if (s_expansionStarDiagnosticStage == stage) return;
            s_expansionStarDiagnosticStage = stage;
            DebugLog.Msg("[Expansion star] " + stage + "; " + details);
        }

        private static void ClearExpansionMarker()
        {
            if (s_toggle != null && s_expansionMarkerIcon?.IconTexture != null)
                s_toggle.SetToggleIcon(s_expansionMarkerIcon.IconTexture, s_expansionMarkerIcon.IconOffset ?? UnityEngine.Vector2.zero);
            s_expansionIconMarker.Clear();
            s_expansionMarkerIcon = null;
            s_expansionMarkerPending = false;
            s_expansionStarDiagnosticStage = s_expansionStarSelectionDiagnostic = null;
        }

        private static object GetSpecificCards(SetFilterItem source)
        {
            PropertyInfo property = AccessTools.Property(typeof(SetFilterItem), "SpecificCards");
            if (property != null) return property.GetValue(source, null);
            FieldInfo field = AccessTools.Field(typeof(SetFilterItem), "m_specificCards");
            if (field != null) return field.GetValue(source);
            throw new MissingMemberException(typeof(SetFilterItem).FullName, "SpecificCards");
        }

        public static bool TryGetToggleScreenRect(Camera fallback, out Rect rect)
        {
            rect = default(Rect);
            if (!IsAttached) return false;
            bool found = false;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (BoxCollider box in s_toggle.GetComponentsInChildren<BoxCollider>(false))
            {
                // A pressed or temporarily disabled toggle still occupies
                // this space; use layout bounds, not its interaction state.
                Camera camera = CameraUtils.FindProjectionCameraForObject(box.gameObject) ?? fallback;
                if (camera == null) continue;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = camera.WorldToScreenPoint(box.transform.TransformPoint(box.center + new Vector3(
                        box.size.x * 0.5f * x, box.size.y * 0.5f * y, box.size.z * 0.5f * z)));
                    left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
                    low = Mathf.Min(low, point.y); high = Mathf.Max(high, point.y);
                    found = true;
                }
            }
            if (found) rect = Rect.MinMaxRect(left, low, right, high);
            return found;
        }

        private static void ShowNative(bool show)
        {
            s_nativeShowInProgress = true;
            try
            {
                s_live.Show(show);
            }
            finally
            {
                s_nativeShowInProgress = false;
            }
        }

        private static void RemoveBackItem()
        {
            KeywordSetTray.ReleasePinnedBackItem(s_backItem);
            s_backItem = null;
            s_expansionSelectionCaptured = false;
            s_expansionSelectionBeforeBack = null;
        }

        public static void RestoreExpansionSelectionForBack()
        {
            if (!s_expansionSelectionCaptured || s_live == null || s_selectedItemField == null)
                return;
            SetFilterItem clicked = s_selectedItemField.GetValue(s_live) as SetFilterItem;
            s_selectedItemField.SetValue(s_live, s_expansionSelectionBeforeBack);
            if (clicked != null && clicked != s_expansionSelectionBeforeBack)
                clicked.SetSelected(false);
            if (s_expansionSelectionBeforeBack != null)
                s_expansionSelectionBeforeBack.SetSelected(true);
        }

        // The cloned tray's outside-click handler must recognize the original
        // expansion button, even though there are no separate mod plaques now.
        public static bool IsPointerOverToggle()
        {
            if (!IsAttached || !s_toggle.gameObject.activeInHierarchy)
                return false;
            Collider[] colliders = s_toggle.GetComponentsInChildren<Collider>(false);
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null || !camera.enabled)
                    continue;
                foreach (Collider collider in colliders)
                {
                    if (!collider.enabled || (camera.cullingMask & (1 << collider.gameObject.layer)) == 0)
                        continue;
                    Ray ray = camera.ScreenPointToRay(Input.mousePosition);
                    if (collider.Raycast(ray, out _, 800f))
                        return true;
                }
            }
            return false;
        }

        public static void Detach()
        {
            CatalogTransferMenu.DestroyMenu();
            PackageBrowser.Reset();
            ClearExpansionMarker();
            RemoveBackItem();
            ExpansionFilterState.Clear();
            FilterAvailability.Clear();
            InvalidateNativeExpansionPool();
            s_live = null;
            s_toggle = null;
            s_nativeShowInProgress = false;
            s_lastToggleFrame = -1;
        }
    }
}
