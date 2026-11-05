using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed partial class ModsSettingsMenu
    {
        private sealed class SettingsScrollArt
        {
            public Mesh Source, Private;
            public MeshFilter Filter;
            public Matrix4x4 ToTrack;
            public Bounds Bounds;
            public bool Thumb;
            public Vector3[] Points;
        }

        private GameObject m_settingsScrollbar;
        private readonly List<SettingsScrollArt> m_settingsScrollArt = new List<SettingsScrollArt>();
        private readonly List<Mesh> m_settingsSourceMeshes = new List<Mesh>();
        private readonly List<Material> m_settingsScrollMaterials = new List<Material>();
        private Bounds m_settingsRailArt, m_settingsGripArt;
        private Rect m_settingsTrackRect, m_settingsThumbRect;
        private bool m_settingsScrollbarDragging;
        private float m_settingsThumbGrab, m_settingsScrollbarRetry, m_settingsArtHeight, m_settingsRailAxisX, m_settingsGripAxisX;
        private int m_settingsScrollbarReleaseFrame = -1;

        private void LayoutSettingsScrollbar()
        {
            if (m_settingsScrollRange <= 0f)
            {
                if (m_settingsScrollbar != null) m_settingsScrollbar.SetActive(false);
                StopSettingsScrollbarDrag();
                return;
            }
            if (m_settingsScrollbar == null && !CreateSettingsScrollbar()) return;
            m_settingsScrollbar.SetActive(true);
            float unit = m_settingsScrollStep / 1.5f;
            float width = unit * 1.05f;
            float nativeWidth = m_settingsGripArt.size.x;
            float scale = width / nativeWidth;
            float right = m_settingsViewport.xMax - unit * 0.04f;
            m_settingsTrackRect = Rect.MinMaxRect(right - width, m_settingsViewport.yMin + unit * 0.12f,
                right, m_settingsViewport.yMax - unit * 0.12f);
            // Hearthstone's grip keeps its original proportions, independently
            // of the content length. Only the middle of the rail stretches.
            float gripScale = Mathf.Min(scale, m_settingsTrackRect.height * 0.9f / m_settingsGripArt.size.z);
            float gripHeight = m_settingsGripArt.size.z * gripScale;
            float travel = m_settingsTrackRect.height - gripHeight;
            float top = m_settingsTrackRect.yMax - travel * (m_settingsScroll / m_settingsScrollRange);
            m_settingsThumbRect = Rect.MinMaxRect(m_settingsTrackRect.center.x + (m_settingsGripArt.center.x - m_settingsGripAxisX) * gripScale - m_settingsGripArt.size.x * gripScale * 0.5f,
                top - gripHeight, m_settingsTrackRect.center.x + (m_settingsGripArt.center.x - m_settingsGripAxisX) * gripScale + m_settingsGripArt.size.x * gripScale * 0.5f, top);
            DrawSettingsScrollbar(scale, gripScale);
        }

        private bool CreateSettingsScrollbar()
        {
            if (NativeScrollbarArt.CaptureAvailable(out var captured, out Bounds railArt, out Bounds gripArt, requireRail: true))
            { CopySettingsScrollbar(captured, railArt, gripArt); return true; }
            m_settingsScrollbarRetry = Time.unscaledTime + 1f;
            return false;
        }

        private void CopySettingsScrollbar(List<NativeScrollbarArt.Part> captured, Bounds railArt, Bounds gripArt)
        {
            var parts = new List<SettingsScrollArt>(); var renderers = new List<Renderer>();
            foreach (var part in captured)
            {
                parts.Add(new SettingsScrollArt { Source = part.Mesh, ToTrack = part.ToTrack, Bounds = part.Bounds, Thumb = part.Grip });
                renderers.Add(part.Renderer);
                if (part.Owned) m_settingsSourceMeshes.Add(part.Mesh);
            }
            m_settingsRailAxisX = SettingsScrollbarAxis(captured, false, railArt.center.x);
            m_settingsGripAxisX = SettingsScrollbarAxis(captured, true, gripArt.center.x);
            m_settingsRailArt = railArt;
            m_settingsGripArt = gripArt;
            m_settingsScrollbar = new GameObject("BCM_SettingsScrollbar");
            m_settingsScrollbar.transform.SetParent(m_menu.m_buttonContainer.transform, false);
            m_settingsScrollbar.layer = m_menu.m_buttonContainer.gameObject.layer;
            int queue = 0, order = int.MinValue, layer = 0;
            foreach (Renderer background in ComboSceneGeometry.VisibleRenderers(m_menu.gameObject))
                if (background.GetComponentInParent<UberText>() == null)
                {
                    if (background.sortingOrder >= order) { order = background.sortingOrder; layer = background.sortingLayerID; }
                    foreach (Material material in background.sharedMaterials)
                        if (material != null) queue = System.Math.Max(queue, material.renderQueue);
                }
            for (int i = 0; i < parts.Count; i++)
            {
                Renderer original = renderers[i];
                var go = new GameObject(original.gameObject.name);
                go.layer = m_settingsScrollbar.layer;
                go.transform.SetParent(m_settingsScrollbar.transform, false);
                parts[i].Filter = go.AddComponent<MeshFilter>();
                var copy = go.AddComponent<MeshRenderer>();
                Material[] materials = (Material[])original.sharedMaterials.Clone();
                for (int j = 0; j < materials.Length; j++)
                    if (materials[j] != null)
                    {
                        materials[j] = new Material(materials[j]);
                        materials[j].renderQueue = System.Math.Max(materials[j].renderQueue, System.Math.Min(5000, queue + 1));
                        m_settingsScrollMaterials.Add(materials[j]);
                    }
                copy.sharedMaterials = materials;
                copy.sortingLayerID = order == int.MinValue ? original.sortingLayerID : layer;
                copy.sortingOrder = order == int.MinValue ? original.sortingOrder : order + 1;
                var properties = new MaterialPropertyBlock();
                original.GetPropertyBlock(properties);
                copy.SetPropertyBlock(properties);
                m_settingsScrollArt.Add(parts[i]);
            }
        }

        private static float SettingsScrollbarAxis(List<NativeScrollbarArt.Part> parts, bool grip, float fallback)
        {
            // Native hit bones, shadows and surrounding tray art can have
            // different centres. Align the principal rail and grip artwork;
            // retain each shadow's original offset from its own artwork.
            var bounds = new Dictionary<Renderer, Bounds>();
            foreach (var part in parts)
            {
                if (part.Grip != grip) continue;
                string name = (part.Renderer.gameObject.name + " " + part.Mesh.name).ToLowerInvariant();
                if (name.Contains("shadow") || name.Contains("glow") || name.Contains("highlight")) continue;
                if (bounds.TryGetValue(part.Renderer, out Bounds area)) area.Encapsulate(part.Bounds);
                else area = part.Bounds;
                bounds[part.Renderer] = area;
            }
            float height = 0f, axis = fallback;
            bool namedArtwork = false;
            foreach (var entry in bounds)
            {
                string name = entry.Key.gameObject.name.ToLowerInvariant();
                bool named = grip ? name.Contains("thumb") || name.Contains("grip") || name.Contains("slider")
                    : name.Contains("scrollbar") || name.Contains("rail") || name.Contains("track");
                // A narrow piece of the surrounding deck frame can be taller
                // than the actual ScrollBar renderer in a native tray prefab.
                if ((named && !namedArtwork) || (named == namedArtwork && entry.Value.size.z > height))
                { height = entry.Value.size.z; axis = entry.Value.center.x; namedArtwork = named; }
            }
            return axis;
        }

        private void DrawSettingsScrollbar(float scale, float gripScale)
        {
            float depth = m_settingsCamera.WorldToScreenPoint(m_menu.m_buttonContainer.transform.position).z;
            Vector3 world = m_settingsCamera.ScreenToWorldPoint(new Vector3(0f, 0f, depth));
            float worldScale = (m_settingsCamera.ScreenToWorldPoint(new Vector3(scale, 0f, depth)) - world).magnitude;
            var target = new Bounds(m_settingsRailArt.center,
                new Vector3(m_settingsRailArt.size.x, m_settingsRailArt.size.y, m_settingsTrackRect.height / scale));
            var axes = new FilterTrayGeometry.Axes(m_settingsRailArt, target, m_settingsRailArt.size.x);
            foreach (SettingsScrollArt art in m_settingsScrollArt)
            {
                if (art.Private == null || m_settingsArtHeight != target.size.z)
                {
                    if (art.Private != null) UnityEngine.Object.Destroy(art.Private);
                    // Preserve the native atlas UVs, vertex tint and submeshes.
                    // End caps translate; the grip never stretches with the rail.
                    var partAxes = art.Thumb
                        ? new FilterTrayGeometry.Axes(m_settingsGripArt, m_settingsGripArt, 0f) : axes;
                    art.Private = FilterTrayGeometry.ResizeMesh(art.Source, art.ToTrack, Matrix4x4.identity,
                        partAxes, art.Bounds.center, false, !art.Thumb && art.Bounds.size.z > m_settingsRailArt.size.z * 0.5f);
                    art.Filter.sharedMesh = art.Private;
                    art.Points = art.Private.vertices;
                }
                // Keep the native-space snapshot apart from the rendered mesh;
                // repeated scrolling never accumulates deformation.
                Mesh shaped = art.Private;
                Vector3[] vertices = (Vector3[])art.Points.Clone();
                Vector3 center = art.Thumb ? m_settingsGripArt.center : m_settingsRailArt.center;
                Rect destination = art.Thumb ? m_settingsThumbRect : m_settingsTrackRect;
                float partScale = art.Thumb ? gripScale : scale;
                var bounds = new Bounds();
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    vertices[i] = art.Filter.transform.InverseTransformPoint(m_settingsCamera.ScreenToWorldPoint(new Vector3(
                        m_settingsTrackRect.center.x + (v.x - (art.Thumb ? m_settingsGripAxisX : m_settingsRailAxisX)) * partScale,
                        destination.center.y + (v.z - center.z) * partScale,
                        depth - (v.y - m_settingsRailArt.center.y) * worldScale)));
                    if (i == 0) bounds = new Bounds(vertices[i], Vector3.zero); else bounds.Encapsulate(vertices[i]);
                }
                shaped.vertices = vertices;
                shaped.bounds = bounds;
            }
            m_settingsArtHeight = target.size.z;
            LayerUtils.SetLayer(m_settingsScrollbar.transform, GameLayer.HighPriorityUI);
            // NineSliceElement stores its artwork on child slice objects.
            // Its root has no Renderer in the installed client.
            foreach (Renderer background in m_menu.m_background.GetComponentsInChildren<Renderer>(false))
                if (background.enabled && background.GetComponentInParent<UberText>() == null
                    && !background.transform.IsChildOf(m_settingsScrollbar.transform))
                    ComboSceneGeometry.InFrontOf(m_settingsScrollbar, background, m_settingsCamera);
        }

        private bool PollSettingsScrollbar()
        {
            if (m_settingsScrollbar == null || !m_settingsScrollbar.activeInHierarchy) return false;
            bool down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0), up = Input.GetMouseButtonUp(0);
            Vector3 pointer = Input.mousePosition;
            if (down && m_settingsTrackRect.Contains(pointer))
            {
                m_settingsScrollbarDragging = true;
                m_settingsThumbGrab = m_settingsThumbRect.height * 0.5f;
            }
            if (!m_settingsScrollbarDragging) return false;
            if (down || held || up)
            {
                ScrollSettingsToPointer(pointer);
            }
            if (up || (!held && !down))
            {
                m_settingsScrollbarDragging = false;
                m_settingsScrollbarReleaseFrame = Time.frameCount;
                LayoutSettingsScrollbar();
            }
            return true;
        }

        private void ScrollSettingsToPointer(Vector3 pointer)
        {
            float travel = m_settingsTrackRect.height - m_settingsThumbRect.height;
            float fraction = travel > 0f ? (m_settingsTrackRect.yMax - pointer.y - m_settingsThumbGrab) / travel : 0f;
            m_settingsScroll = Mathf.Clamp01(fraction) * m_settingsScrollRange;
            ApplySettingsScroll();
        }

        private bool ConsumeSettingsScrollbarRelease()
        {
            if (m_settingsScrollRange <= 0f || m_settingsScrollbar == null || !m_settingsScrollbar.activeInHierarchy) return false;
            foreach (DropdownControl control in m_controls) if (control.isMenuShown()) return false;
            if (m_settingsScrollbarDragging)
            {
                m_settingsScrollbarDragging = false;
                m_settingsScrollbarReleaseFrame = Time.frameCount;
                ScrollSettingsToPointer(Input.mousePosition);
                return true;
            }
            return m_settingsScrollbarReleaseFrame == Time.frameCount || m_settingsTrackRect.Contains(Input.mousePosition);
        }

        private void StopSettingsScrollbarDrag()
        {
            m_settingsScrollbarDragging = false;
            m_settingsScrollbarReleaseFrame = -1;
        }

        private void DestroySettingsScrollbar()
        {
            StopSettingsScrollbarDrag();
            if (m_settingsScrollbar != null) UnityEngine.Object.Destroy(m_settingsScrollbar);
            foreach (SettingsScrollArt art in m_settingsScrollArt)
                if (art.Private != null) UnityEngine.Object.Destroy(art.Private);
            foreach (Mesh mesh in m_settingsSourceMeshes) UnityEngine.Object.Destroy(mesh);
            m_settingsSourceMeshes.Clear();
            m_settingsScrollArt.Clear();
            foreach (Material material in m_settingsScrollMaterials) UnityEngine.Object.Destroy(material);
            m_settingsScrollMaterials.Clear();
            m_settingsArtHeight = 0f;
            m_settingsScrollbar = null;
        }
    }
}
