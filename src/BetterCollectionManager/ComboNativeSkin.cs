using System;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    // Keep the complete Product Information artwork hierarchy. Remove its shop
    // controller while inactive, before it can register callbacks or URL actions.
    internal sealed class ComboNativeSkin : IDisposable
    {
        private readonly AssetHandle<GameObject> m_asset;
        internal readonly GameObject Root, Frame, Body;
        internal readonly UberText HeaderText;
        internal readonly StoreSendToBAM Template;
        private readonly MultiSliceElement m_sections;
        private readonly UIBButton m_save, m_cancel;
        private readonly Vector3 m_frameScale, m_bodyScale;
        private ComboInputTemplate m_inputTemplate;
        private readonly System.Collections.Generic.Dictionary<string, ComboNativeInput> m_inputs = new System.Collections.Generic.Dictionary<string, ComboNativeInput>();
        private readonly System.Collections.Generic.Dictionary<Renderer, Material[]> m_controlMaterials = new System.Collections.Generic.Dictionary<Renderer, Material[]>();
        private bool m_inputLoaded;
        internal UberText InputStyle => m_inputTemplate?.Style;
        internal Rect SaveArea, CancelArea, ContentArea;

        internal ComboNativeSkin(Transform parent)
        {
            m_asset = AssetLoader.Get().LoadAsset<GameObject>((string)ShopPrefabs.ShopSendToBamPrefab);
            try
            {
                Template = m_asset?.Asset?.GetComponent<StoreSendToBAM>();
                if (Template?.m_sendToBAMRoot == null || Template.m_midSection == null || Template.m_allSections == null
                    || Template.m_headlineText == null || Template.m_messageText == null
                    || Template.m_okayButton == null || Template.m_cancelButton == null)
                    throw new InvalidOperationException("Native Product Information artwork is unavailable.");
                Root = new GameObject("BCM_ComboNativeSkin"); Root.SetActive(false);
                Root.transform.SetParent(parent, false);
                GameObject shell = UnityEngine.Object.Instantiate(m_asset.Asset, Root.transform, false);
                shell.SetActive(true); // Its parent is still inactive.
                StoreSendToBAM controller = shell.GetComponent<StoreSendToBAM>();
                Frame = controller.m_sendToBAMRoot; Frame.name = "BCM_ComboProductInformation";
                Body = controller.m_midSection; Body.name = "BCM_ComboParchment";
                HeaderText = controller.m_headlineText;
                m_sections = controller.m_allSections; m_save = controller.m_okayButton; m_cancel = controller.m_cancelButton;
                controller.m_messageText.Hide();
                if (controller.m_miniSummary != null) controller.m_miniSummary.gameObject.SetActive(false);
                foreach (Collider hit in shell.GetComponentsInChildren<Collider>(true)) hit.enabled = false;
                foreach (Renderer renderer in shell.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(Frame.transform)) renderer.enabled = false;
                // Delayed Destroy would let Awake run when the artwork is shown.
                UnityEngine.Object.DestroyImmediate(controller);
                Frame.SetActive(true); Body.SetActive(true);
                HeaderText.GameStringLookup = HeaderText.RichText = false;
                if (HeaderText.Width > 0f) HeaderText.ResizeToFit = true;
                HeaderText.ResizeToFitAndGrow = false;
                m_frameScale = Frame.transform.localScale; m_bodyScale = Body.transform.localScale;
                LayerUtils.SetLayer(m_sections, GameLayer.HighPriorityUI);
            }
            catch { if (Root != null) UnityEngine.Object.Destroy(Root); m_asset?.Dispose(); throw; }
        }

        internal void Layout(Camera camera, Rect panel, Rect header, float depth, float font, string title)
        {
            Root.SetActive(true);
            m_save.gameObject.SetActive(true); m_cancel.gameObject.SetActive(true);
            Frame.transform.localScale = m_frameScale; Body.transform.localScale = m_bodyScale;
            HeaderText.Text = title; HeaderText.Show(); HeaderText.UpdateNow(true);
            ComboSceneGeometry.FaceCaption(Frame.transform, HeaderText, camera);
            var target = Rect.MinMaxRect(panel.xMin, panel.yMin, panel.xMax, header.yMax);
            Frame.transform.position = camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth));
            for (int pass = 0; pass < 3; pass++)
            {
                m_sections.UpdateSlices();
                if (!ComboSceneGeometry.ScreenBounds(Frame, camera, out Rect bounds))
                    throw new InvalidOperationException("Native Product Information frame has no geometry.");
                Frame.transform.localScale *= target.width / bounds.width;
                if (!ComboSceneGeometry.ScreenBounds(Frame, camera, out bounds)
                    || !ComboSceneGeometry.ScreenBounds(Body, camera, out Rect paper)) break;
                float desiredHeight = Math.Max(font * 8f, paper.height + target.height - bounds.height);
                Vector3 scale = Body.transform.localScale;
                Body.transform.localScale = new Vector3(scale.x, scale.y, scale.z * desiredHeight / paper.height);
            }
            m_sections.UpdateSlices();
            if (ComboSceneGeometry.ScreenBounds(Frame, camera, out Rect frameBounds))
                Frame.transform.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth))
                    - camera.ScreenToWorldPoint(new Vector3(frameBounds.center.x, frameBounds.center.y, depth));
            ComboSceneGeometry.ScreenBounds(Body, camera, out ContentArea);
            ComboSceneGeometry.ScreenBounds(m_save.gameObject, camera, out SaveArea);
            ComboSceneGeometry.ScreenBounds(m_cancel.gameObject, camera, out CancelArea);
            m_save.gameObject.SetActive(false); m_cancel.gameObject.SetActive(false);
            ComboSceneGeometry.InFrontOf(HeaderText.gameObject, Body, camera);
            ComboSceneGeometry.TextAfterArtwork(HeaderText, Body);
        }
        internal void OrderFooterButton(UIBButton button, Camera camera)
        {
            // The footer's metal posts are part of Frame, outside Body. Place
            // complete button artwork above those posts, including by queue.
            int queue = 0;
            foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(Frame))
                if (renderer.GetComponentInParent<UberText>() == null)
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null) queue = Math.Max(queue, material.renderQueue);
            foreach (Renderer renderer in button.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.GetComponentInParent<UberText>() != null) continue;
                if (!m_controlMaterials.TryGetValue(renderer, out Material[] copies))
                {
                    copies = (Material[])renderer.sharedMaterials.Clone();
                    for (int i = 0; i < copies.Length; i++) if (copies[i] != null) copies[i] = new Material(copies[i]);
                    m_controlMaterials.Add(renderer, copies);
                }
                foreach (Material material in copies) if (material != null) material.renderQueue = Math.Max(material.renderQueue, queue);
                renderer.sharedMaterials = copies;
            }
            ComboSceneGeometry.InFrontOf(button.gameObject, Frame, camera);
            ComboSceneGeometry.TextAfterArtwork(button.m_ButtonText, Frame);
        }
        public void Dispose()
        {
            foreach (ComboNativeInput input in m_inputs.Values) input.Dispose(); m_inputs.Clear(); m_inputTemplate?.Dispose();
            foreach (Material[] materials in m_controlMaterials.Values)
                foreach (Material material in materials) if (material != null) UnityEngine.Object.Destroy(material);
            m_controlMaterials.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root);
            m_asset?.Dispose();
        }
        internal GameObject InputSurface(bool multiline) => InputSurface(multiline ? "description" : "name");
        internal GameObject InputSurface(string id) => m_inputs.TryGetValue(id, out ComboNativeInput input) ? input.Root : null;
        internal GameObject LayoutInput(bool multiline, Camera camera, Rect target, float depth) => LayoutInput(multiline ? "description" : "name", camera, target, depth);
        internal GameObject LayoutInput(string id, Camera camera, Rect target, float depth)
        {
            if (!m_inputLoaded) { m_inputTemplate = ComboInputTemplate.Load(); m_inputLoaded = true; }
            if (!m_inputs.TryGetValue(id, out ComboNativeInput field))
            {
                string title = char.ToUpperInvariant(id[0]) + id.Substring(1);
                field = new ComboNativeInput(Root.transform, m_inputTemplate.Background, "BCM_Combo" + title + "Input");
                m_inputs.Add(id, field);
            }
            int queue = 0;
            foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(Body))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) queue = Math.Max(queue, material.renderQueue);
            field.Layout(camera, target, depth, queue);
            ComboSceneGeometry.InFrontOf(field.Root, Body, camera);
            return field.Root;
        }
    }
}
