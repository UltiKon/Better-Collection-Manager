using System;
using System.Collections.Generic;
using System.Reflection;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    // Clone the installed game's deck-recipe artwork. No texture GUIDs or
    // approximated frame meshes are baked into the mod.
    internal sealed class PackageNativeTile : IDisposable
    {
        internal const float CollectionScale = 1.08f;
        internal static Rect CollectionArea(CollectionCardVisual visual, Camera camera)
        {
            Rect slot = ComboSceneGeometry.CardSlot(visual, camera);
            float width = slot.width * CollectionScale, height = slot.height * CollectionScale;
            return new Rect(slot.center.x - width * .5f, slot.center.y - height * .5f, width, height);
        }
        private static GameObject s_template, s_storage;
        private static AssetHandle<GameObject> s_asset;
        private static float s_nextSearch;
        private static CollectionPageManager s_searchManager;
        private static string s_searchAsset;
        private static string s_failure;
        private static readonly Dictionary<string, float> s_createRetry = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly Dictionary<string, AssetHandle<Texture>> s_preloadedArtwork = new Dictionary<string, AssetHandle<Texture>>();
        private readonly List<Mesh> m_meshes = new List<Mesh>();
        private static readonly List<Mesh> s_templateMeshes = new List<Mesh>();

        internal static void Preload(CategoryCatalog.Combo package)
        {
            if (!package.IsPackage) return;
            FindTemplate();
            EntityDef artwork = PackageArtwork.ResolveFor(package);
            if (artwork == null || s_preloadedArtwork.ContainsKey(artwork.GetCardId())) return;
            try
            {
                using (DefLoader.DisposableCardDef definition = DefLoader.Get().GetCardDef(artwork.GetCardId()))
                {
                    AssetHandle<Texture> texture = definition?.CardDef?.GetPortraitTexture(TAG_PREMIUM.NORMAL, true);
                    if (texture != null) s_preloadedArtwork.Add(artwork.GetCardId(), texture);
                }
            }
            catch (Exception ex)
            {
                if (s_failure != ex.Message) { s_failure = ex.Message; DebugLog.Warning("Package portrait preload: " + ex.Message); }
            }
        }
        internal static void ReleaseAssets()
        {
            foreach (var texture in s_preloadedArtwork.Values) texture.Dispose();
            s_preloadedArtwork.Clear();
            if (s_storage != null) UnityEngine.Object.Destroy(s_storage);
            foreach (Mesh mesh in s_templateMeshes) UnityEngine.Object.Destroy(mesh);
            s_templateMeshes.Clear(); s_template = s_storage = null;
            s_asset?.Dispose(); s_asset = null;
            s_searchManager = null; s_searchAsset = null;
            s_nextSearch = 0; s_createRetry.Clear(); s_failure = null;
        }
        private static void FreezeGraphics(GameObject shell, List<Mesh> meshes)
        {
            // An inactive recipe prefab has not run a skinning/render update.
            // Snapshot its bones now, including tight bounds, so the very first
            // page fit uses real geometry rather than deferred renderer bounds.
            foreach (SkinnedMeshRenderer skin in shell.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var mesh = new Mesh(); meshes.Add(mesh);
                skin.BakeMesh(mesh); mesh.RecalculateBounds();
                var properties = new MaterialPropertyBlock(); skin.GetPropertyBlock(properties);
                var go = new GameObject(skin.gameObject.name);
                go.transform.SetParent(skin.transform, false); go.layer = skin.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = skin.sharedMaterials;
                renderer.sortingLayerID = skin.sortingLayerID; renderer.sortingOrder = skin.sortingOrder;
                renderer.SetPropertyBlock(properties); renderer.enabled = skin.enabled;
                UnityEngine.Object.DestroyImmediate(skin);
            }
        }
        private readonly GameObject m_root;
        private readonly Actor m_actor;
        private AssetHandle<Texture> m_artwork;
        private string m_artworkId;
        private readonly CategoryCatalog.Combo m_artworkPackage;
        private float m_nextPortrait;
        private void LoadPortrait()
        {
            if (m_artwork != null || Time.unscaledTime < m_nextPortrait) return;
            m_nextPortrait = Time.unscaledTime + .25f;
            try
            {
                if (string.IsNullOrEmpty(m_artworkId)) m_artworkId = PackageArtwork.ResolveFor(m_artworkPackage)?.GetCardId();
                if (string.IsNullOrEmpty(m_artworkId)) return;
                if (s_preloadedArtwork.TryGetValue(m_artworkId, out m_artwork)) s_preloadedArtwork.Remove(m_artworkId);
                else using (DefLoader.DisposableCardDef definition = DefLoader.Get().GetCardDef(m_artworkId))
                    m_artwork = definition?.CardDef?.GetPortraitTexture(TAG_PREMIUM.NORMAL, true);
            }
            catch (Exception ex)
            { if (s_failure != ex.Message) { s_failure = ex.Message; DebugLog.Warning("Package portrait: " + ex.Message); } }
        }
        private readonly List<Material> m_materials = new List<Material>();
        private readonly Dictionary<Renderer, bool> m_hidden = new Dictionary<Renderer, bool>();
        private readonly List<Renderer> m_portraits = new List<Renderer>();
        private readonly List<Renderer> m_graphics = new List<Renderer>();
        private readonly List<Renderer> m_previewGraphics = new List<Renderer>();
        private readonly List<GameObject> m_glows = new List<GameObject>();
        private readonly List<Renderer> m_glowRenderers = new List<Renderer>();
        private void RefreshGlow(bool hover)
        {
            foreach (GameObject glow in m_glows) if (glow != null) glow.SetActive(hover);
            // Native actor/page transitions can disable cached renderers after
            // construction. Restore only this tile's private effect geometry.
            foreach (Renderer renderer in m_glowRenderers) if (renderer != null) renderer.enabled = hover;
        }
        private readonly List<UberText> m_titles = new List<UberText>(), m_counts = new List<UberText>();
        private string m_name;
        private readonly string m_count;
        internal GameObject Root => m_root;
        private bool m_oriented;
        private bool m_collectionFitted;
        private Vector3 m_collectionSize, m_collectionCenter;
        internal void AttachTo(Transform parent)
        {
            if (m_root.transform.parent == parent) return;
            m_root.SetActive(false);
            m_root.transform.SetParent(parent, false);
            if (m_lockRoot != null) { m_lockRoot.SetActive(false); m_lockRoot.transform.SetParent(parent, false); }
            m_root.transform.localPosition = Vector3.zero;
            m_root.transform.localRotation = Quaternion.identity;
            m_root.transform.localScale = Vector3.one;
            m_oriented = false; m_collectionFitted = false;
        }
        internal void RefreshCollection(CollectionCardVisual visual)
        {
            AttachTo(visual.transform);
            Vector3 size = visual.m_boxColliderSize, center = visual.m_boxColliderCenter;
            if (!m_collectionFitted || !size.Equals(m_collectionSize) || !center.Equals(m_collectionCenter))
            {
                // Fit in the card slot's own coordinates, before the page moves.
                // Repeated fitting to its changing screen projection made the
                // folder resize during the native page-turn animation.
                m_root.transform.localPosition = Vector3.zero;
                m_root.transform.localScale = Vector3.one;
                m_root.transform.localRotation = Quaternion.identity;
                if (!LocalGraphicsBounds(m_root.transform, out Bounds source)) return;
                if (source.size.y > source.size.z)
                {
                    float half = (float)Math.Sqrt(.5);
                    m_root.transform.localRotation = new Quaternion(half, 0, 0, half);
                }
                if (!LocalGraphicsBounds(visual.transform, out Bounds bounds) || bounds.size.x <= .0001f || bounds.size.z <= .0001f) return;
                float scale = Math.Min(size.x * CollectionScale / bounds.size.x, size.z * CollectionScale / bounds.size.z) * .985f;
                if (scale <= 0) return;
                m_root.transform.localScale *= scale;
                if (!LocalGraphicsBounds(visual.transform, out bounds)) return;
                m_root.transform.localPosition += center - bounds.center;
                m_collectionSize = size; m_collectionCenter = center; m_collectionFitted = true;
            }
            PrepareForPage();
            // Native OnOver only animates owned carrier cards, and new cards
            // use a separate actor state. Folder feedback belongs to the actual
            // slot interaction, independently of the hidden carrier's inventory.
            PegUIElement.InteractionState interaction = visual.GetInteractionState();
            RefreshGlow(interaction == PegUIElement.InteractionState.Over
                || interaction == PegUIElement.InteractionState.Down);
        }
        private bool LocalGraphicsBounds(Transform space, out Bounds bounds)
        {
            bounds = default; bool found = false;
            foreach (Renderer renderer in m_graphics)
            {
                if (renderer == null || !renderer.enabled) continue;
                bool hidden = false;
                for (Transform node = renderer.transform; node != null && node != m_root.transform; node = node.parent)
                    hidden |= !node.gameObject.activeSelf;
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (hidden || mesh == null) continue;
                Bounds part = FilterTrayGeometry.TransformBounds(mesh.bounds, point => space.InverseTransformPoint(renderer.transform.TransformPoint(point)));
                if (found) bounds.Encapsulate(part); else { bounds = part; found = true; }
            }
            return found;
        }
        internal bool PrepareForPage()
        {
            m_root.SetActive(true);
            LoadPortrait();
            bool ready = m_artwork == null || m_artwork.Asset != null;
            bool ReadyCaption(UberText label, string text)
            {
                Caption(label, text);
                if (label.isHidden()) label.Show();
                if (label.GetTextWorldSpaceBounds().size.x > 0) return true;
                label.UpdateNow(true);
                return label.GetTextWorldSpaceBounds().size.x > 0;
            }
            foreach (UberText title in m_titles) ready &= ReadyCaption(title, m_name);
            foreach (UberText count in m_counts) ready &= ReadyCaption(count, m_count);
            if (m_artwork?.Asset != null)
                foreach (Renderer portrait in m_portraits)
                    foreach (Material material in portrait.sharedMaterials) if (material != null) material.mainTexture = m_artwork.Asset;
            foreach (var hidden in m_hidden) if (hidden.Key != null) hidden.Key.enabled = false;
            return ready;
        }
        private GameObject m_lockRoot, m_lockSource;
        private UberText m_lockText;
        private readonly Dictionary<UberText, bool> m_hiddenLockTexts = new Dictionary<UberText, bool>();

        internal void LayoutLock(Component nativeLock, string reason, Rect area)
        {
            if (nativeLock == null) { m_lockRoot?.SetActive(false); m_lockText?.Hide(); return; }
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            GameObject Part(string name) => nativeLock.GetType().GetField(name, fields)?.GetValue(nativeLock) as GameObject;
            GameObject source = Part("m_bannedRibbon");
            if (source != null && source.activeSelf)
            {
                // Invalid packages disappear with their deck context. Never
                // clone the red ribbon into a detached sibling of the folder.
                source.SetActive(false); m_lockRoot?.SetActive(false); m_lockText?.Hide(); nativeLock.gameObject.SetActive(false); return;
            }
            if (!nativeLock.gameObject.activeSelf) { m_lockRoot?.SetActive(false); m_lockText?.Hide(); return; }
            if (source == null || !source.activeSelf) source = Part("m_lockPlate");
            if (source == null) return;
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_actor.gameObject);
            if (camera == null) return;
            if (m_lockSource != source)
            {
                if (m_lockRoot != null) UnityEngine.Object.Destroy(m_lockRoot);
                m_lockSource = source;
                m_lockRoot = new GameObject("BCM_PackageLock"); m_lockRoot.SetActive(false);
                m_lockRoot.transform.SetParent(m_root.transform.parent, false);
                var plate = UnityEngine.Object.Instantiate(source, m_lockRoot.transform, false); plate.SetActive(true);
                UberText originalText = nativeLock.GetType().GetField("m_lockText", fields)?.GetValue(nativeLock) as UberText;
                m_lockText = plate.GetComponentInChildren<UberText>(true);
                if (m_lockText == null && originalText != null)
                {
                    var label = UnityEngine.Object.Instantiate(originalText.gameObject, plate.transform, false);
                    label.transform.localPosition = source.transform.InverseTransformPoint(originalText.transform.position);
                    label.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * originalText.transform.rotation;
                    Vector3 textScale = originalText.transform.lossyScale, plateScale = source.transform.lossyScale;
                    label.transform.localScale = new Vector3(textScale.x / plateScale.x, textScale.y / plateScale.y, textScale.z / plateScale.z);
                    m_lockText = label.GetComponent<UberText>();
                }
                StripNativeControls(m_lockRoot);
                int queue = 3000;
                foreach (Material material in m_materials) queue = Math.Max(queue, material.renderQueue);
                foreach (Renderer renderer in m_lockRoot.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                    if (ComboSceneGeometry.IsGlyphRenderer(renderer)) continue;
                    Material[] materials = (Material[])renderer.sharedMaterials.Clone();
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null)
                        { var copy = new Material(materials[i]); copy.renderQueue = queue + 1; materials[i] = copy; m_materials.Add(copy); }
                    renderer.sharedMaterials = materials;
                }
                if (m_lockText != null) ComboSceneGeometry.FaceCaption(m_lockRoot.transform, m_lockText, camera);
                foreach (Transform node in m_lockRoot.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = m_actor.gameObject.layer;
            }
            m_lockRoot.SetActive(true);
            if (m_lockText != null)
            {
                m_lockText.gameObject.SetActive(true); m_lockText.RichText = true;
                m_lockText.ResizeToFit = true; m_lockText.ResizeToFitAndGrow = false;
                Caption(m_lockText, reason ?? "Cannot add to this deck."); m_lockText.Show();
            }
            // Preserve the native plate and font, with no card-shaped backdrop.
            ComboSceneGeometry.Fit(m_lockRoot.transform, camera,
                new Rect(area.xMin + area.width * .06f, area.yMin + area.height * .55f, area.width * .88f, area.height * .13f));
            ComboSceneGeometry.InFrontOf(m_lockRoot, m_root, camera);
            if (m_lockText != null) ComboSceneGeometry.TextAfterArtwork(m_lockText, m_lockRoot);
            ComboSceneGeometry.TextInFront(m_lockRoot, camera);
            foreach (Renderer renderer in nativeLock.GetComponentsInChildren<Renderer>(true))
            {
                if (!m_hidden.ContainsKey(renderer)) m_hidden[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
            foreach (UberText text in nativeLock.GetComponentsInChildren<UberText>(true))
            {
                if (!m_hiddenLockTexts.ContainsKey(text)) m_hiddenLockTexts[text] = text.isHidden();
                if (!text.isHidden()) text.Hide();
            }
        }

        internal static void Observe(CollectionCardVisual visual) => CaptureTemplate();
        internal static void CaptureTemplate()
        {
            if (s_template != null || !PreferredQualityMode.IsEnabled
                || CollectionManager.Get()?.GetCollectibleDisplay() == null) return;
            FindTemplate();
        }
        private static void MarkNativeFields(DeckTemplatePickerButton button)
        {
            if (button.m_title != null) button.m_title.gameObject.name = "BCM_PackageTitle";
            if (button.m_deckTexture != null) button.m_deckTexture.gameObject.name = "BCM_PackagePortrait";
            if (button.m_packRibbon != null) button.m_packRibbon.gameObject.name = "BCM_PackageClassRibbon";
            if (button.m_selectGlow != null) button.m_selectGlow.name = "BCM_PackageGlow";
            if (button.m_incompleteTextRibbon != null) button.m_incompleteTextRibbon.name = "BCM_PackageBadgeIncomplete_Count";
            if (button.m_completeTextRibbon != null) button.m_completeTextRibbon.name = "BCM_PackageBadgeComplete_Count";
            if (button.m_runes != null) button.m_runes.SetActive(false);
            if (button.m_incompleteTextRibbon != null) button.m_incompleteTextRibbon.SetActive(false);
            if (button.m_completeTextRibbon != null) button.m_completeTextRibbon.SetActive(true);
            // The native recipe keeps separate complete/incomplete captions.
            // Copy one caption from the visible ribbon, never both variants.
            UberText chosenCount = null;
            int chosenPriority = -1;
            foreach (UberText count in button.m_cardCountTexts)
            {
                if (count == null) continue;
                bool visible = true;
                for (Transform part = count.transform; part != null && part != button.transform; part = part.parent)
                    visible &= part.gameObject.activeSelf;
                int priority = button.m_completeTextRibbon != null && count.transform.IsChildOf(button.m_completeTextRibbon.transform)
                    ? 2 : visible ? 1 : 0;
                if (priority > chosenPriority) { chosenCount = count; chosenPriority = priority; }
            }
            foreach (UberText count in button.m_cardCountTexts)
                if (count != null) count.gameObject.name = count == chosenCount ? "BCM_PackageCount" : "BCM_UnusedPackageCount";
            if (button.m_packCustomMaterial != null)
            {
                if (button.m_packFlap != null) button.m_packFlap.sharedMaterial = button.m_packCustomMaterial;
                if (button.m_packInner != null) button.m_packInner.sharedMaterial = button.m_packCustomMaterial;
            }
        }
        private static void StripNativeControls(GameObject root)
        {
            // The folder owns input/hover. Native pack-flight controllers would
            // move or hide the cloned graphic when its GameObject activates.
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                if (component is PegUIElement || component is AnimatedLowPolyPack || component is RandomTransform
                    || component?.GetType().Name == "PlayMakerFSM")
                    UnityEngine.Object.DestroyImmediate(component);
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }
        private static bool RememberTemplate(DeckTemplatePicker picker)
        {
            DeckTemplatePickerButton button = picker?.m_pickerButtonTpl;
            if (button?.m_title == null || button.m_deckTexture == null || picker.m_deckArtMaterial == null) return false;
            var storage = new GameObject("BCM_DeckRecipeTemplate"); storage.SetActive(false);
            int firstMesh = s_templateMeshes.Count;
            try
            {
                GameObject copy = UnityEngine.Object.Instantiate(button.gameObject, storage.transform, false);
                var copiedButton = copy.GetComponent<DeckTemplatePickerButton>();
                copiedButton.m_deckTexture.sharedMaterial = picker.m_deckArtMaterial;
                MarkNativeFields(copiedButton);
                StripNativeControls(copy);
                FreezeGraphics(copy, s_templateMeshes);
                s_template = copy; s_storage = storage; s_failure = null; s_createRetry.Clear();
                DebugLog.Msg("Package template captured from DeckTemplatePicker.m_pickerButtonTpl.");
                return true;
            }
            catch
            {
                UnityEngine.Object.Destroy(storage);
                for (int i = s_templateMeshes.Count - 1; i >= firstMesh; i--)
                { UnityEngine.Object.Destroy(s_templateMeshes[i]); s_templateMeshes.RemoveAt(i); }
                throw;
            }
        }
        private static bool FindTemplate()
        {
            if (s_template != null) return true;
            var manager = CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager() as CollectionPageManager;
            string asset = manager?.m_deckTemplatePickerPrefab;
            // An early collection-results pass may precede the native page
            // manager/prefab. Its retry delay must not block the first ready page.
            if (manager == s_searchManager && asset == s_searchAsset && Time.unscaledTime < s_nextSearch) return false;
            s_searchManager = manager; s_searchAsset = asset;
            s_nextSearch = Time.unscaledTime + .25f;
            try
            {
                if (RememberTemplate(manager?.GetDeckTemplatePicker())) return true;
                // The native page manager stores the installed client's prefab
                // reference. Load its asset without opening/instantiating the page.
                if (!string.IsNullOrEmpty(asset))
                {
                    AssetHandle<GameObject> handle = AssetLoader.Get().LoadAsset<GameObject>(asset);
                    bool retained = false;
                    try
                    {
                        var picker = handle?.Asset?.GetComponent<DeckTemplatePicker>();
                        if (RememberTemplate(picker))
                        { s_asset = handle; retained = true; return true; }
                    }
                    finally { if (!retained) handle?.Dispose(); }
                }
                foreach (DeckTemplatePicker picker in Resources.FindObjectsOfTypeAll<DeckTemplatePicker>())
                    if (RememberTemplate(picker)) return true;
                if (manager == null || string.IsNullOrEmpty(asset)) return false;
                throw new InvalidOperationException("Recipe button unavailable; manager=" + (manager?.GetType().Name ?? "none")
                    + "; prefab=" + (asset ?? "none"));
            }
            catch (Exception ex)
            {
                if (s_failure != ex.Message) { s_failure = ex.Message; DebugLog.Warning("Package native template: " + ex.Message); }
            }
            return false;
        }
        internal static PackageNativeTile TryCreate(Actor actor, CategoryCatalog.Combo package, Transform parent = null)
        {
            string retryKey = package.Id ?? "preview:" + (package.Artwork ?? "");
            if (s_createRetry.TryGetValue(retryKey, out float retry) && Time.unscaledTime < retry) return null;
            try { return FindTemplate() ? new PackageNativeTile(actor, package, parent) : null; }
            catch (Exception ex)
            {
                s_createRetry[retryKey] = Time.unscaledTime + .25f;
                if (s_failure != ex.Message) { s_failure = ex.Message; DebugLog.Warning("Package recipe template: " + ex.Message); }
                return null;
            }
        }

        private PackageNativeTile(Actor actor, CategoryCatalog.Combo package, Transform visualParent)
        {
            m_actor = actor; m_name = package.Name; m_artworkPackage = package;
            int total = 0; foreach (var requirement in package.Cards) total += requirement.Count;
            m_count = total + (total == 1 ? " Card" : " Cards");
            m_root = new GameObject("BCM_PackageRecipe"); m_root.SetActive(false);
            // Collection folders belong to the page slot. Native hand-card
            // Start/state updates must not animate or fade their replacement.
            m_root.transform.SetParent(visualParent != null ? visualParent : actor.transform, false);
            try
            {
                var shell = UnityEngine.Object.Instantiate(s_template, m_root.transform, false);
                StripNativeControls(shell);
                FreezeGraphics(shell, m_meshes);
                foreach (Behaviour behavior in shell.GetComponentsInChildren<Behaviour>(true))
                    if (!(behavior is UberText)) UnityEngine.Object.DestroyImmediate(behavior);
                shell.SetActive(true);
                foreach (UberText label in shell.GetComponentsInChildren<UberText>(true))
                {
                    string name = label.gameObject.name.ToLowerInvariant();
                    bool title = name == "bcm_packagetitle";
                    bool count = name == "bcm_packagecount";
                    if (title || count)
                    {
                        // Native cached text planes can become ready a frame
                        // after the folder. Build direct captions with the art.
                        UberText caption = CounterTextLayout.CreateDirectCopy(label, label.transform.parent, label.gameObject.name);
                        if (title) caption.TextColor = caption.GradientUpperColor = caption.GradientLowerColor = Color.white;
                        for (Transform parent = caption.transform; parent != null && parent != m_root.transform; parent = parent.parent) parent.gameObject.SetActive(true);
                        label.Hide(); label.enabled = false;
                        foreach (Renderer old in label.GetComponentsInChildren<Renderer>(true)) old.enabled = false;
                        label.gameObject.SetActive(false);
                        if (title) m_titles.Add(caption); else m_counts.Add(caption);
                    }
                    else { label.Hide(); label.gameObject.SetActive(false); }
                }
                if (m_titles.Count == 0) throw new InvalidOperationException("Native recipe title label was not found.");
                foreach (Renderer renderer in shell.GetComponentsInChildren<Renderer>(true))
                {
                    string path = renderer.gameObject.name.ToLowerInvariant();
                    for (Transform parent = renderer.transform.parent; parent != null && parent != shell.transform; parent = parent.parent) path += "/" + parent.gameObject.name.ToLowerInvariant();
                    if ((path.Contains("portrait") || path.Contains("artwork") || path.Contains("illustration")) && !path.Contains("frame")) m_portraits.Add(renderer);
                    Transform glowRoot = null;
                    for (Transform node = renderer.transform; node != null && node != shell.transform; node = node.parent)
                    {
                        string name = node.gameObject.name;
                        if (name.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("highlight", StringComparison.OrdinalIgnoreCase) >= 0) glowRoot = node;
                    }
                    bool glow = glowRoot != null;
                    if (glow)
                    {
                        // m_selectGlow may be an inactive container with nested
                        // geometry. Manage that container, not only its leaves,
                        // and prepare the private subtree before the first hover.
                        if (!m_glows.Contains(glowRoot.gameObject)) m_glows.Add(glowRoot.gameObject);
                        for (Transform node = renderer.transform; node != glowRoot; node = node.parent) node.gameObject.SetActive(true);
                        m_glowRenderers.Add(renderer);
                    }
                    if (!glow && !path.Contains("shadow")
                        && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                        && !ComboSceneGeometry.IsGlyphRenderer(renderer) && renderer.GetComponentInParent<UberText>() == null)
                    {
                        m_previewGraphics.Add(renderer);
                        if (!path.Contains("bcm_packagebadge")) m_graphics.Add(renderer);
                    }
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null) { var copy = new Material(materials[i]); materials[i] = copy; m_materials.Add(copy); }
                    renderer.sharedMaterials = materials;
                    if (renderer.gameObject.name == "BCM_PackageClassRibbon")
                        foreach (Material material in materials)
                            if (material != null) material.color = CollectionPageManager.ColorForClass(ComboCollectionCards.ClassForPackage(package));
                }
                if (m_portraits.Count == 0) throw new InvalidOperationException("Native recipe portrait renderer was not found.");
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(m_root.transform)) m_hidden[renderer] = renderer.enabled;
                LoadPortrait(); // Continue unresolved token artwork on later updates.
                foreach (Renderer portrait in m_portraits)
                    foreach (Material material in portrait.sharedMaterials) if (material != null) material.mainTexture = m_artwork?.Asset;
                foreach (Transform part in m_root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = actor.gameObject.layer;
            }
            catch { Dispose(); throw; }
        }
        internal void Refresh(string caption = null, Rect? area = null)
        {
            LoadPortrait();
            if (caption != null) m_name = caption;
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_actor.gameObject);
            if (camera == null) return;
            foreach (UberText title in m_titles)
            {
                title.TextColor = title.GradientUpperColor = title.GradientLowerColor = Color.white;
                Caption(title, m_name);
            }
            foreach (UberText count in m_counts) Caption(count, m_count);
            Rect target;
            if (area.HasValue) target = area.Value;
            else
            {
                Renderer card = m_actor.GetMeshRenderer();
                if (card == null) return;
                target = ComboSceneGeometry.ProjectBounds(card.bounds, camera);
            }
            if (target.width <= .001f || target.height <= .001f) return;
            m_root.SetActive(true);
            foreach (UberText title in m_titles) if (title.isHidden()) title.Show();
            foreach (UberText count in m_counts) if (count.isHidden()) count.Show();
            if (!m_oriented)
            {
                ComboSceneGeometry.FaceCaption(m_root.transform, m_titles[0], camera);
                m_oriented = true;
            }
            // Re-evaluate after native Start/state animations. The old one-time
            // fit retained the actor's smaller initial mesh scale indefinitely.
            if (!FitGraphics(camera, target, area.HasValue)) return;
            foreach (var hidden in m_hidden) if (hidden.Key != null) hidden.Key.enabled = false;
            if (m_artwork?.Asset != null)
                foreach (Renderer portrait in m_portraits)
                    foreach (Material material in portrait.sharedMaterials) if (material != null) material.mainTexture = m_artwork.Asset;
            ActorStateType state = m_actor.GetActorStateType();
            RefreshGlow(state == ActorStateType.CARD_MOUSE_OVER || state == ActorStateType.CARD_RECENTLY_ACQUIRED_MOUSE_OVER);
        }
        internal void Hide()
        {
            // Actor.Hide caches native renderers before this extra artwork
            // exists. Explicitly hide it at the same collection page boundary.
            foreach (UberText title in m_titles) title.Hide();
            foreach (UberText count in m_counts) count.Hide();
            m_root.SetActive(false);
            m_lockRoot?.SetActive(false);
            m_lockText?.Hide();
        }
        private bool GraphicsBounds(Camera camera, out Rect bounds, bool preview = false)
        {
            bounds = default; bool found = false;
            foreach (Renderer renderer in preview ? m_previewGraphics : m_graphics)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Rect part = ComboSceneGeometry.ProjectBounds(renderer.bounds, camera);
                if (part.width <= .001f || part.height <= .001f) continue;
                bounds = found ? Rect.MinMaxRect(Math.Min(bounds.xMin, part.xMin), Math.Min(bounds.yMin, part.yMin),
                    Math.Max(bounds.xMax, part.xMax), Math.Max(bounds.yMax, part.yMax)) : part;
                found = true;
            }
            return found;
        }
        private bool FitGraphics(Camera camera, Rect target, bool preview = false)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                if (!GraphicsBounds(camera, out Rect bounds, preview)) return false;
                float scale = Math.Min(target.width / bounds.width, target.height / bounds.height) * .985f;
                if (Math.Abs(scale - 1f) < .002f && Math.Abs(bounds.center.x - target.center.x) < .25f
                    && Math.Abs(bounds.center.y - target.center.y) < .25f) return true;
                m_root.transform.localScale *= scale;
                if (!GraphicsBounds(camera, out bounds, preview)) return false;
                float depth = camera.WorldToScreenPoint(m_root.transform.position).z;
                m_root.transform.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth))
                    - camera.ScreenToWorldPoint(new Vector3(bounds.center.x, bounds.center.y, depth));
            }
            return true;
        }
        internal void OrderInPopup(GameObject background, Camera camera)
        {
            int queue = 3600;
            foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(background))
                if (!ComboSceneGeometry.IsGlyphRenderer(renderer))
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null) queue = Math.Max(queue, material.renderQueue + 10);
            foreach (Material material in m_materials) material.renderQueue = Math.Max(queue, material.renderQueue);
            ComboSceneGeometry.InFrontOf(m_root, background, camera);
            foreach (UberText label in m_titles) { ComboSceneGeometry.TextAfterArtwork(label, background); ComboSceneGeometry.TextAfterArtwork(label, m_root); }
            foreach (UberText label in m_counts) { ComboSceneGeometry.TextAfterArtwork(label, background); ComboSceneGeometry.TextAfterArtwork(label, m_root); }
            ComboSceneGeometry.TextInFront(m_root, camera);
        }
        private static void Caption(UberText label, string text)
        {
            label.GameStringLookup = false;
            if (label.Text != text) { label.Text = text; label.UpdateNow(true); }
        }
        public void Dispose()
        {
            foreach (var saved in m_hidden) if (saved.Key != null) saved.Key.enabled = saved.Value;
            foreach (var saved in m_hiddenLockTexts) if (saved.Key != null && !saved.Value) saved.Key.Show();
            m_artwork?.Dispose(); m_artwork = null;
            foreach (Material material in m_materials) UnityEngine.Object.Destroy(material);
            foreach (Mesh mesh in m_meshes) UnityEngine.Object.Destroy(mesh);
            m_meshes.Clear();
            if (m_root != null) { m_root.SetActive(false); UnityEngine.Object.Destroy(m_root); }
            if (m_lockRoot != null) { m_lockRoot.SetActive(false); UnityEngine.Object.Destroy(m_lockRoot); }
        }
    }
}
