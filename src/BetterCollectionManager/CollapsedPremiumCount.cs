using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using PegasusShared;

namespace BetterCollectionManager
{
    /// <summary>Displays all existing premiums without changing the native inventory/count data.</summary>
    internal static class CollapsedPremiumCount
    {
        private static readonly Dictionary<CollectionCardCount, Display> s_displays =
            new Dictionary<CollectionCardCount, Display>();
        private static readonly FieldInfo s_centerBone = typeof(CollectionCardCount).GetField(
            "m_centerBoneToUse", BindingFlags.Instance | BindingFlags.NonPublic);

        public static string Format(string cardId, TAG_PREMIUM displayedPremium, bool colorCode = false)
            => Format(cardId, displayedPremium, colorCode, colorCode);

        private static string Format(string cardId, TAG_PREMIUM displayedPremium, bool colorCode, bool omitZeros)
        {
            CollectionManager manager = CollectionManager.Get();
            if (manager == null || string.IsNullOrEmpty(cardId)) return null;
            var text = new StringBuilder();
            bool reachedDisplayed = false;
            foreach (TAG_PREMIUM premium in PreferredPremiumPrefs.Order)
            {
                if (premium == displayedPremium) reachedDisplayed = true;
                // GetCard is the game's exact (CardId, premium) index. In
                // particular it omits nonexistent Diamond/Signature variants.
                if (manager.GetCard(cardId, premium) == null) continue;
                int owned = Math.Max(0, manager.GetOwnedCount(cardId, premium));
                // Colors identify each quality without positional zero slots.
                if (owned == 0 && (omitZeros || !reachedDisplayed)) continue;
                if (text.Length > 0) text.Append('+');
                string color = colorCode ? QualityColor(premium) : null;
                if (color != null) text.Append("<color=#").Append(color).Append('>');
                text.Append(owned.ToString(CultureInfo.InvariantCulture));
                if (color != null) text.Append("</color>");
            }
            return text.Length > 0 ? text.ToString() : null;
        }

        private static string QualityColor(TAG_PREMIUM premium)
        {
            switch (premium)
            {
                // Quality colors for readable digits on the native counter tab.
                case TAG_PREMIUM.NORMAL: return "FFFFFF";
                case TAG_PREMIUM.GOLDEN: return "FFB300";
                case TAG_PREMIUM.DIAMOND: return "00D5FF";
                case TAG_PREMIUM.SIGNATURE: return "D291FF";
                default: return null;
            }
        }

        public static void Attach(CollectionCardVisual visual)
        {
            if (visual == null || visual.GetVisualType() != CollectionUtils.ViewMode.CARDS
                || visual.m_count == null || visual.m_count.m_normalCountText == null
                || !PreferredQualityCollapse.IsSingleTile(visual.CardId)) return;
            try
            {
                Release(visual);
                s_displays[visual.m_count] = new Display(visual);
                // Re-enter native visibility logic after binding. This keeps
                // deck locks and the separate trial tab under native control.
                visual.OnDoneCrafting();
            }
            catch (Exception ex)
            {
                Release(visual);
                DebugLog.Exception("Collapsed premium count setup failed", ex);
            }
        }

        public static void Refresh(CollectionCardCount count)
        {
            if (count == null || !s_displays.TryGetValue(count, out Display display)) return;
            if (!PreferredQualityCollapse.IsSingleTile(display.CardId))
            {
                s_displays.Remove(count);
                display.Restore(false);
                return;
            }
            try { display.Apply(); }
            catch (Exception ex)
            {
                s_displays.Remove(count);
                display.Restore();
                DebugLog.Exception("Collapsed premium count update failed", ex);
            }
        }

        public static void Release(CollectionCardVisual visual)
        {
            if (visual == null || visual.m_count == null) return;
            if (s_displays.TryGetValue(visual.m_count, out Display display))
            {
                s_displays.Remove(visual.m_count);
                display.Restore();
            }
        }

        public static void OnUpdate()
        {
            // Text meshes may not have their font geometry on the first Show.
            // Retry only pending visible counters; native deck locks stay hidden.
            List<CollectionCardCount> pending = null;
            foreach (var entry in s_displays)
                if (entry.Value.NeedsLayout && entry.Key != null && entry.Key.gameObject.activeInHierarchy)
                {
                    if (pending == null) pending = new List<CollectionCardCount>();
                    pending.Add(entry.Key);
                }
            if (pending != null)
                foreach (CollectionCardCount count in pending) Refresh(count);
        }

        public static void ResetSession()
        {
            foreach (Display display in s_displays.Values) display.Restore();
            s_displays.Clear();
        }

