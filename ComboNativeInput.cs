using System;
using System.Collections.Generic;
using System.Reflection;
using Blizzard.T5.AssetManager;
using UnityEngine;
using UnityEngine.UI;

namespace BetterCollectionManager
{
    internal sealed class ComboInputTemplate : IDisposable
    {
        private readonly AssetHandle<GameObject> m_asset;
        internal readonly DropdownControl Field;
        internal readonly Image Background;
        internal UberText Style => Field.m_selectedItem.m_text;
        private ComboInputTemplate(DropdownControl field, Image background, AssetHandle<GameObject> asset)
        { Field = field; Background = background; m_asset = asset; }
        internal static ComboInputTemplate Load()
        {
            // Reuse the actual input background sprite, rather than the combined
            // dropdown artwork whose right-hand arrow is baked into its frame.
            Image background = typeof(InputFieldUI).GetField("m_inputFieldBackgroundImage", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(OverlayUI.Get()?.m_inputFieldUI) as Image;
            if (background == null) throw new InvalidOperationException("Native text input background is unavailable.");
            AssetHandle<GameObject> asset = AssetLoader.Get().LoadAsset<GameObject>("OptionsMenu.prefab:a6e5621068fd7c8429475b3e1a1aa991");
            OptionsMenu menu = asset?.Asset?.GetComponent<OptionsMenu>();
            DropdownControl field = menu?.m_graphicsQuality ?? menu?.m_signatureTextDisplayDropdown;
            if (field?.m_selectedItem?.m_text == null) { asset?.Dispose(); throw new InvalidOperationException("Native input font template is unavailable."); }
            return new ComboInputTemplate(field, background, asset);
        }
        public void Dispose() { m_asset?.Dispose(); }
    }

    internal sealed class ComboNativeInput : IDisposable
    {
        internal readonly GameObject Root;
        private readonly Sprite m_sprite;
        private readonly Color m_color;
        private readonly Material m_material;
        private readonly Mesh m_mesh;
        private readonly float m_pixelsPerUnit;
        internal ComboNativeInput(Transform parent, Image background, string name)
        {
            Root = new GameObject(name); Root.SetActive(false); Root.transform.SetParent(parent, false);
            m_sprite = background.sprite; m_color = new Color(.91f, .85f, .69f, 1f); m_pixelsPerUnit = Math.Max(.001f, background.pixelsPerUnit);
            m_material = new Material(background.material); m_material.mainTexture = m_sprite != null ? m_sprite.texture : background.mainTexture;
            if (m_material.HasProperty("_Color")) m_material.SetColor("_Color", Color.white);
            // The sprite is rendered in the menu's world geometry, outside its
            // original Canvas/Mask. Retain its native shader and atlas texture.
            if (m_material.HasProperty("_StencilComp")) m_material.SetInt("_StencilComp", 8);
            if (m_material.HasProperty("_Stencil")) m_material.SetInt("_Stencil", 0);
            m_mesh = new Mesh { name = name + "_NativeSprite", subMeshCount = 1 };
            Root.AddComponent<MeshFilter>().sharedMesh = m_mesh;
            Root.AddComponent<MeshRenderer>().sharedMaterial = m_material;
            LayerUtils.SetLayer(Root.transform, GameLayer.HighPriorityUI);
        }
        internal void Layout(Camera camera, Rect target, float depth, int paperQueue)
        {
            Root.SetActive(true); Root.transform.localScale = Vector3.one;
            Root.transform.position = camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth));
            m_material.renderQueue = Math.Max(m_material.renderQueue, paperQueue + 1);
            Vector4 border = m_sprite != null ? m_sprite.border : Vector4.zero;
            Vector4 outer = m_sprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(m_sprite) : new Vector4(0,0,1,1);
            Vector4 inner = m_sprite != null ? UnityEngine.Sprites.DataUtility.GetInnerUV(m_sprite) : outer;
            float edge = Math.Max(1f, Math.Min(3f, target.height / 25f));
            float left = Math.Max(edge, border.x / m_pixelsPerUnit), right = Math.Max(edge, border.z / m_pixelsPerUnit);
            float bottom = Math.Max(edge, border.y / m_pixelsPerUnit), top = Math.Max(edge, border.w / m_pixelsPerUnit);
            float horizontal = Math.Min(1f, target.width / (left + right)), vertical = Math.Min(1f, target.height / (bottom + top));
            float[] x = { target.xMin, target.xMin + left * horizontal, target.xMax - right * horizontal, target.xMax };
            float[] y = { target.yMin, target.yMin + bottom * vertical, target.yMax - top * vertical, target.yMax };
            float[] u = { outer.x, inner.x, inner.z, outer.z }, v = { outer.y, inner.y, inner.w, outer.w };
            var vertices = new Vector3[16]; var uv = new List<Vector4>(16); var colors = new Color[16];
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
            {
                int index = row * 4 + column;
                vertices[index] = Root.transform.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(x[column], y[row], depth)));
                uv.Add(new Vector4(u[column], v[row], 0, 0));
                colors[index] = row == 0 || row == 3 || column == 0 || column == 3 ? new Color(.40f, .32f, .19f, 1f) : m_color;
            }
            var triangles = new List<int>(54);
            for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++)
            {
                int first = row * 4 + column;
                triangles.AddRange(new[] { first, first + 4, first + 1, first + 1, first + 4, first + 5 });
            }
            m_mesh.vertices = vertices; m_mesh.colors = colors; m_mesh.SetUVs(0, uv);
            m_mesh.SetTriangles(triangles, 0, false); m_mesh.RecalculateBounds();
        }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(m_mesh); UnityEngine.Object.Destroy(m_material); UnityEngine.Object.Destroy(Root);
        }
    }
}
