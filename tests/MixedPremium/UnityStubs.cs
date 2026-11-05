using System;
using System.Collections.Generic;
using System.Linq;

// Managed test doubles: the production clipper runs without a Unity player.
namespace UnityEngine
{
    public static class Resources
    {
        public static Object[] Objects = Array.Empty<Object>();
        public static T[] FindObjectsOfTypeAll<T>() where T : Object => Objects.OfType<T>().Where(obj => !obj.Destroyed).ToArray();
    }
    public class Object
    {
        public bool Destroyed;
        public string name;
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            obj.Destroyed = true;
            if (obj is GameObject go) go.transform.SetParent(null, false);
        }
        public static void DestroyImmediate(Object obj)
        {
            Destroy(obj);
            if (obj is Component component) component.gameObject.RemoveComponent(component);
        }
        public static GameObject Instantiate(GameObject source) => Instantiate(source, null, false);
        public static GameObject Instantiate(GameObject source, Transform parent, bool worldPositionStays)
        {
            if (source.name == "NativePackageRecipeFixture")
            {
                if (parent == null || parent.gameObject.activeInHierarchy) throw new InvalidOperationException("Recipe must be cloned inactive");
                var copy = global::PackageRecipeFixture.Make(parent);
                copy.GetComponent<global::DeckTemplatePickerButton>().m_deckTexture.sharedMaterial = source.GetComponentsInChildren<Renderer>(true)
                    .First(renderer => renderer.gameObject.name == "DeckTexture" || renderer.gameObject.name == "BCM_PackagePortrait").sharedMaterial;
                copy.GetComponent<global::DeckTemplatePickerButton>().m_packRibbon.sharedMaterial = source.GetComponentsInChildren<Renderer>(true)
                    .First(renderer => renderer.gameObject.name == "Class ribbon" || renderer.gameObject.name == "BCM_PackageClassRibbon").sharedMaterial;
                if (source.GetComponent<global::DeckTemplatePickerButton>()?.Destroyed != false)
                {
                    var button = copy.GetComponent<global::DeckTemplatePickerButton>();
                    button.m_title.gameObject.name = "BCM_PackageTitle";
                    button.m_deckTexture.gameObject.name = "BCM_PackagePortrait";
                    button.m_packRibbon.gameObject.name = "BCM_PackageClassRibbon";
                    for (int i = 0; i < button.m_cardCountTexts.Count; i++)
                        button.m_cardCountTexts[i].gameObject.name = i == 0 ? "BCM_PackageCount" : "BCM_UnusedPackageCount";
                    button.m_incompleteTextRibbon.SetActive(false);
                    button.m_selectGlow.name = "BCM_PackageGlow";
                    if (button.m_completeTextRibbon != null) button.m_completeTextRibbon.name = "BCM_PackageBadgeComplete";
                    DestroyImmediate(button);
                    foreach (var part in copy.GetComponentsInChildren<global::AnimatedLowPolyPack>(true)) DestroyImmediate(part);
                    foreach (var part in copy.GetComponentsInChildren<global::RandomTransform>(true)) DestroyImmediate(part);
                }
                return copy;
            }
            if (source.GetComponent<global::AdjustBuyingAmount>() != null)
            {
                if (parent == null || parent.gameObject.activeInHierarchy) throw new InvalidOperationException("Shop quantity must be cloned inactive");
                return global::AdjustBuyingAmount.MakePrefab(parent);
            }
            if (source.GetComponent<global::StoreSendToBAM>() != null)
            {
                if (parent == null || parent.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Shop artwork must be cloned under an inactive parent");
                return global::StoreSendToBAM.MakePrefab(parent);
            }
            if (source.GetComponent<global::StoreQuantityPrompt>() != null || source.GetComponent<global::CardInfoPane>() != null)
            {
                if (parent == null || parent.gameObject.activeInHierarchy) throw new InvalidOperationException("Native pane must be cloned inactive");
                return source.GetComponent<global::StoreQuantityPrompt>() != null
                    ? global::StoreQuantityPrompt.MakePrefab(parent) : global::CardInfoPane.MakePrefab(parent);
            }
            var go = new GameObject(source.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = source.transform.localPosition;
            go.transform.localRotation = source.transform.localRotation;
            go.transform.localScale = source.transform.localScale;
            if (source.GetComponent<global::GemObject>() != null) go.AddComponent<global::GemObject>();
            if (source.GetComponent<global::CardRuneBanner>() is global::CardRuneBanner banner)
                go.AddComponent<global::CardRuneBanner>().Current = banner.Current;
            if (source.GetComponent<global::UIBButton>() is global::UIBButton nativeButton)
            {
                var clone = go.AddComponent<global::UIBButton>();
                clone.Template = nativeButton;
                clone.m_ClickDownOffset = nativeButton.m_ClickDownOffset;
                clone.m_DepressTime = nativeButton.m_DepressTime; clone.m_RaiseTime = nativeButton.m_RaiseTime;
                bool sharedFace = nativeButton.m_ButtonText != null && nativeButton.m_ButtonText.gameObject == nativeButton.m_RootObject;
                if (nativeButton.m_ButtonText != null && !sharedFace)
                    clone.m_ButtonText = Instantiate(nativeButton.m_ButtonText.gameObject, go.transform, false).GetComponent<global::UberText>();
                if (nativeButton.m_RootObject != null) clone.m_RootObject = Instantiate(nativeButton.m_RootObject, go.transform, false);
                if (sharedFace) clone.m_ButtonText = clone.m_RootObject.GetComponent<global::UberText>();
                var hit = go.AddComponent<BoxCollider>();
                if (source.GetComponent<BoxCollider>() is BoxCollider sourceHit) { hit.center = sourceHit.center; hit.size = sourceHit.size; }
            }
            if (source.GetComponent<global::UIBHighlight>() is global::UIBHighlight nativeHighlight)
            {
                var highlight = go.AddComponent<global::UIBHighlight>();
                highlight.m_MouseOverSound = nativeHighlight.m_MouseOverSound; highlight.m_MouseOutSound = nativeHighlight.m_MouseOutSound;
                highlight.m_MouseDownSound = nativeHighlight.m_MouseDownSound; highlight.m_MouseUpSound = nativeHighlight.m_MouseUpSound;
                highlight.m_ReleaseDragSound = nativeHighlight.m_ReleaseDragSound;
                if (nativeHighlight.m_MouseOverHighlight != null)
                {
                    highlight.m_MouseOverHighlight = Instantiate(nativeHighlight.m_MouseOverHighlight, go.transform, false);
                    highlight.m_MouseOverHighlight.SetActive(nativeHighlight.m_MouseOverHighlight.activeSelf);
                }
            }
            if (source.GetComponent<global::CheckBox>() is global::CheckBox checkbox)
            {
                var clone = global::CheckBox.MakeTemplate(go);
                clone.SetButtonText(checkbox.m_uberText.Text);
                clone.SetChecked(checkbox.IsChecked());
            }
            if (source.GetComponent<global::DropdownControl>() is global::DropdownControl dropdown)
            {
                var clone = global::DropdownControl.MakeTemplate(go);
                foreach (object item in dropdown.Items) clone.addItem(item);
                clone.setSelection(dropdown.getSelection());
                // Native Settings sections can include unrelated captions next
                // to the dropdown's actual selected field/menu/arrow.
                foreach (Transform child in source.transform.Children)
                    if (child.gameObject.GetComponent<global::UberText>() != null)
                        Instantiate(child.gameObject, go.transform, false);
            }
            if (source.GetComponent<global::MultiSliceElement>() != null)
            {
                go.AddComponent<global::MultiSliceElement>();
                go.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                go.AddComponent<MeshRenderer>();
            }
            if (source.GetComponent<global::UberText>() is global::UberText text)
            {
                var copy = go.AddComponent<global::UberText>();
                foreach (var field in typeof(global::UberText).GetFields(System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
                    field.SetValue(copy, field.GetValue(text));
            }
            if (source.GetComponent<global::SetFilterItem>() is global::SetFilterItem nativeRow)
            {
                var copy = go.AddComponent<global::SetFilterItem>();
                copy.Height = nativeRow.Height; copy.IsHeader = nativeRow.IsHeader;
                copy.Text = nativeRow.Text; copy.FormatType = nativeRow.FormatType; copy.Callback = nativeRow.Callback;
                copy.CardSets = nativeRow.CardSets; copy.SpecificCards = nativeRow.SpecificCards;
                if (source.GetComponent<BoxCollider>() is BoxCollider nativeHit)
                {
                    BoxCollider copyHit = go.AddComponent<BoxCollider>();
                    copyHit.center = nativeHit.center; copyHit.size = nativeHit.size; copyHit.enabled = nativeHit.enabled;
                }
                if (source.GetComponent<global::UIBScrollableItem>() is global::UIBScrollableItem size)
                    go.AddComponent<global::UIBScrollableItem>().m_size = size.m_size;
                go.AddComponent<global::PegUIElement>();
                BetterCollectionManager.KeywordSetTray.AddRowArtwork(copy);
            }
            if (source.GetComponent<MeshFilter>() is MeshFilter sourceMesh && go.GetComponent<MeshFilter>() == null)
                go.AddComponent<MeshFilter>().sharedMesh = sourceMesh.sharedMesh;
            if (source.GetComponent<MeshRenderer>() is MeshRenderer sourceRenderer && go.GetComponent<MeshRenderer>() == null)
            {
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.enabled = sourceRenderer.enabled;
            }
            if (source.GetComponent<BoxCollider>() is BoxCollider nativeBox && go.GetComponent<BoxCollider>() == null)
            {
                var box = go.AddComponent<BoxCollider>(); box.center = nativeBox.center; box.size = nativeBox.size; box.enabled = nativeBox.enabled;
            }
            return go;
        }
    }
    public sealed class Sprite : Object
    {
        public Texture texture = new Texture(); public Vector4 border = new Vector4(5,5,5,5);
    }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum TextureFormat { RGBA32 }
    public enum RenderTextureFormat { ARGB32 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public enum ColorSpace { Gamma, Linear }
    public class Texture : Object
    {
        public int width = 64, height = 64;
        // The marker copies this opaque native enum value without inspecting it.
        public int filterMode = 1;
        public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
        internal Color[] Pixels;
    }
    public sealed class Texture2D : Texture
    {
        public bool isReadable = true;
        public Texture2D(int w, int h, TextureFormat format, bool mipChain)
        { width = w; height = h; Pixels = new Color[w * h]; }
        public Color[] GetPixels()
        {
            if (!isReadable) throw new InvalidOperationException("Texture has discarded CPU pixels");
            return (Color[])Pixels.Clone();
        }
        public void SetPixels(Color[] pixels) { Pixels = (Color[])pixels.Clone(); }
        public void ReadPixels(Rect source, int x, int y, bool recalculateMipMaps)
        { Pixels = (Color[])RenderTexture.active.Pixels.Clone(); }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { isReadable = !makeNoLongerReadable; }
    }
    public sealed class RenderTexture : Texture
    {
        public RenderTextureReadWrite ReadWrite;
        public static RenderTexture active;
        public static int LiveTemporaries;
        public static RenderTexture GetTemporary(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite)
        {
            LiveTemporaries++;
            return new RenderTexture { width = width, height = height, Pixels = new Color[width * height], ReadWrite = readWrite };
        }
        public static void ReleaseTemporary(RenderTexture texture) { LiveTemporaries--; texture.Destroyed = true; }
    }
    // Match the restricted API reported by the real game build.
    public static class GL { }
    public static class Graphics
    {
        public static void ExecuteCommandBuffer(UnityEngine.Rendering.CommandBuffer buffer) { }
        public static int Blits;
        public static bool FailNextBlit;
        public static RenderTextureReadWrite LastReadWrite;
        public static void Blit(Texture source, RenderTexture dest, Vector2 scale, Vector2 offset)
        {
            if (scale.x != 1f || scale.y != 1f || offset.x != 0f || offset.y != 0f)
                throw new InvalidOperationException("Copy must retain the full source UVs");
            Blits++; LastReadWrite = dest.ReadWrite;
            RenderTexture.active = dest;
            if (FailNextBlit) { FailNextBlit = false; throw new InvalidOperationException("GPU copy failed"); }
            dest.Pixels = source.Pixels == null ? new Color[dest.width * dest.height] : (Color[])source.Pixels.Clone();
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => new Vector3(1, 1, 1);
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static float Distance(Vector3 a, Vector3 b) => (float)Math.Sqrt((a.x-b.x)*(a.x-b.x) + (a.y-b.y)*(a.y-b.y) + (a.z-b.z)*(a.z-b.z));
        public Vector3 normalized
        {
            get { float n = (float)Math.Sqrt(x * x + y * y + z * z); return n < 0.00001f ? zero : new Vector3(x / n, y / n, z / n); }
        }
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }
    public struct Rect
    {
        public float xMin, yMin, xMax, yMax;
        public Rect(float x, float y, float width, float height)
        { xMin = x; yMin = y; xMax = x + width; yMax = y + height; }
        public bool Contains(Vector3 point) => point.x >= xMin && point.x <= xMax && point.y >= yMin && point.y <= yMax;
        public float width => xMax - xMin;
        public float height => yMax - yMin;
        public Vector3 center => new Vector3((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, 0);
        public static Rect MinMaxRect(float left, float bottom, float right, float top)
            => new Rect { xMin = left, yMin = bottom, xMax = right, yMax = top };
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x+b.x,a.y+b.y,a.z+b.z,a.w+b.w);
        public static Vector4 operator -(Vector4 a, Vector4 b) => new Vector4(a.x-b.x,a.y-b.y,a.z-b.z,a.w-b.w);
        public static Vector4 operator *(Vector4 a, float b) => new Vector4(a.x*b,a.y*b,a.z*b,a.w*b);
        public static Vector4 zero => default;
        public static Vector4 LerpUnclamped(Vector4 a, Vector4 b, float t) => new Vector4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color LerpUnclamped(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    }
    public struct Vector2Int { public int x, y; public Vector2Int(int x, int y) { this.x=x; this.y=y; } }
    public struct Matrix4x4
    {
        public Vector3 Offset;
        public float Scale;
        private bool m_explicit;
        private Vector4 m_c0, m_c1, m_c2, m_c3;
        public Matrix4x4(Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3)
        { Offset = Vector3.zero; Scale = 1; m_explicit = true; m_c0 = c0; m_c1 = c1; m_c2 = c2; m_c3 = c3; }
        public static Matrix4x4 identity => new Matrix4x4 { Scale = 1 };
        public Vector3 MultiplyPoint3x4(Vector3 p) => m_explicit
            ? new Vector3(m_c0.x * p.x + m_c1.x * p.y + m_c2.x * p.z + m_c3.x,
                m_c0.y * p.x + m_c1.y * p.y + m_c2.y * p.z + m_c3.y,
                m_c0.z * p.x + m_c1.z * p.y + m_c2.z * p.z + m_c3.z)
            : new Vector3(p.x * Scale + Offset.x, p.y * Scale + Offset.y, p.z * Scale + Offset.z);
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            if (!a.m_explicit && !b.m_explicit) return new Matrix4x4 { Scale = a.Scale * b.Scale, Offset = a.MultiplyPoint3x4(b.Offset) };
            Vector3 o = a.MultiplyPoint3x4(b.MultiplyPoint3x4(Vector3.zero));
            Vector3 x = a.MultiplyPoint3x4(b.MultiplyPoint3x4(new Vector3(1, 0, 0))) - o;
            Vector3 y = a.MultiplyPoint3x4(b.MultiplyPoint3x4(new Vector3(0, 1, 0))) - o;
            Vector3 z = a.MultiplyPoint3x4(b.MultiplyPoint3x4(new Vector3(0, 0, 1))) - o;
            return new Matrix4x4(new Vector4(x.x, x.y, x.z, 0), new Vector4(y.x, y.y, y.z, 0),
                new Vector4(z.x, z.y, z.z, 0), new Vector4(o.x, o.y, o.z, 1));
        }
    }
    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 center, size;
        public Vector3 extents => size * 0.5f;
        public Vector3 min => center - size * 0.5f;
        public Vector3 max => center + size * 0.5f;
        public void Encapsulate(Vector3 point)
        {
            Vector3 low = min, high = max;
            low = new Vector3(Math.Min(low.x, point.x), Math.Min(low.y, point.y), Math.Min(low.z, point.z));
            high = new Vector3(Math.Max(high.x, point.x), Math.Max(high.y, point.y), Math.Max(high.z, point.z));
            center = (low + high) * 0.5f; size = high - low;
        }
        public void Encapsulate(Bounds bounds) { Encapsulate(bounds.min); Encapsulate(bounds.max); }
    }
    public struct BoneWeight
    {
        private int m_BoneIndex0, m_BoneIndex1, m_BoneIndex2, m_BoneIndex3;
        private float m_Weight0, m_Weight1, m_Weight2, m_Weight3;
        public int boneIndex0 => m_BoneIndex0;
        public int boneIndex1 => m_BoneIndex1;
        public int boneIndex2 => m_BoneIndex2;
        public int boneIndex3 => m_BoneIndex3;
        public float weight0 => m_Weight0;
        public float weight1 => m_Weight1;
        public float weight2 => m_Weight2;
        public float weight3 => m_Weight3;
        public static BoneWeight TestWeight(int bone)
        {
            return new BoneWeight { m_BoneIndex0 = bone, m_Weight0 = 1f,
                m_BoneIndex1 = 0, m_BoneIndex2 = 0, m_BoneIndex3 = 0,
                m_Weight1 = 0, m_Weight2 = 0, m_Weight3 = 0 };
        }
    }
    public enum MeshTopology { Triangles, Lines }
    public class Mesh : Object
    {
        public bool isReadable = true;
        public int blendShapeCount;
        private Vector3[] m_positions = Array.Empty<Vector3>(), m_normals = Array.Empty<Vector3>();
        public Vector3[] vertices { get { RequireReadable(); return m_positions; } set { m_positions = value; } }
        public Vector3[] normals { get { RequireReadable(); return m_normals; } set { m_normals = value; } }
        public Vector4[] tangents = Array.Empty<Vector4>();
        public Color[] colors = Array.Empty<Color>();
        public BoneWeight[] boneWeights = Array.Empty<BoneWeight>();
        public Matrix4x4[] bindposes = Array.Empty<Matrix4x4>();
        public Bounds bounds;
        public Rendering.IndexFormat indexFormat;
        public int subMeshCount;
        public readonly Dictionary<int, int[]> Triangles = new Dictionary<int, int[]>();
        public readonly Dictionary<int, List<Vector4>> Uvs = new Dictionary<int, List<Vector4>>();
        public MeshTopology Topology = MeshTopology.Triangles;
        public MeshTopology GetTopology(int sub) => Topology;
        public int[] GetTriangles(int sub) { RequireReadable(); return Triangles[sub].Select(i => i + GetSubMesh(sub).baseVertex).ToArray(); }
        public void SetTriangles(List<int> indices, int sub, bool calculateBounds) => Triangles[sub] = indices.ToArray();
        public void Clear()
        {
            m_positions = m_normals = Array.Empty<Vector3>(); tangents = Array.Empty<Vector4>(); colors = Array.Empty<Color>();
            Triangles.Clear(); Uvs.Clear(); bounds = default; subMeshCount = 0;
        }
        public void RecalculateBounds()
        {
            if (m_positions.Length == 0) { bounds = default; return; }
            var lo = new Vector3(m_positions.Min(v => v.x), m_positions.Min(v => v.y), m_positions.Min(v => v.z));
            var hi = new Vector3(m_positions.Max(v => v.x), m_positions.Max(v => v.y), m_positions.Max(v => v.z));
            bounds = new Bounds((lo+hi)*.5f, hi-lo);
        }
        public void GetUVs(int channel, List<Vector4> target) { RequireReadable(); if (Uvs.TryGetValue(channel, out List<Vector4> uv)) target.AddRange(uv); }
        public void SetUVs(int channel, List<Vector4> values) => Uvs[channel] = new List<Vector4>(values);

        public void GetVertices(List<Vector3> target) { RequireReadable(); target.Clear(); target.AddRange(m_positions); }
        public void GetNormals(List<Vector3> target) { RequireReadable(); target.Clear(); target.AddRange(m_normals); }
        public void GetTangents(List<Vector4> target) { RequireReadable(); target.Clear(); target.AddRange(tangents); }
        public void GetColors(List<Color> target) { RequireReadable(); target.Clear(); target.AddRange(colors); }
        public void GetTriangles(List<int> target, int sub) { target.Clear(); target.AddRange(GetTriangles(sub)); }
        public void SetVertices(List<Vector3> values) => m_positions = values.ToArray();
        public void SetNormals(List<Vector3> values) => m_normals = values.ToArray();
        public void SetTangents(List<Vector4> values) => tangents = values.ToArray();
        public void SetColors(List<Color> values) => colors = values.ToArray();

        private void RequireReadable() { if (!isReadable) throw new InvalidOperationException("CPU mesh data was discarded"); }
        private Rendering.VertexAttributeDescriptor[] m_attributes;
        private byte[] m_indices;
        public bool FailGpuRead;
        public int VertexReads, IndexReads, BufferDisposals;
        public readonly Dictionary<int, Rendering.SubMeshDescriptor> SubMeshes = new Dictionary<int, Rendering.SubMeshDescriptor>();
        public int vertexCount => m_positions.Length;
        public int vertexBufferCount => GetVertexAttributes().Select(a => a.stream).DefaultIfEmpty(-1).Max() + 1;
        public Rendering.VertexAttributeDescriptor[] GetVertexAttributes()
        {
            if (m_attributes != null) return m_attributes;
            var attrs = new List<Rendering.VertexAttributeDescriptor>();
            attrs.Add(new Rendering.VertexAttributeDescriptor(Rendering.VertexAttribute.Position, Rendering.VertexAttributeFormat.Float32, 3, 0));
            if (m_normals.Length == vertexCount) attrs.Add(new Rendering.VertexAttributeDescriptor(Rendering.VertexAttribute.Normal, Rendering.VertexAttributeFormat.Float32, 3, 1));
            if (tangents.Length == vertexCount) attrs.Add(new Rendering.VertexAttributeDescriptor(Rendering.VertexAttribute.Tangent, Rendering.VertexAttributeFormat.Float32, 4, 1));
            if (colors.Length == vertexCount) attrs.Add(new Rendering.VertexAttributeDescriptor(Rendering.VertexAttribute.Color, Rendering.VertexAttributeFormat.UNorm8, 4, 1));
            foreach (int channel in Uvs.Keys.OrderBy(i => i))
                attrs.Add(new Rendering.VertexAttributeDescriptor(Rendering.VertexAttribute.TexCoord0 + channel, Rendering.VertexAttributeFormat.Float32, 4, 1));
            return attrs.ToArray();
        }
        private static int AttributeSize(Rendering.VertexAttributeDescriptor a) => a.dimension * (a.format == Rendering.VertexAttributeFormat.UNorm8 ? 1 : 4);
        public int GetVertexBufferStride(int stream) => GetVertexAttributes().Where(a => a.stream == stream).Sum(AttributeSize);
        public void SetVertexBufferParams(int count, params Rendering.VertexAttributeDescriptor[] attrs)
        {
            m_attributes = attrs.ToArray(); m_positions = new Vector3[count];
            foreach (var a in attrs)
            {
                if (a.attribute == Rendering.VertexAttribute.Normal) m_normals = new Vector3[count];
                else if (a.attribute == Rendering.VertexAttribute.Tangent) tangents = new Vector4[count];
                else if (a.attribute == Rendering.VertexAttribute.Color) colors = new Color[count];
                else if (a.attribute >= Rendering.VertexAttribute.TexCoord0)
                    Uvs[(int)a.attribute - (int)Rendering.VertexAttribute.TexCoord0] = Enumerable.Repeat(Vector4.zero, count).ToList();
            }
        }
        private Vector4 AttributeValue(Rendering.VertexAttribute attr, int i)
        {
            if (attr == Rendering.VertexAttribute.Position) { Vector3 p = m_positions[i]; return new Vector4(p.x, p.y, p.z, 0); }
            if (attr == Rendering.VertexAttribute.Normal) { Vector3 n = m_normals[i]; return new Vector4(n.x, n.y, n.z, 0); }
            if (attr == Rendering.VertexAttribute.Tangent) return tangents[i];
            if (attr == Rendering.VertexAttribute.Color) { Color c = colors[i]; return new Vector4(c.r, c.g, c.b, c.a); }
            return Uvs[(int)attr - (int)Rendering.VertexAttribute.TexCoord0][i];
        }
        public GraphicsBuffer GetVertexBuffer(int stream)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < vertexCount; i++)
                foreach (var a in GetVertexAttributes().Where(a => a.stream == stream))
                {
                    Vector4 v = AttributeValue(a.attribute, i);
                    float[] components = { v.x, v.y, v.z, v.w };
                    for (int c = 0; c < a.dimension; c++)
                        if (a.format == Rendering.VertexAttributeFormat.UNorm8) bytes.Add((byte)Math.Round(components[c] * 255));
                        else bytes.AddRange(BitConverter.GetBytes(components[c]));
                }
            return new GraphicsBuffer(bytes.ToArray(), () => { VertexReads++; if (FailGpuRead) throw new InvalidOperationException("GPU readback unavailable"); }, () => BufferDisposals++);
        }
        public void SetVertexBufferData(byte[] data, int dataStart, int bufferStart, int count, int stream, Rendering.MeshUpdateFlags flags)
        {
            int offset = dataStart;
            for (int i = 0; i < vertexCount; i++)
                foreach (var a in GetVertexAttributes().Where(a => a.stream == stream))
                {
                    var parts = new float[4];
                    for (int c = 0; c < a.dimension; c++)
                        if (a.format == Rendering.VertexAttributeFormat.UNorm8) parts[c] = data[offset++] / 255f;
                        else { parts[c] = BitConverter.ToSingle(data, offset); offset += 4; }
                    var v = new Vector4(parts[0], parts[1], parts[2], parts[3]);
                    if (a.attribute == Rendering.VertexAttribute.Position) m_positions[i] = new Vector3(v.x, v.y, v.z);
                    else if (a.attribute == Rendering.VertexAttribute.Normal) m_normals[i] = new Vector3(v.x, v.y, v.z);
                    else if (a.attribute == Rendering.VertexAttribute.Tangent) tangents[i] = v;
                    else if (a.attribute == Rendering.VertexAttribute.Color) colors[i] = new Color(v.x, v.y, v.z, v.w);
                    else Uvs[(int)a.attribute - (int)Rendering.VertexAttribute.TexCoord0][i] = v;
                }
            if (offset - dataStart != count || bufferStart != 0) throw new Exception("Wrong vertex byte count/offset");
        }
        public Rendering.SubMeshDescriptor GetSubMesh(int index)
        {
            return SubMeshes.TryGetValue(index, out var sub) ? sub
                : new Rendering.SubMeshDescriptor { indexStart = Triangles.Where(kv => kv.Key < index).Sum(kv => kv.Value.Length), indexCount = Triangles[index].Length, topology = Topology };
        }
        public GraphicsBuffer GetIndexBuffer()
        {
            int size = indexFormat == Rendering.IndexFormat.UInt16 ? 2 : 4;
            int total = Enumerable.Range(0, subMeshCount).Select(i => GetSubMesh(i).indexStart + GetSubMesh(i).indexCount).Max();
            byte[] data = new byte[total * size];
            for (int sub = 0; sub < subMeshCount; sub++)
            {
                var desc = GetSubMesh(sub);
                for (int i = 0; i < desc.indexCount; i++)
                {
                    byte[] bytes = size == 2 ? BitConverter.GetBytes((ushort)Triangles[sub][i]) : BitConverter.GetBytes(Triangles[sub][i]);
                    Buffer.BlockCopy(bytes, 0, data, (desc.indexStart + i) * size, size);
                }
            }
            return new GraphicsBuffer(data, () => IndexReads++, () => BufferDisposals++);
        }
        public void SetIndexBufferParams(int count, Rendering.IndexFormat format) { indexFormat = format; m_indices = new byte[count * (format == Rendering.IndexFormat.UInt16 ? 2 : 4)]; }
        public void SetIndexBufferData(byte[] data, int dataStart, int bufferStart, int count, Rendering.MeshUpdateFlags flags) => Buffer.BlockCopy(data, dataStart, m_indices, bufferStart, count);
        public void SetSubMesh(int index, Rendering.SubMeshDescriptor desc, Rendering.MeshUpdateFlags flags)
        {
            SubMeshes[index] = desc;
            int size = indexFormat == Rendering.IndexFormat.UInt16 ? 2 : 4;
            Triangles[index] = Enumerable.Range(0, desc.indexCount).Select(i => size == 2 ? (int)BitConverter.ToUInt16(m_indices, (desc.indexStart + i) * size) : BitConverter.ToInt32(m_indices, (desc.indexStart + i) * size)).ToArray();
        }
    }
    public sealed class GraphicsBuffer : IDisposable
    {
        private readonly byte[] m_bytes;
        private readonly Action m_read, m_dispose;
        public GraphicsBuffer(byte[] bytes, Action read, Action dispose) { m_bytes = bytes; m_read = read; m_dispose = dispose; }
        internal byte[] Read(int size, int offset) { m_read(); var data = new byte[size]; Buffer.BlockCopy(m_bytes, offset, data, 0, size); return data; }
        public void Dispose() => m_dispose();
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform node = transform; node != null; node = node.parent)
                if (node.GetComponent<T>() is T component) return component;
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component => gameObject.GetComponentsInChildren<T>(includeInactive);
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : Component { results.Clear(); results.AddRange(GetComponentsInChildren<T>(includeInactive)); }
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        public int layer;
        public string tag = "Untagged";
        public bool CompareTag(string value) => tag == value;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public Transform transform;
        private readonly List<Component> m_components = new List<Component>();
        internal void RemoveComponent(Component component) => m_components.Remove(component);
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this, name = name };
            m_components.Add(transform);
        }
        public void SetActive(bool value)
        {
            activeSelf = value;
            if (value && activeInHierarchy && (GetComponentsInChildren<global::StoreSendToBAM>(true).Any(controller => !controller.Destroyed)
                || GetComponentsInChildren<global::StoreQuantityPrompt>(true).Any(controller => !controller.Destroyed)
                || GetComponentsInChildren<global::CardInfoPane>(true).Any(controller => !controller.Destroyed)))
                throw new InvalidOperationException("The shop controller must be removed before its artwork is activated");
        }
        public static bool RequireConcreteButtonCollider;
        public T AddComponent<T>() where T : Component
        {
            // Unity cannot satisfy UIBButton's RequireComponent(Collider)
            // with an abstract collider on a new, empty GameObject.
            if (RequireConcreteButtonCollider && typeof(T) == typeof(global::UIBButton) && GetComponent<Collider>() == null)
                return null;
            var c = (T)Activator.CreateInstance(typeof(T), true);
            c.gameObject = this; m_components.Add(c);
            // Modal tests exercise the production Awake/Show/Hide lifecycle.
            if (c is global::ButtonListMenu)
                typeof(T).GetMethod("Awake", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.Invoke(c, null);
            return c;
        }
        public T GetComponent<T>() where T : Component => m_components.OfType<T>().FirstOrDefault();
        public T GetComponentInChildren<T>(bool includeInactive) where T : Component => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : Component { results.Clear(); results.AddRange(GetComponentsInChildren<T>(includeInactive)); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var list = new List<T>(m_components.OfType<T>());
            foreach (Transform child in transform.Children)
                if (includeInactive || child.gameObject.activeInHierarchy)
                    list.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            return list.ToArray();
        }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => identity;
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,
            a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
            a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,
            a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z);
        public static Quaternion Inverse(Quaternion q)
        {
            float norm = q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
            return norm > 0 ? new Quaternion(-q.x/norm, -q.y/norm, -q.z/norm, q.w/norm) : identity;
        }
    }
    public sealed class Transform : Component
    {
        public readonly List<Transform> Children = new List<Transform>();
        public Transform parent;
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Quaternion rotation {
            get => parent == null ? localRotation : parent.rotation * localRotation;
            set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value;
        }
        public void SetAsFirstSibling() { if (parent == null) return; parent.Children.Remove(this); parent.Children.Insert(0, this); }
        public void SetAsLastSibling() { if (parent == null) return; parent.Children.Remove(this); parent.Children.Add(this); }
        public Vector3 position
        {
            get => parent == null ? localPosition : parent.TransformPoint(localPosition);
            set => localPosition = parent == null ? value : parent.InverseTransformPoint(value);
        }
        public Vector3 up => new Vector3(0, 1, 0);
        public Vector3 forward => new Vector3(0, -1, 0);
        public float WorldScale => localScale.x * (parent?.WorldScale ?? 1);
        public Vector3 lossyScale
        {
            get
            {
                Vector3 inherited = parent?.lossyScale ?? Vector3.one;
                return new Vector3(localScale.x * inherited.x, localScale.y * inherited.y, localScale.z * inherited.z);
            }
        }
        public Matrix4x4 worldToLocalMatrix => new Matrix4x4 { Scale = 1 / WorldScale, Offset = position * (-1 / WorldScale) };
        public Matrix4x4 localToWorldMatrix => new Matrix4x4 { Scale = WorldScale, Offset = position };
        public void SetParent(Transform target, bool worldPositionStays)
        {
            parent?.Children.Remove(this); parent = target; parent?.Children.Add(this);
        }
        public bool IsChildOf(Transform target)
        {
            for (Transform node = this; node != null; node = node.parent)
                if (node == target) return true;
            return false;
        }
        public Vector3 TransformPoint(Vector3 p)
        {
            Vector3 point = localPosition + new Vector3(p.x * localScale.x, p.y * localScale.y, p.z * localScale.z);
            return parent != null ? parent.TransformPoint(point) : point;
        }
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            Vector3 point = (parent != null ? parent.InverseTransformPoint(p) : p) - localPosition;
            return new Vector3(point.x / localScale.x, point.y / localScale.y, point.z / localScale.z);
        }
    }
    public sealed class Shader : Object
    {
        public static bool MissingCopyShader;
        public static Shader Find(string name) => MissingCopyShader ? null : new Shader { name = name };
    }
    public sealed class Material : Object
    {
        public void CopyPropertiesFromMaterial(Material source) { mainTexture = source.mainTexture; renderQueue = source.renderQueue; shader = source.shader; }
        public Texture mainTexture;
        public bool HasProperty(string name) => true;
        public void SetInt(string name, int value) { }
        public void SetColor(string name, Color value) { }
        public Shader shader;
        public Vector2 mainTextureScale = new Vector2(1f, 1f), mainTextureOffset;
        public int renderQueue;
        public Color color;
        public Material() { }
        public Material(Shader shader) { this.shader = shader; }
        public Material(Material source)
        { mainTexture = source.mainTexture; shader = source.shader; name = source.name; mainTextureScale = source.mainTextureScale; mainTextureOffset = source.mainTextureOffset; renderQueue = source.renderQueue; color = source.color; }
    }
    public sealed class MaterialPropertyBlock { public bool isEmpty { get; set; } = true; public Color Color; public void SetColor(string name, Color color) { Color = color; } }
    public class Renderer : Component
    {
        public bool enabled = true, forceRenderingOff;
        public uint renderingLayerMask = 1;
        public int sortingOrder, sortingLayerID;
        private Material[] m_materials;
        public Material sharedMaterial
        {
            get => m_materials == null ? null : m_materials.FirstOrDefault();
            set => m_materials = value == null ? Array.Empty<Material>() : new[] { value };
        }
        public Material[] sharedMaterials { get => m_materials ?? Array.Empty<Material>(); set => m_materials = value; }
        public void GetSharedMaterials(List<Material> result) { result.Clear(); result.AddRange(sharedMaterials); }
        private Color m_propertyColor;
        public void GetPropertyBlock(MaterialPropertyBlock block) { block.Color = m_propertyColor; }
        public void GetPropertyBlock(MaterialPropertyBlock block, int index = -1) { block.isEmpty = true; }
        public void SetPropertyBlock(MaterialPropertyBlock block, int index) { m_propertyColor = block.Color; }
        public void SetPropertyBlock(MaterialPropertyBlock block) { m_propertyColor = block.Color; }
        public bool UseLocalBounds;
        public Bounds LocalBounds, WorldBounds;
        public virtual Bounds bounds => UseLocalBounds
            ? new Bounds { center = transform.TransformPoint(LocalBounds.center),
                size = new Vector3(LocalBounds.size.x * Math.Abs(transform.lossyScale.x),
                    LocalBounds.size.y * Math.Abs(transform.lossyScale.y), LocalBounds.size.z * Math.Abs(transform.lossyScale.z)) }
            : WorldBounds;
    }
    public sealed class MeshRenderer : Renderer
    {
        public override Bounds bounds => !UseLocalBounds && WorldBounds.size.Equals(Vector3.zero) && GetComponent<MeshFilter>()?.sharedMesh is Mesh mesh
            ? BetterCollectionManager.FilterTrayGeometry.TransformBounds(mesh.bounds, transform.TransformPoint) : base.bounds;
    }
    public sealed class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh;
        public void BakeMesh(Mesh target)
        {
            target.name = sharedMesh.name; target.vertices = (Vector3[])sharedMesh.vertices.Clone();
            target.normals = (Vector3[])sharedMesh.normals.Clone(); target.colors = (Color[])sharedMesh.colors.Clone();
            target.bounds = sharedMesh.bounds; target.subMeshCount = sharedMesh.subMeshCount;
            foreach (var pair in sharedMesh.Triangles) target.Triangles[pair.Key] = (int[])pair.Value.Clone();
            foreach (var pair in sharedMesh.Uvs) target.Uvs[pair.Key] = new List<Vector4>(pair.Value);
        }
    }
    public sealed class TextMesh : Component { }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public class Collider : Component
    {
        public bool enabled = true;
        public bool isTrigger;
        public virtual Bounds bounds => default;
        public bool Raycast(Ray ray, out object hit, float distance) { hit = null; return false; }
    }
    public sealed class BoxCollider : Collider
    {
        public Vector3 center, size;
        public override Bounds bounds => new Bounds(transform.TransformPoint(center), size * Math.Abs(transform.WorldScale));
    }
    public struct RaycastHit { public Collider collider; }
    public static class Physics
    {
        public static RaycastHit[] Hits = Array.Empty<RaycastHit>();
        public static RaycastHit[] RaycastAll(Ray ray, float distance) => Hits;
        public static int RaycastNonAlloc(Ray ray, RaycastHit[] buffer, float distance, int mask)
        {
            int count = 0;
            foreach (RaycastHit hit in Hits)
                if (hit.collider != null && (mask & (1 << hit.collider.gameObject.layer)) != 0)
                { if (count == buffer.Length) break; buffer[count++] = hit; }
            return count;
        }
    }
    public static class Screen { public static int width = 1920, height = 1080; }
    public sealed class Camera
    {
        public Rect pixelRect => new Rect(0,0,Screen.width,Screen.height);
        public Matrix4x4 worldToCameraMatrix => Matrix4x4.identity;
        public Matrix4x4 projectionMatrix => Matrix4x4.identity;
        public bool enabled = true;
        public bool orthographic = true;
        public float nearClipPlane = .3f, farClipPlane = 1000f;
        public int cullingMask = -1;
        public static Camera[] allCameras => new[] { global::CameraUtils.Camera };
        public static int allCamerasCount => 1;
        public static int GetAllCameras(Camera[] target) { target[0] = global::CameraUtils.Camera; return 1; }
        public readonly Transform transform = new GameObject("Camera").transform;
        public float UpDirection = 1f;
        public float RightDirection = 1f;
        public float PixelScale = 1f;
        public Vector3 Offset;
        public float DepthSkewX, DepthSkewZ;
        public float DepthScale;
        public Vector3 WorldToScreenPoint(Vector3 world) => new Vector3((world.x - DepthSkewX * world.y) * RightDirection * PixelScale + Offset.x,
            (world.z - DepthSkewZ * world.y) * UpDirection * PixelScale + Offset.y, 1f - world.y * DepthScale);
        public Vector3 WorldToViewportPoint(Vector3 world)
        {
            Vector3 screen = WorldToScreenPoint(world);
            return new Vector3(screen.x / Screen.width, screen.y / Screen.height, screen.z);
        }
        public Vector3 ScreenToWorldPoint(Vector3 screen)
        {
            float y = DepthScale == 0f ? 0f : (1f - screen.z) / DepthScale;
            return new Vector3((screen.x - Offset.x) / (RightDirection * PixelScale) + DepthSkewX * y,
                y, (screen.y - Offset.y) / (UpDirection * PixelScale) + DepthSkewZ * y);
        }
        public Ray ScreenPointToRay(Vector3 screen)
        {
            float eyeY = Math.Abs(transform.position.y) > 0.001f ? transform.position.y : 20f;
            Vector3 point = ScreenToWorldPoint(screen);
            float dy = eyeY > 0f ? -1f : 1f;
            return new Ray(new Vector3(point.x + DepthSkewX * eyeY, eyeY, point.z + DepthSkewZ * eyeY),
                new Vector3(DepthSkewX * dy, dy, DepthSkewZ * dy));
        }
    }
    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction; }
        public Vector3 GetPoint(float distance) => origin + direction * distance;
    }
    public struct Plane
    {
        private Vector3 m_point;
        public Plane(Vector3 normal, Vector3 point) { m_point = point; }
        public bool Raycast(Ray ray, out float distance) { distance = (m_point.y - ray.origin.y) / ray.direction.y; return distance >= 0; }
    }
    public enum KeyCode { Return = 13, Escape = 27, KeypadEnter = 271 }
    public static class Input
    {
        public static Vector3 mousePosition;
        public static Vector2 mouseScrollDelta;
        public static bool LeftReleased, RightReleased, LeftPressed, LeftHeld, RightPressed;
        public static bool ReturnPressed, KeypadEnterPressed, EscapePressed;
        public static bool GetKeyDown(KeyCode key) => key == KeyCode.Return ? ReturnPressed
            : key == KeyCode.KeypadEnter ? KeypadEnterPressed : key == KeyCode.Escape && EscapePressed;
        public static bool GetMouseButtonUp(int button) => button == 0 ? LeftReleased : button == 1 && RightReleased;
        public static bool GetMouseButtonDown(int button) => button == 0 ? LeftPressed : button == 1 && RightPressed;
        public static bool GetMouseButton(int button) => button == 0 && LeftHeld;
    }
    public static class Time
    {
        public static int frameCount;
        public static float unscaledDeltaTime = 0.1f;
        public static float unscaledTime;
    }
    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Max(0f, Math.Min(1f, t));
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        public static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
namespace UnityEngine.Rendering
{
    public sealed class CommandBuffer
    {
        public static readonly List<UnityEngine.Material> Drawn = new List<UnityEngine.Material>();
        public void Clear() { }
        public void SetRenderTarget(UnityEngine.RenderTexture target) { }
        public void SetViewProjectionMatrices(UnityEngine.Matrix4x4 view, UnityEngine.Matrix4x4 projection) { }
        public void DrawRenderer(UnityEngine.Renderer renderer, UnityEngine.Material material, int submesh, int pass) { Drawn.Add(material); }
        public void Release() { }
    }

