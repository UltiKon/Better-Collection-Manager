using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

public sealed class DefLoader
{
    private static readonly DefLoader Instance = new DefLoader();
    public static DefLoader Get() => Instance;
    public readonly Dictionary<string, EntityDef> Definitions = new Dictionary<string, EntityDef>();
    public EntityDef GetEntityDef(string id) => Definitions.TryGetValue(id, out EntityDef def) ? def : null;
    public DisposableCardDef GetCardDef(string id) => new DisposableCardDef { CardDef = new CardDef { Id = id } };
    public sealed class DisposableCardDef : IDisposable
    {
        public CardDef CardDef { get; set; }
        public void Dispose() { }
    }
}
public sealed class CardDef
{
    public string Id;
    public static string PendingId, FailingId;
    public static readonly List<Blizzard.T5.AssetManager.AssetHandle<Texture>> Pending = new List<Blizzard.T5.AssetManager.AssetHandle<Texture>>();
    public static int PortraitLoads;
    public Blizzard.T5.AssetManager.AssetHandle<Texture> GetPortraitTexture(TAG_PREMIUM premium, bool fallbackToLowerPremium = false, bool requireAvailableOnDisk = false)
    {
        PortraitLoads++;
        if (Id == FailingId) throw new InvalidOperationException("Portrait temporarily unavailable");
        var handle = new Blizzard.T5.AssetManager.AssetHandle<Texture> { Asset = PendingId == Id ? null : new Texture { name = "Portrait:" + Id } };
        if (PendingId == Id) Pending.Add(handle);
        return handle;
    }
}
public sealed class TooltipPanelManager
{
    public enum Orientation { RightTop, RightBottom }
    private static readonly TooltipPanelManager Instance = new TooltipPanelManager();
    public static TooltipPanelManager Get() => Instance;
    public int Requests, Hides;
    public void HideKeywordHelp() { Hides++; }
    public void UpdateKeywordHelpForCollectionManager(EntityDef definition, Actor actor, Orientation orientation)
    {
        NativeComboRuntime.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), this, new object[] { definition, actor, orientation }, _ => { Requests++; });
    }
}
// Public fields match the installed client. In particular the portrait field
// is m_deckTexture, not m_portrait. Loading the picker asset needs no open page.
public sealed class DeckTemplatePicker : Component { public DeckTemplatePickerButton m_pickerButtonTpl; public Material m_deckArtMaterial = new Material { name = "NativeRecipePortraitMaterial" }; }
public sealed class DeckTemplatePickerButton : PegUIElement
{
    public MeshRenderer m_deckTexture, m_packFlap, m_packInner, m_packRibbon;
    public Material m_packCustomMaterial;
    public UberText m_title;
    public List<UberText> m_cardCountTexts = new List<UberText>();
    public GameObject m_selectGlow, m_runes, m_incompleteTextRibbon, m_completeTextRibbon;
}
public sealed class AnimatedLowPolyPack : Component { }
public sealed class RandomTransform : Component { }
public sealed class CollectionCardLock : Component
{
    private GameObject m_lockPlate, m_bannedRibbon;
    private UberText m_lockText;
    public void Init()
    {
        var background = new GameObject("Native card-shaped lock backdrop"); background.transform.SetParent(transform, false);
        background.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(2, .2f, 3)) };
        background.AddComponent<MeshRenderer>().sharedMaterial = new Material { renderQueue = 2000 };
        m_lockPlate = new GameObject("Native lock plate"); m_lockPlate.transform.SetParent(transform, false);
        m_lockPlate.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(2, .05f, .3f)) };
        m_lockPlate.AddComponent<MeshRenderer>().sharedMaterial = new Material { renderQueue = 2000 };
        m_bannedRibbon = new GameObject("Native banned ribbon"); m_bannedRibbon.transform.SetParent(transform, false); m_bannedRibbon.SetActive(false);
        var label = new GameObject("Native lock reason"); label.transform.SetParent(m_lockPlate.transform, false);
        m_lockText = label.AddComponent<UberText>(); m_lockText.Text = "Deck is full.";
    }
    public void UpdateReason(string text) { m_lockText.Text = text ?? ""; }
}
public static class PackageRecipeFixture
{
    public static GameObject Make(Transform parent = null)
    {
        var root = new GameObject("NativePackageRecipeFixture"); root.transform.SetParent(parent, false);
        var controller = root.AddComponent<DeckTemplatePickerButton>();
        GameObject Part(string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, size) };
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material { renderQueue = 3000 }; return go;
        }
        GameObject frame = Part("Wooden frame", Vector3.zero, new Vector3(4, .2f, 6));
        // Native skin bounds have not been evaluated for an inactive prefab.
        // There is no valid render bound until the first player render update.
        var frameRenderer = frame.GetComponent<MeshRenderer>();
        Mesh frameMesh = frame.GetComponent<MeshFilter>().sharedMesh;
        frameMesh.vertices = new[] { new Vector3(-2, 0, -3), new Vector3(2, 0, -3), new Vector3(2, 0, 3), new Vector3(-2, 0, 3) };
        frameMesh.subMeshCount = 1;
        frameMesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false);
        var skin = frame.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = frameMesh; skin.sharedMaterials = frameRenderer.sharedMaterials;
        UnityEngine.Object.DestroyImmediate(frameRenderer);
        Part("Pack drop shadow", new Vector3(1, .1f, -1), new Vector3(12, .2f, 18));
        controller.m_packRibbon = Part("Class ribbon", new Vector3(0, -.1f, -1), new Vector3(3, .2f, 1)).GetComponent<MeshRenderer>();
        controller.m_packRibbon.sharedMaterial.color = Color.white;
        controller.m_deckTexture = Part("DeckTexture", new Vector3(0, -.1f, 1), new Vector3(3, .2f, 3)).GetComponent<MeshRenderer>();
        var title = new GameObject("Text"); title.transform.SetParent(root.transform, false); controller.m_title = title.AddComponent<UberText>(); controller.m_title.Text = "Native recipe";
        var count = new GameObject("Number"); count.transform.SetParent(root.transform, false); controller.m_cardCountTexts.Add(count.AddComponent<UberText>()); controller.m_cardCountTexts[0].Text = "30/30";
        controller.m_completeTextRibbon = Part("Complete badge", new Vector3(0, 0, -4.3f), new Vector3(1.4f, .2f, 1.8f));
        count.transform.SetParent(controller.m_completeTextRibbon.transform, false);
        controller.m_incompleteTextRibbon = Part("Incomplete badge", new Vector3(0, 0, -4.1f), new Vector3(1.4f, .2f, 1.8f));
        var incompleteCount = new GameObject("Incomplete number"); incompleteCount.transform.SetParent(controller.m_incompleteTextRibbon.transform, false);
        controller.m_cardCountTexts.Add(incompleteCount.AddComponent<UberText>());
        controller.m_cardCountTexts[1].Text = "0/30";
        // The native glow field can be an inactive container rather than the
        // renderer itself. Its cold child geometry must not need an editor visit.
        controller.m_selectGlow = new GameObject("Selection");
        controller.m_selectGlow.transform.SetParent(root.transform, false);
        var glowVisuals = new GameObject("Selection visuals");
        glowVisuals.transform.SetParent(controller.m_selectGlow.transform, false);
        var glowSurface = Part("Selection surface", Vector3.zero, new Vector3(40, .2f, 60));
        glowSurface.transform.SetParent(glowVisuals.transform, false);
        glowSurface.GetComponent<MeshRenderer>().enabled = false;
        glowVisuals.SetActive(false); controller.m_selectGlow.SetActive(false);
        root.AddComponent<AnimatedLowPolyPack>(); root.AddComponent<RandomTransform>(); root.AddComponent<BoxCollider>();
        return root;
    }
}

