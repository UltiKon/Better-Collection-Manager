using System;
using System.Collections.Generic;
using System.Reflection;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    // Keep the shop's quantity artwork together, including its number well.
    // Input belongs to the editor; no shop/data-context callbacks are copied.
    internal sealed class ComboQuantityControl : IDisposable
    {
        internal sealed class Template : IDisposable
        {
            private readonly List<AssetHandle<GameObject>> m_assets = new List<AssetHandle<GameObject>>();
            internal AdjustBuyingAmount Source;
            internal Template()
            {
                foreach (AdjustBuyingAmount source in Resources.FindObjectsOfTypeAll<AdjustBuyingAmount>())
                    if (Get(source, "m_incrementClickable") != null && Get(source, "m_decrementClickable") != null)
                    { Source = source; return; }
                var visited = new HashSet<string>(StringComparer.Ordinal);
                void Load(string path, int depth)
                {
                    if (Source != null || string.IsNullOrEmpty(path) || depth > 6 || visited.Count >= 64 || !visited.Add(path)) return;
                    AssetHandle<GameObject> asset;
                    try { asset = AssetLoader.Get().LoadAsset<GameObject>(path); }
                    catch { return; }
                    if (asset == null) return;
                    m_assets.Add(asset);
                    GameObject root = asset.Asset;
                    if (root == null) return;
                    Source = root.GetComponentInChildren<AdjustBuyingAmount>(true);
                    if (Source != null) return;
                    var references = new List<string>();
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null) continue;
                        // Widget templates retain their serialized weak asset
                        // references even when the shop has never been opened.
                        if (component.GetType().Name == "WidgetInstance")
                        {
                            object reference = component.GetType().GetField("m_widgetTemplate", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(component);
                            string nested = reference?.GetType().GetField("AssetString")?.GetValue(reference) as string
                                ?? reference?.GetType().GetProperty("AssetString")?.GetValue(reference, null) as string;
                            if (!string.IsNullOrEmpty(nested)) references.Add(nested);
                        }
                        else if (component is NestedPrefab prefab) references.Add(prefab.m_Prefab);
                        else if (component is NestedPrefabPlatformOverride platform) references.Add((string)platform.m_Prefab);
                    }
                    int Priority(string reference)
                    {
                        string name = (reference ?? "").ToLowerInvariant();
                        return name.Contains("adjustbuying") ? 0 : name.Contains("quantity") ? 1
                            : name.Contains("product") ? 2 : name.Contains("shop") ? 3 : 4;
                    }
                    references.Sort((a, b) => Priority(a).CompareTo(Priority(b)));
                    foreach (string reference in references) Load(reference, depth + 1);
                }
                try { Load("MainShopWidget.prefab:debaa5f123c09694eaca007ce8375d8d", 0); }
                catch { Dispose(); throw; }
                if (Source == null) { Dispose(); throw new InvalidOperationException("Native shop quantity template is unavailable."); }
            }
            public void Dispose() { foreach (var asset in m_assets) asset.Dispose(); m_assets.Clear(); }
        }
        private static Component Get(Component source, string field) => source.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source) as Component;
        internal readonly GameObject Root;
        private readonly GameObject m_minus, m_plus;
        private readonly UberText m_amount;
        private readonly List<Material> m_materials = new List<Material>();
        internal ComboQuantityControl(Transform parent, Template template)
        {
            Root = new GameObject("BCM_ShopQuantity"); Root.SetActive(false); Root.transform.SetParent(parent, false);
            try
            {
                GameObject shell = UnityEngine.Object.Instantiate(template.Source.gameObject, Root.transform, false);
                AdjustBuyingAmount source = shell.GetComponent<AdjustBuyingAmount>();
                m_minus = Get(source, "m_decrementClickable")?.gameObject;
                m_plus = Get(source, "m_incrementClickable")?.gameObject;
                UberText amount = Get(source, "m_buyingAmountText") as UberText;
                if (m_minus == null || m_plus == null || amount == null) throw new InvalidOperationException("Native shop quantity controls are incomplete.");
                m_amount = CounterTextLayout.CreateDirectCopy(amount, amount.transform.parent, "BCM_QuantityAmount");
                amount.Hide(); amount.gameObject.SetActive(false);
                foreach (Behaviour behavior in shell.GetComponentsInChildren<Behaviour>(true))
                    if (!(behavior is UberText)) UnityEngine.Object.DestroyImmediate(behavior);
                foreach (Collider hit in shell.GetComponentsInChildren<Collider>(true)) hit.enabled = false;
                shell.transform.localPosition = Vector3.zero; shell.SetActive(true);
                foreach (Renderer renderer in shell.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.GetComponentInParent<UberText>() != null) continue;
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null) { materials[i] = new Material(materials[i]); m_materials.Add(materials[i]); }
                    renderer.sharedMaterials = materials;
                }
                LayerUtils.SetLayer(Root.transform, GameLayer.HighPriorityUI);
            }
            catch { Dispose(); throw; }
        }
        internal void Layout(Camera camera, GameObject paper, Rect target, int count, bool less, bool more, UIBButton minus, UIBButton plus)
        {
            Root.SetActive(true); m_minus.SetActive(true); m_plus.SetActive(true);
            string caption = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (m_amount.Text != caption) { m_amount.Text = caption; m_amount.UpdateNow(true); }
            m_amount.Show();
            ComboSceneGeometry.FaceCaption(Root.transform, m_amount, camera);
            ComboSceneGeometry.Fit(Root.transform, camera, target);
            // Native shop artwork includes uneven padding around its number.
            // Align the actual digit ink to the card name's row center.
            if (CounterTextLayout.TryScreenInkRect(m_amount, camera, out Rect amountInk))
            {
                float depth = camera.WorldToScreenPoint(Root.transform.position).z;
                Root.transform.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth))
                    - camera.ScreenToWorldPoint(new Vector3(target.center.x, amountInk.center.y, depth));
            }
            int queue = 0;
            foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(paper))
                if (!ComboSceneGeometry.IsGlyphRenderer(renderer))
                    foreach (Material material in renderer.sharedMaterials) if (material != null) queue = Math.Max(queue, material.renderQueue);
            foreach (Material material in m_materials) material.renderQueue = Math.Max(material.renderQueue, queue + 10);
            ComboSceneGeometry.InFrontOf(Root, paper, camera);
            ComboSceneGeometry.TextAfterArtwork(m_amount, Root);
            // Missing actions disappear at the exact limits. Their invisible
            // input proxies are disabled at the same time.
            void Action(GameObject art, UIBButton button, bool enabled)
            {
                button.SetEnabled(enabled, true);
                foreach (Renderer renderer in button.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                button.m_ButtonText.Hide();
                if (ComboSceneGeometry.ScreenBounds(art, camera, out Rect bounds))
                {
                    ComboSceneGeometry.FaceCaption(button.transform, button.m_ButtonText, camera);
                    button.transform.position = camera.ScreenToWorldPoint(new Vector3(bounds.center.x, bounds.center.y,
                        camera.WorldToScreenPoint(Root.transform.position).z));
                    ComboPopup.FitFieldHitArea(button, camera, bounds);
                }
                foreach (Collider hit in button.GetComponentsInChildren<Collider>(true))
                    hit.enabled = enabled && hit.gameObject == button.gameObject;
                art.SetActive(enabled);
            }
            Action(m_minus, minus, less); Action(m_plus, plus, more);
        }
        public void Dispose()
        {
            foreach (Material material in m_materials) UnityEngine.Object.Destroy(material);
            if (Root != null) { Root.SetActive(false); UnityEngine.Object.Destroy(Root); }
        }
    }
}
