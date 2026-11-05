using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CompleteEditorExit(ComboPopup popup)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var motion = (ComboEditorExitMotion)typeof(ComboPopup).GetField("m_editorExit", flags).GetValue(popup);
        if (motion == null) return;
        Time.unscaledTime += .2f;
        typeof(ComboEditorExitMotion).GetMethod("LateUpdate", flags).Invoke(motion, null);
    }
    private static void CheckEditorMotion(ComboPopup popup, CategoryCatalog.Combo original)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var menu = (ButtonListMenuDef)typeof(ButtonListMenu).GetField("m_menu", flags).GetValue(popup);
        Transform content = menu.m_buttonContainer.transform, parent = content.parent;
        var exitField = typeof(ComboPopup).GetField("m_editorExit", flags);
        var update = typeof(ComboPopup).GetMethod("Update", flags);
        var late = typeof(ComboPopup).GetMethod("LateUpdate", flags);
        bool down = Input.LeftPressed, held = Input.LeftHeld, up = Input.LeftReleased;
        try
        {
            Input.LeftPressed = Input.LeftHeld = Input.LeftReleased = false;
            float started = Time.unscaledTime, initial = content.localScale.x;
            float largest = initial;
            foreach (float seconds in new[] { .03f, .09f, .16f, .24f })
            {
                Time.unscaledTime = started + seconds; Time.frameCount++;
                update.Invoke(popup, null); late.Invoke(popup, null);
                largest = Math.Max(largest, content.localScale.x);
                foreach (UIBButton button in popup.Buttons.Where(button => button.gameObject.name == "BCM_SaveCombo" || button.gameObject.name == "BCM_CloseCombo"))
                {
                    ComboSceneGeometry.ScreenBounds(button.gameObject, CameraUtils.Camera, out Rect area);
                    Check(area.xMin >= 0 && area.xMax <= Screen.width && area.yMin >= 0 && area.yMax <= Screen.height,
                        "Editor opening animation stays on screen while late layout continues at the final scale");
                }
            }
            Check(largest > initial && Math.Abs(content.localScale.x - initial / .92f) < .001f,
                "Editor opening grows through a small punch and settles at the exact fitted scale without compensating for its animated size");
            Vector3 fitted = content.localScale;
            popup.Hide();
            var closing = (ComboEditorExitMotion)exitField.GetValue(popup);
            Check(closing != null && !popup.IsShown() && !UniversalInputManager.Get().DialogActive && content.gameObject.activeInHierarchy
                && content.GetComponentsInChildren<Collider>(true).All(hit => !hit.enabled),
                "Closing releases modal input immediately while the visible editor shrinks with all hitboxes disabled");
            Time.unscaledTime += .08f;
            typeof(ComboEditorExitMotion).GetMethod("LateUpdate", flags).Invoke(closing, null);
            Check(content.localScale.x < fitted.x && content.localScale.x > 0,
                "Editor exit has visible intermediate frames rather than deactivating immediately");
            Time.unscaledTime += .2f;
            typeof(ComboEditorExitMotion).GetMethod("LateUpdate", flags).Invoke(closing, null);
            Check(exitField.GetValue(popup) == null && content.parent == parent && !content.gameObject.activeInHierarchy,
                "Exit completion restores the inactive hierarchy and leaves no detached editor artwork");
            ComboPopup.Inspect(original); popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            popup.Hide(); closing = (ComboEditorExitMotion)exitField.GetValue(popup);
            ComboPopup.Inspect(original);
            Check(exitField.GetValue(popup) == null && closing.gameObject.Destroyed && content.parent == parent,
                "Reopening during exit cancels the old animation before reusing its controls");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            popup.Buttons.Single(button => button.gameObject.name == "BCM_CloseCombo").Click();
            UniversalInputManager.Get().SetGameDialogActive(true);
            CompleteEditorExit(popup);
            Check(!popup.IsShown() && UniversalInputManager.Get().DialogActive,
                "A pending return to inspect never opens over a different dialog or releases its input claim");
            UniversalInputManager.Get().SetGameDialogActive(false);
            ComboPopup.Inspect(original); popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            Time.unscaledTime += .25f; late.Invoke(popup, null);
        }
        finally { Input.LeftPressed = down; Input.LeftHeld = held; Input.LeftReleased = up; }
    }
    private static void CheckComboEditing(CategoryCatalog.Combo original, string path)
    {
        CollectionManager manager = CollectionManager.Get();
        var deck = manager.EditedDeck; bool editing = manager.Editing;
        int width = Screen.width, height = Screen.height;
        try
        {
            manager.EditedDeck = null; manager.Editing = false;
            ComboPopup.Inspect(original);
            var popup = (ComboPopup)ButtonListMenu.Visible;
            Check(popup.Buttons.Any(button => button.gameObject.name == "BCM_EditCombo"),
                "Combo inspect exposes an Edit action without requiring a deck to be open");
            foreach (var size in new[] { (320, 240), (480, 900), (1920, 1080) })
            {
                Screen.width = size.Item1; Screen.height = size.Item2;
                typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                var rects = popup.Buttons.Select(button => { ComboSceneGeometry.ScreenBounds(button.gameObject, CameraUtils.Camera, out Rect area); return area; }).ToArray();
                Check(rects.Length == 3
                    && rects.All(rect => rect.xMin >= 0 && rect.xMax <= Screen.width)
                    && rects.Zip(rects.Skip(1), (a, b) => a.xMax <= b.xMin).All(value => value),
                    "Inspect shows only the necessary actions and keeps them on screen without overlap at every resolution");
            }
            Check(!popup.Header.Contains("1/1") && popup.Buttons.Count == 3
                && !popup.ActiveButtons.Any(button => button.gameObject.name == "BCM_PreviousComboCards" || button.gameObject.name == "BCM_NextComboCards")
                && !popup.NativeBackground.GetComponent<Renderer>().enabled && !popup.NativeBorder.GetComponent<Renderer>().enabled
                && !popup.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "BCM_ComboParchment"),
                "Inspect has no opaque panel and omits redundant page controls and the 1/1 suffix");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ExportEntry").Click();
            Check(UnityEngine.GUIUtility.systemCopyBuffer == CategoryCatalog.ExportTransfer(false, entryId: original.Id),
                "Inspect exports only its saved combo through the clipboard");
            void EditText(string id, string text)
            {
                popup.Buttons.Single(button => button.gameObject.name == id).Click();
                UniversalInputManager.Get().CompleteNativeText(text);
            }
            popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            CheckEditorMotion(popup, original);
            CheckComboButtonHitAreas(popup);
            foreach (UIBButton field in popup.Buttons.Where(button => button.gameObject.name == "BCM_ComboName"
                || button.gameObject.name == "BCM_ComboDescription"))
            {
                Transform artwork = field.m_RootObject.transform;
                Quaternion rotation = artwork.localRotation; Vector3 position = artwork.localPosition;
                for (int pass = 0; pass < 3; pass++) field.Hover();
                Check(artwork.localRotation.Equals(rotation) && artwork.localPosition.Equals(position)
                    && !UniversalInputManager.Get().IsTextInputActive(),
                    "Hovering a combo field leaves its large artwork on the UI plane before native text input is active");
                Check(field.m_ClickDownOffset.sqrMagnitude == 0f && !field.m_AnimateFlip && !field.m_WigglePostFlip,
                    "Combo fields also disable click-depth movement and follow-up flip animations");
            }
            var saveAction = popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo");
            Check(saveAction.m_WiggleTime > 0f && saveAction.m_WiggleAmount.sqrMagnitude > 0f,
                "Save retains the native action-button hover animation independently of the editor fields");
            string longDescription = string.Join("\n", Enumerable.Repeat("First cast Equality. Then cast Consecration. Keep all captured copies.", 20));
            var description = popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboDescription");
            var parchment = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboParchment");
            Vector3 paperScale = parchment.localScale;
            var inputSurface = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboDescriptionInput");
            Renderer inputBackground = inputSurface.gameObject.GetComponentsInChildren<Renderer>(false).Single(renderer => renderer.enabled);
            ComboSceneGeometry.ScreenBounds(inputSurface.gameObject, CameraUtils.Camera, out Rect inputArea);
            Check(inputBackground.gameObject.name == "BCM_ComboDescriptionInput" && inputBackground.sharedMaterial.renderQueue > 4600
                && AssetLoader.Get().QuantityPrefab.GetComponent<StoreQuantityPrompt>().m_root.GetComponentsInChildren<Renderer>(true)
                    .All(renderer => renderer.sharedMaterial.renderQueue == 4300),
                "The editor reuses the native dark input artwork with private draw priority and leaves the source asset unchanged");
            Check(inputSurface.gameObject.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.gameObject.name == "Pack icon" || renderer.gameObject.name == "Outer frame").All(renderer => !renderer.enabled),
                "Quantity-specific icons, instructions and the surrounding quantity dialog are excluded from text fields");
            Check(saveAction.Template == AssetLoader.Get().ProductInfoPrefab.GetComponent<StoreSendToBAM>().m_okayButton,
                "Save is cloned from the native Product Information confirmation button");
            foreach (UIBButton action in new[] { saveAction, popup.Buttons.Single(button => button.gameObject.name == "BCM_CloseCombo") })
            {
                Quaternion relative = Quaternion.Inverse(CameraUtils.Camera.transform.rotation) * action.m_ButtonText.transform.rotation;
                Check(Math.Abs(relative.x) + Math.Abs(relative.y) + Math.Abs(relative.z) < .00001f,
                    "Editor buttons from the Product Information template remain upright after fitting");
            }
            foreach (UberText value in popup.GetComponentsInChildren<UberText>(false)
                .Where(label => label.gameObject.name == "BCM_ComboNameDisplay" || label.gameObject.name == "BCM_ComboDescriptionDisplay"))
                Check(value.RenderQueue >= AssetLoader.Get().ProductInfoPrefab.GetComponent<StoreSendToBAM>().m_messageText.RenderQueue
                    && CounterTextLayout.VisibleRenderers(value).All(renderer => renderer.sharedMaterials.All(material => material.renderQueue > 4600)),
                    "Both saved field values inherit native priority and actually render above the opaque paper material");
            Check(popup.GetComponentsInChildren<UberText>(false)
                .Where(label => label.gameObject.name == "BCM_ComboNameDisplay" || label.gameObject.name == "BCM_ComboDescriptionDisplay")
                .All(label => label.TextColor.a == 1f && label.TextColor.r < .3f),
                "Both inactive field values use opaque dark text on their light parchment input surfaces");
            description.Hover();
            ComboSceneGeometry.ScreenBounds(inputSurface.gameObject, CameraUtils.Camera, out Rect hoveredArea);
            Check(Near(inputArea.xMin, hoveredArea.xMin) && Near(inputArea.yMin, hoveredArea.yMin)
                && Near(inputArea.width, hoveredArea.width) && Near(inputArea.height, hoveredArea.height),
                "Hovering the description cannot rotate, depress or resize the native input surface");
            Check(description.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled)
                && parchment.localScale.Equals(paperScale),
                "Hover uses stable native parchment and cannot reveal the hidden action-button skin");
            description.Click(); UniversalInputManager.Get().UpdateNativeText(longDescription);
            Check(parchment.localScale.Equals(paperScale)
                && UniversalInputManager.Get().CurrentTextInput.m_orderInLayer > 0,
                "Native text input is drawn above the original parchment without creating a custom field mesh");
            int editingFont = UniversalInputManager.Get().NativeTextField.Field.textComponent.fontSize;
            for (int pass = 0; pass < 5; pass++)
            {
                UniversalInputManager.Get().NativeGuiPass();
                Check(UniversalInputManager.Get().CurrentTextInput.m_multiLine && editingFont >= 16 && editingFont <= 32
                    && UniversalInputManager.Get().NativeTextField.Field.textComponent.fontSize == editingFont,
                    "The native per-GUI-pass font recalculation cannot enlarge a multiline combo editor to the rectangle's height");
            }
            Input.ReturnPressed = true;
            Check(!UniversalInputManager.Get().NativeFinishKeys(), "Enter remains available to the multiline field rather than completing the description");
            Input.ReturnPressed = false; Input.KeypadEnterPressed = true;
            Check(!UniversalInputManager.Get().NativeFinishKeys(), "Keypad Enter also remains a description newline");
            Input.KeypadEnterPressed = false; Input.EscapePressed = true;
            Check(UniversalInputManager.Get().NativeFinishKeys(), "Escape still follows the native input cancellation path");
            Input.EscapePressed = false;
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            Check(UniversalInputManager.Get().NativeTextField.Field.lineType == HSInputField.LineType.SingleLine,
                "Switching from description to name restores single-line native editing");
            Input.ReturnPressed = true;
            Check(UniversalInputManager.Get().NativeFinishKeys(), "Enter still completes a single-line combo name");
            Input.ReturnPressed = false;
            UniversalInputManager.Get().CompleteNativeText(original.Name);
            description.Click();
            Check(UniversalInputManager.Get().CurrentTextInput.m_text == longDescription,
                "Changing fields preserves the full multiline description independently of its inactive excerpt");
            foreach (var size in new[] { (1280, 720), (1920, 1080) })
            {
                Screen.width = size.Item1; Screen.height = size.Item2;
                typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                var input = UniversalInputManager.Get().CurrentTextInput;
                Check(input != null && input.m_text == longDescription && input.m_rect.height * Screen.height > UniversalInputManager.Get().NativeTextField.Field.textComponent.fontSize * 5f && input.m_rect.width > .15f
                    && input.m_alignment == TextAnchor.UpperLeft
                    && UniversalInputManager.Get().NativeTextField.Field.textComponent.fontSize >= 16,
                    "Resizing an active description preserves all text and refits a large multiline editor with a readable font");
            }
            UniversalInputManager.Get().CompleteNativeText(longDescription);
            var display = popup.GetComponentsInChildren<UberText>(false).Single(label => label.gameObject.name == "BCM_ComboDescriptionDisplay");
            Vector3 stableScale = display.transform.lossyScale;
            for (int pass = 0; pass < 8; pass++)
            {
                // Native button-caption resizing and render-to-texture surfaces
                // must not affect the independent paragraph's typography.
                description.m_ButtonText.transform.localScale *= .1f;
                typeof(ComboPopup).GetMethod("LayoutMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                Check(Vector3.Distance(stableScale, display.transform.lossyScale) < .001f && display.transform.parent != description.transform,
                    "Repeated native button layouts cannot shrink or resize the independent description preview");
            }
            description.m_ButtonText.Show();
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            Check(description.m_ButtonText.isHidden() && !description.m_ButtonText.RenderOnObject.GetComponent<Renderer>().enabled,
                "Late native caption surfaces are suppressed so stale button textures cannot cover the fields with black rectangles");
            Check(popup.GetComponentsInChildren<UberText>(false).Any(label => label.gameObject.name == "BCM_ComboDescriptionDisplay"
                    && label.WordWrap && !label.ResizeToFit && !label.ResizeToFitAndGrow && label.Text.Length < longDescription.Length)
                && popup.GetComponentsInChildren<UberText>(false).Any(label => label.gameObject.name == "BCM_ComboDescriptionLabel" && label.Text == "Description"),
                "Long inactive descriptions wrap a readable excerpt beneath a separate label rather than shrinking the whole field");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            Check(UniversalInputManager.Get().CurrentTextInput.m_text == original.Name,
                "Editing pre-fills the existing combo name in its native input field");
            UniversalInputManager.Get().CompleteNativeText("Canceled rename");
            EditText("BCM_ComboDescription", "Canceled description");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_CloseCombo").Click();
            Check(!popup.IsShown() && popup.GetComponentsInChildren<Actor>(false).Length == 0,
                "Cancel finishes the editor exit before bringing back combo inspect");
            CompleteEditorExit(popup);
            Check(popup.IsShown() && popup.Buttons.Any(button => button.gameObject.name == "BCM_EditCombo")
                && CategoryCatalog.Combos.Single().Name == original.Name && CategoryCatalog.Combos.Single().Description == original.Description,
                "Cancel returns to inspect and leaves persisted combo metadata unchanged");
            var otherOwner = new GameObject("Other native input owner");
            UniversalInputManager.Get().UseTextInput(new UniversalInputManager.TextInputParams { m_owner = otherOwner,
                m_rect = new Rect(0, 0, .5f, .5f) }, true);
            UniversalInputManager.Get().NativeGuiPass(); Input.ReturnPressed = true;
            Check(UniversalInputManager.Get().NativeTextField.Field.textComponent.fontSize == 96
                && UniversalInputManager.Get().NativeFinishKeys(),
                "Combo hooks leave another native input owner's font and Enter completion unchanged");
            Input.ReturnPressed = false; UniversalInputManager.Get().CancelTextInput(otherOwner); UnityEngine.Object.Destroy(otherOwner);
            popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            EditText("BCM_ComboName", "Renamed Combo"); EditText("BCM_ComboDescription", "Updated description.\nSame cards.");
            var categorySelector = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboCategory");
            Check((string)categorySelector.getSelection() == CategoryCatalog.Categories.Single(category => category.Value == original.Category).Name,
                "Editing initializes the category selector with the combo's saved category");
            categorySelector.SelectOption("Value");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            var changed = CategoryCatalog.Combos.Single();
            Check(popup.IsShown() && popup.Buttons.Any(button => button.gameObject.name == "BCM_EditCombo")
                && changed.Id == original.Id && changed.Category == CuratedCategory.Value && changed.Name == "Renamed Combo"
                && changed.Description == "Updated description.\nSame cards." && changed.ExactVersions == original.ExactVersions
                && changed.Cards.Select(card => (card.Id, card.Count)).SequenceEqual(original.Cards.Select(card => (card.Id, card.Count))),
                "Saving an edit moves the combo to its chosen category while retaining identity, exact editions and all quantities");
            CategoryCatalog.LoadUserFile(path);
            Check(CategoryCatalog.Combos.Single().Id == original.Id && CategoryCatalog.Combos.Single().Name == "Renamed Combo" && CategoryCatalog.Combos.Single().Category == CuratedCategory.Value,
                "Combo edits survive replaying the persistent category files without duplicating the entry");
        }
        finally
        {
            Input.ReturnPressed = Input.KeypadEnterPressed = Input.EscapePressed = false;
            ComboPopup.Close(); CategoryCatalog.UpdateCombo(original.Id, original.Name, original.Description, categoryName: CategoryCatalog.ComboCategories.Single(category => category.Value == original.Category).Name);
            manager.EditedDeck = deck; manager.Editing = editing;
            Screen.width = width; Screen.height = height;
        }
    }

    private static void CheckComboButtonHitAreas(ComboPopup popup)
    {
        int width = Screen.width, height = Screen.height;
        try
        {
            foreach (var size in new[] { (1920, 1080), (480, 900), (320, 240) })
            {
                Screen.width = size.Item1; Screen.height = size.Item2;
                typeof(ComboPopup).GetMethod("LayoutMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                foreach (UIBButton button in popup.Buttons.Where(action => action.gameObject.name == "BCM_SaveCombo"
                    || action.gameObject.name == "BCM_CloseCombo"))
                {
                    BoxCollider hit = button.GetComponent<BoxCollider>();
                    Check(hit != null && hit.enabled, "Save and Cancel have fitted native input colliders after editor layout");
                    MeshFilter face = button.m_RootObject.GetComponent<MeshFilter>();
                    Bounds expected = FilterTrayGeometry.TransformBounds(face.sharedMesh.bounds,
                        p => button.transform.InverseTransformPoint(face.transform.TransformPoint(p)));
                    Check((hit.center - expected.center).sqrMagnitude < .000001f
                        && Near(hit.size.x, expected.size.x) && Near(hit.size.z, expected.size.z),
                        "Save and Cancel hit boxes follow their visible artwork at wide, portrait and small resolutions");

                    Vector3 position = button.transform.position, scale = button.transform.localScale;
                    Quaternion rotation = button.transform.rotation;
                    Vector3 artPosition = face.transform.localPosition, artScale = face.transform.localScale;
                    Quaternion artRotation = face.transform.localRotation;
                    var inherited = new GameObject("Stale template hit"); inherited.transform.SetParent(button.transform, false);
                    BoxCollider stale = inherited.AddComponent<BoxCollider>(); stale.size = new Vector3(100, 100, 100);
                    hit.center += new Vector3(3, 0, 2); hit.size *= 4f;
                    ComboSceneGeometry.FitButtonHitArea(button);
                    Check(hit == button.GetComponent<BoxCollider>() && !stale.enabled
                        && (hit.center - expected.center).sqrMagnitude < .000001f
                        && Near(hit.size.x, expected.size.x) && Near(hit.size.z, expected.size.z),
                        "Refitting repairs an offset template hit and disables inherited input outside the button");
                    Check(button.transform.position.Equals(position) && button.transform.localScale.Equals(scale)
                        && button.transform.rotation.Equals(rotation) && face.transform.localPosition.Equals(artPosition)
                        && face.transform.localScale.Equals(artScale) && face.transform.localRotation.Equals(artRotation),
                        "Correcting button hit boxes leaves visible button position, size and rotation untouched");
                    UnityEngine.Object.Destroy(inherited);
                }
            }
        }
        finally
        {
            Screen.width = width; Screen.height = height;
            typeof(ComboPopup).GetMethod("LayoutMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
        }
    }
}
