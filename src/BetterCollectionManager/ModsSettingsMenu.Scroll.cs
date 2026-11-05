using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed partial class ModsSettingsMenu
    {
        private sealed class ScrollRow
        {
            public GameObject Object;
            public Vector3 Position;
            public Rect Bounds;
            public bool Active;
            public readonly List<SettingsScrollHit> Hits = new List<SettingsScrollHit>();
        }
        private readonly List<ScrollRow> m_scrollRows = new List<ScrollRow>();
        private Rect m_settingsViewport;
        private float m_settingsScroll, m_settingsScrollRange, m_settingsScrollStep;
        private Camera m_settingsCamera;
        private string m_settingsScrollDiagnostic;

        private static Rect[] OriginalCheckboxBounds(List<int> original, List<int> visible, Rect[] bounds)
        {
            var result = new Rect[original.Count];
            for (int i = 0; i < original.Count; i++) result[i] = bounds[visible.IndexOf(original[i])];
            return result;
        }

        private void RestoreSettingsScrollRows()
        {
            StopSettingsClipping();
            foreach (ScrollRow row in m_scrollRows)
                if (row.Object != null)
                {
                    RestoreSettingsHits(row);
                    row.Object.transform.position = row.Position;
                    row.Object.SetActive(row.Active);
                }
            m_scrollRows.Clear();
            if (m_settingsScrollbar != null) m_settingsScrollbar.SetActive(false);
        }

        private void CaptureSettingsScroll(Camera camera, float width, float height, float unit)
        {
            m_settingsCamera = camera;
            Vector3 origin = camera.WorldToScreenPoint(m_menu.m_buttonContainer.transform.position);
            m_settingsViewport = Rect.MinMaxRect(origin.x - width * 0.5f, origin.y - height,
                origin.x + width * 0.5f, origin.y);
            m_settingsScrollStep = unit * 1.5f;
            var objects = new List<GameObject>(m_rows);
            objects.AddRange(m_checkboxRows);
            objects.AddRange(m_sectionRows);
            float bottom = m_settingsViewport.yMax;
            foreach (GameObject go in objects)
            {
                bool active = go.activeSelf;
                if (!active) continue;
                bool found = TryColliderRect(go.GetComponentsInChildren<Collider>(false), camera, out Rect rect);
                foreach (UberText text in go.GetComponentsInChildren<UberText>(false))
                    if (CounterTextLayout.TryScreenInkRect(text, camera, out Rect ink))
                    { rect = found ? UnionRect(rect, ink) : ink; found = true; }
                foreach (MeshRenderer art in go.GetComponentsInChildren<MeshRenderer>(false))
                    if (art.GetComponentInParent<UberText>() == null && TryArtworkRect(art, camera, out Rect artwork))
                    { rect = found ? UnionRect(rect, artwork) : artwork; found = true; }
                if (!found) continue;
                bottom = Mathf.Min(bottom, rect.yMin);
                var row = new ScrollRow { Object = go, Position = go.transform.position, Bounds = rect, Active = active };
                foreach (BoxCollider hit in go.GetComponentsInChildren<BoxCollider>(false))
                    row.Hits.Add(new SettingsScrollHit { Collider = hit, Center = hit.center, Size = hit.size, Enabled = hit.enabled });
                m_scrollRows.Add(row);
            }
            m_settingsScrollRange = Mathf.Max(0f, m_settingsViewport.yMin + unit * BottomPaddingRatio - bottom);
            if (m_settingsScrollRange <= 0.5f) m_settingsScrollRange = 0f;
            m_settingsScroll = Mathf.Clamp(m_settingsScroll, 0f, m_settingsScrollRange);
            ApplySettingsScroll();
            string diagnostic = "Mod Settings scroll: rows=" + m_scrollRows.Count + "; range=" + m_settingsScrollRange.ToString("F1")
                + "; viewport=" + m_settingsViewport.width.ToString("F1") + "x" + m_settingsViewport.height.ToString("F1")
                + "; native bar=" + (m_settingsScrollbar != null);
            if (diagnostic != m_settingsScrollDiagnostic) { m_settingsScrollDiagnostic = diagnostic; DebugLog.Msg(diagnostic); }
        }

        private void ApplySettingsScroll()
        {
            if (m_settingsCamera == null) return;
            foreach (ScrollRow row in m_scrollRows)
            {
                Vector3 point = m_settingsCamera.WorldToScreenPoint(row.Position);
                row.Object.transform.position = row.Position + m_settingsCamera.ScreenToWorldPoint(
                    point + new Vector3(0f, m_settingsScroll, 0f)) - m_settingsCamera.ScreenToWorldPoint(point);
                // Keep partial edge rows alive. Private native meshes clip
                // their pixels, while hit geometry follows the same viewport.
                bool overlaps = m_settingsScrollRange == 0f || (row.Bounds.yMax + m_settingsScroll > m_settingsViewport.yMin
                    && row.Bounds.yMin + m_settingsScroll < m_settingsViewport.yMax);
                row.Object.SetActive(row.Active && overlaps);
                RestoreSettingsHits(row);
                if (m_settingsScrollRange > 0f)
                    foreach (SettingsScrollHit hit in row.Hits) ClipSettingsHit(hit);
            }
            if (!m_updatingSettings) UpdateSettingsClipping();
            LayoutSettingsScrollbar();
        }

        private void UpdateSettingsScroll()
        {
            if (m_settingsScrollRange <= 0f) return;
            if (m_settingsScrollbar == null && Time.unscaledTime >= m_settingsScrollbarRetry) LayoutSettingsScrollbar();
            foreach (DropdownControl control in m_controls)
                if (control.isMenuShown()) { StopSettingsScrollbarDrag(); return; }
            if (PollSettingsScrollbar() || !m_settingsViewport.Contains(Input.mousePosition)) return;
            float wheel = Input.mouseScrollDelta.y;
            if (wheel == 0f) return;
            m_settingsScroll = Mathf.Clamp(m_settingsScroll - wheel * m_settingsScrollStep, 0f, m_settingsScrollRange);
            ApplySettingsScroll();
        }
    }
}
