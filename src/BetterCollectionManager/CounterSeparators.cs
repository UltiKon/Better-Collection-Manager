using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Legacy TextMesh has no vertical-offset tag: position small native labels instead.</summary>
    internal sealed class CounterSeparators
    {
        private readonly UberText m_source;
        private readonly List<UberText> m_labels = new List<UberText>();

        public CounterSeparators(UberText source) { m_source = source; }

        public bool Update(string text, int fontSize, Color color)
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_source.gameObject);
            if (camera == null || !CounterTextLayout.TryScreenInkRect(m_source, camera, out Rect whole)) return false;
            int index = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '+') continue;
                if (index == m_labels.Count) m_labels.Add(CloneLabel());
                UberText label = m_labels[index++];
                label.transform.localPosition = m_source.transform.localPosition;
                label.transform.localRotation = m_source.transform.localRotation;
                label.transform.localScale = m_source.transform.localScale;
                label.TextColor = color;
                label.FontSize = fontSize;
                label.CharacterSize = m_source.CharacterSize;
                label.Width = m_source.Width;
                label.Alignment = m_source.Alignment;
                label.Anchor = m_source.Anchor;
                label.ResizeToFitAndGrow = false;
                label.ResizeToFit = false;
                label.WordWrap = false;
                label.RichText = true;
                label.gameObject.SetActive(true);

                // Measure the real prefix, including kerning and earlier small
                // separators. The final plus occupies the existing inline gap.
                label.Text = CounterTextLayout.Decorate(text.Substring(0, i + 1), fontSize, false);
                label.Show();
                if (!CounterTextLayout.TryScreenInkRect(label, camera, out Rect prefix)) return false;
                label.Text = text.Replace("+", "");
                label.UpdateNow(true);
                if (!CounterTextLayout.TryScreenInkRect(label, camera, out Rect numbers)) return false;

                label.Text = "+";
                label.CharacterSize = m_source.CharacterSize * 0.5f;
                label.Alignment = UberText.AlignmentOptions.Center;
                label.Anchor = UberText.AnchorOptions.Middle;
                label.UpdateNow(true);
                if (!CounterTextLayout.TryScreenInkRect(label, camera, out Rect plus)) return false;
                float targetX = whole.xMin + prefix.width - plus.width * 0.5f;
                Vector3 from = camera.WorldToScreenPoint(label.transform.position);
                Vector3 to = from;
                to.x += targetX - plus.center.x;
                // A small optical correction below the geometric ink center
                // keeps the short cross from looking raised beside tall digits.
                to.y += numbers.center.y - numbers.height * 0.08f - plus.center.y;
                label.transform.position += camera.ScreenToWorldPoint(to) - camera.ScreenToWorldPoint(from);
            }
            for (int i = index; i < m_labels.Count; i++) m_labels[i].gameObject.SetActive(false);
            return true;
        }

        private UberText CloneLabel()
        {
            // Cloning preserves the native font, outlines and texture settings.
            // Generated render surfaces belong to their old UberText instance;
            // rebuild only those children on this private copy.
            GameObject go = Object.Instantiate(m_source.gameObject, m_source.transform.parent, false);
            go.SetActive(false);
            go.name = "BCM_CounterSeparator";
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            UberText label = go.GetComponent<UberText>();
            label.RenderOnObject = null;
            label.GameStringLookup = false;
            return label;
        }

        public void Clear()
        {
            foreach (UberText label in m_labels)
                if (label != null)
                {
                    label.gameObject.SetActive(false);
                    Object.Destroy(label.gameObject);
                }
            m_labels.Clear();
        }
    }
}
