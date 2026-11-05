using System;
using System.Collections.Generic;
using System.Reflection;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    // Cross-type portraits use the original frame's coordinate system. Sample
    // the complete native portrait and clamp uncovered corners to its boundary,
    // avoiding extra zoom and sampling outside the native artwork.
    internal sealed class ComboPortraitMapping : IDisposable
    {
        private static readonly FieldInfo s_legacy = typeof(Actor).GetField("m_legacyPortraitMaterialIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Actor m_target, m_source;
        private readonly AssetHandle<GameObject> m_asset;
        private readonly bool m_ellipse;
        private Component m_filter;
        private Mesh m_original, m_mapped;
        private bool m_failed;

        internal ComboPortraitMapping(Actor target, CollectibleCard component, TAG_PREMIUM premium)
        {
            m_target = target;
            m_ellipse = component.GetEntityDef().GetTag(GAME_TAG.CARDTYPE) != (int)TAG_CARDTYPE.SPELL;
            m_asset = AssetLoader.Get().LoadAsset<GameObject>(ActorNames.GetHandActor(component.GetEntityDef(), premium));
            m_source = m_asset?.Asset?.GetComponent<Actor>();
            if (m_source == null) { m_asset?.Dispose(); throw new InvalidOperationException("Native portrait geometry is unavailable."); }
        }
        private static Mesh MeshOf(Component surface) => surface is MeshFilter filter ? filter.sharedMesh : (surface as SkinnedMeshRenderer)?.sharedMesh;
        private static void SetMesh(Component surface, Mesh mesh)
        { if (surface is MeshFilter filter) filter.sharedMesh = mesh; else if (surface is SkinnedMeshRenderer skinned) skinned.sharedMesh = mesh; }
        private static Component Surface(GameObject go) => (Component)go?.GetComponent<MeshFilter>() ?? go?.GetComponent<SkinnedMeshRenderer>();
        private static bool Portrait(Actor actor, out Component filter, out int index)
        {
            GameObject go = actor.GetPortraitMesh(); index = actor.m_portraitMatIdx;
            if (go == null) { go = actor.GetMeshRenderer()?.gameObject; index = (int)(s_legacy?.GetValue(actor) ?? -1); }
            filter = Surface(go);
            if (MeshOf(filter) != null && index >= 0 && index < MeshOf(filter).subMeshCount) return true;
            // Raw prefab assets have not run Actor.AssignMeshRenderers yet.
            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                Component candidate = Surface(renderer.gameObject);
                Mesh mesh = MeshOf(candidate); Material[] materials = renderer.sharedMaterials;
                if (mesh == null || materials == null) continue;
                for (int i = 0; i < materials.Length && i < mesh.subMeshCount; i++)
                    if (materials[i] != null && materials[i].name.IndexOf("Portrait", StringComparison.OrdinalIgnoreCase) >= 0)
                    { filter = candidate; index = i; return true; }
            }
            return false;
        }
        internal void Refresh()
        {
            if (m_failed) return;
            try
            {
                if (!Portrait(m_target, out Component target, out int targetIndex)
                    || !Portrait(m_source, out Component source, out int sourceIndex)) return;
                if (m_filter == target && MeshOf(target) == m_mapped) return;
                Restore();
                m_original = MeshOf(target); m_filter = target;
                m_mapped = Map(m_original, targetIndex, m_target.transform.worldToLocalMatrix * target.transform.localToWorldMatrix,
                    MeshOf(source), sourceIndex, m_source.transform.worldToLocalMatrix * source.transform.localToWorldMatrix, m_ellipse);
                SetMesh(target, m_mapped);
            }
            catch (Exception ex) { m_failed = true; DebugLog.Exception("Combo portrait mapping failed", ex); }
        }
        private void Restore()
        {
            if (m_filter != null && MeshOf(m_filter) == m_mapped) SetMesh(m_filter, m_original);
            if (m_mapped != null) UnityEngine.Object.Destroy(m_mapped);
            m_mapped = null; m_filter = null;
        }
        public void Dispose() { Restore(); m_asset?.Dispose(); }

        internal static Mesh Map(Mesh target, int targetIndex, Matrix4x4 targetToCard,
            Mesh source, int sourceIndex, Matrix4x4 sourceToCard, bool ellipse)
        {
            if (!target.isReadable) target = MixedPremiumMesh.GetReadableCopy(target);
            if (!source.isReadable) source = MixedPremiumMesh.GetReadableCopy(source);
            Vector3[] native = source.vertices, destination = target.vertices;
            int[] sourceTriangles = source.GetTriangles(sourceIndex), triangles = target.GetTriangles(targetIndex);
            var plane = new Vector3[native.Length];
            for (int i = 0; i < plane.Length; i++) plane[i] = sourceToCard.MultiplyPoint3x4(native[i]);
            Rect from = Bounds(plane, sourceTriangles, Matrix4x4.identity), to = Bounds(destination, triangles, targetToCard);
            Vector3 center = new Vector3(from.center.x, 0, from.center.y);
            if (!Sample(center, plane, sourceTriangles, out _, out _))
                center = (plane[sourceTriangles[0]] + plane[sourceTriangles[1]] + plane[sourceTriangles[2]]) * (1f / 3f);
            // Preserve the native framing, including its top and bottom. An
            // inscribed source rectangle magnified oval artwork even when the
            // destination was another minion. Only clamp uncovered corners.
            Rect crop = new Rect(from.xMin, from.yMin, from.width, from.height);
            var positions = new List<Vector3>(destination);
            Vector3[] originalNormals = target.normals; Vector4[] originalTangents = target.tangents;
            Color[] originalColors = target.colors; BoneWeight[] originalSkin = target.boneWeights;
            var normals = new List<Vector3>(originalNormals); var tangents = new List<Vector4>(originalTangents);
            var colors = new List<Color>(originalColors); var skin = new List<BoneWeight>(originalSkin);
            var uv = new List<Vector4>[4]; var nativeUv = new List<Vector4>[4];
            for (int channel = 0; channel < 4; channel++)
            {
                uv[channel] = new List<Vector4>(); target.GetUVs(channel, uv[channel]); nativeUv[channel] = new List<Vector4>(); source.GetUVs(channel, nativeUv[channel]);
                if (uv[channel].Count == 0 && nativeUv[channel].Count == native.Length)
                    for (int i = 0; i < destination.Length; i++) uv[channel].Add(Vector4.zero);
            }
            bool hasNormals = normals.Count == destination.Length, hasTangents = tangents.Count == destination.Length,
                hasColors = colors.Count == destination.Length, hasSkin = skin.Count == destination.Length;
            var mappedTriangles = new List<int>();
            int steps = triangles.Length <= 12 ? 8 : triangles.Length <= 96 ? 3 : 1;
            for (int tri = 0; tri < triangles.Length; tri += 3)
            {
                int a = triangles[tri], b = triangles[tri + 1], c = triangles[tri + 2];
                void Vertex(float u, float v)
                {
                    float w = 1f - u - v;
                    Vector3 point = destination[a] * w + destination[b] * u + destination[c] * v;
                    Vector3 p = targetToCard.MultiplyPoint3x4(point);
                    Vector3 sample = new Vector3(crop.xMin + (p.x - to.xMin) / to.width * crop.width, 0,
                        crop.yMin + (p.z - to.yMin) / to.height * crop.height);
                    Vector3 direction = sample - center;
                    float nx = direction.x / (from.width * .5f), nz = direction.z / (from.height * .5f);
                    float radius = (float)Math.Sqrt(nx * nx + nz * nz);
                    float extent = ellipse && radius > 1f ? 1f / radius : 1f;
                    sample = center + direction * (extent * .998f);
                    if (!Sample(sample, plane, sourceTriangles, out int offset, out Vector3 weights))
                    {
                        float low = 0f, high = extent;
                        for (int pass = 0; pass < 18; pass++)
                        {
                            float mid = (low + high) * .5f;
                            if (Sample(center + direction * mid, plane, sourceTriangles, out _, out _)) low = mid; else high = mid;
                        }
                        if (!Sample(center + direction * (low * .998f), plane, sourceTriangles, out offset, out weights))
                            throw new InvalidOperationException("Portrait coordinate falls outside its native artwork.");
                    }
                    mappedTriangles.Add(positions.Count); positions.Add(point);
                    if (hasNormals) normals.Add(originalNormals[a] * w + originalNormals[b] * u + originalNormals[c] * v);
                    if (hasTangents) tangents.Add(originalTangents[a] * w + originalTangents[b] * u + originalTangents[c] * v);
                    if (hasColors) colors.Add(new Color(originalColors[a].r * w + originalColors[b].r * u + originalColors[c].r * v,
                        originalColors[a].g * w + originalColors[b].g * u + originalColors[c].g * v,
                        originalColors[a].b * w + originalColors[b].b * u + originalColors[c].b * v,
                        originalColors[a].a * w + originalColors[b].a * u + originalColors[c].a * v));
                    if (hasSkin) skin.Add(w + u > .000001f
                        ? MixedPremiumMesh.InterpolateSkin(MixedPremiumMesh.InterpolateSkin(originalSkin[a], originalSkin[b], u / (w + u)), originalSkin[c], v)
                        : originalSkin[c]);
                    for (int channel = 0; channel < 4; channel++)
                    {
                        if (uv[channel].Count == 0) continue;
                        List<Vector4> src = nativeUv[channel];
                        uv[channel].Add(src.Count == native.Length
                            ? src[sourceTriangles[offset]] * weights.x + src[sourceTriangles[offset + 1]] * weights.y + src[sourceTriangles[offset + 2]] * weights.z
                            : uv[channel][a] * w + uv[channel][b] * u + uv[channel][c] * v);
                    }
                }
                for (int row = 0; row < steps; row++) for (int col = 0; col < steps - row; col++)
                {
                    Vertex(col / (float)steps, row / (float)steps); Vertex((col + 1f) / steps, row / (float)steps); Vertex(col / (float)steps, (row + 1f) / steps);
                    if (col + row < steps - 1)
                    { Vertex((col + 1f) / steps, row / (float)steps); Vertex((col + 1f) / steps, (row + 1f) / steps); Vertex(col / (float)steps, (row + 1f) / steps); }
                }
            }
            var mesh = new Mesh { name = target.name + "_ComboPortrait", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                vertices = positions.ToArray(), normals = normals.ToArray(), tangents = tangents.ToArray(), colors = colors.ToArray(),
                boneWeights = skin.ToArray(), bindposes = target.bindposes, subMeshCount = target.subMeshCount };
            try
            {
                for (int channel = 0; channel < 4; channel++) if (uv[channel].Count > 0) mesh.SetUVs(channel, uv[channel]);
                for (int i = 0; i < target.subMeshCount; i++) mesh.SetTriangles(i == targetIndex ? mappedTriangles : new List<int>(target.GetTriangles(i)), i, false);
                mesh.RecalculateBounds(); return mesh;
            }
            catch { UnityEngine.Object.Destroy(mesh); throw; }
        }
        private static Rect Bounds(Vector3[] points, int[] indices, Matrix4x4 transform)
        {
            float left = float.PositiveInfinity, bottom = float.PositiveInfinity, right = float.NegativeInfinity, top = float.NegativeInfinity;
            foreach (int index in indices) { Vector3 p = transform.MultiplyPoint3x4(points[index]); left = Math.Min(left, p.x); right = Math.Max(right, p.x); bottom = Math.Min(bottom, p.z); top = Math.Max(top, p.z); }
            if (right - left < .000001f || top - bottom < .000001f) throw new InvalidOperationException("Invalid portrait plane.");
            return Rect.MinMaxRect(left, bottom, right, top);
        }
        private static bool Sample(Vector3 p, Vector3[] points, int[] indices, out int offset, out Vector3 weights)
        {
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = points[indices[i]], b = points[indices[i + 1]] - a, c = points[indices[i + 2]] - a, delta = p - a;
                float determinant = b.x * c.z - b.z * c.x;
                if (Math.Abs(determinant) < .00000001f) continue;
                float u = (delta.x * c.z - delta.z * c.x) / determinant, v = (b.x * delta.z - b.z * delta.x) / determinant;
                if (u >= -.00001f && v >= -.00001f && u + v <= 1.00001f) { offset = i; weights = new Vector3(1f - u - v, u, v); return true; }
            }
            offset = 0; weights = Vector3.zero; return false;
        }
    }
}