internal static partial class Program
{
    private static void CheckPackages(CollectibleCard a, CollectibleCard b, string path)
    {
        var manager = CollectionManager.Get();
        var previousDeck = manager.EditedDeck; var previousTray = CollectionDeckTray.Instance;
        var previousInput = CollectionInputMgr.Instance; var previousResources = Resources.Objects;
        var previousMode = NativeComboRuntime.Active;
        var previousClick = CollectionQualitySettings.Click;
        var previousProjection = CameraUtils.Projection;
        int inventory = manager.Cards.Count;
        var snapshot = new CategoryDeckImport.Deck { Name = "Packages" };
        snapshot.Cards.Add(a.CardId); snapshot.Cards.Add(b.CardId); snapshot.RawCounts[a.CardId] = 2; snapshot.RawCounts[b.CardId] = 1;
        var visuals = new List<CollectionCardVisual>();
        var previousMeshFactory = AssetLoader.Get().MeshFactory;
        AssetLoader.Get().MeshFactory = () => { var mesh = previousMeshFactory(); mesh.RecalculateBounds(); mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size * .5f); return mesh; };
        var source = AssetLoader.Get().InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>(); source.SetEntityDef(a.GetEntityDef());
        var recipe = PackageRecipeFixture.Make();
        var pickerAsset = new GameObject("Native recipe page asset"); pickerAsset.SetActive(false);
        pickerAsset.AddComponent<DeckTemplatePicker>().m_pickerButtonTpl = recipe.GetComponent<DeckTemplatePickerButton>();
        string previousAsset = manager.Display.m_deckTemplatePickerPrefab;
        const string nativeAsset = "InstalledClientRecipePage.prefab:fixture";
        manager.Display.m_deckTemplatePickerPrefab = nativeAsset; AssetLoader.Get().Assets.Add(nativeAsset, pickerAsset);
        try
        {
            foreach (string animal in new[] { "Fox", "Stag", "Bear" })
            {
                string id = "TEST_WILDSEED_" + animal.ToUpperInvariant();
                DefLoader.Get().Definitions[id] = new EntityDef { CardId = id, Name = animal + " Wildseed" };
            }
            GameUtils.RelatedCards[a.CardId] = DefLoader.Get().Definitions.Keys.Where(id => id.StartsWith("TEST_WILDSEED_")).ToArray();
            const string collectibleRef = "TEST_REFERENCED_COLLECTIBLE", otherToken = "TEST_OTHER_FAMILY_TOKEN", nestedToken = "TEST_NESTED_TOKEN";
            var referencedCollectible = new EntityDef { CardId = collectibleRef, Name = "Referenced collectible" };
            referencedCollectible.SetTag(GAME_TAG.COLLECTIBLE, 1); DefLoader.Get().Definitions[collectibleRef] = referencedCollectible;
            DefLoader.Get().Definitions[otherToken] = new EntityDef { CardId = otherToken, Name = "Other family's token" };
            DefLoader.Get().Definitions[nestedToken] = new EntityDef { CardId = nestedToken, Name = "Nested transformation" };
            GameUtils.RelatedCards[collectibleRef] = new[] { otherToken };
            GameUtils.RelatedCards["TEST_WILDSEED_FOX"] = new[] { nestedToken, a.CardId };
            GameUtils.RelatedCards[a.CardId] = GameUtils.RelatedCards[a.CardId].Concat(new[] { collectibleRef }).ToArray();
            var artCatalog = PackageArtwork.For(new[] { a.CardId, b.CardId });
            while (artCatalog.Advance()) { }
            var seeds = artCatalog.Search("wildseed");
            Check(seeds.Count == 3 && seeds.All(choice => choice.Definition != null)
                && manager.Cards.Count == inventory && seeds.All(choice => !manager.Cards.Keys.Any(key => key.Item1 == choice.Id)),
                "Artwork search includes all three uncollectible token definitions without inserting inventory cards");
            const string unrelated = "TEST_UNRELATED_WILDSEED";
            DefLoader.Get().Definitions[unrelated] = new EntityDef { CardId = unrelated, Name = "Unrelated Wildseed" };
            PackageArtwork.Resolve(unrelated);
            Check(artCatalog.Search(unrelated).Count == 0 && !artCatalog.Contains(unrelated),
                "Typing an unrelated token ID cannot broaden the package artwork scope even when its definition is loaded");
            var otherPackage = PackageArtwork.For(new[] { b.CardId });
            while (otherPackage.Advance()) { }
            Check(otherPackage.Search("wildseed").Count == 0, "Artwork choices do not leak between different packages");
            Check(artCatalog.Contains(a.CardId) && artCatalog.Contains(b.CardId) && artCatalog.Contains(nestedToken)
                && !artCatalog.Contains(collectibleRef) && !artCatalog.Contains(otherToken),
                "Artwork includes contained cards and their transitive token forms, with cycle protection and no traversal into referenced collectibles");
            string artwork = seeds[0].Id;
            CategoryCatalog.AddCombo("Packages", "Wildseed package", "", snapshot, packageArtwork: artwork);
            CategoryCatalog.LoadUserFile(path);
            var package = CategoryCatalog.Combos.Single(combo => combo.IsPackage);
            Check(package.Artwork == artwork && package.ExactVersions && package.Cards.Single(card => card.Id == a.CardId).Count == 2,
                "A package persists separate token artwork and exact captured editions and quantities");
            CategoryCatalog.UpdateCombo(package.Id, "Renamed package", "", packageArtwork: seeds[1].Id);
            CategoryCatalog.LoadUserFile(path); package = CategoryCatalog.Combos.Single(combo => combo.IsPackage);
            Check(package.Name == "Renamed package" && package.Artwork == seeds[1].Id && package.Cards.Count == 2,
                "Editing package art/name preserves its identity and contents");
            int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            bool rejected = false;
            try { CategoryCatalog.UpdateCombo(package.Id, "Broken", "", packageArtwork: "../bad\n.card"); }
            catch (FormatException) { rejected = true; }
            Check(rejected && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                "Invalid package artwork is rejected before writing a persistent edit");
            ExtraFilterState.ClearAll(); ExtraFilterState.TogglePackage(PackageKind.Custom);
            ComboCollectionCards.Ready = true;
            PackageNativeTile.ReleaseAssets();
            int loadsBeforePreparing = CardDef.PortraitLoads;
            Resources.Objects = Array.Empty<UnityEngine.Object>();
            manager.Display.m_deckTemplatePickerPrefab = null;
            PackageNativeTile.Preload(package); // Collection results can precede page-manager setup.
            manager.Display.m_deckTemplatePickerPrefab = nativeAsset;
            var buckets = new Dictionary<int, List<CollectibleCard>> { [1] = manager.GetAllCards(), [2] = new List<CollectibleCard>() };
            int beforeResults = CardDef.PortraitLoads;
            ComboCollectionCards.Prepare(buckets)();
            Check(CardDef.PortraitLoads == beforeResults,
                "Collection result preparation never preloads off-page package portraits");
            var proxy = buckets[1].Single(card => ComboCollectionCards.StableId(card) == "BCM_COMBO_" + package.Id);
            var harmony = new HarmonyLib.Harmony(); ComboCollectionCards.Install(harmony); NativeComboRuntime.Active = harmony;
            CheckPackagePageLoading(proxy, a, package);
            CheckPackageArtworkFailure(proxy, a, package);
            Resources.Objects = Array.Empty<UnityEngine.Object>();
            CollectionCardVisual Bind(CollectibleCard card)
            {
                var visual = new GameObject("PackageSlot").AddComponent<CollectionCardVisual>(); visuals.Add(visual);
                new CollectibleRenderFixture().Render(card, visual, new CollectionCardActors(source)); return visual;
            }
            int instantiations = AssetLoader.Get().InstantiateCalls;
            PackageNativeTile.CaptureTemplate();
            Check(AssetLoader.Get().InstantiateCalls == instantiations && manager.Display.Picker == null,
                "Recipe graphics load from the manager's installed prefab without opening or instantiating the native recipe page");
            using (var mageTile = PackageNativeTile.TryCreate(source, new CategoryCatalog.Combo {
                IsPackage = true, Name = "Mage", Artwork = artwork, PackageClass = "MAGE", Cards = package.Cards }))
            using (var warriorTile = PackageNativeTile.TryCreate(source, new CategoryCatalog.Combo {
                IsPackage = true, Name = "Warrior", Artwork = artwork, PackageClass = "WARRIOR", Cards = package.Cards }))
            {
                Material Ribbon(PackageNativeTile value) => value.Root.GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => renderer.gameObject.name == "BCM_PackageClassRibbon").sharedMaterial;
                var originalRibbonMaterial = recipe.GetComponent<DeckTemplatePickerButton>().m_packRibbon.sharedMaterial;
                Check(Ribbon(mageTile).color.Equals(CollectionPageManager.ColorForClass(TAG_CLASS.MAGE))
                    && Ribbon(warriorTile).color.Equals(CollectionPageManager.ColorForClass(TAG_CLASS.WARRIOR)),
                    "Package banners use the client's recipe class colors independently for simultaneous tiles");
                Check(!ReferenceEquals(Ribbon(mageTile), Ribbon(warriorTile)) && originalRibbonMaterial.color.Equals(Color.white),
                    "Class banner coloring never modifies the shared recipe prefab or another package's material");
            }
            var folder = Bind(proxy); Actor folderActor = folder.GetActor();
            var tile = folder.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_PackageRecipe");
            Check(tile.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0
                && tile.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.gameObject.name == "Wooden frame" && r.bounds.size.x > 0),
                "An inactive recipe skin is baked with tight bounds before the first page render, without a frame or page turn");
            Check(tile.parent == folder.transform && !tile.IsChildOf(folderActor.transform),
                "Package art belongs to the native page slot and cannot inherit a hand-card actor's startup scale or animation");
            CardDef.PendingId = package.Artwork;
            PackageNativeTile waiting = null;
            var waitingOwner = new GameObject("PendingPortraitSlot");
            try
            {
                waiting = PackageNativeTile.TryCreate(source, package, waitingOwner.transform);
                Check(waiting != null && CardDef.Pending.Count > 0,
                    "A pending native portrait handle does not discard a prepared package or trigger the five-second construction backoff");
                waiting.Refresh(area: PackageNativeTile.CollectionArea(folder, CameraUtils.Camera));
                GameObject preparedRoot = waiting.Root;
                foreach (var handle in CardDef.Pending) handle.Asset = new Texture { name = "ReadyPortrait" };
                waiting.Refresh(area: PackageNativeTile.CollectionArea(folder, CameraUtils.Camera));
                Check(waiting.Root == preparedRoot && waiting.Root.activeInHierarchy
                    && waiting.Root.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.name == "BCM_PackagePortrait")
                        .All(r => r.sharedMaterial.mainTexture?.name == "ReadyPortrait"),
                    "A delayed portrait completes on the existing first-page package without another page turn or reconstruction");
            }
            finally { waiting?.Dispose(); UnityEngine.Object.Destroy(waitingOwner); CardDef.PendingId = null; CardDef.Pending.Clear(); }
            for (int turn = 0; turn < 3; turn++)
            {
                folder.Hide();
                Check(!tile.gameObject.activeSelf && tile.GetComponentsInChildren<UberText>(true).All(text => text.isHidden()),
                    "A native page Hide immediately hides the package and all its text without waiting for Actor's cached renderers");
                ComboCollectionCards.OnLateUpdate();
                Check(!tile.gameObject.activeSelf, "Late updates cannot reveal a package on a hidden page");
                folder.Show();
                Check(tile.gameObject.activeSelf && tile.GetComponentsInChildren<UberText>(false).All(text => !text.isHidden()),
                    "Showing the page restores the same package and its native captions immediately");
            }
            Check(tile.GetComponentsInChildren<UberText>(false).Any(text => text.Text == package.Name)
                && tile.GetComponentsInChildren<UberText>(false).Any(text => text.Text == "3 Cards")
                && !folderActor.GetMeshRenderer().enabled && source.GetMeshRenderer().enabled
                && recipe.GetComponent<DeckTemplatePickerButton>().m_title.Text == "Native recipe",
                "Package tiles clone native recipe artwork and change only their private labels/materials/renderers");
            Check(tile.GetComponentsInChildren<UberText>(false).All(text => !text.RenderToTexture
                && CounterTextLayout.VisibleRenderers(text).Length > 0),
                "Package captions have direct visible geometry before another frame can build cached native text planes");
            Vector3 carrierScale = folderActor.transform.localScale;
            folderActor.transform.localScale *= .03f;
            folderActor.gameObject.SetActive(false);
            ComboCollectionCards.OnLateUpdate();
            Check(tile.gameObject.activeInHierarchy,
                "The page's prepared package remains visible while its unused carrier runs a startup hide or scale animation");
            folderActor.gameObject.SetActive(true); folderActor.transform.localScale = carrierScale;
            Check(tile.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.gameObject.name == "BCM_PackagePortrait")
                .All(renderer => renderer.sharedMaterial.mainTexture?.name == "Portrait:" + package.Artwork)
                && tile.GetComponentsInChildren<Actor>(true).Length == 0
                && pickerAsset.GetComponent<DeckTemplatePicker>().m_deckArtMaterial.mainTexture == null,
                "Native recipe art uses the selected token's raw portrait texture and creates no extra card actor");
            var frame = tile.GetComponentsInChildren<Renderer>(true).Single(renderer => renderer.gameObject.name == "Wooden frame");
            Rect frameArea = ComboSceneGeometry.ProjectBounds(frame.bounds, CameraUtils.Camera);
            Rect cardArea = PackageNativeTile.CollectionArea(folder, CameraUtils.Camera);
            Check(Math.Abs(Math.Max(frameArea.width / cardArea.width, frameArea.height / cardArea.height) - .985f) < .01f
                && Math.Abs(frameArea.center.x - cardArea.center.x) < 1f && Math.Abs(frameArea.center.y - cardArea.center.y) < 1f,
                "The package body fills the complete input slot despite a half-sized Actor mesh, oversized glow and a separate count badge");
            Rect inputSlot = ComboSceneGeometry.CardSlot(folder, CameraUtils.Camera);
            Check(Math.Abs(cardArea.height / inputSlot.height - 1.08f) < .001f,
                "Package collection bodies receive the requested modest enlargement while preserving their aspect ratio");
            folderActor.GetMeshRenderer().LocalBounds = new Bounds(Vector3.zero, new Vector3(2, .1f, 2.7f));
            ComboCollectionCards.Apply(folder);
            Rect settled = ComboSceneGeometry.ProjectBounds(frame.bounds, CameraUtils.Camera);
            Check(Math.Abs(settled.width - frameArea.width) < .1f && Math.Abs(settled.height - frameArea.height) < .1f,
                "Native Actor initialization cannot shrink or grow an already sized package independently of its collection slot");
            folder.m_boxColliderSize *= 1.4f; ComboCollectionCards.Apply(folder);
            Rect resized = ComboSceneGeometry.ProjectBounds(frame.bounds, CameraUtils.Camera), slot = PackageNativeTile.CollectionArea(folder, CameraUtils.Camera);
            Check(Math.Abs(Math.Max(resized.width / slot.width, resized.height / slot.height) - .985f) < .01f,
                "A changed collection slot refits the package after the initial binding");
            folder.m_boxColliderSize *= 1f / 1.4f; ComboCollectionCards.Apply(folder);
            Vector3 slotPosition = folder.transform.localPosition, slotScale = folder.transform.localScale;
            Quaternion slotRotation = folder.transform.localRotation;
            Vector3 packagePosition = tile.localPosition, packageScale = tile.localScale;
            Quaternion packageRotation = tile.localRotation;
            try
            {
                for (int step = 0; step < 5; step++)
                {
                    folder.transform.localPosition = new Vector3(-300 + step * 75, step * .2f, -300 + step * 75);
                    folder.transform.localScale = new Vector3(.6f + step * .1f, 1, .7f + step * .05f);
                    float angle = step * .13f;
                    folder.transform.localRotation = new Quaternion(0, (float)Math.Sin(angle), 0, (float)Math.Cos(angle));
                    ComboCollectionCards.OnLateUpdate();
                    Check(tile.localPosition.Equals(packagePosition) && tile.localScale.Equals(packageScale)
                        && tile.localRotation.Equals(packageRotation),
                        "Page movement and rotation carry the complete package without recomputing its screen fit or assembling it during the turn");
                }
            }
            finally
            {
                folder.transform.localPosition = slotPosition; folder.transform.localScale = slotScale;
                folder.transform.localRotation = slotRotation; ComboCollectionCards.Apply(folder);
            }
            var tooltip = TooltipPanelManager.Get(); int requests = tooltip.Requests, hides = tooltip.Hides;
            tooltip.UpdateKeywordHelpForCollectionManager(folderActor.GetEntityDef(), folderActor, TooltipPanelManager.Orientation.RightTop);
            Check(tooltip.Requests == requests && tooltip.Hides == hides + 1, "Package hover suppresses and clears the carrier's keyword tooltip through the installed native hook");
            tooltip.UpdateKeywordHelpForCollectionManager(source.GetEntityDef(), source, TooltipPanelManager.Orientation.RightTop);
            Check(tooltip.Requests == requests + 1, "Ordinary card keyword tooltips retain their native behavior");
            // PegUIElement records Over before CollectionCardVisual.OnOver.
            // Native OnOver leaves the actor idle when the carrier has no owned
            // copies (including a package whose visible count badge is hidden).
            folder.SetInteractionState(PegUIElement.InteractionState.Over);
            folderActor.SetActorState(ActorStateType.CARD_IDLE); ComboCollectionCards.Apply(folder);
            var glow = tile.GetComponentsInChildren<Transform>(true).Single(part => part.gameObject.name == "BCM_PackageGlow");
            Check(glow.gameObject.activeInHierarchy
                && glow.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                "A cold package hover lights the native effect even when an unowned carrier stays idle, without visiting the editor");
            var sourceGlow = recipe.GetComponent<DeckTemplatePickerButton>().m_selectGlow;
            Check(!sourceGlow.activeSelf && sourceGlow.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled),
                "Preparing package hover does not warm or modify the shared recipe prefab");
            folder.SetInteractionState(PegUIElement.InteractionState.Out);
            folderActor.SetActorState(ActorStateType.CARD_IDLE); ComboCollectionCards.Apply(folder);
            Check(!glow.gameObject.activeInHierarchy
                && glow.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled),
                "Leaving the package clears its private selection glow and renderers");
            foreach (Renderer renderer in glow.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            folder.SetInteractionState(PegUIElement.InteractionState.Over);
            folderActor.SetActorState(ActorStateType.CARD_RECENTLY_ACQUIRED_MOUSE_OVER); ComboCollectionCards.OnLateUpdate();
            Check(glow.gameObject.activeInHierarchy && glow.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled),
                "A package with a newly acquired carrier lights its effect and restores renderers after a native reset");
            folder.SetInteractionState(PegUIElement.InteractionState.Out);
            // The native ownership guard can also leave an actor's old hover
            // state unchanged on exit. The folder must still clear its effect.
            ComboCollectionCards.Apply(folder);
            Check(!glow.gameObject.activeInHierarchy && glow.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled),
                "Leaving a package clears the glow even when the carrier retains a stale hover state");
            folderActor.SetActorState(ActorStateType.CARD_IDLE);
            Check(tile.GetComponentsInChildren<AnimatedLowPolyPack>(true).All(control => control.Destroyed)
                && tile.GetComponentsInChildren<RandomTransform>(true).All(control => control.Destroyed),
                "Package graphics remove native pack-flight and random-position controllers before activation");
            Check(tile.GetComponentsInChildren<Collider>(true).All(hit => !hit.enabled)
                && tile.GetComponentsInChildren<PegUIElement>(true).All(input => input.Destroyed),
                "Recipe graphics do not retain native click callbacks or competing colliders");
            manager.Editing = true; manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Instance = new CollectionDeckTray { Context = manager.EditedDeck };
            CollectionInputMgr.Instance = new CollectionInputMgr(); CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
            int capacity = manager.Rules.Capacity;
            manager.Rules.Capacity = 0; Time.unscaledTime += .2f; ComboCollectionCards.Apply(folder);
            var packageLock = folder.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_PackageLock");
            var plate = packageLock.GetComponentsInChildren<Renderer>(false).Single(renderer => renderer.gameObject.name == "Native lock plate");
            Rect lockArea = ComboSceneGeometry.ProjectBounds(plate.bounds, CameraUtils.Camera);
            Check(packageLock.GetComponentsInChildren<UberText>(false).Any(text => text.Text == "Deck is full.")
                && packageLock.GetComponentsInChildren<Transform>(true).All(part => part.gameObject.name != "Native card-shaped lock backdrop")
                && lockArea.width <= cardArea.width && lockArea.center.y > cardArea.center.y,
                "A full package uses the native lock plate fitted above the recipe title, without the carrier's card-shaped backdrop");
            Check(plate.sharedMaterial.renderQueue > frame.sharedMaterial.renderQueue
                && folder.m_cardLock.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled),
                "Package deck-limit artwork draws over the recipe and hides the original native card overlay");
            ComboCollectionCards.Apply(folder);
            Check(Math.Abs(ComboSceneGeometry.ProjectBounds(plate.bounds, CameraUtils.Camera).width - lockArea.width) < .1f,
                "Repeated package updates keep the lock plate size stable");
            manager.Rules.Capacity = capacity; Time.unscaledTime += .2f; ComboCollectionCards.Apply(folder);
            Check(!packageLock.gameObject.activeSelf, "Making room in the deck clears the package limit plate");
            int maxCopies = manager.Rules.MaxCopies;
            var errorText = manager.Rules.ErrorText;
            try
            {
                manager.Rules.MaxCopies = 0;
                manager.Rules.ErrorText = _ => "You can only have 2 copies in your deck.";
                Time.unscaledTime += .2f; ComboCollectionCards.Apply(folder);
                Check(packageLock.GetComponentsInChildren<UberText>(false).Any(text => text.Text == "Maximum copies in deck.")
                    && packageLock.GetComponentsInChildren<UberText>(false).All(text => !text.Text.Any(char.IsDigit)),
                    "Packages replace the native per-card copy number with an unnumbered limit message");
                package.IsPackage = false;
                ComboCollectionCards.EvaluateLock(package, out string ordinaryReason);
                Check(ordinaryReason == "You can only have 2 copies in your deck.",
                    "Ordinary combos retain their native numbered copy-limit message");
            }
            finally { package.IsPackage = true; manager.Rules.MaxCopies = maxCopies; manager.Rules.ErrorText = errorText; }
            bool previousBanned = manager.Rules.Banned;
            var nativeRibbon = folder.m_cardLock.GetComponentsInChildren<Transform>(true).Single(part => part.gameObject.name == "Native banned ribbon");
            Transform ribbonParent = nativeRibbon.parent;
            try
            {
                // A native banner may be anchored independently of the lock root.
                nativeRibbon.SetParent(folder.transform, false); nativeRibbon.gameObject.SetActive(true);
                manager.Rules.Banned = true; Time.unscaledTime += .2f; ComboCollectionCards.Apply(folder);
                Check(folder.LastLock == CollectionCardVisual.LockType.NONE && !nativeRibbon.gameObject.activeSelf && !packageLock.gameObject.activeSelf,
                    "Invalid packages suppress both the native banned ribbon and any previous private lock artwork");
                var lockType = CollectionCardVisual.LockType.BANNED; string bannedReason = "Banned";
                ComboCollectionCards.TryAdjustComboLock(folder, ref lockType, ref bannedReason);
                Check(lockType == CollectionCardVisual.LockType.NONE && bannedReason == null,
                    "Native package lock refreshes cannot reintroduce the redundant banned banner");
            }
            finally { manager.Rules.Banned = previousBanned; nativeRibbon.SetParent(ribbonParent, false); }
            Time.unscaledTime += .2f; ComboCollectionCards.Apply(folder);
            var release = typeof(CollectionCardVisual).GetMethod("OnRelease", BindingFlags.Instance | BindingFlags.NonPublic);
            int beforeFailedOpen = KeywordFilterApplier.Refreshes;
            CardListPanel.FailNextCreate = true;
            bool failedOpen = false;
            try { ComboCollectionCards.OpenPackage(package); }
            catch (InvalidOperationException) { failedOpen = true; }
            Check(failedOpen && ComboCollectionCards.OpenedPackage == null && KeywordFilterApplier.Refreshes == beforeFailedOpen,
                "Failed navigation leaves the package closed and collection results untouched");
            int errors = DebugLog.Exceptions.Count;
            ComboCollectionCards.OnUpdate();
            Check(DebugLog.Exceptions.Count == errors,
                "A failed package open does not retry broken navigation on every update");
            Input.LeftReleased = false; Input.RightReleased = false;
            int folderSounds = SoundManager.Get().Played.Count;
            Time.frameCount++; Input.LeftPressed = Input.LeftHeld = true;
            var folderCamera = CameraUtils.FindProjectionCameraForObject(folder.gameObject);
            var folderSlot = ComboSceneGeometry.CardSlot(folder, folderCamera);
            Input.mousePosition = new Vector3(folderSlot.center.x, folderSlot.center.y, 0);
            ComboCollectionCards.OnUpdate();
            Check(SoundManager.Get().Played.Count == folderSounds + 1 && ComboCollectionCards.OpenedPackage == null,
                "The package click sounds on mouse down before folder navigation runs");
            Input.LeftPressed = Input.LeftHeld = false; Input.LeftReleased = true; Time.frameCount++;
            CardListPanel.IncludeHoverGlow = true;
            try { ComboCollectionCards.HandleInput(folder, release); }
            finally { CardListPanel.IncludeHoverGlow = false; }
            Check(SoundManager.Get().Played.Count == folderSounds + 1
                && SoundManager.Get().Played.Last() == ComboCollectionCards.PackageClickSound,
                "Opening a package plays exactly one normal button-click sound");
            Check(ComboCollectionCards.OpenedPackage == package && manager.EditedDeck.Slots.Count == 0 && !UniversalInputManager.Get().DialogActive,
                "Left-click opens a package collection folder without adding cards or opening inspect");
            int tabSounds = SoundManager.Get().Played.Count;
            SoundManager.Get().LoadAndPlay("class_tab_retract.prefab:da79957be76b10343999d6fa92a6a2f0");
            Check(SoundManager.Get().Played.Count == tabSounds + 1,
                "Package navigation preserves native class-tab sounds");
            var navigation = (PackageNavigation)typeof(ComboCollectionCards).GetField("s_packageNavigation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var uiCamera = new Camera { PixelScale = 3f, Offset = new Vector3(411, 97, 0), DepthScale = .1f, nearClipPlane = 10f, farClipPlane = 110f };
            manager.Display.m_pageLeftArrowBone.transform.rotation = new Quaternion(0, .70710677f, 0, .70710677f);
            CameraUtils.Projection = go => OverlayUI.Get().HasObject(go) ? uiCamera : CameraUtils.Camera;
            navigation.Layout(new Rect(Screen.width * .1f, Screen.height * .4f, Screen.width * .4f, Screen.height * .1f), true);
            float navigationHeight = Math.Max(36f, Math.Min(Screen.height * .025f, Screen.height * .065f)) * 1.25f;
            Check(OverlayUI.Get().HasObject(navigation.Root) && !UniversalInputManager.Get().DialogActive,
                "Independent folder navigation stays in the native overlay hierarchy without a dialog or hidden popup ancestor");
            Check(navigation.Root.activeInHierarchy && ComboSceneGeometry.ScreenBounds(navigation.Back.Root, uiCamera, out Rect backArea)
                && Math.Abs(backArea.center.x - (Screen.width * .1f + navigationHeight * .5f)) < 1f
                && Math.Abs(backArea.center.y - (Screen.height * .5f + navigationHeight * .85f)) < 1f,
                "Back exists immediately on entering a folder and is positioned with its UI camera even when the card camera has different coordinates");
            Check(navigation.Back.Root.GetComponent<PagingArrow>() == null && navigation.Back.Root.GetComponent<UIBButton>() != null
                && navigation.Back.Root.GetComponent<UIBHighlight>() != null
                && NativeArrowFace(navigation.Back.Root).sharedMaterial.name == "Native hero left triangle",
                "Folder Back uses the complete native left arrow and its original hover behavior, as in Inspect");
            float arrowDepth = uiCamera.WorldToScreenPoint(navigation.Back.Root.transform.position).z;
            Quaternion facing = navigation.Back.Root.transform.rotation;
            manager.Display.m_pageLeftArrowBone.transform.rotation = Quaternion.identity;
            navigation.Layout(new Rect(Screen.width * .1f, Screen.height * .4f, Screen.width * .4f, Screen.height * .1f), true);
            Check(navigation.Back.Root.transform.rotation.Equals(facing) && navigation.Back.Root.transform.localScale.x > 0
                && arrowDepth > uiCamera.nearClipPlane && arrowDepth < uiCamera.farClipPlane,
                "Back retains the native picker arrow's direction independently of book bones and stays within the UI camera clipping planes");
            var backFeedback = navigation.Back.Root.GetComponent<UIBHighlight>();
            var nativeGlow = backFeedback.m_MouseOverHighlight;
            Check(nativeGlow != null && !nativeGlow.activeInHierarchy
                && nativeGlow.GetComponent<Renderer>().sharedMaterial.name == "Native arrow light",
                "Back keeps the source button's lit face hidden until hover");
            var backHit = navigation.Back.Root.GetComponent<BoxCollider>();
            Vector3 hitCenter = backHit.center, hitSize = backHit.size;
            Renderer backFace = NativeArrowFace(navigation.Back.Root);
            Rect idleFace = ComboSceneGeometry.ProjectBounds(backFace.bounds, uiCamera);
            var edge = new Vector3(idleFace.xMax - .25f, idleFace.center.y, 0);
            for (int hover = 0; hover < 8; hover++)
            {
                navigation.Back.Root.GetComponent<PegUIElement>().Send(hover % 2 == 0 ? UIEventType.ROLLOVER : UIEventType.ROLLOUT);
                navigation.Layout(new Rect(Screen.width * .1f, Screen.height * .4f, Screen.width * .4f, Screen.height * .1f), true);
                Rect litFace = ComboSceneGeometry.ProjectBounds(backFace.bounds, uiCamera);
                Check(nativeGlow.activeInHierarchy == (hover % 2 == 0)
                    && Math.Abs(litFace.xMin - idleFace.xMin) < .01f && Math.Abs(litFace.width - idleFace.width) < .01f
                    && backHit.center.Equals(hitCenter) && backHit.size.Equals(hitSize) && navigation.Back.Contains(uiCamera, edge),
                    "The native Back face lights on hover and returns to idle without resizing its edge hit area");
            }
            int openingSounds = SoundManager.Get().Played.Count;
            var right = typeof(CollectionCardVisual).GetMethod("OnRightClick", BindingFlags.Instance | BindingFlags.NonPublic);
            Time.frameCount++; Input.LeftReleased = false; Input.RightPressed = true;
            ComboCollectionCards.PollPackageClickSounds();
            Check(SoundManager.Get().Played.Count == openingSounds + 1 && !UniversalInputManager.Get().IsDialogActive(),
                "Right-click feedback precedes package editor setup");
            Input.RightPressed = false; Time.frameCount++;
            ComboCollectionCards.HandleInput(folder, right);
            Check(SoundManager.Get().Played.Count == openingSounds + 1 && SoundManager.Get().Played.Last().StartsWith("Small_Click"),
                "Right-click opens the package editor with one native menu opening sound: shown=" + folder.IsShown() + "; dialog=" + UniversalInputManager.Get().IsDialogActive() + "; " + string.Join(" | ", DebugLog.Exceptions.Skip(System.Math.Max(0, DebugLog.Exceptions.Count-2))));
            Check(ButtonListMenu.Visible is ComboPopup && ButtonListMenu.Visible.Header == "Edit Package"
                && ButtonListMenu.Visible.Buttons.Any(button => button.m_ButtonText.Text == "Delete package"),
                "Right-click opens package editing and deletion directly without an inspect step");
            var editorSkin = (ComboNativeSkin)typeof(ComboPopup).GetField("m_skin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ButtonListMenu.Visible);
            Time.unscaledTime += .25f;
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ButtonListMenu.Visible, null);
            var cancel = ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_CloseCombo");
            var packageExport = ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_ExportEntry");
            packageExport.Click();
            Check(UnityEngine.GUIUtility.systemCopyBuffer == CategoryCatalog.ExportTransfer(true, entryId: package.Id),
                "The package editor exports only the saved package to the clipboard");
            var packageDelete = ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_DeletePackage");
            Check(packageDelete.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.sharedMaterial?.name == "Native Concede red"),
                "Package Delete shares the native red Concede appearance with combo Delete");
            Check(ComboSceneGeometry.ScreenBounds(cancel.gameObject, CameraUtils.FindProjectionCameraForObject(cancel.gameObject), out Rect cancelArea)
                && Math.Abs(cancelArea.center.x - editorSkin.CancelArea.center.x) < 1f,
                "Cancel stays in the native footer's cancel well instead of crossing the metal center divider");
            Check(cancel.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.GetComponentInParent<UberText>() == null)
                .SelectMany(renderer => renderer.sharedMaterials).All(material => material == null || material.renderQueue >= 5000)
                && cancel.GetComponentsInChildren<Renderer>(false).All(renderer => renderer.sortingOrder > 0)
                && editorSkin.Template.m_cancelButton.GetComponent<Renderer>().sharedMaterial.renderQueue == 4600,
                "Cancel's private artwork renders above the complete native footer without changing shared button materials");
            Check(!ButtonListMenu.Visible.ActiveButtons.Any(button => button.gameObject == navigation.Back.Root),
                "Navigation buttons are independent clones outside the popup's native button cache");
            ComboCollectionCards.OnUpdate();
            Check(!navigation.Root.activeInHierarchy, "Folder navigation hides while its editor owns the modal input");
            ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_CloseCombo").Click();
            Check(!UniversalInputManager.Get().DialogActive && ComboCollectionCards.OpenedPackage?.Id == package.Id,
                "Cancel in the direct package editor returns to its folder without reopening inspect");
            ComboCollectionCards.OnUpdate();
            Check(navigation.Root.activeInHierarchy && navigation.Back.Root.activeInHierarchy,
                "Back becomes visible again after closing the package editor");
            ComboPopup.Close(); CameraUtils.Projection = previousProjection;
            PackagePicker.Open();
            Check(ButtonListMenu.Visible.Buttons.Any(button => button.gameObject.name == "BCM_EditPackage_" + package.Id),
                "The Packages editor lists the selected Custom group independently of card rendering");
            int pickerSounds = SoundManager.Get().Played.Count;
            ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_EditPackage_" + package.Id).Click();
            Check(SoundManager.Get().Played.Count == pickerSounds + 1,
                "The picker plays one press sound and the editor callback does not add a second click");
            Check(ButtonListMenu.Visible is ComboPopup && ButtonListMenu.Visible.Header == "Edit Package",
                "Selecting a package in the management picker opens its editor directly");
            ComboPopup.Close();
            buckets[1] = manager.GetAllCards(); ComboCollectionCards.Prepare(buckets)();
            Check(ComboCollectionCards.CollectionName(buckets[1][0]) == "Add All"
                && buckets.Values.SelectMany(list => list).Skip(1).All(card => card.CardId == a.CardId || card.CardId == b.CardId),
                "Folder results put Add All first and contain only the package's component cards");
            var editedPackageDeck = manager.EditedDeck;
            var updateFolder = typeof(ComboCollectionCards).GetMethod("UpdatePackageNavigation", BindingFlags.Static | BindingFlags.NonPublic);
            int transitionRefreshes = KeywordFilterApplier.Refreshes;
            manager.Editing = false; manager.EditedDeck = null; updateFolder.Invoke(null, null);
            Check(ComboCollectionCards.OpenedPackage?.Id == package.Id && KeywordFilterApplier.Refreshes == transitionRefreshes + 1,
                "Ending deck editing preserves the open folder and refreshes its contextual action");
            manager.Editing = true; manager.EditedDeck = editedPackageDeck; updateFolder.Invoke(null, null);
            Check(ComboCollectionCards.OpenedPackage?.Id == package.Id && KeywordFilterApplier.Refreshes == transitionRefreshes + 2,
                "Starting deck editing preserves the open folder and refreshes Add All");
            manager.Editing = false; manager.EditedDeck = null;
            buckets[1] = manager.GetAllCards(); ComboCollectionCards.Prepare(buckets)();
            int preparedRefreshes = KeywordFilterApplier.Refreshes; updateFolder.Invoke(null, null);
            Check(ComboCollectionCards.OpenedPackage?.Id == package.Id && KeywordFilterApplier.Refreshes == preparedRefreshes,
                "A native deck-exit refresh does not trigger a redundant second package refresh or page turn");
            CheckPackagePageTransitions(package, a, b);
            manager.Editing = true; manager.EditedDeck = editedPackageDeck;
            var originalClassRule = manager.Rules.ClassAllowed;
            try
            {
                manager.Rules.ClassAllowed = definition => definition.GetCardId() != b.CardId;
                int beforeIllegalDeck = KeywordFilterApplier.Refreshes;
                buckets[1] = manager.GetAllCards(); ComboCollectionCards.Prepare(buckets)();
                Check(ComboCollectionCards.OpenedPackage == null && KeywordFilterApplier.Refreshes == beforeIllegalDeck,
                    "Native deck-result preparation closes an incompatible package in the same pass without refreshing twice");
                manager.Rules.ClassAllowed = originalClassRule;
                manager.Editing = false; manager.EditedDeck = null; ComboCollectionCards.OpenPackage(package);
                manager.Editing = true; manager.EditedDeck = editedPackageDeck;
                manager.Rules.ClassAllowed = definition => definition.GetCardId() != b.CardId;
                beforeIllegalDeck = KeywordFilterApplier.Refreshes; updateFolder.Invoke(null, null);
                Check(ComboCollectionCards.OpenedPackage == null && KeywordFilterApplier.Refreshes == beforeIllegalDeck + 1,
                    "A deck change detected before native preparation closes an incompatible folder with one refresh");
            }
            finally { manager.Rules.ClassAllowed = originalClassRule; }
            ComboCollectionCards.BackFromPackage(); manager.Editing = false; manager.EditedDeck = null;
            ComboCollectionCards.OpenPackage(package);
            buckets[1] = manager.GetAllCards(); ComboCollectionCards.Prepare(buckets)();
            Check(ComboCollectionCards.CollectionName(buckets[1][0]) == "Edit Package",
                "Without an edited deck the first package action is labeled Edit Package");
            var editAction = Bind(buckets[1][0]);
            var editArea = ComboSceneGeometry.CardSlot(editAction, CameraUtils.FindProjectionCameraForObject(editAction.gameObject));
            Input.mousePosition = new Vector3(editArea.center.x, editArea.center.y, 0);
            int editPressSounds = SoundManager.Get().Played.Count;
            Time.frameCount++; Input.LeftPressed = Input.LeftHeld = true; Input.LeftReleased = false;
            ComboCollectionCards.PollPackageClickSounds();
            Check(SoundManager.Get().Played.Count == editPressSounds + 1 && !UniversalInputManager.Get().IsDialogActive(),
                "The no-deck Edit Package action sounds on press before creating its editor");
            Input.LeftPressed = Input.LeftHeld = false; Input.LeftReleased = true; Time.frameCount++;
            ComboCollectionCards.HandleInput(editAction, release);
            Check(ButtonListMenu.Visible.Header == "Edit Package" && SoundManager.Get().Played.Count == editPressSounds + 1,
                "Releasing the no-deck action opens the package editor without a second click");
            ComboPopup.Close(); ComboCollectionCards.BackFromPackage();
            manager.Editing = true; manager.EditedDeck = editedPackageDeck; ComboCollectionCards.OpenPackage(package);
            buckets[1] = manager.GetAllCards(); ComboCollectionCards.Prepare(buckets)();
            Check(ComboCollectionCards.CollectionName(buckets[1][0]) == "Add All",
                "Returning to an edited deck restores Add All");
            var addAll = Bind(buckets[1][0]); ComboCollectionCards.HandleInput(addAll, release);
            Check(manager.EditedDeck.LocalCount(a.CardId) == 2 && manager.EditedDeck.LocalCount(b.CardId) == 1
                && manager.EditedDeck.LocalCount(artwork) == 0,
                "Add All uses captured quantities and native deck rules; token artwork is never added");
            Check(CollectionDeckTray.Get().AnimationDeckCounts.SequenceEqual(new[] { 3, 3, 3 })
                && CollectionDeckTray.Get().Animations.Select(actor => actor.GetEntityDef().GetCardId())
                    .OrderBy(id => id).SequenceEqual(new[] { a.CardId, a.CardId, b.CardId }.OrderBy(id => id)),
                "Add All animates each contained copy after insertion has finished, using member identities rather than the selected token portrait");
            int backSounds = SoundManager.Get().Played.Count;
            navigation.Back.Root.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == backSounds + 1 && ComboCollectionCards.OpenedPackage != null,
                "The package Back arrow sounds before its release requests navigation");
            navigation.Back.Root.GetComponent<PegUIElement>().Release();
            Check(SoundManager.Get().Played.Count == backSounds + 1
                && SoundManager.Get().Played.Last() == ComboCollectionCards.PackageClickSound,
                "Package Back plays exactly one normal button-click sound without a deck-selection sound");
            ComboCollectionCards.OnUpdate();
            Check(ComboCollectionCards.OpenedPackage == null && !navigation.Root.activeInHierarchy, "The visible Back button leaves the package folder and removes its navigation");
            manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Instance = new CollectionDeckTray { Context = manager.EditedDeck };
            var drag = typeof(CollectionCardVisual).GetMethod("OnDrag", BindingFlags.Instance | BindingFlags.NonPublic);
            Input.LeftReleased = false;
            drag.Invoke(folder, null);
            Check(CollectionInputMgr.Get().HasHeldCard(), "A closed package starts through the actual native GrabCardVisual route");
            CollectionInputMgr.Get().OverDeck = true; Input.LeftReleased = true;
            Check(ComboCollectionCards.TryHandleDragRelease(CollectionInputMgr.Get()) && !CollectionInputMgr.Get().HasHeldCard(),
                "Package drop cancels the native carrier through its own cleanup");
            ComboCollectionCards.HandleInput(folder, release);
            Time.frameCount += 2; ComboCollectionCards.OnUpdate();
            Check(manager.EditedDeck.LocalCount(a.CardId) == 2 && manager.EditedDeck.LocalCount(b.CardId) == 1
                && ComboCollectionCards.OpenedPackage == null,
                "Dragging the closed folder adds its contents without opening it or adding an extra carrier");
            var savedCards = manager.Cards.ToArray(); var savedCounts = manager.Counts.ToArray();
            var savedDrag = CollectionQualitySettings.Drag;
            try
            {
                foreach (CollectibleCard member in new[] { a, b })
                {
                    manager.Cards[(member.CardId, TAG_PREMIUM.GOLDEN)] = Card(member.CardId, TAG_PREMIUM.GOLDEN, 1);
                    manager.Counts[(member.CardId, TAG_PREMIUM.GOLDEN)] = 1;
                    manager.Counts[(member.CardId, TAG_PREMIUM.NORMAL)] = 2;
                }
                CollectionQualitySettings.Drag = DragQualityMode.ChooseOnDrop;
                manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Instance = new CollectionDeckTray { Context = manager.EditedDeck };
                Input.LeftReleased = false; drag.Invoke(folder, null);
                CollectionInputMgr.Get().OverDeck = true; Input.LeftReleased = true;
                ComboCollectionCards.TryHandleDragRelease(CollectionInputMgr.Get());
                Time.frameCount += 2; ComboCollectionCards.OnUpdate();
                Check(ButtonListMenu.Visible is QualityChoiceMenu && ButtonListMenu.Visible.Header == package.Name + " · Choose quality"
                    && manager.EditedDeck.Slots.Count == 0,
                    "A package drop shows one package-wide quality popup before adding any contained card");
                ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_ChooseNORMAL").Click();
                Check(manager.EditedDeck.LocalCount(a.CardId) == 2 && manager.EditedDeck.LocalCount(b.CardId) == 1
                    && manager.EditedDeck.LocalCount(artwork) == 0 && ButtonListMenu.Visible == null && !UniversalInputManager.Get().IsDialogActive(),
                    "One confirmation adds every package member without another popup or its token artwork");
            }
            finally
            {
                QualityChoiceMenu.Cancel(); CollectionQualitySettings.Drag = savedDrag;
                manager.Cards.Clear(); foreach (var pair in savedCards) manager.Cards.Add(pair.Key, pair.Value);
                manager.Counts.Clear(); foreach (var pair in savedCounts) manager.Counts.Add(pair.Key, pair.Value);
            }
            ComboPopup.AddPackage(); var editor = ButtonListMenu.Visible;
            Check(editor.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "BCM_PackageRecipe"),
                "The package editor previews the native deck-recipe folder instead of a normal card");
            Check(!editor.Buttons.Any(button => button.gameObject.name == "BCM_ComboDescription"),
                "The artwork picker shows the selected name without a search text field");
            Check(((PackageArtwork.Catalog)typeof(ComboPopup).GetField("m_artCatalog", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor)).Search(unrelated).Count == 0,
                "The package artwork choices exclude unrelated token families");
            var artChoices = (List<PackageArtwork.Choice>)typeof(ComboPopup).GetField("m_artChoices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
            for (int attempt = 0; attempt < 10 && !artChoices.Any(choice => choice.Id.StartsWith("TEST_WILDSEED_")); attempt++)
            {
                Time.unscaledTime += .3f;
                typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                artChoices = (List<PackageArtwork.Choice>)typeof(ComboPopup).GetField("m_artChoices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
            }
            var previewUpdate = typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            var packagePreview = (PackageNativeTile)typeof(ComboPopup).GetField("m_packagePreview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
            Check(packagePreview.Root.GetComponentsInChildren<Renderer>(true).Single(renderer => renderer.gameObject.name == "BCM_PackageClassRibbon")
                .sharedMaterial.color.Equals(CollectionPageManager.ColorForClass(ComboCollectionCards.ClassForPackage(package))),
                "The package editor preview uses the same class banner color as the collection tile");
            Actor artworkActor = editor.GetComponentsInChildren<Actor>(false).Single();
            artworkActor.gameObject.SetActive(false); previewUpdate.Invoke(editor, null);
            Check(packagePreview.Root.activeInHierarchy && packagePreview.Root.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.enabled),
                "The recipe preview stays visible when native actor animations deactivate their carrier");
            artworkActor.gameObject.SetActive(true);

            for (int i = 0; i < 3; i++) { Time.frameCount++; previewUpdate.Invoke(editor, null); }
            var artworkDropdown = editor.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboArtwork");
            Check(!editor.Buttons.Any(button => button.gameObject.name == "BCM_NextComboCards" || button.gameObject.name == "BCM_PreviousComboCards")
                && artChoices.All(choice => artworkDropdown.Items.Contains(choice.Id)),
                "Package artwork uses a native dropdown instead of paging buttons");
            var title = packagePreview.Root.GetComponentsInChildren<UberText>(false).First(label => label.gameObject.name == "BCM_PackageTitle");
            Check(!title.isHidden() && title.TextColor.a == 1f && title.TextColor.r == 1f && title.TextColor.g == 1f && title.TextColor.b == 1f && title.Text.Length > 0,
                "The package preview renders its title with opaque white fill");
            Check(packagePreview.Root.GetComponentsInChildren<UberText>(false).Count(label => label.gameObject.name == "BCM_PackageCount" && !label.isHidden()) == 1,
                "Complete and incomplete native recipe variants produce one visible card count");
            int layoutWidth = Screen.width, layoutHeight = Screen.height;
            bool layoutPressed = Input.LeftPressed, layoutHeld = Input.LeftHeld, layoutReleased = Input.LeftReleased;
            try
            {
                Input.LeftPressed = Input.LeftHeld = Input.LeftReleased = false;
                foreach (var size in new[] { (320, 240), (480, 900), (1920, 1080) })
                {
                    Screen.width = size.Item1; Screen.height = size.Item2;
                    typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                    previewUpdate.Invoke(editor, null);
                    var skin = (ComboNativeSkin)typeof(ComboPopup).GetField("m_skin", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                    float footerTop = Math.Max(skin.SaveArea.yMax, skin.CancelArea.yMax);
                    packagePreview = (PackageNativeTile)typeof(ComboPopup).GetField("m_packagePreview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                    var visibleBadge = packagePreview.Root.GetComponentsInChildren<Renderer>(false).FirstOrDefault(renderer => renderer.gameObject.name.StartsWith("BCM_PackageBadgeComplete"));
                    Check(visibleBadge != null, "Package preview has a visible complete badge: " + string.Join(",", packagePreview.Root.GetComponentsInChildren<Renderer>(true).Select(renderer => renderer.gameObject.name + ":" + renderer.gameObject.activeInHierarchy)));
                    Rect badge = ComboSceneGeometry.ProjectBounds(visibleBadge.bounds, CameraUtils.Camera);
                    Check(badge.yMin > footerTop, "The complete recipe badge stays above Save and Cancel at every viewport size");
                    var nameLabel = editor.GetComponentsInChildren<UberText>(false).Single(label => label.gameObject.name == "BCM_ComboNameLabel");
                    CounterTextLayout.TryScreenInkRect(nameLabel, CameraUtils.Camera, out Rect nameInk);
                    CounterTextLayout.TryScreenInkRect(skin.HeaderText, CameraUtils.Camera, out Rect headerInk);
                    Check(nameInk.yMax < headerInk.yMin, "The raised package fields retain a clear gap below the title");
                }
            }
            finally
            {
                Screen.width = layoutWidth; Screen.height = layoutHeight;
                typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null); previewUpdate.Invoke(editor, null);
                Input.LeftPressed = layoutPressed; Input.LeftHeld = layoutHeld; Input.LeftReleased = layoutReleased;
            }
            int selectionWrites = artworkDropdown.SelectionWrites;
            for (int i = 0; i < 5; i++) { Time.unscaledTime += .3f; previewUpdate.Invoke(editor, null); }
            Check(artworkDropdown.SelectionWrites == selectionWrites,
                "Periodic artwork discovery leaves an unchanged native selection alone instead of restarting its visuals");
            artworkDropdown.SelectOption(artChoices.First(choice => choice.Id.StartsWith("TEST_WILDSEED_")).Id);
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
            Check(editor.GetComponentsInChildren<Actor>(false).Any(actor => actor.GetEntityDef()?.GetCardId().StartsWith("TEST_WILDSEED_") == true),
                "The package editor previews selectable token art using real token definitions");
            ComboPopup.Close(); ComboPopup.Add(); editor = ButtonListMenu.Visible;
            Check(editor.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").m_ButtonText.Text == "Save combo",
                "Reusing the popup for an ordinary combo restores its controls and captions");
            Check(editor.GetComponentsInChildren<DropdownControl>(false).Any(control => control.gameObject.name == "BCM_ComboFrame")
                && editor.GetComponentsInChildren<CheckBox>(false).Any(control => control.gameObject.name == "BCM_ComboDragon"),
                "Switching from a package to a combo restores card frame and ornament controls");

            ComboPopup.Close();
            CategoryCatalog.DeleteCombo(package.Id); CategoryCatalog.LoadUserFile(path);
            Check(!CategoryCatalog.Combos.Any(combo => combo.Id == package.Id), "Packages can be deleted without stale edits resurrecting them");
        }
        finally
        {
            ComboPopup.Close(); ComboCollectionCards.BackFromPackage();
            foreach (var visual in visuals) { ComboCollectionCards.ReleaseHiddenActors(visual); UnityEngine.Object.Destroy(visual.gameObject); }
            source.Destroy(); UnityEngine.Object.Destroy(recipe); UnityEngine.Object.Destroy(pickerAsset);
            manager.Display.m_deckTemplatePickerPrefab = previousAsset; AssetLoader.Get().Assets.Remove(nativeAsset);
            manager.EditedDeck = previousDeck; CollectionDeckTray.Instance = previousTray; CollectionInputMgr.Instance = previousInput;
            Resources.Objects = previousResources; NativeComboRuntime.Active = previousMode;
            CameraUtils.Projection = previousProjection; AssetLoader.Get().MeshFactory = previousMeshFactory;
            CollectionQualitySettings.Click = previousClick; Input.LeftReleased = Input.RightReleased = Input.RightPressed = Input.LeftPressed = Input.LeftHeld = false; ExtraFilterState.ClearAll();
        }
    }
}
