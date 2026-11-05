using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static readonly Dictionary<Collider, bool> s_collectionInput = new Dictionary<Collider, bool>();
        private static SetFilterItem s_pinnedPressedControl;
        private static int s_pinnedPressFrame = -1;

        private static void ResetPinnedPress() { s_pinnedPressedControl = null; s_pinnedPressFrame = -1; }
        private static void RecordPinnedPress(SetFilterItem item)
        {
            s_pinnedPressedControl = item != null && s_backLayouts.ContainsKey(item) ? item : null;
            s_pinnedPressFrame = Time.frameCount;
        }
        private static bool CanReleasePinnedControl(SetFilterItem item)
        {
            ObservePointerGesture();
            if (IsScrollbarGesture) return false;
            // Preserve the press target through every RELEASE callback, even
            // when native input dispatch runs before menu polling.
            return !Input.GetMouseButtonUp(0) || (s_pinnedPressFrame >= 0
                && ReferenceEquals(s_pinnedPressedControl, item) && FindPinnedBackUnderPointer() == item);
        }

        private static Camera[] s_inputCameras = new Camera[8];
        private static RaycastHit[] s_inputRaycast = new RaycastHit[64];
        private static readonly List<RaycastHit> s_pointerHits = new List<RaycastHit>();
        private static bool s_pointerHitScope, s_pointerHitsValid;
        private static List<RaycastHit> PointerHits()
        {
            if (s_pointerHitScope && s_pointerHitsValid) return s_pointerHits;
            s_pointerHits.Clear(); s_pointerHitsValid = true;
            int count = Camera.allCamerasCount;
            if (s_inputCameras.Length < count) Array.Resize(ref s_inputCameras, Math.Max(count, s_inputCameras.Length * 2));
            count = Camera.GetAllCameras(s_inputCameras);
            for (int i = 0; i < count; i++)
            {
                Camera camera = s_inputCameras[i];
                if (camera == null || !camera.enabled || camera.cullingMask == 0) continue;
                Ray ray = camera.ScreenPointToRay(Input.mousePosition);
                int hits;
                while ((hits = Physics.RaycastNonAlloc(ray, s_inputRaycast, 800f, camera.cullingMask)) == s_inputRaycast.Length)
                    Array.Resize(ref s_inputRaycast, s_inputRaycast.Length * 2);
                for (int hit = 0; hit < hits; hit++) s_pointerHits.Add(s_inputRaycast[hit]);
                Array.Clear(s_inputRaycast, 0, hits);
            }
            Array.Clear(s_inputCameras, 0, count);
            return s_pointerHits;
        }

        private static void UpdateCollectionInputBlocker()
        {
            if (s_trayObject == null || !s_trayObject.activeInHierarchy || (!s_shown && !s_isAnimating))
            {
                // Native callbacks and scene changes can hide the hierarchy
                // without going through our closing animation.
                if (s_shown || s_isAnimating) HideImmediately();
                else if (s_collectionInput.Count > 0 || (s_hideArea != null && s_hideArea.activeSelf) || (s_hidePeg?.IsEnabled() ?? false))
                {
                    RestoreCollectionInput();
                    SetKeywordHideAreaActive(false);
                }
                return;
            }
            foreach (Collider blocked in s_collectionInput.Keys)
                if (blocked != null) blocked.enabled = false;
            foreach (RaycastHit hit in PointerHits())
            {
                Collider collider = hit.collider;
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy
                    || IsMenuInputCollider(collider)
                    || (s_hideArea != null && collider.transform.IsChildOf(s_hideArea.transform))
                    || CollectionFilterMenu.IsToggleCollider(collider)) continue;
                s_collectionInput[collider] = true;
                collider.enabled = false;
            }
        }

        private static bool IsMenuInputCollider(Collider collider)
        {
            Transform root = s_trayObject.transform;
            if (collider.transform.IsChildOf(root) || root.IsChildOf(collider.transform)
                || collider == s_scroller?.m_ScrollBounds) return true;
            // A sibling collider can belong to the shared collection PegUI
            // controller. Disabling that controller's input also disables us.
            PegUIElement owner = collider.GetComponentInParent<PegUIElement>();
            return owner != null && root.IsChildOf(owner.transform);
        }

        private static void RestoreCollectionInput()
        {
            foreach (KeyValuePair<Collider, bool> collider in s_collectionInput)
                if (collider.Key != null) collider.Key.enabled = collider.Value;
            s_collectionInput.Clear();
        }

        private static void ObservePointerGesture()
        {
            if (Input.GetMouseButtonDown(0) && s_pinnedPressFrame != Time.frameCount)
                RecordPinnedPress(FindPinnedBackUnderPointer());
            Vector3 pointer = Input.mousePosition;
            s_pointerGesture.Observe(Time.frameCount, Input.GetMouseButtonDown(0), Input.GetMouseButton(0),
                Input.GetMouseButtonUp(0), pointer.x, pointer.y,
                IsPointerOverVisibleTray());
        }

        /// <summary>
        /// Scroll refresh + outside-close. Must NEVER close when pointer is over tray tiles / K —
        /// that was the regression (tile click → reason=outside).
        /// </summary>
        public static void PollOutsideClick()
        {
            s_pointerHitScope = true; s_pointerHitsValid = false;
            try { PollOutsideClickCore(); }
            finally { s_pointerHitScope = false; s_pointerHitsValid = false; s_pointerHits.Clear(); }
        }
        private static void PollOutsideClickCore()
        {
            RefreshCategoryActionLabels();
            UpdateCollectionInputBlocker();
            if (s_shown) ObservePointerGesture();
            try
            {
                PollPinnedBackInput();
            }
            catch (Exception ex)
            {
                ResetPinnedBackInput();
                DebugLog.Exception("Pinned Back input failed", ex);
            }
            if (!s_shown)
                return;
            RefreshScrollbarChrome();
            PollMenuInteractions();
            if (!s_isAnimating && (s_gridDirty || s_gridScreenWidth != Screen.width || s_gridScreenHeight != Screen.height))
                RebuildRows();

            // Sets-style visible-affected refresh while scrolling (no custom fullyInside).
            try
            {
                if (s_scroller != null)
                {
                    float v = s_scroller.ScrollValue;
                    if (float.IsNaN(s_lastScrollValue) || Mathf.Abs(v - s_lastScrollValue) > 0.0005f)
                    {
                        s_lastScrollValue = v;
                        ApplyVisibleAffectedClip();
                        // Optional: compare live Sets fire when Sets tray is also open/inspectable.
                        if (s_live != null && s_live.IsShown() && s_live.m_scroller != null)
                        {
                            DebugLog.Msg(
                                $"KeywordSetTray scroll fire: kwScroll={v:F3} setsScroll={s_live.m_scroller.ScrollValue:F3} " +
                                $"(both UpdateAndFire paths)");
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (Time.unscaledTime < s_ignoreOutsideUntil)
                return;
            if (!Input.GetMouseButtonUp(0) || !s_pointerGesture.CloseOutside)
                return;
            // K cork: leave alone (PegUI RELEASE toggles). Tray contents: don't outside-close.
            if (KeywordFilterUi.IsPointerOverOpenButton())
                return;
            if (IsPointerOverTrayContents())
                return;
            SetShown(false, "outside");
        }

        /// <summary>Call when a FilterItem is clicked so hide/poll cannot close on the same release.</summary>
        public static void NotifyItemClicked()
        {
            s_ignoreOutsideUntil = Time.unscaledTime + 0.35f;
        }

        public static bool ShouldKeepOpenForScrollRelease()
        {
            if (!s_shown) return false;
            ObservePointerGesture();
            return Input.GetMouseButtonUp(0) && !s_pointerGesture.CloseOutside && (s_pointerGesture.Dragged || IsScrollbarGesture);
        }

        // CameraMask clips pixels, but does not clip PegUI hit targets. Capture
        // Back independently of the collider PegUI happened to choose.
        private static void PollPinnedBackInput()
        {
            if (!s_shown)
            {
                ResetPinnedBackInput();
                return;
            }
            if (s_backNavigationPending && Time.frameCount > s_backNavigationFrame && !s_isAnimating)
            {
                Action action = s_pendingPinnedAction;
                ResetPinnedBackInput();
                FilterButtonFeedback.RunFilterAction(action ?? CollectionFilterMenu.GoBack);
                return;
            }

            // A held press can move between controls. Release the previous
            // pointer's blockers before resolving the current hover target.
            if (s_backBlockedColliders.Count > 0 && Time.frameCount > s_backReleaseFrame)
            {
                RestoreBackBlockedColliders();
                s_pointerHitsValid = false;
            }
            SetFilterItem hovered = FindPinnedBackUnderPointer();
            if (Input.GetMouseButtonDown(0))
            {
                s_backPressedItem = hovered;
                if (hovered != null) FilterButtonFeedback.PlayPressSound(hovered.GetComponent<PegUIElement>());
            }

            if (hovered != null)
                BlockCollidersBehindBack(hovered);
            else if (!s_backNavigationPending && Time.frameCount > s_backReleaseFrame)
                RestoreBackBlockedColliders();

            // Hold capture through every RELEASE handler in this frame,
            // including native expansion callbacks.
            if (Input.GetMouseButtonUp(0))
            {
                if (hovered != null)
                    RequestPinnedControl(hovered);
                s_backPressedItem = null;
                if (s_backBlockedColliders.Count > 0)
                    s_backReleaseFrame = Time.frameCount;
            }
        }

        public static void RequestPinnedBackNavigation()
        {
            SetFilterItem hovered = FindPinnedBackUnderPointer();
            if (hovered != null && s_pinnedActions.ContainsKey(hovered)) { RequestPinnedControl(hovered); return; }
            if (IsScrollbarGesture) return;
            ObservePointerGesture();
            if (Input.GetMouseButtonUp(0) && (hovered == null || !CanReleasePinnedControl(hovered)))
                return;
            NotifyItemClicked();
            if (s_backNavigationPending)
                return;
            s_backNavigationPending = true;
            s_backNavigationFrame = Time.frameCount;
        }

        public static bool ShouldSuppressFilterSelectionForBack()
        {
            return Input.GetMouseButtonUp(0) && (s_backNavigationPending
                || (s_backReleaseFrame == Time.frameCount && s_backBlockedColliders.Count > 0)
                || FindPinnedBackUnderPointer() != null);
        }

        private static void EnsureKeywordHideArea()
        {
            if (s_hideArea != null || s_trayObject == null)
                return;
            try
            {
                Transform parent = s_trayObject.transform.parent != null
                    ? s_trayObject.transform.parent
                    : s_trayObject.transform;

                s_hideArea = new GameObject("BCM_KeywordHideArea");
                GameUtils.SetParent(s_hideArea, parent.gameObject, false);
                s_hideArea.layer = s_trayObject.layer;
                s_hideArea.transform.SetAsFirstSibling();

                // Place WELL behind the tray along the camera→tray axis so PegUI hits tiles first.
                Camera cam = ResolveUiCamera();
                Vector3 trayPos = s_trayObject.transform.position;
                if (cam != null)
                {
                    Vector3 away = (trayPos - cam.transform.position).normalized;
                    if (away.sqrMagnitude < 0.01f)
                        away = cam.transform.forward;
                    s_hideArea.transform.position = trayPos + away * 12f;
                    s_hideArea.transform.rotation = Quaternion.LookRotation(away, cam.transform.up);
                }
                else
                {
                    s_hideArea.transform.localPosition = new Vector3(0f, 0f, 20f);
                    s_hideArea.transform.localRotation = Quaternion.identity;
                }
                s_hideArea.transform.localScale = Vector3.one;

                BoxCollider box = s_hideArea.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = Vector3.zero;
                // Wide plane behind tray — not overlapping tray depth.
                box.size = new Vector3(100f, 80f, 0.25f);

                s_hidePeg = s_hideArea.AddComponent<PegUIElement>();
                s_hidePeg.SetEnabled(true, true);
                s_hidePeg.AddEventListener(UIEventType.RELEASE, OnKeywordHideAreaReleased);
                s_hideArea.SetActive(false);
                DebugLog.Msg(
                    $"KeywordSetTray: BCM_KeywordHideArea behind tray at {Format(s_hideArea.transform.position)}");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.EnsureKeywordHideArea failed", ex);
            }
        }

        private static void OnKeywordHideAreaReleased(UIEvent e)
        {
            ObservePointerGesture();
            if (s_pointerGesture.Dragged && !s_pointerGesture.CloseOutside) return;
            if (ShouldSuppressFilterSelectionForBack())
            {
                RequestPinnedBackNavigation();
                return;
            }
            if (Time.unscaledTime < s_ignoreOutsideUntil)
                return;
            if (!s_shown || !s_pointerGesture.CloseOutside)
                return;
            // K cork owns toggle — hide-area often steals the RELEASE; relay toggle instead of ignoring.
            if (KeywordFilterUi.IsPointerOverOpenButton())
            {
                DebugLog.Msg("KeywordSetTray: hide release over K → relay toggle");
                KeywordFilterUi.RelayOpenButtonToggle("hide-steal");
                return;
            }
            // Tray tiles / scroll chrome — do not outside-close.
            if (IsPointerOverTrayContents())
            {
                DebugLog.Msg("KeywordSetTray: hide release ignored (pointer over tray contents)");
                return;
            }
            if (s_pointerGesture.CloseOutside) SetShown(false, "outside");
        }

        private static void SetKeywordHideAreaActive(bool active)
        {
            if (s_hideArea == null || (!active && !s_hideArea.activeSelf && !(s_hidePeg?.IsEnabled() ?? false)))
                return;
            s_hidePeg?.SetEnabled(active, true);
            foreach (Collider collider in s_hideArea.GetComponentsInChildren<Collider>(true))
                collider.enabled = active;
            s_hideArea.SetActive(active);
            if (active && s_trayObject != null)
            {
                // Re-assert behind tray each show (tray may have moved to showBone).
                try
                {
                    Camera cam = ResolveUiCamera();
                    Vector3 trayPos = s_trayObject.transform.position;
                    if (cam != null)
                    {
                        Vector3 away = (trayPos - cam.transform.position).normalized;
                        if (away.sqrMagnitude < 0.01f)
                            away = cam.transform.forward;
                        s_hideArea.transform.position = trayPos + away * 12f;
                        s_hideArea.transform.rotation = Quaternion.LookRotation(away, cam.transform.up);
                    }
                    s_hideArea.transform.SetAsFirstSibling();
                    s_trayObject.transform.SetAsLastSibling();
                }
                catch
                {
                    // ignore
                }
            }
        }

        /// <summary>
        /// True if cursor is over keyword tray chrome / FilterItems / scroll — NOT the K cork.
        /// K is handled separately (explicit toggle).
        /// </summary>
        private static bool IsPointerOverTrayContents() => s_shown && IsPointerOverVisibleTray();

    }
}