    public static class AsyncGPUReadback
    {
        public static AsyncGPUReadbackRequest Request(UnityEngine.GraphicsBuffer buffer, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null)
            => new AsyncGPUReadbackRequest { Buffer = buffer, Size = size, Offset = offset };
    }
    public struct AsyncGPUReadbackRequest
    {
        internal UnityEngine.GraphicsBuffer Buffer;
        internal int Size, Offset;
        private byte[] m_data;
        public bool done, hasError;
        public void WaitForCompletion()
        {
            try { m_data = Buffer.Read(Size, Offset); }
            catch { hasError = true; }
            done = true;
        }
        public Unity.Collections.NativeArray<T> GetData<T>(int layer = 0) where T : struct
        {
            if (!done || hasError) throw new Exception("Readback not ready");
            return new Unity.Collections.NativeArray<T> { Data = (T[])(object)m_data };
        }
    }
    public enum IndexFormat { UInt16, UInt32 }
    public enum MeshUpdateFlags { DontRecalculateBounds }
    public enum VertexAttribute { Position, Normal, Tangent, Color, TexCoord0, TexCoord1, TexCoord2, TexCoord3 }
    public enum VertexAttributeFormat { Float32, UNorm8 }
    public struct VertexAttributeDescriptor
    {
        public VertexAttribute attribute;
        public VertexAttributeFormat format;
        public int dimension, stream;
        public VertexAttributeDescriptor(VertexAttribute attr, VertexAttributeFormat fmt, int dim, int str) { attribute = attr; format = fmt; dimension = dim; stream = str; }
    }
    public struct SubMeshDescriptor
    {
        public int indexStart, indexCount, baseVertex;
        public UnityEngine.MeshTopology topology;
    }
}
namespace Unity.Collections
{
    public struct NativeArray<T> where T : struct
    {
        internal T[] Data;
        public int Length => Data.Length;
        public T[] ToArray() => Data.ToArray();
    }
}

namespace UnityEngine.Sprites
{
    public static class DataUtility
    {
        public static UnityEngine.Vector4 GetOuterUV(UnityEngine.Sprite sprite) => new UnityEngine.Vector4(0,0,1,1);
        public static UnityEngine.Vector4 GetInnerUV(UnityEngine.Sprite sprite) => new UnityEngine.Vector4(.1f,.1f,.9f,.9f);
    }
}
