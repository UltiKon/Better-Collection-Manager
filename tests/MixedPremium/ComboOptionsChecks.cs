using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboOptions(CategoryCatalog.Combo original, string path)
    {
        var source = new EntityDef { CardId = original.Cards[0].Id, Rarity = TAG_RARITY.LEGENDARY };
        source.SetTag(GAME_TAG.CLASS, 4); source.SetTag(GAME_TAG.MULTIPLE_CLASSES, 7); source.SetTag(GAME_TAG.ATK, 99);
        source.SetTag(GAME_TAG.CARDRACE, (int)TAG_RACE.BEAST); source.SetTag(GAME_TAG.CARDRACE2, (int)TAG_RACE.DRAGON);
        foreach (string frame in CategoryCatalog.ComboAppearance.Frames)
        {
            var combo = new CategoryCatalog.Combo { Appearance = new CategoryCatalog.ComboAppearance { Frame = frame, Attack = 7, Health = 9, MinionType = TAG_RACE.MECHANICAL, SpellSchool = TAG_SPELL_SCHOOL.FIRE } };
            EntityDef definition = ComboCardTemplate.Definition(combo, source);
            Check(definition != source && definition.GetCardId() == source.GetCardId()
                && definition.GetTag(GAME_TAG.CARDTYPE) == (int)ComboCardTemplate.CardType(combo.Appearance)
                && definition.GetTag(GAME_TAG.CLASS) == (int)TAG_CLASS.NEUTRAL && definition.GetTag(GAME_TAG.MULTIPLE_CLASSES) == 0
                && definition.GetTag(GAME_TAG.RARITY) == (int)TAG_RARITY.FREE && definition.GetTag(GAME_TAG.HIDE_WATERMARK) == 1
                && definition.GetTag(GAME_TAG.ATK) == (combo.Appearance.HasAttack ? 7 : 0)
                && definition.GetTag(GAME_TAG.HEALTH) == (frame == "Minion" || frame == "Weapon" || frame == "Location" ? 9 : 0)
                && definition.GetTag(GAME_TAG.ARMOR) == (frame == "Hero" ? 9 : 0)
                && definition.GetTag(GAME_TAG.SPELL_SCHOOL) == (frame == "Spell" ? (int)TAG_SPELL_SCHOOL.FIRE : 0)
                && definition.GetTag(GAME_TAG.CARDRACE) == (frame == "Minion" ? (int)TAG_RACE.MECHANICAL : 0) && definition.GetTag(GAME_TAG.CARDRACE2) == 0
                && source.GetTag(GAME_TAG.CARDRACE) == (int)TAG_RACE.BEAST && source.GetTag(GAME_TAG.CARDRACE2) == (int)TAG_RACE.DRAGON
                && source.GetTag(GAME_TAG.RARITY) == (int)TAG_RARITY.LEGENDARY && source.GetTag(GAME_TAG.ATK) == 99,
                "Each selected frame uses an independent gemless neutral definition and preserves the inventory's original tags: " + frame);
            combo.Appearance.MinionType = (TAG_RACE)0;
            definition = ComboCardTemplate.Definition(combo, source);
            Check(definition.GetTag(GAME_TAG.CARDRACE) == 0 && definition.GetTag(GAME_TAG.CARDRACE2) == 0,
                "None clears both types inherited from dual-type artwork: " + frame);
        }
        var components = original.Cards.SelectMany(card => ComboCollectionCards.ComponentCards(original, card.Id)).Distinct().ToArray();
        var oldDefinitions = components.Select(card => card.Definition).ToArray();
        try
        {
            foreach (CollectibleCard component in components)
            {
                component.Definition = new EntityDef { CardId = component.CardId };
                bool first = ComboCollectionCards.ComponentCards(original, original.Cards[0].Id).Contains(component);
                component.Definition.SetTag(GAME_TAG.COST_BLOOD, first ? 2 : 1);
                component.Definition.SetTag(GAME_TAG.COST_FROST, first ? 0 : 1);
            }
            source.SetTag(GAME_TAG.COST_UNHOLY, 3);
            foreach (CollectibleCard artwork in components)
            {
                EntityDef definition = ComboCardTemplate.Definition(original, artwork.GetEntityDef());
                Check(definition.GetTag(GAME_TAG.COST_BLOOD) == 2 && definition.GetTag(GAME_TAG.COST_FROST) == 1
                    && definition.GetTag(GAME_TAG.COST_UNHOLY) == 0,
                    "Combo runes combine the colour maxima across members, independent of artwork and duplicate quantities");
            }
            EntityDef combined = ComboCardTemplate.Definition(original, source);
            Check(combined.GetTag(GAME_TAG.COST_UNHOLY) == 0 && source.GetTag(GAME_TAG.COST_UNHOLY) == 3,
                "Unrelated artwork runes are cleared without changing the original definition");
            var actor = new GameObject("Rune combo").AddComponent<Actor>();
            actor.SetEntityDef(combined);
            ComboCardTemplate.Prepare(actor, original.Appearance); actor.UpdateAllComponents();
            Check(actor.m_cardRuneBanner != null && actor.m_cardRuneBanner.gameObject.activeSelf
                && actor.m_cardRuneBanner.Current.Blood == 2 && actor.m_cardRuneBanner.Current.Frost == 1,
                "A frame without a rune banner borrows the native banner and shows the aggregate requirements");
            UnityEngine.Object.Destroy(actor.gameObject);
            foreach (CollectibleCard component in components) component.Definition.SetTag(GAME_TAG.COST_BLOOD, 0);
            foreach (CollectibleCard component in components) component.Definition.SetTag(GAME_TAG.COST_FROST, 0);
            combined = ComboCardTemplate.Definition(original, source);
            Check(combined.GetTag(GAME_TAG.COST_BLOOD) == 0 && combined.GetTag(GAME_TAG.COST_FROST) == 0 && combined.GetTag(GAME_TAG.COST_UNHOLY) == 0,
                "A combo with no rune requirements does not inherit an artwork banner");
        }
        finally { for (int i = 0; i < components.Length; i++) components[i].Definition = oldDefinitions[i]; }
        var optional = new GameObject("Minion with optional native elements").AddComponent<Actor>();
        try
        {
            var appearance = new CategoryCatalog.ComboAppearance { Frame = "Minion", Dragon = true, ShowAttack = true, ShowHealth = true };
            ComboCardTemplate.Prepare(optional, appearance); ComboCardTemplate.ApplyFrame(optional, appearance);
            Check(optional.m_attackObject?.activeSelf == true && optional.m_healthObject?.activeSelf == true
                && optional.m_eliteObject?.activeSelf == true && optional.GetAttackText() != null && optional.GetHealthText() != null,
                "Frames without their own stat gems or dragon can reuse those native minion elements");
            appearance.ShowAttack = appearance.ShowHealth = appearance.Dragon = false;
            ComboCardTemplate.ApplyFrame(optional, appearance);
            Check(!optional.m_attackObject.activeSelf && !optional.m_healthObject.activeSelf && !optional.m_eliteObject.activeSelf,
                "Optional stats and dragon can be hidden independently of the base card frame");
            optional.m_armorObject = new GameObject("Native hero armor"); optional.m_armorObject.transform.SetParent(optional.transform, false);
            optional.m_armorTextMesh = optional.m_armorObject.AddComponent<UberText>();
            appearance.Frame = "Hero"; appearance.ShowHealth = appearance.ShowAttack = true;
            ComboCardTemplate.ApplyFrame(optional, appearance);
            Check(optional.m_armorObject.activeSelf && !optional.m_attackObject.activeSelf && !optional.m_healthObject.activeSelf
                && ComboCardTemplate.SecondaryText(optional, appearance) == optional.m_armorTextMesh,
                "Heroes show the native armor element and caption without an attack or health gem");
            appearance.ShowHealth = false; ComboCardTemplate.ApplyFrame(optional, appearance);
            Check(!optional.m_armorObject.activeSelf && optional.m_armorTextMesh.isHidden(), "Disabling hero armor hides both its object and caption");
            appearance.Frame = "Spell"; appearance.ShowHealth = appearance.ShowAttack = true;
            ComboCardTemplate.ApplyFrame(optional, appearance);
            Check(!optional.m_attackObject.activeSelf && !optional.m_healthObject.activeSelf && !optional.m_armorObject.activeSelf,
                "Spell frames cannot reveal attack, health or armor even when those saved options were previously enabled");
        }
        finally { UnityEngine.Object.Destroy(optional.gameObject); }
        int width = Screen.width, height = Screen.height, originalFrame = Time.frameCount; float time = Time.unscaledTime;
        bool held = Input.LeftHeld, released = Input.LeftReleased;
        Vector3 pointer = Input.mousePosition;
        try
        {
            Screen.width = 1920; Screen.height = 1080;
            ComboPopup.Inspect(original); var popup = (ComboPopup)ButtonListMenu.Visible;
            Check(popup.Buttons.All(button => button.m_RootObject.name != "BCM_InspectStoneButton")
                && !popup.GetComponentsInChildren<CraftingButton>(true).Any() && !popup.GetComponentsInChildren<CraftingUI>(true).Any(),
                "Inspect retains complete native dialog buttons without the failed custom crafting skin");
            void BeginEdit() => popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            void Text(string id, string value)
            {
                popup.Buttons.Single(button => button.gameObject.name == "BCM_Combo" + id).Click();
                UniversalInputManager.Get().CompleteNativeText(value);
            }
            void Configure()
            {
                popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboFrame").SelectOption("Spell");
                popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboSpellSchool").SelectOption(TAG_SPELL_SCHOOL.FIRE);
                popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboFrame").SelectOption("Minion");
                popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboMinionType").SelectOption(TAG_RACE.MECHANICAL);
                foreach (string name in new[] { "BCM_ComboDragon", "BCM_ComboShowAttack", "BCM_ComboShowHealth" })
                    popup.GetComponentsInChildren<CheckBox>(false).Single(box => box.gameObject.name == name).Click();
                Text("CardText", "Card rules\nwith a second line."); Text("Description", "Inspect description only.");
                Text("Attack", "7"); Text("Health", "9"); Text("Mana", "6");
            }
            int inspectActionSounds = SoundManager.Get().Played.Count;
            BeginEdit();
            Check(popup.GetComponentsInChildren<UberText>(false).Any(label => label.Text == "Dragon Ornament")
                && popup.GetComponentsInChildren<UberText>(false).Any(label => label.Text == "Spell School"),
                "The editor names the ornament and initially offers spell school for the saved Spell frame");
            var frameSelector = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboFrame");
            foreach (string frame in CategoryCatalog.ComboAppearance.Frames)
            {
                frameSelector.SelectOption(frame);
                bool Active(string name) => popup.GetComponentsInChildren<Transform>(true).Single(node => node.gameObject.name == name).gameObject.activeInHierarchy;
                Check(Active("BCM_ComboMinionType") == (frame == "Minion") && Active("BCM_ComboSpellSchool") == (frame == "Spell")
                    && Active("BCM_ComboShowAttack") == (frame == "Minion" || frame == "Weapon")
                    && Active("BCM_ComboShowHealth") == (frame != "Spell")
                    && Active("BCM_ComboAttack") == (frame == "Minion" || frame == "Weapon")
                    && Active("BCM_ComboHealth") == (frame != "Spell"),
                    "Changing frames immediately shows only that type's dropdowns, checkboxes and value fields: " + frame);
                if (frame != "Spell") Check(popup.GetComponentsInChildren<CheckBox>(false).Single(box => box.gameObject.name == "BCM_ComboShowHealth").m_uberText.Text
                    == (frame == "Minion" ? "Health" : frame == "Hero" ? "Armor" : "Durability"), "The secondary stat is named for its card type: " + frame);
            }
            Check(SoundManager.Get().Played.Count == inspectActionSounds + 1,
                "Edit plays its single press sound without adding a card transition sound");
            Configure(); popup.Buttons.Single(button => button.gameObject.name == "BCM_CloseCombo").Click(); CompleteEditorExit(popup);
            Check(CategoryCatalog.Combos.Single().Appearance.Frame == original.Appearance.Frame
                && CategoryCatalog.Combos.Single().Appearance.MinionType == original.Appearance.MinionType
                && CategoryCatalog.Combos.Single().Appearance.SpellSchool == original.Appearance.SpellSchool
                && CategoryCatalog.Combos.Single().Appearance.ManaCost == original.Appearance.ManaCost
                && CategoryCatalog.Combos.Single().Appearance.CardText == original.Appearance.CardText,
                "Cancel discards dropdowns, checkboxes and values as well as text changes");
            BeginEdit(); Configure(); popup.Buttons.Single(button => button.gameObject.name == "BCM_SaveCombo").Click();
            CategoryCatalog.LoadUserFile(path); var saved = CategoryCatalog.Combos.Single();
            Check(saved.Id == original.Id && saved.Appearance.Frame == "Minion" && saved.Appearance.Dragon
                && saved.Appearance.MinionType == TAG_RACE.MECHANICAL
                && saved.Appearance.SpellSchool == TAG_SPELL_SCHOOL.FIRE
                && saved.Appearance.ShowAttack && saved.Appearance.Attack == 7 && saved.Appearance.ShowHealth && saved.Appearance.Health == 9
                && saved.Appearance.ManaCost == 6
                && saved.Appearance.CardText == "Card rules\nwith a second line." && saved.Description == "Inspect description only."
                && saved.ExactVersions == original.ExactVersions && saved.Cards.Select(card => (card.Id, card.Count)).SequenceEqual(original.Cards.Select(card => (card.Id, card.Count))),
                "The native editor saves all appearance options and separate texts without altering the captured cards or editions");
            Check(!popup.GetComponentsInChildren<UberText>(false).Any(label => label.gameObject.name == "BCM_ComboInspectTitle"),
                "Inspect has no duplicate name above the cards");
            BeginEdit();
            Camera camera = CameraUtils.Camera;
            GameObject panel = popup.GetComponentsInChildren<Transform>(false).Single(node => node.gameObject.name == "BCM_ComboProductInformation").gameObject;
            GameObject nameInput = popup.GetComponentsInChildren<Transform>(false).Single(node => node.gameObject.name == "BCM_ComboNameInput").gameObject;
            GameObject rulesInput = popup.GetComponentsInChildren<Transform>(false).Single(node => node.gameObject.name == "BCM_ComboCardTextInput").gameObject;
            DropdownControl frameControl = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboFrame");
            DropdownControl typeControl = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboMinionType");
            ComboSceneGeometry.ScreenBounds(panel, camera, out Rect panelArea);
            ComboSceneGeometry.ScreenBounds(nameInput, camera, out Rect nameArea);
            ComboSceneGeometry.ScreenBounds(popup.Buttons.Single(button => button.gameObject.name == "BCM_EditComboCards").gameObject, camera, out Rect cardsArea);
            ComboSceneGeometry.ScreenBounds(popup.Buttons.Single(button => button.gameObject.name == "BCM_ExportEntry").gameObject, camera, out Rect exportArea);
            ComboSceneGeometry.ScreenBounds(rulesInput, camera, out Rect rulesArea);
            ComboSceneGeometry.ScreenBounds(frameControl.gameObject, camera, out Rect frameArea);
            ComboSceneGeometry.ScreenBounds(popup.Buttons.Single(button => button.gameObject.name == "BCM_AddNewFolder").gameObject, camera, out Rect folderActionArea);
            GameObject manaInput = popup.GetComponentsInChildren<Transform>(false).Single(node => node.gameObject.name == "BCM_ComboManaInput").gameObject;
            ComboSceneGeometry.ScreenBounds(manaInput, camera, out Rect manaArea);
            Check(nameArea.xMax < manaArea.xMin && Math.Abs(nameArea.yMin - manaArea.yMin) < 1f && cardsArea.yMax < nameArea.yMin
                && exportArea.xMin > cardsArea.xMax && folderActionArea.xMin > exportArea.xMax
                && Math.Abs(cardsArea.height - exportArea.height) < 1f && Math.Abs(exportArea.height - folderActionArea.height) < 1f
                && Math.Abs(frameArea.xMin - rulesArea.xMin) < 1f,
                "Name and optional mana share a row above equally sized Cards, Export and New folder actions without overlap");
            Check((string)popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboFrame").getSelection() == "Minion"
                && (TAG_RACE)typeControl.getSelection() == TAG_RACE.MECHANICAL && typeControl.m_selectedItem.m_text.Text == "Mech"
                && popup.GetComponentsInChildren<CheckBox>(false).All(box => box.IsChecked()),
                "Reopening Edit preselects the saved frame and every saved checkbox");
            Check(typeControl.Items.Count == ExtraFilterCatalog.Races.Length + 1 && typeControl.Items.Contains((TAG_RACE)0)
                && typeControl.Items.Contains(TAG_RACE.ALL), "The type dropdown offers None, All and every catalog minion type");
            frameControl.onUserPressedButton(); typeControl.onUserPressedButton();
            Check(!frameControl.isMenuShown() && typeControl.isMenuShown(), "Only one appearance dropdown opens at a time");
            var blocker = new GameObject("Late high-queue editor surface"); blocker.transform.SetParent(typeControl.transform.parent.parent, false);
            blocker.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(1, .01f, 1)) };
            var blockingRenderer = blocker.AddComponent<MeshRenderer>();
            blockingRenderer.sharedMaterial = new Material { renderQueue = 4500 }; blockingRenderer.sortingOrder = 50;
            var row = new GameObject("Late native dropdown row"); row.transform.SetParent(typeControl.m_menu.transform, false);
            row.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(1, .01f, 1)) };
            var rowRenderer = row.AddComponent<MeshRenderer>(); var sourceMaterial = new Material { renderQueue = 2000 }; rowRenderer.sharedMaterial = sourceMaterial;
            var rowText = new GameObject("Late row text").AddComponent<UberText>(); rowText.transform.SetParent(row.transform, false); rowText.Text = "Mech";
            float depthScale = camera.DepthScale; camera.DepthScale = 1f;
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            Check(rowRenderer.sharedMaterial != sourceMaterial && sourceMaterial.renderQueue == 2000
                && rowRenderer.sharedMaterial.renderQueue > blockingRenderer.sharedMaterial.renderQueue
                && rowRenderer.sortingOrder > blockingRenderer.sortingOrder
                && CounterTextLayout.VisibleRenderers(rowText).All(output => output.sortingOrder > rowRenderer.sortingOrder),
                "Rows created after opening and their text render above every editor element, using private materials");
            ComboSceneGeometry.DepthRange(typeControl.m_menu.gameObject, camera, out _, out float menuFar);
            ComboSceneGeometry.DepthRange(blocker, camera, out float blockerNear, out _);
            Check(menuFar < blockerNear, "The open dropdown also clears the other controls' depth, keeping rows visible and clickable: menuFar=" + menuFar + ", blockerNear=" + blockerNear);
            camera.DepthScale = depthScale;
            UnityEngine.Object.Destroy(row); UnityEngine.Object.Destroy(blocker);
            typeControl.onUserCancelled();
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboCardText").Click();
            Input.ReturnPressed = true;
            Check(!UniversalInputManager.Get().NativeFinishKeys() && UniversalInputManager.Get().CurrentTextInput.m_multiLine,
                "Card text supports native multiline input and Enter just like the separate description");
            Input.ReturnPressed = false;
            var inline = UniversalInputManager.Get().CurrentTextInput;
            Check(inline.m_rect.width < .5f && inline.m_rect.height > .05f
                && popup.GetComponentsInChildren<DropdownControl>(false).Any(control => control.gameObject.name == "BCM_ComboFrame")
                && !popup.Buttons.Any(button => button.gameObject.name == "BCM_ComboEditorBack"),
                "Large text fields edit inline while frame controls remain visible without opening an extra view");
            UniversalInputManager.Get().UpdateNativeText("Draft kept when returning to options");
            foreach (var size in new[] { (480, 900), (1920, 1080) })
            {
                Screen.width = size.Item1; Screen.height = size.Item2;
                typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
                var input = UniversalInputManager.Get().CurrentTextInput;
                Check(input.m_text == "Draft kept when returning to options" && input.m_rect.width > 0
                    && input.m_rect.height > 0 && input.m_rect.xMin >= 0 && input.m_rect.yMin >= 0
                    && input.m_rect.xMax <= 1 && input.m_rect.yMax <= 1,
                    "A focused native text editor preserves its complete draft and remains inside the panel after portrait/landscape resizing");
            }
            UniversalInputManager.Get().CompleteNativeText("Draft kept when returning to options");
            var draft = (CategoryCatalog.ComboAppearance)typeof(ComboPopup).GetField("m_appearance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(popup);
            Text("Mana", "0");
            Check(draft.ManaCost == 0, "The mana editor accepts an explicit zero without falling back to the sum");
            Text("Mana", "");
            Check(draft.ManaCost == null && popup.GetComponentsInChildren<UberText>(false).Any(label => label.Text == "Sum"),
                "Clearing custom mana restores the sum fallback and its placeholder");
            Text("Mana", "-1");
            Check(draft.ManaCost == null, "Negative custom mana cannot replace the sum fallback");
            Check(popup.GetComponentsInChildren<DropdownControl>(false).Any(control => control.gameObject.name == "BCM_ComboFrame")
                && draft.CardText == "Draft kept when returning to options",
                "Finishing inline editing retains the draft and frame controls");
            Check(!popup.GetComponentsInChildren<UberText>(false).Any(label => label.Text.Contains("/2000") || label.Text.Contains("(inspect)")),
                "The editor omits character counters and the redundant inspect suffix");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboFrame").onUserPressedButton();
            Check(!UniversalInputManager.Get().IsTextInputActive(), "Opening a frame dropdown finishes the current text field");
            popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboFrame").onUserCancelled();
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboName").Click();
            typeControl.onUserPressedButton();
            Check(!UniversalInputManager.Get().IsTextInputActive(), "Opening the minion type dropdown also finishes the current text field");
            typeControl.onUserCancelled();
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ComboDescription").Click();
            UniversalInputManager.Get().UpdateNativeText("Outside click discards this draft");
            Time.frameCount += 3; Input.mousePosition = Vector3.zero; Input.LeftReleased = false;
            Input.LeftPressed = Input.LeftHeld = true;
            int outsideSounds = SoundManager.Get().Played.Count;
            typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            Check(SoundManager.Get().Played.Count == outsideSounds && popup.IsShown(),
                "Editor outside presses stay silent until the dialog actually closes");
            Input.LeftPressed = Input.LeftHeld = false; Input.LeftReleased = true; Time.frameCount++;
            typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            Check(popup.IsShown() && UniversalInputManager.Get().IsTextInputActive(),
                "An editor off-click waits for release dispatch instead of letting the click fall through to collection cards");
            typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            CompleteEditorExit(popup);
            Check(SoundManager.Get().Played.Count == outsideSounds + 1
                && SoundManager.Get().Played.Last().StartsWith("Small_Click"), "Editor off-click returns with one regular click and no card-transition sound");
            Check(popup.IsShown() && popup.Buttons.Any(button => button.gameObject.name == "BCM_EditCombo")
                && !UniversalInputManager.Get().IsTextInputActive()
                && CategoryCatalog.Combos.Single(value => value.Id == original.Id).Description == saved.Description,
                "Clicking outside the text editor returns to inspect and discards all unsaved edits");
            Input.LeftReleased = false;
            popup.Hide();
            var large = new CategoryCatalog.Combo { Id = "paged", Name = original.Name, Description = "", Appearance = saved.Appearance,
                Cards = Enumerable.Repeat(original.Cards[0], 30).ToArray() };
            ComboPopup.Inspect(large); popup = (ComboPopup)ButtonListMenu.Visible;
            var next = popup.GetComponentsInChildren<Transform>(false).Single(node => node.gameObject.name == "BCM_ComboNextArrow");
            Check(next.GetComponent<UIBButton>() != null && next.GetComponent<PagingArrow>() == null && popup.Buttons.Count == 3
                && !popup.GetComponentsInChildren<Transform>(false).Any(node => node.gameObject.name == "BCM_ComboPreviousArrow"),
                "Paged inspect uses the native arrow asset and hides the previous arrow on its first page");
            int sounds = SoundManager.Get().Played.Count;
            next.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == sounds + 1 && SoundManager.Get().Played[sounds] == CollectionFilterMenu.ButtonPressSound,
                "The paging arrow uses the ordinary click on press without a metallic deck-selection sound");
            next.GetComponent<PegUIElement>().Send(UIEventType.RELEASE);
            var pageField = typeof(ComboPopup).GetField("m_page", BindingFlags.Instance | BindingFlags.NonPublic);
            Check((int)pageField.GetValue(popup) == 1 && SoundManager.Get().Played.Count == sounds + 1
                && popup.IsShown(),
                "A native triangle release turns exactly one page and triggers one click sound");
            Time.unscaledTime += .13f;
            typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            Check(next.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.enabled) && SoundManager.Get().Played.Count == sounds + 1,
                "The native triangle stays visible without closing inspect or replaying its opening sound");
        }
        finally
        {
            Input.ReturnPressed = false; Input.LeftHeld = held; Input.LeftReleased = released; Input.mousePosition = pointer;
            ComboPopup.Close(); Screen.width = width; Screen.height = height; Time.unscaledTime = time; Time.frameCount = originalFrame;
            CategoryCatalog.UpdateCombo(original.Id, original.Name, original.Description, original.Appearance);
        }
    }
}
