using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    // Normalize the native hit bone's plane before selecting or stretching art.
    // Some client prefabs use XY, others XZ; the grip need not own its renderer.
    internal static class NativeScrollbarArt
    {
        internal sealed class Part
        {
            internal Mesh Mesh;
            internal Renderer Renderer;
            internal Matrix4x4 ToTrack;
            internal Bounds Bounds;
            internal bool Grip, Owned, Hidden;
            internal float AxisX;
        }
        // Keep original UVs, tint, normals and submesh/material slots. Splitting
        // by topology also keeps unrelated tray panels out of the copied rail.
        private static IEnumerable<Mesh> Islands(Mesh original)
        {
            Mesh source = original.isReadable ? original : MixedPremiumMesh.GetReadableCopy(original);
            Vector3[] points = source.vertices;
            int[] roots = Enumerable.Range(0, points.Length).ToArray();
            int Root(int value) { while (roots[value] != value) { roots[value] = roots[roots[value]]; value = roots[value]; } return value; }
            var triangles = new int[source.subMeshCount][];
            for (int sub = 0; sub < triangles.Length; sub++)
            {
                triangles[sub] = source.GetTriangles(sub);
                for (int i = 0; i + 2 < triangles[sub].Length; i += 3)
                {
                    int a = Root(triangles[sub][i]);
                    roots[Root(triangles[sub][i + 1])] = a;
                    roots[Root(triangles[sub][i + 2])] = a;
                }
            }
            var groups = new Dictionary<int, List<int>[]>();
            for (int sub = 0; sub < triangles.Length; sub++)
                for (int i = 0; i + 2 < triangles[sub].Length; i += 3)
                {
                    int root = Root(triangles[sub][i]);
                    if (!groups.TryGetValue(root, out var slots))
                    { slots = Enumerable.Range(0, triangles.Length).Select(_ => new List<int>()).ToArray(); groups.Add(root, slots); }
                    for (int j = 0; j < 3; j++) slots[sub].Add(triangles[sub][i + j]);
                }
            Vector3[] normals = source.normals; Vector4[] tangents = source.tangents; Color[] colors = source.colors;
            var uvs = new List<Vector4>[4];
            for (int channel = 0; channel < uvs.Length; channel++) { uvs[channel] = new List<Vector4>(); source.GetUVs(channel, uvs[channel]); }
            foreach (var slots in groups.Values)
            {
                int[] used = slots.SelectMany(slot => slot).Distinct().ToArray();
                var indices = new Dictionary<int, int>();
                for (int i = 0; i < used.Length; i++) indices.Add(used[i], i);
                var copy = new Mesh { name = original.name, vertices = used.Select(i => points[i]).ToArray(), subMeshCount = triangles.Length };
                if (normals.Length == points.Length) copy.normals = used.Select(i => normals[i]).ToArray();
                if (tangents.Length == points.Length) copy.tangents = used.Select(i => tangents[i]).ToArray();
                if (colors.Length == points.Length) copy.colors = used.Select(i => colors[i]).ToArray();
                for (int channel = 0; channel < uvs.Length; channel++)
                    if (uvs[channel].Count == points.Length) copy.SetUVs(channel, used.Select(i => uvs[channel][i]).ToList());
                for (int sub = 0; sub < slots.Length; sub++) copy.SetTriangles(slots[sub].Select(i => indices[i]).ToList(), sub, false);
                copy.RecalculateBounds(); yield return copy;
            }
        }
        private static readonly HashSet<string> s_reported = new HashSet<string>();
        private static void Report(string message)
        {
            if (s_reported.Add(message)) DebugLog.Msg(message);
        }
        private static readonly Dictionary<string, AssetHandle<GameObject>> s_prefabs = new Dictionary<string, AssetHandle<GameObject>>();
        private static float s_prefabRetry;
        private static void LoadPrefabSources()
        {
            if (Time.unscaledTime < s_prefabRetry) return;
            s_prefabRetry = Time.unscaledTime + 1f;
            try
            {
                // Read this client's native desktop collection prefab reference.
                // Load asset data only: never instantiate or open the collection.
                Type scene = typeof(CollectionManager).Assembly.GetType("CollectionManagerScene");
                string reference = scene?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(NativePopupDiagnostics.ReferencedStrings)
                    .FirstOrDefault(value => value.StartsWith("CollectionManager.prefab:", StringComparison.OrdinalIgnoreCase));
                if (reference == null) { Report("Native scrollbar prefab unavailable: collection reference missing"); return; }
                Load(reference, 0);
            }
            catch (Exception ex) { Report("Native scrollbar prefab: " + ex.Message); }

            void Load(string reference, int depth)
            {
                if (!s_prefabs.TryGetValue(reference, out var handle))
                {
                    handle = AssetLoader.Get().LoadAsset<GameObject>(reference);
                    if (handle == null) return;
                    s_prefabs.Add(reference, handle);
                    Report("Native scrollbar prefab retained: " + reference);
                }
                if (handle.Asset == null || depth >= 3) return;
                var references = handle.Asset.GetComponentsInChildren<NestedPrefab>(true).Select(nested => nested.m_Prefab)
                    .Concat(handle.Asset.GetComponentsInChildren<NestedPrefabPlatformOverride>(true).Select(nested => (string)nested.m_Prefab));
                foreach (string path in references)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    string name = path.ToLowerInvariant();
                    if (name.Contains("decktray") || name.Contains("scrollbar")) Load(path, depth + 1);
                }
            }
        }
        internal static void ReleasePrefabSources()
        {
            foreach (var handle in s_prefabs.Values) handle.Dispose();
            s_prefabs.Clear(); s_prefabRetry = 0;
        }
        internal static bool CaptureAvailable(out List<Part> parts, out Bounds railArt, out Bounds gripArt, bool requireRail = false)
        {
            var sources = new List<UIBScrollable>();
            var roots = new Dictionary<UIBScrollable, Transform>();
            foreach (SetFilterTray tray in Resources.FindObjectsOfTypeAll<SetFilterTray>())
                if (tray?.m_scroller != null && !sources.Contains(tray.m_scroller))
                {
                    sources.Add(tray.m_scroller);
                    if (tray.m_trayObject != null) roots.Add(tray.m_scroller, tray.m_trayObject.transform);
                }
            // The original implementation also used native deck/list bars.
            // Settings can open before a Sets tray exists, or with a content
            // scroller that has no scrollbar references of its own.
            foreach (UIBScrollable source in Resources.FindObjectsOfTypeAll<UIBScrollable>())
                if (source != null && !sources.Contains(source)) sources.Add(source);
            AddPrefabSources();
            parts = null; railArt = gripArt = default;
            for (int index = 0, loadedCount = sources.Count; index <= sources.Count; index++)
            {
                if (index == loadedCount)
                {
                    // A valid hit bone and grip do not imply visible rail art.
                    // Settings needs the complete bar even when a live Sets or
                    // hub list exposes only its grip. Retain native assets and
                    // retry pending loads without instantiating their scene.
                    if (parts == null || requireRail) { LoadPrefabSources(); AddPrefabSources(); }
                    loadedCount = -1;
                }
                if (index == sources.Count) break;
                UIBScrollable source = sources[index];
                if (source.m_ScrollPlane == UIBScrollable.ScrollDirection.X) continue;
                Transform root = roots.TryGetValue(source, out var known) ? known : source.transform;
                if (!Capture(source, root, out var candidate, out Bounds rail, out Bounds grip)) continue;
                bool complete = candidate.Exists(part => !part.Grip);
                if (parts == null || complete)
                {
                    if (parts != null) foreach (Part part in parts) if (part.Owned) UnityEngine.Object.Destroy(part.Mesh);
                    parts = candidate; railArt = rail; gripArt = grip;
                    if (complete) return true;
                }
                else foreach (Part part in candidate) if (part.Owned) UnityEngine.Object.Destroy(part.Mesh);
            }
            if (parts != null)
            {
                if (!requireRail) return true;
                foreach (Part part in parts) if (part.Owned) UnityEngine.Object.Destroy(part.Mesh);
            }
            parts = new List<Part>();
            Report("Native scrollbar unavailable: loaded scrollers=" + sources.Count + "; require rail=" + requireRail);
            return false;

            void AddPrefabSources()
            {
                foreach (var handle in s_prefabs.Values)
                    if (handle.Asset != null)
                        foreach (UIBScrollable source in handle.Asset.GetComponentsInChildren<UIBScrollable>(true))
                            if (!sources.Contains(source)) sources.Add(source);
            }
        }
        internal static bool Capture(UIBScrollable source, Transform root, out List<Part> parts, out Bounds railArt, out Bounds gripArt)
        {
            parts = new List<Part>(); railArt = gripArt = default;
            if (source?.m_ScrollTrack == null || source.m_ScrollThumb == null)
            {
                Report("Native scrollbar unavailable: source=" + (source?.gameObject.name ?? "none")
                    + "; track=" + (source?.m_ScrollTrack != null) + "; thumb=" + (source?.m_ScrollThumb != null));
                return false;
            }
            // The controller may live above the input bones while its art is
            // a sibling branch. Include that native controller's hierarchy.
            while (root.parent != null && (!source.transform.IsChildOf(root)
                || !source.m_ScrollTrack.transform.IsChildOf(root) || !source.m_ScrollThumb.transform.IsChildOf(root))) root = root.parent;
            BoxCollider track = source.m_ScrollTrack;
            Transform thumb = source.m_ScrollThumb.transform;
            bool xy = track.size.y > track.size.z;
            Matrix4x4 plane = xy ? new Matrix4x4(new Vector4(1, 0, 0, 0), new Vector4(0, 0, 1, 0),
                new Vector4(0, -1, 0, 0), new Vector4(0, 0, 0, 1)) : Matrix4x4.identity;
            Bounds rail = FilterTrayGeometry.TransformBounds(new Bounds(track.center, track.size), plane.MultiplyPoint3x4);
            if (rail.size.z <= 0 || rail.size.x <= 0)
            {
                Report("Native scrollbar unavailable: source=" + source.gameObject.name + "; track size=" + track.size);
                return false;
            }
            BoxCollider thumbHit = thumb.GetComponent<BoxCollider>();
            Bounds thumbBounds = thumbHit != null
                ? FilterTrayGeometry.TransformBounds(new Bounds(thumbHit.center, thumbHit.size),
                    (plane * track.transform.worldToLocalMatrix * thumb.localToWorldMatrix).MultiplyPoint3x4)
                : new Bounds(plane.MultiplyPoint3x4(track.transform.InverseTransformPoint(thumb.position)),
                    new Vector3(rail.size.x * 2, 0, rail.size.x * 3));
            var candidates = new List<Part>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<UberText>() != null
                    || (source.m_scrollTrackCover != null && (renderer.gameObject == source.m_scrollTrackCover
                        || renderer.transform.IsChildOf(source.m_scrollTrackCover.transform)))) continue;
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                bool owned = false;
                if (mesh == null && renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                { mesh = new Mesh(); skin.BakeMesh(mesh); owned = true; }
                if (mesh == null) continue;
                Matrix4x4 toTrack = plane * track.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Bounds bounds = FilterTrayGeometry.TransformBounds(mesh.bounds, toTrack.MultiplyPoint3x4);
                bool grip = renderer.transform == thumb || renderer.transform.IsChildOf(thumb), hidden = false;
                float corridor = Math.Max(rail.size.x, thumbBounds.size.x);
                if (!grip && (bounds.max.x < rail.center.x - corridor || bounds.min.x > rail.center.x + corridor
                    || bounds.max.z < rail.min.z - rail.size.x || bounds.min.z > rail.max.z + rail.size.x))
                { if (owned) UnityEngine.Object.Destroy(mesh); continue; }
                if (grip)
                    for (Transform node = renderer.transform; node != null && node != thumb; node = node.parent)
                        hidden |= !node.gameObject.activeSelf;
                bool NearRail(Bounds area) => area.size.x <= Math.Max(rail.size.x * 3, thumbBounds.size.x * 2)
                    && Math.Abs(area.center.x - rail.center.x) <= Math.Max(rail.size.x, thumbBounds.size.x)
                    && area.max.z >= rail.min.z - rail.size.x && area.min.z <= rail.max.z + rail.size.x;
                // Imported bounds can include the whole skinned prefab or
                // unused vertices. Compact the actual triangles for the grip
                // as well as the rail before calculating their visible size.
                foreach (Mesh island in Islands(mesh))
                {
                    Bounds area = FilterTrayGeometry.TransformBounds(island.bounds, toTrack.MultiplyPoint3x4);
                    if (!grip && !NearRail(area)) { UnityEngine.Object.Destroy(island); continue; }
                    candidates.Add(new Part { Mesh = island, Renderer = renderer, ToTrack = toTrack,
                        Bounds = area, Grip = grip, Hidden = hidden, Owned = true });
                }
                if (owned) UnityEngine.Object.Destroy(mesh);
            }
            bool foundGrip = candidates.Exists(part => part.Grip && !part.Hidden);
            if (!foundGrip)
            {
                // The rendered handle can be a sibling of the input bone.
                // Choose the compact piece best matching its native hit box.
                Part closest = null; float score = float.MaxValue;
                foreach (Part part in candidates)
                {
                    if (part.Hidden || part.Bounds.size.z > rail.size.z * .4f) continue;
                    float distance = Math.Abs(part.Bounds.center.x - thumbBounds.center.x)
                        + Math.Abs(part.Bounds.center.z - thumbBounds.center.z)
                        + Math.Abs(part.Bounds.size.z - thumbBounds.size.z) * .2f;
                    if (distance < score) { closest = part; score = distance; }
                }
                if (closest != null && score < Math.Max(thumbBounds.size.z * 2, rail.size.x * 4))
                { closest.Grip = true; foundGrip = true; }
            }
            // Use the rendered grip as the ruler. Track colliders can cover
            // the entire list, admitting unrelated tray borders as "rail".
            Bounds visibleGrip = default; bool hasVisibleGrip = false;
            foreach (Part part in candidates)
                if (part.Grip && !part.Hidden)
                { if (hasVisibleGrip) visibleGrip.Encapsulate(part.Bounds); else visibleGrip = part.Bounds; hasVisibleGrip = true; }
            float artWidth = visibleGrip.size.x;
            float railX = visibleGrip.center.x;
            bool foundRail = false;
            foreach (Part part in candidates)
            {
                bool unrelated = !part.Grip && hasVisibleGrip && (part.Bounds.size.x > artWidth * 1.5f
                    || Math.Abs(part.Bounds.center.x - railX) > artWidth * .75f
                    || (part.Bounds.size.z < rail.size.z * .45f
                        && Math.Min(Math.Abs(part.Bounds.center.z - rail.min.z), Math.Abs(part.Bounds.center.z - rail.max.z)) > artWidth * 2));
                if (part.Hidden || unrelated) { if (part.Owned) UnityEngine.Object.Destroy(part.Mesh); continue; }
                if (part.Grip) { if (!parts.Exists(p => p.Grip)) gripArt = part.Bounds; else gripArt.Encapsulate(part.Bounds); }
                else { if (!foundRail) railArt = part.Bounds; else railArt.Encapsulate(part.Bounds); foundRail = true; }
                part.AxisX = rail.center.x;
                parts.Add(part);
            }
            // The live Sets tray in the supplied client exposes only its grip
            // as separate artwork. Its track is an input collider in the tray
            // background. Requiring another rail renderer rejected that exact
            // native scrollbar (rail=False, grip=True in the game log).
            if (!foundRail) railArt = new Bounds(rail.center, new Vector3(gripArt.size.x, rail.size.y, rail.size.z));
            bool valid = foundGrip && railArt.size.z > 0 && gripArt.size.x > 0 && gripArt.size.z > 0;
            string message = "Native scrollbar: source=" + source.gameObject.name + "; plane=" + (xy ? "XY" : "XZ")
                + "; grip size=" + gripArt.size + "; rail size=" + railArt.size
                + "; artwork=" + string.Join(",", parts.Select(part => part.Renderer.gameObject.name).Distinct().ToArray())
                + "; parts=" + parts.Count + "; rail=" + foundRail + "; grip=" + foundGrip + "; usable=" + valid;
            Report(message);
            if (!valid)
            {
                foreach (Part part in parts) if (part.Owned) UnityEngine.Object.Destroy(part.Mesh);
                parts.Clear();
            }
            return valid;
        }
    }
}
