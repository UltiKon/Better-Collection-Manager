using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboDragQualities(CollectibleRenderFixture renderer, CollectibleCard proxy, CollectibleCard carrier, CategoryCatalog.Combo combo)
    {
        CollectionManager manager = CollectionManager.Get();
        var cards = manager.Cards.ToArray(); var counts = manager.Counts.ToArray();
        var root = manager.EditedDeck; var tray = CollectionDeckTray.Instance; var oldInput = CollectionInputMgr.Instance;
        var order = PreferredPremiumPrefs.Order; var display = CollectionQualitySettings.Display;
        var click = CollectionQualitySettings.Click; var dragMode = CollectionQualitySettings.Drag; bool hidden = CollectionQualitySettings.HideRedundant;
        var previousProjection = CameraUtils.Projection;
        var previousEscapeBone = GameMenu.Get().m_menuBone;
        var escapeBone = new GameObject("Hidden Escape menu test bone").transform;
        GameMenu.Get().m_menuBone = escapeBone;
        int frame = Time.frameCount, capacity = manager.Rules.Capacity; float time = Time.unscaledTime;
        bool left = Input.LeftReleased, editing = manager.Editing;
        var source = AssetLoader.Get().InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
        source.SetEntityDef(carrier.GetEntityDef());
        var visual = new GameObject("ComboDragQuality").AddComponent<CollectionCardVisual>();
        var cached = new CollectionCardActors(source);
        var input = CollectionInputMgr.Instance = new CollectionInputMgr();
        var a = ComboCollectionCards.ComponentCards(combo, combo.Cards.Single(card => card.Count == 2).Id)[0];
        var b = ComboCollectionCards.ComponentCards(combo, combo.Cards.Single(card => card.Count == 1).Id)[0];
        void NewDeck()
        {
            QualityChoiceMenu.Cancel(); manager.Editing = true; manager.Rules.Capacity = 30;
            manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Instance = new CollectionDeckTray { Context = manager.EditedDeck };
            foreach (CollectibleCard card in new[] { a, b })
            {
                manager.Counts[(card.CardId, TAG_PREMIUM.NORMAL)] = 2;
                var golden = Card(card.CardId, TAG_PREMIUM.GOLDEN, 1); golden.ManaCost = card.ManaCost;
                manager.Cards[(card.CardId, TAG_PREMIUM.GOLDEN)] = golden; manager.Counts[(card.CardId, TAG_PREMIUM.GOLDEN)] = 1;
            }
            PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
            Time.frameCount += 3; Time.unscaledTime += .2f; Input.LeftReleased = false;
            renderer.Render(proxy, visual, cached);
            PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
        }
        void Drop()
        {
            typeof(CollectionCardVisual).GetMethod("OnDrag", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(visual, null);
            Check(input.HasHeldCard(), "Drag quality fixture acquires the native combo visual");
            input.OverDeck = true; Input.LeftReleased = true;
            InvokeQualityHook("HeldReleasePatch", "Prefix", input); Time.frameCount++; ComboCollectionCards.OnUpdate();
        }
        void ChooseNormal()
        {
            Check(ButtonListMenu.Visible is QualityChoiceMenu && ButtonListMenu.Visible.IsShown() && ButtonListMenu.Visible.gameObject.activeInHierarchy,
                "Choose-after-drop displays an active native quality menu for the current combo component");
            QualityDeckInput.OnUpdate();
            ClickControl("BCM_ChooseNORMAL");
        }
        Collider PickAt(ButtonListMenu menu, Vector3 pointer)
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(menu.gameObject);
            return menu.GetComponentsInChildren<Collider>(false)
                .Where(hit => hit.enabled && ComboSceneGeometry.ProjectBounds(hit.bounds, camera).Contains(pointer))
                .OrderBy(hit => camera.WorldToScreenPoint(hit.bounds.center).z).FirstOrDefault();
        }
        void ClickControl(string name)
        {
            ButtonListMenu menu = ButtonListMenu.Visible;
            UIBButton target = menu.Buttons.Single(button => button.gameObject.name == name);
            Camera camera = CameraUtils.FindProjectionCameraForObject(menu.gameObject);
            Vector3 pointer = camera.WorldToScreenPoint(target.GetComponent<BoxCollider>().bounds.center);
            Collider picked = PickAt(menu, pointer);
            Check(picked?.GetComponentInParent<UIBButton>() == target && target.IsEnabled(),
                "The visible " + name + " receives the pointer hit ahead of the modal blocker");
            target.Click();
        }
        try
        {
            CollectionQualitySettings.Display = QualityDisplayMode.Split; CollectionQualitySettings.HideRedundant = false;
            CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder; CollectionQualitySettings.Drag = DragQualityMode.DraggedHalf;
            NewDeck(); Drop();
            Check(manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.GOLDEN) == 1
                && manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.NORMAL) == 1
                && manager.EditedDeck.GetCardCountAllMatchingSlots(b.CardId, TAG_PREMIUM.GOLDEN) == 1,
                "Dragged-half mode uses the combo's displayed common Golden quality, with owned fallback for the second copy");

            CollectionQualitySettings.Drag = DragQualityMode.SameAsClick; NewDeck(); Drop();
            Check(manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.GOLDEN) == 1
                && manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.NORMAL) == 1
                && manager.EditedDeck.GetCardCountAllMatchingSlots(b.CardId, TAG_PREMIUM.GOLDEN) == 1,
                "Same-as-click drag uses saved quality order with owned fallback for each captured copy");
            CollectionQualitySettings.Click = ClickQualityMode.HighestFullyAvailable; NewDeck(); Drop();
            Check(manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.NORMAL) == 2
                && manager.EditedDeck.GetCardCountAllMatchingSlots(b.CardId, TAG_PREMIUM.NORMAL) == 1,
                "Same-as-click drag honors Highest fully available independently for every combo component");
            CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf; NewDeck();
            ComboCollectionCards.HandleInput(visual, typeof(CollectionCardVisual).GetMethod("OnRelease", BindingFlags.Instance | BindingFlags.NonPublic));
            var clicked = CollectionDeckTray.Get().Added.ToArray();
            NewDeck(); Drop();
            Check(CollectionDeckTray.Get().Added.SequenceEqual(clicked) && clicked.Length == 3
                && clicked.Count(premium => premium == TAG_PREMIUM.GOLDEN) == 2 && clicked.Count(premium => premium == TAG_PREMIUM.NORMAL) == 1,
                "Same-as-click uses the same displayed quality preference as an actual combo click in Clicked-half mode");

            QualityChoiceMenu.DestroyMenu();
            escapeBone.localScale = Vector3.zero;
            escapeBone.localPosition = new Vector3(-10000, -10000, -10000);
            escapeBone.localRotation = new Quaternion(1, 0, 0, 0);
            CollectionQualitySettings.Drag = DragQualityMode.ChooseOnDrop; NewDeck(); Drop();
            Check(CollectionDeckTray.Get().Added.Count == 0, "Choose-after-drop inserts no cards before the first quality confirmation");
            Check(ComboSceneGeometry.ScreenBounds(ButtonListMenu.Visible.NativeRig.gameObject, CameraUtils.Camera, out Rect initialArea)
                && initialArea.width / ButtonListMenu.Visible.NativeRig.localScale.x > Screen.height * .1f && initialArea.height > 0,
                "A hidden, collapsed and offscreen Escape-menu bone cannot hide the quality popup on first open");
            var uiCamera = new Camera { PixelScale = 3f, Offset = new Vector3(411, 97, 0), DepthScale = .1f,
                nearClipPlane = 10f, farClipPlane = 110f };
            CameraUtils.Projection = go => OverlayUI.Get().HasObject(go) ? uiCamera : CameraUtils.Camera;
            var choiceMenu = (QualityChoiceMenu)ButtonListMenu.Visible;
            Check(choiceMenu.NativeOpeningAnimationPending == ButtonListMenu.SimulateNativeOpeningAnimation,
                "The quality picker retains the original native opening animation");
            choiceMenu.CompleteNativeOpeningAnimation();
            var originalScale = choiceMenu.NativeRig.localScale;
            var originalRotation = choiceMenu.NativeRig.localRotation;
            var labels = choiceMenu.NativeRig.GetComponentsInChildren<UberText>(true)
                .Select(text => (text.transform, text.transform.localPosition, text.transform.localScale)).ToArray();
            choiceMenu.NativeRig.position = uiCamera.ScreenToWorldPoint(new Vector3(-10000, -10000, -50));
            typeof(QualityChoiceMenu).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(choiceMenu, null);
            Check(OverlayUI.Get().HasObject(choiceMenu.gameObject)
                && ComboSceneGeometry.ScreenBounds(choiceMenu.NativeRig.gameObject, uiCamera, out Rect choicesArea)
                && choicesArea.xMin >= 0 && choicesArea.xMax <= Screen.width && choicesArea.yMin >= 0 && choicesArea.yMax <= Screen.height
                && uiCamera.WorldToScreenPoint(choiceMenu.NativeRig.position).z > uiCamera.nearClipPlane
                && uiCamera.WorldToScreenPoint(choiceMenu.NativeRig.position).z < uiCamera.farClipPlane,
                "Choose-after-drop recovers an offscreen or clipped native menu in the overlay camera before blocking input");
            Check(choiceMenu.NativeRig.localScale.Equals(originalScale) && choiceMenu.NativeRig.localRotation.Equals(originalRotation)
                && labels.All(label => label.transform.localPosition.Equals(label.localPosition) && label.transform.localScale.Equals(label.localScale)),
                "Placing the popup through its anchor preserves the rig's native scale, local orientation and button-caption offsets");
            choiceMenu.NativeRig.localScale = Vector3.one * .01f;
            typeof(QualityChoiceMenu).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(choiceMenu, null);
            Check(choiceMenu.NativeRig.localScale.Equals(Vector3.one * .01f),
                "Fitting the parent anchor preserves the native animation's current scale");
            choiceMenu.NativeRig.localScale = originalScale;
            var normal = choiceMenu.Buttons.Single(button => button.gameObject.name == "BCM_ChooseNORMAL");
            normal.SetEnabled(false, true); normal.GetComponent<BoxCollider>().enabled = false; normal.gameObject.layer = 19;
            var decoration = new GameObject("Native menu decoration collider"); decoration.transform.SetParent(choiceMenu.NativeRig, false);
            var decorationHit = decoration.AddComponent<BoxCollider>(); decorationHit.size = new Vector3(10000, 10000, 10000);
            typeof(QualityChoiceMenu).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(choiceMenu, null);
            Check(normal.IsEnabled() && normal.GetComponent<BoxCollider>().enabled && normal.gameObject.layer == (int)GameLayer.HighPriorityUI
                && !decorationHit.enabled,
                "Quality choices enable their native hit areas on the input layer while decorative colliders cannot intercept clicks");
            Collider outside = PickAt(choiceMenu, new Vector3(Screen.width - 1, Screen.height - 1, 0));
            Check(outside?.gameObject.name == "GameMenuInputBlocker",
                "The native modal blocker still covers the viewport outside the quality menu");
            ChooseNormal();
            Check(CollectionDeckTray.Get().Added.Count == 3 && !UniversalInputManager.Get().IsDialogActive()
                && ButtonListMenu.Visible == null
                && manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.NORMAL) == 2
                && manager.EditedDeck.GetCardCountAllMatchingSlots(b.CardId, TAG_PREMIUM.NORMAL) == 1,
                "One quality choice adds every captured component and closes the only combo popup");
            ComboCollectionCards.OnUpdate(); ComboCollectionCards.OnUpdate();
            Check(CollectionDeckTray.Get().Added.Count == 3 && !QualityChoiceMenu.Cancel(),
                "Completed bundle selection cannot reopen another component popup or insert the combo twice");

            NewDeck(); Drop(); QualityChoiceMenu.Cancel(); ComboCollectionCards.OnUpdate();
            Check(CollectionDeckTray.Get().Added.Count == 0, "Canceling the bundle quality popup inserts no component");
            NewDeck(); Drop(); ClickControl("BCM_QualityCancel");
            Check(!UniversalInputManager.Get().IsDialogActive() && CollectionDeckTray.Get().Added.Count == 0,
                "Clicking the visible Cancel button releases modal input without inserting a card");
            NewDeck(); Drop(); ClickControl("BCM_ChooseGOLDEN");
            Check(CollectionDeckTray.Get().Added.Count == 3 && !UniversalInputManager.Get().IsDialogActive()
                && manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.GOLDEN) == 1
                && manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.NORMAL) == 1
                && manager.EditedDeck.GetCardCountAllMatchingSlots(b.CardId, TAG_PREMIUM.GOLDEN) == 1,
                "The single Golden choice applies to the entire combo with owned fallback when a quality runs out");
            NewDeck(); manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = 0;
            Drop();
            Check(CollectionDeckTray.Get().Added.Count == 0 && ButtonListMenu.Visible?.Header == combo.Name + " · Choose quality",
                "No single-quality member is inserted before the shared bundle popup is confirmed");
            ClickControl("BCM_QualityCancel");
            Check(CollectionDeckTray.Get().Added.Count == 0, "Canceling leaves even automatically resolvable members out of the deck");
            NewDeck(); manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = 0; manager.Counts[(b.CardId, TAG_PREMIUM.NORMAL)] = 0;
            Drop(); ClickControl("BCM_ChooseGOLDEN");
            Check(manager.EditedDeck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.NORMAL) == 2
                && manager.EditedDeck.GetCardCountAllMatchingSlots(b.CardId, TAG_PREMIUM.GOLDEN) == 1 && ButtonListMenu.Visible == null,
                "A single popup offers the union of member qualities and resolves disjoint availability without more prompts");
            NewDeck(); manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = manager.Counts[(b.CardId, TAG_PREMIUM.GOLDEN)] = 0;
            Drop();
            Check(CollectionDeckTray.Get().Added.Count == 3 && !UniversalInputManager.Get().IsDialogActive(),
                "A bundle with only one available quality inserts directly without a redundant popup");
            NewDeck(); Drop();
            manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Get().Context = manager.EditedDeck;
            QualityChoiceMenu.ValidateContext(); ComboCollectionCards.OnUpdate();
            Check(!UniversalInputManager.Get().IsDialogActive() && CollectionDeckTray.Get().Added.Count == 0,
                "Changing decks closes and discards the pending combo quality sequence");
            NewDeck(); Drop(); manager.Rules.Capacity = 0; ChooseNormal();
            Check(CollectionDeckTray.Get().Added.Count == 0 && !UniversalInputManager.Get().IsDialogActive(),
                "Confirming a quality revalidates native deck limits and skips components that became unavailable");
        }
        finally
        {
            QualityChoiceMenu.Cancel(); ComboCollectionCards.ReleaseHiddenActors(visual); ComboCollectionCards.Forget(visual);
            source.Destroy(); UnityEngine.Object.Destroy(visual.gameObject);
            manager.Cards.Clear(); manager.Counts.Clear();
            foreach (var pair in cards) manager.Cards.Add(pair.Key, pair.Value);
            foreach (var pair in counts) manager.Counts.Add(pair.Key, pair.Value);
            manager.EditedDeck = root; manager.Editing = editing; manager.Rules.Capacity = capacity;
            CollectionDeckTray.Instance = tray; CollectionInputMgr.Instance = oldInput; PreferredPremiumPrefs.Order = order;
            CollectionQualitySettings.Display = display; CollectionQualitySettings.Click = click; CollectionQualitySettings.Drag = dragMode;
            CollectionQualitySettings.HideRedundant = hidden; Time.frameCount = frame; Time.unscaledTime = time; Input.LeftReleased = left;
            CameraUtils.Projection = previousProjection;
            GameMenu.Get().m_menuBone = previousEscapeBone; UnityEngine.Object.Destroy(escapeBone.gameObject);
        }
    }
}
