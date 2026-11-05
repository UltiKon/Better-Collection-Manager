using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Measures the visible glyphs, including native offscreen-text projection.</summary>
    internal static class CounterTextLayout
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo s_texture = typeof(UberText).GetField("m_UberTextRenderToTexture", Fields);
        private static readonly FieldInfo s_plane = typeof(UberTextRenderToTexture).GetField("m_planeMeshRenderer", Fields);
        private static readonly FieldInfo s_view = typeof(UberTextRenderToTexture).GetField("viewMatrix", Fields);
        private static readonly FieldInfo s_projection = typeof(UberTextRenderToTexture).GetField("projectionMatrix", Fields);

        private static readonly FieldInfo s_rendering = typeof(UberText).GetField("m_UberTextRendering", Fields);
        private static readonly MethodInfo s_captionRotation = typeof(UberText).GetMethod("GetTextRotation", Fields);
        private static readonly MethodInfo s_captionCenter = typeof(UberText).GetMethod("GetTextCenter", Fields);
        internal static void UseDirectCaption(SetFilterItem item)
        {
            UberText label = item?.m_uberText;
            if (label == null || label.gameObject.name == "BCM_FilterCaption") return;
            // These rows are moved and culled by UIBScrollable. Native RTT
            // output and its separate shadow retain an independent cached pose;
            // a later text update can reveal both at different positions.
            Renderer oldSurface = TextureSurface(label);
            label.Hide(); label.enabled = false;
            if (oldSurface != null) oldSurface.enabled = false;
            foreach (TextMesh glyph in label.GetComponentsInChildren<TextMesh>(true))
            {
                Renderer renderer = glyph.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
            }
            // Runtime-created surfaces can have been inherited from a scene clone.
            foreach (Transform node in item.GetComponentsInChildren<Transform>(true))
                if (node.gameObject.name.StartsWith("UberText_RenderPlane_", StringComparison.Ordinal)
                    || node.gameObject.name.StartsWith("UberText_ShadowPlane_", StringComparison.Ordinal))
                    node.gameObject.SetActive(false);
            // Give the custom row a fresh native text owner. Reusing the old
            // owner lets its RTT helper re-enable cached surfaces on UpdateNow.
            // Its normal OnDestroy remains responsible for releasing those assets.
            UberText caption = CreateDirectCopy(label, item.transform, "BCM_FilterCaption");
            item.m_uberText = caption;
            item.gameObject.AddComponent<FilterCaptionLifetime>().Initialize(item, label, caption, oldSurface);
        }
        internal static UberText CreateDirectCopy(UberText label, Transform parentTransform, string objectName)
        {
            var go = new GameObject(objectName); go.transform.SetParent(parentTransform, false);
            go.layer = label.gameObject.layer;
            Vector3 center = s_captionCenter?.Invoke(label, null) is Vector3 offset ? offset : Vector3.zero;
            go.transform.position = label.transform.TransformPoint(center);
            Quaternion rotation = s_captionRotation?.Invoke(label, null) is Quaternion native
                ? native : Quaternion.identity;
            go.transform.rotation = label.transform.rotation * rotation;
            Vector3 scale = label.transform.lossyScale, parent = parentTransform.lossyScale;
            float Local(float desired, float value) => Mathf.Abs(value) > .000001f ? desired / value : 1f;
            go.transform.localScale = new Vector3(Local(scale.x, parent.x), Local(scale.y, parent.y), Local(scale.z, parent.z));
            UberText caption = go.AddComponent<UberText>();
            caption.TrueTypeFont = label.TrueTypeFont ?? label.GetLocalizedFont(); caption.FontSize = label.FontSize;
            caption.CharacterSize = label.CharacterSize; caption.BoldSize = label.BoldSize;
            caption.Width = label.Width; caption.Alignment = label.Alignment; caption.Anchor = label.Anchor;
            caption.TextColor = label.TextColor; caption.GradientUpperColor = label.GradientUpperColor; caption.GradientLowerColor = label.GradientLowerColor;
            caption.RenderQueue = label.RenderQueue; caption.WordWrap = label.WordWrap;
            caption.ResizeToFit = label.ResizeToFit; caption.ResizeToFitAndGrow = label.ResizeToFitAndGrow;
            caption.RichText = label.RichText; caption.GameStringLookup = false;
            foreach (string name in new[] { "Height", "LineSpacing", "Outline", "OutlineSize", "OutlineColor" })
            {
                PropertyInfo property = typeof(UberText).GetProperty(name);
                if (property?.CanRead == true && property.CanWrite) property.SetValue(caption, property.GetValue(label, null), null);
            }
            caption.RenderToTexture = false; caption.Text = label.Text;
            return caption;
        }
        internal static void SetRowVisible(GameObject row, bool visible)
        {
            row.SetActive(visible);
            UberText caption = row.GetComponent<SetFilterItem>()?.m_uberText;
            if (caption == null) return;
            if (visible) caption.Show(); else caption.Hide();
            Renderer glyph = GlyphRenderer(caption);
            if (glyph != null) glyph.enabled = visible;
        }
        private static readonly Dictionary<Type, MethodInfo> s_glyphMethods = new Dictionary<Type, MethodInfo>();
        private static Renderer CurrentGlyph(object rendering)
        {
            if (rendering == null) return null;
            Type type = rendering.GetType();
            if (!s_glyphMethods.TryGetValue(type, out MethodInfo method))
            { method = type.GetMethod("GetTextMeshRenderer", BindingFlags.Instance | BindingFlags.Public); s_glyphMethods[type] = method; }
            return method?.Invoke(rendering, null) as Renderer;
        }
        internal static Renderer GlyphRenderer(UberText label)
        {
            object rendering = label != null ? s_rendering?.GetValue(label) : null;
            return CurrentGlyph(rendering);
        }
        [ThreadStatic] private static List<TextMesh> s_clonedGlyphs;
        internal static int RemoveClonedGlyphs(UberText label)
        {
            object rendering = s_rendering?.GetValue(label);
            Renderer current = CurrentGlyph(rendering);
            if (current == null) return 0;
            int removed = 0;
            // Scene templates can already contain a generated text mesh. Unity
            // clones that mesh, while the new UberText builds another one.
            List<TextMesh> glyphs = s_clonedGlyphs ?? (s_clonedGlyphs = new List<TextMesh>());
            label.GetComponentsInChildren(true, glyphs);
            foreach (TextMesh text in glyphs)
            {
                Renderer renderer = text.GetComponent<Renderer>();
                if (renderer == null || renderer == current || text.GetComponentInParent<UberText>() != label || !renderer.enabled) continue;
                renderer.enabled = false; removed++;
            }
            glyphs.Clear(); return removed;
        }
        public static string Decorate(string text, int fontSize, bool hideSeparators = true)
        {
            int half = Math.Max(1, fontSize / 2);
            string plus = "<size=" + half.ToString(System.Globalization.CultureInfo.InvariantCulture) + ">+</size>";
            if (hideSeparators) plus = "<color=#00000000>" + plus + "</color>";
            return text.Replace("+", plus);
        }

        internal static Renderer TextureSurface(UberText label)
        {
            object texture = s_texture?.GetValue(label);
            return label.RenderOnObject != null ? label.RenderOnObject.GetComponent<Renderer>()
                : texture == null ? null : s_plane?.GetValue(texture) as Renderer;
        }

        public static Renderer[] VisibleRenderers(UberText label)
        {
            if (label.RenderToTexture)
            {
                Renderer surface = TextureSurface(label);
                return surface != null && surface.enabled && surface.gameObject.activeInHierarchy
                    ? new[] { surface } : Array.Empty<Renderer>();
            }
            var renderers = new List<Renderer>();
            foreach (Renderer renderer in label.GetComponentsInChildren<Renderer>(false))
                if (renderer.enabled) renderers.Add(renderer);
            return renderers.ToArray();
        }

        internal static void VisibleRenderers(UberText label, List<Renderer> result)
        {
            result.Clear();
            if (label.RenderToTexture)
            {
                Renderer surface = TextureSurface(label);
                if (surface != null && surface.enabled && surface.gameObject.activeInHierarchy) result.Add(surface);
                return;
            }
            label.GetComponentsInChildren(false, result);
            for (int i = result.Count - 1; i >= 0; i--) if (!result[i].enabled) result.RemoveAt(i);
        }

        public static bool TryGlyphRange(UberText label, out float left, out float right)
        {
            left = right = 0f;
            Bounds ink = label.GetTextWorldSpaceBounds();
            if (!(ink.size.x > 0f) || float.IsInfinity(ink.size.x)) return false;
            if (!label.RenderToTexture)
            {
                left = ink.min.x; right = ink.max.x;
                return true;
            }

            object texture = s_texture?.GetValue(label);
            if (texture == null) return false;
            Renderer surface = label.RenderOnObject != null ? label.RenderOnObject.GetComponent<Renderer>()
                : s_plane?.GetValue(texture) as Renderer;
            if (texture == null || surface == null || !(surface.bounds.size.x > 0f)
                || !(s_view?.GetValue(texture) is Matrix4x4 view)
                || !(s_projection?.GetValue(texture) is Matrix4x4 projection)) return false;

            // The raw mesh lives offscreen. Map its ink through the same camera
            // matrices and output surface that the native text renderer uses.
            // This also retains glyph bearings, alignment and widget offsets.
            Matrix4x4 toTexture = projection * view;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            Vector3 lo = ink.min, hi = ink.max;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        float projected = toTexture.MultiplyPoint3x4(new Vector3(
                            x == 0 ? lo.x : hi.x, y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z)).x;
                        min = Mathf.Min(min, projected); max = Mathf.Max(max, projected);
                    }
            Bounds output = surface.bounds;
            left = output.min.x + (min + 1f) * 0.5f * output.size.x;
            right = output.min.x + (max + 1f) * 0.5f * output.size.x;
            return right > left && !float.IsInfinity(right - left);
        }

        public static bool TryScreenInkRect(UberText label, Camera camera, out Rect rect)
        {
            Bounds ink = label.GetTextWorldSpaceBounds();
            rect = default(Rect);
            if (!(ink.size.x > 0f)) return false;
            if (!label.RenderToTexture)
            {
                rect = ScreenRect(ink, camera);
                return rect.width > 0f && rect.height > 0f;
            }
            object texture = s_texture?.GetValue(label);
            if (texture == null) return false;
            Renderer surface = label.RenderOnObject != null ? label.RenderOnObject.GetComponent<Renderer>()
                : s_plane?.GetValue(texture) as Renderer;
            if (surface == null || !(s_view?.GetValue(texture) is Matrix4x4 view)
                || !(s_projection?.GetValue(texture) is Matrix4x4 projection)) return false;
            Rect clip = ProjectRect(ink, projection * view);
            Rect output = ScreenRect(surface.bounds, camera);
            rect = Rect.MinMaxRect(output.xMin + (clip.xMin + 1f) * 0.5f * output.width,
                output.yMin + (clip.yMin + 1f) * 0.5f * output.height,
                output.xMin + (clip.xMax + 1f) * 0.5f * output.width,
                output.yMin + (clip.yMax + 1f) * 0.5f * output.height);
            return rect.width > 0f && rect.height > 0f;
        }

        private static Rect ProjectRect(Bounds bounds, Matrix4x4 matrix)
            => RectOf(bounds, p => matrix.MultiplyPoint3x4(p));

        private static Rect ScreenRect(Bounds bounds, Camera camera)
            => RectOf(bounds, camera.WorldToScreenPoint);

        private static Rect RectOf(Bounds bounds, Func<Vector3, Vector3> project)
        {
            Vector3 lo = bounds.min, hi = bounds.max;
            float left = float.PositiveInfinity, bottom = float.PositiveInfinity;
            float right = float.NegativeInfinity, top = float.NegativeInfinity;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 p = project(new Vector3(x == 0 ? lo.x : hi.x,
                            y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z));
                        left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x);
                        bottom = Mathf.Min(bottom, p.y); top = Mathf.Max(top, p.y);
                    }
            return Rect.MinMaxRect(left, bottom, right, top);
        }

        public static bool TryFrameRange(GameObject frame, out float left, out float right)
        {
            left = float.PositiveInfinity; right = float.NegativeInfinity;
            if (frame == null) return false;
            Renderer face = frame.GetComponent<Renderer>();
            if (face != null && face.enabled && face.bounds.size.x > 0f)
            {
                left = face.bounds.min.x; right = face.bounds.max.x;
                return true;
            }
            foreach (Renderer renderer in frame.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled || !(renderer.bounds.size.x > 0f)) continue;
                left = Mathf.Min(left, renderer.bounds.min.x);
                right = Mathf.Max(right, renderer.bounds.max.x);
            }
            return right > left;
        }
    }

    // Native text geometry can outlive a culled row outside its transform tree.
    // Hide the renderer itself when its row disappears, including during reflow.
    internal sealed class FilterCaptionLifetime : MonoBehaviour
    {
        private SetFilterItem m_row;
        private UberText m_retired, m_caption;
        private Renderer m_oldSurface;
        private readonly List<Renderer> m_retiredGlyphs = new List<Renderer>();
        internal void Initialize(SetFilterItem row, UberText retired, UberText caption, Renderer oldSurface)
        {
            m_row = row; m_retired = retired; m_caption = caption; m_oldSurface = oldSurface;
            foreach (TextMesh glyph in row.GetComponentsInChildren<TextMesh>(true))
                if (!glyph.transform.IsChildOf(caption.transform))
                {
                    Renderer renderer = glyph.GetComponent<Renderer>();
                    if (renderer != null) m_retiredGlyphs.Add(renderer);
                }
            Retire();
        }
        private void Retire()
        {
            if (m_retired != null)
            {
                m_retired.enabled = false;
                Renderer old = CounterTextLayout.GlyphRenderer(m_retired);
                if (old != null) old.enabled = false;
            }
            if (m_oldSurface != null) m_oldSurface.enabled = false;
            foreach (Renderer glyph in m_retiredGlyphs)
                if (glyph != null) glyph.enabled = false;
        }
        private void OnDisable()
        {
            Retire();
            if (m_caption == null) return;
            m_caption.Hide();
            Renderer glyph = CounterTextLayout.GlyphRenderer(m_caption);
            if (glyph != null) glyph.enabled = false;
        }
        private void OnEnable()
        {
            Retire();
            if (m_caption != null) m_caption.Show();
        }
        private void LateUpdate()
        {
            Retire();
            if (m_caption == null) return;
            if (m_caption.isHidden()) m_caption.Show();
            KeywordSetTray.AlignVisibleCaption(m_row);
        }
    }
}
