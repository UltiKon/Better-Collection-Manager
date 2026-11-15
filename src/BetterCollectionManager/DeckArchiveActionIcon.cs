using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BetterCollectionManager
{
    // A small tray with an in/out arrow, using the native icon's material,
    // transform and footprint. No font glyph or external texture is required.
    internal sealed class DeckArchiveActionIcon : IDisposable
    {
        private static readonly Color IconColor = new Color(.35f, .75f, 1f, 1f);
        private readonly GameObject m_root;
        private readonly Mesh m_mesh;
        private readonly Material m_material;
        private readonly Texture2D m_texture;
        private readonly Bounds m_bounds;
        private readonly int m_horizontal, m_vertical;
        private readonly float m_xSign = 1f, m_ySign = 1f;
        private bool? m_restore;
        internal DeckArchiveActionIcon(GameObject template)
        {
            MeshFilter source = template.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null);
            Renderer renderer = source?.GetComponent<Renderer>();
            if (source == null || renderer?.sharedMaterial == null) throw new InvalidOperationException("Native popup icon artwork is unavailable.");
            m_bounds = source.sharedMesh.bounds;
            int[] axes = Enumerable.Range(0, 3).OrderByDescending(i => m_bounds.size[i]).Take(2).OrderBy(i => i).ToArray();
            m_horizontal = axes[0]; m_vertical = axes[1];
            Camera camera = CameraUtils.FindProjectionCameraForObject(source.gameObject);
            if (camera != null)
            {
                Vector3 center = camera.WorldToScreenPoint(source.transform.TransformPoint(m_bounds.center));
                Vector3 Delta(int axis) { Vector3 p = m_bounds.center; p[axis] += 1f; return camera.WorldToScreenPoint(source.transform.TransformPoint(p)) - center; }
                if (Math.Abs(Delta(m_vertical).x) > Math.Abs(Delta(m_horizontal).x))
                { int swap = m_horizontal; m_horizontal = m_vertical; m_vertical = swap; }
                m_xSign = Delta(m_horizontal).x < 0 ? -1f : 1f;
                m_ySign = Delta(m_vertical).y < 0 ? -1f : 1f;
            }
            m_root = new GameObject("BCM_ArchiveTrayIcon");
            m_root.layer = source.gameObject.layer;
            m_root.transform.SetParent(source.transform.parent, false);
            m_root.transform.localPosition = source.transform.localPosition;
            m_root.transform.localRotation = source.transform.localRotation;
            m_root.transform.localScale = source.transform.localScale;
            m_mesh = new Mesh { name = "BCM_ArchiveTrayGlyph" };
            m_material = new Material(renderer.sharedMaterial) { name = "BCM_ArchiveIconBlue" };
            // Supply the color in the artwork itself: a native icon shader
            // need not use any of the conventional material tint properties.
            m_texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "BCM_ArchiveIconBlue" };
            m_texture.SetPixels(new[] { IconColor });
            m_texture.Apply(false, true);
            foreach (string property in new[] { "_MainTex", "_BaseMap", "_DiffuseTex" })
                if (m_material.HasProperty(property)) m_material.SetTexture(property, m_texture);
            foreach (string property in new[] { "_Color", "_TintColor", "_BaseColor" })
                if (m_material.HasProperty(property)) m_material.SetColor(property, Color.white);
            m_root.AddComponent<MeshFilter>().sharedMesh = m_mesh;
            var output = m_root.AddComponent<MeshRenderer>();
            output.sharedMaterial = m_material; output.sortingLayerID = renderer.sortingLayerID; output.sortingOrder = renderer.sortingOrder;
            SetRestore(false);
        }
        internal void SetRestore(bool restore)
        {
            if (m_restore == restore) return;
            m_restore = restore;
            var vertices = new List<Vector3>();
            float side = Math.Min(m_bounds.size[m_horizontal], m_bounds.size[m_vertical]);
            Vector3 Point(float x, float y)
            {
                Vector3 point = m_bounds.center;
                point[m_horizontal] += (x - .5f) * side * m_xSign;
                point[m_vertical] += (y - .5f) * side * m_ySign;
                return point;
            }
            void Triangle(float x1, float y1, float x2, float y2, float x3, float y3)
            { vertices.Add(Point(x1, y1)); vertices.Add(Point(x2, y2)); vertices.Add(Point(x3, y3)); }
            void Bar(float left, float bottom, float right, float top)
            { Triangle(left, bottom, right, bottom, right, top); Triangle(left, bottom, right, top, left, top); }
            Bar(.08f, .08f, .92f, .21f); Bar(.08f, .08f, .21f, .52f); Bar(.79f, .08f, .92f, .52f);
            if (restore)
            { Bar(.43f, .30f, .57f, .73f); Triangle(.24f, .66f, .76f, .66f, .50f, .96f); }
            else
            { Bar(.43f, .56f, .57f, .96f); Triangle(.24f, .63f, .76f, .63f, .50f, .32f); }
            var triangles = new List<int>();
            for (int i = 0; i < vertices.Count; i += 3)
            { triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2); triangles.Add(i + 2); triangles.Add(i + 1); triangles.Add(i); }
            m_mesh.Clear(); m_mesh.vertices = vertices.ToArray(); m_mesh.triangles = triangles.ToArray();
            m_mesh.uv = Enumerable.Repeat(new Vector2(.5f, .5f), vertices.Count).ToArray();
            m_mesh.colors = Enumerable.Repeat(IconColor, vertices.Count).ToArray();
            m_mesh.RecalculateNormals(); m_mesh.RecalculateBounds();
        }
        public void Dispose()
        {
            if (m_root != null) UnityEngine.Object.Destroy(m_root);
            if (m_mesh != null) UnityEngine.Object.Destroy(m_mesh);
            if (m_material != null) UnityEngine.Object.Destroy(m_material);
            if (m_texture != null) UnityEngine.Object.Destroy(m_texture);
        }
    }
}
