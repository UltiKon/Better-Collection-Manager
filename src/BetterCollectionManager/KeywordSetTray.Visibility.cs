using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static CameraMask GetActiveCameraMask(GameObject tray)
        {
            foreach (PinnedBackLayout layout in s_backLayouts.Values)
            {
                if (layout.Tray == tray && layout.Mask != null)
                    return layout.Mask;
            }
            return tray.GetComponent<CameraMask>();
        }

        private static void RefreshSlidingCameraMask()
        {
            if (s_trayObject != null) UpdateVisibleMenuMask(GetActiveCameraMask(s_trayObject));
        }

        private static void UpdateVisibleMenuMask(CameraMask mask, bool reinitialize = false)
        {
            if (mask == null) return;
            // Native OnEnable schedules a pass before assigning its scissor.
            // UpdateMask leaves that scissor untouched for an empty offscreen
            // rectangle, so an opening tray can briefly redraw the deck's
            // transparent borders/shadows across an unrestricted viewport.
            if (!mask.gameObject.activeInHierarchy || !HasVisibleMaskArea(mask))
            {
                mask.enabled = false;
                return;
            }
            if (reinitialize) mask.enabled = false;
            mask.enabled = true;
            // Initialize the scissor synchronously with enabling/moving the
            // pass, before a rendered frame can observe the new tray pose.
            mask.UpdateMask();
        }

        private static bool HasVisibleMaskArea(CameraMask mask)
        {
            Camera camera = mask.m_UseCameraFromLayer
                ? CameraUtils.FindFirstByLayer(mask.m_CameraFromLayer)
                : CameraUtils.FindProjectionCameraForObject(mask.gameObject);
            if (camera == null) return false;
            Vector3 center = mask.transform.position, scale = mask.transform.lossyScale;
            Vector3 half = mask.m_UpVector == CameraMask.CAMERA_MASK_UP_VECTOR.Y
                ? new Vector3(mask.m_Width * scale.x, 0f, mask.m_Height * scale.z) * 0.5f
                : new Vector3(mask.m_Width * scale.x, mask.m_Height * scale.y, 0f) * 0.5f;
            Vector3 low = camera.WorldToViewportPoint(center - half), high = camera.WorldToViewportPoint(center + half);
            return Mathf.Abs(Mathf.Clamp01(high.x) - Mathf.Clamp01(low.x)) > 0.00001f
                && Mathf.Abs(Mathf.Clamp01(high.y) - Mathf.Clamp01(low.y)) > 0.00001f;
        }

        private static readonly Dictionary<Collider, bool> s_closedColliders = new Dictionary<Collider, bool>();
        private static readonly Dictionary<PegUIElement, bool> s_closedPegs = new Dictionary<PegUIElement, bool>();
        private static readonly Dictionary<FilterTrayKind, float> s_menuScroll = new Dictionary<FilterTrayKind, float>();
        private static FilterTrayKind s_rowsKind;
        private static int s_rowsRevision = -1, s_rowsRequestedRows, s_rowsRequestedColumns;
        private static FilterModeBehavior s_rowsBehavior;
        private static string s_rowsControlShape;
        private static bool s_rowsClearAll, s_rowsHideEmpty, s_rowsLogicControls, s_rowsEditControls;
        private static void RememberBuiltRows()
        {
            s_rowsControlShape = ControlShape;
            s_rowsKind = s_activeKind; s_rowsRevision = FilterModes.ResultsRevision;
            s_rowsBehavior = CollectionQualitySettings.FilterModeBehavior;
            s_rowsClearAll = CollectionQualitySettings.ShowFilterClearAll;
            s_rowsHideEmpty = CollectionQualitySettings.HideEmptyFilters;
            s_rowsLogicControls = CollectionQualitySettings.ShowLogicControls;
            s_rowsEditControls = CollectionQualitySettings.ShowEditControls;
            s_rowsRequestedRows = CollectionQualitySettings.FilterMenuRows;
            s_rowsRequestedColumns = CollectionQualitySettings.FilterMenuColumns;
        }
        private static bool CanReuseRows(FilterTrayKind kind) => s_rowsRevision == FilterModes.ResultsRevision && CanReuseRowLayout(kind);
        private static bool CanReuseRowLayout(FilterTrayKind kind) => kind == s_rowsKind && s_rowsControlShape == ControlShape && !s_gridDirty && s_gridScreenWidth == Screen.width && s_gridScreenHeight == Screen.height
            && s_rowsBehavior == CollectionQualitySettings.FilterModeBehavior
            && s_rowsLogicControls == CollectionQualitySettings.ShowLogicControls && s_rowsEditControls == CollectionQualitySettings.ShowEditControls
            && s_rowsClearAll == CollectionQualitySettings.ShowFilterClearAll && s_rowsHideEmpty == CollectionQualitySettings.HideEmptyFilters
            && s_rowsRequestedRows == CollectionQualitySettings.FilterMenuRows && s_rowsRequestedColumns == CollectionQualitySettings.FilterMenuColumns;
        private static float? s_openingScroll;
        private static int s_slideVersion;
        private static Vector3 s_slideShown, s_slideHidden;
        internal static bool IsVisible => s_trayObject != null && s_trayObject.activeInHierarchy && (s_shown || s_isAnimating);
        public static FilterTrayKind LastMenuKind { get; private set; } = FilterTrayKind.Menu;

        public static void ResetMenuSession()
        {
            s_menuScroll.Clear(); s_openControls.Clear();
            s_rowsKind = FilterTrayKind.None; s_rowsRevision = -1;
            s_openingScroll = null;
            LastMenuKind = FilterTrayKind.Menu;
        }

        private static void RememberMenuState()
        {
            if (!s_shown || s_scroller == null) return;
            LastMenuKind = s_activeKind;
            s_menuScroll[s_activeKind] = s_openingScroll ?? s_scroller.ScrollValue;
        }

        private static float SavedMenuScroll =>
            s_menuScroll.TryGetValue(s_activeKind, out float saved) ? saved : 0f;

        private static void RestoreMenuScroll()
        {
            ApplyMenuScroll(s_openingScroll ?? SavedMenuScroll);
        }

        private static void ApplyMenuScroll(float value)
        {
            if (s_scroller == null) return;
            float position = Mathf.Clamp01(value);
            s_scroller.SetScrollImmediate(position);
            s_scroller.ScrollValue = position;
            s_scroller.ImmediateScrollValue = position;
            s_scroller.UpdateScroll();
            RefreshScrollbarChrome();
        }
        private static void RefreshScrollbarChrome()
        {
            if (s_scroller == null) return;
            bool needed = s_scroller.EnableIfNeeded();
            // Native Start unconditionally uncovers the rail. Enable has an
            // early return when the state already matches, so reassert the
            // cover after activation as well as after changing menu contents.
            if (s_scroller.m_scrollTrackCover != null) s_scroller.m_scrollTrackCover.SetActive(!needed);
        }
        // Navigation between the native expansion tray and our tray must cancel an
        // outstanding slide, including when hide/leave happens during the animation.
        public static void HideImmediately()
        {
            RememberMenuState();
            StopSlideCoroutine();
            CompleteHide();
        }

        private static void CompleteHide()
        {
            try
            {
                ReleaseMenuInput();
            }
            finally
            {
                // Native PegUI/scroller cleanup must never leave a full-screen
                // capture alive, even if it throws while the tray is rebuilt.
                if (s_trayObject != null) s_trayObject.SetActive(false);
                if (s_hideArea != null) s_hideArea.SetActive(false);
                RestoreCollectionInput();
                s_shown = false;
                s_isAnimating = false;
                s_slideCoroutine = null;
                s_openingScroll = null;
            }
        }

        private static void ReleaseMenuInput(bool keepCollectionBlocked = false)
        {
            ResetMenuInteractions();
            ClearMenuHoverFeedback();
            // HideArea is a sibling of the sliding panel. Hiding the panel
            // alone leaves this full-screen input target in the collection.
            ResetPinnedBackInput();
            s_pointerGesture.Reset();
            ResetPinnedPress();
            SetKeywordHideAreaActive(keepCollectionBlocked);
            if (!keepCollectionBlocked) RestoreCollectionInput();
            if (s_scroller != null)
            {
                s_scroller.m_OverridePegUI = false;
                s_scroller.enabled = false;
            }
            if (s_trayObject == null) return;
            foreach (PegUIElement peg in s_trayObject.GetComponentsInChildren<PegUIElement>(true))
            {
                if (s_hideArea != null && peg.transform.IsChildOf(s_hideArea.transform)) continue;
                if (!s_closedPegs.ContainsKey(peg)) s_closedPegs[peg] = peg.IsEnabled();
                peg.SetEnabled(false, true);
            }
            foreach (Collider collider in s_trayObject.GetComponentsInChildren<Collider>(true))
            {
                if (s_hideArea != null && collider.transform.IsChildOf(s_hideArea.transform)) continue;
                // Native scroll tweens can still refresh row visibility after
                // the scroller is disabled. Collider.bounds is empty when
                // disabled, so retain the viewport until the panel is hidden.
                // Its PegUI and the scroller input are already disabled above.
                if (keepCollectionBlocked && s_scroller != null && collider == s_scroller.m_ScrollBounds) continue;
                if (!s_closedColliders.ContainsKey(collider)) s_closedColliders[collider] = collider.enabled;
                collider.enabled = false;
            }
        }

        private static void RestoreMenuInput()
        {
            foreach (KeyValuePair<Collider, bool> collider in s_closedColliders)
                if (collider.Key != null) collider.Key.enabled = collider.Value;
            foreach (KeyValuePair<PegUIElement, bool> peg in s_closedPegs)
                if (peg.Key != null) peg.Key.SetEnabled(peg.Value, true);
            s_closedColliders.Clear();
            s_closedPegs.Clear();
        }

        public static void SetShown(bool shown)
        {
            SetShown(shown, FilterTrayKind.Keywords, shown ? "Show(true)" : "Show(false)");
        }

        public static void SetShown(bool shown, string reason)
        {
            SetShown(shown, s_activeKind != FilterTrayKind.None ? s_activeKind : FilterTrayKind.Keywords, reason);
        }

        /// <summary>Open/close the shared sliding tray with the given content kind (mutual exclusive).</summary>
        public static void SetShown(bool shown, FilterTrayKind kind, string reason)
        {
            if (shown)
            {
                if (s_trayObject == null || s_live == null)
                {
                    DebugLog.Error("KeywordSetTray.SetShown: not built");
                    return;
                }

                FilterTrayKind requested = kind == FilterTrayKind.None ? FilterTrayKind.Keywords : kind;
                if (IsVisible)
                {
                    if (s_shown && requested == s_activeKind) return;
                    bool animate = s_isAnimating;
                    RememberMenuState();
                    StopSlideCoroutine();
                    RestoreMenuInput();
                    s_scroller.enabled = true;
                    s_scroller.m_OverridePegUI = s_live.m_scroller.m_OverridePegUI;
                    if (requested != s_activeKind)
                    {
                        s_activeKind = requested;
                        s_openingScroll = SavedMenuScroll;
                        RebuildRows();
                    }
                    UpdateSlideEndpoints();
                    s_shown = true;
                    SetKeywordHideAreaActive(true);
                    UpdateCollectionInputBlocker();
                    RestoreMenuScroll();
                    ApplyVisibleAffectedClip();
                    ForceCameraMaskRefresh("content-or-direction-change");
                    if (animate) StartSlide(s_trayObject.transform.localPosition, s_slideShown, false, reason);
                    else
                    {
                        s_trayObject.transform.localPosition = s_slideShown;
                        RefreshSlidingCameraMask();
                        s_openingScroll = null;
                    }
                    return;
                }

                StopSlideCoroutine();
                RestoreMenuInput();
                s_activeKind = kind == FilterTrayKind.None ? FilterTrayKind.Keywords : kind;
                // Keep the requested position independent of native OnEnable /
                // first-frame resets while the new rows and viewport initialize.
                s_openingScroll = SavedMenuScroll;

                try
                {
                    if (s_live.IsShown())
                        s_live.Show(false);
                }
                catch (Exception ex)
                {
                    DebugLog.Exception("KeywordSetTray: closing live sets tray failed", ex);
                }

                Vector3 hidePos = s_live.m_hideBone != null
                    ? s_live.m_hideBone.transform.localPosition
                    : s_trayObject.transform.localPosition;
                Vector3 showPos = s_live.m_showBone != null
                    ? s_live.m_showBone.transform.localPosition
                    : hidePos;

                s_trayObject.transform.localPosition = hidePos + s_gridOffset;
                bool reuseRows = CanReuseRows(s_activeKind);
                if (!reuseRows) EnsureContentsBone(s_live);
                ActivateTrayHierarchy();
                s_scroller.enabled = true;
                s_scroller.m_OverridePegUI = s_live.m_scroller.m_OverridePegUI;
                if (!reuseRows)
                {
                    ConfigureScrollerLikeSets(s_live);
                    RebuildRows();
                }
                UpdateSlideEndpoints();
                hidePos = s_slideHidden;
                showPos = s_slideShown;
                s_trayObject.transform.localPosition = hidePos;
                RestoreMenuScroll();
                ApplyVisibleAffectedClip();
                ForceCameraMaskRefresh("after-populate");

                EnsureKeywordHideArea();
                SetKeywordHideAreaActive(true);

                s_shown = true;
                s_isAnimating = true;
                UpdateCollectionInputBlocker();
                StartSlide(hidePos, showPos, false, reason);
                DebugLog.Msg($"KeywordSetTray: slide open kind={s_activeKind} reason={reason}");
            }
            else
            {
                // A second native/polling close must not cancel a close that
                // is already in progress and run its final hide prematurely.
                if (!s_shown) return;
                FilterButtonFeedback.PlayCloseSound();
                RememberMenuState();
                StopSlideCoroutine();
                // Keep the collection modal until the complete panel is hidden.
                ReleaseMenuInput(s_trayObject != null && s_trayObject.activeSelf);
                if (!s_shown && (s_trayObject == null || !s_trayObject.activeSelf))
                    return;
                if (s_trayObject == null)
                {
                    s_shown = false;
                    return;
                }

                UpdateSlideEndpoints();
                Vector3 showPos = s_trayObject.transform.localPosition;

                s_shown = false;
                s_isAnimating = true;
                StartSlide(showPos, s_slideHidden, true, reason);
                DebugLog.Msg($"KeywordSetTray: slide close reason={reason}");
            }
        }

        private static void UpdateSlideEndpoints()
        {
            s_slideShown = s_live.m_showBone != null
                ? s_live.m_showBone.transform.localPosition + s_gridOffset : s_trayObject.transform.localPosition;
            Vector3 hidden = s_live.m_hideBone != null ? s_live.m_hideBone.transform.localPosition : s_slideShown;
            // Always derive the direction from the fully shown pose. A point
            // halfway offscreen can lie beyond the native hide bone.
            s_slideHidden = GetHiddenGridPosition(hidden, s_slideShown);
        }

        private static void StartSlide(Vector3 from, Vector3 to, bool closing, string reason)
        {
            float distance = Vector3.Distance(s_slideHidden, s_slideShown);
            float fraction = distance > 0.0001f ? Mathf.Clamp01(Vector3.Distance(from, to) / distance) : 0f;
            float seconds = (closing ? SlideOutSeconds : SlideInSeconds) * fraction;
            s_isAnimating = true;
            s_slideCoroutine = LoaderCoroutines.Start(AnimateLocalSlide(from, to, seconds, closing, reason, s_slideVersion));
        }

        private static void StopSlideCoroutine()
        {
            // Invalidate before Dispose: a superseded iterator's finally block
            // must not hide the panel or clear the replacement's state.
            s_slideVersion++;
            object previous = s_slideCoroutine;
            s_slideCoroutine = null;
            if (previous != null)
                try { LoaderCoroutines.Stop(previous); } catch { /* ignore */ }
            s_isAnimating = false;
        }

        /// <summary>
        /// Slide the complete frame from outside the screen, with smooth start
        /// and stop. The geometry and clipping region keep a fixed size.
        /// </summary>
        private static IEnumerator AnimateLocalSlide(
            Vector3 fromLocal,
            Vector3 toLocal,
            float seconds,
            bool closing,
            string reason,
            int version)
        {
            try
            {
                if (version != s_slideVersion || s_trayObject == null) yield break;

                s_trayObject.transform.localPosition = fromLocal;
                RefreshSlidingCameraMask();
                yield return null;
                float t = 0f;
                float dur = Mathf.Max(0.05f, seconds);
                while (t < dur && s_trayObject != null && version == s_slideVersion)
                {
                    t += Time.unscaledDeltaTime;
                    float u = Mathf.Clamp01(t / dur);
                    float e = u * u * (3f - 2f * u);
                    s_trayObject.transform.localPosition = Vector3.LerpUnclamped(fromLocal, toLocal, e);
                    RefreshSlidingCameraMask();
                    yield return null;
                }

                if (version != s_slideVersion) yield break;
                if (s_trayObject != null)
                    s_trayObject.transform.localPosition = toLocal;
                RefreshSlidingCameraMask();

                if (!closing)
                {
                    ForceCameraMaskRefresh("after-slide-in");
                    RestoreMenuScroll();
                    ApplyVisibleAffectedClip();
                    // Native scroller initialization may run after the first
                    // restore. Apply once more at the final pose next frame.
                    yield return null;
                    if (version != s_slideVersion) yield break;
                    RestoreMenuScroll();
                    ApplyVisibleAffectedClip();
                    DebugLog.Verbose($"KeywordSetTray: slide-in complete reason={reason}");
                }
            }
            finally
            {
                if (version == s_slideVersion)
                {
                    try
                    {
                        if (closing || s_trayObject == null)
                        {
                            CompleteHide();
                            DebugLog.Msg($"KeywordSetTray: Show(false) reason={reason}");
                        }
                    }
                    finally
                    {
                        if (!closing) s_openingScroll = null;
                        s_isAnimating = false;
                        s_slideCoroutine = null;
                    }
                }
            }
        }

    }
}
