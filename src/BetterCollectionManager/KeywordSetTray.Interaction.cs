using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        // Use the same native drag path as the deck-list scrollbar. ScrollValue
        // is a fraction (0..1), not a distance in content/world units.
        private static readonly MethodInfo s_startScrollbarDrag = AccessTools.Method(typeof(UIBScrollable), "StartDragging");
        private static readonly MethodInfo s_updateScrollbarDrag = AccessTools.Method(typeof(UIBScrollable), "DragThumb");
        private static bool s_scrollbarCaptured;
        private static int s_scrollbarReleaseFrame = -1;

        private static bool IsScrollbarGesture => s_scrollbarCaptured || s_scrollbarReleaseFrame == Time.frameCount;

        // A modal can claim input before PegUI delivers OnOut. Explicitly retire
        // hover/press visuals while leaving the persistent selection glow intact.
        private static void ClearMenuHoverFeedback()
        {
            if (s_trayObject == null) return;
            foreach (SetFilterItem item in s_trayObject.GetComponentsInChildren<SetFilterItem>(true))
            {
                item.m_mouseOverGlow?.SetActive(false);
                if (item.m_selectedGlow == null || !item.m_selectedGlow.activeSelf) item.m_pressedShadow?.SetActive(false);
            }
            foreach (UIBHighlight highlight in s_trayObject.GetComponentsInChildren<UIBHighlight>(true))
                highlight.m_MouseOverHighlight?.SetActive(false);
        }

        private static void ResetMenuInteractions()
        {
            if (s_scroller?.m_ScrollThumb != null)
                s_scroller.m_ScrollThumb.StopDragging();
            s_scrollbarCaptured = false;
            s_scrollbarReleaseFrame = -1;
        }

        private static void PollMenuInteractions()
        {
            if (!IsShown || s_isAnimating || s_scroller == null || !s_scroller.enabled)
            {
                ResetMenuInteractions();
                return;
            }
            Camera camera = ResolveUiCamera();
            if (camera == null) return;
            bool down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0), up = Input.GetMouseButtonUp(0);
            bool hasBar = TryGetScrollbarRects(camera, out Rect track, out Rect thumb);
            if (down && hasBar && (track.Contains(Input.mousePosition)
                || (thumb.width > 0f && thumb.height > 0f && thumb.Contains(Input.mousePosition)))
                && s_startScrollbarDrag != null && s_updateScrollbarDrag != null)
            {
                s_startScrollbarDrag.Invoke(s_scroller, null);
                s_scrollbarCaptured = s_scroller.m_ScrollThumb.IsDragging();
            }
            if (!s_scrollbarCaptured) return;
            // Jump on the press frame, then continue the same native drag even
            // outside the rail. No renderer-name/shape guess or second click.
            if (down || held || up) s_updateScrollbarDrag.Invoke(s_scroller, null);
            if (up || (!held && !down))
            {
                s_scroller.m_ScrollThumb.StopDragging();
                s_scrollbarCaptured = false;
                s_scrollbarReleaseFrame = Time.frameCount;
            }
        }

        private static bool TryGetScrollbarRects(Camera camera, out Rect track, out Rect thumb)
        {
            track = thumb = default(Rect);
            BoxCollider rail = s_scroller?.m_ScrollTrack;
            ScrollBarThumb grip = s_scroller?.m_ScrollThumb;
            if (rail == null || grip == null || !rail.enabled || !rail.gameObject.activeInHierarchy
                || !grip.gameObject.activeInHierarchy || !grip.IsEnabled()) return false;
            camera = CameraUtils.FindProjectionCameraForObject(rail.gameObject) ?? camera;
            // The configured native rail collider is the complete click area
            // and thumb travel; decorative meshes can be wider or shorter.
            track = GridScreenRect(GetBoxWorldBounds(rail), camera);
            Collider gripHit = grip.GetComponent<Collider>();
            if (gripHit != null && gripHit.enabled)
                thumb = GridScreenRect(gripHit is BoxCollider box ? GetBoxWorldBounds(box) : gripHit.bounds, camera);
            return true;
        }
    }
}
