using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>A thin screen-space stroke using the same native font as the counter.</summary>
    internal sealed class CounterOutline
    {
        private const float OffsetPixels = 1.25f;
        // An opaque edge stays legible on both light and dark book themes.
        private const float LayerOpacity = 1f;
        private static readonly Vector2[] Directions = {
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1),
            new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1)
        };
        private readonly UberText m_source;
        private readonly Dictionary<Renderer, int> m_nativeOrders = new Dictionary<Renderer, int>();
        private readonly UberText[] m_labels = new UberText[8];

        public CounterOutline(UberText source)
        {
            m_source = source;
        }

        public bool Update(string text, int fontSize, bool enabled)
        {
            if (!enabled) { Clear(); return true; }
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_source.gameObject);
            if (camera == null || !CounterTextLayout.TryScreenInkRect(m_source, camera, out Rect ink)) return false;

            Renderer[] faces = CounterTextLayout.VisibleRenderers(m_source);
            if (faces.Length == 0) { Clear(); return false; }
            // Order the actual screen output, including the native texture
            // quad, rather than relying on the offscreen font's render queue.
            foreach (Renderer face in faces)
            {
                if (!m_nativeOrders.ContainsKey(face)) m_nativeOrders.Add(face, face.sortingOrder);
                face.sortingOrder = m_nativeOrders[face] + 1;
            }
            int order = m_nativeOrders[faces[0]], layer = faces[0].sortingLayerID;
            for (int i = 0; i < m_labels.Length; i++)
            {
                if (m_labels[i] == null) m_labels[i] = CloneLabel();
                UberText label = m_labels[i];
                label.transform.localPosition = m_source.transform.localPosition;
                label.transform.localRotation = m_source.transform.localRotation;
                label.transform.localScale = m_source.transform.localScale;
                label.FontSize = fontSize;
                label.CharacterSize = m_source.CharacterSize;
                label.Width = m_source.Width;
                label.Alignment = m_source.Alignment;
                label.Anchor = m_source.Anchor;
                label.ResizeToFitAndGrow = false;
                label.ResizeToFit = false;
                label.WordWrap = false;
                label.RichText = true;
                label.TextColor = new Color(0f, 0f, 0f, m_source.TextColor.a * LayerOpacity);
                label.GradientUpperColor = label.GradientLowerColor = Color.white;
                // Use plain counts, retaining transparent separator gaps but
                // omitting the foreground quality colors from the black stroke.
                label.Text = CounterTextLayout.Decorate(text, fontSize);
                label.gameObject.SetActive(true);
                label.UpdateNow(true);
                foreach (Renderer stroke in CounterTextLayout.VisibleRenderers(label))
                {
                    stroke.sortingLayerID = layer;
                    stroke.sortingOrder = order;
                }
                if (!CounterTextLayout.TryScreenInkRect(label, camera, out Rect outline)) return false;
                Vector3 from = camera.WorldToScreenPoint(label.transform.position);
                Vector3 target = from;
                target.x += ink.center.x - outline.center.x + Directions[i].x * OffsetPixels;
                target.y += ink.center.y - outline.center.y + Directions[i].y * OffsetPixels;
                label.transform.position += camera.ScreenToWorldPoint(target) - camera.ScreenToWorldPoint(from);
            }
            return true;
        }

        private UberText CloneLabel()
        {
            GameObject go = Object.Instantiate(m_source.gameObject, m_source.transform.parent, false);
            go.SetActive(false);
            go.name = "BCM_CounterOutline";
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            UberText label = go.GetComponent<UberText>();
            label.RenderOnObject = null;
            label.GameStringLookup = false;
            return label;
        }

        public void Clear()
        {
            foreach (var entry in m_nativeOrders)
                if (entry.Key != null && entry.Key.sortingOrder == entry.Value + 1)
                    entry.Key.sortingOrder = entry.Value;
            m_nativeOrders.Clear();
            for (int i = 0; i < m_labels.Length; i++)
            {
                if (m_labels[i] == null) continue;
                m_labels[i].gameObject.SetActive(false);
                Object.Destroy(m_labels[i].gameObject);
                m_labels[i] = null;
            }
        }
    }
}
