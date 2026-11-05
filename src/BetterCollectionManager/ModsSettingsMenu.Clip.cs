using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed partial class ModsSettingsMenu
    {
        private sealed class SettingsScrollHit
        {
            internal BoxCollider Collider;
            internal Vector3 Center, Size;
            internal bool Enabled;
        }
        private static void RestoreSettingsHits(ScrollRow row)
        {
            foreach (SettingsScrollHit hit in row.Hits)
                if (hit.Collider != null)
                { hit.Collider.center = hit.Center; hit.Collider.size = hit.Size; hit.Collider.enabled = hit.Enabled; }
        }

        private void ClipSettingsHit(SettingsScrollHit hit)
        {
            BoxCollider box = hit.Collider;
            if (box == null || !hit.Enabled) return;
            Vector3 half = hit.Size * .5f;
            var points = new Vector3[8];
            Rect area = default; int index = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = m_settingsCamera.WorldToScreenPoint(box.transform.TransformPoint(hit.Center
                    + new Vector3(half.x * x, half.y * y, half.z * z)));
                points[index] = point;
                area = index++ == 0 ? new Rect(point.x, point.y, 0, 0) : Rect.MinMaxRect(
                    Mathf.Min(area.xMin, point.x), Mathf.Min(area.yMin, point.y),
                    Mathf.Max(area.xMax, point.x), Mathf.Max(area.yMax, point.y));
            }
            if (area.xMax <= m_settingsViewport.xMin || area.xMin >= m_settingsViewport.xMax
                || area.yMax <= m_settingsViewport.yMin || area.yMin >= m_settingsViewport.yMax)
            { box.enabled = false; return; }
            if (area.xMin >= m_settingsViewport.xMin && area.xMax <= m_settingsViewport.xMax
                && area.yMin >= m_settingsViewport.yMin && area.yMax <= m_settingsViewport.yMax) return;
            Bounds clipped = default;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = points[i];
                point.x = Mathf.Clamp(point.x, m_settingsViewport.xMin, m_settingsViewport.xMax);
                point.y = Mathf.Clamp(point.y, m_settingsViewport.yMin, m_settingsViewport.yMax);
                point = box.transform.InverseTransformPoint(m_settingsCamera.ScreenToWorldPoint(point));
                if (i == 0) clipped = new Bounds(point, Vector3.zero); else clipped.Encapsulate(point);
            }
            box.center = clipped.center; box.size = clipped.size;
        }

        private sealed class SettingsClipPart
        {
            internal Renderer Renderer;
            internal MeshFilter Filter;
            internal Mesh Source, Private, Baked;
            internal GameObject Proxy;
            internal MeshRenderer ProxyRenderer;
            internal bool Applied, ForceOff, Seen;
            internal SettingsClipStamp Stamp;
            internal void Restore()
            {
                if (!Applied) return;
                if (Filter != null && Filter.sharedMesh == Private) Filter.sharedMesh = Source;
                if (Renderer != null) Renderer.forceRenderingOff = ForceOff;
                if (Proxy != null) Proxy.SetActive(false);
                Applied = false;
            }
            internal void Dispose()
            {
                Restore();
                if (Private != null) UnityEngine.Object.Destroy(Private);
                if (Baked != null) UnityEngine.Object.Destroy(Baked);
                if (Proxy != null) UnityEngine.Object.Destroy(Proxy);
            }
        }
        private readonly Dictionary<Renderer, SettingsClipPart> m_settingsClipParts = new Dictionary<Renderer, SettingsClipPart>();
        private readonly Dictionary<UberText, SettingsTextClip> m_settingsClipTexts = new Dictionary<UberText, SettingsTextClip>();
        private bool InSettingsPopup(Transform node)
        {
            foreach (DropdownControl control in m_controls)
                if (node.IsChildOf(control.m_menu.transform) || node.IsChildOf(control.m_menuItemTemplate.transform)
                    || node.IsChildOf(control.m_cancelCatcher.transform)) return true;
            return false;
        }
        private bool OutsideSettings(Rect area) => area.xMin < m_settingsViewport.xMin || area.xMax > m_settingsViewport.xMax
            || area.yMin < m_settingsViewport.yMin || area.yMax > m_settingsViewport.yMax;

        private readonly HashSet<Renderer> m_clipRenderers = new HashSet<Renderer>();
        private readonly List<UberText> m_clipLabels = new List<UberText>();
        private readonly List<Renderer> m_clipRowRenderers = new List<Renderer>();
        private bool m_updatingSettings;
        private void UpdateSettingsClipping()
        {
            foreach (SettingsClipPart part in m_settingsClipParts.Values) part.Seen = false;
            if (m_settingsScrollRange <= 0f || m_settingsCamera == null) { StopSettingsClipping(); return; }
            foreach (SettingsTextClip text in m_settingsClipTexts.Values) text.Seen = false;
            foreach (ScrollRow row in m_scrollRows)
                if (row.Object.activeInHierarchy)
                {
                    row.Object.GetComponentsInChildren(false, m_clipLabels);
                    foreach (UberText label in m_clipLabels)
                    {
                        if (InSettingsPopup(label.transform)) continue;
                        CounterTextLayout.RemoveClonedGlyphs(label);
                        if (label.RenderToTexture || !CounterTextLayout.TryScreenInkRect(label, m_settingsCamera, out Rect ink)
                            || !OutsideSettings(ink)) continue;
                        var glyph = CounterTextLayout.GlyphRenderer(label) as MeshRenderer;
                        m_settingsClipTexts.TryGetValue(label, out SettingsTextClip text);
                        if (glyph == null || !glyph.enabled || (glyph.forceRenderingOff && !(text?.Owns(glyph) ?? false))) continue;
                        if (text == null)
                        { text = new SettingsTextClip(row.Object.transform); m_settingsClipTexts.Add(label, text); }
                        text.Apply(label, glyph, m_settingsCamera, ink, m_settingsViewport);
                    }
                }
            var renderers = m_clipRenderers; renderers.Clear();
            foreach (ScrollRow row in m_scrollRows)
                if (row.Object.activeInHierarchy)
                {
                    row.Object.GetComponentsInChildren(false, m_clipRowRenderers);
                    foreach (Renderer renderer in m_clipRowRenderers) renderers.Add(renderer);
                    row.Object.GetComponentsInChildren(false, m_clipLabels);
                    foreach (UberText label in m_clipLabels)
                        if (!InSettingsPopup(label.transform))
                        {
                            CounterTextLayout.VisibleRenderers(label, m_clipRowRenderers);
                            foreach (Renderer surface in m_clipRowRenderers) renderers.Add(surface);
                        }
                }
            foreach (Renderer renderer in renderers)
            {
                m_settingsClipParts.TryGetValue(renderer, out SettingsClipPart part);
                if (renderer == null || !renderer.enabled || (renderer.forceRenderingOff && !(part?.Applied ?? false)) || InSettingsPopup(renderer.transform)
                    || renderer.gameObject.name == "BCM_SettingsClippedSkin" || renderer.gameObject.name == "BCM_SettingsClippedText") continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = part != null && filter != null && filter.sharedMesh == part.Private ? part.Source : filter?.sharedMesh;
                Bounds world = mesh != null ? FilterTrayGeometry.TransformBounds(mesh.bounds, renderer.transform.TransformPoint) : renderer.bounds;
                if (!OutsideSettings(ComboSceneGeometry.ProjectBounds(world, m_settingsCamera))) continue;
                SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
                if (mesh == null && (skin == null || skin.sharedMesh == null)) continue;
                if (part == null)
                { part = new SettingsClipPart { Renderer = renderer, Filter = filter }; m_settingsClipParts.Add(renderer, part); }
                part.Seen = true;
                var stamp = new SettingsClipStamp(mesh, renderer.transform, m_settingsCamera, m_settingsViewport);
                if (skin == null && part.Applied && part.Stamp.Matches(stamp)) continue;
                part.Restore();
                if (skin != null)
                {
                    if (part.Baked == null) part.Baked = new Mesh();
                    skin.BakeMesh(part.Baked); mesh = part.Baked;
                    if (part.Proxy == null)
                    {
                        part.Proxy = new GameObject("BCM_SettingsClippedSkin"); part.Proxy.SetActive(false);
                        part.Proxy.transform.SetParent(skin.transform, false); part.Proxy.AddComponent<MeshFilter>();
                        part.ProxyRenderer = part.Proxy.AddComponent<MeshRenderer>();
                    }
                }
                if (mesh.vertexCount == 0 || mesh.subMeshCount == 0) continue;
                part.Source = mesh; part.Stamp = stamp;
                part.Private = FilterTrayGeometry.ClipMesh(mesh, renderer.transform, m_settingsCamera, m_settingsViewport, part.Private);
                part.ForceOff = renderer.forceRenderingOff; part.Applied = true;
                if (skin == null) filter.sharedMesh = part.Private;
                else
                {
                    part.Proxy.layer = renderer.gameObject.layer;
                    part.Proxy.GetComponent<MeshFilter>().sharedMesh = part.Private;
                    part.ProxyRenderer.sharedMaterials = renderer.sharedMaterials;
                    part.ProxyRenderer.sortingLayerID = renderer.sortingLayerID; part.ProxyRenderer.sortingOrder = renderer.sortingOrder;
                    part.ProxyRenderer.renderingLayerMask = renderer.renderingLayerMask;
                    var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties); part.ProxyRenderer.SetPropertyBlock(properties);
                    renderer.forceRenderingOff = true; part.Proxy.SetActive(true);
                }
            }
            foreach (SettingsClipPart part in m_settingsClipParts.Values) if (!part.Seen) part.Restore();
            foreach (SettingsTextClip text in m_settingsClipTexts.Values) if (!text.Seen) text.Restore();
        }
        private void StopSettingsClipping()
        {
            foreach (SettingsClipPart part in m_settingsClipParts.Values) part.Dispose();
            m_settingsClipParts.Clear();
            foreach (SettingsTextClip text in m_settingsClipTexts.Values) text.Dispose();
            m_settingsClipTexts.Clear();
        }
        private void DestroySettingsClipping()
        {
            StopSettingsClipping();
            foreach (ScrollRow row in m_scrollRows) RestoreSettingsHits(row);
        }
    }
}
