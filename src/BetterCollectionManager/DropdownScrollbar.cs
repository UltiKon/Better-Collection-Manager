using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    // Native DropdownControl lays out every item and has no scrolling of its
    // own. Keep its rows/callbacks, but give overflowing mod lists a viewport.
    internal sealed class DropdownScrollbar : MonoBehaviour
    {
        private DropdownControl m_control;
        private readonly List<DropdownMenuItem> m_rows = new List<DropdownMenuItem>();
        private ComboMemberScrollbar m_bar;
        private bool m_open;
        private int m_first, m_visible, m_width, m_height;
        private Vector3 m_menuPosition, m_middleScale;
        private Rect m_viewport;

        internal static void Attach(DropdownControl control)
        {
            FilterButtonFeedback.BindDropdown(control);
            if (control.GetComponent<DropdownScrollbar>() != null) return;
            DropdownScrollbar scroll = control.gameObject.AddComponent<DropdownScrollbar>();
            scroll.m_control = control;
            var previous = control.getMenuShownCallback();
            control.setMenuShownCallback(shown => {
                previous?.Invoke(shown);
                if (shown) scroll.Tick(); else scroll.Reset();
            });
        }

        internal bool ConsumeRelease()
            => Input.GetMouseButtonUp(0) && (m_bar?.ConsumeRelease(Input.mousePosition) ?? false);

        private int m_tickFrame = -1, m_tickButtons;
        private Vector3 m_tickPointer;
        private float m_tickWheel;
        private void LateUpdate() => Tick();
        internal bool Tick()
        {
            if (m_control == null) return false;
            if (!m_control.isMenuShown()) { Reset(); return false; }
            Camera camera = CameraUtils.FindProjectionCameraForObject(gameObject);
            if (camera == null) return false;
            int count = 0;
            foreach (DropdownMenuItem row in m_control.m_menuItemContainer.GetComponentsInChildren<DropdownMenuItem>(true))
                if (row != m_control.m_menuItemTemplate && row != m_control.m_selectedItem) count++;
            if (m_open && count != m_rows.Count) Reset();
            if (!m_open) Open(camera);
            if (m_width != Screen.width || m_height != Screen.height) { Reset(); Open(camera); }
            if (m_rows.Count <= m_visible) return false;
            int buttons = (Input.GetMouseButtonDown(0) ? 1 : 0) | (Input.GetMouseButton(0) ? 2 : 0) | (Input.GetMouseButtonUp(0) ? 4 : 0);
            if (m_tickFrame == Time.frameCount && m_tickPointer.Equals(Input.mousePosition)
                && m_tickWheel == Input.mouseScrollDelta.y && m_tickButtons == buttons) return false;
            m_tickFrame = Time.frameCount; m_tickPointer = Input.mousePosition;
            m_tickWheel = Input.mouseScrollDelta.y; m_tickButtons = buttons;
            int first = m_first;
            bool consumed = m_bar != null && m_bar.Poll(ref first);
            if (!consumed && m_viewport.Contains(Input.mousePosition) && Input.mouseScrollDelta.y != 0f)
            { first -= (int)Math.Sign(Input.mouseScrollDelta.y); consumed = true; }
            first = Math.Max(0, Math.Min(first, m_rows.Count - m_visible));
            if (first != m_first) { m_first = first; Layout(camera); }
            else if (m_bar?.Retry ?? false) Layout(camera);
            return consumed;
        }

        private void Open(Camera camera)
        {
            FilterButtonFeedback.BindDropdownRows(m_control);
            m_open = true; m_width = Screen.width; m_height = Screen.height;
            m_menuPosition = m_control.m_menu.transform.localPosition;
            m_middleScale = m_control.m_menuMiddle.transform.localScale;
            m_rows.Clear();
            foreach (DropdownMenuItem row in m_control.m_menuItemContainer.GetComponentsInChildren<DropdownMenuItem>(true))
                if (row != m_control.m_menuItemTemplate && row != m_control.m_selectedItem) m_rows.Add(row);
            float height = 0;
            if (m_rows.Count > 0 && ComboSceneGeometry.ScreenBounds(m_rows[0].gameObject, camera, out Rect rowArea)) height = rowArea.height;
            m_visible = height > 0 ? Math.Max(2, Math.Min(8, (int)(Screen.height * .55f / height))) : 8;
            m_first = 0;
            // Start near the current choice rather than hiding it below the fold.
            for (int i = 0; i < m_rows.Count; i++)
                if (m_rows[i].m_selected != null && m_rows[i].m_selected.activeSelf)
                    m_first = Math.Min(i, Math.Max(0, m_rows.Count - m_visible));
            if (m_rows.Count > m_visible) Layout(camera);
        }

        private void Layout(Camera camera)
        {
            if (m_bar?.Root != null) m_bar.Root.SetActive(false);
            var container = m_control.m_menuItemContainer;
            container.ClearSlices();
            for (int i = 0; i < m_rows.Count; i++)
            {
                DropdownMenuItem row = m_rows[i];
                bool visible = i >= m_first && i < m_first + m_visible;
                row.gameObject.SetActive(visible);
                if (row.m_text != null) { if (visible) row.m_text.Show(); else row.m_text.Hide(); }
                if (visible) container.AddSlice(row.gameObject);
            }
            container.UpdateSlices();
            Vector3 scale = m_middleScale;
            scale.z *= (m_visible - 1f) / Math.Max(1, m_rows.Count - 1);
            m_control.m_menuMiddle.transform.localScale = scale;
            m_control.m_menu.UpdateSlices();
            // Translate the whole native popup; do not shrink its font or grip.
            m_control.m_menu.transform.localPosition = m_menuPosition;
            if (!ComboSceneGeometry.ScreenBounds(m_control.m_menu.gameObject, camera, out Rect bounds)) return;
            float margin = Math.Max(4f, Screen.height * .01f);
            float dx = bounds.xMin < margin ? margin - bounds.xMin
                : bounds.xMax > Screen.width - margin ? Screen.width - margin - bounds.xMax : 0;
            float dy = bounds.yMin < margin ? margin - bounds.yMin
                : bounds.yMax > Screen.height - margin ? Screen.height - margin - bounds.yMax : 0;
            Transform menu = m_control.m_menu.transform;
            Vector3 point = camera.WorldToScreenPoint(menu.position);
            menu.position += camera.ScreenToWorldPoint(point + new Vector3(dx, dy, 0)) - camera.ScreenToWorldPoint(point);
            bounds = new Rect(bounds.xMin + dx, bounds.yMin + dy, bounds.width, bounds.height);
            m_viewport = bounds;
            float width = Math.Max(18f, bounds.height / m_visible * .75f);
            if (m_bar == null) m_bar = new ComboMemberScrollbar(menu);
            m_bar.Layout(camera, m_control.m_menu.gameObject,
                new Rect(bounds.xMax - width, bounds.yMin, width, bounds.height), m_first, m_rows.Count, m_visible);
        }

        private void Reset()
        {
            if (!m_open) return;
            m_bar?.Hide();
            if (m_control != null)
            {
                m_control.m_menu.transform.localPosition = m_menuPosition;
                m_control.m_menuMiddle.transform.localScale = m_middleScale;
                foreach (DropdownMenuItem row in m_rows)
                    if (row != null) { row.gameObject.SetActive(true); row.m_text?.Show(); }
            }
            m_rows.Clear(); m_open = false;
        }
        private void OnDisable() => Reset();
        private void OnDestroy() { Reset(); m_bar?.Dispose(); }
    }
}
