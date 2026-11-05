using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    // Clone only the native rail/caps/grip artwork. Input belongs to this modal;
    // collection scrollbar controllers and their callbacks remain untouched.
    internal sealed class ComboMemberScrollbar : IDisposable
    {
        private sealed class Part
        {
            internal Mesh Source, Private;
            internal MeshFilter Filter;
            internal Matrix4x4 ToTrack;
            internal Bounds Bounds;
            internal bool Grip;
            internal Vector3[] Points;
        }
        private readonly Transform m_parent;
        private readonly List<Part> m_parts = new List<Part>();
        private readonly List<Mesh> m_sourceMeshes = new List<Mesh>();
        private readonly List<Material> m_materials = new List<Material>();
        private Bounds m_rail, m_grip;
        private float m_height, m_grab, m_retry, m_axisX;
        private bool m_dragging;
        private int m_range, m_releaseFrame = -1;
        internal GameObject Root { get; private set; }
        internal Rect Track { get; private set; }
        internal Rect Thumb { get; private set; }
        internal bool Retry => Root == null && Time.unscaledTime >= m_retry;
        internal ComboMemberScrollbar(Transform parent) { m_parent = parent; }

        internal void Layout(Camera camera, GameObject frame, Rect area, int first, int count, int visible)
        {
            m_range = Math.Max(0, count - visible);
            if (m_range == 0) { Hide(); return; }
            if (Root == null && (Time.unscaledTime < m_retry || !Create())) return;
            Root.SetActive(true); Track = area;
            float scale = area.width / m_grip.size.x;
            float gripScale = Math.Min(scale, area.height * .8f / m_grip.size.z);
            float gripHeight = m_grip.size.z * gripScale;
            float top = area.yMax - (area.height - gripHeight) * first / m_range;
            Thumb = new Rect(area.center.x + (m_grip.center.x - m_axisX) * gripScale - m_grip.size.x * gripScale * .5f, top - gripHeight, m_grip.size.x * gripScale, gripHeight);
            var target = new Bounds(m_rail.center, new Vector3(m_rail.size.x, m_rail.size.y, area.height / scale));
            var axes = new FilterTrayGeometry.Axes(m_rail, target, m_rail.size.x);
            float depth = camera.WorldToScreenPoint(m_parent.position).z;
            float worldScale = (camera.ScreenToWorldPoint(new Vector3(scale, 0, depth)) - camera.ScreenToWorldPoint(new Vector3(0, 0, depth))).magnitude;
            foreach (Part part in m_parts)
            {
                if (part.Private == null || m_height != target.size.z)
                {
                    if (part.Private != null) UnityEngine.Object.Destroy(part.Private);
                    part.Private = FilterTrayGeometry.ResizeMesh(part.Source, part.ToTrack, Matrix4x4.identity,
                        part.Grip ? new FilterTrayGeometry.Axes(m_grip, m_grip, 0f) : axes,
                        part.Bounds.center, false, !part.Grip && part.Bounds.size.z > m_rail.size.z * .5f);
                    part.Filter.sharedMesh = part.Private; part.Points = part.Private.vertices;
                }
                Vector3[] vertices = (Vector3[])part.Points.Clone();
                Vector3 center = part.Grip ? m_grip.center : m_rail.center;
                Rect destination = part.Grip ? Thumb : Track;
                float partScale = part.Grip ? gripScale : scale;
                var bounds = new Bounds();
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    vertices[i] = part.Filter.transform.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(
                        Track.center.x + (v.x - m_axisX) * partScale,
                        destination.center.y + (v.z - center.z) * partScale,
                        depth - (v.y - m_rail.center.y) * worldScale)));
                    if (i == 0) bounds = new Bounds(vertices[i], Vector3.zero); else bounds.Encapsulate(vertices[i]);
                }
                part.Private.vertices = vertices; part.Private.bounds = bounds;
            }
            m_height = target.size.z;
            int queue = 0, order = int.MinValue, layer = 0;
            foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(frame))
            {
                if (renderer.transform.IsChildOf(Root.transform)) continue;
                if (renderer.GetComponentInParent<UberText>() != null) continue;
                if (renderer.sortingOrder >= order) { order = renderer.sortingOrder; layer = renderer.sortingLayerID; }
                foreach (Material material in renderer.sharedMaterials) if (material != null) queue = Math.Max(queue, material.renderQueue);
            }
            foreach (Material material in m_materials) material.renderQueue = Math.Max(material.renderQueue, Math.Min(5000, queue + 1));
            if (order != int.MinValue)
                foreach (Renderer renderer in Root.GetComponentsInChildren<Renderer>(true))
                { renderer.sortingLayerID = layer; renderer.sortingOrder = order + 1; }
            LayerUtils.SetLayer(Root.transform, GameLayer.HighPriorityUI);
            ComboSceneGeometry.InFrontOf(Root, frame, camera);
        }
        private bool Create()
        {
            if (NativeScrollbarArt.CaptureAvailable(out var captured, out Bounds railArt, out Bounds gripArt))
            { Copy(captured, railArt, gripArt); return true; }
            m_retry = Time.unscaledTime + 1f; return false;
        }
        private void Copy(List<NativeScrollbarArt.Part> captured, Bounds railArt, Bounds gripArt)
        {
            m_axisX = captured[0].AxisX;
            var parts = new List<Part>(); var renderers = new List<Renderer>();
            foreach (var part in captured)
            {
                parts.Add(new Part { Source = part.Mesh, ToTrack = part.ToTrack, Bounds = part.Bounds, Grip = part.Grip });
                renderers.Add(part.Renderer);
                if (part.Owned) m_sourceMeshes.Add(part.Mesh);
            }
            m_rail = railArt; m_grip = gripArt;
            Root = new GameObject("BCM_ComboMemberScrollbar"); Root.transform.SetParent(m_parent, false);
            for (int i = 0; i < parts.Count; i++)
            {
                Renderer original = renderers[i];
                var go = new GameObject(original.gameObject.name); go.transform.SetParent(Root.transform, false);
                parts[i].Filter = go.AddComponent<MeshFilter>();
                var copy = go.AddComponent<MeshRenderer>(); Material[] materials = (Material[])original.sharedMaterials.Clone();
                for (int j = 0; j < materials.Length; j++) if (materials[j] != null)
                { materials[j] = new Material(materials[j]); m_materials.Add(materials[j]); }
                copy.sharedMaterials = materials; copy.sortingLayerID = original.sortingLayerID; copy.sortingOrder = original.sortingOrder;
                var properties = new MaterialPropertyBlock(); original.GetPropertyBlock(properties); copy.SetPropertyBlock(properties);
                m_parts.Add(parts[i]);
            }
        }
        internal bool Poll(ref int first)
        {
            if (Root == null || !Root.activeInHierarchy) return false;
            bool down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0), up = Input.GetMouseButtonUp(0);
            if (down && Track.Contains(Input.mousePosition))
            {
                m_dragging = true;
                m_grab = Thumb.Contains(Input.mousePosition) ? Thumb.yMax - Input.mousePosition.y : Thumb.height * .5f;
            }
            if (!m_dragging) return false;
            if (down || held || up)
            {
                float travel = Track.height - Thumb.height;
                first = (int)Math.Round(Mathf.Clamp01(travel > 0 ? (Track.yMax - Input.mousePosition.y - m_grab) / travel : 0) * m_range);
            }
            if (up || (!held && !down)) { m_dragging = false; m_releaseFrame = Time.frameCount; }
            return true;
        }
        internal bool ConsumeRelease(Vector3 pointer)
        {
            if (Root == null || !Root.activeInHierarchy) return false;
            if (m_dragging) { m_dragging = false; m_releaseFrame = Time.frameCount; return true; }
            return m_releaseFrame == Time.frameCount || Track.Contains(pointer);
        }
        internal void Hide() { Root?.SetActive(false); m_dragging = false; m_releaseFrame = -1; }
        public void Dispose()
        {
            Hide(); if (Root != null) UnityEngine.Object.Destroy(Root);
            foreach (Part part in m_parts) if (part.Private != null) UnityEngine.Object.Destroy(part.Private);
            foreach (Material material in m_materials) UnityEngine.Object.Destroy(material);
            foreach (Mesh mesh in m_sourceMeshes) UnityEngine.Object.Destroy(mesh);
            m_sourceMeshes.Clear(); m_parts.Clear(); m_materials.Clear(); Root = null;
        }
    }
}