        private sealed class Display
        {
            // Fixed card-space inset on each side, independent of glyph count.
            private const float HorizontalPadding = 0.30f;
            private readonly CollectionCardCount m_count;
            private readonly string m_cardId;
            private readonly TAG_PREMIUM m_displayedPremium;
            private readonly UberText m_label;
            private readonly float m_width;
            private readonly bool m_resize, m_grow, m_wrap, m_richText;
            private readonly int m_fontSize;
            private readonly Vector3 m_wideScale, m_widePosition;
            private readonly string m_text;
            private readonly Color m_gradientUpper, m_gradientLower;
            private readonly Transform m_visual;
            private bool m_custom;
            private readonly CounterSeparators m_separators;
            private readonly CounterOutline m_outline;
            public bool NeedsLayout { get; private set; }
            public string CardId => m_cardId;

            public Display(CollectionCardVisual visual)
            {
                CollectionCardCount count = visual.m_count;
                m_count = count;
                m_visual = visual.transform;
                m_cardId = visual.CardId;
                // Keep the tile's representative quality when a split tile
                // selects its lower half for deck input.
                m_displayedPremium = visual.GetActor().GetPremium();
                m_label = count.m_normalCountText;
                m_separators = new CounterSeparators(m_label);
                m_outline = new CounterOutline(m_label);
                m_width = m_label.Width;
                m_resize = m_label.ResizeToFit;
                m_grow = m_label.ResizeToFitAndGrow;
                m_wrap = m_label.WordWrap;
                m_richText = m_label.RichText;
                m_fontSize = m_label.FontSize;
                m_text = m_label.Text;
                m_gradientUpper = m_label.GradientUpperColor;
                m_gradientLower = m_label.GradientLowerColor;
                m_wideScale = count.m_normalWideBorder != null
                    ? count.m_normalWideBorder.transform.localScale : Vector3.one;
                m_widePosition = count.m_normalWideBorder != null
                    ? count.m_normalWideBorder.transform.localPosition : Vector3.zero;
            }

            public void Apply()
            {
                if (m_count == null || m_label == null) return;
                string breakdown = Format(m_cardId, m_displayedPremium);
                bool colorCode = CollectionQualitySettings.ColorCodeCounter;
                string text = colorCode ? Format(m_cardId, m_displayedPremium, false, true) : breakdown;
                if (text != null)
                {
                    long total = 0;
                    foreach (string part in text.Split('+'))
                        if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity)) total += quantity;
                    if (total == 1)
                    {
                        Restore(false);
                        // A single owned copy needs no tab, including when
                        // hidden zero qualities made it look like a breakdown.
                        m_count.m_normalTab?.SetActive(false);
                        if (m_count.GetTrialCount() == 0) m_count.gameObject.SetActive(false);
                        return;
                    }
                }
                if (breakdown == null || text == null || (!colorCode && breakdown.IndexOf('+') < 0))
                {
                    // Native UpdateVisibility has already set the text, frame,
                    // trial tabs and visibility for an individual quantity.
                    Restore(false);
                    return;
                }
                bool single = text.IndexOf('+') < 0;
                if (single) text = "x" + text;
                m_custom = true;
                // This is only the initial layout width; the final frame is
                // measured from rendered glyphs with a fixed inset below.
                m_label.Width = m_width * Mathf.Max(1f, text.Length / 3f);
                // Keep the native font size for every breakdown. Only the tab
                // grows; auto-fitting would make longer counts look smaller.
                m_label.ResizeToFitAndGrow = false;
                m_label.ResizeToFit = false;
                m_label.WordWrap = false;
                m_label.FontSize = m_fontSize;
                m_label.RichText = true;
                string styledText = colorCode ? Format(m_cardId, m_displayedPremium, true) : text;
                if (colorCode && single) styledText = styledText.Insert(styledText.IndexOf('>') + 1, "x");
                if (!colorCode) m_outline.Clear();
                m_label.Text = CounterTextLayout.Decorate(styledText, m_fontSize);
                Color separatorColor = ApplyTheme();
                if (m_count.m_normalBorder != null) m_count.m_normalBorder.SetActive(false);
                if (m_count.GetTrialCount() == 0)
                {
                    if (m_count.m_trialTab != null) m_count.m_trialTab.SetActive(false);
                    if (m_count.m_trialWideBorder != null) m_count.m_trialWideBorder.SetActive(false);
                    if (m_count.m_trialAndOwnedBorder != null) m_count.m_trialAndOwnedBorder.SetActive(false);
                }
                if (m_count.m_normalTab != null)
                {
                    if (m_count.GetTrialCount() == 0 && s_centerBone?.GetValue(m_count) is Transform center)
                        m_count.m_normalTab.transform.position = center.position;
                    m_count.m_normalTab.SetActive(true);
                }
                // A breakdown remains visible even when omitted zeros leave
                // a single colored quantity identifying the owned quality.
                m_count.gameObject.SetActive(true);
                m_label.UpdateNow(true);
                float cardScale = Mathf.Max(Mathf.Abs(m_visual.lossyScale.x), 0.0001f);
                GameObject frame = m_count.m_normalWideBorder;
                if (frame == null) return;
                frame.transform.localScale = m_wideScale;
                frame.transform.localPosition = m_widePosition;
                frame.SetActive(true);
                NeedsLayout = !CounterTextLayout.TryGlyphRange(m_label, out float left, out float right)
                    || !CounterTextLayout.TryFrameRange(frame, out _, out _);
                if (NeedsLayout)
                {
                    m_outline.Clear();
                    Vector3 fallback = m_wideScale;
                    fallback.x *= Mathf.Max(1f, text.Length / 3f);
                    frame.transform.localScale = fallback;
                    return;
                }
                float padding = HorizontalPadding * cardScale;
                float width = right - left + 2f * padding;
                if (m_label.RenderOnObject == null)
                {
                    float labelScale = Mathf.Max(Mathf.Abs(m_label.transform.lossyScale.x), 0.0001f);
                    m_label.Width = width / labelScale;
                    m_label.UpdateNow(true);
                    // Final text layout may move bearings or rebuild its plane.
                    if (!CounterTextLayout.TryGlyphRange(m_label, out left, out right))
                    {
                        m_outline.Clear();
                        NeedsLayout = true;
                        return;
                    }
                    width = right - left + 2f * padding;
                }
                CounterTextLayout.TryFrameRange(frame, out float frameLeft, out float frameRight);
                Vector3 scale = m_wideScale;
                scale.x *= width / (frameRight - frameLeft);
                frame.transform.localScale = scale;
                CounterTextLayout.TryFrameRange(frame, out frameLeft, out frameRight);
                Vector3 position = frame.transform.position;
                position.x += (left + right - frameLeft - frameRight) * 0.5f;
                frame.transform.position = position;
                NeedsLayout = !m_separators.Update(text, m_fontSize, separatorColor);
                // Do not short-circuit: both decorations need updates even if
                // one is still waiting for its native font geometry.
                NeedsLayout |= !m_outline.Update(text, m_fontSize, colorCode);
            }

