using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    // Copy graphics only. Native dialog and deck-info controllers must never
    // register input, spawn actors or update the live deck during an export.
    internal sealed class DeckImageNativeArt : IDisposable
    {
        private readonly Transform m_parent;
        private AssetHandle<GameObject> m_paperAsset;
        private readonly List<Material> m_materials = new List<Material>();
        private readonly List<Mesh> m_meshes = new List<Mesh>();
        private readonly List<GameObject> m_objects = new List<GameObject>();
        private readonly Dictionary<TAG_RARITY, GameObject> m_rarityGems = new Dictionary<TAG_RARITY, GameObject>();
        private readonly List<AssetHandle<GameObject>> m_assets = new List<AssetHandle<GameObject>>();
        private ManaFilterTab m_manaTab;
        private CollectionDeckInfo m_deckInfo;
        internal DeckImageNativeArt(Transform parent) { m_parent = parent; }

        private StoreSendToBAM ParchmentTemplate()
        {
            m_paperAsset ??= AssetLoader.Get().LoadAsset<GameObject>((string)ShopPrefabs.ShopSendToBamPrefab);
            StoreSendToBAM template = m_paperAsset?.Asset?.GetComponent<StoreSendToBAM>();
            if (template?.m_midSection == null || template.m_headlineText == null)
                throw new InvalidOperationException("Native parchment is unavailable.");
            return template;
        }

        internal GameObject Parchment(Camera camera)
        {
            StoreSendToBAM template = ParchmentTemplate();
            GameObject paper = CopyGeometry(template.m_midSection);
            Face(paper, template.m_midSection.transform, template.m_headlineText.transform, camera);
            return paper;
        }

        internal GameObject InfoSurface(Camera camera)
        {
            // The Game Menu uses this prefab for its background. Take only the
            // middle tile, without the bezel, header, or button graphics.
            const string menuAsset = "ButtonListMenuDef.prefab:1ab57b5c429373a4b8b4e0c0c706ca3e";
            var asset = AssetLoader.Get().LoadAsset<GameObject>(menuAsset);
            m_assets.Add(asset);
            ButtonListMenuDef template = asset?.Asset?.GetComponent<ButtonListMenuDef>();
            GameObject source = template?.m_background?.m_middle?.m_slice;
            if (source == null || template.m_headerText == null
                || !source.GetComponentsInChildren<MeshFilter>(true).Any(mesh => mesh.sharedMesh != null))
                return Parchment(camera);
            GameObject surface = CopyGeometry(source);
            Face(surface, source.transform, template.m_headerText.transform, camera);
            return surface;
        }

        internal GameObject QuantityFrame(Camera camera)
        {
            StoreQuantityPrompt prompt = ComboQuantityControl.Template.Find<StoreQuantityPrompt>(m_assets,
                value => value.m_root != null && value.m_instructionText != null);
            if (prompt == null) return null;
            GameObject source = prompt.m_root;
            Transform catcher = prompt.m_clickCatcherClickable?.transform;
            bool IsShadow(string name) => name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0;
            bool IsFrameGraphic(MeshRenderer renderer)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (!renderer.enabled || mesh == null || IsShadow(mesh.name ?? "") || renderer.GetComponentInParent<UberText>() != null
                    || renderer.sharedMaterials.All(material => material == null || IsShadow(material.name ?? ""))) return false;
                for (Transform node = renderer.transform; node != source.transform; node = node.parent)
                    if (!node.gameObject.activeSelf || node == catcher || IsShadow(node.name ?? "")) return false;
                return true;
            }
            // Preserve the complete frame. The largest individual mesh can
            // be the drop shadow, and the metal rim may be in separate meshes.
            var graphics = new HashSet<Renderer>(source.GetComponentsInChildren<MeshRenderer>(true).Where(IsFrameGraphic));
            if (graphics.Count == 0) return null;
            GameObject frame = CopyGeometry(source, include: graphics.Contains);
            Face(frame, source.transform, prompt.m_instructionText.transform, camera);
            return frame;
        }

        internal GameObject WildFrame(Camera camera, out Vector3[] panelCorners)
        {
            bool Usable(MatchingPopupDisplay value) => value != null && value.m_title != null
                && value.m_wildVines != null && value.m_wildVines.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh != null
                        || renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null);
            MatchingPopupDisplay template = Resources.FindObjectsOfTypeAll<MatchingPopupDisplay>().FirstOrDefault(Usable);
            if (template == null)
            {
                // Deck editing does not keep deck boxes or the matchmaking
                // window resident. Load the actual vine asset directly, without
                // instantiating its matchmaking controller or running Awake.
                foreach (string path in new[] {
                    "MatchingPopup3D.prefab:4f4a40d14d907e94da1b81d97c18a44f",
                    "MatchingPopup3D_phone.prefab:a7a5cea6306a1fa4680a9782fd25be14" })
                {
                    var asset = AssetLoader.Get().LoadAsset<GameObject>(path);
                    if (asset == null) continue;
                    m_assets.Add(asset);
                    template = asset.Asset?.GetComponentsInChildren<MatchingPopupDisplay>(true).FirstOrDefault(Usable);
                    if (template != null) break;
                }
            }
            if (template == null) throw new InvalidOperationException("Native matchmaking Wild vine artwork is unavailable.");
            GameObject vines = CopyGeometry(template.m_wildVines);
            RestoreOpacity(vines);
            Face(vines, template.m_wildVines.transform, template.m_title.transform, camera);
            // Measure the source panel in the very same coordinate system as
            // its vines. Keep the original attachment offsets, rather than
            // treating a leaf tip as the corner of the panel.
            panelCorners = null;
            float largest = 0;
            foreach (Renderer renderer in template.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform.IsChildOf(template.m_wildVines.transform)
                    || renderer.GetComponentInParent<UberText>() != null
                    || renderer.GetComponentInParent<UIBButton>() != null) continue;
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                string name = ((renderer.gameObject.name ?? "") + " " + (mesh.name ?? "")).ToLowerInvariant();
                if (name.Contains("shadow") || name.Contains("glow") || name.Contains("blur") || name.Contains("fullscreen")) continue;
                Bounds bounds = mesh.bounds;
                var points = new Vector3[8];
                Bounds localBounds = default;
                int index = 0;
                for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) for (int iz = 0; iz < 2; iz++)
                {
                    Vector3 original = renderer.transform.TransformPoint(new Vector3(ix == 0 ? bounds.min.x : bounds.max.x,
                        iy == 0 ? bounds.min.y : bounds.max.y, iz == 0 ? bounds.min.z : bounds.max.z));
                    Vector3 local = template.m_wildVines.transform.InverseTransformPoint(original);
                    points[index] = local;
                    if (index++ == 0) localBounds = new Bounds(local, Vector3.zero);
                    else localBounds.Encapsulate(local);
                }
                // Selection happens before the copy is staged in front of the
                // export camera. Compare native local geometry, not an offstage
                // perspective projection or a live popup's animation scale.
                Vector3 size = localBounds.size;
                float area = Math.Max(size.x * size.y, Math.Max(size.x * size.z, size.y * size.z));
                if (area <= largest) continue;
                largest = area; panelCorners = points;
            }
            if (panelCorners == null) throw new InvalidOperationException("Native matchmaking panel reference is unavailable.");
            return vines;
        }

        internal void RememberRarity(Actor actor, Camera camera)
        {
            TAG_RARITY rarity = (TAG_RARITY)actor.GetEntityDef().GetTag(GAME_TAG.RARITY);
            if (m_rarityGems.ContainsKey(rarity) || actor.m_rarityGemMesh == null || actor.GetNameText() == null) return;
            GameObject gem = CopyGeometry(actor.m_rarityGemMesh);
            Face(gem, actor.m_rarityGemMesh.transform, actor.GetNameText().transform, camera);
            m_rarityGems[rarity] = gem;
        }
        internal GameObject RarityGem(TAG_RARITY rarity)
        {
            if (!m_rarityGems.TryGetValue(rarity, out GameObject source)) return null;
            GameObject gem = CopyGeometry(source); gem.transform.rotation = source.transform.rotation;
            return gem;
        }
        internal readonly struct TextureArt
        {
            internal readonly Texture Texture;
            internal readonly Vector2 Scale, Offset;
            internal TextureArt(Texture texture, Vector2 scale, Vector2 offset)
            { Texture = texture; Scale = scale; Offset = offset; }
        }
        internal TextureArt ClassIcon(TAG_CLASS deckClass)
        {
            HeroPickerButton button = Resources.FindObjectsOfTypeAll<HeroPickerButton>()
                .OrderByDescending(value => value.m_heroClass == deckClass)
                .FirstOrDefault(value => value.m_heroClassIcon != null && value.m_classLabel != null && value.CLASS_MATERIALS.Count >= 12);
            if (button == null) throw new InvalidOperationException("Native class icon is unavailable.");
            Material material = typeof(HeroPickerButton).GetMethod("GetClassIconMaterial", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(button, new object[] { deckClass }) as Material;
            Mesh mesh = button.m_heroClassIcon.GetComponent<MeshFilter>()?.sharedMesh;
            return TextureRegion(mesh, material, material?.mainTexture);
        }

        private static TextureArt TextureRegion(Mesh mesh, Material material, Texture texture)
        {
            if (material == null || texture == null || mesh == null)
                throw new InvalidOperationException("Native symbol texture or UVs are unavailable.");
            if (!mesh.isReadable) mesh = MixedPremiumMesh.GetReadableCopy(mesh);
            var uv = new List<Vector4>(); mesh.GetUVs(0, uv);
            if (uv.Count == 0) throw new InvalidOperationException("Native class icon UVs are unavailable.");
            float left = uv.Min(point => point.x), bottom = uv.Min(point => point.y);
            float width = uv.Max(point => point.x) - left, height = uv.Max(point => point.y) - bottom;
            Vector2 scale = material.mainTextureScale, offset = material.mainTextureOffset;
            if (width <= 0 || height <= 0 || scale.x == 0 || scale.y == 0)
                throw new InvalidOperationException("Native class icon has an empty atlas region.");
            // Copy the native symbol's RGBA atlas region directly. The hero
            // picker mesh/material can be invisible outside its own UI scene,
            // even after resetting its fade. Preserve both mesh UVs and the
            // requested class material's scale/offset, including flipped axes.
            return new TextureArt(texture, new Vector2(width * scale.x, height * scale.y),
                new Vector2(left * scale.x + offset.x, bottom * scale.y + offset.y));
        }

        internal static void SideboardStyle(Actor actor, TAG_PREMIUM premium)
        {
            GhostCard ghost = actor.m_ghostCardGameObject?.GetComponent<GhostCard>();
            if (ghost == null) throw new InvalidOperationException("Native rune-invalid card style is unavailable.");
            ghost.SetGhostType(GhostCard.Type.NOT_VALID); ghost.SetPremium(premium);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo initialize = typeof(GhostCard).GetMethod("Init", flags, null, new[] { typeof(bool), typeof(bool) }, null);
            MethodInfo apply = typeof(GhostCard).GetMethod("ApplyGhostMaterials", flags);
            if (initialize == null || apply == null) throw new InvalidOperationException("Native rune-invalid material API is unavailable.");
            // Use the game's direct mesh-material route. RenderGhostCard also
            // moves the card root and creates a separate cached RTT quad, which
            // cannot be registered pixel-for-pixel against the export camera.
            initialize.Invoke(ghost, new object[] { true, false });
            apply.Invoke(ghost, null);
            RenderToTexture capture = ghost.GetComponent<RenderToTexture>();
            if (capture != null) capture.enabled = false;
            if (ghost.m_R2T_EffectGhost != null) ghost.m_R2T_EffectGhost.enabled = false;
            foreach (GameObject plane in new[] { ghost.m_GlowPlane, ghost.m_GlowPlaneElite })
                if (plane != null)
                    foreach (Renderer renderer in plane.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }
        internal GameObject CountShield(bool wild, bool wide, Camera camera, out Color textColor)
        {
            CollectionCardCount count = Resources.FindObjectsOfTypeAll<CollectionCardCount>()
                .OrderByDescending(value => value.gameObject.activeInHierarchy)
                .FirstOrDefault(value => value.m_normalTab != null && value.m_normalBorder != null && value.m_normalCountText != null);
            if (count == null) throw new InvalidOperationException("Native card quantity shield is unavailable.");
            textColor = wild ? count.m_wildTextColor : count.m_standardTextColor;
            GameObject border = wide && count.m_normalWideBorder != null ? count.m_normalWideBorder : count.m_normalBorder;
            // Copy the requested border directly. Its enclosing tab can carry
            // hidden state, text ancestry and unrelated counter decorations.
            GameObject shield = CopyGeometry(border);
            Material material = wild ? count.m_wildBorderMaterial : count.m_standardBorderMaterial;
            if (material != null)
                foreach (Renderer renderer in shield.GetComponentsInChildren<Renderer>(true))
                    if (renderer.GetComponent<MeshFilter>() != null)
                    { Material owned = new Material(material); m_materials.Add(owned); renderer.sharedMaterial = owned; }
            RestoreOpacity(shield);
            Face(shield, border.transform, count.m_normalCountText.transform, camera); return shield;
        }
        private CollectionDeckInfo ManaCurveInfo()
        {
            m_deckInfo ??= Resources.FindObjectsOfTypeAll<CollectionDeckInfo>()
                .FirstOrDefault(value => value.m_manaBars?.Any(bar => bar?.m_barFill != null && bar.m_costText != null) == true);
            if (m_deckInfo == null)
            {
                DeckTrayDeckListContent content = Resources.FindObjectsOfTypeAll<DeckTrayDeckListContent>().FirstOrDefault(value => !string.IsNullOrEmpty(value.m_deckInfoActorPrefab));
                if (content != null)
                {
                    var asset = AssetLoader.Get().LoadAsset<GameObject>(content.m_deckInfoActorPrefab); m_assets.Add(asset);
                    m_deckInfo = asset?.Asset?.GetComponentInChildren<CollectionDeckInfo>(true);
                }
            }
            return m_deckInfo;
        }

        internal GameObject ManaWell(Camera camera, out Func<Rect> measure)
        {
            measure = null;
            CollectionDeckInfo info = ManaCurveInfo();
            var bars = info?.m_manaBars?.Where(bar => bar?.m_costText != null
                && bar.m_barFill?.GetComponent<MeshFilter>()?.sharedMesh != null).OrderBy(bar => bar.m_manaCostID).ToArray();
            if (bars == null || bars.Length < 3) return null;

            int middle = bars.Length / 2;
            DeckInfoManaBar bar = bars[middle];
            GameObject source = info.m_root ?? info.m_visualRoot ?? info.gameObject;
            if (!bar.m_barFill.transform.IsChildOf(source.transform)) source = info.gameObject;
            var fills = bars.Select(value => value.m_barFill.transform).ToArray();
            Transform heroPower = info.m_heroPowerParent?.transform;
            Transform offClicker = info.m_offClicker?.transform;
            GameObject artwork = CopyGeometry(source, include: renderer =>
            {
                if (!renderer.enabled || fills.Any(fill => renderer.transform.IsChildOf(fill))) return false;
                for (Transform node = renderer.transform; node != source.transform; node = node.parent)
                    if (!node.gameObject.activeSelf || node == heroPower || node == offClicker) return false;
                return true;
            });
            if (artwork.GetComponentsInChildren<MeshRenderer>(true).Length == 0) return null;
            Face(artwork, source.transform, bar.m_costText.transform, camera);

            Vector3 InSource(DeckInfoManaBar value, Vector3 point) =>
                source.transform.InverseTransformPoint(value.m_barFill.transform.TransformPoint(point));
            Bounds FillBounds(DeckInfoManaBar value) => value.m_barFill.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 previous = InSource(bars[middle - 1], FillBounds(bars[middle - 1]).center);
            Vector3 next = InSource(bars[middle + 1], FillBounds(bars[middle + 1]).center);
            Bounds fillBounds = FillBounds(bar);
            Vector3 center = InSource(bar, fillBounds.center);
            var corners = new List<Vector3>(8);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        corners.Add(InSource(bar, new Vector3(x == 0 ? fillBounds.min.x : fillBounds.max.x,
                            y == 0 ? fillBounds.min.y : fillBounds.max.y, z == 0 ? fillBounds.min.z : fillBounds.max.z)));
            // Sample a middle recess between adjacent fill centers. The
            // extra height includes the native rim and room for the count.
            measure = () =>
            {
                Vector3 Screen(Vector3 point) => camera.WorldToScreenPoint(artwork.transform.TransformPoint(point));
                float width = Math.Abs(Screen(next).x - Screen(previous).x) / 2;
                float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
                foreach (Vector3 point in corners)
                {
                    float y = Screen(point).y;
                    bottom = Math.Min(bottom, y);
                    top = Math.Max(top, y);
                }
                float height = top - bottom;
                return new Rect(Screen(center).x - width / 2, bottom - height * .05f, width, height * 1.28f);
            };
            return artwork;
        }

        internal GameObject ManaBar(float fraction, Camera camera)
        {
            DeckInfoManaBar source = ManaCurveInfo()?.m_manaBars?.FirstOrDefault(bar => bar?.m_barFill != null && bar.m_costText != null);
            if (source == null) throw new InvalidOperationException("Native mana curve artwork is unavailable.");
            GameObject barObject = CopyGeometry(source.m_barFill);
            Face(barObject, source.m_barFill.transform, source.m_costText.transform, camera);
            foreach (Renderer renderer in barObject.GetComponentsInChildren<Renderer>(true))
            {
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                properties.SetFloat("_Percent", fraction); renderer.SetPropertyBlock(properties);
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && material.HasProperty("_Percent")) material.SetFloat("_Percent", fraction);
            }
            return barObject;
        }

        internal GameObject ManaCrystal(Camera camera)
        {
            m_manaTab ??= Resources.FindObjectsOfTypeAll<ManaFilterTab>()
                .Where(tab => tab.m_costText != null && tab.m_crystal != null && tab.m_crystal.gem != null)
                .OrderByDescending(tab => tab.m_crystal.state == global::ManaCrystal.State.READY)
                .ThenByDescending(tab => tab.gameObject.activeInHierarchy)
                .FirstOrDefault();
            if (m_manaTab == null) throw new InvalidOperationException("Native mana crystal is unavailable.");
            GameObject gem = m_manaTab.m_crystal.gem;
            MeshRenderer crystal = gem.GetComponentsInChildren<MeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh != null);
            if (crystal == null) throw new InvalidOperationException("Native mana crystal has no geometry.");
            GameObject art = CopyGeometry(gem, include: renderer => renderer == crystal);
            Face(art, gem.transform, m_manaTab.m_costText.transform, camera);
            return art;
        }

        internal GameObject DustIcon(Camera camera)
        {
            ArcaneDustAmount amount = Resources.FindObjectsOfTypeAll<ArcaneDustAmount>()
                .OrderByDescending(value => value.gameObject.activeInHierarchy)
                .FirstOrDefault(value => value.m_dustJar != null && value.m_dustCount != null);
            if (amount != null)
            {
                GameObject jar = CopyGeometry(amount.m_dustJar);
                Face(jar, amount.m_dustJar.transform, amount.m_dustCount.transform, camera);
                return jar;
            }
            CurrencyFrame frame = Resources.FindObjectsOfTypeAll<CurrencyFrame>()
                .FirstOrDefault(value => value.CurrentCurrencyType == CurrencyType.DUST && value.CurrencyIconContainer != null && value.gameObject.activeInHierarchy);
            if (frame != null)
            {
                GameObject icon = CopyGeometry(frame.CurrencyIconContainer, true);
                Camera native = CameraUtils.FindProjectionCameraForObject(frame.CurrencyIconContainer);
                icon.transform.rotation = native != null ? camera.transform.rotation * Quaternion.Inverse(native.transform.rotation) * frame.CurrencyIconContainer.transform.rotation
                    : frame.CurrencyIconContainer.transform.rotation;
                return icon;
            }
            throw new InvalidOperationException("Native Arcane Dust icon is unavailable.");
        }

        private static void Face(GameObject copy, Transform source, Transform text, Camera camera)
        {
            // Native UI artwork lies in the X/Z plane; its text provides the
            // orientation without assuming a particular collection camera.
            copy.transform.rotation = camera.transform.rotation * Quaternion.Inverse(text.rotation) * source.rotation;
        }

        private GameObject CopyGeometry(GameObject source, bool visibleOnly = false, Func<Renderer, bool> include = null)
        {
            var root = new GameObject("BCM_DeckImageNative_" + source.name);
            m_objects.Add(root); root.SetActive(false);
            root.transform.SetParent(m_parent, false);
            root.transform.localScale = source.transform.lossyScale;
            var nodes = new Dictionary<Transform, Transform> { [source.transform] = root.transform };
            Transform CopyNode(Transform original)
            {
                if (nodes.TryGetValue(original, out Transform existing)) return existing;
                Transform parent = CopyNode(original.parent);
                Transform node = new GameObject(original.name).transform;
                node.SetParent(parent, false);
                node.localPosition = original.localPosition;
                node.localRotation = original.localRotation;
                node.localScale = original.localScale;
                nodes[original] = node;
                return node;
            }
            foreach (Renderer original in source.GetComponentsInChildren<Renderer>(true))
            {
                if (include != null && !include(original)) continue;
                if (visibleOnly && (!original.enabled || !original.gameObject.activeInHierarchy)) continue;
                UberText text = original.GetComponentInParent<UberText>();
                // Icons may be attached below a label in the source prefab.
                // Only exclude text belonging to the subtree being copied.
                if (text != null && text.transform.IsChildOf(source.transform)) continue;
                Mesh mesh = original.GetComponent<MeshFilter>()?.sharedMesh;
                if (original is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    mesh = new Mesh(); m_meshes.Add(mesh); skin.BakeMesh(mesh); mesh.RecalculateBounds();
                }
                if (mesh == null) continue;
                GameObject node = CopyNode(original.transform).gameObject;
                node.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = node.AddComponent<MeshRenderer>();
                Material[] materials = (Material[])original.sharedMaterials.Clone();
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null) { materials[i] = new Material(materials[i]); m_materials.Add(materials[i]); }
                renderer.sharedMaterials = materials;
                renderer.sortingLayerID = original.sortingLayerID; renderer.sortingOrder = original.sortingOrder;
                var properties = new MaterialPropertyBlock(); original.GetPropertyBlock(properties); renderer.SetPropertyBlock(properties);
            }
            return root;
        }

        private static void RestoreOpacity(GameObject graphics)
        {
            foreach (Renderer renderer in graphics.GetComponentsInChildren<Renderer>(true))
            {
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    // Same opacity properties used by native RenderUtils.SetAlpha.
                    // Work only on the private export copies, including the
                    // property block that otherwise overrides material color.
                    foreach (string property in new[] { "_Color", "_TintColor" })
                    {
                        if (!material.HasProperty(property)) continue;
                        Color color = material.GetColor(property); color.a = 1;
                        material.SetColor(property, color); properties.SetColor(property, color);
                    }
                }
                renderer.SetPropertyBlock(properties);
            }
        }

        internal static void Fit(GameObject artwork, Camera camera, Rect target, bool preserveAspect = false, Func<Rect> measure = null)
        {
            // Scale in camera space to fit parchment and bar meshes without
            // changing their UVs or replacing their native shaders.
            Transform root = artwork.transform;
            Quaternion rotation = root.rotation; Vector3 scale = root.localScale;
            var holder = new GameObject("BCM_DeckImageArtFit");
            holder.transform.SetParent(root.parent, false);
            float depth = (camera.nearClipPlane + camera.farClipPlane) * .5f;
            holder.transform.position = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, depth));
            holder.transform.rotation = camera.transform.rotation;
            root.SetParent(holder.transform, false);
            root.localPosition = Vector3.zero;
            root.rotation = rotation; root.localScale = scale;
            Rect Bounds()
            {
                if (measure != null) return measure();
                ComboSceneGeometry.ScreenBounds(artwork, camera, out Rect bounds);
                return bounds;
            }
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    Rect bounds = Bounds();
                    if (bounds.width <= .001f || bounds.height <= .001f)
                        throw new InvalidOperationException("Native export artwork has no geometry.");
                    Vector3 size = holder.transform.localScale;
                    if (preserveAspect) holder.transform.localScale = size * Math.Min(target.width / bounds.width, target.height / bounds.height);
                    else holder.transform.localScale = new Vector3(size.x * target.width / bounds.width, size.y * target.height / bounds.height, size.z);
                }
                Rect area = Bounds();
                if (area.width > .001f && area.height > .001f)
                    holder.transform.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth))
                        - camera.ScreenToWorldPoint(new Vector3(area.center.x, area.center.y, depth));
                // Keep the fitting transform as the graphics' parent until the
                // capture finishes; collapsing non-uniform scale loses shear.
            }
            catch { root.SetParent(holder.transform.parent, true); UnityEngine.Object.Destroy(holder); throw; }
        }

        public void Dispose()
        {
            foreach (GameObject root in m_objects) if (root != null) UnityEngine.Object.Destroy(root);
            foreach (Material material in m_materials) UnityEngine.Object.Destroy(material);
            foreach (Mesh mesh in m_meshes) UnityEngine.Object.Destroy(mesh);
            m_paperAsset?.Dispose();
            foreach (var asset in m_assets) asset?.Dispose();
        }
    }
}
