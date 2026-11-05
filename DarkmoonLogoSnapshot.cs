using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    // Load assets, then copy renderer data only. Never instantiate a DFT widget,
    // Animator, Spell, Clickable or other client behaviour to make a thumbnail.
    internal static class DarkmoonLogoSnapshot
    {
        internal static Texture2D Read(UnityEngine.Object asset, BoxMenuButton button)
        {
            if (asset is Texture texture) return ReadTexture(texture);
            if (asset is Material material && material.mainTexture != null) return ReadTexture(material.mainTexture);
            var meshes = new List<Mesh>();
            var root = new GameObject("BCM_DarkmoonStaticAsset");
            root.SetActive(false);
            try
            {
                root.transform.position = button.transform.position;
                root.transform.rotation = button.transform.rotation;
                if (asset is GameObject prefab) CopyNode(prefab.transform, root.transform, meshes, true);
                else if (asset is Sprite sprite) root.AddComponent<SpriteRenderer>().sprite = sprite;
                else throw new InvalidOperationException("Unsupported DFT artwork asset type: " + asset.GetType().Name);
                root.SetActive(true);
                return Render(root, CameraUtils.FindProjectionCameraForObject(button.gameObject));
            }
            finally
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                foreach (Mesh mesh in meshes) UnityEngine.Object.Destroy(mesh);
            }
        }

        private static void CopyNode(Transform source, Transform parent, List<Mesh> owned, bool root = false)
        {
            if (!root && !source.gameObject.activeSelf) return;
            var go = new GameObject(source.name + "_Static");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = root ? Vector3.zero : source.localPosition;
            go.transform.localRotation = source.localRotation;
            go.transform.localScale = source.localScale;
            // Text/particles are separate from the entry artwork.
            if (source.GetComponent<UberText>() != null) return;
            Renderer original = source.GetComponent<Renderer>();
            Renderer copy = null;
            if (original != null && original.enabled)
            {
                Mesh mesh = null;
                if (original is SkinnedMeshRenderer skinned)
                { mesh = new Mesh(); owned.Add(mesh); skinned.BakeMesh(mesh); }
                else if (original is MeshRenderer) mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    copy = go.AddComponent<MeshRenderer>();
                }
                else if (original is SpriteRenderer sprite)
                {
                    var result = go.AddComponent<SpriteRenderer>();
                    result.sprite = sprite.sprite; result.color = sprite.color;
                    result.flipX = sprite.flipX; result.flipY = sprite.flipY;
                    copy = result;
                }
                if (copy != null)
                {
                    copy.sharedMaterials = original.sharedMaterials;
                    copy.sortingLayerID = original.sortingLayerID;
                    copy.sortingOrder = original.sortingOrder;
                    var properties = new MaterialPropertyBlock();
                    original.GetPropertyBlock(properties); copy.SetPropertyBlock(properties);
                }
            }
            for (int i = 0; i < source.childCount; i++) CopyNode(source.GetChild(i), go.transform, owned);
        }

        private static Texture2D ReadTexture(Texture source)
        {
            if (source.width <= 0 || source.height <= 0) throw new InvalidOperationException("Empty DFT texture");
            float scale = Math.Min(1f, 1024f / Math.Max(source.width, source.height));
            int width = Math.Max(1, (int)(source.width * scale)), height = Math.Max(1, (int)(source.height * scale));
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D image = null;
            var blitter = new ExpansionIconMarker();
            try
            {
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                blitter.Blit(source, target);
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                image.Apply(false, false);
                return image;
            }
            catch { if (image != null) UnityEngine.Object.Destroy(image); throw; }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                blitter.Clear();
            }
        }

        private static Texture2D Render(GameObject root, Camera source)
        {
            if (source == null) throw new InvalidOperationException("No DFT projection camera");
            var renderers = new List<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
                if (renderer.enabled && renderer.GetComponentInParent<UberText>() == null
                    && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is SpriteRenderer)
                    && renderer.bounds.size.sqrMagnitude > .000001f) renderers.Add(renderer);
            if (renderers.Count == 0) throw new InvalidOperationException("No visible DFT artwork; wait for it to finish loading");

            uint used = 0;
            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                if (renderer.gameObject.activeInHierarchy) used |= 1u << renderer.gameObject.layer;
            int layer = 31;
            while (layer >= 0 && (used & (1u << layer)) != 0) layer--;
            if (layer < 0) throw new InvalidOperationException("No unused render layer for an isolated logo export");

            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (Renderer renderer in renderers)
            {
                Rect rect = DarkmoonLogoArt.ScreenBounds(renderer.bounds, source);
                left = Math.Min(left, rect.xMin); right = Math.Max(right, rect.xMax);
                bottom = Math.Min(bottom, rect.yMin); top = Math.Max(top, rect.yMax);
            }
            float size = Math.Max(right - left, top - bottom) * 1.08f;
            if (size < 8f || float.IsInfinity(size) || float.IsNaN(size))
                throw new InvalidOperationException("DFT artwork has no usable screen bounds");
            int resolution = Math.Max(128, Math.Min(1024, (int)Math.Ceiling(size * 2f)));
            Rect view = source.pixelRect;
            Matrix4x4 crop = Matrix4x4.identity;
            crop.m00 = view.width / size; crop.m11 = view.height / size;
            crop.m03 = (view.center.x - (left + right) * .5f) * 2f / size;
            crop.m13 = (view.center.y - (bottom + top) * .5f) * 2f / size;

            var layers = new Dictionary<GameObject, int>();
            var lights = new Dictionary<Light, int>();
            GameObject cameraObject = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                cameraObject = new GameObject("BCM_DarkmoonLogoCapture");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.CopyFrom(source);
                camera.enabled = false;
                camera.transform.position = source.transform.position;
                camera.transform.rotation = source.transform.rotation;
                camera.worldToCameraMatrix = source.worldToCameraMatrix;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << layer;
                camera.allowHDR = camera.allowMSAA = false;
                target = RenderTexture.GetTemporary(resolution, resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.projectionMatrix = crop * source.projectionMatrix;
                foreach (Renderer renderer in renderers)
                    if (!layers.ContainsKey(renderer.gameObject))
                    { layers.Add(renderer.gameObject, renderer.gameObject.layer); renderer.gameObject.layer = layer; }
                // Preserve native lighting while using the isolated render layer.
                foreach (Light light in Resources.FindObjectsOfTypeAll<Light>())
                    if (light.enabled && light.gameObject.activeInHierarchy)
                    { lights.Add(light, light.cullingMask); light.cullingMask |= 1 << layer; }
                camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
                pixels.Apply(false, false);
                int ink = 0;
                foreach (Color pixel in pixels.GetPixels())
                    if (pixel.a > .05f) ink++;
                if (ink < resolution * resolution / 100)
                    throw new InvalidOperationException("Current DFT asset produced an empty static thumbnail");
                Texture2D result = pixels;
                pixels = null;
                return result;
            }
            finally
            {
                foreach (var item in layers) if (item.Key != null) item.Key.layer = item.Value;
                foreach (var item in lights) if (item.Key != null) item.Key.cullingMask = item.Value;
                RenderTexture.active = previous;
                if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (pixels != null) UnityEngine.Object.Destroy(pixels);
            }
        }
    }
}
