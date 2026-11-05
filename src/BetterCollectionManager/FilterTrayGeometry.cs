using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>
    /// Resizes a private tray clone in its own X/Z plane. Border strips translate;
    /// only the centre stretches. Small meshes (thumbs, arrows, ornaments) retain
    /// their size. All snapshots refer to the clone, never the shared prefab.
    /// </summary>
    internal sealed class FilterTrayGeometry : IDisposable
    {
        internal readonly struct Axes
        {
            public readonly Bounds Source, Target;
            public readonly float BorderX, BorderZ;
            public Axes(Bounds source, Bounds target, float border)
            {
                Source = source; Target = target;
                BorderX = Mathf.Min(border, Mathf.Min(source.size.x, target.size.x) * 0.24f);
                BorderZ = Mathf.Min(border, Mathf.Min(source.size.z, target.size.z) * 0.24f);
            }
            public Vector3 Map(Vector3 point) => new Vector3(
                MapAxis(point.x, Source.min.x, Source.max.x, Target.min.x, Target.max.x, BorderX),
                point.y,
                MapAxis(point.z, Source.min.z, Source.max.z, Target.min.z, Target.max.z, BorderZ));
            private static float MapAxis(float point, float low, float high, float newLow, float newHigh, float border)
            {
                if (point <= low + border) return point + newLow - low;
                if (point >= high - border) return point + newHigh - high;
                return newLow + border + (point - low - border)
                    * (newHigh - newLow - 2f * border) / Mathf.Max(0.0001f, high - low - 2f * border);
            }
        }

        private sealed class Pose
        {
            public Transform Transform;
            public Vector3 Position, RootPosition;
        }
        private sealed class Art
        {
            public MeshFilter Filter;
            public Mesh Source, Private;
            public Renderer Renderer;
            public bool Enabled;
            public Matrix4x4 ToRoot;
            public Bounds Bounds;
        }
        private sealed class HitArea
        {
            public BoxCollider Box;
            public Vector3 Center, Size;
            public Bounds Bounds;
        }
        private readonly Transform m_root;
        private readonly SetFilterItem m_row;
        private readonly UIBHighlight[] m_highlights;
        private readonly List<Pose> m_poses = new List<Pose>();
        private readonly List<Art> m_art = new List<Art>();
        private readonly List<HitArea> m_hits = new List<HitArea>();
        private Art m_panel;
        public Renderer FrameRenderer => m_panel?.Renderer;

        private static bool Under(Transform child, Transform parent) => parent != null && (child == parent || child.IsChildOf(parent));

        public FilterTrayGeometry(GameObject tray, Transform contents, Renderer[] textSurfaces = null)
        {
            m_root = tray.transform;
            m_row = tray.GetComponent<SetFilterItem>();
            m_highlights = tray.GetComponentsInChildren<UIBHighlight>(true);
            var protectedRoots = new List<Transform>();
            if (contents != null && contents != m_root) protectedRoots.Add(contents);
            if (textSurfaces != null)
                foreach (Renderer renderer in textSurfaces)
                    if (renderer != null && renderer.transform != m_root) protectedRoots.Add(renderer.transform);
            // Retired labels and inherited glyphs are text too. Never snapshot
            // them as button artwork: Restore would otherwise revive their
            // cached enabled state and Resize would stretch their glyph mesh.
            foreach (UberText label in tray.GetComponentsInChildren<UberText>(true))
            {
                if (label.transform != m_root) protectedRoots.Add(label.transform);
                Renderer surface = CounterTextLayout.TextureSurface(label);
                if (surface != null && surface.transform != m_root) protectedRoots.Add(surface.transform);
            }
            foreach (TextMesh glyph in tray.GetComponentsInChildren<TextMesh>(true))
                if (glyph.transform != m_root) protectedRoots.Add(glyph.transform);
            Func<Transform, bool> protectedNode = child => {
                foreach (Transform root in protectedRoots) if (Under(child, root)) return true;
                return false;
            };
            foreach (Transform child in tray.GetComponentsInChildren<Transform>(true))
            {
                if (child == m_root || protectedNode(child)) continue;
                m_poses.Add(new Pose { Transform = child, Position = child.localPosition,
                    RootPosition = m_root.InverseTransformPoint(child.position) });
            }
            foreach (MeshFilter filter in tray.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || protectedNode(filter.transform)) continue;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || filter.GetComponent<UberText>() != null
                    || (textSurfaces != null && Array.IndexOf(textSurfaces, renderer) >= 0)) continue;
                m_art.Add(new Art { Filter = filter, Source = filter.sharedMesh, Renderer = renderer,
                    Enabled = renderer.enabled, ToRoot = m_root.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                    Bounds = TransformBounds(filter.sharedMesh.bounds, p => m_root.InverseTransformPoint(filter.transform.TransformPoint(p))) });
            }
            float panelArea = 0f;
            SetFilterItem row = tray.GetComponent<SetFilterItem>();
            foreach (Art art in m_art)
            {
                if (!art.Enabled || !art.Renderer.gameObject.activeInHierarchy || IsEffect(art.Renderer, row)) continue;
                float area = art.Bounds.size.x * art.Bounds.size.z;
                if (area > panelArea) { panelArea = area; m_panel = art; }
            }
            foreach (BoxCollider box in tray.GetComponentsInChildren<BoxCollider>(true))
            {
                if (protectedNode(box.transform)) continue;
                m_hits.Add(new HitArea { Box = box, Center = box.center, Size = box.size,
                    Bounds = TransformBounds(new Bounds(box.center, box.size), p => m_root.InverseTransformPoint(box.transform.TransformPoint(p))) });
            }
        }

        private static bool IsEffectName(string name) => name.Contains("shadow") || name.Contains("glow") || name.Contains("highlight");

        private bool IsEffect(Renderer renderer, SetFilterItem row = null)
        {
            Transform part = renderer.transform;
            string name = ((renderer.gameObject.name ?? "") + " " + (renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "")
                + " " + (renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "")).ToLowerInvariant();
            if (IsEffectName(name)) return true;
            if (row != null && (Under(part, row.m_selectedGlow != null ? row.m_selectedGlow.transform : null)
                || Under(part, row.m_mouseOverGlow != null ? row.m_mouseOverGlow.transform : null)
                || Under(part, row.m_pressedShadow != null ? row.m_pressedShadow.transform : null))) return true;
            for (Transform parent = part.parent; parent != null && parent != m_root; parent = parent.parent)
                if (IsEffectName((parent.gameObject.name ?? "").ToLowerInvariant())) return true;
            return false;
        }

        public void Restore()
        {
            // Traversal order is parent before child; restore locals exactly.
            foreach (Pose pose in m_poses)
                if (pose.Transform != null) pose.Transform.localPosition = pose.Position;
            foreach (Art art in m_art)
            {
                if (art.Filter != null) art.Filter.sharedMesh = art.Source;
                if (art.Renderer != null) art.Renderer.enabled = art.Enabled;
                if (art.Private != null) UnityEngine.Object.Destroy(art.Private);
                art.Private = null;
            }
            foreach (HitArea hit in m_hits)
                if (hit.Box != null) { hit.Box.center = hit.Center; hit.Box.size = hit.Size; }
        }

        public void Resize(Bounds source, Bounds target, float border, BoxCollider viewport, int column = -1, int columns = 1, float wideThreshold = 0.5f)
        {
            var axes = new Axes(source, target, border);
            foreach (Pose pose in m_poses)
                if (pose.Transform != null) pose.Transform.position = m_root.TransformPoint(axes.Map(pose.RootPosition));
            foreach (Art art in m_art)
            {
                if (art.Filter == null) continue;
                // Native inner-shadow planes can cover the entire opening.
                // Suppress those planes rather than magnifying their darkness.
                string name = ((art.Filter.gameObject.name ?? "") + " " + (art.Source.name ?? "") + " "
                    + (art.Renderer.sharedMaterial != null ? art.Renderer.sharedMaterial.name : "")).ToLowerInvariant();
                bool feedback = FilterButtonFeedback.OwnsVisual(m_row, m_highlights, art.Filter.transform);
                bool broad = art.Bounds.size.x > source.size.x * 0.5f && art.Bounds.size.z > source.size.z * 0.5f;
                if (!feedback && broad && name.Contains("shadow") && (name.Contains("inner")
                    || (art.Bounds.size.x <= source.size.x * 1.1f && art.Bounds.size.z <= source.size.z * 1.1f)))
                {
                    art.Renderer.enabled = false;
                    continue;
                }
                bool wide = art.Bounds.size.x > source.size.x * wideThreshold;
                bool tall = art.Bounds.size.z > source.size.z * 0.5f;
                // Pure translations need no mesh allocation. In particular,
                // scrolling thumb geometry and its native animation stay intact.
                if (!wide && !tall)
                {
                    Vector3 origin = art.ToRoot.MultiplyPoint3x4(Vector3.zero);
                    art.Filter.transform.position = m_root.TransformPoint(MapPart(origin, art.Bounds.center, axes, false, false));
                    continue;
                }
                try
                {
                    art.Private = ResizeMesh(art.Source, art.ToRoot,
                        art.Filter.transform.worldToLocalMatrix * m_root.localToWorldMatrix, axes, art.Bounds.center, wide, tall,
                        // Every wide layer shares the row gradient, including
                        // native hover/selected/pressed artwork. Its visibility
                        // still belongs to the native interaction state.
                        wide ? column : -1, columns, art.Bounds);
                    art.Filter.sharedMesh = art.Private;
                }
                catch (Exception ex)
                {
                    DebugLog.Exception("Filter tray mesh resize: " + art.Source.name, ex);
                }
            }
            foreach (HitArea hit in m_hits)
            {
                if (hit.Box == null) continue;
                bool wide = hit.Box == viewport || hit.Bounds.size.x > source.size.x * 0.5f;
                bool tall = hit.Box == viewport || hit.Bounds.size.z > source.size.z * 0.5f;
                Bounds local = TransformBounds(hit.Bounds, p => hit.Box.transform.InverseTransformPoint(
                    m_root.TransformPoint(MapPart(p, hit.Bounds.center, axes, wide, tall))));
                hit.Box.center = local.center;
                hit.Box.size = local.size;
            }
        }

        private static Vector3 MapPart(Vector3 point, Vector3 center, Axes axes, bool wide, bool tall)
        {
            Vector3 mapped = axes.Map(point), movedCenter = axes.Map(center);
            return new Vector3(wide ? mapped.x : movedCenter.x + point.x - center.x, point.y,
                tall ? mapped.z : movedCenter.z + point.z - center.z);
        }

        internal static bool TryProjectToPlane(Transform root, Camera camera, Vector3 screen, float depth, out Vector3 point)
        {
            Ray ray = camera.ScreenPointToRay(screen);
            Vector3 origin = root.InverseTransformPoint(ray.origin);
            Vector3 direction = root.InverseTransformPoint(ray.origin + ray.direction) - origin;
            point = Vector3.zero;
            if (Mathf.Abs(direction.y) < 0.000001f) return false;
            float t = (depth - origin.y) / direction.y;
            if (t < 0f) return false;
            point = origin + direction * t;
            return true;
        }

        internal static Bounds TransformBounds(Bounds bounds, Func<Vector3, Vector3> transform)
        {
            Vector3 first = transform(bounds.min), low = first, high = first;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 p = transform(bounds.center + new Vector3(bounds.extents.x * x, bounds.extents.y * y, bounds.extents.z * z));
                low = new Vector3(Mathf.Min(low.x, p.x), Mathf.Min(low.y, p.y), Mathf.Min(low.z, p.z));
                high = new Vector3(Mathf.Max(high.x, p.x), Mathf.Max(high.y, p.y), Mathf.Max(high.z, p.z));
            }
            return new Bounds((low + high) * 0.5f, high - low);
        }

        private static Vector4 Lerp4(Vector4 a, Vector4 b, float t) => new Vector4(
            a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);

        private struct Vertex
        {
            public Vector3 Point, Normal;
            public Vector4 Tangent;
            public Color Color;
            public Vector4 Uv0, Uv1, Uv2, Uv3;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex {
                Point = Vector3.LerpUnclamped(a.Point, b.Point, t), Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t).normalized,
                Tangent = Lerp4(a.Tangent, b.Tangent, t), Color = Color.LerpUnclamped(a.Color, b.Color, t),
                Uv0 = Lerp4(a.Uv0, b.Uv0, t), Uv1 = Lerp4(a.Uv1, b.Uv1, t),
                Uv2 = Lerp4(a.Uv2, b.Uv2, t), Uv3 = Lerp4(a.Uv3, b.Uv3, t) };
        }

        internal static Mesh ResizeMesh(Mesh source, Matrix4x4 toRoot, Matrix4x4 fromRoot, Axes axes,
            Vector3 center, bool wide, bool tall, int column = -1, int columns = 1, Bounds? gradientBounds = null)
        {
            if (!source.isReadable) source = MixedPremiumMesh.GetReadableCopy(source);
            Vector3[] points = source.vertices, normals = source.normals;
            Vector4[] tangents = source.tangents;
            Color[] colors = source.colors;
            var uv = new List<Vector4>[4];
            for (int i = 0; i < 4; i++) { uv[i] = new List<Vector4>(); source.GetUVs(i, uv[i]); }
            var input = new Vertex[points.Length];
            for (int i = 0; i < points.Length; i++)
                input[i] = new Vertex { Point = toRoot.MultiplyPoint3x4(points[i]),
                    Normal = normals.Length == points.Length ? normals[i] : Vector3.zero,
                    Tangent = tangents.Length == points.Length ? tangents[i] : Vector4.zero,
                    Color = colors.Length == points.Length ? colors[i] : Color.white,
                    Uv0 = uv[0].Count == points.Length ? uv[0][i] : Vector4.zero,
                    Uv1 = uv[1].Count == points.Length ? uv[1][i] : Vector4.zero,
                    Uv2 = uv[2].Count == points.Length ? uv[2][i] : Vector4.zero,
                    Uv3 = uv[3].Count == points.Length ? uv[3][i] : Vector4.zero };
            var output = new List<Vertex>();
            Bounds gradient = gradientBounds ?? axes.Source;
            var submeshes = new List<int>[source.subMeshCount];
            for (int sub = 0; sub < submeshes.Length; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles) throw new InvalidOperationException("Tray mesh is not triangulated");
                var indices = submeshes[sub] = new List<int>();
                int[] triangles = source.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vertex a = input[triangles[i]], b = input[triangles[i + 1]], c = input[triangles[i + 2]];
                    var pieces = new List<List<Vertex>> { new List<Vertex> { a, b, c } };
                    // Imported planes often have only four vertices. Split at
                    // the slice boundaries before moving them; just moving the
                    // original vertices would stretch the border texture again.
                    if (wide) { Split(pieces, axes.Source.min.x + axes.BorderX, true); Split(pieces, axes.Source.max.x - axes.BorderX, true); }
                    if (tall) { Split(pieces, axes.Source.min.z + axes.BorderZ, false); Split(pieces, axes.Source.max.z - axes.BorderZ, false); }
                    foreach (List<Vertex> polygon in pieces)
                    {
                        Vector3 middle = Vector3.zero;
                        foreach (Vertex vertex in polygon) middle += vertex.Point * (1f / polygon.Count);
                        bool interior = middle.x > axes.Source.min.x + axes.BorderX && middle.x < axes.Source.max.x - axes.BorderX
                            && middle.z > axes.Source.min.z + axes.BorderZ && middle.z < axes.Source.max.z - axes.BorderZ;
                        for (int j = 1; j + 1 < polygon.Count; j++)
                        {
                            indices.Add(output.Count); output.Add(SpreadGradient(polygon[0], a, b, c, axes, gradient, column, columns, interior));
                            indices.Add(output.Count); output.Add(SpreadGradient(polygon[j], a, b, c, axes, gradient, column, columns, interior));
                            indices.Add(output.Count); output.Add(SpreadGradient(polygon[j + 1], a, b, c, axes, gradient, column, columns, interior));
                        }
                    }
                }
            }
            var result = new Mesh { name = source.name + "_BCM_Resized" };
            if (output.Count > 65535) result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var outPoints = new Vector3[output.Count];
            var outNormals = new Vector3[output.Count];
            var outTangents = new Vector4[output.Count];
            var outColors = new Color[output.Count];
            var outUv = new List<Vector4>[4];
            for (int i = 0; i < 4; i++) outUv[i] = new List<Vector4>(output.Count);
            for (int i = 0; i < output.Count; i++)
            {
                Vertex v = output[i];
                outPoints[i] = fromRoot.MultiplyPoint3x4(MapPart(v.Point, center, axes, wide, tall));
                outNormals[i] = v.Normal; outTangents[i] = v.Tangent; outColors[i] = v.Color;
                outUv[0].Add(v.Uv0); outUv[1].Add(v.Uv1); outUv[2].Add(v.Uv2); outUv[3].Add(v.Uv3);
            }
            result.vertices = outPoints;
            if (normals.Length == points.Length) result.normals = outNormals;
            if (tangents.Length == points.Length) result.tangents = outTangents;
            if (colors.Length == points.Length) result.colors = outColors;
            for (int i = 0; i < 4; i++) if (uv[i].Count == points.Length) result.SetUVs(i, outUv[i]);
            result.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++) result.SetTriangles(submeshes[i], i, false);
            result.bounds = TransformBounds(source.bounds,
                p => fromRoot.MultiplyPoint3x4(MapPart(toRoot.MultiplyPoint3x4(p), center, axes, wide, tall)));
            return result;
        }

        private struct ClipPoint
        {
            internal Vertex Vertex;
            internal Vector4 Distances;
            internal float Distance(int edge) => edge == 0 ? Distances.x : edge == 1 ? Distances.y : edge == 2 ? Distances.z : Distances.w;
            internal static ClipPoint Lerp(ClipPoint a, ClipPoint b, float t) => new ClipPoint {
                Vertex = Vertex.Lerp(a.Vertex, b.Vertex, t), Distances = a.Distances + (b.Distances - a.Distances) * t };
        }
        private sealed class ClipWorkspace
        {
            internal bool Busy;
            internal readonly List<Vector3> Points = new List<Vector3>(), Normals = new List<Vector3>();
            internal readonly List<Vector4> Tangents = new List<Vector4>();
            internal readonly List<Color> Colors = new List<Color>();
            internal readonly List<ClipPoint> Input = new List<ClipPoint>();
            internal readonly List<ClipPoint> Polygon = new List<ClipPoint>(8), Scratch = new List<ClipPoint>(8);
            internal readonly List<int> Triangles = new List<int>();
            internal readonly List<List<int>> Slots = new List<List<int>>();
            internal readonly List<Vector4>[] Uv = { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
            internal readonly List<Vector3> OutputPoints = new List<Vector3>(), OutputNormals = new List<Vector3>();
            internal readonly List<Vector4> OutputTangents = new List<Vector4>();
            internal readonly List<Color> OutputColors = new List<Color>();
            internal readonly List<Vector4>[] OutputUv = { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
            internal void Add(Vertex vertex, List<int> indices)
            {
                indices.Add(OutputPoints.Count); OutputPoints.Add(vertex.Point); OutputNormals.Add(vertex.Normal);
                OutputTangents.Add(vertex.Tangent); OutputColors.Add(vertex.Color);
                OutputUv[0].Add(vertex.Uv0); OutputUv[1].Add(vertex.Uv1); OutputUv[2].Add(vertex.Uv2); OutputUv[3].Add(vertex.Uv3);
            }
        }
        [ThreadStatic] private static ClipWorkspace s_clipWorkspace;

        // Reuse CPU buffers as well as the output Mesh. Project each input
        // vertex once; homogeneous edge distances interpolate across a triangle.
        internal static Mesh ClipMesh(Mesh original, Transform transform, Camera camera, Rect viewport, Mesh result = null)
        {
            ClipWorkspace work = s_clipWorkspace ?? (s_clipWorkspace = new ClipWorkspace());
            if (work.Busy) work = new ClipWorkspace(); // Keep nested callers independent.
            work.Busy = true;
            try
            {
                Mesh source = original.isReadable ? original : MixedPremiumMesh.GetReadableCopy(original);
                source.GetVertices(work.Points); source.GetNormals(work.Normals);
                source.GetTangents(work.Tangents); source.GetColors(work.Colors);
                int count = work.Points.Count, subCount = source.subMeshCount;
                for (int channel = 0; channel < 4; channel++) { work.Uv[channel].Clear(); source.GetUVs(channel, work.Uv[channel]); work.OutputUv[channel].Clear(); }
                work.Input.Clear(); work.OutputPoints.Clear(); work.OutputNormals.Clear(); work.OutputTangents.Clear(); work.OutputColors.Clear();
                for (int i = 0; i < count; i++)
                {
                    Vector3 point = camera.WorldToScreenPoint(transform.TransformPoint(work.Points[i]));
                    float weight = camera.orthographic ? 1f : point.z;
                    work.Input.Add(new ClipPoint { Distances = new Vector4(point.x - viewport.xMin, viewport.xMax - point.x,
                        point.y - viewport.yMin, viewport.yMax - point.y) * weight, Vertex = new Vertex {
                        Point = work.Points[i], Normal = work.Normals.Count == count ? work.Normals[i] : Vector3.zero,
                        Tangent = work.Tangents.Count == count ? work.Tangents[i] : Vector4.zero,
                        Color = work.Colors.Count == count ? work.Colors[i] : Color.white,
                        Uv0 = work.Uv[0].Count == count ? work.Uv[0][i] : Vector4.zero,
                        Uv1 = work.Uv[1].Count == count ? work.Uv[1][i] : Vector4.zero,
                        Uv2 = work.Uv[2].Count == count ? work.Uv[2][i] : Vector4.zero,
                        Uv3 = work.Uv[3].Count == count ? work.Uv[3][i] : Vector4.zero } });
                }
                while (work.Slots.Count < subCount) work.Slots.Add(new List<int>());
                for (int sub = 0; sub < subCount; sub++)
                {
                    List<int> indices = work.Slots[sub]; indices.Clear();
                    work.Triangles.Clear(); source.GetTriangles(work.Triangles, sub);
                    for (int i = 0; i + 2 < work.Triangles.Count; i += 3)
                    {
                        List<ClipPoint> polygon = work.Polygon, scratch = work.Scratch; polygon.Clear();
                        polygon.Add(work.Input[work.Triangles[i]]); polygon.Add(work.Input[work.Triangles[i + 1]]); polygon.Add(work.Input[work.Triangles[i + 2]]);
                        for (int edge = 0; edge < 4 && polygon.Count >= 3; edge++)
                        {
                            scratch.Clear(); ClipPoint a = polygon[polygon.Count - 1]; float da = a.Distance(edge);
                            foreach (ClipPoint b in polygon)
                            {
                                float db = b.Distance(edge);
                                if ((da < 0) != (db < 0)) scratch.Add(ClipPoint.Lerp(a, b, da / (da - db)));
                                if (db >= 0) scratch.Add(b);
                                a = b; da = db;
                            }
                            List<ClipPoint> swap = polygon; polygon = scratch; scratch = swap;
                        }
                        for (int j = 1; j + 1 < polygon.Count; j++)
                        { work.Add(polygon[0].Vertex, indices); work.Add(polygon[j].Vertex, indices); work.Add(polygon[j + 1].Vertex, indices); }
                    }
                }
                if (result == null) result = new Mesh { name = original.name + "_BCM_Clipped" }; else result.Clear();
                if (work.OutputPoints.Count > 65535) result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                result.SetVertices(work.OutputPoints); result.subMeshCount = subCount;
                if (work.Normals.Count == count) result.SetNormals(work.OutputNormals);
                if (work.Tangents.Count == count) result.SetTangents(work.OutputTangents);
                if (work.Colors.Count == count) result.SetColors(work.OutputColors);
                for (int channel = 0; channel < 4; channel++) if (work.Uv[channel].Count == count) result.SetUVs(channel, work.OutputUv[channel]);
                for (int sub = 0; sub < subCount; sub++) result.SetTriangles(work.Slots[sub], sub, false);
                result.RecalculateBounds(); return result;
            }
            finally { work.Busy = false; }
        }

        // Native buttons may shade through vertex tint or through their atlas
        // texture. Sample the corresponding slice of one row-wide gradient.
        // Texture sampling is restricted to the interior so gold border UVs,
        // corner thickness and neighbouring atlas regions remain intact.
        private static Vertex SpreadGradient(Vertex vertex, Vertex a, Vertex b, Vertex c, Axes axes, Bounds gradient,
            int column, int columns, bool interior)
        {
            if (column < 0 || columns <= 1) return vertex;
            float bx = b.Point.x - a.Point.x, bz = b.Point.z - a.Point.z;
            float cx = c.Point.x - a.Point.x, cz = c.Point.z - a.Point.z;
            float determinant = bx * cz - cx * bz;
            if (Mathf.Abs(determinant) < 0.000001f) return vertex;
            Vector4 ca = ToVector(a.Color), cb = ToVector(b.Color), cc = ToVector(c.Color);
            Vector4 slope = ShiftAttribute(Vector4.zero, ca, cb, cc, cz / determinant, -bz / determinant);
            bool tinted = Mathf.Abs(slope.x) + Mathf.Abs(slope.y) + Mathf.Abs(slope.z) + Mathf.Abs(slope.w) > 0.0001f;
            float low = gradient.min.x, width = gradient.size.x;
            float fraction = Mathf.Clamp01((vertex.Point.x - low) / Mathf.Max(width, 0.0001f));
            float delta = low + (column + fraction) * width / columns - vertex.Point.x;
            if (tinted)
            {
                Vector4 color = ShiftAttribute(ToVector(vertex.Color), ca, cb, cc, delta * cz / determinant, -delta * bz / determinant);
                vertex.Color = new Color(color.x, color.y, color.z, color.w);
            }
            else if (interior)
            {
                low = Mathf.Max(gradient.min.x, axes.Source.min.x + axes.BorderX);
                width = Mathf.Min(gradient.max.x, axes.Source.max.x - axes.BorderX) - low;
                if (width <= 0.0001f) return vertex;
                fraction = Mathf.Clamp01((vertex.Point.x - low) / Mathf.Max(width, 0.0001f));
                delta = low + (column + fraction) * width / columns - vertex.Point.x;
                float wb = delta * cz / determinant, wc = -delta * bz / determinant;
                vertex.Uv0 = ShiftAttribute(vertex.Uv0, a.Uv0, b.Uv0, c.Uv0, wb, wc);
                vertex.Uv1 = ShiftAttribute(vertex.Uv1, a.Uv1, b.Uv1, c.Uv1, wb, wc);
                vertex.Uv2 = ShiftAttribute(vertex.Uv2, a.Uv2, b.Uv2, c.Uv2, wb, wc);
                vertex.Uv3 = ShiftAttribute(vertex.Uv3, a.Uv3, b.Uv3, c.Uv3, wb, wc);
            }
            return vertex;
        }

        private static Vector4 ToVector(Color color) => new Vector4(color.r, color.g, color.b, color.a);
        private static Vector4 ShiftAttribute(Vector4 value, Vector4 a, Vector4 b, Vector4 c, float wb, float wc)
            => new Vector4(value.x + (b.x - a.x) * wb + (c.x - a.x) * wc,
                value.y + (b.y - a.y) * wb + (c.y - a.y) * wc,
                value.z + (b.z - a.z) * wb + (c.z - a.z) * wc,
                value.w + (b.w - a.w) * wb + (c.w - a.w) * wc);

        private static void Split(List<List<Vertex>> polygons, float boundary, bool xAxis)
        {
            int count = polygons.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                List<Vertex> polygon = polygons[i];
                var low = new List<Vertex>(); var high = new List<Vertex>();
                Vertex a = polygon[polygon.Count - 1];
                float da = (xAxis ? a.Point.x : a.Point.z) - boundary;
                foreach (Vertex b in polygon)
                {
                    float db = (xAxis ? b.Point.x : b.Point.z) - boundary;
                    if ((da < 0f && db > 0f) || (da > 0f && db < 0f))
                    {
                        Vertex cut = Vertex.Lerp(a, b, da / (da - db));
                        low.Add(cut); high.Add(cut);
                    }
                    if (db <= 0f) low.Add(b);
                    if (db >= 0f) high.Add(b);
                    a = b; da = db;
                }
                // A polygon wholly on a slice boundary must not be duplicated.
                if (low.Count < 3 || high.Count < 3) continue;
                bool hasLow = false, hasHigh = false;
                foreach (Vertex v in polygon)
                {
                    float d = (xAxis ? v.Point.x : v.Point.z) - boundary;
                    hasLow |= d < 0f; hasHigh |= d > 0f;
                }
                if (!hasLow || !hasHigh) continue;
                polygons[i] = low; polygons.Add(high);
            }
        }

        public void Dispose()
        {
            Restore();
        }
    }
}
