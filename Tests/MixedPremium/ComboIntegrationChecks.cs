using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboIntegration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-combo-integration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "categories.json");
        var previousManager = CollectionManager.Instance; var previousInput = UniversalInputManager.Instance;
        var previousMesh = AssetLoader.Get().MeshFactory; bool previousText = AssetLoader.Get().CreateRulesText;
        bool previousMode = PreferredQualityMode.IsEnabled;
        Transform previousBone = GameMenu.Get().m_menuBone;
        Material previousRedButton = GameMenu.Get().m_redButtonMaterial;
        bool previousCameraUnavailable = CameraUtils.Unavailable;
        bool previousActivation = ButtonListMenu.NativeShowActivatesController;
        bool previousAnimation = ButtonListMenu.SimulateNativeOpeningAnimation;
        float previousTime = Time.unscaledTime;
        int width = Screen.width, height = Screen.height;
        try
        {
            var inputHooks = new HarmonyLib.Harmony(); ComboPopup.Install(inputHooks);
            UniversalInputManager.ComboInputHooks = inputHooks;
            Check(inputHooks.Patched.Count(method => method.DeclaringType == typeof(UniversalInputManager)) == 2,
                "Combo input hooks target the native per-pass font calculation and Enter completion methods");
            ExtraFilterState.ClearAll(); Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"version\":1,\"entries\":[]}"); CategoryCatalog.LoadUserFile(path);
            foreach (string categoryName in new[] { "AoE", "Value", "Draw" })
                CategoryCatalog.ImportDeckCards(categoryName, new[] { "CS2_179" });
            var manager = CollectionManager.Instance = new CollectionManager();
            UniversalInputManager.Instance = new UniversalInputManager(); PreferredQualityMode.IsEnabled = true;
            var originalMaterial = new Material { name = "SharedCardFrame", renderQueue = 2200 };
            AssetLoader.Get().MeshFactory = () => {
                Actor created = AssetLoader.Get().LastActor;
                created.StateChanged = _ => created.GetMeshRenderer().sharedMaterial = originalMaterial;
                return CardMesh();
            };
            AssetLoader.Get().CreateRulesText = true;
            var a = Card("EX1_619", TAG_PREMIUM.NORMAL, 2); var b = Card("CS2_093", TAG_PREMIUM.NORMAL, 2);
            a.ManaCost = 2; b.ManaCost = 4;
            manager.Cards[(a.CardId, a.PremiumType)] = a; manager.Cards[(b.CardId, b.PremiumType)] = b;
            manager.Counts[(a.CardId, a.PremiumType)] = a.OwnedCount; manager.Counts[(b.CardId, b.PremiumType)] = b.OwnedCount;
            manager.Editing = true; manager.EditedDeck = new CollectionDeck { Name = "AoE" };
            manager.EditedDeck.Add(a.CardId, TAG_PREMIUM.NORMAL); manager.EditedDeck.Add(a.CardId, TAG_PREMIUM.NORMAL);
            manager.EditedDeck.Add(b.CardId, TAG_PREMIUM.NORMAL);
            // The game does not provide the Escape-menu bone until that menu
            // exists. Reproduce the player's direct collection entry path.
            GameMenu.Get().m_menuBone = null;
            // Category membership prompts can already have created the shared popup.
            // Exercise unavailable dependencies on a fresh instance.
            ComboPopup.Close();
            var singleton = typeof(ComboPopup).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic);
            if (singleton.GetValue(null) is ComboPopup existing)
            {
                typeof(ComboPopup).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(existing, null);
                UnityEngine.Object.Destroy(existing.gameObject); singleton.SetValue(null, null);
            }
            CameraUtils.Unavailable = true;
            bool setupRejected = false;
            try { ComboPopup.Add(); }
            catch (InvalidOperationException ex) { setupRejected = ex.Message.Contains("camera is unavailable"); }
            Check(setupRejected && !UniversalInputManager.Get().DialogActive,
                "Unavailable native popup dependencies report the actual cause without acquiring a modal claim");
            CameraUtils.Unavailable = false;
            ButtonListMenu.NativeShowActivatesController = false;
            ButtonListMenu.SimulateNativeOpeningAnimation = true;
            GameMenu.Get().m_redButtonMaterial = null;
            ComboPopup.Add();
            Check(GameMenu.Get().m_menuBone == null && ButtonListMenu.Visible is ComboPopup,
                "Loading native Concede artwork before Escape is opened does not instantiate the game menu");
            GameMenu.Get().m_redButtonMaterial = previousRedButton;
            ButtonListMenu popup = ButtonListMenu.Visible;
            bool VisibleControlArea(UIBButton button, out Rect area)
            {
                if (new[] { "BCM_ComboName", "BCM_ComboDescription", "BCM_ComboCardText", "BCM_ComboAttack", "BCM_ComboHealth", "BCM_ComboMana" }.Contains(button.gameObject.name))
                {
                    BoxCollider hit = button.GetComponent<BoxCollider>();
                    Vector3 low = CameraUtils.Camera.WorldToScreenPoint(button.transform.TransformPoint(hit.center - hit.size * .5f));
                    Vector3 high = CameraUtils.Camera.WorldToScreenPoint(button.transform.TransformPoint(hit.center + hit.size * .5f));
                    area = Rect.MinMaxRect(Math.Min(low.x, high.x), Math.Min(low.y, high.y), Math.Max(low.x, high.x), Math.Max(low.y, high.y));
                    return true;
                }
                return ComboSceneGeometry.ScreenBounds(button.gameObject, CameraUtils.Camera, out area);
            }
            Check(popup != null && popup.IsShown() && popup.Header.Contains("Combos"), "Add Combo opens a native modal with the target category");
            var categorySelector = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboCategory");
            Check((string)categorySelector.getSelection() == "Combos", "Creating a combo without a category uses Combos and asks for its name instead of deriving a category from the deck name");
            categorySelector.SelectOption("Value");
            Check((string)categorySelector.getSelection() == "Value", "Creating a combo can choose a different category through the native dropdown");
            categorySelector.SelectOption("AoE");
            ScreenEffectsHandle editorEffect = ScreenEffectsHandle.Active.Single(effect => effect.Owner == popup);
            Check(editorEffect.Parameters.Blur.Amount == 1f && editorEffect.Parameters.Blur.Radius == 1f,
                "The combo popup uses the native crafting-inspect blur parameters");
            popup.Show(false);
            Check(editorEffect.Starts == 1 && UniversalInputManager.Get().DialogCount == 1,
                "Repeated Show does not acquire another blur effect or native modal claim");
            Vector3 anchor = CameraUtils.Camera.WorldToScreenPoint(popup.AnchorPosition);
            Check(Math.Abs(anchor.x - Screen.width * .5f) < .01f && Math.Abs(anchor.y - Screen.height * .7f) < .01f,
                "Combo creation retries successfully and anchors to the viewport before the native game menu has ever opened");
            Check(popup.gameObject.activeInHierarchy,
                "Opening a native menu rig also activates its dynamic popup controller when native Show does not activate it");
            bool openingTween = popup.NativeOpeningAnimationPending;
            Vector3 openingScale = popup.NativeRig.localScale;
            // The original bug refit during the early, shrunken animation frame.
            // Reproduce that frame before allowing the native tween to finish;
            // no editor relayout may hide the subsequent expansion in this test.
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            popup.CompleteNativeOpeningAnimation();
            Time.unscaledTime += .5f;
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            foreach (UIBButton button in popup.Buttons)
                Check(VisibleControlArea(button, out Rect area)
                    && area.xMin >= 0 && area.xMax <= Screen.width && area.yMin >= 0 && area.yMax <= Screen.height,
                    "Popup geometry remains bounded after the time at which the native punch tween would have expanded the rig");
            Check(!openingTween && openingScale.Equals(Vector3.one),
                "Combo popup disables the native punch tween before Show can shrink the rig after virtual LayoutMenu");
            var definition = (ButtonListMenuDef)typeof(ButtonListMenu).GetField("m_menu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(popup);
            var unrelated = new GameObject("Native scene with fullscreen curtain");
            var curtain = new GameObject("Fullscreen brown curtain"); curtain.transform.SetParent(unrelated.transform, false);
            curtain.AddComponent<MeshRenderer>(); unrelated.SetActive(false);
            popup.Hide();
            definition.m_buttonContainer.transform.SetParent(unrelated.transform, true);
            definition.m_header.transform.SetParent(unrelated.transform, true);
            popup.Show(false);
            Check(!unrelated.activeSelf && !curtain.activeInHierarchy && popup.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "BCM_ComboProductInformation")
                && definition.m_buttonContainer.gameObject.activeInHierarchy && popup.gameObject.activeInHierarchy,
                "Popup controls become visible without activating their native ancestor or its fullscreen curtain");
            UnityEngine.Object.Destroy(unrelated);
            var editorAreas = new List<Rect>();
            foreach (UIBButton button in popup.Buttons)
            {
                Check(button.gameObject.activeInHierarchy && button.IsEnabled()
                    && VisibleControlArea(button, out Rect bounds)
                    && bounds.width > 0 && bounds.height > 0 && bounds.xMin >= 0 && bounds.xMax <= Screen.width
                    && bounds.yMin >= 0 && bounds.yMax <= Screen.height,
                    "Every editor button has enabled, visible geometry within the screen rather than only an open-state flag");
                VisibleControlArea(button, out Rect area);
                foreach (Rect earlier in editorAreas)
                {
                    Check(area.yMin >= earlier.yMax || area.yMax <= earlier.yMin || area.xMin >= earlier.xMax || area.xMax <= earlier.xMin,
                        "The native editor buttons do not overlap after fitting the real text geometry");
                }
                editorAreas.Add(area);
            }
            MethodInfo blocker = typeof(ComboPopup).GetMethod("OnBlockerRelease", BindingFlags.Instance | BindingFlags.NonPublic);
            blocker.Invoke(popup, new object[] { new UIEvent() });
            Check(popup.IsShown(), "The input release that opens the popup cannot immediately close its newly created blocker");
            foreach (var size in new[] { (320, 240), (480, 900), (1920, 1080) })
            {
                Screen.width = size.Item1; Screen.height = size.Item2;
                typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                foreach (UIBButton button in popup.Buttons)
                    Check(VisibleControlArea(button, out Rect area)
                        && area.xMin >= 0 && area.xMax <= Screen.width && area.yMin >= 0 && area.yMax <= Screen.height,
                        "The editor keeps actual visible controls on screen after resizing and its deferred native layout");
            }
            Transform parchment = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboParchment");
            foreach (float scale in new[] { .2f, 1f, 4f })
            {
                parchment.localScale = Vector3.one * scale;
                typeof(ComboPopup).GetMethod("LayoutMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                Check(ComboSceneGeometry.ScreenBounds(parchment.gameObject, CameraUtils.Camera, out Rect area)
                    && area.width < Screen.width * .65f && area.height < Screen.height * .90f
                    && area.width > Screen.width * .30f && area.height > Screen.height * .20f
                    && Math.Abs(area.center.x - Screen.width * .5f) < 1f && area.yMin > 0 && area.yMax < Screen.height,
                    "Native Product Information parchment fits the editor without distorting its border artwork");
                Vector3 stable = parchment.localScale;
                for (int pass = 0; pass < 12; pass++)
                    typeof(ComboPopup).GetMethod("LayoutMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                Check(Vector3.Distance(parchment.localScale, stable) < .001f,
                    "Repeated native panel layout keeps its corner scale stable");
            }
            Check(!popup.NativeBackground.GetComponent<Renderer>().enabled && !popup.NativeBorder.GetComponent<Renderer>().enabled,
                "The old brown menu background is suppressed when using the native Product Information parchment");
            GameObject backgroundObject = popup.NativeBackground.gameObject;
            MeshFilter sharedFrame = popup.gameObject.AddComponent<MeshFilter>(); sharedFrame.sharedMesh = new Mesh();
            MeshRenderer sharedRenderer = popup.gameObject.AddComponent<MeshRenderer>();
            Vector3 originalPosition = popup.transform.position, originalScale = popup.transform.localScale;
            try
            {
                // Native slice components can share a transform with controls.
                // Resizing their artwork must not fit that entire transform.
                popup.NativeBackground.gameObject = popup.gameObject;
                typeof(ComboPopup).GetMethod("LayoutMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                Check(popup.transform.position.Equals(originalPosition) && popup.transform.localScale.Equals(originalScale),
                    "A background on the menu's shared transform cannot scale or move the entire popup and its other artwork");
            }
            finally { popup.NativeBackground.gameObject = backgroundObject; sharedRenderer.enabled = false; sharedFrame.sharedMesh = null; }
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            Check(UniversalInputManager.Get().CurrentTextInput?.m_maxCharacters == 80
                && !UniversalInputManager.Get().CurrentTextInput.m_number, "Combo name uses a native text field without numeric input restrictions");
            UniversalInputManager.Get().UpdateNativeText("Equality Combo");
            UniversalInputManager.Get().CompleteNativeText("Equality Combo");
            UIBButton descriptionButton = popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboDescription");
            ComboSceneGeometry.ScreenBounds(descriptionButton.gameObject, CameraUtils.Camera, out Rect descriptionArea);
            descriptionButton.Click();
            Check(!UniversalInputManager.Get().CurrentTextInput.m_showBackground
                && UniversalInputManager.Get().CurrentTextInput.m_rect.height * Screen.height > UniversalInputManager.Get().NativeTextField.Field.textComponent.fontSize * 5f
                && UniversalInputManager.Get().NativeTextField.Field.lineType == HSInputField.LineType.MultiLineNewline
                && UniversalInputManager.Get().NativeTextField.Field.textComponent.fontSize >= 18
                && !UniversalInputManager.Get().NativeTextField.Field.textComponent.resizeTextForBestFit,
                "Description editing uses a large multiline native field with a readable fixed font rather than shrinking to a button caption");
            UniversalInputManager.Get().UpdateNativeText("Equality, then Consecration.");
            UniversalInputManager.Get().CompleteNativeText("Equality, then Consecration.");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            var combo = CategoryCatalog.Combos.Single();
            Check(!popup.IsShown() && !UniversalInputManager.Get().DialogActive && combo.Name == "Equality Combo"
                && combo.Description == "Equality, then Consecration." && combo.Cards.Single(c => c.Id == a.CardId).Count == 2,
                "Saving the native popup persists literal metadata and all captured card quantities and releases its modal claim");
            Check(!ScreenEffectsHandle.Active.Contains(editorEffect), "Saving an editor releases its native blur effect");
            var buckets = new Dictionary<int, List<CollectibleCard>> { [1] = new List<CollectibleCard> { a, b } };
            var append = ComboCollectionCards.Prepare(buckets); append();
            Check(buckets[1].Count == 2, "Combos never appear outside a selected category");
            ExtraFilterState.ToggleCategory(CuratedCategory.AoE);
            ExtraFilterState.OtherFilterMatch = card => card.CardId != b.CardId;
            ComboCollectionCards.Prepare(buckets)();
            Check(buckets[1].Count == 2, "Combo tiles do not bypass active filters that exclude a required component");
            ExtraFilterState.OtherFilterMatch = card => true;
            append = ComboCollectionCards.Prepare(buckets);
            buckets[1].Clear(); // Standalone components do not match the combo-only category.
            append();
            CollectibleCard proxy = buckets[1].Single();
            Check(!ReferenceEquals(proxy, a) && !ReferenceEquals(proxy, b) && ComboCollectionCards.StableId(proxy).Contains(combo.Id)
                && manager.GetAllCards().Count == 2 && a.OwnedCount == 2 && b.OwnedCount == 2,
                "Combo proxies occupy a native result slot without registering synthetic inventory cards or mutating component cards");
            var root = new GameObject("ComboVisual"); var visual = root.AddComponent<CollectionCardVisual>();
            var actor = AssetLoader.Get().InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
            actor.SetEntityDef(a.GetEntityDef()); visual.Actors = new CollectionCardActors(actor);
            string originalName = actor.NameText.Text, originalRules = actor.RulesText.Text;
            ComboCollectionCards.CaptureBinding(visual, new object[] { proxy });
            Check(ComboCollectionCards.Apply(visual) && actor.NameText.Text == combo.Name && actor.RulesText.Text == combo.Appearance.CardText,
                "The collection card renders the combo name and description through its private actor text without changing an EntityDef");
            Check(!ComboCollectionCards.PreventGrab(new object[] { visual }) && !CollectionClickInspect.TryInspect(visual),
                "Synthetic combo tiles cannot drag or route native single-card inspect/deck actions for their carrier card");
            var otherEffect = new ScreenEffectsHandle(new object());
            otherEffect.StartEffect(ScreenEffectParameters.BlurVignettePerspective);
            int startsBeforeInspect = editorEffect.Starts;
            int openingSounds = SoundManager.Get().Played.Count(sound => sound.StartsWith("Card_Transition_Out.prefab:"));
            var activePreviewFactory = AssetLoader.Get().MeshFactory;
            AssetLoader.Get().MeshFactory = () => {
                Mesh mesh = activePreviewFactory();
                // Native prefab pools can hand out an inactive actor. Its
                // modal owner must explicitly activate it before fitting.
                AssetLoader.Get().LastActor.gameObject.SetActive(false);
                return mesh;
            };
            bool native = ComboCollectionCards.HandleInput(visual, typeof(CollectionCardVisual).GetMethod("OnRightClick", BindingFlags.Instance | BindingFlags.NonPublic));
            AssetLoader.Get().MeshFactory = activePreviewFactory;
            Check(popup.GetComponentsInChildren<Actor>(false).Length == 2,
                "Inspect activates pooled component prefabs immediately instead of leaving an empty modal");
            Check(!native && ButtonListMenu.Visible is ComboPopup && ButtonListMenu.Visible.IsShown()
                && AssetLoader.Get().LastActor.Def.CardId != null,
                "Inspecting a combo opens its component-card modal and suppresses the carrier card's native action");
            ScreenEffectsHandle inspectEffect = ScreenEffectsHandle.Active.Single(effect => effect.Owner == popup);
            Check(inspectEffect.Starts == startsBeforeInspect + 1 && ScreenEffectsHandle.Active.Contains(otherEffect)
                && SoundManager.Get().Played.Count(sound => sound.StartsWith("Card_Transition_Out.prefab:")) == openingSounds + 1,
                "Opening inspect acquires its own effect and plays the exact native FinishBigCardMove sound once");
            int soundCount = SoundManager.Get().Played.Count;
            var beforeBoundary = popup.GetComponentsInChildren<Actor>(false);
            typeof(ComboPopup).GetMethod("ChangePage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, new object[] { 1 });
            Check(popup.GetComponentsInChildren<Actor>(false).SequenceEqual(beforeBoundary),
                "Paging past an inspect boundary leaves the existing actors and opening animation intact");
            Time.unscaledTime += ComboPopup.InspectZoomDuration;
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            Check(SoundManager.Get().Played.Count == soundCount && inspectEffect.Starts == startsBeforeInspect + 1,
                "Rebuilding pages and deferred layout neither replay the opening sound nor restart blur");
            Check(!popup.NativeBackground.GetComponent<Renderer>().enabled && !popup.NativeBorder.GetComponent<Renderer>().enabled
                && !popup.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "BCM_ComboParchment"),
                "Inspect renders component cards and controls over native blur without either popup background");
            Check(!ButtonListMenu.Visible.NativeOpeningAnimationPending,
                "Combo inspect also avoids the native opening tween while arranging its component cards");
            ButtonListMenu.Visible.CompleteNativeOpeningAnimation();
            var previews = ButtonListMenu.Visible.GetComponentsInChildren<Actor>(false);
            var strayEffect = Child(previews[0].transform, "NativeOffscreenEffect");
            var strayRenderer = strayEffect.AddComponent<MeshRenderer>();
            strayRenderer.UseLocalBounds = true;
            strayRenderer.LocalBounds = new Bounds(new Vector3(1000, 0, 1000), new Vector3(1000, .1f, 1000));
            previews[0].GetMeshRenderer().enabled = false;
            previews[0].gameObject.SetActive(false);
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            Check(previews[0].gameObject.activeInHierarchy && previews[0].GetMeshRenderer().enabled,
                "Inspect repairs a late native cached visibility reset without rebuilding its component actors");
            Camera camera = CameraUtils.FindProjectionCameraForObject(ButtonListMenu.Visible.gameObject);
            var areas = new List<Rect>();
            foreach (Actor preview in previews)
            {
                Check(ComboSceneGeometry.CardFaceBounds(preview, camera, out Rect area)
                    && area.xMin >= 0 && area.yMin >= 0 && area.xMax <= Screen.width && area.yMax <= Screen.height,
                    "The instantiated native card actors and their text actually fit the screen, beyond the abstract layout cells");
                foreach (Rect earlier in areas) Check(area.xMin >= earlier.xMax || area.xMax <= earlier.xMin
                    || area.yMin >= earlier.yMax || area.yMax <= earlier.yMin,
                    $"The measured native actor geometry never overlaps another component preview: {area.xMin},{area.yMin},{area.width},{area.height} vs {earlier.xMin},{earlier.yMin},{earlier.width},{earlier.height}");
                areas.Add(area);
            }
            Check(previews.Length == 2, "Inspect instantiates every component on a small combo page");
            Material previewMaterial = previews[0].GetMeshRenderer().sharedMaterial;
            Check(previewMaterial != originalMaterial && previewMaterial.renderQueue >= 3600 && originalMaterial.renderQueue == 2200,
                "Previews render above the modal using private materials without changing shared collectible assets");
            int previousStops = inspectEffect.Stops;
            MethodInfo disable = typeof(ComboPopup).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
            disable.Invoke(popup, null); disable.Invoke(popup, null);
            Check(inspectEffect.Stops == previousStops + 1 && !ScreenEffectsHandle.Active.Contains(inspectEffect)
                && ScreenEffectsHandle.Active.Contains(otherEffect),
                "Disabling the popup releases exactly its own blur even when the lifecycle callback repeats");
            ComboPopup.Close();
            Check(inspectEffect.Stops == previousStops + 1, "Hide does not release blur a second time after OnDisable");
            Check(!ScreenEffectsHandle.Active.Contains(inspectEffect) && ScreenEffectsHandle.Active.Contains(otherEffect)
                && SoundManager.Get().Played.Last().StartsWith("Card_Transition_In.prefab:"),
                "Closing inspect stops only its own effect and uses the native CancelCraftMode sound");
            soundCount = SoundManager.Get().Played.Count;
            ComboPopup.Close();
            Check(SoundManager.Get().Played.Count == soundCount && ScreenEffectsHandle.Active.Contains(otherEffect),
                "Repeated Close leaves other effects intact and does not replay the close sound");
            otherEffect.StopEffect();
            Check(previewMaterial.Destroyed, "Closing inspect releases its private render materials");
            Check(!UniversalInputManager.Get().DialogActive, "Closing combo inspect releases the native modal exactly once");
            ComboCollectionCards.CaptureBinding(visual, new object[] { a });
            Check(!ComboCollectionCards.IsCombo(visual) && actor.NameText.Text == originalName && actor.RulesText.Text == originalRules,
                "Reusing a native tile for an ordinary card restores its original captions and behavior");
            ExtraFilterState.ClearCategories(); append = ComboCollectionCards.Prepare(buckets); append();
            Check(buckets[1].Count == 0, "Refreshing without an active category removes presentation proxies from reusable native lists");
            CheckComboInspectLayouts();
            CheckComboSceneOrder();
            CheckComboNativeSkinGeometry();
            CheckComboDismissal(combo);
            CheckComboInspectPaging(combo);
            CheckComboDeckInput(proxy, a, b, combo);
            CheckComboEditing(combo, path);
            CheckComboVersions(combo);
            CheckComboOptions(combo, path);
            CheckComboPortraitMapping();
            CheckNativeComboBindings(proxy, a, combo);
            CheckPackages(a, b, path);
            CheckBuiltinPackageResults();
            CheckComboMembers(a, b, path);
            CheckComboCategories(path);
            CheckFolderEditors(a, b, path);
            CheckComboClassBuckets(a, b, combo);
        }
        finally
        {
            ComboCollectionCards.Reset(); ExtraFilterState.ClearAll();
            ExtraFilterState.OtherFilterMatch = card => true;
            CollectionManager.Instance = previousManager; UniversalInputManager.Instance = previousInput;
            PreferredQualityMode.IsEnabled = previousMode; AssetLoader.Get().MeshFactory = previousMesh; AssetLoader.Get().CreateRulesText = previousText;
            GameMenu.Get().m_menuBone = previousBone; GameMenu.Get().m_redButtonMaterial = previousRedButton; CameraUtils.Unavailable = previousCameraUnavailable;
            ButtonListMenu.NativeShowActivatesController = previousActivation;
            ButtonListMenu.SimulateNativeOpeningAnimation = previousAnimation; Time.unscaledTime = previousTime;
            Screen.width = width; Screen.height = height;
            foreach (string file in Directory.GetFiles(CategoryCatalog.DeckDirectory(path))) File.Delete(file);
            CategoryCatalog.LoadUserFile(path); Directory.Delete(directory, true);
        }
    }
    private static void CheckComboSceneOrder()
    {
        Camera camera = CameraUtils.Camera;
        float previousDepth = camera.DepthScale;
        var background = new GameObject("Opaque brown background");
        var control = new GameObject("Native control frame");
        try
        {
            camera.DepthScale = 1;
            MeshRenderer Art(GameObject root, int order)
            {
                root.transform.position = new Vector3(0, -511, 0);
                root.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(5, .2f, 2)) };
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sortingOrder = order; renderer.sortingLayerID = 7;
                renderer.sharedMaterial = new Material { renderQueue = 3000 };
                return renderer;
            }
            MeshRenderer brown = Art(background, 80), frame = Art(control, 0);
            var caption = new GameObject("Caption"); caption.transform.SetParent(control.transform, false);
            UberText text = caption.AddComponent<UberText>(); text.Text = "Save combo"; text.UpdateNow(true);
            ComboSceneGeometry.InFrontOf(control, background, camera);
            ComboSceneGeometry.TextInFront(control, camera);
            ComboSceneGeometry.DepthRange(background, camera, out float brownNear, out _);
            ComboSceneGeometry.DepthRange(control, camera, out float frameNear, out float controlFar);
            Check(controlFar < brownNear && frame.sortingLayerID == brown.sortingLayerID && frame.sortingOrder > brown.sortingOrder,
                "An active control occupies a plane and draw order in front of the opaque brown background");
            Check(camera.WorldToScreenPoint(text.transform.position).z < frameNear
                && CounterTextLayout.VisibleRenderers(text).All(renderer => renderer.sortingOrder > frame.sortingOrder)
                && text.RenderQueue < 1000,
                "Actual text output is in front of its native button frame using native sorting increments");
            float captionDepth = camera.WorldToScreenPoint(text.transform.position).z;
            for (int i = 0; i < 20; i++)
            {
                ComboSceneGeometry.InFrontOf(control, background, camera);
                ComboSceneGeometry.TextInFront(control, camera);
            }
            Check(Math.Abs(camera.WorldToScreenPoint(text.transform.position).z - captionDepth) < .001f
                && brown.sharedMaterial.renderQueue == 3000 && frame.sharedMaterial.renderQueue == 3000,
                "Repeated layout does not move captions toward the camera or mutate shared material queues");
        }
        finally { camera.DepthScale = previousDepth; UnityEngine.Object.Destroy(background); UnityEngine.Object.Destroy(control); }
    }
    private static void CheckComboInspectLayouts()
    {
        var pairStarts = Enumerable.Range(0, 3).Select(p => ComboInspectLayout.Arrange(2560, 1440, 5, p)).ToArray();
        Check(pairStarts.All(page => page.Count == 2) && pairStarts.Select(page => page.First).SequenceEqual(new[] { 0, 2, 3 }),
            "Five cards in pairs advance by two and then by one, keeping both slots filled on the last page");
        foreach (var screen in new[] { (320, 240), (480, 900), (1280, 720), (1920, 1080), (3840, 2160) })
        foreach (int count in new[] { 1, 2, 5, 30, 60 })
        {
            var visited = new HashSet<int>();
            var firstPage = ComboInspectLayout.Arrange(screen.Item1, screen.Item2, count, 0);
            int pages = firstPage.TotalPages;
            for (int p = 0; p < pages; p++)
            {
                var page = ComboInspectLayout.Arrange(screen.Item1, screen.Item2, count, p);
                Check(page.Count == firstPage.Count && page.Panel.Equals(firstPage.Panel) && page.Description.Equals(firstPage.Description),
                    "Every page keeps the same number of cards, panel and flavor position, including the last page");
                if (p == pages - 1) Check(page.First == Math.Max(0, count - firstPage.Count),
                    "The final page advances only far enough to reveal the remaining cards and fills earlier slots with overlap");
                Check(page.Panel.xMin >= 0 && page.Panel.yMin >= 0 && page.Panel.xMax <= screen.Item1 && page.Header.yMax <= screen.Item2
                    && page.Toolbar.yMin > page.Panel.yMin && page.Description.yMax < page.Panel.yMax,
                    "Compact inspect panels, descriptions and toolbars fit together inside the viewport");
                for (int i = 0; i < page.Count; i++)
                {
                    Rect card = page.Cards[i]; visited.Add(page.First + i);
                    Check(card.xMin >= 0 && card.yMin >= 0 && card.xMax <= screen.Item1 && card.yMax <= screen.Item2,
                        "Every large combo preview remains within the actual screen at every supported aspect ratio");
                    Check(card.xMin >= page.Panel.xMin && card.xMax <= page.Panel.xMax
                        && (card.xMax <= page.Description.xMin || card.yMin > page.Description.yMax)
                        && page.Description.yMin > page.Toolbar.yMax && card.yMax <= page.Panel.yMax + .01f,
                        "Component previews fit their compact panel without colliding with actions or the description");
                    if (screen.Item2 >= 720) Check(card.height >= Math.Min(400f, screen.Item2 * .5f), "Inspect uses large previews and pages instead of shrinking many cards to unreadable thumbnails");
                    for (int j = 0; j < i; j++) Check(card.xMin >= page.Cards[j].xMax || card.xMax <= page.Cards[j].xMin
                        || card.yMin >= page.Cards[j].yMax || card.yMax <= page.Cards[j].yMin, "Component card previews never overlap");
                }
            }
            Check(visited.SetEquals(Enumerable.Range(0, count)), "Paging reaches every combo component while allowing overlap on the final page");
        }
    }
}
