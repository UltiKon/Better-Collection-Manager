using System;
using System.Collections.Generic;
using Assets;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed partial class ComboPopup
    {
        private Action<string> m_nameAccepted;
        private string m_nameConfirmText;

        internal static void AddFolder(bool package) => ShowFolderEditor(package);

        internal static void PromptCategoryName(string title, string confirmText, Action<string> accepted)
            => ShowFolderEditor(false, title, confirmText, accepted);

        private static void ShowFolderEditor(bool package, string title = null, string confirmText = null, Action<string> accepted = null)
        {
            ComboPopup menu = Get(); menu.Hide();
            menu.m_combo = menu.m_editingCombo = null; menu.m_deck = null;
            menu.SetCardDraft(null); menu.m_directPackageEdit = false;
            menu.m_folderOnly = true; menu.m_package = package;
            menu.m_name = menu.m_description = "";
            menu.m_nameAccepted = accepted; menu.m_nameConfirmText = confirmText;
            menu.m_menu.m_headerText.Text = title ?? (package ? "New Package Folder" : "New Category");
            menu.RefreshFields(); menu.m_cancel.SetText("Cancel");
            menu.m_buttons.Clear(); menu.m_buttons.AddRange(menu.EditorButtons);
            KeywordSetTray.HideImmediately(); menu.Show(false);
        }
        private void CreateSelectedFolder(string text)
        {
            try
            {
                string name = CategoryCatalog.CreateFolder(text, m_package);
                if (m_package) m_options.SetPackageGroup(PackageCatalog.EnsureGroup(name));
                else m_options.SetCategory(name);
                KeywordFilterUi.NotifyFilterChangedFromChrome(); LayoutMenu();
            }
            catch (Exception ex) { m_menu.m_headerText.Text = ex.Message; LayoutMenu(); }
        }
        private void LayoutFolderSelector(Camera camera, Rect area, float depth, float font, bool only = false)
        {
            m_folderRect = area;
            m_options.LayoutCategory(camera, m_folderRect, depth, m_skin.Body, only);
        }
        private void LayoutFolderEditor(Camera camera, float left, float width, float font)
        {
            m_options.Root.SetActive(false);
            foreach (UberText label in EditorLabels) label.gameObject.SetActive(false);
            foreach (UIBButton field in TextFields) field.gameObject.SetActive(field == m_nameField);
            foreach (string id in new[] { "description", "cardText", "attack", "health", "mana" }) m_skin.InputSurface(id)?.SetActive(false);
            m_nameLabel.gameObject.SetActive(true); m_nameDisplay.gameObject.SetActive(true);
            m_nameRect = new Rect(left, m_nameRect.yMin, width, m_nameRect.height);
            PlaceEditorText(m_nameLabel, camera, new Rect(left, m_nameRect.yMax + font * .35f, width, font * 1.4f), m_package ? "Folder name" : "Category name", false);
            LayoutField(m_nameField, camera, m_nameRect, false);
            m_save.SetText(m_nameConfirmText ?? (m_package ? "Create folder" : "Create category")); FinishEditorLayout(camera);
        }
        private bool m_package;
        private PackageArtwork.Catalog m_artCatalog;
        private PackageNativeTile m_packagePreview;
        private string m_artwork = "", m_previewArtwork, m_artError;
        private List<PackageArtwork.Choice> m_artChoices = new List<PackageArtwork.Choice>();
        private Rect m_artPreviewRect;
        private float m_nextArtSearch;
        private int m_artChoicesRevision = -1;

        private void SearchArtwork()
        {
            if (m_artCatalog == null)
            {
                var ids = new List<string>();
                if (m_cardDraft != null) ids.AddRange(m_cardDraft.Cards);
                else if (m_editingCombo != null) foreach (var card in m_editingCombo.Cards) ids.Add(card.Id);
                else if (m_deck != null) ids.AddRange(m_deck.Cards);
                m_artCatalog = PackageArtwork.For(ids);
                m_artChoicesRevision = -1;
                // One bounded step: keep saved token art while its family loads.
                m_artCatalog.Advance();
            }
            if (m_artChoicesRevision != m_artCatalog.Revision)
            { m_artChoices = m_artCatalog.Search(""); m_artChoicesRevision = m_artCatalog.Revision; }
            // Keep the user's current selection as the native index fills.
            bool found = false;
            foreach (var choice in m_artChoices) if (choice.Id == m_artwork) found = true;
            if (!found && (string.IsNullOrEmpty(m_artwork) || m_artCatalog.Complete))
                m_artwork = m_artChoices.Count > 0 ? m_artChoices[0].Id : "";
            m_options.SetArtworkChoices(m_artChoices, m_artwork, selected => { StopInput(); m_artwork = selected; LayoutMenu(); });
        }
        private void UpdateArtworkPreview(Camera camera)
        {
            m_artCatalog?.Advance();
            if (Time.unscaledTime >= m_nextArtSearch)
            { m_nextArtSearch = Time.unscaledTime + .25f; SearchArtwork(); }
            if (m_previewArtwork != m_artwork || (m_actors.Count == 0 && m_artError == null))
            {
                ClearActors(); m_previewArtwork = m_artwork; m_artError = null;
                try
                {
                    EntityDef definition = PackageArtwork.Resolve(m_artwork);
                    if (definition != null)
                    {
                        GameObject root = AssetLoader.Get().InstantiatePrefab(ActorNames.GetHandActor(definition, TAG_PREMIUM.NORMAL), AssetLoadingOptions.IgnorePrefabPosition);
                        Actor actor = root.GetComponent<Actor>();
                        if (actor == null) UnityEngine.Object.Destroy(root);
                        else
                        {
                            m_actors.Add(actor); root.transform.SetParent(m_menu.m_buttonContainer.transform, false);
                            root.transform.localPosition = Vector3.zero; root.SetActive(true);
                            actor.SetPremium(TAG_PREMIUM.NORMAL); actor.SetEntityDef(definition); actor.UpdateAllComponents();
                            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                            LayerUtils.SetLayer(actor, GameLayer.HighPriorityUI); actor.SetActorState(ActorStateType.CARD_IDLE);
                            actor.Hide(); actor.Show();
                            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                            {
                                Material[] materials = renderer.sharedMaterials;
                                for (int i = 0; i < materials.Length; i++)
                                    if (materials[i] != null) { var copy = new Material(materials[i]); copy.renderQueue = Math.Max(3600, copy.renderQueue); materials[i] = copy; m_previewMaterials.Add(copy); }
                                renderer.sharedMaterials = materials;
                            }
                        }
                    }
                }
                catch (Exception ex) { ClearActors(); m_artError = "Artwork preview unavailable"; DebugLog.Warning("Package artwork preview: " + ex.Message); }
            }
            if (m_actors.Count > 0)
            {
                Actor actor = m_actors[0];
                if (m_packagePreview == null)
                {
                    var cards = new List<CategoryCatalog.CardRequirement>();
                    if (m_cardDraft != null)
                        foreach (string id in m_cardDraft.Cards)
                            cards.Add(new CategoryCatalog.CardRequirement(id, MemberCount(id), Array.Empty<string>()));
                    else if (m_editingCombo != null) cards.AddRange(m_editingCombo.Cards);
                    else if (m_deck != null)
                        foreach (string id in m_deck.Cards)
                            cards.Add(new CategoryCatalog.CardRequirement(id, m_deck.RawCounts.TryGetValue(id, out int count) ? count : 1, Array.Empty<string>()));
                    if (cards.Count > 0) m_packagePreview = PackageNativeTile.TryCreate(actor,
                        new CategoryCatalog.Combo { IsPackage = true, Artwork = m_artwork, Name = m_name, Cards = cards,
                            PackageClass = m_editingCombo?.PackageClass ?? "" },
                        m_menu.m_buttonContainer.transform);
                }
                if (m_packagePreview != null)
                {
                    m_packagePreview.Refresh(string.IsNullOrWhiteSpace(m_name) ? "Package" : m_name, m_artPreviewRect);
                    m_packagePreview.OrderInPopup(m_skin.Body, camera);
                }
                else ComboSceneGeometry.Fit(actor.transform, camera, m_artPreviewRect);
            }

        }
        private void LayoutPackageEditor(Camera camera, Rect content, float left, float width, float font, float depth)
        {
            m_options.Root.SetActive(false);
            foreach (UberText label in EditorLabels) label.gameObject.SetActive(false);
            foreach (UIBButton field in TextFields) field.gameObject.SetActive(field == m_nameField);
            foreach (string id in new[] { "description", "cardText", "attack", "health", "mana" }) m_skin.InputSurface(id)?.SetActive(false);
            m_nameLabel.gameObject.SetActive(true); m_nameDisplay.gameObject.SetActive(true);
            m_descriptionLabel.gameObject.SetActive(true);
            m_cardTextDisplay.gameObject.SetActive(false);
            PlaceEditorText(m_nameLabel, camera, new Rect(left, m_nameRect.yMax + font * .35f, width, font * 1.15f), "Name", false);
            LayoutField(m_nameField, camera, m_nameRect, false);
            Rect categoryArea = new Rect(left, m_nameRect.yMin - font * 7.6f, width * .48f, font * 2.2f);
            m_categoryLabel.gameObject.SetActive(true);
            PlaceEditorText(m_categoryLabel, camera, new Rect(left, categoryArea.yMax + font * .25f, categoryArea.width, font * 1.15f), "Package folder", false);
            LayoutFolderSelector(camera, categoryArea, depth, font, only: true);
            float artTop = categoryArea.yMin - font * 2.8f;
            PlaceEditorText(m_descriptionLabel, camera, new Rect(left, artTop, width * .48f, font * 1.15f), "Artwork", false);
            Rect artwork = new Rect(left, artTop - font * 2.8f, width * .48f, font * 2.2f);
            m_options.LayoutArtwork(camera, artwork, depth, m_skin.Body);
            m_artPreviewRect = new Rect(left + width * .54f, content.yMin + font * .5f, width * .46f,
                Math.Max(font * 8f, categoryArea.yMax - content.yMin - font * .5f));
            m_previous.gameObject.SetActive(false); m_next.gameObject.SetActive(false);
            if (m_editingCombo != null)
            {
                m_packageDelete.gameObject.SetActive(true);
                Rect deleteArea = new Rect(left, artwork.yMin - font * 3.8f, width * .48f, font * 2.4f);
                PlaceEditorAction(m_packageDelete, camera, deleteArea, font);
                if (ComboSceneGeometry.ScreenBounds(m_packageDelete.gameObject, camera, out Rect deleteBounds))
                {
                    float buttonDepth = camera.WorldToScreenPoint(m_packageDelete.transform.position).z;
                    m_packageDelete.transform.position += camera.ScreenToWorldPoint(new Vector3(deleteArea.xMin, deleteBounds.center.y, buttonDepth))
                        - camera.ScreenToWorldPoint(new Vector3(deleteBounds.xMin, deleteBounds.center.y, buttonDepth));
                }
            }
            m_save.SetText("Save package");
            FinishEditorLayout(camera); UpdateArtworkPreview(camera);
        }
    }
}
