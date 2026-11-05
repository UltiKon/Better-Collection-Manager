using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterCollectionManager
{
    /// <summary>
    /// Clips private mesh copies, leaving native materials (including animated
    /// golden artwork), inventory objects and shared prefab meshes intact.
    /// </summary>
    internal static class MixedPremiumMesh
    {
        private static readonly Dictionary<Mesh, Mesh> s_readableCopies = new Dictionary<Mesh, Mesh>();
        // The game's stripped Unity build omits BoneWeight's setters.
        private static readonly FieldInfo[] s_skinWeights = SkinFields("m_Weight");
        private static readonly FieldInfo[] s_skinIndices = SkinFields("m_BoneIndex");

        private static FieldInfo[] SkinFields(string prefix)
        {
            var fields = new FieldInfo[4];
            for (int i = 0; i < fields.Length; i++)
                fields[i] = typeof(BoneWeight).GetField(prefix + i, BindingFlags.Instance | BindingFlags.NonPublic);
            return fields;
        }

        private static Vector4 InterpolateVector4(Vector4 a, Vector4 b, float t)
        {
            return new Vector4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
        }

        private struct Vertex
        {
            public Vector3 Position, Normal;
            public Vector4 Tangent;
            public Color Color;
            public Vector4 Uv0, Uv1, Uv2, Uv3;
            public BoneWeight Skin;
            public float Distance;

            public static Vertex Interpolate(Vertex a, Vertex b)
            {
                float t = a.Distance / (a.Distance - b.Distance);
                return new Vertex
                {
                    Position = Vector3.LerpUnclamped(a.Position, b.Position, t),
                    Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t).normalized,
                    Tangent = InterpolateVector4(a.Tangent, b.Tangent, t),
                    Color = Color.LerpUnclamped(a.Color, b.Color, t),
                    Uv0 = InterpolateVector4(a.Uv0, b.Uv0, t),
                    Uv1 = InterpolateVector4(a.Uv1, b.Uv1, t),
                    Uv2 = InterpolateVector4(a.Uv2, b.Uv2, t),
                    Uv3 = InterpolateVector4(a.Uv3, b.Uv3, t),
                    Skin = InterpolateSkin(a.Skin, b.Skin, t),
                    Distance = 0f
                };
            }
        }

        public static Mesh Clip(Mesh source, Matrix4x4 toCard, Vector3 center, Vector3 size, bool golden,
            float upDirection = 1f, float splitFraction = MixedPremiumPair.NameBannerFraction)
        {
            if (source.blendShapeCount != 0)
                throw new InvalidOperationException("Card mesh uses blend shapes: " + source.name);
            if (!source.isReadable)
                source = GetReadableCopy(source);

            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Color[] colors = source.colors;
            BoneWeight[] weights = source.boneWeights;
            var uv = new List<Vector4>[4];
            for (int i = 0; i < uv.Length; i++)
            {
                uv[i] = new List<Vector4>();
                source.GetUVs(i, uv[i]);
            }
            var vertices = new Vertex[positions.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = toCard.MultiplyPoint3x4(positions[i]);
                vertices[i] = new Vertex
                {
                    Position = positions[i],
                    Normal = normals.Length == positions.Length ? normals[i] : Vector3.zero,
                    Tangent = tangents.Length == positions.Length ? tangents[i] : Vector4.zero,
                    Color = colors.Length == positions.Length ? colors[i] : Color.white,
                    Skin = weights.Length == positions.Length ? weights[i] : default,
                    Uv0 = uv[0].Count == positions.Length ? uv[0][i] : Vector4.zero,
                    Uv1 = uv[1].Count == positions.Length ? uv[1][i] : Vector4.zero,
                    Uv2 = uv[2].Count == positions.Length ? uv[2][i] : Vector4.zero,
                    Uv3 = uv[3].Count == positions.Length ? uv[3][i] : Vector4.zero,
                    Distance = MixedPremiumPair.SignedDistance(point.z,
                        center.z, size.z, upDirection, splitFraction) * (golden ? 1f : -1f)
                };
            }

            var output = new List<Vertex>();
            var submeshes = new List<int>[source.subMeshCount];
            var polygon = new Vertex[4];
            for (int sub = 0; sub < submeshes.Length; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Card mesh is not triangulated: " + source.name);
                int[] triangles = source.GetTriangles(sub);
                var indices = submeshes[sub] = new List<int>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vertex a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    int count = 0;
                    ClipEdge(c, a, polygon, ref count);
                    ClipEdge(a, b, polygon, ref count);
                    ClipEdge(b, c, polygon, ref count);
                    for (int j = 1; j + 1 < count; j++)
                    {
                        indices.Add(output.Count); output.Add(polygon[0]);
                        indices.Add(output.Count); output.Add(polygon[j]);
                        indices.Add(output.Count); output.Add(polygon[j + 1]);
                    }
                }
            }

            var mesh = new Mesh { name = source.name + (golden ? "_BCM_GoldenHalf" : "_BCM_NormalHalf") };
            try
            {
                // Clipping duplicates triangle vertices; large imported meshes
                // can exceed the 16-bit limit even when the source did not.
                if (output.Count > 65535)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var outPositions = new Vector3[output.Count];
                var outNormals = new Vector3[output.Count];
                var outTangents = new Vector4[output.Count];
                var outColors = new Color[output.Count];
                var outWeights = new BoneWeight[output.Count];
                var outUv = new List<Vector4>[4];
                for (int channel = 0; channel < 4; channel++) outUv[channel] = new List<Vector4>(output.Count);
                for (int i = 0; i < output.Count; i++)
                {
                    Vertex v = output[i];
                    outPositions[i] = v.Position; outNormals[i] = v.Normal;
                    outTangents[i] = v.Tangent; outColors[i] = v.Color; outWeights[i] = v.Skin;
                    outUv[0].Add(v.Uv0); outUv[1].Add(v.Uv1); outUv[2].Add(v.Uv2); outUv[3].Add(v.Uv3);
                }
                mesh.vertices = outPositions;
                if (normals.Length == positions.Length) mesh.normals = outNormals;
                if (tangents.Length == positions.Length) mesh.tangents = outTangents;
                if (colors.Length == positions.Length) mesh.colors = outColors;
                if (weights.Length == positions.Length)
                {
                    mesh.boneWeights = outWeights;
                    mesh.bindposes = source.bindposes;
                }
                for (int channel = 0; channel < 4; channel++)
                    if (uv[channel].Count == positions.Length) mesh.SetUVs(channel, outUv[channel]);
                mesh.subMeshCount = submeshes.Length;
                for (int sub = 0; sub < submeshes.Length; sub++) mesh.SetTriangles(submeshes[sub], sub, false);
                mesh.bounds = source.bounds;
                return mesh;
            }
            catch
            {
                UnityEngine.Object.Destroy(mesh);
                throw;
            }
        }

        internal static Mesh GetReadableCopy(Mesh source)
        {
            if (s_readableCopies.TryGetValue(source, out Mesh cached) && cached != null)
                return cached;

            // Imported card meshes (including pPlane2) discard their CPU copy.
            // The GPU buffers remain available. Copy their exact layout into a
            // private readable mesh; never change the source's import flags or
            // buffer targets. Cache immutable imported geometry for this session
            // so shared card frames are not read back on every tile or frame.
            var copy = new Mesh { name = source.name + "_BCM_Readable" };
            try
            {
                copy.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    int stride = source.GetVertexBufferStride(stream);
                    if (copy.GetVertexBufferStride(stream) != stride)
                        throw new InvalidOperationException("Card vertex buffer layout changed while copying " + source.name);
                    byte[] bytes;
                    using (GraphicsBuffer buffer = source.GetVertexBuffer(stream))
                    {
                        bytes = ReadBuffer(buffer, checked(source.vertexCount * stride));
                    }
                    copy.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream,
                        MeshUpdateFlags.DontRecalculateBounds);
                }

                var submeshes = new SubMeshDescriptor[source.subMeshCount];
                int indexCount = 0;
                for (int i = 0; i < submeshes.Length; i++)
                {
                    submeshes[i] = source.GetSubMesh(i);
                    indexCount = Math.Max(indexCount, checked(submeshes[i].indexStart + submeshes[i].indexCount));
                }
                int indexSize = source.indexFormat == IndexFormat.UInt16 ? 2 : 4;
                byte[] indices = Array.Empty<byte>();
                if (indexCount > 0)
                {
                    using (GraphicsBuffer buffer = source.GetIndexBuffer())
                    {
                        indices = ReadBuffer(buffer, checked(indexCount * indexSize));
                    }
                }
                copy.SetIndexBufferParams(indexCount, source.indexFormat);
                copy.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontRecalculateBounds);
                copy.subMeshCount = submeshes.Length;
                for (int i = 0; i < submeshes.Length; i++)
                    copy.SetSubMesh(i, submeshes[i], MeshUpdateFlags.DontRecalculateBounds);
                copy.bindposes = source.bindposes;
                copy.bounds = source.bounds;

                s_readableCopies[source] = copy;
                DebugLog.Msg($"Mixed premium GPU mesh readback: {source.name}, vertices={source.vertexCount}, streams={source.vertexBufferCount}");
                return copy;
            }
            catch
            {
                UnityEngine.Object.Destroy(copy);
                throw;
            }
        }

        private static byte[] ReadBuffer(GraphicsBuffer buffer, int byteCount)
        {
            if (buffer == null) throw new InvalidOperationException("Card graphics buffer is unavailable");
            // GraphicsBuffer.GetData is stripped from the game's Unity build.
            // Its readback request API is retained. Finish the initial snapshot
            // before drawing this tile; subsequent tiles use the cached copy.
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(buffer, byteCount, 0);
            request.WaitForCompletion();
            if (!request.done || request.hasError)
                throw new InvalidOperationException("Card graphics buffer readback failed");
            var data = request.GetData<byte>();
            if (data.Length != byteCount)
                throw new InvalidOperationException("Card graphics buffer readback has the wrong size");
            // GetData returns a borrowed view owned by the request.
            return data.ToArray();
        }

        public static void ClearReadbackCache()
        {
            foreach (Mesh mesh in s_readableCopies.Values)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            s_readableCopies.Clear();
        }

        private static void ClipEdge(Vertex a, Vertex b, Vertex[] output, ref int count)
        {
            bool insideA = a.Distance >= 0f, insideB = b.Distance >= 0f;
            if (insideA != insideB) output[count++] = Vertex.Interpolate(a, b);
            if (insideB) output[count++] = b;
        }

        internal static BoneWeight InterpolateSkin(BoneWeight a, BoneWeight b, float t)
        {
            if (a.weight0 + a.weight1 + a.weight2 + a.weight3
                + b.weight0 + b.weight1 + b.weight2 + b.weight3 <= 0f)
                return default;
            int[] bones = { a.boneIndex0, a.boneIndex1, a.boneIndex2, a.boneIndex3,
                b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3 };
            float[] values = { a.weight0 * (1f - t), a.weight1 * (1f - t), a.weight2 * (1f - t), a.weight3 * (1f - t),
                b.weight0 * t, b.weight1 * t, b.weight2 * t, b.weight3 * t };
            for (int i = 0; i < bones.Length; i++)
                for (int j = i + 1; j < bones.Length; j++)
                    if (bones[i] == bones[j]) { values[i] += values[j]; values[j] = 0f; }
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < bones.Length; j++)
                    if (values[j] > values[i])
                    {
                        float weight = values[i]; values[i] = values[j]; values[j] = weight;
                        int bone = bones[i]; bones[i] = bones[j]; bones[j] = bone;
                    }
            float sum = values[0] + values[1] + values[2] + values[3];
            if (sum <= 0f) return default;
            object boxed = default(BoneWeight);
            for (int i = 0; i < 4; i++)
            {
                if (s_skinWeights[i] == null || s_skinIndices[i] == null)
                    throw new InvalidOperationException("Card bone weights cannot be interpolated on this Unity build");
                s_skinWeights[i].SetValue(boxed, values[i] / sum);
                s_skinIndices[i].SetValue(boxed, bones[i]);
            }
            return (BoneWeight)boxed;
        }
    }
}
