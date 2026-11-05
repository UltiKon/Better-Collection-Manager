using System;
using System.Collections.Generic;
using System.Globalization;
using Assets;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed partial class ComboPopup
    {
        private const int MemberPageSize = 4;
        private ComboQuantityControl.Template m_quantityTemplate;
        private readonly ComboQuantityControl[] m_memberQuantities = new ComboQuantityControl[MemberPageSize];
        private sealed class MemberChoice
        {
            internal string Id, Name;
            internal EntityDef Definition;
        }
        private CategoryDeckImport.Deck m_cardDraft;
        private CategoryDeckImport.Deck m_memberParentDraft;
        private bool m_memberParentChanged;
        private string m_memberParentTitle;
        private bool m_cardsEditing, m_cardsChanged, m_memberRowsDirty;
        private int m_memberOffset;
        private string m_memberSearch = "", m_memberPreviewId;
        private Rect m_memberPreviewRect;
        private Rect m_memberViewport;
        private ComboMemberScrollbar m_memberScrollbar;
        private GameObject m_membersRoot;
        private UIBButton m_editCards;
        private readonly UIBButton[] m_memberNames = new UIBButton[MemberPageSize], m_memberMinus = new UIBButton[MemberPageSize], m_memberPlus = new UIBButton[MemberPageSize];
        private readonly UberText[] m_memberLabels = new UberText[MemberPageSize];
        private readonly List<MemberChoice> m_memberCatalog = new List<MemberChoice>();
        private readonly List<MemberChoice> m_memberResults = new List<MemberChoice>();

        private void InitializeMemberControls()
        {
            UIBButton Button(string id, string text, Action action)
            {
                UIBButton button = CreateMenuButton(id, "GLOBAL_BACK", _ => action());
                button.m_ButtonText.GameStringLookup = false; button.SetText(text); FilterButtonFeedback.BindPressSound(button); return button;
            }
            m_editCards = Button("BCM_EditComboCards", "Edit cards", BeginMemberEdit);
            m_membersRoot = new GameObject("BCM_ComboMembers"); m_membersRoot.transform.SetParent(m_menu.m_buttonContainer.transform, false);
            for (int i = 0; i < MemberPageSize; i++)
            {
                int slot = i;
                m_memberNames[i] = Button("BCM_ComboMember" + i, "", () => SelectMember(slot));
                ConfigureTextField(m_memberNames[i]);
                FilterButtonFeedback.BindPressSound(m_memberNames[i], "collection_manager_hero_mouse_over.prefab:653cc8000b988cd468d2210a209adce6");
                m_memberMinus[i] = Button("BCM_ComboMemberMinus" + i, "", () => ChangeMember(slot, -1));
                ConfigureTextField(m_memberMinus[i]);
                FilterButtonFeedback.BindPressSound(m_memberMinus[i]);
                m_memberPlus[i] = Button("BCM_ComboMemberPlus" + i, "", () => ChangeMember(slot, 1));
                ConfigureTextField(m_memberPlus[i]);
                FilterButtonFeedback.BindPressSound(m_memberPlus[i]);
                foreach (UIBButton button in new[] { m_memberNames[i], m_memberMinus[i], m_memberPlus[i] }) button.transform.SetParent(m_membersRoot.transform, false);
                m_memberLabels[i] = EditorLabel("BCM_ComboMemberLabel" + i, "");
                m_memberLabels[i].transform.SetParent(m_membersRoot.transform, false);
                m_memberLabels[i].TextColor = m_memberLabels[i].GradientUpperColor = m_memberLabels[i].GradientLowerColor = new Color(.22f, .16f, .1f, 1f);
            }
            m_membersRoot.SetActive(false); m_editCards.gameObject.SetActive(false);
        }
        private void SetCardDraft(CategoryDeckImport.Deck snapshot, CategoryCatalog.Combo combo = null)
        {
            m_cardsEditing = m_cardsChanged = m_memberRowsDirty = false; m_memberSearch = ""; m_memberOffset = 0;
            m_memberParentDraft = null; m_memberScrollbar?.Hide();
            m_memberPreviewId = null; m_memberCatalog.Clear(); m_memberResults.Clear();
            m_cardDraft = new CategoryDeckImport.Deck { Name = snapshot?.Name };
            if (snapshot != null)
                foreach (string id in snapshot.Cards)
                {
                    int count = snapshot.RawCounts.TryGetValue(id, out int quantity) ? quantity
                        : snapshot.Counts.TryGetValue(CategoryCatalog.NormalizeCardId(id), out quantity) ? quantity : 1;
                    m_cardDraft.Cards.Add(id); m_cardDraft.RawCounts[id] = count;
                }
            if (combo != null)
                foreach (var card in combo.Cards)
                {
                    var editions = combo.ExactVersions ? null : ComboCollectionCards.ComponentCards(combo, card.Id);
                    string id = editions != null && editions.Count > 0 ? editions[0].CardId : card.Id;
                    m_cardDraft.Cards.Add(id); m_cardDraft.RawCounts[id] = MemberCount(id) + card.Count;
                }
        }
        private int MemberCount(string id) => id != null && m_cardDraft != null && m_cardDraft.RawCounts.TryGetValue(id, out int count) ? count : 0;
        private int MemberTotal()
        {
            int count = 0;
            if (m_cardDraft != null) foreach (int quantity in m_cardDraft.RawCounts.Values) count += quantity;
            return count;
        }
        private void BeginMemberEdit()
        {
            StopInput(); ClearActors();
            m_memberParentDraft = m_cardDraft; m_memberParentChanged = m_cardsChanged;
            m_cardDraft = new CategoryDeckImport.Deck { Name = m_memberParentDraft.Name };
            foreach (string id in m_memberParentDraft.Cards)
            { m_cardDraft.Cards.Add(id); m_cardDraft.RawCounts[id] = m_memberParentDraft.RawCounts[id]; }
            m_memberParentTitle = m_menu.m_headerText.Text;
            m_menu.m_headerText.Text = "Edit Cards";
            m_cardsEditing = true; m_memberSearch = ""; m_memberOffset = 0; m_memberPreviewId = null;
            m_openedFrame = Time.frameCount;
            m_memberCatalog.Clear();
            var known = new HashSet<string>(StringComparer.Ordinal);
            CollectionManager manager = CollectionManager.Get();
            CollectionDeck edited = manager.IsInEditMode() ? manager.GetEditedDeck() : null;
            foreach (CollectibleCard card in manager.GetAllCards())
            {
                if (card == null || string.IsNullOrEmpty(card.CardId) || known.Contains(card.CardId)) continue;
                EntityDef def = card.GetEntityDef();
                if (def == null) continue;
                TAG_CARDTYPE type = (TAG_CARDTYPE)def.GetTag(GAME_TAG.CARDTYPE);
                if (type != TAG_CARDTYPE.MINION && type != TAG_CARDTYPE.SPELL && type != TAG_CARDTYPE.WEAPON && type != TAG_CARDTYPE.HERO && type != TAG_CARDTYPE.LOCATION) continue;
                known.Add(card.CardId);
                if (edited != null && !ComboCollectionCards.ClassAllowedInDeck(card, edited)) continue;
                m_memberCatalog.Add(new MemberChoice { Id = card.CardId, Name = card.Name ?? def.GetName() ?? card.CardId, Definition = def });
            }
            foreach (string id in m_cardDraft.Cards)
                if (known.Add(id)) m_memberCatalog.Add(new MemberChoice { Id = id, Name = id, Definition = PackageArtwork.Resolve(id) });
            RefreshMemberResults(); LayoutMenu();
        }
        private void FinishMemberEdit(bool save)
        {
            StopInput();
            if (save && m_cardDraft.Cards.Count == 0)
            { m_menu.m_headerText.Text = "Add at least one card."; LayoutMenu(); return; }
            if (!save) { m_cardDraft = m_memberParentDraft; m_cardsChanged = m_memberParentChanged; }
            ClearActors(); m_memberScrollbar?.Hide(); m_cardsEditing = false; m_memberParentDraft = null; m_memberPreviewId = null;
            m_menu.m_headerText.Text = m_memberParentTitle; m_openedFrame = Time.frameCount;
            if (save && m_package) { m_artCatalog = null; SearchArtwork(); }
            RefreshFields(); LayoutMenu();
        }
        private void RefreshMemberResults()
        {
            m_memberResults.Clear(); string query = m_memberSearch.Trim();
            foreach (var choice in m_memberCatalog)
                if (query.Length == 0 ? MemberCount(choice.Id) > 0
                    : choice.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || choice.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    m_memberResults.Add(choice);
            m_memberResults.Sort((a, b) => { int order = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id); });
            m_memberOffset = Math.Max(0, Math.Min(m_memberOffset, Math.Max(0, m_memberResults.Count - MemberPageSize)));
            m_memberRowsDirty = false;
        }
        private MemberChoice MemberAt(int slot)
        {
            int index = m_memberOffset + slot;
            return index >= 0 && index < m_memberResults.Count ? m_memberResults[index] : null;
        }
        private void SelectMember(int slot)
        {
            MemberChoice member = MemberAt(slot); if (member == null) return;
            StopInput(); m_memberPreviewId = member.Id; LayoutMenu();
        }
        private void ChangeMember(int slot, int delta)
        {
            MemberChoice member = MemberAt(slot); if (member == null) return;
            StopInput();
            int count = MemberCount(member.Id) + delta;
            if (count < 0 || (delta > 0 && MemberFamilyCount(member.Id) >= MemberLimit(member))
                || (count > 0 && !m_cardDraft.Cards.Contains(member.Id) && m_cardDraft.Cards.Count >= 1000)) return;
            if (count == 0) { m_cardDraft.Cards.Remove(member.Id); m_cardDraft.RawCounts.Remove(member.Id); }
            else { m_cardDraft.Cards.Add(member.Id); m_cardDraft.RawCounts[member.Id] = count; }
            m_cardsChanged = true; m_memberPreviewId = member.Id;
            RefreshMemberResults(); LayoutMenu();
        }
        private static int MemberLimit(MemberChoice member) => (TAG_RARITY)(member.Definition?.GetTag(GAME_TAG.RARITY) ?? 0) == TAG_RARITY.LEGENDARY ? 1 : 2;
        private int MemberFamilyCount(string id)
        {
            string family = CategoryCatalog.NormalizeCardId(id); int count = 0;
            foreach (string card in m_cardDraft.Cards)
                if (CategoryCatalog.NormalizeCardId(card) == family) count += MemberCount(card);
            return count;
        }
        private IEnumerable<UIBButton> MemberButtons()
        {
            yield return m_descriptionField;
            for (int i = 0; i < MemberPageSize && MemberAt(i) != null; i++)
            { yield return m_memberNames[i]; yield return m_memberMinus[i]; yield return m_memberPlus[i]; }
            yield return m_save; yield return m_cancel;
        }
        private Rect EditorBody(Rect content, float font) => Rect.MinMaxRect(content.xMin,
            Math.Max(content.yMin + font, Math.Max(m_skin.SaveArea.yMax, m_skin.CancelArea.yMax) + font * 1.2f),
            content.xMax, content.yMax);
        private void PlaceMemberAction(UIBButton button, Camera camera, Rect area)
        {
            ComboSceneGeometry.FaceCaption(button.transform, button.m_ButtonText, camera);
            ComboSceneGeometry.Fit(button.transform, camera, area);
            m_skin.OrderFooterButton(button, camera);
        }
        private void LayoutMembers(Camera camera, Rect content, float left, float width, float font, float depth)
        {
            if (m_quantityTemplate == null) m_quantityTemplate = new ComboQuantityControl.Template();
            foreach (ComboQuantityControl quantity in m_memberQuantities) quantity?.Root.SetActive(false);
            m_options.Root.SetActive(false);
            foreach (UberText label in EditorLabels) label.gameObject.SetActive(false);
            foreach (UIBButton field in TextFields) field.gameObject.SetActive(field == m_descriptionField);
            foreach (string id in new[] { "name", "cardText", "attack", "health", "mana" }) m_skin.InputSurface(id)?.SetActive(false);
            m_descriptionRect = new Rect(left, content.yMax - font * 3.2f, width, font * 2.3f);
            m_descriptionLabel.gameObject.SetActive(true); m_descriptionDisplay.gameObject.SetActive(true);
            PlaceEditorText(m_descriptionLabel, camera, new Rect(left, m_descriptionRect.yMax + font * .35f, width, font * 1.3f), "Search cards", false);
            LayoutField(m_descriptionField, camera, m_descriptionRect, false, "description");
            m_cardTextDisplay.gameObject.SetActive(true);
            PlaceEditorText(m_cardTextDisplay, camera, new Rect(left, m_descriptionRect.yMin - font * 2f, width, font * 1.3f),
                MemberTotal() + " cards selected", false);
            float listWidth = width * .57f;
            float rowTop = m_descriptionRect.yMin - font * 3.3f;
            float rowHeight = Math.Min(font * 4.1f, (rowTop - content.yMin - font * 1.7f) / MemberPageSize);
            float bottom = rowTop - rowHeight * MemberPageSize;
            m_memberViewport = Rect.MinMaxRect(left, bottom, left + listWidth + font * 1.9f, rowTop);
            if (m_memberPreviewId == null && MemberAt(0) != null) m_memberPreviewId = MemberAt(0).Id;
            for (int i = 0; i < MemberPageSize; i++)
            {
                MemberChoice member = MemberAt(i); m_memberLabels[i].gameObject.SetActive(member != null);
                if (member == null) continue;
                float y = rowTop - rowHeight * i;
                float actionWidth = Math.Min(font * 3.8f, listWidth * .23f), gap = font * .35f;
                float actionHeight = Math.Min(font * 2.9f, rowHeight * .83f);
                Rect nameArea = new Rect(left, y - rowHeight + (rowHeight - actionHeight) * .5f,
                    listWidth - actionWidth * 2f - gap * 2f, actionHeight);
                UIBButton name = m_memberNames[i]; name.transform.localRotation = Quaternion.identity; name.transform.localScale = Vector3.one;
                name.transform.position = camera.ScreenToWorldPoint(new Vector3(nameArea.center.x, nameArea.center.y, depth));
                FitFieldHitArea(name, camera, nameArea);
                foreach (Renderer renderer in name.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                name.m_ButtonText.Hide();
                Rect labelArea = new Rect(nameArea.xMin + font * .55f, nameArea.yMin, nameArea.width - font * 1.1f, nameArea.height);
                PlaceEditorText(m_memberLabels[i], camera, labelArea, member.Name, false, .9f);
                ComboSceneGeometry.InFrontOf(m_memberLabels[i].gameObject, m_skin.Body, camera);
                ComboSceneGeometry.TextAfterArtwork(m_memberLabels[i], m_skin.Body);
                if (member.Id == m_memberPreviewId)
                {
                    GameObject selected = m_skin.LayoutInput("memberSelection", camera, nameArea, depth);
                    ComboSceneGeometry.InFrontOf(m_memberLabels[i].gameObject, selected, camera);
                    ComboSceneGeometry.TextAfterArtwork(m_memberLabels[i], selected);
                }

                if (m_memberQuantities[i] == null) m_memberQuantities[i] = new ComboQuantityControl(m_membersRoot.transform, m_quantityTemplate);
                m_memberQuantities[i].Layout(camera, m_skin.Body,
                    new Rect(left + listWidth - actionWidth * 2f - gap, nameArea.yMin, actionWidth * 2f + gap, actionHeight),
                    MemberCount(member.Id), MemberCount(member.Id) > 0,
                    MemberFamilyCount(member.Id) < MemberLimit(member) && (MemberCount(member.Id) > 0 || m_cardDraft.Cards.Count < 1000),
                    m_memberMinus[i], m_memberPlus[i]);
            }
            m_editorHint.gameObject.SetActive(m_memberResults.Count == 0);
            if (m_memberResults.Count == 0)
                PlaceEditorText(m_editorHint, camera, new Rect(left, bottom - font * 1.7f, listWidth, font * 1.3f), "No cards found", false, .85f);
            m_memberPreviewRect = new Rect(left + width * .63f, bottom + font * .3f, width * .37f, rowTop - bottom + font * 2.2f);
            if (m_memberScrollbar == null) m_memberScrollbar = new ComboMemberScrollbar(m_membersRoot.transform);
            m_memberScrollbar.Layout(camera, m_skin.Frame, new Rect(left + listWidth + font * .3f, bottom, font * 1.6f, rowTop - bottom),
                m_memberOffset, m_memberResults.Count, MemberPageSize);
            UpdateMemberPreview(camera);
            FinishEditorLayout(camera);
        }
        private bool UpdateMemberScroll()
        {
            if (m_memberScrollbar == null) return false;
            if (m_memberResults.Count > MemberPageSize && m_memberScrollbar.Retry) LayoutMenu();
            int offset = m_memberOffset;
            bool consumed = m_memberScrollbar.Poll(ref offset);
            if (!consumed && m_memberViewport.Contains(Input.mousePosition) && Input.mouseScrollDelta.y != 0f)
            { offset -= (int)Math.Sign(Input.mouseScrollDelta.y); consumed = true; }
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, m_memberResults.Count - MemberPageSize)));
            if (offset != m_memberOffset) { m_memberOffset = offset; LayoutMenu(); }
            return consumed;
        }
        private void UpdateMemberPreview(Camera camera)
        {
            MemberChoice selected = m_memberCatalog.Find(card => card.Id == m_memberPreviewId) ?? MemberAt(0);
            if (selected == null) { ClearActors(); m_memberPreviewId = null; return; }
            if (m_actors.Count == 0 || m_actors[0].GetEntityDef()?.GetCardId() != selected.Id)
            {
                ClearActors(); m_memberPreviewId = selected.Id;
                if (selected.Definition == null) return;
                GameObject root = AssetLoader.Get().InstantiatePrefab(ActorNames.GetHandActor(selected.Definition, TAG_PREMIUM.NORMAL), AssetLoadingOptions.IgnorePrefabPosition);
                Actor actor = root.GetComponent<Actor>();
                if (actor == null) { UnityEngine.Object.Destroy(root); return; }
                m_actors.Add(actor); root.transform.SetParent(m_menu.m_buttonContainer.transform, false);
                actor.SetPremium(TAG_PREMIUM.NORMAL); actor.SetEntityDef(selected.Definition); actor.UpdateAllComponents();
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                root.transform.localPosition = Vector3.zero; root.SetActive(true);
                LayerUtils.SetLayer(actor, m_targetLayer); actor.SetActorState(ActorStateType.CARD_IDLE); actor.Hide(); actor.Show();
                int queue = 3600;
                foreach (GameObject artwork in new[] { m_skin.Body, m_skin.Frame })
                    foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(artwork))
                        foreach (Material material in renderer.sharedMaterials)
                            if (material != null) queue = Math.Max(queue, Math.Min(5000, material.renderQueue + 1));
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = (Material[])renderer.sharedMaterials.Clone();
                    for (int i = 0; i < materials.Length; i++) if (materials[i] != null)
                    { var copy = new Material(materials[i]); copy.renderQueue = Math.Max(queue, copy.renderQueue); materials[i] = copy; m_previewMaterials.Add(copy); }
                    renderer.sharedMaterials = materials;
                }
            }
            if (m_actors.Count > 0) ComboSceneGeometry.FitCardFace(m_actors[0], camera, m_memberPreviewRect);
            foreach (UberText label in m_memberLabels)
                if (label.gameObject.activeInHierarchy)
                {
                    ComboSceneGeometry.InFrontOf(label.gameObject, m_skin.Body, camera);
                    ComboSceneGeometry.TextAfterArtwork(label, m_skin.Body);
                }
        }
    }
}
