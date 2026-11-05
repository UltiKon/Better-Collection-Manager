using System;
using System.Collections.Generic;
using System.IO;
using Blizzard.T5.AssetManager;
#if BEPINEX
using LogoPaths = BepInEx.Paths;
#else
using MelonLoader.Utils;
#endif
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class DarkmoonLogo
    {
        internal static string CacheDirectory => Path.Combine(
#if BEPINEX
            LogoPaths.CachePath,
#else
            MelonEnvironment.UserDataDirectory,
#endif
            "BetterCollectionManager", "DarkmoonLogos");

        internal static void Refresh(BoxMenuButton button, int offerId)
        {
            try
            {
                var art = button.GetComponent<DarkmoonLogoArt>() ?? button.gameObject.AddComponent<DarkmoonLogoArt>();
                art.RefreshOffer(button, offerId);
            }
            catch (Exception ex) { DebugLog.Exception("Hub: DFT artwork update failed; static access retained", ex); }
        }
    }

    internal sealed class DarkmoonLogoArt : MonoBehaviour
    {
        private BoxMenuButton m_button;
        private Texture2D m_texture;
        private Material m_material;
        private Mesh m_mesh;
        private GameObject m_art;
        private readonly Dictionary<Renderer, bool> m_original = new Dictionary<Renderer, bool>();
        private readonly DarkmoonLogoRequestState m_request = new DarkmoonLogoRequestState();
        private int m_offerId = -1;
        private float m_nextCheck, m_started, m_retryAfter;
        private bool m_pending;
        private string m_failedIdentity;

        internal void RefreshOffer(BoxMenuButton button, int offerId)
        {
            m_button = button;
            if (offerId != m_offerId) { ResetRequest(); m_offerId = offerId; m_nextCheck = 0f; }
            if (Time.unscaledTime < m_nextCheck) return;
            m_nextCheck = Time.unscaledTime + .5f;
            var manager = LuckyDrawManager.Get();
            if (manager == null || manager.IsDataDirty()
                || !manager.TryGetButtonDataModelForBoxID(offerId, out var model) || model == null)
            { ResetRequest(); return; }
            var selection = DarkmoonLogoSource.ReadSelection(offerId, model);
            if (m_failedIdentity == selection.Identity && Time.unscaledTime < m_retryAfter) return;
            if (!m_request.Select(selection.Identity))
            {
                if (m_pending && Time.unscaledTime - m_started > 30f)
                    Failed(m_request.Revision, selection, new TimeoutException("DFT artwork load timed out"));
                return;
            }
            ClearImage();
            m_pending = true;
            m_started = Time.unscaledTime;
            int revision = m_request.Revision;
            DarkmoonLogoSource.Resolve(selection,
                path => ResolveAsset(revision, selection, path),
                error => Failed(revision, selection, error));
        }

        private bool Current(int revision, DarkmoonLogoSelection selection)
        {
            if (this == null || m_button == null || !m_button.gameObject.activeInHierarchy
                || !m_request.IsCurrent(revision)) return false;
            var manager = LuckyDrawManager.Get();
            return manager != null && !manager.IsDataDirty()
                && manager.GetPreferredLuckyDrawID(out int id) && id == selection.OfferId
                && manager.LuckyDrawEnabled(id)
                && manager.TryGetButtonDataModelForBoxID(id, out var model) && model != null
                && DarkmoonLogoSource.ReadSelection(id, model).Identity == selection.Identity;
        }

        private void ResolveAsset(int revision, DarkmoonLogoSelection selection, string path)
        {
            if (!Current(revision, selection)) return;
            string key = DarkmoonLogoSelection.CacheKey(DarkmoonLogoSource.BuildKey, selection.Identity, path);
            string file = Path.Combine(DarkmoonLogo.CacheDirectory, key + ".rgba");
            try
            {
                if (File.Exists(file))
                {
                    try
                    {
                        var image = DarkmoonLogoCache.Read(file);
                        var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false);
                        try { texture.LoadRawTextureData(image.Rgba); texture.Apply(false, false); ApplyImage(texture); }
                        catch { if (m_texture != texture) UnityEngine.Object.Destroy(texture); throw; }
                        m_pending = false;
                        DebugLog.Msg($"Hub: current DFT logo restored from cache; offer={selection.OfferId}, asset='{path}'");
                        return;
                    }
                    catch (Exception ex)
                    {
                        ClearImage();
                        DebugLog.Warning("Hub: rebuilding unreadable DFT logo cache: " + ex.Message);
                    }
                }
                bool queued = AssetLoader.Get().LoadAsset<UnityEngine.Object>(path, (assetRef, handle, data) =>
                {
                    using (handle)
                    {
                        if (!Current(revision, selection)) return;
                        Texture2D image = null;
                        try
                        {
                            if (handle?.Asset == null) throw new InvalidOperationException("DFT artwork asset could not be loaded: " + path);
                            image = DarkmoonLogoSnapshot.Read(handle.Asset, m_button);
                            if (!Current(revision, selection)) { UnityEngine.Object.Destroy(image); return; }
                            ApplyImage(image);
                            m_pending = false;
                            try
                            {
                                DarkmoonLogoCache.WriteAtomic(file, DarkmoonLogoCache.Encode(image.width, image.height, image.GetRawTextureData()));
                                byte[] png = ImageConversion.EncodeToPNG(image);
                                if (png != null && png.Length > 0) DarkmoonLogoCache.WriteAtomic(Path.ChangeExtension(file, ".png"), png);
                            }
                            catch (Exception ex) { DebugLog.Warning("Hub: using current DFT artwork in memory; disk cache unavailable: " + ex.Message); }
                            DebugLog.Msg($"Hub: current DFT artwork cached automatically; offer={selection.OfferId}, kind={selection.Kind}, asset='{path}', key={key}");
                        }
                        catch (Exception ex)
                        {
                            if (image != null && image != m_texture) UnityEngine.Object.Destroy(image);
                            Failed(revision, selection, ex);
                        }
                    }
                });
                if (!queued) throw new InvalidOperationException("DFT artwork is not available for asynchronous loading: " + path);
            }
            catch (Exception ex) { Failed(revision, selection, ex); }
        }

        private void Failed(int revision, DarkmoonLogoSelection selection, Exception error)
        {
            if (this == null || !m_request.IsCurrent(revision)) return;
            m_pending = false;
            m_failedIdentity = selection.Identity;
            m_retryAfter = Time.unscaledTime + 30f;
            m_request.Invalidate();
            ClearImage();
            DebugLog.Exception($"Hub: current DFT artwork unavailable; offer={selection.OfferId}, kind={selection.Kind}; static access retained", error);
        }

        private void ResetRequest()
        {
            m_request.Invalidate();
            m_pending = false;
            m_failedIdentity = null;
            ClearImage();
        }

        private void ApplyImage(Texture2D image)
        {
            BoxMenuButton button = m_button;
            Renderer reference = button.m_ButtonMeshRenderer;
            if (reference == null)
                foreach (Renderer renderer in button.GetComponentsInChildren<Renderer>(true))
                    if (renderer.sharedMaterial != null && renderer.GetComponentInParent<UberText>() == null)
                    { reference = renderer; break; }
            if (reference?.sharedMaterial == null || button.GetComponent<Collider>() == null)
                throw new InvalidOperationException("Native button artwork/material unavailable");
            Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            m_material = shader != null ? new Material(shader) : new Material(reference.sharedMaterial);
            m_material.name = "BCM_StaticDarkmoonLogoMaterial";
            if (m_material.HasProperty("_Color")) m_material.SetColor("_Color", Color.white);
            if (m_material.HasProperty("_StencilComp")) m_material.SetInt("_StencilComp", 8);
            if (m_material.HasProperty("_Stencil")) m_material.SetInt("_Stencil", 0);
            m_material.mainTextureScale = Vector2.one;
            m_material.mainTextureOffset = Vector2.zero;
            m_material.renderQueue = Math.Max(3000, m_material.renderQueue);
            image.wrapMode = TextureWrapMode.Clamp;
            image.filterMode = UnityEngine.FilterMode.Bilinear;
            m_mesh = new Mesh { name = "BCM_StaticDarkmoonLogoMesh" };
            m_mesh.vertices = new Vector3[4];
            m_mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m_mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m_mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m_art = new GameObject("BCM_SavedDarkmoonLogo");
            m_art.transform.SetParent(button.transform, false);
            m_art.layer = reference.gameObject.layer;
            m_art.AddComponent<MeshFilter>().sharedMesh = m_mesh;
            m_art.AddComponent<MeshRenderer>().sharedMaterial = m_material;
            m_texture = image;
            m_material.mainTexture = image;
            if (!UpdateGeometry()) throw new InvalidOperationException("Native logo position is not ready");
            foreach (Renderer renderer in button.GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(m_art.transform)
                    && !renderer.transform.IsChildOf(button.m_TextMesh.transform))
                { m_original[renderer] = renderer.enabled; renderer.enabled = false; }
            DebugLog.Msg("Hub: saved original DFT logo replaces the Collection placeholder");
        }

        private void LateUpdate()
        {
            if (m_art == null) return;
            try { if (!UpdateGeometry()) throw new InvalidOperationException("DFT logo layout is temporarily unavailable"); }
            catch (Exception ex)
            {
                ResetRequest(); m_nextCheck = Time.unscaledTime + 30f;
                DebugLog.Exception("Hub: saved DFT logo layout failed; placeholder restored", ex);
            }
        }

        private bool UpdateGeometry()
        {
            var hit = m_button.GetComponent<Collider>();
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_button.gameObject);
            if (hit == null || camera == null) return false;
            Rect rect = ScreenBounds(hit.bounds, camera);
            float size = Math.Min(rect.width, rect.height);
            if (size < 1f) return false;
            float width = size, height = size;
            if (m_texture.width > m_texture.height) height *= (float)m_texture.height / m_texture.width;
            else width *= (float)m_texture.width / m_texture.height;
            Vector3 center = camera.WorldToScreenPoint(hit.bounds.center);
            var vertices = new Vector3[4];
            float[] x = { -1, 1, 1, -1 }, y = { -1, -1, 1, 1 };
            for (int i = 0; i < 4; i++)
                vertices[i] = m_art.transform.InverseTransformPoint(camera.ScreenToWorldPoint(
                    new Vector3(rect.center.x + x[i] * width * .5f, rect.center.y + y[i] * height * .5f, center.z - .01f)));
            m_mesh.vertices = vertices;
            m_mesh.RecalculateBounds();
            return true;
        }

        internal static Rect ScreenBounds(Bounds bounds, Camera camera)
        {
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                Vector3 p = camera.WorldToScreenPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z));
                if (p.z <= 0f) throw new InvalidOperationException("DFT artwork is behind the projection camera");
                left = Math.Min(left, p.x); right = Math.Max(right, p.x);
                bottom = Math.Min(bottom, p.y); top = Math.Max(top, p.y);
            }
            return Rect.MinMaxRect(left, bottom, right, top);
        }

        private void Restore()
        {
            foreach (var item in m_original) if (item.Key != null) item.Key.enabled = item.Value;
            m_original.Clear();
            if (m_art != null) m_art.SetActive(false);
        }

        private void ClearImage()
        {
            Restore();
            if (m_art != null) UnityEngine.Object.Destroy(m_art);
            if (m_mesh != null) UnityEngine.Object.Destroy(m_mesh);
            if (m_material != null) UnityEngine.Object.Destroy(m_material);
            if (m_texture != null) UnityEngine.Object.Destroy(m_texture);
            m_art = null; m_mesh = null; m_material = null; m_texture = null;
        }

        private void OnDisable() { ResetRequest(); m_nextCheck = 0f; }
        private void OnDestroy() { ResetRequest(); }
    }
}