            private Color NativeTextColor()
            {
                switch (CollectionManager.Get()?.GetThemeShowing())
                {
                    case FormatType.FT_WILD: return m_count.m_wildTextColor;
                    case FormatType.FT_CLASSIC: return m_count.m_classicTextColor;
                    default: return m_count.m_standardTextColor;
                }
            }

            private Color ApplyTheme()
            {
                // Native UpdateVisibility returns before styling when the real
                // selected premium has only one copy. Mixed totals still show.
                Color color = NativeTextColor();
                Material material = m_count.m_standardBorderMaterial;
                switch (CollectionManager.Get().GetThemeShowing())
                {
                    case FormatType.FT_WILD:
                        material = m_count.m_wildBorderMaterial;
                        break;
                    case FormatType.FT_CLASSIC:
                        material = m_count.m_classicBorderMaterial;
                        break;
                }
                // Quality colors are absolute RGB targets. The native brown
                // text tint must not darken/recolor them; retain native alpha.
                // The separate plus labels keep the original theme tint.
                m_label.TextColor = CollectionQualitySettings.ColorCodeCounter
                    ? new Color(1f, 1f, 1f, color.a) : color;
                // Native theme gradients otherwise dim the quality colors.
                m_label.GradientUpperColor = CollectionQualitySettings.ColorCodeCounter ? Color.white : m_gradientUpper;
                m_label.GradientLowerColor = CollectionQualitySettings.ColorCodeCounter ? Color.white : m_gradientLower;
                if (material != null && m_count.m_normalWideBorder != null)
                {
                    Renderer renderer = m_count.m_normalWideBorder.GetComponent<Renderer>();
                    if (renderer != null) renderer.sharedMaterial = material;
                }
                return color;
            }

            public void Restore(bool restoreText = true)
            {
                NeedsLayout = false;
                m_separators.Clear();
                m_outline.Clear();
                if (!m_custom) return;
                m_custom = false;
                if (m_label != null)
                {
                    if (m_count != null) m_label.TextColor = NativeTextColor();
                    m_label.Width = m_width;
                    m_label.ResizeToFitAndGrow = m_grow;
                    m_label.ResizeToFit = m_resize;
                    m_label.WordWrap = m_wrap;
                    m_label.RichText = m_richText;
                    m_label.FontSize = m_fontSize;
                    m_label.GradientUpperColor = m_gradientUpper;
                    m_label.GradientLowerColor = m_gradientLower;
                    if (restoreText) m_label.Text = m_text;
                }
                if (m_count != null && m_count.m_normalWideBorder != null)
                {
                    m_count.m_normalWideBorder.transform.localScale = m_wideScale;
                    m_count.m_normalWideBorder.transform.localPosition = m_widePosition;
                }
            }
        }
    }
}
