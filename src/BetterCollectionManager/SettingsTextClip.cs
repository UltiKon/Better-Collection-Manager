using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterCollectionManager
{
    internal struct SettingsClipStamp
    {
        private Mesh m_mesh;
        private int m_vertices;
        private Bounds m_bounds;
        private Matrix4x4 m_model, m_view, m_projection;
        private Rect m_viewport, m_pixels;
        internal SettingsClipStamp(Mesh mesh, Transform transform, Camera camera, Rect viewport)
        {
            m_mesh = mesh; m_vertices = mesh != null ? mesh.vertexCount : 0; m_bounds = mesh != null ? mesh.bounds : default;
            m_model = transform.localToWorldMatrix; m_view = camera.worldToCameraMatrix; m_projection = camera.projectionMatrix; m_viewport = viewport; m_pixels = camera.pixelRect;
        }
        internal bool Matches(SettingsClipStamp other) => m_mesh == other.m_mesh && m_vertices == other.m_vertices
            && m_bounds.Equals(other.m_bounds) && m_model.Equals(other.m_model) && m_view.Equals(other.m_view)
            && m_projection.Equals(other.m_projection) && m_viewport.Equals(other.m_viewport) && m_pixels.Equals(other.m_pixels);
    }

    // Snapshot the existing glyph renderer with the UI camera's projection.
    // Never change UberText's mode, font layout, texture size or cached surfaces.
    internal sealed class SettingsTextClip : IDisposable
    {
        private static readonly FieldInfo View = typeof(UberTextRenderToTexture).GetField("viewMatrix", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Projection = typeof(UberTextRenderToTexture).GetField("projectionMatrix", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Target = typeof(UberTextRenderToTexture).GetField("m_renderTexture", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly CommandBuffer m_bold = new CommandBuffer();
        private readonly GameObject m_proxy;
        private readonly MeshRenderer m_surface;
        private readonly Mesh m_quad = new Mesh { name = "BCM_SettingsTextSnapshot" };
        private readonly UberTextRenderToTexture m_capture;
        private Mesh m_clipped;
        private Renderer m_source;
        private bool m_forceOff, m_applied;
        internal bool Seen;
        private SettingsClipStamp m_stamp;
        private Rect m_ink;
        private string m_text;
        private object m_typeface;
        private float m_font, m_character, m_boldSize;
        private Color m_color, m_upper, m_lower;
        private int m_queue, m_order, m_layer, m_materialSignature;
        private uint m_mask;
        private readonly List<Material> m_materials = new List<Material>();
        private readonly Vector3[] m_points = new Vector3[4];
        private static readonly List<Vector4> QuadUv = new List<Vector4> { new Vector4(0,0,0,0), new Vector4(1,0,0,0), new Vector4(0,1,0,0), new Vector4(1,1,0,0) };
        private static readonly List<int> QuadIndices = new List<int> { 3,1,2,2,1,0 };
        internal bool Owns(Renderer source) => m_applied && m_source == source;

        internal SettingsTextClip(Transform parent)
        {
            m_proxy = new GameObject("BCM_SettingsClippedText");
            m_proxy.transform.SetParent(parent, false); m_proxy.SetActive(false);
            m_proxy.AddComponent<MeshFilter>(); m_surface = m_proxy.AddComponent<MeshRenderer>();
            m_capture = new UberTextRenderToTexture(UberTextSetup.GetConfig().UberTextRenderTextureTracker);
        }
        internal void Restore()
        {
            if (m_applied && m_source != null) m_source.forceRenderingOff = m_forceOff;
            m_applied = false; m_proxy.SetActive(false);
        }
        internal void Apply(UberText label, MeshRenderer source, Camera camera, Rect ink, Rect viewport)
        {
            Seen = true;
            source.GetSharedMaterials(m_materials);
            int materialSignature = 17;
            foreach (Material material in m_materials)
                unchecked { materialSignature = materialSignature * 31 + (material == null ? 0 : material.GetHashCode() ^ material.renderQueue ^ (material.HasProperty("_Color") ? material.color.GetHashCode() : 0)
                    ^ (material.HasProperty("_MainTex") && material.mainTexture != null ? material.mainTexture.GetHashCode() : 0)); }
            var stamp = new SettingsClipStamp(source.GetComponent<MeshFilter>()?.sharedMesh, source.transform, camera, viewport);
            if (m_applied && m_source == source && m_stamp.Matches(stamp) && m_ink.Equals(ink) && m_text == label.Text
                && Equals(m_typeface, label.TrueTypeFont) && m_font == label.FontSize && m_character == label.CharacterSize && m_boldSize == label.BoldSize
                && m_color.Equals(label.TextColor) && m_upper.Equals(label.GradientUpperColor) && m_lower.Equals(label.GradientLowerColor)
                && m_queue == label.RenderQueue && m_order == source.sortingOrder && m_layer == source.sortingLayerID
                && m_mask == source.renderingLayerMask && m_materialSignature == materialSignature)
            { source.forceRenderingOff = true; m_proxy.SetActive(true); return; }
            Restore();
            // Integer pixel edges and a two-pixel guard preserve the glyph's
            // complete antialiased outline before cutting the output geometry.
            Rect area = Rect.MinMaxRect((float)Math.Floor(ink.xMin) - 2f, (float)Math.Floor(ink.yMin) - 2f,
                (float)Math.Ceiling(ink.xMax) + 2f, (float)Math.Ceiling(ink.yMax) + 2f);
            Rect pixels = camera.pixelRect;
            var crop = new Matrix4x4(new Vector4(pixels.width / area.width, 0, 0, 0),
                new Vector4(0, pixels.height / area.height, 0, 0), new Vector4(0, 0, 1, 0),
                new Vector4((2f * pixels.xMin + pixels.width - 2f * area.center.x) / area.width,
                    (2f * pixels.yMin + pixels.height - 2f * area.center.y) / area.height, 0, 1));
            m_capture.InitOnRenderObject(m_proxy, new Vector2Int((int)area.width * 2, (int)area.height * 2));
            m_capture.SetTargetClearColor(label.TextColor);
            View.SetValue(m_capture, camera.worldToCameraMatrix);
            Projection.SetValue(m_capture, crop * camera.projectionMatrix);
            m_capture.SetRenderQueueIncrement(label.RenderQueue);
            m_capture.DoRenderToTexture(source);
            // Native bold text draws the same glyph mesh again with its second
            // material. Retain those passes in the captured edge as well.
            if (m_materials.Count > 1)
            {
                m_bold.Clear(); m_bold.SetRenderTarget((RenderTexture)Target.GetValue(m_capture));
                m_bold.SetViewProjectionMatrices(camera.worldToCameraMatrix, crop * camera.projectionMatrix);
                for (int i = 1; i < m_materials.Count; i++)
                    if (m_materials[i] != null) m_bold.DrawRenderer(source, m_materials[i], 0, -1);
                Graphics.ExecuteCommandBuffer(m_bold);
            }
            float depth = camera.WorldToScreenPoint(source.bounds.center).z;
            Vector3 Point(float x, float y) => m_proxy.transform.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(x, y, depth)));
            m_points[0] = Point(area.xMin, area.yMin); m_points[1] = Point(area.xMax, area.yMin);
            m_points[2] = Point(area.xMin, area.yMax); m_points[3] = Point(area.xMax, area.yMax);
            m_quad.Clear(); m_quad.vertices = m_points;
            m_quad.SetUVs(0, QuadUv);
            m_quad.subMeshCount = 1; m_quad.SetTriangles(QuadIndices, 0, false); m_quad.RecalculateBounds();
            m_clipped = FilterTrayGeometry.ClipMesh(m_quad, m_proxy.transform, camera, viewport, m_clipped);
            m_proxy.GetComponent<MeshFilter>().sharedMesh = m_clipped;
            m_proxy.layer = source.gameObject.layer; m_surface.sortingLayerID = source.sortingLayerID;
            m_surface.sortingOrder = source.sortingOrder; m_surface.renderingLayerMask = source.renderingLayerMask;
            m_stamp = stamp; m_ink = ink; m_text = label.Text; m_typeface = label.TrueTypeFont; m_font = label.FontSize;
            m_character = label.CharacterSize; m_boldSize = label.BoldSize; m_color = label.TextColor;
            m_upper = label.GradientUpperColor; m_lower = label.GradientLowerColor; m_queue = label.RenderQueue;
            m_order = source.sortingOrder; m_layer = source.sortingLayerID; m_mask = source.renderingLayerMask; m_materialSignature = materialSignature;
            m_source = source; m_forceOff = source.forceRenderingOff; m_applied = true;
            source.forceRenderingOff = true; m_proxy.SetActive(true);
        }
        public void Dispose()
        {
            Restore(); m_capture.Destroy(); m_bold.Release(); UnityEngine.Object.Destroy(m_quad);
            if (m_clipped != null) UnityEngine.Object.Destroy(m_clipped);
            UnityEngine.Object.Destroy(m_proxy);
        }
    }
}
