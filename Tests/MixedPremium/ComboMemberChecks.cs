using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboMembers(CollectibleCard a, CollectibleCard b, string path)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var manager = CollectionManager.Get(); bool editing = manager.Editing;
        int screenWidth = Screen.width, screenHeight = Screen.height;
        var deckBefore = manager.EditedDeck.Slots.ToArray(); var previousResources = Resources.Objects;
        var sourceBar = MakeSettingsScrollbarTemplate();
        var createdIds = new List<string>(); var fixtures = new List<CollectibleCard>();
        const string token = "TEST_MEMBER_TOKEN";
        ComboPopup popup = null;
        UIBButton Button(string id) => popup.Buttons.FirstOrDefault(button => button.gameObject.name == id) ?? throw new Exception("Missing button " + id + " in " + popup.Header);
        void Invoke(string method) => typeof(ComboPopup).GetMethod(method, flags).Invoke(popup, null);
        void Frame(Vector3 pointer, bool down = false, bool held = false, bool up = false)
        {
            Input.mousePosition = pointer; Input.LeftPressed = down; Input.LeftHeld = held; Input.LeftReleased = up;
            Time.frameCount++; Invoke("Update"); Invoke("LateUpdate");
        }
        string[] Rows() => popup.GetComponentsInChildren<UberText>(false)
            .Where(text => text.gameObject.name.StartsWith("BCM_ComboMemberLabel")).Select(text => text.Text).ToArray();
        void Search(string query)
        {
            Button("BCM_ComboDescription").Click(); UniversalInputManager.Get().CompleteNativeText(query); Invoke("LateUpdate");
        }
        void Change(string id, int delta)
        {
            Search(id); Button(delta > 0 ? "BCM_ComboMemberPlus0" : "BCM_ComboMemberMinus0").Click();
        }
        CategoryCatalog.Combo Saved(string id) => CategoryCatalog.Combos.Single(combo => combo.Id == id);
        void Open(CategoryCatalog.Combo combo)
        {
            if (combo.IsPackage) ComboPopup.EditPackage(combo);
            else
            {
                ComboPopup.Inspect(combo);
                ButtonListMenu.Visible.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo").Click();
            }
            popup = (ComboPopup)ButtonListMenu.Visible; Button("BCM_EditComboCards").Click();
        }
        ComboNativeSkin Skin() => (ComboNativeSkin)typeof(ComboPopup).GetField("m_skin", flags).GetValue(popup);
        try
        {
            for (int i = 0; i < 8; i++)
            {
                var card = Card(i == 7 ? "CORE_TEST_MEMBER_0" : "TEST_MEMBER_" + i, TAG_PREMIUM.NORMAL, 4,
                    i == 6 ? TAG_RARITY.LEGENDARY : TAG_RARITY.COMMON);
                card.Name = "Member search " + i;
                card.Definition = new EntityDef { CardId = card.CardId, Name = card.Name, Rarity = card.Rarity };
                card.Definition.SetTag(GAME_TAG.COLLECTIBLE, 1);
                manager.Cards[(card.CardId, card.PremiumType)] = card; DefLoader.Get().Definitions[card.CardId] = card.Definition; fixtures.Add(card);
            }
            DefLoader.Get().Definitions[token] = new EntityDef { CardId = token, Name = "Member token" };
            GameUtils.RelatedCards[fixtures[2].CardId] = new[] { token };
            var snapshot = new CategoryDeckImport.Deck(); snapshot.Cards.Add(a.CardId); snapshot.RawCounts[a.CardId] = 2;
            snapshot.Cards.Add(b.CardId); snapshot.RawCounts[b.CardId] = 1;
            CategoryCatalog.Combo Create(bool package)
            {
                var before = new HashSet<string>(CategoryCatalog.Combos.Select(combo => combo.Id));
                var members = snapshot;
                if (package)
                {
                    members = new CategoryDeckImport.Deck(); members.Cards.Add(fixtures[2].CardId); members.RawCounts[fixtures[2].CardId] = 2;
                    members.Cards.Add(fixtures[3].CardId); members.RawCounts[fixtures[3].CardId] = 1;
                }
                CategoryCatalog.AddCombo("Member editing", package ? "Member package" : "Member combo", "Saved description", members,
                    new CategoryCatalog.ComboAppearance { CardText = "Saved rules", Dragon = true }, package ? token : null);
                var added = CategoryCatalog.Combos.Single(combo => !before.Contains(combo.Id)); createdIds.Add(added.Id); return added;
            }
            var original = Create(false); manager.Editing = false; Open(original);
            Check(popup.Header == "Edit Cards" && Button("BCM_SaveCombo").m_ButtonText.Text == "Save"
                && !popup.ActiveButtons.Any(button => button.gameObject.name == "BCM_ComboEditorBack"
                    || button.gameObject.name == "BCM_PreviousComboCards" || button.gameObject.name == "BCM_NextComboCards")
                && !popup.ActiveButtons.Any(button => !popup.Buttons.Contains(button)),
                "Card editing is an independent Save/Cancel dialog with no Back or paging controls");
            Check(Rows().Length == 2 && popup.GetComponentsInChildren<UberText>(false).Any(label => label.gameObject.name == "BCM_QuantityAmount" && label.Text == "2"), "An empty search shows current members and quantities");
            Check(popup.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "BCM_ShopQuantity")
                && popup.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.sharedMaterials.Any(material => material?.name == "NativeShopQuantityGold"))
                && popup.GetComponentsInChildren<AdjustBuyingAmount>(true).Length == 0,
                "Member quantities clone actual shop artwork without retaining shop controllers");
            Check(popup.GetComponentsInChildren<UberText>(false).Count(text => text.Text.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0) == 1,
                "Card editing shows one search label without repeated placeholders or footer prompts");
            int memberSounds = SoundManager.Get().Played.Count;
            Button("BCM_ComboMember1").Click();
            Check(SoundManager.Get().Played.Count == memberSounds + 1,
                "Selecting a member plays one click while actual text fields remain silent");
            Check(SoundManager.Get().Played.Last() == "collection_manager_hero_mouse_over.prefab:653cc8000b988cd468d2210a209adce6",
                "Card-name selection uses the native highlight sound identified by the user's F8 capture");
            Check(!popup.GetComponentsInChildren<UberText>(false).Any(label => label.gameObject.name == "BCM_ComboEditorHint" && label.Text.EndsWith(" cards")),
                "Member editing does not show a second count of distinct cards beside the selected-copy total");
            GameObject selection = Skin().InputSurface("memberSelection");
            Check(selection != null && selection.activeInHierarchy
                && popup.GetComponentsInChildren<Actor>(false).Single().GetEntityDef().GetCardId() == Rows()[1],
                "Selecting a member highlights its name and displays that same card");
            ComboSceneGeometry.ScreenBounds(selection, CameraUtils.Camera, out Rect selectedArea);
            var selectedName = popup.GetComponentsInChildren<UberText>(false).Single(text => text.gameObject.name == "BCM_ComboMemberLabel1");
            CounterTextLayout.TryScreenInkRect(selectedName, CameraUtils.Camera, out Rect selectedInk);
            Check(selectedArea.Contains(selectedInk.center), "The selected-row highlight follows the card name rather than another row");
            Check(selectedInk.xMin >= selectedArea.xMin + 2f, "Highlighted card names have padding inside their left edge");
            Search("Member search");
            var bar = (ComboMemberScrollbar)typeof(ComboPopup).GetField("m_memberScrollbar", flags).GetValue(popup);
            Check(Rows().Length == 4 && bar.Root.activeInHierarchy && bar.Thumb.height < bar.Track.height
                && bar.Root.GetComponentsInChildren<MeshRenderer>(false).Length == 4
                && bar.Root.GetComponentsInChildren<UIBScrollable>(true).Length == 0
                && bar.Root.GetComponentsInChildren<Collider>(true).Length == 0,
                "Overflow search results use native rail, end caps and grip artwork without collection input callbacks");
            Check(Math.Abs(bar.Thumb.height / bar.Thumb.width - .3f / .18f) < .001f
                && sourceBar.GetComponentsInChildren<MeshRenderer>(true).All(renderer => renderer.sharedMaterial.renderQueue == 3100),
                "The grip keeps its native proportions and source scrollbar materials remain unchanged");
            Vector3 grab = new Vector3(bar.Thumb.center.x, bar.Thumb.yMax - bar.Thumb.height * .2f, 0);
            Frame(grab, down: true, held: true);
            Check(Rows().First().EndsWith("Member search 0"), "Grabbing the grip away from its center does not jump the list");
            Frame(new Vector3(bar.Track.center.x, bar.Track.yMin - 100, 0), held: true);
            Check(Rows().Last().EndsWith("Member search 7"), "Dragging the scrollbar reaches the final search result");
            Frame(new Vector3(-100, -100, 0), up: true);
            typeof(ComboPopup).GetMethod("OnBlockerRelease", flags).Invoke(popup, new object[] { new UIEvent() });
            Check(popup.IsShown() && popup.Header == "Edit Cards", "Releasing a captured scrollbar drag outside keeps card editing open");
            Frame(bar.Track.center);
            Input.mouseScrollDelta = new Vector2(0, 1); Frame(bar.Track.center); Input.mouseScrollDelta = Vector2.zero;
            Check(Rows().First().EndsWith("Member search 3"), "The mouse wheel scrolls search results by one row");
            Search(token); Check(Rows().Length == 0 && !bar.Root.activeInHierarchy, "Artwork tokens cannot be inserted as members");
            Change(a.CardId, -1); Button("BCM_CloseCombo").Click();
            Check(popup.IsShown() && Button("BCM_EditComboCards").m_ButtonText.Text.Contains("3")
                && Saved(original.Id).Cards.Single(card => card.Id == a.CardId).Count == 2,
                "Cancel returns to the parent editor and discards only the child selection");
            Button("BCM_EditComboCards").Click(); Change(a.CardId, -1);
            Time.frameCount += 3; Frame(new Vector3(-100, -100, 0), up: true); Frame(Vector3.zero);
            Check(popup.IsShown() && popup.Header == "Edit Combo" && Button("BCM_EditComboCards").m_ButtonText.Text.Contains("3"),
                "A normal outside click cancels the child dialog and returns to its parent");
            Button("BCM_EditComboCards").Click();
            Change(fixtures[6].CardId, 1); Change(fixtures[6].CardId, 1);
            Check(popup.GetComponentsInChildren<UberText>(false).Any(label => label.gameObject.name == "BCM_QuantityAmount" && label.Text == "1") && !Button("BCM_ComboMemberPlus0").IsEnabled(), "Legendary members are capped at one even on repeated callbacks");
            Change(fixtures[0].CardId, 1); Change(fixtures[0].CardId, 1); Change(fixtures[0].CardId, 1);
            Check(popup.GetComponentsInChildren<UberText>(false).Any(label => label.gameObject.name == "BCM_QuantityAmount" && label.Text == "2") && !Button("BCM_ComboMemberPlus0").IsEnabled(), "Other members are capped at two copies");
            Search(fixtures[7].CardId); Check(!Button("BCM_ComboMemberPlus0").IsEnabled(), "Alternate editions share the same rarity copy limit");
            Check(!popup.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "ShopPlus")
                && !Button("BCM_ComboMemberPlus0").GetComponent<Collider>().enabled,
                "The native plus artwork disappears and its hit area is disabled at the shared copy limit");
            Search(fixtures[0].CardId); Button("BCM_ComboMemberMinus0").Click(); Button("BCM_ComboMemberMinus0").Click();
            Check(!Button("BCM_ComboMemberMinus0").IsEnabled()
                && !Button("BCM_ComboMemberMinus0").GetComponent<Collider>().enabled
                && !popup.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "ShopMinus"),
                "At zero copies the native minus artwork and its hit area disappear together");
            Button("BCM_ComboMemberPlus0").Click(); Button("BCM_ComboMemberPlus0").Click();

            Search("Member search");
            foreach (var size in new[] { (320, 240), (480, 900), (1920, 1080) })
            {
                Screen.width = size.Item1; Screen.height = size.Item2; Invoke("Update"); Invoke("LateUpdate");
                float footerTop = Math.Max(Skin().SaveArea.yMax, Skin().CancelArea.yMax);
                var areas = new List<Rect>();
                foreach (UIBButton button in popup.Buttons)
                {
                    BoxCollider hit = button.GetComponent<BoxCollider>(); Rect area;
                    if (hit != null)
                    {
                        Vector3 low = CameraUtils.Camera.WorldToScreenPoint(button.transform.TransformPoint(hit.center - hit.size * .5f));
                        Vector3 high = CameraUtils.Camera.WorldToScreenPoint(button.transform.TransformPoint(hit.center + hit.size * .5f));
                        area = Rect.MinMaxRect(Math.Min(low.x, high.x), Math.Min(low.y, high.y), Math.Max(low.x, high.x), Math.Max(low.y, high.y));
                    }
                    else Check(ComboSceneGeometry.ScreenBounds(button.gameObject, CameraUtils.Camera, out area), "Dialog actions have visible artwork");
                    Check(area.xMin >= 0 && area.xMax <= Screen.width && area.yMin >= 0 && area.yMax <= Screen.height,
                        "All card editing controls fit the viewport after resizing");
                    Check(areas.All(other => area.yMin >= other.yMax || area.yMax <= other.yMin || area.xMin >= other.xMax || area.xMax <= other.xMin),
                        "Member row controls, search and footer have disjoint hit areas: " + button.gameObject.name + " " + area + " previous=" + string.Join(";", areas)); areas.Add(area);
                    if (button.gameObject.name.StartsWith("BCM_ComboMember"))
                        Check(area.yMin > footerTop, "Every member action stays above the footer frame");
                    if (button.gameObject.name.StartsWith("BCM_ComboMemberPlus"))
                    {
                        Check(area.width / area.height < 3f && area.height > Math.Min(Screen.width / 51f, Screen.height / 41f),
                            "Native shop quantity buttons keep compact readable hit areas");
                    }
                }
                Actor preview = popup.GetComponentsInChildren<Actor>(false).Single();
                var rowNames = popup.GetComponentsInChildren<UberText>(false).Where(text => text.gameObject.name.StartsWith("BCM_ComboMemberLabel")).ToArray();
                var quantities = popup.GetComponentsInChildren<UberText>(false).Where(text => text.gameObject.name == "BCM_QuantityAmount").ToArray();
                for (int row = 0; row < rowNames.Length; row++)
                {
                    CounterTextLayout.TryScreenInkRect(rowNames[row], CameraUtils.Camera, out Rect nameInk);
                    CounterTextLayout.TryScreenInkRect(quantities[row], CameraUtils.Camera, out Rect quantityInk);
                    Check(Math.Abs(nameInk.center.y - quantityInk.center.y) < 1f, "Shop quantities align with the actual card-name text ink");
                }
                ComboSceneGeometry.ScreenBounds(preview.gameObject, CameraUtils.Camera, out Rect previewArea);
                Check(previewArea.yMin > footerTop && previewArea.yMax <= Skin().ContentArea.yMax,
                    "The native card preview stays inside the paper above the footer");
            }
            Button("BCM_SaveCombo").Click();
            Check(popup.Header == "Edit Combo" && Button("BCM_EditComboCards").m_ButtonText.Text.Contains("6")
                && Saved(original.Id).Cards.Count == 2, "Child Save returns to the parent and stages changes without writing them yet");
            Button("BCM_SaveCombo").Click(); CategoryCatalog.LoadUserFile(path);
            var saved = Saved(original.Id);
            Check(saved.Cards.Count == 4 && saved.Cards.Single(card => card.Id == fixtures[6].CardId).Count == 1
                && saved.Cards.Single(card => card.Id == fixtures[0].CardId).Count == 2 && saved.Appearance.Dragon
                && saved.Name == original.Name && saved.Category == original.Category && saved.Description == original.Description,
                "Parent Save persists staged members while retaining identity and presentation");
            Open(saved);
            foreach (var member in saved.Cards) for (int i = 0; i < member.Count; i++) Change(member.Id, -1);
            int files = Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length;
            Button("BCM_SaveCombo").Click();
            Check(popup.IsShown() && popup.Header.Contains("at least one") && Directory.GetFiles(CategoryCatalog.DeckDirectory(path)).Length == files,
                "An empty selection stays in the child dialog without persisting anything");
            Button("BCM_CloseCombo").Click(); Button("BCM_CloseCombo").Click();
            var package = Create(true); Open(package); Change(fixtures[2].CardId, -1); Change(fixtures[2].CardId, -1); Button("BCM_SaveCombo").Click();
            Check(!popup.Buttons.Any(button => button.gameObject.name == "BCM_ComboDescription")
                && !popup.GetComponentsInChildren<Transform>(false).Any(part => part.gameObject.name == "BCM_ComboDescriptionInput")
                && !popup.GetComponentsInChildren<UberText>(false).Any(text => text.Text == "Member token"),
                "The parent artwork picker has no search field and removes the discarded owner's token family");
            Button("BCM_CloseCombo").Click(); Check(Saved(package.Id).Artwork == token, "Parent Cancel also discards staged members and automatic artwork changes");
            Open(Saved(package.Id)); Change(fixtures[2].CardId, -1); Change(fixtures[2].CardId, -1); Button("BCM_SaveCombo").Click(); Button("BCM_SaveCombo").Click();
            Check(Saved(package.Id).IsPackage && Saved(package.Id).Cards.Single().Id == fixtures[3].CardId && Saved(package.Id).Artwork == fixtures[3].CardId,
                "Package Save commits members and a remaining member's artwork together");
            Open(Saved(package.Id)); Change(fixtures[2].CardId, 1); Button("BCM_SaveCombo").Click();
            var artwork = popup.GetComponentsInChildren<DropdownControl>(false).Single(control => control.gameObject.name == "BCM_ComboArtwork");
            for (int i = 0; i < 5 && !artwork.Items.Contains(token); i++)
            { Time.unscaledTime += .3f; Invoke("LateUpdate"); }
            artwork.SelectOption(token);
            Check((string)artwork.getSelection() == token,
                "The artwork dropdown exposes a restored owner's tokens by their native identity");
            Button("BCM_SaveCombo").Click(); Check(Saved(package.Id).Artwork == token, "Adding the owner back restores token artwork choices");
            manager.Editing = true; var beforeAdd = new HashSet<string>(CategoryCatalog.Combos.Select(combo => combo.Id));
            ComboPopup.Add(); popup = (ComboPopup)ButtonListMenu.Visible;
            Button("BCM_ComboName").Click(); UniversalInputManager.Get().CompleteNativeText("Edited capture");
            Button("BCM_EditComboCards").Click(); Change(fixtures[1].CardId, 1); Button("BCM_SaveCombo").Click(); Button("BCM_SaveCombo").Click();
            var added = CategoryCatalog.Combos.Single(combo => !beforeAdd.Contains(combo.Id)); createdIds.Add(added.Id);
            Check(added.Cards.Sum(card => card.Count) == deckBefore.Sum(slot => slot.Value) + 1 && manager.EditedDeck.Slots.SequenceEqual(deckBefore),
                "New entries also stage member edits while leaving the real deck unchanged");
        }
        finally
        {
            ComboPopup.Close(); foreach (string id in createdIds) CategoryCatalog.DeleteCombo(id);
            foreach (var card in fixtures) { manager.Cards.Remove((card.CardId, card.PremiumType)); DefLoader.Get().Definitions.Remove(card.CardId); }
            DefLoader.Get().Definitions.Remove(token); GameUtils.RelatedCards.Remove("TEST_MEMBER_2");
            UnityEngine.Object.Destroy(sourceBar.gameObject); Resources.Objects = previousResources;
            manager.Editing = editing; Screen.width = screenWidth; Screen.height = screenHeight;
            Input.LeftPressed = Input.LeftHeld = Input.LeftReleased = false; Input.mouseScrollDelta = Vector2.zero;
        }
    }
}
