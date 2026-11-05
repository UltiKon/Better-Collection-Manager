using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private sealed class QualityFixture
    {
        public CollectionManager Manager;
        public CollectionDeck Deck;
        public CollectionDeckTray Tray;
        public CollectionCardVisual Visual;
        public Actor Actor;
    }

    private static readonly DeckRule.RuleType[] AddIgnores = { DeckRule.RuleType.DEATHKNIGHT_RUNE_LIMIT };

    private static QualityFixture QualityCase(int signature, int golden, int normal,
        QualityDisplayMode mode = QualityDisplayMode.HighestOnly, TAG_RARITY rarity = TAG_RARITY.COMMON,
        bool hideRedundant = false)
    {
        CollectionQualitySettings.HideRedundant = hideRedundant;
        CollectionQualitySettings.Display = mode;
        QualityDeckInput.ResetSession();
        MixedPremiumCard.ResetSession();
        PreferredQualityMode.IsEnabled = PreferredPremiumPrefs.CollapseEnabled = true;
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        CameraUtils.Camera.UpDirection = 1;
        AssetLoader.Get().CreateRulesText = false;
        AssetLoader.Get().MeshFactory = CardMesh;
        var fixture = new QualityFixture { Manager = new CollectionManager(), Deck = new CollectionDeck(), Tray = new CollectionDeckTray() };
        CollectionManager.Instance = fixture.Manager;
        fixture.Manager.Editing = true;
        fixture.Manager.EditedDeck = fixture.Deck;
        fixture.Manager.Display.ViewMode = CollectionUtils.ViewMode.CARDS;
        fixture.Tray.Context = fixture.Deck;
        CollectionDeckTray.Instance = fixture.Tray;
        CollectionInputMgr.Instance = new CollectionInputMgr();
        Input.LeftReleased = false;
        UniversalInputManager.Instance = new UniversalInputManager();
        CraftingTray.Instance = null;
        var variants = new List<CollectibleCard>();
        foreach (var value in new[] { (TAG_PREMIUM.SIGNATURE, signature), (TAG_PREMIUM.GOLDEN, golden), (TAG_PREMIUM.NORMAL, normal) })
        {
            if (value.Item2 < 0) continue; // A nonexistent premium is different from an unowned premium.
            CollectibleCard card = Card("quality", value.Item1, value.Item2, rarity);
            variants.Add(card);
            fixture.Manager.Cards[("quality", value.Item1)] = card;
            fixture.Manager.Counts[("quality", value.Item1)] = value.Item2;
        }
        PreferredQualityCollapse.BeginUpdate();
        PreferredQualityCollapse.CollapseInPlace(variants, "quality fixture", null);
        var go = new GameObject("QualityTile");
        fixture.Visual = go.AddComponent<CollectionCardVisual>();
        fixture.Actor = Child(go.transform, "Actor").AddComponent<Actor>();
        fixture.Actor.Premium = variants.Last().PremiumType;
        fixture.Actor.Def = variants.Last().GetEntityDef();
        fixture.Actor.gameObject.AddComponent<MeshFilter>().sharedMesh = CardMesh();
        fixture.Actor.gameObject.AddComponent<MeshRenderer>();
        fixture.Visual.Actors = new CollectionCardActors(fixture.Actor);
        return fixture;
    }

    private static bool AddGesture(QualityFixture fixture, bool drag, out TAG_PREMIUM selected)
    {
        if (drag) QualityDeckInput.CaptureDrag(fixture.Visual, true);
        QualityAddRequest previous = drag ? QualityDeckInput.BeginDrop(false) : QualityDeckInput.BeginClick(fixture.Visual);
        Actor actor = fixture.Visual.GetActor();
        selected = actor.GetPremium();
        bool result = false;
        try
        {
            if (QualityDeckInput.RouteAdd(actor.GetEntityDef(), ref selected, drag, ref actor, AddIgnores, ref result))
                result = fixture.Tray.AddCard(fixture.Actor.Def, selected, drag, actor, AddIgnores);
        }
        finally
        {
            if (drag) QualityDeckInput.EndDrop(previous, false);
            else QualityDeckInput.EndClick(previous);
        }
        return result;
    }

    private static void CheckQualitySettings()
    {
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        CollectionQualitySettings.ShowFilterClearAll = true;
        CollectionQualitySettings.ShowLogicControls = CollectionQualitySettings.ShowEditControls = true;
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionQualitySettings.RememberFilterModes = false;
        FilterModes.StartSession();
        CollectionQualitySettings.Changed += QualityDeckInput.SettingsChanged;
        QualityDeckInput.Initialize();
        CheckDisplayModes();
        CheckStacking();
        CheckStackingClickPolicy();
        CheckStackingNativeInteraction();
        CheckStackingTextLayers();
        CheckStackingManaDepth();
        CheckAddPriorities();
        CheckLockedClickPolicy();
        CheckSeparateQualityInput();
        CheckQualityLocks();
        CheckDropChooser();
        CheckNativeDropLifecycle();
        CheckDragHooks();
        CheckLiveSettingsRefresh();
        CheckModsSettingsMenu();
        CheckSettingsPersistence();
        CheckFilterModePersistence();
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
        CollectionQualitySettings.Changed -= QualityDeckInput.SettingsChanged;
        QualityDeckInput.ResetSession();
        MixedPremiumCard.ResetSession();
        CollectionManager.Instance = null;
        CollectionDeckTray.Instance = null;
    }

    private static void CheckDisplayModes()
    {
        foreach (QualityDisplayMode mode in Enum.GetValues(typeof(QualityDisplayMode)))
        {
            QualityFixture fixture = QualityCase(1, 1, 2, mode);
            var input = fixture.Manager.Cards.Values.ToList();
            PreferredQualityCollapse.CollapseInPlace(input, "display", null);
            TAG_PREMIUM[] expected = mode == QualityDisplayMode.Stacking ? new[] { TAG_PREMIUM.SIGNATURE }
                : mode == QualityDisplayMode.Split || mode == QualityDisplayMode.TwoHighest
                ? new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE }
                : new[] { mode == QualityDisplayMode.PreferTwoCopies ? TAG_PREMIUM.NORMAL : TAG_PREMIUM.SIGNATURE };
            Check(input.Select(c => c.PremiumType).SequenceEqual(expected), "Signature frame cases must follow the selected display mode");
            Check(PreferredQualityCollapse.TryGetMixed("quality", out _) == (mode == QualityDisplayMode.Stacking),
                "Special frames may stack as complete actors but must never be cut together");
            Check(PreferredQualityCollapse.TryGetVariants("quality", out var allowed) && allowed.Count == 3,
                "Hidden eligible qualities must remain registered for deck selection");
            Check(fixture.Manager.Counts[("quality", TAG_PREMIUM.SIGNATURE)] == 1, "Display changes must not modify inventory");

            fixture = QualityCase(-1, 1, 1, mode);
            Check(PreferredQualityCollapse.TryGetMixed("quality", out _) == QualityOptions.HasMergedTiles,
                "Split and Stacking must merge compatible pairs");
            Check(fixture.Visual.GetActor().GetPremium() == TAG_PREMIUM.GOLDEN, "Pair-mode fallback must keep the highest owned quality");
        }
        var legend = QualityCase(1, 2, 2, QualityDisplayMode.PreferTwoCopies, TAG_RARITY.LEGENDARY);
        Check(legend.Actor.Premium == TAG_PREMIUM.SIGNATURE, "Legendary cards must not downgrade to obtain an unnecessary second copy");
        var single = QualityCase(-1, 2, 4, QualityDisplayMode.Split);
        Check(!PreferredQualityCollapse.TryGetMixed("quality", out _), "Two preferred copies must retain a complete preferred card");
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
        CollectionQualitySettings.Display = QualityDisplayMode.PreferTwoCopies;
        var list = single.Manager.Cards.Values.ToList();
        PreferredQualityCollapse.CollapseInPlace(list, "custom order", null);
        Check(list.Count == 1 && list[0].PremiumType == TAG_PREMIUM.NORMAL, "Pair selection must honor the saved quality order");
        PreferredQualityCollapse.BeginUpdate();
        Check(!PreferredQualityCollapse.TryGetVariants("quality", out _), "A new result set must clear old quality candidates");
    }

    private static void CheckStacking()
    {
        foreach (bool special in new[] { false, true })
        foreach (float rightDirection in new[] { -1f, 1f })
        {
            var fixture = QualityCase(special ? 1 : -1, 1, 2, QualityDisplayMode.Stacking);
            CameraUtils.Camera.RightDirection = rightDirection;
            AssetLoader.Get().CreateRulesText = true;
            AssetLoader.AttachRulesText(fixture.Actor);
            Mesh original = fixture.Actor.GetComponent<MeshFilter>().sharedMesh;
            Vector3 position = fixture.Actor.transform.localPosition;
            fixture.Visual.m_newCardCallout = Child(fixture.Visual.transform, "StackCallout");
            Vector3 calloutPosition = new Vector3(0.3f, 0.7f, -0.4f);
            fixture.Visual.m_newCardCallout.transform.localPosition = calloutPosition;
            MixedPremiumCard.Attach(fixture.Visual);
            Actor under = AssetLoader.Get().LastActor;
            GameObject manaShadow = Child(under.transform, "CostGem_DropShadow");
            typeof(MixedPremiumCard).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fixture.Visual.GetComponent<MixedPremiumCard>(), null);
            Check(fixture.Visual.GetComponent<MixedPremiumCard>().enabled && under != fixture.Actor,
                "Stacking must attach a real second actor, including special frames");
            Check(ReferenceEquals(fixture.Actor.GetComponent<MeshFilter>().sharedMesh, original),
                "Stacking must preserve full native meshes");
            Check(CameraUtils.Camera.WorldToScreenPoint(under.transform.position).x
                    < CameraUtils.Camera.WorldToScreenPoint(fixture.Actor.transform.position).x
                && under.transform.localPosition.y < fixture.Actor.transform.localPosition.y,
                "The lower quality must appear on the screen's left even when the collection's local X axis is mirrored");
            Check(under.transform.localPosition.y > position.y, "Both stacked frames must remain above the collection paper");
            Check(!under.RulesText.gameObject.activeSelf && !under.GetComponent<Collider>().enabled,
                "The lower card's duplicate labels and input colliders must be hidden");
            Check(!under.m_manaObject.activeSelf && fixture.Actor.m_manaObject.activeSelf,
                "Hide the rear mana gem together with its digits while retaining the front card's complete mana gem");
            Check(!manaShadow.activeSelf, "A separate rear mana-gem shadow must also be hidden");
            Check(Near(Math.Abs(fixture.Actor.transform.localPosition.x - under.transform.localPosition.x),
                    fixture.Visual.m_boxColliderSize.x * 0.10f),
                "The narrower stack must expose ten percent of the card width");
            Input.mousePosition = new Vector3(-0.79f, 0, 0);
            MixedPremiumCard.Choose(fixture.Visual, false, false);
            Check(fixture.Visual.GetActor() == fixture.Actor, "The narrower seam must select the front card at its new edge");
            Input.mousePosition = new Vector3(-0.81f, 0, 0);
            MixedPremiumCard.Choose(fixture.Visual, true, false);
            Check(fixture.Visual.GetActor() == under, "The exposed left frame must select the lower quality");
            CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
            Check(AddGesture(fixture, false, out var lower) && lower == under.GetPremium(),
                "Stacking's click policy must add the selected lower quality to the deck");
            Input.mousePosition = new Vector3(0.5f, 0, 0);
            MixedPremiumCard.Choose(fixture.Visual, false, true);
            Check(fixture.Visual.GetActor() == under, "Dragging must retain the quality selected on press");
            MixedPremiumCard.Choose(fixture.Visual, false, false);
            Check(fixture.Visual.GetActor() == fixture.Actor, "The full front card must select the preferred quality");
            CollectionQualitySettings.Drag = DragQualityMode.DraggedHalf;
            Check(AddGesture(fixture, true, out var upper) && upper == fixture.Actor.GetPremium(),
                "Stacking's independent drag policy must add the selected preferred quality");
            Vector3 stacked = fixture.Actor.transform.localPosition;
            under.m_manaObject.SetActive(true);
            manaShadow.SetActive(true);
            for (int i = 0; i < 5; i++)
                typeof(MixedPremiumCard).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(fixture.Visual.GetComponent<MixedPremiumCard>(), null);
            Check(fixture.Actor.transform.localPosition.Equals(stacked), "Stack offsets must not accumulate over frames");
            Check(!under.m_manaObject.activeSelf && !manaShadow.activeSelf,
                "Native actor refreshes must not leave an empty rear mana gem or shadow visible");
            MixedPremiumCard.Release(fixture.Visual);
            Check(fixture.Actor.transform.localPosition.Equals(position) && under.Destroyed,
                "Releasing a stack must restore the original position and destroy the extra actor");
            Check(fixture.Visual.m_newCardCallout.transform.localPosition.Equals(calloutPosition),
                "Releasing a stack must restore its native chrome position");
            Check(under.m_manaObject.activeSelf && manaShadow.activeSelf,
                "Releasing a stack must restore the original visibility of hidden mana objects");
        }
        CameraUtils.Camera.RightDirection = 1f;
        var full = QualityCase(-1, 2, 4, QualityDisplayMode.Stacking);
        Check(!PreferredQualityCollapse.TryGetMixed("quality", out _), "Two preferred copies must not stack");
        var legendary = QualityCase(1, 1, 2, QualityDisplayMode.Stacking, TAG_RARITY.LEGENDARY);
        Check(!PreferredQualityCollapse.TryGetMixed("quality", out _), "Legendary cards need no second-copy stack");
        MixedPremiumCard.Ready = false;
        var fallback = new List<CollectibleCard> { Card("fallback-stack", TAG_PREMIUM.GOLDEN, 1),
            Card("fallback-stack", TAG_PREMIUM.NORMAL, 1) };
        CollectionManager.Instance = null;
        PreferredQualityCollapse.CollapseInPlace(fallback, "stack fallback", null);
        Check(fallback.Count == 2, "An unavailable stacking renderer must retain two accessible quality tiles");
        MixedPremiumCard.Ready = true;
        AssetLoader.Get().CreateRulesText = false;
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
    }

    private static void CheckStackingClickPolicy()
    {
        CollectionQualitySettings.HideRedundant = false;
        CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
        var fixture = QualityCase(-1, 1, 2, QualityDisplayMode.Stacking, hideRedundant: true);
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
        MixedPremiumCard.Attach(fixture.Visual);
        Input.mousePosition = new Vector3(-0.9f, 0, 0);
        MixedPremiumCard.Choose(fixture.Visual, false, false);
        Check(fixture.Visual.GetActor().GetPremium() == TAG_PREMIUM.NORMAL,
            "The stack's exposed frame must remain selectable for inspection and explicit drag policies");
        Check(QualityOptions.ClickLocked && !QualityOptions.ShowClickSettings
            && QualityOptions.EffectiveClick == ClickQualityMode.PreferredOrder,
            "Stacking must hide redundant click choices and default to quality order");
        CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable;
        Check(CollectionQualitySettings.Click == ClickQualityMode.ClickedHalf,
            "Stacking's automatic click policy must preserve the saved manual preference");
        Check(AddGesture(fixture, false, out var first) && first == TAG_PREMIUM.GOLDEN,
            "Clicking the lower stack frame must add the highest available quality by default");
        Check(AddGesture(fixture, true, out var second) && second == TAG_PREMIUM.NORMAL,
            "Same as click must inherit quality order and fall back when the preferred copy is exhausted");
        fixture.Deck.Slots.Clear();
        CollectionQualitySettings.Drag = DragQualityMode.DraggedHalf;
        Check(AddGesture(fixture, true, out var dragged) && dragged == TAG_PREMIUM.NORMAL,
            "An explicit Dragged half policy must remain independent of Stacking's automatic click policy");
        fixture.Deck.Slots.Clear();
        CollectionQualitySettings.HideRedundant = false;
        CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable;
        Check(!QualityOptions.ClickLocked && QualityOptions.ShowClickSettings
            && AddGesture(fixture, false, out var manual) && manual == TAG_PREMIUM.NORMAL,
            "Showing redundant settings must allow an explicit manual click override in Stacking");
        CollectionQualitySettings.HideRedundant = true;
        Check(QualityOptions.EffectiveClick == ClickQualityMode.PreferredOrder
            && CollectionQualitySettings.Click == ClickQualityMode.HighestFullyAvailable,
            "Hiding redundant settings again must restore quality order without discarding the manual override");
        MixedPremiumCard.Release(fixture.Visual);
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        Check(QualityOptions.ShowClickSettings && QualityOptions.EffectiveClick == ClickQualityMode.HighestFullyAvailable,
            "Returning to Split must restore its visible manual click preference");
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
    }

    private static void CheckStackingNativeInteraction()
    {
        var fixture = QualityCase(-1, 1, 2, QualityDisplayMode.Stacking, hideRedundant: true);
        Vector3 originalScale = fixture.Actor.transform.localScale;
        var hitArea = fixture.Visual.gameObject.AddComponent<BoxCollider>();
        hitArea.size = fixture.Visual.m_boxColliderSize;
        var nativeCollider = fixture.Actor.gameObject.AddComponent<BoxCollider>();
        var disabledCollider = Child(fixture.Actor.transform, "OriginallyDisabledCollider").AddComponent<BoxCollider>();
        disabledCollider.enabled = false;
        MixedPremiumCard.Attach(fixture.Visual);
        Actor rear = AssetLoader.Get().LastActor;
        var rearCollider = Child(rear.transform, "RebuiltMeshCollider").AddComponent<BoxCollider>();
        GameObject rearRune = Child(rear.transform, "RuneHolder");
        Renderer rearRuneGlow = Child(rearRune.transform, "Hover_Highlight").AddComponent<MeshRenderer>();
        GameObject frontRune = Child(fixture.Actor.transform, "RuneHolder");
        Renderer frontRuneGlow = Child(frontRune.transform, "Hover_Highlight").AddComponent<MeshRenderer>();
        Renderer warm = Child(fixture.Actor.transform, "UnlabelledWarmEffect").AddComponent<MeshRenderer>();
        warm.sharedMaterials = new[] { new Material { name = "Other" }, new Material { name = "Glow_Warm (Instance)" } };
        Renderer white = Child(fixture.Actor.transform, "Hover_Highlight").AddComponent<MeshRenderer>();
        white.sharedMaterial = new Material { name = "NormalWhiteHover" };
        white.enabled = false;
        fixture.Actor.StateChanged = state => {
            nativeCollider.enabled = true;
            frontRuneGlow.gameObject.SetActive(true); frontRuneGlow.enabled = true;
            warm.gameObject.SetActive(true); warm.enabled = true;
            white.enabled = state == ActorStateType.CARD_MOUSE_OVER;
        };
        rear.StateChanged = state => {
            rearCollider.enabled = true;
            rearRune.SetActive(true); rearRuneGlow.enabled = true;
        };
        void UpdateStack() => typeof(MixedPremiumCard).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(fixture.Visual.GetComponent<MixedPremiumCard>(), null);
        // Simulate PegUI's nearest hit: the exact collider object must own
        // CollectionCardVisual. An elevated actor collider would swallow it.
        CollectionCardVisual Hit() => fixture.Visual.GetComponentsInChildren<Collider>(false)
            .Where(c => c.enabled).OrderByDescending(c => c.transform.position.y)
            .FirstOrDefault()?.gameObject.GetComponent<CollectionCardVisual>();
        Input.mousePosition = Vector3.zero;
        UpdateStack();
        Check(Hit() == fixture.Visual && hitArea.enabled && !nativeCollider.enabled && !rearCollider.enabled,
            "Stacked actor colliders must not swallow the collection visual's native mouse events");
        Check(rear.transform.localScale.x == -fixture.Actor.transform.localScale.x
            && rear.transform.localScale.z == fixture.Actor.transform.localScale.z,
            "Only rear artwork must mirror horizontally about the vertical card axis");
        Check(!rearRune.activeSelf && !rearRuneGlow.enabled && !frontRuneGlow.gameObject.activeSelf
            && !frontRuneGlow.enabled && !warm.enabled,
            "Rear runes and both actors' warm/rune hover effects must be suppressed");
        Hit().GetActor().SetActorState(ActorStateType.CARD_MOUSE_OVER);
        UpdateStack();
        Check(white.gameObject.activeSelf && white.enabled && fixture.Actor.State == ActorStateType.CARD_MOUSE_OVER,
            "Native hover must reach the card and retain its ordinary white glow");
        Check(Hit() == fixture.Visual && !nativeCollider.enabled && !rearCollider.enabled
            && !rearRune.activeSelf && !frontRuneGlow.enabled && !warm.enabled,
            "Hover-triggered rebuilds must not reintroduce collider blockers or rune effects");
        Check(AddGesture(fixture, false, out var selected) && selected == TAG_PREMIUM.GOLDEN,
            "A native hit on the stack must still add by the default quality order");
        Hit().NativeLeftRelease();
        Check(fixture.Visual.NativeLeftCalls == 1 && fixture.Visual.NativeDeckActions == 1,
            "The collection visual must receive its native click action");
        Input.mousePosition = new Vector3(5, 5, 0);
        fixture.Visual.GetActor().SetActorState(ActorStateType.CARD_IDLE);
        UpdateStack();
        Check(!white.enabled && fixture.Actor.State == ActorStateType.CARD_IDLE && rear.State == ActorStateType.CARD_IDLE,
            "Leaving the stack must clear its normal hover state");
        for (int i = 0; i < 3; i++) UpdateStack();
        Check(rear.transform.localScale.x == -originalScale.x,
            "Repeated updates must not alternate the rear mirror direction");
        MixedPremiumCard.Release(fixture.Visual);
        Check(nativeCollider.enabled && !disabledCollider.enabled && hitArea.enabled
            && fixture.Actor.transform.localScale.Equals(originalScale),
            "Releasing a stack must restore native collider states and the original actor scale");
        Check(rearRune.activeSelf && rearRuneGlow.enabled && frontRuneGlow.enabled && warm.enabled,
            "Releasing a stack must restore the original rune/effect visibility");
        fixture.Actor.StateChanged = rear.StateChanged = null;
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
    }

    private static void CheckStackingTextLayers()
    {
        var fixture = QualityCase(-1, 1, 2, QualityDisplayMode.Stacking);
        fixture.Manager.Display.BookOpened = false;
        AssetLoader.Get().CreateRulesText = true;
        AssetLoader.AttachRulesText(fixture.Actor);
        Renderer frame = fixture.Actor.GetMeshRenderer();
        frame.sortingLayerID = 3; frame.sortingOrder = 4;
        fixture.Actor.gameObject.layer = 7;
        UberText cost = fixture.Actor.CostText;
        cost.Text = "5";
        cost.RenderQueue = 75;
        cost.UpdateNow(true);
        Renderer[] outputs = CounterTextLayout.VisibleRenderers(cost);
        foreach (Renderer output in outputs)
        {
            output.sortingLayerID = 9; output.sortingOrder = 75; output.gameObject.layer = 12;
        }
        int created = AssetLoader.Get().InstantiateCalls;
        void Tick() => typeof(MixedPremiumCard).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(fixture.Visual.GetComponent<MixedPremiumCard>(), null);
        MixedPremiumCard.Attach(fixture.Visual);
        Check(AssetLoader.Get().InstantiateCalls == created + 1
            && MixedPremiumCard.TryGetSelected(fixture.Visual.Actors, out _),
            "Stacking must be built immediately while the collection book is opening");
        Check(!cost.isHidden() && cost.gameObject.activeInHierarchy && cost.RenderQueue == 4
            && outputs.All(r => r.enabled && r.sortingLayerID == 3 && r.sortingOrder == 4 && r.gameObject.layer == 7),
            "The visible mana digit must use the card's drawing layer without delaying or hiding the stack");
        Actor rear = AssetLoader.Get().LastActor;
        var external = new GameObject("ExternalRearManaSurface");
        Renderer externalOutput = external.AddComponent<MeshRenderer>();
        rear.CostText.RenderToTexture = true;
        rear.CostText.RenderOnObject = external;
        rear.CostText.Show();
        Check(externalOutput.enabled, "The fixture must reproduce a rear text surface outside the actor subtree");
        Tick();
        Check(rear.CostText.isHidden() && !externalOutput.enabled,
            "Native text hiding must suppress external rear mana surfaces, even after native Show rebuilds them");
        fixture.Manager.Display.BookOpened = true;
        for (int i = 0; i < 3; i++) Tick();
        Check(AssetLoader.Get().InstantiateCalls == created + 1,
            "Completing the book animation must not rebuild the stack");
        MixedPremiumCard.Release(fixture.Visual);
        Check(cost.RenderQueue == 75 && outputs.All(r => r.sortingLayerID == 9 && r.sortingOrder == 75 && r.gameObject.layer == 12),
            "Releasing the stack must restore the native mana drawing layer");
        Check(!rear.CostText.isHidden(), "Releasing the stack must restore the native text visibility state");
        UnityEngine.Object.Destroy(external);
        AssetLoader.Get().CreateRulesText = false;
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
    }

    private static void CheckStackingManaDepth()
    {
        float oldDepth = CameraUtils.Camera.DepthScale;
        CameraUtils.Camera.DepthScale = 1f;
        foreach (bool texture in new[] { false, true })
        {
            var fixture = QualityCase(-1, 1, 2, QualityDisplayMode.Stacking);
            fixture.Manager.Display.BookOpened = false;
            AssetLoader.AttachRulesText(fixture.Actor);
            UberText cost = fixture.Actor.CostText;
            cost.transform.localPosition = new Vector3(-0.5f, 0.8f, 0.7f);
            Vector3 original = cost.transform.localPosition;
            Renderer gem = fixture.Actor.m_manaObject.AddComponent<MeshRenderer>();
            gem.UseLocalBounds = true;
            gem.LocalBounds = new Bounds(new Vector3(-0.5f, 0.02f, 0.7f), new Vector3(0.3f, 0.02f, 0.3f));
            GameObject external = null;
            if (texture)
            {
                external = new GameObject("FrontManaTextureSurface");
                external.transform.position = cost.transform.position;
                Renderer surface = external.AddComponent<MeshRenderer>();
                surface.UseLocalBounds = true;
                surface.LocalBounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.002f, 0.2f));
                cost.RenderToTexture = true;
                cost.RenderOnObject = external;
            }
            cost.UpdateNow(true);
            foreach (Renderer output in CounterTextLayout.VisibleRenderers(cost))
            {
                output.UseLocalBounds = true;
                output.LocalBounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.002f, 0.2f));
            }
            Vector3 originalOutput = external?.transform.position ?? cost.transform.position;
            MixedPremiumCard.Attach(fixture.Visual);
            float gemDepth = CameraUtils.Camera.WorldToScreenPoint(gem.bounds.max).z;
            Renderer visible = CounterTextLayout.VisibleRenderers(cost).Last();
            float textDepth = CameraUtils.Camera.WorldToScreenPoint(visible.bounds.max).z;
            Check(Near(textDepth, gemDepth - 0.001f) && textDepth > 0.4f,
                "The mana digit must sit just above its gem and behind the closed cover, including external texture surfaces");
            Vector3 corrected = cost.transform.localPosition;
            Vector3 correctedOutput = visible.transform.position;
            for (int i = 0; i < 4; i++)
                typeof(MixedPremiumCard).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(fixture.Visual.GetComponent<MixedPremiumCard>(), null);
            Check(cost.transform.localPosition.Equals(corrected) && visible.transform.position.Equals(correctedOutput),
                "Mana depth correction must not accumulate over stack updates");
            Check(Near(CameraUtils.Camera.WorldToScreenPoint(correctedOutput).x,
                    CameraUtils.Camera.WorldToScreenPoint(originalOutput).x + (texture ? 0f : 0.08f))
                && Near(correctedOutput.z, originalOutput.z), "Mana depth changes must preserve the digit's screen position");
            MixedPremiumCard.Release(fixture.Visual);
            Check(cost.transform.localPosition.Equals(original), "Releasing a stack must restore the native mana text transform");
            if (external != null)
            {
                Check(external.transform.position.Equals(originalOutput), "External mana output depth must also be restored");
                UnityEngine.Object.Destroy(external);
            }
        }
        CameraUtils.Camera.DepthScale = oldDepth;
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
    }

    private static void CheckAddPriorities()
    {
        CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
        var fixture = QualityCase(1, 2, 2);
        Check(AddGesture(fixture, false, out var first) && first == TAG_PREMIUM.SIGNATURE, "First click must use the highest available quality");
        Check(AddGesture(fixture, false, out var second) && second == TAG_PREMIUM.GOLDEN, "Second click must fall back from exhausted Signature to Golden");
        fixture.Deck.Slots[("quality", TAG_PREMIUM.SIGNATURE)] = 0;
        Check(AddGesture(fixture, false, out var restored) && restored == TAG_PREMIUM.SIGNATURE, "Removing Signature from the deck must make it available again");
        Check(!AddGesture(fixture, false, out _), "Automatic fallback must not bypass the per-card deck limit");

        fixture = QualityCase(1, 1, 1);
        var diamond = Card("quality", TAG_PREMIUM.DIAMOND, 1);
        fixture.Manager.Cards[("quality", TAG_PREMIUM.DIAMOND)] = diamond;
        fixture.Manager.Counts[("quality", TAG_PREMIUM.DIAMOND)] = 1;
        var allQualities = fixture.Manager.Cards.Values.ToList();
        PreferredQualityCollapse.CollapseInPlace(allQualities, "all qualities", null);
        fixture.Manager.Rules.MaxCopies = 4;
        foreach (TAG_PREMIUM expected in PreferredPremiumPrefs.Order)
            Check(AddGesture(fixture, false, out var actual) && actual == expected,
                "Fallback must traverse all four qualities while respecting the actual deck copy rule");

        CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable;
        fixture = QualityCase(1, 2, 5);
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.GOLDEN,
            "Highest fully available must prefer two Golden copies over one Signature and five Normal copies");
        Check(AddGesture(fixture, false, out second) && second == TAG_PREMIUM.GOLDEN,
            "The second click must keep the same complete playset after its first copy was added");
        fixture = QualityCase(1, 1, 1);
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.SIGNATURE,
            "Without a complete playset, selection must fall back to the highest available quality");
        Check(AddGesture(fixture, false, out second) && second == TAG_PREMIUM.GOLDEN,
            "Incomplete playset fallback must still respect live inventory");
        fixture = QualityCase(2, 2, 2);
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE };
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.NORMAL,
            "Complete playsets must follow the configured quality order");
        fixture = QualityCase(1, 2, 2, QualityDisplayMode.HighestOnly, TAG_RARITY.LEGENDARY);
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.SIGNATURE,
            "A Legendary playset needs only one copy and must keep the highest quality");
        fixture = QualityCase(1, 2, 3);
        var occupiedSideboard = new CollectionDeck();
        occupiedSideboard.Add("quality", TAG_PREMIUM.GOLDEN);
        occupiedSideboard.Add("quality", TAG_PREMIUM.GOLDEN);
        fixture.Deck.Sideboards.Add(occupiedSideboard);
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.NORMAL,
            "A complete quality already consumed by sideboards must fall back to another available playset");
        fixture = QualityCase(1, 1, 3);
        fixture.Manager.Trials[("quality", TAG_PREMIUM.GOLDEN)] = 1;
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.GOLDEN
            && AddGesture(fixture, false, out second) && second == TAG_PREMIUM.GOLDEN,
            "Owned and trial inventory together must supply a complete usable playset without changing owned counts");

        CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
        fixture = QualityCase(-1, 1, 1, QualityDisplayMode.Split);
        MixedPremiumCard.Attach(fixture.Visual);
        Input.mousePosition = new Vector3(0, 0.8f, 0);
        MixedPremiumCard.Choose(fixture.Visual, true, false);
        MixedPremiumCard.Choose(fixture.Visual, false, true);
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.GOLDEN, "Clicking the Golden half must add the real Golden copy");
        Check(AddGesture(fixture, false, out second) && second == TAG_PREMIUM.NORMAL, "An exhausted clicked half must fall back to the other half");
        Check(fixture.Visual.GetActor().GetPremium() == TAG_PREMIUM.GOLDEN, "Adding a fallback must not mutate the displayed/inspected actor premium");

        foreach (ClickQualityMode mode in Enum.GetValues(typeof(ClickQualityMode)))
        {
            CollectionQualitySettings.Click = mode;
            CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
            fixture = QualityCase(mode == ClickQualityMode.ClickedHalf ? -1 : 1,
                mode == ClickQualityMode.ClickedHalf ? 1 : 2, 3,
                mode == ClickQualityMode.ClickedHalf ? QualityDisplayMode.Split : QualityDisplayMode.HighestOnly);
            // This is the real selected actor captured by GrabCardVisual.
            fixture.Actor.Premium = mode == ClickQualityMode.ClickedHalf ? TAG_PREMIUM.NORMAL : TAG_PREMIUM.GOLDEN;
            TAG_PREMIUM expected = mode == ClickQualityMode.ClickedHalf ? TAG_PREMIUM.NORMAL
                : mode == ClickQualityMode.PreferredOrder ? TAG_PREMIUM.SIGNATURE : TAG_PREMIUM.GOLDEN;
            Check(AddGesture(fixture, true, out first) && first == expected,
                "Same as click must inherit all click policies, using the grabbed half for split cards");
        }

        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
        fixture = QualityCase(1, 2, 2);
        QualityDeckInput.CaptureDrag(fixture.Visual, true);
        CollectionInputMgr.Get().HoldingCard = false; // Native offscreen hiding.
        QualityDeckInput.OnUpdate();
        var resumed = QualityDeckInput.BeginDrop(false);
        TAG_PREMIUM resumedPremium = TAG_PREMIUM.SIGNATURE; Actor resumedActor = fixture.Actor; bool resumedResult = false;
        Check(QualityDeckInput.RouteAdd(fixture.Actor.Def, ref resumedPremium, true, ref resumedActor, AddIgnores, ref resumedResult)
            && resumedPremium == TAG_PREMIUM.GOLDEN, "A temporarily hidden held visual must not lose its captured drag policy");
        QualityDeckInput.EndDrop(resumed, false);

        fixture = QualityCase(1, 1, 1);
        var scoped = new List<CollectibleCard> { fixture.Manager.Cards[("quality", TAG_PREMIUM.NORMAL)] };
        PreferredQualityCollapse.BeginUpdate();
        PreferredQualityCollapse.CollapseInPlace(scoped, "filtered", null);
        fixture.Actor.Premium = TAG_PREMIUM.NORMAL;
        CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.NORMAL, "A scoped result must not select a quality filtered out of that result");
        fixture.Manager.Trials[("quality", TAG_PREMIUM.NORMAL)] = 1;
        Check(AddGesture(fixture, false, out second) && second == TAG_PREMIUM.NORMAL, "Native trial inventory must remain usable without fabricating owned copies");

        fixture = QualityCase(1, 1, 2);
        var sideboard = new CollectionDeck();
        fixture.Deck.Sideboards.Add(sideboard);
        fixture.Tray.Context = sideboard;
        fixture.Deck.Slots[("quality", TAG_PREMIUM.SIGNATURE)] = 1;
        fixture.Deck.Slots[("quality", TAG_PREMIUM.GOLDEN)] = 1;
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.NORMAL && sideboard.LocalCount("quality") == 1,
            "Selection must use root inventory counts and the active sideboard destination");

        foreach (string excluded in new[] { "browsing", "not editing", "touch", "crafting", "picker", "cosmetic", "disabled", "unregistered" })
        {
            fixture = QualityCase(1, 1, 2);
            if (excluded == "browsing") fixture.Manager.EditedDeck = null;
            if (excluded == "not editing") fixture.Manager.Editing = false;
            if (excluded == "touch") UniversalInputManager.Instance.Touch = true;
            if (excluded == "crafting") CraftingTray.Instance = new CraftingTray { Shown = true };
            if (excluded == "picker") fixture.Manager.Display.Submode = CollectionUtils.ViewSubmode.CARD_ZILLIAX_MODULES;
            if (excluded == "cosmetic") fixture.Manager.Display.ViewMode = fixture.Visual.ViewMode = CollectionUtils.ViewMode.PETS;
            if (excluded == "disabled") PreferredPremiumPrefs.CollapseEnabled = false;
            if (excluded == "unregistered") PreferredQualityCollapse.BeginUpdate();
            var previous = QualityDeckInput.BeginClick(fixture.Visual);
            TAG_PREMIUM premium = TAG_PREMIUM.SIGNATURE; Actor actor = fixture.Actor; bool result = false;
            Check(QualityDeckInput.RouteAdd(actor.Def, ref premium, false, ref actor, AddIgnores, ref result)
                && premium == TAG_PREMIUM.SIGNATURE, "Native input must remain untouched for " + excluded);
            QualityDeckInput.EndClick(previous);
        }
        fixture = QualityCase(1, 1, 2);
        TAG_PREMIUM outsidePremium = TAG_PREMIUM.NORMAL; Actor outsideActor = fixture.Actor; bool outsideResult = false;
        Check(QualityDeckInput.RouteAdd(fixture.Actor.Def, ref outsidePremium, false, ref outsideActor, AddIgnores, ref outsideResult)
            && outsidePremium == TAG_PREMIUM.NORMAL, "Imports and other AddCard callers must keep their explicit quality");
        var old = QualityDeckInput.BeginClick(fixture.Visual);
        fixture.Manager.EditedDeck = fixture.Tray.Context = new CollectionDeck();
        Check(!QualityDeckInput.RouteAdd(fixture.Actor.Def, ref outsidePremium, false, ref outsideActor, AddIgnores, ref outsideResult),
            "A gesture captured for another deck must not insert into a newly selected deck");
        QualityDeckInput.EndClick(old);
    }

    private static void CheckLockedClickPolicy()
    {
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
        var fixture = QualityCase(1, 2, 3, QualityDisplayMode.PreferTwoCopies, hideRedundant: true);
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
        Check(QualityOptions.ClickLocked && QualityOptions.EffectiveClick == ClickQualityMode.HighestFullyAvailable,
            "Prefer two owned copies must force the complete playset click policy");
        CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
        Check(CollectionQualitySettings.Click == ClickQualityMode.PreferredOrder
            && QualityOptions.EffectiveClick == ClickQualityMode.HighestFullyAvailable,
            "The forced policy must reject edits without overwriting the previous unlocked preference");
        Check(AddGesture(fixture, false, out var first) && first == TAG_PREMIUM.GOLDEN
            && AddGesture(fixture, true, out var second) && second == TAG_PREMIUM.GOLDEN,
            "Prefer two owned copies must add the displayed Golden playset by both click and inherited drag");
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        Check(!QualityOptions.ClickLocked && QualityOptions.EffectiveClick == ClickQualityMode.PreferredOrder,
            "Leaving the paired display must restore the previously chosen click policy");
        fixture = QualityCase(1, 2, 3, QualityDisplayMode.PreferTwoCopies, hideRedundant: true);
        CollectionQualitySettings.Drag = DragQualityMode.ChooseOnDrop;
        Check(!AddGesture(fixture, true, out _) && fixture.Tray.Added.Count == 0,
            "A locked click policy must still allow explicit quality selection after dropping");
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(ButtonListMenu.Visible is QualityChoiceMenu,
            "Choose after dropping must remain available with Prefer two owned copies");
        ButtonListMenu.Visible.Buttons.First(b => b.m_ButtonText.Text.StartsWith("Signature")).Click();
        Check(fixture.Tray.Added.SequenceEqual(new[] { TAG_PREMIUM.SIGNATURE }),
            "The drag chooser must honor an explicitly selected quality rather than force the automatic click policy");
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable;
        fixture = QualityCase(1, 2, 3, QualityDisplayMode.HighestOnly, hideRedundant: true);
        Check(QualityOptions.ClickLocked && QualityOptions.EffectiveClick == ClickQualityMode.PreferredOrder
            && !QualityOptions.ShowClickSettings,
            "Highest only must hide its click field and automatically use quality order");
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.SIGNATURE,
            "The highest-only automatic click policy must add the displayed highest quality");
        CollectionQualitySettings.HideRedundant = false;
        Check(!QualityOptions.ClickLocked && QualityOptions.ShowClickSettings
            && QualityOptions.EffectiveClick == ClickQualityMode.HighestFullyAvailable,
            "Disabling redundant-setting hiding must restore the editable manual click override");
        Check(AddGesture(fixture, false, out second) && second == TAG_PREMIUM.GOLDEN,
            "An unhidden manual complete-playset policy must be applied even with highest-only display");
        fixture = QualityCase(1, 2, 3, QualityDisplayMode.PreferTwoCopies);
        CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
        Check(AddGesture(fixture, false, out first) && first == TAG_PREMIUM.SIGNATURE,
            "With redundant settings shown, an explicit quality-order override must also apply in paired display");
        CollectionQualitySettings.HideRedundant = true;
        Check(QualityOptions.EffectiveClick == ClickQualityMode.HighestFullyAvailable,
            "Re-enabling hiding must restore the display mode's automatic policy");
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
    }

    private static void CheckSeparateQualityInput()
    {
        foreach (var scenario in new[] { (QualityDisplayMode.TwoHighest, 1),
            (QualityDisplayMode.TwoHighest, 2), (QualityDisplayMode.Split, 1) })
        {
            var fixture = QualityCase(scenario.Item2, 1, 2, scenario.Item1);
            CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable;
            CollectionQualitySettings.Drag = DragQualityMode.ChooseOnDrop;
            fixture.Actor.Premium = TAG_PREMIUM.GOLDEN;
            Check(AddGesture(fixture, false, out var added) && added == TAG_PREMIUM.GOLDEN,
                "Direct quality tiles must add the selected quality even with a different saved click policy");
            Check(!AddGesture(fixture, false, out added) && added == TAG_PREMIUM.GOLDEN,
                "An exhausted direct quality tile must not silently add a different visible quality");
            var type = CollectionCardVisual.LockType.NO_MORE_INSTANCES;
            string reason = "Golden exhausted";
            QualityDeckInput.AdjustLock(fixture.Visual, ref type, ref reason);
            Check(type == CollectionCardVisual.LockType.NO_MORE_INSTANCES && reason == "Golden exhausted",
                "Separate quality tiles must retain their native per-quality inventory frame and reason");
            var input = CollectionInputMgr.Get();
            input.NativeGrab(fixture.Visual);
            input.OverDeck = true;
            Input.LeftReleased = true;
            Check(!QualityDeckInput.TryHandleRelease(input) && input.DropCalls == 0,
                "Direct quality drags must leave native release cleanup in charge");
            Check(!QualityDeckInput.PrepareDrop(false, true),
                "Direct quality drags must never queue a saved quality chooser");
            fixture.Actor.Premium = TAG_PREMIUM.NORMAL;
            Check(AddGesture(fixture, true, out added) && added == TAG_PREMIUM.NORMAL,
                "Dragging a directly selected quality must add it immediately despite a saved chooser preference");
            Time.frameCount++;
            QualityDeckInput.OnUpdate();
            Check(fixture.Tray.Added.SequenceEqual(new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL })
                && ButtonListMenu.Visible == null, "Direct quality gestures must not leave a deferred insertion or chooser");
            Input.LeftReleased = false;
        }
    }

    private static void CheckQualityLocks()
    {
        CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
        var fixture = QualityCase(-1, 1, 1);
        Check(AddGesture(fixture, false, out _), "Lock fixture must add its first copy");
        var type = CollectionCardVisual.LockType.NO_MORE_INSTANCES;
        string reason = "No more copies";
        QualityDeckInput.AdjustLock(fixture.Visual, ref type, ref reason);
        Check(type == CollectionCardVisual.LockType.NONE, "The selected quality being exhausted must not lock an available fallback");
        Check(AddGesture(fixture, false, out _), "The unlocked fallback must be addable");
        type = CollectionCardVisual.LockType.NO_MORE_INSTANCES;
        QualityDeckInput.AdjustLock(fixture.Visual, ref type, ref reason);
        Check(type == CollectionCardVisual.LockType.MAX_COPIES_IN_DECK, "A native inventory lock masking the deck limit must become the proper copy-limit frame");
        fixture = QualityCase(-1, -1, 1);
        type = CollectionCardVisual.LockType.NO_MORE_INSTANCES;
        QualityDeckInput.AdjustLock(fixture.Visual, ref type, ref reason);
        Check(type == CollectionCardVisual.LockType.NO_MORE_INSTANCES, "Exactly one copy in one owned quality must retain its native inventory frame");
        fixture = QualityCase(-1, 1, 1);
        fixture.Manager.Rules.Banned = true;
        type = CollectionCardVisual.LockType.NO_MORE_INSTANCES;
        QualityDeckInput.AdjustLock(fixture.Visual, ref type, ref reason);
        Check(type == CollectionCardVisual.LockType.BANNED && !AddGesture(fixture, false, out _), "Inventory-frame removal must not bypass bans");
        fixture.Manager.Rules.Banned = false; fixture.Manager.Rules.NotPlayable = true;
        type = CollectionCardVisual.LockType.NO_MORE_INSTANCES;
        QualityDeckInput.AdjustLock(fixture.Visual, ref type, ref reason);
        Check(type == CollectionCardVisual.LockType.NOT_PLAYABLE, "Class/playability failures must remain visible");
        foreach (var native in new[] { CollectionCardVisual.LockType.BANNED, CollectionCardVisual.LockType.NOT_PLAYABLE,
            CollectionCardVisual.LockType.MAX_COPIES_IN_DECK })
        {
            type = native;
            QualityDeckInput.AdjustLock(fixture.Visual, ref type, ref reason);
            Check(type == native, "Other native lock types must remain untouched");
        }
    }

    private static QualityFixture QueueDrop()
    {
        CollectionQualitySettings.Drag = DragQualityMode.ChooseOnDrop;
        var fixture = QualityCase(1, 1, 1);
        Time.frameCount += 10;
        Check(!AddGesture(fixture, true, out _) && fixture.Tray.Added.Count == 0,
            "Choose-on-drop must defer insertion until a choice, rather than add then replace");
        QualityDeckInput.OnUpdate();
        Check(ButtonListMenu.Visible == null, "The release frame must not open a picker under the same mouse-up event");
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(ButtonListMenu.Visible != null && UniversalInputManager.Get().DialogActive, "The native modal picker must open after native drag cleanup");
        return fixture;
    }

    private static void CheckDropChooser()
    {
        var fixture = QueueDrop();
        Check(ButtonListMenu.Visible.Buttons.Count == 4, "Picker must show three usable qualities and Cancel");
        ButtonListMenu.Visible.Buttons.Single(b => b.m_ButtonText.Text.StartsWith("Normal (")).Click();
        Check(fixture.Tray.Added.SequenceEqual(new[] { TAG_PREMIUM.NORMAL }) && fixture.Tray.Suggestions == 1
            && ButtonListMenu.Visible == null && !UniversalInputManager.Get().DialogActive,
            "Choosing a quality must insert it exactly once, notify suggestions once, and close the modal");
        fixture = QueueDrop();
        UIBButton staleButton = ButtonListMenu.Visible.Buttons.Single(b => b.m_ButtonText.Text.StartsWith("Normal ("));
        staleButton.Click();
        staleButton.Click();
        Check(fixture.Tray.Added.Count == 1, "A second release on a closed choice button must not add twice");
        fixture = QueueDrop();
        ButtonListMenu.Visible.Buttons.Single(b => b.m_ButtonText.Text == "Cancel").Click();
        Check(fixture.Tray.Added.Count == 0 && !UniversalInputManager.Get().DialogActive, "Cancel must not add any card");
        fixture = QueueDrop();
        Check(QualityDeckInput.CancelChoice() && fixture.Tray.Added.Count == 0 && ButtonListMenu.Visible == null,
            "The Escape route must consume and cancel the quality picker");
        fixture = QueueDrop();
        fixture.Manager.EditedDeck = fixture.Tray.Context = new CollectionDeck();
        QualityDeckInput.OnUpdate();
        Check(ButtonListMenu.Visible == null && fixture.Tray.Added.Count == 0, "Changing decks must cancel an open selection");
        fixture = QueueDrop();
        fixture.Manager.Counts[("quality", TAG_PREMIUM.NORMAL)] = 0;
        ButtonListMenu.Visible.Buttons.Single(b => b.m_ButtonText.Text.StartsWith("Normal (")).Click();
        Check(fixture.Tray.Added.Count == 0, "Confirmation must recheck inventory instead of trusting an obsolete picker label");
        fixture = QueueDrop();
        CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable;
        Check(ButtonListMenu.Visible == null && fixture.Tray.Added.Count == 0, "A settings change must cancel an outstanding choice");
        QualityDeckInput.ResetSession();

        fixture = QualityCase(1, 1, 1);
        Check(!AddGesture(fixture, true, out _), "A multiple-quality drop must queue a chooser");
        fixture.Manager.Counts[("quality", TAG_PREMIUM.SIGNATURE)] = 0;
        fixture.Manager.Counts[("quality", TAG_PREMIUM.GOLDEN)] = 0;
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(fixture.Tray.Added.SequenceEqual(new[] { TAG_PREMIUM.NORMAL }) && ButtonListMenu.Visible == null,
            "If only one quality remains before the dialog opens, add it without a redundant picker");

        fixture = QualityCase(1, 1, 1);
        Check(!AddGesture(fixture, true, out _), "A pending drop must be cancellable before showing");
        Check(QualityDeckInput.CancelChoice(), "Escape must consume a queued selection as well as an open one");
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(ButtonListMenu.Visible == null && fixture.Tray.Added.Count == 0, "A queued canceled selection must never insert later");

        fixture = QualityCase(-1, -1, 2);
        Check(AddGesture(fixture, true, out var only) && only == TAG_PREMIUM.NORMAL && ButtonListMenu.Visible == null,
            "A single available quality must be added immediately without a redundant picker");
        fixture = QualityCase(1, 1, 1);
        QualityDeckInput.CaptureDrag(fixture.Visual, true);
        QualityDeckInput.ClearDrag(); // Native deck-tile pickup, not collection pickup.
        var previous = QualityDeckInput.BeginDrop(false);
        TAG_PREMIUM explicitPremium = TAG_PREMIUM.GOLDEN; Actor actor = fixture.Actor; bool result = false;
        Check(QualityDeckInput.RouteAdd(actor.Def, ref explicitPremium, true, ref actor, AddIgnores, ref result)
            && explicitPremium == TAG_PREMIUM.GOLDEN, "Moving deck tiles must not change quality or open a picker");
        QualityDeckInput.EndDrop(previous, false);

        fixture = QualityCase(1, 1, 1);
        QualityDeckInput.CaptureDrag(fixture.Visual, true);
        previous = QualityDeckInput.BeginDrop(true);
        result = false;
        Check(QualityDeckInput.RouteAdd(actor.Def, ref explicitPremium, true, ref actor, AddIgnores, ref result),
            "Canceled drags must not create an add context");
        QualityDeckInput.EndDrop(previous, false);
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(ButtonListMenu.Visible == null, "Canceled drags must not queue a quality picker");
    }

    private static void CheckSettingsPersistence()
    {
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        CollectionQualitySettings.HideRedundant = false;
        CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable;
        CollectionQualitySettings.Display = QualityDisplayMode.PreferTwoCopies;
        CollectionQualitySettings.Drag = DragQualityMode.ChooseOnDrop;
        CollectionQualitySettings.ColorCodeCounter = true;
        CollectionQualitySettings.NeverDustSignaturesDiamonds = false;
        CollectionQualitySettings.DisenchantPopup = false;
        CollectionQualitySettings.CollapseInCrafting = true;
        CollectionQualitySettings.OpenSearch = CollectionOpenSearchMode.NewOrRefund;
        CollectionQualitySettings.FilterMenuRows = 8;
        CollectionQualitySettings.FilterMenuColumns = 3;
        CollectionQualitySettings.ShowLogicControls = false; CollectionQualitySettings.ShowEditControls = true;
        string json = File.ReadAllText(CollectionQualitySettings.SettingsPath);
        Check(json.Contains("PreferTwoCopies") && json.Contains("HighestFullyAvailable") && json.Contains("ChooseOnDrop")
            && json.Contains("\"hideRedundant\":false") && json.Contains("\"colorCodeCounter\":true")
            && json.Contains("\"collectionOpenSearch\":\"NewOrRefund\"")
            && json.Contains("\"neverDustSignaturesDiamonds\":false") && json.Contains("\"disenchantPopup\":false")
            && json.Contains("\"collapseInCrafting\":true")
            && json.Contains("\"filterRows\":8") && json.Contains("\"filterColumns\":3")
            && json.Contains("\"showLogicControls\":false") && json.Contains("\"showEditControls\":true"), "All preferences including the checkbox must be saved in the JSON backup");
        var loaded = typeof(CollectionQualitySettings).GetField("s_loaded", BindingFlags.Static | BindingFlags.NonPublic);
        MelonLoader.MelonPreferences.Categories.Clear();
        loaded.SetValue(null, false);
        CollectionQualitySettings.EnsureLoaded();
        Check(CollectionQualitySettings.Display == QualityDisplayMode.PreferTwoCopies
            && CollectionQualitySettings.Click == ClickQualityMode.HighestFullyAvailable && CollectionQualitySettings.Drag == DragQualityMode.ChooseOnDrop
            && !CollectionQualitySettings.HideRedundant && CollectionQualitySettings.ColorCodeCounter
            && !CollectionQualitySettings.DisenchantPopup && !CollectionQualitySettings.NeverDustSignaturesDiamonds && CollectionQualitySettings.OpenSearch == CollectionOpenSearchMode.NewOrRefund
            && CollectionQualitySettings.CollapseInCrafting
            && CollectionQualitySettings.FilterMenuRows == 8 && CollectionQualitySettings.FilterMenuColumns == 3
            && !CollectionQualitySettings.ShowLogicControls && CollectionQualitySettings.ShowEditControls,
            "A fresh process with default native entries must recover all settings from JSON");
        File.WriteAllText(CollectionQualitySettings.SettingsPath, "{\"click\":\"LowestFirst\",\"drag\":\"LowestFirst\"}");
        MelonLoader.MelonPreferences.Categories.Clear();
        loaded.SetValue(null, false);
        CollectionQualitySettings.EnsureLoaded();
        Check(CollectionQualitySettings.OpenSearch == CollectionOpenSearchMode.NewOrRefund, "Missing search preferences use the shipped opening-search default");
        Check(!CollectionQualitySettings.DisenchantPopup, "Missing popup preferences use the shipped disabled default");
        Check(CollectionQualitySettings.NeverDustSignaturesDiamonds, "Older configurations enable Signature/Diamond protection by default");
        Check(!CollectionQualitySettings.CollapseInCrafting, "Older configurations keep crafting collapse opt-in");
        Check(CollectionQualitySettings.HideRedundant && !CollectionQualitySettings.ColorCodeCounter
            && CollectionQualitySettings.FilterMenuRows == 0 && CollectionQualitySettings.FilterMenuColumns == 0
            && CollectionQualitySettings.ShowLogicControls && CollectionQualitySettings.ShowEditControls,
            "Older configuration files without a checkbox preference must default to hiding redundant settings");
        Check(CollectionQualitySettings.Click == ClickQualityMode.HighestFullyAvailable
            && CollectionQualitySettings.Drag == DragQualityMode.SameAsClick,
            "Legacy click settings must migrate to complete playsets and legacy drag settings to Same as click");
        foreach (string legacy in new[] { "PreferredOrder", "HighestFullyAvailable", "1", "2" })
        {
            File.WriteAllText(CollectionQualitySettings.SettingsPath, "{\"drag\":\"" + legacy + "\"}");
            MelonLoader.MelonPreferences.Categories.Clear();
            loaded.SetValue(null, false);
            CollectionQualitySettings.EnsureLoaded();
            Check(CollectionQualitySettings.Drag == DragQualityMode.SameAsClick,
                "Previous automatic drag settings must migrate to Same as click: " + legacy);
        }
        File.WriteAllText(CollectionQualitySettings.SettingsPath,
            "{\"display\":\"HighestOnly\",\"click\":\"HighestFullyAvailable\",\"drag\":\"DraggedHalf\"}");
        MelonLoader.MelonPreferences.Categories.Clear();
        loaded.SetValue(null, false);
        CollectionQualitySettings.EnsureLoaded();
        Check(CollectionQualitySettings.Drag == DragQualityMode.DraggedHalf && QualityOptions.EffectiveDrag == DragQualityMode.SameAsClick,
            "A saved independent drag-half preference must be retained but use Same as click outside Split");
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        Check(QualityOptions.EffectiveDrag == DragQualityMode.DraggedHalf
            && QualityOptions.EffectiveClick == ClickQualityMode.HighestFullyAvailable,
            "Returning to Split must restore independent dragging without changing the click policy");
        File.WriteAllText(CollectionQualitySettings.SettingsPath, "{\"display\":\"999\",\"click\":\"unknown\",\"drag\":\"-1\"}");
        MelonLoader.MelonPreferences.Categories.Clear();
        loaded.SetValue(null, false);
        CollectionQualitySettings.EnsureLoaded();
        Check(CollectionQualitySettings.Display == QualityDisplayMode.Split && CollectionQualitySettings.Click == ClickQualityMode.PreferredOrder
            && CollectionQualitySettings.Drag == DragQualityMode.ChooseOnDrop, "Unknown enum names and numeric values must fall back to safe defaults");
        CollectionQualitySettings.Display = (QualityDisplayMode)100;
        CollectionQualitySettings.Click = (ClickQualityMode)100;
        CollectionQualitySettings.Drag = (DragQualityMode)100;
        Check(CollectionQualitySettings.Display == QualityDisplayMode.Split && CollectionQualitySettings.Click == ClickQualityMode.PreferredOrder
            && CollectionQualitySettings.Drag == DragQualityMode.ChooseOnDrop, "Invalid UI/config assignments must not persist undefined states");
        File.Delete(CollectionQualitySettings.SettingsPath);

        File.WriteAllText(CollectionQualitySettings.SettingsPath, "{\"display\":null,\"HighestOnly\":\"ignored\",\"drag\":\"HighestFullyAvailable\"}");
        MelonLoader.MelonPreferences.Categories.Clear();
        loaded.SetValue(null, false);
        CollectionQualitySettings.EnsureLoaded();
        Check(CollectionQualitySettings.Display == QualityDisplayMode.Split && CollectionQualitySettings.Drag == DragQualityMode.SameAsClick,
            "A malformed value must not borrow a later field name as its enum value");
        File.WriteAllText(CollectionQualitySettings.SettingsPath, "{\"hideRedundant\":\"invalid\",\"colorCodeCounter\":\"invalid\",\"filterRows\":-1,\"filterColumns\":999}");
        MelonLoader.MelonPreferences.Categories.Clear();
        loaded.SetValue(null, false);
        CollectionQualitySettings.EnsureLoaded();
        Check(CollectionQualitySettings.HideRedundant && !CollectionQualitySettings.ColorCodeCounter
            && CollectionQualitySettings.FilterMenuRows == 0 && CollectionQualitySettings.FilterMenuColumns == 0
            && CollectionQualitySettings.ShowLogicControls && CollectionQualitySettings.ShowEditControls,
            "Invalid settings must retain their safe defaults");
        CollectionQualitySettings.FilterMenuRows = CollectionQualitySettings.MaxFilterRows + 1;
        CollectionQualitySettings.FilterMenuColumns = -1;
        Check(CollectionQualitySettings.FilterMenuRows == 0 && CollectionQualitySettings.FilterMenuColumns == 0
            && CollectionQualitySettings.ShowLogicControls && CollectionQualitySettings.ShowEditControls,
            "Unsupported grid dimensions must not be persisted");
        CollectionQualitySettings.FilterMenuColumns = 0;
        MelonLoader.MelonPreferences.Categories.Clear();
        loaded.SetValue(null, false);
        CollectionQualitySettings.EnsureLoaded();
        Check(CollectionQualitySettings.FilterMenuRows == 0 && CollectionQualitySettings.FilterMenuColumns == 0,
            "Automatic must persist and reload independently for both dimensions");
        CollectionQualitySettings.FilterMenuColumns = 1;
        File.Delete(CollectionQualitySettings.SettingsPath);
    }

    private static void CheckNativeDropLifecycle()
    {
        foreach (ClickQualityMode clickMode in Enum.GetValues(typeof(ClickQualityMode)))
        foreach (DragQualityMode mode in Enum.GetValues(typeof(DragQualityMode)))
        {
            bool split = clickMode == ClickQualityMode.ClickedHalf || mode == DragQualityMode.DraggedHalf;
            var fixture = QualityCase(split ? -1 : 1, split ? 1 : 2, 2,
                split ? QualityDisplayMode.Split : QualityDisplayMode.HighestOnly);
            CollectionQualitySettings.Click = clickMode;
            CollectionQualitySettings.Drag = mode;
            fixture.Actor.Premium = mode == DragQualityMode.DraggedHalf ? TAG_PREMIUM.NORMAL : TAG_PREMIUM.GOLDEN;
            QualityAddRequest snapshot = QualityDeckInput.PrepareDrag(fixture.Visual);
            fixture.Visual.Shown = false; // Native pickup may hide/recycle the source visual.
            QualityDeckInput.CompleteDrag(snapshot, true);
            CollectionInputMgr.Get().HoldingCard = true;
            var previous = QualityDeckInput.BeginDrop(false);
            Check(QualityDeckInput.PrepareDrop(false, true), "A collection drop must suppress the original insertion and route its quality explicitly");
            Check(fixture.Tray.Added.Count == 0, "A routed drop must wait until native held-card cleanup has finished");
            CollectionInputMgr.Get().HoldingCard = false;
            QualityDeckInput.EndDrop(previous, false);
            Time.frameCount++;
            QualityDeckInput.OnUpdate();
            if (mode == DragQualityMode.ChooseOnDrop)
            {
                Check(ButtonListMenu.Visible is QualityChoiceMenu && fixture.Tray.Added.Count == 0,
                    "The native drop path must open the chooser without adding the dragged quality first");
                ButtonListMenu.Visible.Buttons.First(b => b.m_ButtonText.Text.StartsWith("Normal")).Click();
                Check(fixture.Tray.Added.SequenceEqual(new[] { TAG_PREMIUM.NORMAL }), "The drop chooser must add exactly the selected quality once");
            }
            else
            {
                TAG_PREMIUM expected = mode == DragQualityMode.DraggedHalf ? TAG_PREMIUM.NORMAL
                    : clickMode == ClickQualityMode.PreferredOrder ? TAG_PREMIUM.SIGNATURE : TAG_PREMIUM.GOLDEN;
                Check(fixture.Tray.Added.SequenceEqual(new[] { expected }), "The native drop path must apply each drag priority after cleanup");
            }
            Check(!UniversalInputManager.Get().DialogActive, "Completing a drop must leave no modal claim behind");
            Check(fixture.Tray.Suggestions == 1, "A routed drop must invoke native suggestions exactly once");
        }
        var canceled = QualityCase(1, 1, 1);
        QualityDeckInput.CaptureDrag(canceled.Visual, true);
        Check(!QualityDeckInput.PrepareDrop(true, true) && !QualityDeckInput.PrepareDrop(false, false),
            "Canceled drops and drops outside the deck must retain native cleanup without an insertion");
        QualityDeckInput.EndDrop(null, false);
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(canceled.Tray.Added.Count == 0, "Canceled and outside drops must never add a card later");
        Check(!QualityDeckInput.PrepareDrop(false, true), "A deck tile or unrelated drag must never use collection quality routing");

        var failed = QualityCase(1, 1, 1);
        var failedSnapshot = QualityDeckInput.PrepareDrag(failed.Visual);
        QualityDeckInput.CompleteDrag(failedSnapshot, false);
        Check(!QualityDeckInput.PrepareDrop(false, true), "A failed pickup must not leave a collection drag capture");
        QualityDeckInput.CaptureDrag(failed.Visual, true);
        Check(QualityDeckInput.PrepareDrop(false, true), "A successful pickup must be routable");
        QualityDeckInput.EndDrop(null, true);
        Time.frameCount++;
        QualityDeckInput.OnUpdate();
        Check(failed.Tray.Added.Count == 0, "Failed native drop cleanup must cancel the pending insertion");

        var nonSplit = QualityCase(1, 1, 1, QualityDisplayMode.HighestOnly);
        CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
        nonSplit.Actor.Premium = TAG_PREMIUM.NORMAL;
        Check(AddGesture(nonSplit, false, out var click) && click == TAG_PREMIUM.SIGNATURE,
            "A hidden half preference must use Quality order outside Split mode");
        Check(AddGesture(nonSplit, true, out var drag) && drag == TAG_PREMIUM.GOLDEN,
            "A hidden drag-half preference must use Quality order and live inventory outside Split mode");
    }

    private static void CheckLiveSettingsRefresh()
    {
        QualityCase(1, 1, 2);
        int refreshes = KeywordFilterApplier.Refreshes;
        UniversalInputManager.Get().SetGameDialogActive(true);
        QualityDeckInput.SettingsChanged();
        QualityDeckInput.SettingsChanged();
        Check(KeywordFilterApplier.Refreshes == refreshes,
            "Setting callbacks must queue refreshes rather than rebuild UI reentrantly");
        CollectionInputMgr.Get().HoldingCard = true;
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes,
            "A queued live refresh must wait while a card is held");
        CollectionInputMgr.Get().HoldingCard = false;
        CraftingTray.Instance = new CraftingTray { Shown = true };
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 1,
            "A queued display refresh can apply while the crafting tray is shown");
        CraftingTray.Instance = null;
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 1 && UniversalInputManager.Get().DialogCount == 1,
            "Pending changes must coalesce into one live refresh without releasing the open menu's input claim");
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 1,
            "A completed live refresh must not repeat on every update");
        UniversalInputManager.Get().SetGameDialogActive(false);
        QualityDeckInput.ResetSession();
    }

    private static void CheckModsSettingsMenu()
    {
        var scrollbarTemplate = MakeSettingsScrollbarTemplate();
        QualityCase(1, 1, 2, QualityDisplayMode.Split, hideRedundant: true);
        CollectionQualitySettings.DisenchantPopup = true;
        CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
        var menu = new GameObject("ModsSettingsMenu").AddComponent<ModsSettingsMenu>();
        var owner = new GameObject("OptionsOwner").AddComponent<OptionsMenu>();
        owner.m_signatureTextDisplayDropdown.addItem("Native signature option");
        owner.m_signatureTextDisplayDropdown.setSelection("Native signature option");
        var graphics = new GameObject("NativeGraphicsGroup");
        graphics.transform.SetParent(owner.transform, false);
        owner.m_graphicsQuality = DropdownControl.MakeTemplate();
        owner.m_graphicsQuality.transform.SetParent(graphics.transform, false);
        var graphicsCaption = new GameObject("GraphicsCaption");
        graphicsCaption.transform.SetParent(graphics.transform, false);
        UberText nativeHeading = graphicsCaption.AddComponent<UberText>();
        nativeHeading.Text = "Quality";
        nativeHeading.TextColor = new Color(0.8f, 0.7f, 0.4f, 1f);
        nativeHeading.WordWrap = nativeHeading.ResizeToFit = nativeHeading.RenderToTexture = true;
        var stale = new GameObject("SourceCaptionGlyphs");
        stale.transform.SetParent(graphicsCaption.transform, false);
        stale.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds { size = new Vector3(1000f, 1000f, 1000f) } };
        var nativeBanner = Child(graphics.transform, "GraphicsHeader");
        nativeBanner.transform.localPosition = new Vector3(0, 0, 1.3f);
        nativeBanner.AddComponent<BoxCollider>(); // Never copy native input.
        var rightHalf = Child(nativeBanner.transform, "RightHalf");
        var bannerArtwork = Child(rightHalf.transform, "NativeHeaderBanner");
        Mesh bannerMesh = bannerArtwork.AddComponent<MeshFilter>().sharedMesh = new Mesh
        {
            name = "NativeOptionsSectionBanner", subMeshCount = 1,
            vertices = new[] { new Vector3(0, 0, -0.275f), new Vector3(2.3f, 0, -0.275f),
                new Vector3(0, 0, 0.275f), new Vector3(2.3f, 0, 0.275f) },
            bounds = new Bounds(new Vector3(1.15f, 0, 0), new Vector3(2.3f, 0.1f, 0.55f))
        };
        bannerMesh.SetTriangles(new List<int> { 0, 2, 1, 1, 2, 3 }, 0, false);
        Material bannerMaterial = bannerArtwork.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = "NativeOptionsHeader" };
        bannerArtwork.GetComponent<MeshRenderer>().sortingLayerID = 6;
        bannerArtwork.GetComponent<MeshRenderer>().sortingOrder = 17;
        bannerArtwork.GetComponent<MeshRenderer>().SetPropertyBlock(new MaterialPropertyBlock { Color = new Color(1, 0, 0, 1) });
        var leftHalf = Child(nativeBanner.transform, "LeftMirroredHalf");
        leftHalf.transform.localScale = new Vector3(-1, 1, 1);
        var leftArtwork = Child(leftHalf.transform, "NativeHeaderBanner");
        leftArtwork.AddComponent<MeshFilter>().sharedMesh = bannerMesh;
        var leftRenderer = leftArtwork.AddComponent<MeshRenderer>();
        leftRenderer.sharedMaterial = bannerMaterial;
        leftRenderer.sortingLayerID = 6;
        leftRenderer.sortingOrder = 17;
        leftRenderer.SetPropertyBlock(new MaterialPropertyBlock { Color = new Color(1, 0, 0, 1) });
        // A separate narrow ornament straddles the resize boundary. Treating
        // it as a rigid decoration while stretching its neighbor leaves a crack.
        var ornamentMesh = new Mesh
        {
            name = "NativeOptionsSectionOrnament", subMeshCount = 1,
            vertices = new[] { new Vector3(2.3f, 0, -0.275f), new Vector3(2.9f, 0, -0.275f),
                new Vector3(2.3f, 0, 0.275f), new Vector3(2.9f, 0, 0.275f) },
            bounds = new Bounds(new Vector3(2.6f, 0, 0), new Vector3(0.6f, 0.1f, 0.55f))
        };
        ornamentMesh.SetTriangles(new List<int> { 0, 2, 1, 1, 2, 3 }, 0, false);
        foreach (Transform half in new[] { rightHalf.transform, leftHalf.transform })
        {
            GameObject tip = Child(half, "NativeHeaderOrnament");
            tip.AddComponent<MeshFilter>().sharedMesh = ornamentMesh;
            var renderer = tip.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = bannerMaterial;
            renderer.sortingLayerID = 6;
            renderer.sortingOrder = 17;
            renderer.SetPropertyBlock(new MaterialPropertyBlock { Color = new Color(1, 0, 0, 1) });
        }
        var bannerText = Child(nativeBanner.transform, "GraphicsTitle").AddComponent<UberText>();
        bannerText.Text = "Graphics";
        bannerText.FontSize = 40;
        bannerText.CharacterSize = 1.65f;
        bannerText.TextColor = Color.white;
        bannerText.BoldSize = 2f;
        bannerText.Alignment = UberText.AlignmentOptions.Center;
        // A nearby gold field caption overlaps the banner's vertical bounds.
        // Proximity alone used to select it and put a small caption below an empty banner.
        var misleadingCaption = Child(graphics.transform, "ResolutionCaption").AddComponent<UberText>();
        misleadingCaption.Text = "Resolution";
        misleadingCaption.TextColor = nativeHeading.TextColor;
        misleadingCaption.transform.localPosition = new Vector3(0, 0, 1.05f);
        foreach (string caption in new[] { "Signature Appearance", "Use favorite appearance" })
        {
            var extra = new GameObject("NativeSectionCaption");
            extra.transform.SetParent(owner.m_signatureTextDisplayDropdown.transform, false);
            extra.AddComponent<UberText>().Text = caption;
            if (caption == "Signature Appearance") extra.transform.localPosition = new Vector3(0f, 0f, 0.4f);
        }
        var optionsHeader = Child(owner.transform, "OriginalOptionsHeader");
        optionsHeader.transform.localPosition = new Vector3(0, 0, 4f);
        Mesh optionsBanner = optionsHeader.AddComponent<MeshFilter>().sharedMesh = new Mesh
            { bounds = new Bounds(new Vector3(0.8f, 0, 0), new Vector3(6f, 0.1f, 1.2f)) };
        optionsHeader.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = "OriginalOptionsHeaderMaterial" };
        UberText optionsTitle = Child(optionsHeader.transform, "OptionsTitle").AddComponent<UberText>();
        optionsTitle.transform.localPosition = new Vector3(0.8f, 0, 0);
        optionsTitle.Text = "Options";
        optionsTitle.FontSize = 50;
        optionsTitle.CharacterSize = 1.3f;
        optionsTitle.TextColor = new Color(0.2f, 0.15f, 0.05f, 1f);
        menu.Owner = owner;
        optionsHeader.SetActive(false);
        nativeBanner.SetActive(false); // Options can hide after template capture.
        Check(menu.Ready, "The production settings menu must initialize with native controls");
        menu.NativeBackground.UseChildRenderer();
        Check(menu.NativeBackground.GetComponent<Renderer>() == null,
            "The reported native Settings background has its renderer on a child slice, not the root");
        menu.Show(false);
        UberText mainTitle = menu.GetComponentsInChildren<UberText>(false).Single(t => t.Text == "Collection Mod Settings");
        Check(mainTitle.FontSize == optionsTitle.FontSize && mainTitle.CharacterSize == optionsTitle.CharacterSize
            && mainTitle.TextColor.Equals(optionsTitle.TextColor), "The main heading must use original Options typography");
        MeshRenderer headerCopy = menu.GetComponentsInChildren<MeshRenderer>(false)
            .Single(r => ReferenceEquals(r.GetComponent<MeshFilter>()?.sharedMesh, optionsBanner));
        Check(Near(headerCopy.bounds.size.z, 1.2f) && !optionsHeader.activeSelf && optionsTitle.Text == "Options",
            "The main banner must retain the original Options size without changing the original header");
        Rect mainInk = SettingsTextBounds(mainTitle);
        Rect mainBannerBounds = SettingsSectionBounds(mainTitle);
        Check(mainInk.center.y > mainBannerBounds.center.y,
            "The main title must retain the original raised baseline within its banner");
        var sectionBanners = menu.GetComponentsInChildren<Transform>(true)
            .Where(t => t.gameObject.name == "BCM_SectionBanner").ToArray();
        Check(sectionBanners.Length == 5,
            "All settings group headings must retain their native Options banners while offscreen rows scroll");
        foreach (Transform banner in sectionBanners)
        {
            var artwork = banner.GetComponentsInChildren<MeshRenderer>(true);
            Check(artwork.Length == 4 && artwork.All(art =>
                (ReferenceEquals(SettingsSourceMesh(art), bannerMesh)
                    || ReferenceEquals(SettingsSourceMesh(art), ornamentMesh)
                    || SettingsSourceMesh(art).name == bannerMesh.name + "_BCM_Resized"
                    || SettingsSourceMesh(art).name == ornamentMesh.name + "_BCM_Resized")
                && ReferenceEquals(art.sharedMaterial, bannerMaterial))
                && banner.GetComponentsInChildren<Collider>(true).Length == 0
                && banner.GetComponentsInChildren<DropdownControl>(true).Length == 0,
                "Section banners must retain both native halves without copying input or settings components: " + banner.parent.name + " | " + string.Join(",", artwork.Select(art => art.gameObject.name + ":" + art.GetComponent<MeshFilter>()?.sharedMesh?.name)));
            foreach (MeshRenderer art in artwork)
            {
                var properties = new MaterialPropertyBlock();
                art.GetPropertyBlock(properties);
                Check(properties.Color.Equals(new Color(1, 0, 0, 1)), "Both banner halves must retain their native material overrides");
            }
            Check(banner.GetComponentsInChildren<Transform>(true).Any(t => t.localScale.x < 0),
                "The mirrored transform for the left banner half must be preserved");
        }
        foreach (UberText title in menu.GetComponentsInChildren<UberText>(true)
            .Where(t => t.gameObject.name.StartsWith("BCM_ModsLabelSection", StringComparison.Ordinal)))
            Check(title.TextColor.Equals(Color.white) && title.FontSize == bannerText.FontSize
                && title.BoldSize == bannerText.BoldSize && title.Alignment == bannerText.Alignment,
                "Section titles must use the white bold native heading style instead of a nearby gold field caption");
        Check(!nativeBanner.activeSelf && bannerArtwork.GetComponent<MeshRenderer>().enabled && bannerText.Text == "Graphics",
            "Copying native banners must preserve the original Options header and visibility");
        var fields = menu.GetComponentsInChildren<DropdownControl>(true);
        Check(fields.Length == 11 && menu.GetComponentsInChildren<DropdownControl>(false).Length >= 8 && menu.Buttons.Count == 0 && menu.Header == "Collection Mod Settings",
            $"Settings must show collection, priorities and filter controls together without a section picker (all={fields.Length}, visible={menu.GetComponentsInChildren<DropdownControl>(false).Length}, buttons={menu.Buttons.Count}, header={menu.Header})");
        Check(menu.GetComponentsInChildren<UberText>(true).All(t => !t.Text.Contains("Signature Appearance")
            && !t.Text.Contains("favorite")), "Cloning a dropdown must not import its section heading or favorite help text");
        foreach (string caption in new[] { "Only one copy at highest quality", "Add to deck by clicking", "Add to deck by dragging" })
            Check(menu.GetComponentsInChildren<UberText>(true).Any(t => t.Text == caption),
                "Settings headings must explain the display case and deck-add gestures");
        var display = fields.Single(d => d.Items[0] is QualityDisplayMode);
        var click = fields.Single(d => d.Items[0] is ClickQualityMode);
        var drag = fields.Single(d => d.Items[0] is DragQualityMode);
        var checkboxes = menu.GetComponentsInChildren<CheckBox>(true);
        Check(checkboxes.Length == 11 && menu.GetComponentsInChildren<CheckBox>(false).Any(box => box.m_uberText.Text == "Collapse quality variants")
            && menu.GetComponentsInChildren<CheckBox>(false).Any(box => box.m_uberText.Text == "Color code counter"),
            "All eleven settings checkboxes must exist and the handling controls stay visible while overflow scrolls");
        var checkbox = checkboxes.Single(c => c.m_uberText.Text == "Hide redundant settings");
        var counterCheckbox = checkboxes.Single(c => c.m_uberText.Text == "Color code counter");
        var rememberCheckbox = checkboxes.Single(c => c.m_uberText.Text == "Remember filter modes");
        Check(!rememberCheckbox.IsChecked() && !CollectionQualitySettings.RememberFilterModes,
            "Remember filter modes must be opt-in");
        FilterModes.Set(FilterTrayKind.Race, FilterMode.Or);
        rememberCheckbox.Click();
        Check(CollectionQualitySettings.RememberFilterModes && CollectionQualitySettings.SavedFilterModes.Contains("Race=Or"),
            "Checking Remember filter modes snapshots the current session modes");
        rememberCheckbox.Click();
        Check(!CollectionQualitySettings.RememberFilterModes && CollectionQualitySettings.SavedFilterModes == ""
            && FilterModes.Get(FilterTrayKind.Race) == FilterMode.Or,
            "Disabling persistence clears saved modes while keeping this session unchanged");
        FilterModes.StartSession();
        Check(!counterCheckbox.IsChecked() && !CollectionQualitySettings.ColorCodeCounter,
            "Counter coloring must be an opt-in setting");
        counterCheckbox.Click();
        Check(counterCheckbox.IsChecked() && CollectionQualitySettings.ColorCodeCounter && checkbox.IsChecked(),
            "The counter checkbox must enable coloring without changing redundant settings");
        counterCheckbox.Click();
        Check(!CollectionQualitySettings.ColorCodeCounter, "The counter checkbox must also disable coloring");
        Check(display.Items.SequenceEqual(new object[] { QualityDisplayMode.Split, QualityDisplayMode.Stacking,
            QualityDisplayMode.HighestOnly, QualityDisplayMode.TwoHighest, QualityDisplayMode.PreferTwoCopies }),
            "Display choices must include Stacking beside Split");
        Check(checkbox.IsChecked() && checkbox.m_uberText.Text == "Hide redundant settings",
            "The bottom checkbox must default to hiding redundant settings with an English caption");
        CheckSettingsScrolling(menu);
        CheckSettingsLayout(menu);
        CheckSettingsSections(menu);
        Action checkLock = () => Check(UniversalInputManager.Get().DialogCount == 1,
            "Changing or reopening dropdowns must retain exactly one native modal claim");
        checkLock();
        Check(click.Items.Contains(ClickQualityMode.ClickedHalf) && drag.Items.Contains(DragQualityMode.SameAsClick),
            "Split mode must expose Clicked half and let dragging inherit that policy");
        Check(drag.Items.SequenceEqual(new object[] { DragQualityMode.DraggedHalf, DragQualityMode.ChooseOnDrop, DragQualityMode.SameAsClick })
            && QualityOptions.Label(DragQualityMode.SameAsClick) == "Same as click",
            "Split drag choices must read Dragged half, Choose after dropping, Same as click");
        click.SelectOption(ClickQualityMode.PreferredOrder);
        drag.SelectOption(DragQualityMode.DraggedHalf);
        Check(QualityOptions.EffectiveClick == ClickQualityMode.PreferredOrder
            && QualityOptions.EffectiveDrag == DragQualityMode.DraggedHalf,
            "Dragged half must be selectable independently of the click policy");
        click.SelectOption(ClickQualityMode.ClickedHalf);
        int refreshes = KeywordFilterApplier.Refreshes;
        foreach (QualityDisplayMode value in new[] { QualityDisplayMode.HighestOnly, QualityDisplayMode.TwoHighest,
            QualityDisplayMode.PreferTwoCopies, QualityDisplayMode.Stacking })
        {
            display.SelectOption(value);
            bool stacking = value == QualityDisplayMode.Stacking;
            Check(click.Items.Contains(ClickQualityMode.ClickedHalf) == stacking && drag.Items.Contains(DragQualityMode.SameAsClick),
                "Only merged displays may offer manual half selection; all displays retain Same as click");
            Check(drag.Items.SequenceEqual(stacking
                    ? new object[] { DragQualityMode.DraggedHalf, DragQualityMode.ChooseOnDrop, DragQualityMode.SameAsClick }
                    : new object[] { DragQualityMode.ChooseOnDrop, DragQualityMode.SameAsClick })
                && CollectionQualitySettings.Drag == DragQualityMode.DraggedHalf,
                "Stacking must retain independent Dragged half while other non-split modes hide it");
            bool locked = value == QualityDisplayMode.PreferTwoCopies;
            Check(Equals(click.getSelection(), locked ? ClickQualityMode.HighestFullyAvailable : ClickQualityMode.PreferredOrder)
                && Equals(drag.getSelection(), stacking ? DragQualityMode.DraggedHalf : DragQualityMode.SameAsClick),
                "The menu must show the forced playset policy for paired display and the effective click policy otherwise");
            Check(click.IsEnabled() && click.m_button.IsEnabled() && click.m_selectedItem.IsEnabled(),
                "Redundant settings must be hidden rather than styled as blocked controls");
            bool direct = value == QualityDisplayMode.TwoHighest;
            Check(!click.gameObject.activeInHierarchy && drag.gameObject.activeInHierarchy == !direct,
                "Highest-only and paired modes must hide click settings; Two highest must hide both input fields");
            Check(menu.GetComponentsInChildren<UberText>(true).Where(t => t.Text == "Add to deck by clicking")
                .All(t => !t.gameObject.activeInHierarchy)
                && menu.GetComponentsInChildren<UberText>(true).Where(t => t.Text == "Add to deck by dragging")
                .All(t => t.gameObject.activeInHierarchy == !direct), "Hidden input settings must also hide their own headings");
            Check(menu.GetComponentsInChildren<DropdownControl>(false).Count(control => control.gameObject.name != "BCM_ModsCollectionopensearch") == (direct ? 8 : 9),
                "Hiding redundant collection inputs must retain every priority and filter control");
            CheckVisibleRowsLayout(menu);
            float fixedHeight = menu.BodyHeight, fixedWidth = menu.BodyWidth;
            Rect fixedTitle = SettingsTextBounds(mainTitle);
            checkbox.Click();
            Check(Math.Abs(menu.BodyHeight - fixedHeight) * CameraUtils.Camera.PixelScale < SettingsPixelTolerance
                && Math.Abs(menu.BodyWidth - fixedWidth) * CameraUtils.Camera.PixelScale < SettingsPixelTolerance
                && Math.Abs(SettingsTextBounds(mainTitle).center.y - fixedTitle.center.y) < SettingsPixelTolerance,
                $"Showing redundant settings must preserve the outer menu size, title position and control scale (height={menu.BodyHeight}/{fixedHeight}, width={menu.BodyWidth}/{fixedWidth}, titleY={SettingsTextBounds(mainTitle).center.y}/{fixedTitle.center.y})");
            Check(!CollectionQualitySettings.HideRedundant && !checkbox.IsChecked()
                && click.gameObject.activeInHierarchy && drag.gameObject.activeInHierarchy,
                "Unchecking Hide redundant settings must show all input fields in every display mode");
            CheckVisibleRowsLayout(menu);
            Check(click.Items.Contains(ClickQualityMode.ClickedHalf) == stacking
                && drag.Items.Contains(DragQualityMode.DraggedHalf) == stacking,
                "Showing redundant settings must expose half choices only in merged displays");
            ClickQualityMode savedClick = CollectionQualitySettings.Click;
            click.SelectOption(ClickQualityMode.HighestFullyAvailable);
            Check(QualityOptions.EffectiveClick == ClickQualityMode.HighestFullyAvailable,
                "Visible manual click overrides must take precedence over automatic display policies");
            click.SelectOption(ClickQualityMode.PreferredOrder);
            Check(QualityOptions.EffectiveClick == ClickQualityMode.PreferredOrder,
                "Both manual policies must remain selectable even in paired display");
            click.SelectOption(savedClick == ClickQualityMode.ClickedHalf ? ClickQualityMode.PreferredOrder : savedClick);
            CollectionQualitySettings.Click = savedClick;
            checkbox.Click();
            Check(CollectionQualitySettings.HideRedundant && checkbox.IsChecked()
                && !click.gameObject.activeInHierarchy && drag.gameObject.activeInHierarchy == !direct,
                "Rechecking Hide redundant settings must restore compact layout and automatic policies");
            Check(menu.GetComponentsInChildren<CheckBox>(true).Contains(checkbox) && counterCheckbox.gameObject.activeInHierarchy,
                "Other remains available by scrolling and the counter checkbox stays visible in every layout");
            checkLock();
        }
        display.SelectOption(QualityDisplayMode.Split);
        Check(Equals(click.getSelection(), ClickQualityMode.ClickedHalf) && Equals(drag.getSelection(), DragQualityMode.DraggedHalf),
            "Returning to Split must restore both saved half preferences");
        Check(click.IsEnabled() && click.m_button.IsEnabled() && click.m_selectedItem.IsEnabled(),
            "Leaving the paired display must restore input on every click field target");
        Check(click.Items.Contains(ClickQualityMode.HighestFullyAvailable)
            && drag.Items.Contains(DragQualityMode.SameAsClick)
            && QualityOptions.Label(ClickQualityMode.HighestFullyAvailable) == "Highest fully available",
            "The complete playset click policy must also be available through inherited dragging");
        click.SelectOption(ClickQualityMode.HighestFullyAvailable);
        drag.SelectOption(DragQualityMode.ChooseOnDrop);
        Check(CollectionQualitySettings.Click == ClickQualityMode.HighestFullyAvailable && CollectionQualitySettings.Drag == DragQualityMode.ChooseOnDrop,
            "Both dropdowns must persist independent input settings immediately");
        display.SelectOption(QualityDisplayMode.HighestOnly);
        Check(KeywordFilterApplier.Refreshes == refreshes,
            "Dropdown callbacks must queue their collection changes until the update loop");
        drag.onUserPressedButton();
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 1 && menu.IsShown() && drag.isMenuShown(),
            "Settings changes must refresh the collection live without closing Mods or its open dropdown");
        checkLock();
        drag.onUserPressedButton();
        Check(ModsSettingsMenu.ReturnToOptions() && menu.IsShown() && !drag.isMenuShown() && owner.Shows == 0,
            "Escape must close an expanded dropdown before leaving Mods");
        checkLock();
        Check(ModsSettingsMenu.ReturnToOptions() && !menu.IsShown() && owner.Shows == 1,
            "Escape with no expanded dropdown must return to the owning Options menu");
        Check(menu.GetComponentsInChildren<CameraMask>(true).Length == 0
            && menu.GetComponentsInChildren<DropdownControl>(true).All(control => control.gameObject.layer == (int)GameLayer.HighPriorityUI)
            && menu.GetComponentsInChildren<MeshFilter>(true).All(filter => filter.sharedMesh == null || filter.sharedMesh.name?.EndsWith("_BCM_Clipped") != true),
            "Leaving Settings restores original native meshes and keeps the ordinary UI layers; leftover meshes="
                + string.Join(",", menu.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.sharedMesh?.name?.EndsWith("_BCM_Clipped") == true)
                    .Select(filter => filter.gameObject.name + ":" + filter.sharedMesh.name)));
        checkLock();
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 1,
            "Returning to Options must not repeat an already applied live refresh");
        CollectionQualitySettings.Display = QualityDisplayMode.TwoHighest;
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 2 && UniversalInputManager.Get().DialogCount == 1,
            "A collection change must also apply live while native Options retains its modal input claim");
        owner.Hide(false);
        menu.Show(false);
        menu.Show(false);
        checkLock();
        Check(menu.GetComponentsInChildren<DropdownControl>(true).Length == fields.Length,
            "Reopening Mods must reuse its existing section controls rather than cloning new rows");
        Check(menu.GetComponentsInChildren<Transform>(true).Count(t => t.gameObject.name == "BCM_SectionBanner") == 5,
            "Reopening Mods must reuse its existing banner copies");
        var offClick = typeof(ModsSettingsMenu).GetMethod("OnBlockerRelease", BindingFlags.Instance | BindingFlags.NonPublic);
        var settingsLateUpdate = typeof(ModsSettingsMenu).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        Vector3 savedPointer = Input.mousePosition;
        bool savedRelease = Input.LeftReleased;
        Rect panel = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsPanel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
        Vector3 outsidePanel = new Vector3(panel.xMax + 1f, panel.center.y, 0f);
        Input.mousePosition = outsidePanel; Input.LeftReleased = true;
        settingsLateUpdate.Invoke(menu, null);
        Check(menu.IsShown(), "The release opening Settings cannot immediately dismiss it");
        Time.frameCount += 3;
        Input.mousePosition = new Vector3(panel.center.x, panel.center.y, 0f);
        offClick.Invoke(menu, new object[] { new UIEvent() });
        Check(menu.IsShown(), "A blocker release over empty space inside Settings keeps the panel open");
        Rect titleInk = SettingsTextBounds(menu.GetComponentsInChildren<UberText>(false).Single(t => t.Text == "Collection Mod Settings"));
        Input.mousePosition = new Vector3(titleInk.center.x, titleInk.center.y, 0f);
        offClick.Invoke(menu, new object[] { new UIEvent() });
        Check(menu.IsShown(), "The visible Settings header belongs to the inside-click area");
        display.onUserPressedButton();
        Check(display.isMenuShown(), "The visible display dropdown opens before the outside dismissal regression");
        Input.mousePosition = new Vector3(panel.xMax + 1f, panel.yMax + 1f, 0f);
        Check(((List<DropdownControl>)typeof(ModsSettingsMenu).GetField("m_controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu)).Contains(display), "Regression uses an owned dropdown");
        settingsLateUpdate.Invoke(menu, null);
        Check(menu.IsShown() && !display.isMenuShown(), "An outside click closes the dropdown before closing Settings");
        settingsLateUpdate.Invoke(menu, null);
        Check(menu.IsShown(), "The dropdown closing release cannot fall through to dismiss Settings");
        Time.frameCount++;
        Input.mousePosition = outsidePanel;
        // Model a broad native frame collider swallowing PegUI blocker events.
        var oversizedFrameHit = menu.NativeBackground.gameObject.AddComponent<BoxCollider>();
        oversizedFrameHit.size = new Vector3(menu.BodyWidth * 4f, .1f, menu.BodyHeight * 4f);
        settingsLateUpdate.Invoke(menu, null);
        UnityEngine.Object.Destroy(oversizedFrameHit);
        Input.mousePosition = savedPointer; Input.LeftReleased = savedRelease;
        Check(!menu.IsShown() && owner.Shows == 2, "Clicking outside must return to Options without creating another menu");
        menu.Hide();
        checkLock();
        Check(owner.Shows == 2, "Repeated hides must not reacquire the Options claim");
        owner.Hide();
        Check(!UniversalInputManager.Get().IsDialogActive(), "Closing Settings must restore normal game input");
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 2, "Closing Settings must not rebuild changes already applied live");
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 2, "Applied settings must not rebuild every frame");
        Check(owner.m_signatureTextDisplayDropdown.Items.SequenceEqual(new[] { "Native signature option" })
            && Equals(owner.m_signatureTextDisplayDropdown.getSelection(), "Native signature option"),
            "Cloned mod dropdowns must not alter the native Signature setting");
        UniversalInputManager.Get().SetGameDialogActive(true); // Another dialog's claim must survive.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ButtonListMenu.FailNextShowLayout = true;
            bool failed = false;
            try { menu.Show(false); } catch (InvalidOperationException) { failed = true; }
            Check(failed && !menu.IsShown() && !menu.gameObject.activeSelf
                && UniversalInputManager.Get().DialogCount == 1 && !ModsSettingsMenu.ReturnToOptions(),
                "A layout failure before native IsShown releases only this menu's input claim, including repeated attempts");
        }
        UniversalInputManager.Get().SetGameDialogActive(false);
        UniversalInputManager.Get().SetGameDialogActive(true);
        menu.Show(false);
        var scrollbar = (GameObject)typeof(ModsSettingsMenu).GetField("m_settingsScrollbar", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
        var scrollbarMeshes = scrollbar.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.sharedMesh).ToArray();
        typeof(ModsSettingsMenu).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
        Check(scrollbarMeshes.All(mesh => mesh.Destroyed), "Destroying Settings must release every private scrollbar mesh");
        Check(scrollbarTemplate.GetComponentsInChildren<MeshFilter>(true).All(filter => !filter.sharedMesh.Destroyed)
            && scrollbarTemplate.GetComponentsInChildren<MeshRenderer>(true).All(renderer => !renderer.sharedMaterial.Destroyed),
            "Destroying Settings must preserve the game's original scrollbar meshes and materials");
        Resources.Objects = Array.Empty<UnityEngine.Object>();
        UnityEngine.Object.Destroy(scrollbarTemplate.gameObject);
        Check(!menu.IsShown() && UniversalInputManager.Get().DialogCount == 1 && owner.Shows == 2,
            "Destroying Mods must release only its own claim without reopening Options");
        Check(!ModsSettingsMenu.ReturnToOptions(), "A destroyed settings menu must not consume Escape");
        UniversalInputManager.Get().SetGameDialogActive(false);
        UnityEngine.Object.Destroy(menu.gameObject);
        QualityDeckInput.ResetSession();
    }
}
