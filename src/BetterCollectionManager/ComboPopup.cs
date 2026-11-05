using System;
using System.Collections.Generic;
using System.Reflection;
using Assets;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed partial class ComboPopup : ButtonListMenu
    {
        private static ComboPopup s_instance;
        private static readonly FieldInfo s_openingAnimation = typeof(ButtonListMenu).GetField("m_showAnimation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private readonly List<UIBButton> m_buttons = new List<UIBButton>();
        private readonly List<Actor> m_actors = new List<Actor>();
        private readonly List<Material> m_previewMaterials = new List<Material>();
        internal const float InspectZoomDuration = .2f;
        private sealed class InspectZoom
        {
            internal Actor Actor;
            internal Vector3 StartPosition, StartScale, EndPosition, EndScale;
        }
        private readonly List<InspectZoom> m_inspectZooms = new List<InspectZoom>();
        private float m_inspectZoomStarted;
        private UIBButton m_nameField, m_descriptionField, m_save, m_cancel, m_previous, m_next, m_edit, m_delete, m_packageDelete, m_newFolder, m_export;
        private bool m_folderOnly;
        private string m_folderDraft = "";
        private Rect m_folderRect;
        private UberText m_nameLabel, m_descriptionLabel, m_editorHint, m_nameDisplay, m_descriptionDisplay, m_fontProbe, m_inspectDescription, m_inspectTitle, m_inputFontProbe;
        private Vector3 m_editorTextScale = Vector3.one, m_fieldTextScale = Vector3.one, m_flavorTextScale = Vector3.one;
        private ComboNativeSkin m_skin;
        private ComboFlavorPane m_flavor;
        private ComboEditorOptions m_options;
        private ComboNativeArrow m_leftArrow, m_rightArrow;
        private ComboNativeArrow.Template m_arrowTemplate;
        private Blizzard.T5.AssetManager.AssetHandle<GameObject> m_gameMenuAsset;
        private CategoryCatalog.ComboAppearance m_appearance = new CategoryCatalog.ComboAppearance();
        private UIBButton m_cardTextField, m_attackField, m_healthField, m_manaField;
        private UberText m_cardTextLabel, m_cardTextDisplay, m_attackDisplay, m_healthDisplay, m_manaDisplay, m_manaLabel, m_frameLabel, m_minionTypeLabel, m_categoryLabel;
        private Rect m_cardTextRect, m_attackRect, m_healthRect, m_manaRect;
        private UIBButton[] m_textFields;
        private UIBButton[] TextFields => m_textFields ?? (m_textFields = new[] { m_nameField, m_descriptionField, m_cardTextField, m_attackField, m_healthField, m_manaField });
        private UberText[] m_editorLabels;
        private UberText[] EditorLabels => m_editorLabels ?? (m_editorLabels = new[] { m_nameLabel, m_descriptionLabel, m_editorHint, m_nameDisplay, m_descriptionDisplay,
            m_cardTextLabel, m_cardTextDisplay, m_attackDisplay, m_healthDisplay, m_manaDisplay, m_manaLabel, m_frameLabel, m_minionTypeLabel, m_categoryLabel });
        private IEnumerable<UIBButton> EditorButtons
        {
            get
            {
                if (m_folderOnly) { yield return m_nameField; yield return m_save; yield return m_cancel; yield break; }
                if (m_cardsEditing) { foreach (UIBButton button in MemberButtons()) yield return button; yield break; }
                if (m_editingCombo != null) yield return m_export;
                yield return m_editCards;
                yield return m_newFolder;
                if (m_package)
                {
                    yield return m_nameField;
                    yield return m_save; yield return m_cancel;
                    if (m_editingCombo != null) yield return m_packageDelete;
                    yield break;
                }
                yield return m_nameField; yield return m_manaField; yield return m_descriptionField; yield return m_cardTextField;
                if (m_appearance.HasAttack) yield return m_attackField;
                if (m_appearance.HasSecondary) yield return m_healthField;
                yield return m_save; yield return m_cancel;
            }
        }
        private string FieldValue(string id) => id == "folder" ? m_folderDraft : id == "name" ? m_name : id == "description" ? (m_cardsEditing ? m_memberSearch : m_description)
            : id == "cardText" ? m_appearance.CardText : id == "mana" ? m_appearance.ManaCost?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""
            : (id == "attack" ? m_appearance.Attack : m_appearance.Health).ToString(System.Globalization.CultureInfo.InvariantCulture);
        private UIBButton FieldButton(string id) => id == "folder" ? m_newFolder : id == "name" ? m_nameField : id == "description" ? m_descriptionField
            : id == "cardText" ? m_cardTextField : id == "mana" ? m_manaField : id == "attack" ? m_attackField : m_healthField;
        private UberText FieldDisplay(string id) => id == "folder" ? m_options.Category.m_selectedItem.m_text : id == "name" ? m_nameDisplay : id == "description" ? m_descriptionDisplay
            : id == "cardText" ? m_cardTextDisplay : id == "mana" ? m_manaDisplay : id == "attack" ? m_attackDisplay : m_healthDisplay;
        private Rect FieldRect(string id) => id == "folder" ? m_folderRect : id == "name" ? m_nameRect : id == "description" ? m_descriptionRect
            : id == "cardText" ? m_cardTextRect : id == "mana" ? m_manaRect : id == "attack" ? m_attackRect : m_healthRect;
        private ScreenEffectsHandle m_screenEffects;
        private bool m_effectActive;
        private readonly Dictionary<UIBButton, Vector3> m_fieldScales = new Dictionary<UIBButton, Vector3>();
        private Rect m_nameRect, m_descriptionRect;
        private float m_editorFont;
        private CategoryDeckImport.Deck m_deck;
        private CategoryCatalog.Combo m_combo;
        private CategoryCatalog.Combo m_editingCombo;
        private CollectionDeck m_editingDeck;
        private string m_name = "", m_description = "", m_inputField;
        private bool m_ready, m_directPackageEdit;
        private bool m_editorPresented, m_editorOpening, m_editorPoseApplied, m_immediateHide;
        private float m_editorOpenedAt;
        private int m_stableLayoutDepth;
        private Vector3 m_editorPosePosition, m_editorPoseScale;
        private ComboEditorExitMotion m_editorExit;
        private Action m_afterEditorExit;
        private Exception m_setupFailure;
        private int m_page, m_firstCard, m_width, m_height;
        private int m_openedFrame;
        private bool m_logAfterLayout;
        private bool m_logSettled;
        private bool m_offClickPending;
        private bool m_editorOffClickPending, m_unfocusPending;
        private Rect m_editorPanel;
        private float m_settledAt;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                MethodInfo font = typeof(UniversalInputManager).GetMethod("ComputeTextInputFontSize", flags);
                MethodInfo finish = typeof(UniversalInputManager).GetMethod("ProcessTextInputFinishKeys", flags);
                if (font == null || finish == null) throw new MissingMethodException("Native combo text-input hooks unavailable.");
                harmony.Patch(font, prefix: new HarmonyLib.HarmonyMethod(typeof(ComboPopup).GetMethod(nameof(NativeFontHeight))));
                harmony.Patch(finish, prefix: new HarmonyLib.HarmonyMethod(typeof(ComboPopup).GetMethod(nameof(KeepDescriptionNewlines))));
            }
            catch (Exception ex) { DebugLog.Exception("Combo input hooks unavailable", ex); }
        }
        private static bool OwnsInput(GameObject owner) => s_instance != null && s_instance.IsShown()
            && s_instance.m_inputField != null && owner == s_instance.gameObject;
        public static void NativeFontHeight(GameObject ___m_inputOwner, ref float rectHeight)
        {
            // UniversalInputManager recalculates the font on every GUI pass.
            // Keep its original calculation, using a text-line height rather
            // than the height of the entire multiline editor.
            if (OwnsInput(___m_inputOwner)) rectHeight = s_instance.m_editorFont + 4f;
        }
        public static bool KeepDescriptionNewlines(GameObject ___m_inputOwner, ref bool __result)
        {
            if (!OwnsInput(___m_inputOwner) || s_instance.m_cardsEditing || (s_instance.m_inputField != "description" && s_instance.m_inputField != "cardText")
                || (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter))) return true;
            __result = false; return false;
        }

        protected override void Awake()
        {
            try
            {
                m_targetLayer = GameLayer.HighPriorityUI; base.Awake();
                m_skin = new ComboNativeSkin(m_menu.m_buttonContainer.transform);
                m_screenEffects = new ScreenEffectsHandle(this);
                Transform source = GameMenu.Get()?.m_menuBone;
                bool useViewportAnchor = source == null;
                // GameMenu is created lazily. Collection actions must also work
                // before the player has opened the game's Escape menu.
                if (useViewportAnchor)
                    source = m_menu.m_buttonContainer.transform.parent ?? m_menu.m_buttonContainer.transform;
                var bone = new GameObject("BCM_ComboPopupBone").transform;
                bone.SetParent(source.parent, false); bone.localPosition = source.localPosition;
                bone.localRotation = source.localRotation; bone.localScale = source.localScale;
                if (useViewportAnchor) bone.localScale = Vector3.one;
                bone.SetParent(transform, true); m_menuParent = bone;
                if (useViewportAnchor)
                {
                    Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
                    if (camera == null) throw new InvalidOperationException("Native popup camera is unavailable.");
                    float depth = camera.WorldToScreenPoint(source.position).z;
                    bone.position = camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, Screen.height * .7f, depth));
                    DebugLog.Msg("Combo popup: using native menu prefab and viewport anchor (GameMenu not initialized).");
                }
                SetTransform();
                m_useTextSizesInSlices = m_menu.m_useTextSizesInSlices;
                m_menu.m_headerText.GameStringLookup = false;
                UIBButton Button(string id, string caption, Action action, UIBButton template = null)
                {
                    UIBButton button = template == null ? CreateMenuButton(id, "GLOBAL_BACK", _ => action())
                        : CreateMenuButton(id, "GLOBAL_BACK", _ => action(), template);
                    button.m_ButtonText.GameStringLookup = false; button.m_ButtonText.RichText = false; button.SetText(caption); FilterButtonFeedback.BindPressSound(button); return button;
                }
                m_nameField = Button("BCM_ComboName", "Name", () => BeginInput("name"));
                m_descriptionField = Button("BCM_ComboDescription", "Description", () => BeginInput("description"));
                m_cardTextField = Button("BCM_ComboCardText", "Card text", () => BeginInput("cardText"));
                m_attackField = Button("BCM_ComboAttack", "0", () => BeginInput("attack"));
                m_healthField = Button("BCM_ComboHealth", "0", () => BeginInput("health"));
                m_manaField = Button("BCM_ComboMana", "Sum", () => BeginInput("mana"));
                foreach (UIBButton field in TextFields) ConfigureTextField(field);
                m_save = Button("BCM_SaveCombo", "Save combo", Save, m_skin.Template.m_okayButton);
                m_cancel = Button("BCM_CloseCombo", "Cancel", Cancel, m_skin.Template.m_cancelButton);
                m_previous = Button("BCM_PreviousComboCards", "Previous", () => ChangePage(-1));
                m_next = Button("BCM_NextComboCards", "Next", () => ChangePage(1));
                m_delete = Button("BCM_DeleteCombo", "Delete combo", Delete);
                m_packageDelete = Button("BCM_DeletePackage", "Delete package", Delete);
                m_edit = Button("BCM_EditCombo", "Edit", Edit);
                m_export = Button("BCM_ExportEntry", "Copy combo", ExportEntry);
                m_newFolder = Button("BCM_AddNewFolder", "New folder", () => { m_folderDraft = ""; m_options.CloseDropdown(); BeginInput("folder"); });
                Material red = GameMenu.Get()?.m_redButtonMaterial;
                if (red == null)
                {
                    m_gameMenuAsset = AssetLoader.Get().LoadAsset<GameObject>("GameMenu.prefab:dc76cbcfb64a34d7e93755df33db2f80");
                    foreach (Component component in m_gameMenuAsset.Asset.GetComponentsInChildren<Component>(true))
                        if (component != null && component.GetType() == typeof(GameMenu))
                            red = typeof(GameMenu).GetField("m_redButtonMaterial").GetValue(component) as Material;
                }
                if (red == null) throw new InvalidOperationException("Native Concede button material is unavailable.");
                MakeButtonRed(m_delete, red); MakeButtonRed(m_packageDelete, red);
                m_options = new ComboEditorOptions(m_menu.m_buttonContainer.transform, StopInput, () => LayoutMenu());
                m_cardTextLabel = EditorLabel("BCM_ComboCardTextLabel", "Card text");
                m_categoryLabel = EditorLabel("BCM_ComboCategoryLabel", "Category");
                m_frameLabel = EditorLabel("BCM_ComboFrameLabel", "Card frame");
                m_minionTypeLabel = EditorLabel("BCM_ComboMinionTypeLabel", "Minion Type");
                m_cardTextDisplay = EditorLabel("BCM_ComboCardTextDisplay", "");
                m_attackDisplay = EditorLabel("BCM_ComboAttackDisplay", "0");
                m_healthDisplay = EditorLabel("BCM_ComboHealthDisplay", "0");
                m_manaDisplay = EditorLabel("BCM_ComboManaDisplay", "Sum");
                m_manaLabel = EditorLabel("BCM_ComboManaLabel", "Mana cost");
                m_nameLabel = EditorLabel("BCM_ComboNameLabel", "Name");
                m_descriptionLabel = EditorLabel("BCM_ComboDescriptionLabel", "Description");
                m_editorHint = EditorLabel("BCM_ComboEditorHint", "");
                m_nameDisplay = EditorLabel("BCM_ComboNameDisplay", "");
                m_descriptionDisplay = EditorLabel("BCM_ComboDescriptionDisplay", "");
                m_inspectTitle = EditorLabel("BCM_ComboInspectTitle", "");
                m_inspectDescription = EditorLabel("BCM_ComboInspectDescription", "");
                m_fontProbe = EditorLabel("BCM_ComboFontProbe", "Ag");
                m_inputFontProbe = EditorLabel("BCM_ComboInputFontProbe", "Ag"); m_inputFontProbe.gameObject.SetActive(false);
                m_fontProbe.gameObject.SetActive(false);
                InitializeMemberControls();
                foreach (UberText text in EditorLabels)
                    text.GradientUpperColor = text.GradientLowerColor = text.TextColor = new Color(.22f, .16f, .1f, 1f);
                foreach (UIBButton field in TextFields) m_fieldScales[field] = field.transform.localScale;
                m_ready = true;
            }
            catch (Exception ex) { m_setupFailure = ex; DebugLog.Exception("Combo popup setup failed", ex); }
            gameObject.SetActive(false);
        }
        private static ComboPopup Get()
        {
            if (s_instance == null) s_instance = new GameObject("BCM_ComboPopup").AddComponent<ComboPopup>();
            if (!s_instance.m_ready)
            {
                Exception failure = s_instance.m_setupFailure;
                UnityEngine.Object.Destroy(s_instance.gameObject); s_instance = null;
                throw new InvalidOperationException("Combo popup: " + (failure?.Message ?? "initialization failed."), failure);
            }
            return s_instance;
        }
        public static void Add() => AddEntry(false);
        public static void AddPackage() => AddEntry(true);
        private static void AddEntry(bool package)
        {
            string selected = null; int selectedCount = 0;
            foreach (CategoryCatalog.Category category in CategoryCatalog.Categories)
                if (CategoryCatalog.IsComboCategory(category.Value) && ExtraFilterState.HasCategory(category.Value))
                { selected = category.Name; selectedCount++; }
            if (selectedCount > 1) selected = null;
            if (package && selected == null) selected = "Packages";
            CategoryDeckImport.Deck deck = EditedCategoryDeckImport.Capture(selected ?? "Combos", allowUnnamed: true);
            if (deck.Cards.Count == 0) throw new InvalidOperationException("Add cards to the edited deck first.");
            ComboPopup menu = Get(); menu.Hide(); menu.m_deck = deck; menu.m_combo = null;
            menu.SetCardDraft(deck);
            menu.m_editingCombo = null; menu.m_directPackageEdit = false; menu.m_folderOnly = false; menu.m_artCatalog = null; menu.m_package = package; menu.m_artwork = ""; menu.m_previewArtwork = null; menu.m_artError = null;
            menu.m_editingDeck = CollectionManager.Get().GetEditedDeck(); menu.m_name = ""; menu.m_description = "";
            menu.m_appearance = new CategoryCatalog.ComboAppearance(); menu.m_options.SetValue(menu.m_appearance);
            if (package)
            {
                PackageKind group = PackageKind.Custom; int groups = 0;
                foreach (var folder in PackageCatalog.Groups)
                    if (ExtraFilterState.HasPackage(folder.Kind)) { group = folder.Kind; groups++; }
                menu.m_options.SetPackageGroup(PackageViewState.IsActive ? PackageViewState.Group ?? PackageKind.Custom : groups == 1 ? group : PackageKind.Custom);
            }
            else menu.m_options.SetCategory(selected ?? "Combos");
            menu.m_menu.m_headerText.Text = (package ? "Add Package · " : "Add Combo · ") + menu.m_options.CategoryName;
            if (package) menu.SearchArtwork();
            menu.RefreshFields(); menu.m_cancel.SetText("Cancel");
            menu.m_buttons.Clear(); menu.m_buttons.AddRange(menu.EditorButtons);
            KeywordSetTray.HideImmediately(); menu.Show(false);
        }
        internal static void EditPackage(CategoryCatalog.Combo package)
        {
            if (package == null || !package.IsPackage) return;
            ComboPopup menu = Get(); menu.Hide(); menu.m_directPackageEdit = true; menu.m_combo = package;
            KeywordSetTray.HideImmediately(); menu.Edit();
        }
        public static void Inspect(CategoryCatalog.Combo combo, Actor source = null)
        {
            Rect? origin = null;
            Camera camera = source != null ? CameraUtils.FindProjectionCameraForObject(source.gameObject) : null;
            if (camera != null && ComboSceneGeometry.CardFaceBounds(source, camera, out Rect area)) origin = area;
            Inspect(combo, true, origin);
        }
        private static void Inspect(CategoryCatalog.Combo combo, bool playSound, Rect? origin = null)
        {
            ComboPopup menu = Get(); menu.Hide(); menu.m_combo = combo; menu.m_deck = null; menu.m_page = menu.m_firstCard = 0;
            menu.SetCardDraft(null);
            menu.m_editingCombo = null; menu.m_directPackageEdit = false; menu.m_package = false; menu.m_folderOnly = false;
            menu.m_edit.SetText(combo.IsPackage ? "Edit package" : "Edit combo");
            menu.m_export.SetText(combo.IsPackage ? "Copy package" : "Copy combo");
            menu.m_delete.SetText(combo.IsPackage ? "Delete package" : "Delete combo");
            menu.m_buttons.Clear();
            menu.m_buttons.AddRange(new[] { menu.m_previous, menu.m_next, menu.m_edit, menu.m_delete });
            KeywordSetTray.HideImmediately(); menu.Show(false); menu.BuildActors();
            if (origin.HasValue) menu.StartInspectZoom(origin.Value);
            if (playSound) SoundManager.Get()?.LoadAndPlay("Card_Transition_Out.prefab:aecf5b5837772844b9d2db995744df82");
        }
        private void Edit()
        {
            CategoryCatalog.Combo combo = m_combo;
            if (combo == null) return;
            m_combo = null; Hide(); m_editingCombo = combo; m_deck = null; m_folderOnly = false;
            SetCardDraft(null, combo);
            m_name = combo.Name; m_description = combo.Description;
            m_artCatalog = null; m_package = combo.IsPackage; m_artwork = combo.Artwork; m_previewArtwork = null; m_artError = null;
            if (m_package) { m_description = ""; SearchArtwork(); }
            m_appearance = combo.Appearance.Copy(); m_options.SetValue(m_appearance);
            if (combo.IsPackage) m_options.SetPackageGroup(combo.PackageGroup);
            else foreach (CategoryCatalog.Category category in CategoryCatalog.Categories)
                if (category.Value == combo.Category) { m_options.SetCategory(category.Name); break; }
            m_export.SetText(m_package ? "Copy package" : "Copy combo");
            m_delete.SetText(m_package ? "Delete package" : "Delete combo");
            m_menu.m_headerText.Text = m_package ? "Edit Package" : "Edit Combo"; RefreshFields(); m_cancel.SetText("Cancel");
            m_buttons.Clear(); m_buttons.AddRange(EditorButtons);
            Show(false);
        }
        private void Cancel() => Cancel(false);
        private void Cancel(bool outsideClick)
        {
            if (!IsShown()) return;
            if (outsideClick) FilterButtonFeedback.PlayClickSound(ComboCollectionCards.PackageClickSound);
            if (m_cardsEditing) { FinishMemberEdit(false); return; }
            CategoryCatalog.Combo edited = m_editingCombo;
            if (edited != null && !m_directPackageEdit)
                m_afterEditorExit = () => {
                    foreach (CategoryCatalog.Combo combo in CategoryCatalog.Combos)
                        if (combo.Id == edited.Id) { Inspect(combo, false); break; }
                };
            Hide();
            if (m_editorExit == null)
            {
                Action after = m_afterEditorExit; m_afterEditorExit = null; after?.Invoke();
            }
        }
        public override void Show(bool playSound = true)
        {
            m_afterEditorExit = null;
            m_editorExit?.Complete();
            RestoreEditorPose(); m_editorOpening = false;
            // Native Show starts ShowWithPunch *after* calling LayoutMenu and
            // sets the rig to HIDDEN_SCALE. Screen fitting during that tween
            // compensates for the tiny scale, then explodes when it reaches 1.
            if (s_openingAnimation == null || s_openingAnimation.FieldType != typeof(bool))
                throw new InvalidOperationException("Native popup animation control is unavailable.");
            if (IsShown()) { LayoutMenu(); return; }
            s_openingAnimation.SetValue(this, false);
            m_openedFrame = Time.frameCount;
            m_offClickPending = false;
            // LayoutMenu is called inside native Show. Activate the owner first
            // so the native shop slices can measure their visible children.
            gameObject.SetActive(true);
            OwnControls();
            m_menu.m_buttonContainer.gameObject.SetActive(true);
            try { base.Show(playSound); }
            catch
            {
                // Native Show claims modal input before LayoutMenu and only
                // sets IsShown afterwards. Hide must release that claim even
                // when a layout failure leaves IsShown false.
                base.Hide(); ClearInspectArrows(); ClearActors();
                m_menu.m_buttonContainer.gameObject.SetActive(false);
                throw;
            }
            // The native menu rig and this dynamically created controller can
            // have separate activation lifetimes. An active rig below an inactive
            // controller still renders nothing and never receives LateUpdate.
            gameObject.SetActive(true);
            OwnControls();
            m_menu.m_buttonContainer.gameObject.SetActive(true);
            m_menu.m_header.gameObject.SetActive(true);
            LayerUtils.SetLayer(m_menu.m_buttonContainer, m_targetLayer);
            LayerUtils.SetLayer(m_menu.m_header, m_targetLayer);
            m_menu.m_headerText.Show();
            foreach (UIBButton button in m_buttons)
            {
                LayerUtils.SetLayer(button, m_targetLayer);
                button.gameObject.SetActive(true); button.SetEnabled(true, true);
                button.m_ButtonText.Show();
            }
            LayoutMenu();
            ScreenEffectParameters effect = ScreenEffectParameters.BlurVignettePerspective;
            effect.Blur = new BlurParameters(1f, 1f);
            m_screenEffects.StartEffect(effect); m_effectActive = true;
            m_logAfterLayout = true;
            m_logSettled = true; m_settledAt = Time.unscaledTime + .5f;
            LogVisibility("show");
            if (DebugLog.VerboseDebug)
                NativePopupDiagnostics.Capture(this, m_menu, CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject),
                    m_menu.m_background, m_menu.m_border);
            m_editorPresented = m_combo == null;
            m_editorOpening = m_editorPresented; m_editorOpenedAt = Time.unscaledTime;
            if (m_stableLayoutDepth == 0) ApplyEditorOpening();
        }
        private void RestoreEditorPose()
        {
            if (!m_editorPoseApplied) return;
            m_editorPoseApplied = false;
            m_menu.m_buttonContainer.transform.position = m_editorPosePosition;
            m_menu.m_buttonContainer.transform.localScale = m_editorPoseScale;
        }
        private void BeginStableLayout()
        {
            if (m_stableLayoutDepth++ == 0) RestoreEditorPose();
        }
        private void EndStableLayout()
        {
            if (--m_stableLayoutDepth == 0) ApplyEditorOpening();
        }
        private void ApplyEditorOpening()
        {
            if (!m_editorOpening || !IsShown()) return;
            float t = Mathf.Clamp01((Time.unscaledTime - m_editorOpenedAt) / .2f);
            if (t >= 1f) { m_editorOpening = false; return; }
            float scale;
            if (t < .7f) { float p = 1f - t / .7f; scale = 1.02f - .1f * p * p * p; }
            else { float p = (t - .7f) / .3f; scale = 1.02f - .02f * p * p * (3f - 2f * p); }
            Transform content = m_menu.m_buttonContainer.transform;
            Camera camera = CameraUtils.FindProjectionCameraForObject(content.gameObject);
            if (camera == null) return;
            m_editorPosePosition = content.position; m_editorPoseScale = content.localScale;
            Vector3 pivot = camera.ScreenToWorldPoint(new Vector3(m_editorPanel.center.x, m_editorPanel.center.y,
                camera.WorldToScreenPoint(content.position).z));
            content.position = pivot + (content.position - pivot) * scale;
            content.localScale *= scale; m_editorPoseApplied = true;
        }
        private void StartEditorExit()
        {
            Transform content = m_menu.m_buttonContainer.transform, parent = content.parent;
            Vector3 position = content.localPosition, scale = content.localScale; Quaternion rotation = content.localRotation;
            Camera camera = CameraUtils.FindProjectionCameraForObject(content.gameObject);
            if (camera == null) { ClearActors(); return; }
            var root = new GameObject("BCM_EditorClosing"); OverlayUI.Get().AddGameObject(root);
            var motion = root.AddComponent<ComboEditorExitMotion>(); m_editorExit = motion;
            Vector3 pivot = camera.ScreenToWorldPoint(new Vector3(m_editorPanel.center.x, m_editorPanel.center.y,
                camera.WorldToScreenPoint(content.position).z));
            content.SetParent(root.transform, true);
            motion.Begin(content, pivot, () => {
                m_editorExit = null;
                if (content != null)
                {
                    content.SetParent(parent, false); content.localPosition = position; content.localRotation = rotation; content.localScale = scale;
                }
                ClearActors();
                Action after = m_afterEditorExit; m_afterEditorExit = null;
                if (!(UniversalInputManager.Get()?.IsDialogActive() ?? false)) after?.Invoke();
            });
        }
        private void OwnControls()
        {
            // These are explicit references to this menu's controls. A common
            // ancestor can also own native screen-sized curtains or cameras.
            // Never activate, recolor, move or scale that ancestor.
            foreach (Transform part in new[] { m_menu.m_buttonContainer.transform, m_menu.m_header.transform })
                if (part != transform && !transform.IsChildOf(part) && !part.IsChildOf(transform)) part.SetParent(transform, true);
            foreach (UIBButton button in m_buttons)
                if (!button.transform.IsChildOf(transform)) button.transform.SetParent(m_menu.m_buttonContainer.transform, true);
        }
        protected override void OnBlockerRelease(UIEvent e)
        {
            if (!IsShown()) return;
            // A release that opens the dialog can also reach its newly created
            // native blocker in the same input dispatch.
            if (Time.frameCount <= m_openedFrame + 1) return;
            if (m_cardsEditing && (m_memberScrollbar?.ConsumeRelease(Input.mousePosition) ?? false)) return;
            if (m_options?.UpdateDropdownScroll() ?? false) return;
            if (m_combo != null) { if (!IsInspectControlAt(Input.mousePosition)) Hide(); }
            else
            {
                if (!IsInsideEditor(Input.mousePosition)) Cancel(true); else m_options.CloseDropdown();
            }
        }
        private bool IsInsideEditor(Vector3 pointer)
        {
            if (m_editorPanel.Contains(pointer)) return true;
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            return m_options.ContainsDropdown(camera, pointer);
        }
        private bool IsInspectControlAt(Vector3 pointer)
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            if (camera == null) return true;
            if ((m_leftArrow?.Contains(camera, pointer) ?? false) || (m_rightArrow?.Contains(camera, pointer) ?? false)) return true;
            foreach (Actor actor in m_actors)
                if (actor != null && actor.gameObject.activeInHierarchy
                    && ComboSceneGeometry.CardFaceBounds(actor, camera, out Rect card) && card.Contains(pointer)) return true;
            foreach (UIBButton button in m_buttons)
                if (button.gameObject.activeInHierarchy
                    && ComboSceneGeometry.ScreenBounds(button.gameObject, camera, out Rect control) && control.Contains(pointer)) return true;
            return false;
        }
        private void LogVisibility(string stage)
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            DebugLog.Msg("[Combo popup] " + stage + " shown=" + IsShown()
                + " controllerActive=" + gameObject.activeInHierarchy + " panelActive=" + m_menu.m_buttonContainer.gameObject.activeInHierarchy
                + " animated=" + s_openingAnimation?.GetValue(this)
                + " rigScale=" + ((object)m_menu is Component rig ? rig.transform.localScale.ToString() : "unavailable")
                + " camera=" + (camera == null ? "missing" : "enabled=" + camera.enabled + " mask=" + camera.cullingMask));
            if (camera == null) return;
            int activeRenderers = 0, cameraRenderers = 0;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(false))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    activeRenderers++;
                    if ((camera.cullingMask & (1 << renderer.gameObject.layer)) != 0) cameraRenderers++;
                }
            DebugLog.Msg("[Combo popup] renderers=" + activeRenderers + " inCameraMask=" + cameraRenderers);
            if (m_combo != null)
            {
                DebugLog.Msg("[Combo popup] inspect components=" + m_combo.Cards.Count + " page=" + m_page);
                foreach (ComboNativeArrow arrow in new[] { m_leftArrow, m_rightArrow })
                    if (arrow != null)
                        DebugLog.Msg("[Combo arrow] " + arrow.Root.name + " active=" + arrow.Root.activeInHierarchy
                            + " bounds=" + (ComboSceneGeometry.ScreenBounds(arrow.Root, camera, out Rect arrowArea) ? arrowArea.ToString() : "unavailable")
                            + " depth=" + camera.WorldToScreenPoint(arrow.Root.transform.position).z
                            + " renderers=" + arrow.Root.GetComponentsInChildren<Renderer>(false).Length);
            }
            foreach (object surface in new object[] { m_menu.m_background, m_menu.m_border })
            {
                Component component = surface as Component;
                DebugLog.Msg("[Combo popup] surface=" + surface.GetType().FullName
                    + " bounds=" + (component != null && ComboSceneGeometry.ScreenBounds(component.gameObject, camera, out Rect area) ? area.ToString() : "unavailable"));
                if (component != null)
                {
                    Renderer renderer = component.GetComponent<Renderer>();
                    DebugLog.Msg("[Combo popup] surfaceObject=" + component.gameObject.name + " parent=" + component.transform.parent?.gameObject.name
                        + " controllerAncestor=" + transform.IsChildOf(component.transform)
                        + " ownRenderer=" + (renderer == null ? "missing" : "order=" + renderer.sortingOrder
                            + " material=" + renderer.sharedMaterial?.name + " queue=" + renderer.sharedMaterial?.renderQueue));
                }
            }
            foreach (Actor actor in m_actors)
                if (actor != null)
                {
                    bool visible = ComboSceneGeometry.CardFaceBounds(actor, camera, out Rect face);
                    DebugLog.Msg("[Combo preview] " + stage + " card=" + actor.GetEntityDef()?.GetCardId()
                        + " active=" + actor.gameObject.activeInHierarchy + " face=" + (visible ? face.ToString() : "unavailable")
                        + " scale=" + actor.transform.localScale + " depth=" + camera.WorldToScreenPoint(actor.transform.position).z);
                }
            foreach (UIBButton button in m_buttons)
            {
                bool measured = ComboSceneGeometry.ScreenBounds(button.gameObject, camera, out Rect rect);
                Vector3 position = camera.WorldToScreenPoint(button.transform.position);
                DebugLog.Msg("[Combo popup] " + button.gameObject.name + " active=" + button.gameObject.activeInHierarchy
                    + " layer=" + button.gameObject.layer + " depth=" + position.z.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " bounds=" + (measured ? rect.ToString() : "unavailable"));
            }
        }
        private void RefreshFields()
        {
            m_nameDisplay.Text = string.IsNullOrEmpty(m_name) ? (m_folderOnly ? (m_package ? "Enter folder name" : "Enter category name") : m_package ? "Enter package name" : "Enter combo name") : m_name;
            m_descriptionDisplay.Text = m_cardsEditing ? m_memberSearch : m_package ? "" : string.IsNullOrEmpty(m_description) ? "Enter description" : m_description;
        }
        private void BeginInput(string field)
        {
            RestoreEditorPose(); m_editorOpening = false;
            StopInput(); m_inputField = field;
            UIBButton button = FieldButton(field);
            Camera camera = CameraUtils.FindProjectionCameraForObject(button.gameObject);
            if (camera == null) throw new InvalidOperationException("Combo text field camera is unavailable.");
            Rect rect = FieldContent(FieldRect(field));
            var parameters = new UniversalInputManager.TextInputParams {
                m_owner = gameObject, m_number = field == "attack" || field == "health" || field == "mana", m_multiLine = !m_cardsEditing && !m_package && (field == "description" || field == "cardText"), m_text = FieldValue(field),
                m_maxCharacters = field == "name" || field == "folder" || m_package || m_cardsEditing ? 80 : field == "attack" || field == "health" || field == "mana" ? 3 : 2000,
                m_rect = new Rect(rect.xMin / Screen.width, 1f - rect.yMax / Screen.height, rect.width / Screen.width, rect.height / Screen.height),
                m_font = m_nameDisplay.GetLocalizedFont(), m_alignment = field == "description" || field == "cardText" ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft,
                m_gameLayer = GameLayer.HighPriorityUI, m_orderInLayer = Math.Max(1000, m_nameDisplay.RenderQueue + 100), m_color = m_nameDisplay.TextColor,
                m_showBackground = false, m_touchScreenKeyboardHideInput = true,
                m_updatedCallback = text => UpdateInput(field, text),
                m_completedCallback = text => {
                    UpdateInput(field, text); StopInput();
                    if (field == "folder") CreateSelectedFolder(text);
                },
                m_canceledCallback = (requested, owner) => StopInput(), m_unfocusedCallback = () => m_unfocusPending = true
            };
            UniversalInputManager.Get().UseTextInput(parameters, force: true);
            if (UniversalInputManager.Get().IsTextInputActive())
                FieldDisplay(field).Hide();
        }
        private void UpdateInput(string field, string text)
        {
            if (m_inputField != field) return;
            if (field == "folder") m_folderDraft = text ?? "";
            else if (field == "name") m_name = text ?? "";
            else if (field == "description")
            {
                if (m_cardsEditing) { m_memberSearch = text ?? ""; m_memberOffset = 0; m_memberPreviewId = null; m_memberRowsDirty = true; }
                else { m_description = text ?? ""; if (m_package) SearchArtwork(); }
            }
            else if (field == "cardText") m_appearance.CardText = text ?? "";
            else if (field == "mana")
            {
                if (string.IsNullOrWhiteSpace(text)) m_appearance.ManaCost = null;
                else if (int.TryParse(text, out int mana) && mana >= 0 && mana <= 999) m_appearance.ManaCost = mana;
            }
            else if (int.TryParse(text, out int number) && number >= 0 && number <= 999 || string.IsNullOrEmpty(text))
            { if (field == "attack") m_appearance.Attack = number; else m_appearance.Health = number; }
        }
        private void StopInput()
        {
            if (m_inputField == null) return;
            m_unfocusPending = false;
            bool folder = m_inputField == "folder";
            m_inputField = null; UniversalInputManager.Get()?.CancelTextInput(gameObject);
            if (folder) { m_options.Category.setSelection(m_options.CategoryName); m_options.Category.m_selectedItem.m_text.Show(); }
            RefreshFields();
            if (IsShown()) LayoutMenu();
        }
        private void Save()
        {
            StopInput();
            if (m_cardsEditing) { FinishMemberEdit(true); return; }
            try
            {
                if (m_folderOnly)
                {
                    if (m_nameAccepted != null)
                    {
                        m_nameAccepted(CategoryDeckImport.CleanName(m_name));
                        Hide(); return;
                    }
                    CategoryCatalog.CreateFolder(m_name, m_package);
                    Hide(); KeywordFilterApplier.RefreshCollectionPages(); KeywordFilterUi.NotifyFilterChangedFromChrome(); return;
                }
                string editedId = m_editingCombo?.Id;
                if (m_cardDraft == null || m_cardDraft.Cards.Count == 0) throw new InvalidOperationException("Add at least one card.");
                if (m_package && (string.IsNullOrEmpty(m_artwork) || (m_artCatalog == null || !m_artCatalog.Contains(m_artwork))))
                    throw new InvalidOperationException("Select a package card or one of its tokens as artwork.");
                if (editedId != null) CategoryCatalog.UpdateCombo(editedId, m_name, m_package ? "" : m_description, m_appearance, m_package ? m_artwork : null,
                    cards: m_cardsChanged ? m_cardDraft : null, categoryName: m_options.CategoryName);
                else
                {
                    if (!ReferenceEquals(m_editingDeck, CollectionManager.Get()?.GetEditedDeck())) throw new InvalidOperationException("The edited deck changed.");
                    CategoryCatalog.AddCombo(m_options.CategoryName, m_name, m_package ? "" : m_description, m_cardDraft, m_appearance, m_package ? m_artwork : null);
                }
                Hide(); KeywordFilterApplier.RefreshCollectionPages(); KeywordFilterUi.NotifyFilterChangedFromChrome();
                if (editedId != null && !m_directPackageEdit)
                    foreach (CategoryCatalog.Combo combo in CategoryCatalog.Combos)
                        if (combo.Id == editedId) { Inspect(combo); break; }
            }
            catch (Exception ex) { m_menu.m_headerText.Text = ex.Message; DebugLog.Warning("Save combo: " + ex.Message); }
        }
        private void ExportEntry()
        {
            CategoryCatalog.Combo selected = m_combo ?? m_editingCombo;
            if (selected == null) return;
            try
            {
                UnityEngine.GUIUtility.systemCopyBuffer = CategoryCatalog.ExportTransfer(selected.IsPackage, entryId: selected.Id);
                m_export.SetText("Copied!");
            }
            catch (Exception ex) { m_menu.m_headerText.Text = ex.Message; }
        }
        private void Delete()
        {
            CategoryCatalog.Combo selected = m_combo ?? m_editingCombo;
            if (selected == null) return;
            CategoryCatalog.DeleteCombo(selected.Id); m_combo = m_editingCombo = null; Hide(); KeywordFilterApplier.RefreshCollectionPages();
            KeywordFilterUi.NotifyFilterChangedFromChrome();
        }
        private void ChangePage(int delta)
        {
            if (m_combo == null) return;
            var page = InspectPage(Screen.width, Screen.height, m_combo.Cards.Count, m_page, true);
            int first = Math.Max(0, Math.Min(page.First + delta * page.Count, m_combo.Cards.Count - page.Count));
            if (first == page.First) return;
            m_openedFrame = Time.frameCount; m_firstCard = first; BuildActors();
        }
        private void ClearActors()
        {
            m_packagePreview?.Dispose(); m_packagePreview = null;
            m_inspectZooms.Clear();
            foreach (Actor actor in m_actors) if (actor != null) actor.Destroy();
            m_actors.Clear();
            foreach (Material material in m_previewMaterials) UnityEngine.Object.Destroy(material);
            m_previewMaterials.Clear();
        }
        private void StartInspectZoom(Rect origin)
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            if (camera == null) return;
            m_inspectZoomStarted = Time.unscaledTime;
            foreach (Actor actor in m_actors)
            {
                if (!ComboSceneGeometry.CardFaceBounds(actor, camera, out Rect bounds)) continue;
                var zoom = new InspectZoom { Actor = actor, EndPosition = actor.transform.localPosition, EndScale = actor.transform.localScale };
                float ratio = Mathf.Clamp(Math.Min(origin.width / bounds.width, origin.height / bounds.height), .05f, .95f);
                actor.transform.localScale *= ratio;
                float depth = camera.WorldToScreenPoint(actor.transform.position).z;
                if (ComboSceneGeometry.CardFaceBounds(actor, camera, out Rect small))
                    actor.transform.position += camera.ScreenToWorldPoint(new Vector3(origin.center.x, origin.center.y, depth))
                        - camera.ScreenToWorldPoint(new Vector3(small.center.x, small.center.y, depth));
                zoom.StartPosition = actor.transform.localPosition; zoom.StartScale = actor.transform.localScale;
                m_inspectZooms.Add(zoom);
            }
        }
        private void AdvanceInspectZoom()
        {
            float progress = Mathf.Clamp01((Time.unscaledTime - m_inspectZoomStarted) / InspectZoomDuration);
            float remaining = 1f - progress, eased = 1f - remaining * remaining * remaining;
            foreach (InspectZoom zoom in m_inspectZooms)
                if (zoom.Actor != null)
                {
                    zoom.Actor.transform.localPosition = zoom.StartPosition + (zoom.EndPosition - zoom.StartPosition) * eased;
                    zoom.Actor.transform.localScale = zoom.StartScale + (zoom.EndScale - zoom.StartScale) * eased;
                }
            if (progress >= 1f) m_inspectZooms.Clear();
        }
        private ComboInspectLayout.Page m_cachedInspectPage;
        private int m_inspectWidth, m_inspectHeight, m_inspectCount, m_inspectNumber, m_inspectFirst;
        private bool m_inspectDescriptionPresent;
        private ComboInspectLayout.Page InspectPage(int width, int height, int count, int number, bool description)
        {
            if (m_cachedInspectPage == null || width != m_inspectWidth || height != m_inspectHeight || count != m_inspectCount
                || number != m_inspectNumber || m_firstCard != m_inspectFirst || description != m_inspectDescriptionPresent)
            {
                m_cachedInspectPage = ComboInspectLayout.Arrange(width, height, count, number, description, m_firstCard);
                m_inspectWidth = width; m_inspectHeight = height; m_inspectCount = count; m_inspectNumber = number; m_inspectDescriptionPresent = description;
                m_inspectFirst = m_firstCard;
            }
            return m_cachedInspectPage;
        }
        private void BuildActors()
        {
            ClearActors(); if (m_combo == null) return;
            ComboInspectLayout.Page page = InspectPage(Screen.width, Screen.height, m_combo.Cards.Count, m_page, true);
            m_page = page.Number; m_firstCard = page.First;
            m_menu.m_headerText.Text = m_combo.Name + (page.TotalPages > 1 ? " · " + (m_page + 1) + "/" + page.TotalPages : "");
            try
            {
                for (int i = 0; i < page.Count; i++)
                {
                    CategoryCatalog.CardRequirement requirement = m_combo.Cards[page.First + i];
                    CollectibleCard source = ComboCollectionCards.PreferredComponent(m_combo, requirement.Id);
                    if (source == null) throw new InvalidOperationException("Card unavailable: " + requirement.Id);
                    EntityDef definition = source.GetEntityDef();
                    TAG_PREMIUM premium = source.PremiumType;
                    GameObject go = AssetLoader.Get().InstantiatePrefab(ActorNames.GetHandActor(definition, premium), AssetLoadingOptions.IgnorePrefabPosition);
                    Actor actor = go.GetComponent<Actor>(); m_actors.Add(actor);
                    if (actor == null) { UnityEngine.Object.Destroy(go); throw new InvalidOperationException("Card preview actor unavailable."); }
                    actor.SetPremium(premium); actor.SetEntityDef(definition); actor.UpdateAllComponents();
                    go.transform.SetParent(m_menu.m_buttonContainer.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    go.SetActive(true);
                    foreach (Collider collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    LayerUtils.SetLayer(actor, GameLayer.HighPriorityUI);
                    actor.SetActorState(ActorStateType.CARD_IDLE);
                    // Actor.Show short-circuits when its cached shown flag is
                    // already true, even on a pooled prefab with hidden meshes.
                    actor.Hide(); actor.Show();
                    foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] materials = renderer.sharedMaterials;
                        for (int m = 0; m < materials.Length; m++)
                        {
                            if (materials[m] == null) continue;
                            // Private materials put previews above the modal's
                            // artwork without changing inventory/shared card assets.
                            var copy = (Material)Activator.CreateInstance(typeof(Material), new object[] { materials[m] });
                            copy.renderQueue = Math.Max(copy.renderQueue, 3600); m_previewMaterials.Add(copy); materials[m] = copy;
                        }
                        renderer.sharedMaterials = materials;
                    }
                    foreach (UberText text in go.GetComponentsInChildren<UberText>(true)) text.UpdateNow(true);
                    if (requirement.Count > 1 && actor.GetNameText() != null)
                    { actor.GetNameText().GameStringLookup = false; actor.GetNameText().Text = requirement.Count + "× " + actor.GetNameText().Text; actor.GetNameText().UpdateNow(true); }
                }
                LayoutInspect(page);
            }
            catch (Exception ex) { ClearActors(); m_menu.m_headerText.Text = ex.Message; DebugLog.Exception("Combo inspect failed", ex); }
        }
        protected override List<UIBButton> GetButtons() => m_buttons;
        protected override void LayoutMenu()
        {
            BeginStableLayout();
            try { LayoutMenuCore(); }
            finally { EndStableLayout(); }
        }
        private void LayoutMenuCore()
        {
            m_membersRoot.SetActive(m_combo == null && m_cardsEditing);
            m_skin.InputSurface("memberSelection")?.SetActive(false);
            // Native LayoutMenu activates its current list but only Show hides
            // the previous list. Switching editor views keeps the same modal.
            foreach (UIBButton button in new[] { m_editCards, m_previous, m_next, m_edit, m_delete, m_packageDelete, m_newFolder, m_save, m_cancel, m_export })
                button.gameObject.SetActive(false);
            foreach (UIBButton field in TextFields) field.gameObject.SetActive(false);
            for (int i = 0; i < MemberPageSize; i++)
                foreach (UIBButton button in new[] { m_memberNames[i], m_memberMinus[i], m_memberPlus[i] }) button.gameObject.SetActive(false);
            if (m_memberRowsDirty) RefreshMemberResults();
            if (m_combo == null)
            {
                m_buttons.Clear(); m_buttons.AddRange(EditorButtons);
            }
            ComboInspectLayout.Page inspectPage = m_combo != null ? InspectPage(Screen.width, Screen.height, m_combo.Cards.Count, m_page, true) : null;
            if (inspectPage != null) ConfigureInspectActions(inspectPage);
            base.LayoutMenu();
            m_inspectDescription.gameObject.SetActive(m_combo != null);
            foreach (UberText label in EditorLabels)
                if (label != null) label.gameObject.SetActive(m_combo == null);
            m_skin.Root.SetActive(m_combo == null);
            if (m_flavor != null) m_flavor.Root.SetActive(m_combo != null);
            m_menu.m_header.gameObject.SetActive(false);
            m_inspectTitle.gameObject.SetActive(false);
            m_options.Root.SetActive(m_combo == null);
            if (m_combo == null) { m_leftArrow?.Root.SetActive(false); m_rightArrow?.Root.SetActive(false); }
            SuppressMenuSurface();
            if (m_combo != null) LayoutInspect(inspectPage);
            else LayoutEditor();
        }
        private void LayoutEditor()
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            if (camera == null) return;
            float depth = camera.WorldToScreenPoint(m_menu.m_buttonContainer.transform.position).z;
            Vector3 World(float x, float y) => camera.ScreenToWorldPoint(new Vector3(x * Screen.width, y * Screen.height, depth));
            float font = Mathf.Clamp(Math.Min(Screen.width / 51f, Screen.height / 41f), 4f, 30f);
            m_editorFont = font;
            float panelWidth = Math.Min(Screen.width * .94f, font * 47f);
            float panelHeight = font * (m_package && !m_cardsEditing && !m_folderOnly ? 33f : 35f), headerHeight = font * 3f;
            float panelLeft = (Screen.width - panelWidth) * .5f;
            float bottom = (Screen.height - panelHeight - headerHeight) * .5f;
            float top = bottom + panelHeight;
            m_menu.m_buttonContainer.transform.position = World(.5f, top / Screen.height);
            m_skin.Layout(camera, new Rect(panelLeft, bottom, panelWidth, panelHeight),
                new Rect(panelLeft, top - font * .7f, panelWidth, headerHeight), depth, font, m_menu.m_headerText.Text);
            Rect paper = m_skin.ContentArea;
            m_editorPanel = new Rect(panelLeft, bottom, panelWidth, panelHeight + headerHeight);
            // Use a symmetric inner area based on the fitted panel. The native
            // midsection mesh contains transparent padding outside its paper.
            float safeWidth = Math.Min(paper.width, panelWidth - font * 4f);
            Rect content = new Rect(m_editorPanel.center.x - safeWidth * .5f, paper.yMin, safeWidth, paper.height);
            // ContentArea includes the panel's opaque border and footer edges.
            // Keep every field inside the parchment, including at wide aspect ratios.
            float left = content.xMin + font * 2.5f, width = content.width - font * 5f;
            float half = (width - font * 1.5f) * .5f;
            m_nameRect = new Rect(left, content.yMax - font * .5f, width, font * 2.3f);
            if (!m_cardsEditing && CounterTextLayout.TryScreenInkRect(m_skin.HeaderText, camera, out Rect headline))
            {
                float nameTop = Math.Min(m_nameRect.yMax, headline.yMin - font * 2.2f);
                m_nameRect = new Rect(left, nameTop - m_nameRect.height, width, m_nameRect.height);
            }
            m_fontProbe.gameObject.SetActive(true); m_fontProbe.Show();
            m_fontProbe.transform.rotation = camera.transform.rotation;
            m_fontProbe.transform.position = World(-2f, -2f); m_fontProbe.UpdateNow(true);
            if (CounterTextLayout.TryScreenInkRect(m_fontProbe, camera, out Rect probe) && probe.height > .001f)
                m_editorTextScale = m_fontProbe.transform.lossyScale * (font / probe.height);
            m_fontProbe.gameObject.SetActive(false);
            if (m_folderOnly) { LayoutFolderEditor(camera, left, width, font); return; }
            m_save.SetText(m_cardsEditing ? "Save" : m_package ? "Save package" : "Save combo");
            if (m_cardsEditing) { LayoutMembers(camera, EditorBody(content, font), left, width, font, depth); return; }
            m_editCards.SetText("Edit cards (" + MemberTotal() + ")");
            m_newFolder.SetText(m_package ? "New package folder" : "New category");
            var actions = m_editingCombo == null ? new[] { m_editCards, m_newFolder } : new[] { m_editCards, m_export, m_newFolder };
            float actionGap = font, actionWidth = (width - actionGap * (actions.Length - 1)) / actions.Length;
            float actionBottom = m_nameRect.yMin - font * 3.4f;
            for (int i = 0; i < actions.Length; i++)
            {
                actions[i].gameObject.SetActive(true);
                PlaceEditorAction(actions[i], camera, new Rect(left + i * (actionWidth + actionGap), actionBottom, actionWidth, font * 2.4f), font);
            }
            if (m_package) { LayoutPackageEditor(camera, EditorBody(content, font), left, width, font, depth); return; }
            float manaWidth = font * 7f;
            m_manaRect = new Rect(left + width - manaWidth, m_nameRect.yMin, manaWidth, m_nameRect.height);
            m_nameRect = new Rect(left, m_nameRect.yMin, width - manaWidth - font * 1.5f, m_nameRect.height);
            float optionsTop = actionBottom - font * 2f;
            Rect frame = new Rect(left, optionsTop - font * 2.2f, half, font * 2.2f);
            Rect minionType = new Rect(left + half + font * 1.5f, frame.yMin, half, font * 2.2f);
            Rect dragon = new Rect(left, frame.yMin - font * 3.4f, half, font * 2.2f);
            float statsY = dragon.yMin - font * 3.4f;
            Rect attack = new Rect(left, statsY, half * .65f, font * 2.2f);
            Rect health = new Rect(left + half + font * 1.5f, statsY, half * .65f, font * 2.2f);
            m_attackRect = new Rect(left + half * .7f, statsY, half * .3f, font * 2.2f);
            m_healthRect = new Rect(health.xMin + half * .7f, statsY, half * .3f, font * 2.2f);
            float textTop = (m_appearance.HasSecondary ? statsY : dragon.yMin) - font * 2.2f;
            float hintBottom = content.yMin + font * .7f;
            float textBottom = hintBottom + font * 1.4f;
            m_cardTextRect = new Rect(left, textBottom, half, Math.Max(font * 3f, textTop - textBottom));
            m_descriptionRect = new Rect(left + half + font * 1.5f, textBottom, half, m_cardTextRect.height);
            foreach (UberText label in EditorLabels) label.gameObject.SetActive(true);
            foreach (UIBButton field in TextFields) field.gameObject.SetActive(true);
            PlaceEditorText(m_nameLabel, camera, new Rect(left, m_nameRect.yMax + font * .35f, m_nameRect.width, font * 1.15f), "Name", false);
            PlaceEditorText(m_manaLabel, camera, new Rect(m_manaRect.xMin, m_manaRect.yMax + font * .35f, manaWidth, font * 1.15f), "Mana cost", false);
            PlaceEditorText(m_frameLabel, camera, new Rect(left, frame.yMax + font * .25f, half, font * 1.15f), "Card frame", false);
            m_minionTypeLabel.gameObject.SetActive(m_appearance.Frame == "Minion" || m_appearance.Frame == "Spell");
            if (m_minionTypeLabel.gameObject.activeSelf)
                PlaceEditorText(m_minionTypeLabel, camera, new Rect(minionType.xMin, minionType.yMax + font * .25f, half, font * 1.15f),
                    m_appearance.Frame == "Minion" ? "Minion Type" : "Spell School", false);
            PlaceEditorText(m_cardTextLabel, camera, new Rect(left, textTop + font * .25f, half, font * 1.15f), "Card text", false);
            PlaceEditorText(m_descriptionLabel, camera, new Rect(m_descriptionRect.xMin, textTop + font * .25f, half, font * 1.15f), "Description", false);
            m_editorHint.gameObject.SetActive(false);
            LayoutField(m_nameField, camera, m_nameRect, false);
            LayoutField(m_manaField, camera, m_manaRect, false, "mana");
            LayoutField(m_descriptionField, camera, m_descriptionRect, true);
            LayoutField(m_cardTextField, camera, m_cardTextRect, true, "cardText");
            LayoutField(m_attackField, camera, m_attackRect, false, "attack");
            LayoutField(m_healthField, camera, m_healthRect, false, "health");
            m_attackField.gameObject.SetActive(m_appearance.HasAttack); m_attackDisplay.gameObject.SetActive(m_appearance.HasAttack);
            m_healthField.gameObject.SetActive(m_appearance.HasSecondary); m_healthDisplay.gameObject.SetActive(m_appearance.HasSecondary);
            m_skin.InputSurface("attack")?.SetActive(m_appearance.HasAttack);
            m_skin.InputSurface("health")?.SetActive(m_appearance.HasSecondary);
            Rect categoryArea = new Rect(minionType.xMin, dragon.yMin, half, dragon.height);
            PlaceEditorText(m_categoryLabel, camera, new Rect(categoryArea.xMin, categoryArea.yMax + font * .25f, half, font * 1.15f), "Category", false);
            LayoutFolderSelector(camera, categoryArea, depth, font);
            m_options.Layout(camera, frame, minionType, dragon, attack, health, depth, m_skin.Body);
            FinishEditorLayout(camera);
        }
        private void PlaceEditorAction(UIBButton button, Camera camera, Rect area, float font)
        {
            PlaceMemberAction(button, camera, area);
            UberText caption = button.m_ButtonText;
            if (caption != null && CounterTextLayout.TryScreenInkRect(caption, camera, out Rect ink) && ink.height > .001f)
            {
                float scale = Math.Min(font * .85f / ink.height, area.width * .86f / Math.Max(.001f, ink.width));
                caption.transform.localScale *= scale;
            }
        }
        private void FinishEditorLayout(Camera camera)
        {
            ComboSceneGeometry.FaceCaption(m_save.transform, m_save.m_ButtonText, camera);
            ComboSceneGeometry.FaceCaption(m_cancel.transform, m_cancel.m_ButtonText, camera);
            ComboSceneGeometry.Fit(m_save.transform, camera, m_skin.SaveArea);
            ComboSceneGeometry.Fit(m_cancel.transform, camera, m_skin.CancelArea);
            OrderContents(camera);
            m_width = Screen.width; m_height = Screen.height;
        }
        private void SuppressMenuSurface()
        {
            foreach (Renderer renderer in SurfaceRenderers((object)m_menu.m_background as Component)) renderer.enabled = false;
            foreach (Renderer renderer in SurfaceRenderers((object)m_menu.m_border as Component)) renderer.enabled = false;
        }
        private UberText EditorLabel(string id, string caption)
        {
            UberText source = m_skin.Template.m_messageText;
            var go = new GameObject(id); go.transform.SetParent(m_menu.m_buttonContainer.transform, false);
            go.transform.rotation = source.transform.rotation;
            UberText text = go.AddComponent<UberText>();
            text.TrueTypeFont = source.TrueTypeFont; text.FontSize = source.FontSize;
            text.CharacterSize = source.CharacterSize; text.BoldSize = source.BoldSize;
            text.RenderQueue = source.RenderQueue;
            text.TextColor = new Color(1f, .9f, .68f, 1f);
            text.GradientUpperColor = text.GradientLowerColor = text.TextColor;
            text.Text = caption; text.GameStringLookup = text.RichText = text.RenderToTexture = false;
            return text;
        }
        private Rect FieldContent(Rect rect)
        {
            float padding = Math.Min(rect.height * .2f, m_editorFont * .65f);
            return new Rect(rect.xMin + padding, rect.yMin + padding, rect.width - padding * 2f, rect.height - padding * 2f);
        }
        private static void ConfigureTextField(UIBButton field)
        {
            FilterButtonFeedback.MutePressSound(field);
            // UIBButton.OnOver punches the artwork through a 90-degree rotation.
            // A tall field then intersects the popup background and exposes its
            // back face as a black band. These controls are stationary fields.
            field.m_WiggleAmount = Vector3.zero; field.m_WiggleTime = 0f;
            field.m_DepressOnOver = false; field.m_ClickDownOffset = Vector3.zero;
            field.m_AnimateFlip = false; field.m_WigglePostFlip = false;
            field.m_HoldDepressionOnRelease = false;
            foreach (UIBHighlight highlight in field.GetComponentsInChildren<UIBHighlight>(true))
            {
                highlight.EnableResponse = false; highlight.enabled = false;
                if (highlight.m_MouseOverHighlight != null) highlight.m_MouseOverHighlight.SetActive(false);
                if (highlight.m_MouseDownHighlight != null) highlight.m_MouseDownHighlight.SetActive(false);
            }
        }
        private void LayoutField(UIBButton button, Camera camera, Rect target, bool multiline, string id = null)
        {
            id = id ?? (multiline ? "description" : "name");
            UberText display = FieldDisplay(id);
            button.transform.localScale = m_fieldScales[button];
            float depth = camera.WorldToScreenPoint(button.transform.position).z;
            button.transform.position = camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth));
            GameObject plate = m_skin.LayoutInput(id, camera, target, depth);
            display.TextColor = display.GradientUpperColor = display.GradientLowerColor = new Color(.22f, .16f, .1f, 1f);
            if (m_skin.InputStyle != null)
            {
                UberText style = m_skin.InputStyle;
                foreach (UberText text in new[] { display, m_inputFontProbe })
                {
                    text.TrueTypeFont = style.TrueTypeFont; text.FontSize = style.FontSize;
                    text.CharacterSize = style.CharacterSize; text.BoldSize = style.BoldSize;
                }
                m_inputFontProbe.gameObject.SetActive(true); m_inputFontProbe.Show();
                m_inputFontProbe.transform.rotation = camera.transform.rotation;
                m_inputFontProbe.transform.position = camera.ScreenToWorldPoint(new Vector3(-Screen.width, -Screen.height, depth));
                m_inputFontProbe.UpdateNow(true);
                if (CounterTextLayout.TryScreenInkRect(m_inputFontProbe, camera, out Rect probe) && probe.height > .001f)
                    m_fieldTextScale = m_inputFontProbe.transform.lossyScale * (m_editorFont / probe.height);
                m_inputFontProbe.gameObject.SetActive(false);
            }
            else m_fieldTextScale = m_editorTextScale;
            FitFieldHitArea(button, camera, target);
            if (m_inputField != id)
                PlaceEditorText(display, camera, FieldContent(target),
                    string.IsNullOrEmpty(FieldValue(id)) ? (id == "mana" ? "Sum" : id == "name" ? "Enter combo name" : id == "cardText" ? "Enter card text" : m_cardsEditing ? "" : "Enter description") : FieldValue(id), multiline);
            else display.Hide();
            SuppressNativeFieldText();
        }
        internal static void FitFieldHitArea(UIBButton button, Camera camera, Rect target)
        {
            if (FilterTrayGeometry.TryProjectToPlane(button.transform, camera, new Vector3(target.xMin, target.yMin, 0), 0, out Vector3 low)
                && FilterTrayGeometry.TryProjectToPlane(button.transform, camera, new Vector3(target.xMax, target.yMax, 0), 0, out Vector3 high))
            {
                BoxCollider hit = button.GetComponent<BoxCollider>() ?? button.gameObject.AddComponent<BoxCollider>();
                hit.center = new Vector3((low.x + high.x) * .5f, hit.center.y, (low.z + high.z) * .5f);
                hit.size = new Vector3(Math.Abs(high.x - low.x), Math.Max(hit.size.y, .02f), Math.Abs(high.z - low.z));
                hit.enabled = true;
            }
        }
        private void SuppressNativeFieldText()
        {
            foreach (UIBButton field in TextFields)
            {
                field.m_ButtonText.Hide();
                Renderer surface = CounterTextLayout.TextureSurface(field.m_ButtonText);
                if (surface != null) surface.enabled = false;
            }
            foreach (UIBButton field in TextFields)
                foreach (Renderer face in field.GetComponentsInChildren<Renderer>(true)) face.enabled = false;
        }
        private sealed class EditorTextLayout
        {
            internal string Caption, Displayed;
            internal Rect Target;
            internal bool Multiline;
            internal float Size, Pixels, Font, Character, Bold, Width;
            internal object Typeface;
            internal Vector3 FontScale;
            internal Matrix4x4 Model, View, Projection;
            internal Bounds Ink;
            internal Renderer Glyph;
        }
        private readonly Dictionary<UberText, EditorTextLayout> m_textLayouts = new Dictionary<UberText, EditorTextLayout>();
        private void PlaceEditorText(UberText text, Camera camera, Rect target, string caption, bool multiline, float size = 1f)
        {
            Vector3 desiredScale = text == m_nameDisplay || text == m_descriptionDisplay || text == m_cardTextDisplay || text == m_attackDisplay || text == m_healthDisplay || text == m_manaDisplay ? m_fieldTextScale
                : text == m_inspectDescription ? m_flavorTextScale : m_editorTextScale;
            m_textLayouts.TryGetValue(text, out EditorTextLayout layout);
            if (layout != null && !text.isHidden() && !text.GameStringLookup && !text.RichText && !text.RenderToTexture
                && !text.ResizeToFit && !text.ResizeToFitAndGrow && text.WordWrap == multiline && layout.Width == text.Width
                && text.Alignment == UberText.AlignmentOptions.Left && text.Anchor == UberText.AnchorOptions.Upper && layout.Caption == caption && layout.Displayed == text.Text
                && layout.Target.Equals(target) && layout.Multiline == multiline && layout.Size == size && layout.Pixels == m_editorFont
                && layout.Font == text.FontSize && layout.Character == text.CharacterSize && layout.Bold == text.BoldSize
                && Equals(layout.Typeface, text.TrueTypeFont) && layout.FontScale.Equals(desiredScale)
                && layout.Model.Equals(text.transform.localToWorldMatrix) && layout.View.Equals(camera.worldToCameraMatrix)
                && layout.Projection.Equals(camera.projectionMatrix) && layout.Ink.Equals(text.GetTextWorldSpaceBounds())
                && layout.Glyph == CounterTextLayout.GlyphRenderer(text)) return;
            text.GameStringLookup = text.RichText = text.RenderToTexture = false;
            text.ResizeToFit = text.ResizeToFitAndGrow = text.WordWrap = false;
            text.Alignment = UberText.AlignmentOptions.Left; text.Anchor = UberText.AnchorOptions.Upper;
            text.transform.rotation = camera.transform.rotation;
            text.Show();
            float pixels = m_editorFont * size;
            Vector3 parent = text.transform.parent != null ? text.transform.parent.lossyScale : Vector3.one;
            Vector3 fontScale = text == m_nameDisplay || text == m_descriptionDisplay || text == m_cardTextDisplay || text == m_attackDisplay || text == m_healthDisplay || text == m_manaDisplay ? m_fieldTextScale
                : text == m_inspectDescription ? m_flavorTextScale : m_editorTextScale;
            text.transform.localScale = new Vector3(fontScale.x / parent.x, fontScale.y / parent.y, fontScale.z / parent.z) * size;
            float depth = camera.WorldToScreenPoint(text.transform.position).z;
            text.transform.position = camera.ScreenToWorldPoint(new Vector3(target.xMin, target.yMax, depth));
            // Measure this font at its final field position. The Options font
            // and parchment font have different metrics, and scaling an offscreen
            // probe left the real field substantially smaller than its caption.
            text.Text = "Ag"; text.UpdateNow(true);
            if (CounterTextLayout.TryScreenInkRect(text, camera, out Rect sample) && sample.height > .001f)
                text.transform.localScale *= pixels / sample.height;
            Vector3 left = text.transform.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(target.xMin, target.yMax, depth)));
            Vector3 right = text.transform.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(target.xMax, target.yMax, depth)));
            text.Width = Vector3.Distance(left, right);
            text.WordWrap = multiline;
            // A long inactive description shows a readable excerpt. The native
            // focused field always receives the complete, unmodified value.
            int columns = Math.Max(1, (int)(target.width / (pixels * .58f)));
            int lines = multiline ? Math.Max(1, (int)(target.height / (pixels * 1.4f))) : 1;
            int limit = columns * lines;
            string preview = caption;
            if (multiline)
            {
                int used = 0, length = 0;
                foreach (string line in caption.Replace("\r", "").Split('\n'))
                {
                    used += Math.Max(1, (line.Length + columns - 1) / columns);
                    if (used > lines) break;
                    length += line.Length + 1;
                }
                if (used > lines && length > 0) limit = Math.Min(limit, length - 1);
            }
            if (preview.Length > limit) preview = preview.Substring(0, Math.Max(0, limit - 1)) + "…";
            text.Text = preview; text.UpdateNow(true);
            if (CounterTextLayout.TryScreenInkRect(text, camera, out Rect ink))
                text.transform.position += camera.ScreenToWorldPoint(new Vector3(target.xMin, multiline ? target.yMax : target.center.y, depth))
                    - camera.ScreenToWorldPoint(new Vector3(ink.xMin, multiline ? ink.yMax : ink.center.y, depth));
            LayerUtils.SetLayer(text, m_targetLayer);
            if (layout == null) m_textLayouts[text] = layout = new EditorTextLayout();
            layout.Caption = caption; layout.Displayed = text.Text; layout.Target = target; layout.Multiline = multiline;
            layout.Size = size; layout.Pixels = m_editorFont; layout.Font = text.FontSize; layout.Character = text.CharacterSize;
            layout.Bold = text.BoldSize; layout.Width = text.Width; layout.Typeface = text.TrueTypeFont; layout.FontScale = desiredScale;
            layout.Model = text.transform.localToWorldMatrix; layout.View = camera.worldToCameraMatrix; layout.Projection = camera.projectionMatrix;
            layout.Ink = text.GetTextWorldSpaceBounds(); layout.Glyph = CounterTextLayout.GlyphRenderer(text);
        }
        private IEnumerable<Renderer> SurfaceRenderers(Component surface)
        {
            if (surface == null) yield break;
            foreach (Renderer renderer in surface.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                bool foreground = renderer.transform.IsChildOf(m_menu.m_header.transform);
                foreach (UIBButton button in m_buttons) if (renderer.transform.IsChildOf(button.transform)) foreground = true;
                if (m_skin != null && renderer.transform.IsChildOf(m_skin.Root.transform)) foreground = true;
                if (m_flavor != null && renderer.transform.IsChildOf(m_flavor.Root.transform)) foreground = true;
                if (m_options != null && renderer.transform.IsChildOf(m_options.Root.transform)) foreground = true;
                if (m_membersRoot != null && renderer.transform.IsChildOf(m_membersRoot.transform)) foreground = true;
                if (m_leftArrow != null && renderer.transform.IsChildOf(m_leftArrow.Root.transform)) foreground = true;
                if (m_rightArrow != null && renderer.transform.IsChildOf(m_rightArrow.Root.transform)) foreground = true;
                foreach (UberText label in new[] { m_nameLabel, m_descriptionLabel, m_editorHint, m_nameDisplay, m_descriptionDisplay, m_cardTextLabel, m_frameLabel, m_minionTypeLabel, m_cardTextDisplay, m_attackDisplay, m_healthDisplay, m_manaDisplay, m_manaLabel, m_inspectDescription, m_inspectTitle })
                    if (label != null && renderer.transform.IsChildOf(label.transform)) foreground = true;
                foreach (Actor actor in m_actors) if (actor != null && renderer.transform.IsChildOf(actor.transform)) foreground = true;
                if (m_packagePreview != null && renderer.transform.IsChildOf(m_packagePreview.Root.transform)) foreground = true;
                if (!foreground) yield return renderer;
            }
        }
        private void OrderContents(Camera camera)
        {
            Component background = (object)m_menu.m_background as Component;
            Component border = (object)m_menu.m_border as Component;
            void Foreground(GameObject root)
            {
                foreach (Renderer renderer in SurfaceRenderers(background)) ComboSceneGeometry.InFrontOf(root, renderer, camera);
                foreach (Renderer renderer in SurfaceRenderers(border)) ComboSceneGeometry.InFrontOf(root, renderer, camera);
                if (m_skin.Root.activeInHierarchy) ComboSceneGeometry.InFrontOf(root, m_skin.Body, camera);
                ComboSceneGeometry.TextInFront(root, camera);
            }
            Foreground(m_menu.m_header.gameObject);
            foreach (UIBButton button in m_buttons)
            {
                if (m_skin.Root.activeInHierarchy && (button == m_save || button == m_cancel || button == m_delete))
                    m_skin.OrderFooterButton(button, camera);
                Foreground(button.gameObject);
                ComboSceneGeometry.FitButtonHitArea(button);
            }

            foreach (UberText label in new[] { m_nameLabel, m_descriptionLabel, m_editorHint, m_nameDisplay, m_descriptionDisplay, m_cardTextLabel, m_frameLabel, m_minionTypeLabel, m_cardTextDisplay, m_attackDisplay, m_healthDisplay, m_manaDisplay, m_manaLabel, m_inspectDescription, m_inspectTitle })
                if (label != null && label.gameObject.activeInHierarchy)
                {
                    if (m_skin.Root.activeInHierarchy) ComboSceneGeometry.TextAfterArtwork(label, m_skin.Body);
                    Foreground(label.gameObject);
                    GameObject inputPlate = label == m_nameDisplay ? m_skin.InputSurface(false)
                        : label == m_descriptionDisplay ? m_skin.InputSurface(true) : label == m_cardTextDisplay ? m_skin.InputSurface("cardText")
                        : label == m_attackDisplay ? m_skin.InputSurface("attack") : label == m_healthDisplay ? m_skin.InputSurface("health") : label == m_manaDisplay ? m_skin.InputSurface("mana") : null;
                    if (inputPlate != null && inputPlate.activeInHierarchy)
                    {
                        ComboSceneGeometry.InFrontOf(label.gameObject, inputPlate, camera);
                        ComboSceneGeometry.TextAfterArtwork(label, inputPlate);
                    }
                    if (label == m_inspectDescription && m_flavor != null && m_flavor.Root.activeInHierarchy)
                    {
                        ComboSceneGeometry.InFrontOf(label.gameObject, m_flavor.Root, camera);
                        ComboSceneGeometry.TextAfterArtwork(label, m_flavor.Root);
                    }
                }
            foreach (Actor actor in m_actors) if (actor != null) Foreground(actor.gameObject);
            if (m_flavor != null && m_flavor.Root.activeInHierarchy) Foreground(m_flavor.Root);
            if (m_leftArrow != null && m_leftArrow.Root.activeInHierarchy) Foreground(m_leftArrow.Root);
            if (m_rightArrow != null && m_rightArrow.Root.activeInHierarchy) Foreground(m_rightArrow.Root);
            if (m_packagePreview != null) m_packagePreview.OrderInPopup(m_skin.Body, camera);
            m_options?.OrderOpenDropdown(camera, m_menu.m_buttonContainer.gameObject);
        }
        private void ConfigureInspectActions(ComboInspectLayout.Page page)
        {
            m_buttons.Clear();

            m_buttons.AddRange(new[] { m_edit, m_export, m_delete });
            foreach (UIBButton button in new[] { m_previous, m_next, m_edit, m_export, m_delete, m_cancel })
                button.gameObject.SetActive(m_buttons.Contains(button));
        }
        private void LayoutInspect(ComboInspectLayout.Page page)
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            if (camera == null) return;
            ConfigureInspectActions(page);
            m_menu.m_headerText.Text = m_combo.Name + (page.TotalPages > 1 ? " · " + (page.Number + 1) + "/" + page.TotalPages : "");
            float depth = camera.WorldToScreenPoint(m_menu.m_buttonContainer.transform.position).z;
            m_menu.m_buttonContainer.transform.position = camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, page.Panel.yMax, depth));
            m_skin.Root.SetActive(false); SuppressMenuSurface();
            m_menu.m_header.gameObject.SetActive(false);
            float gap = page.Font * 1.2f;
            float actionWidth = Math.Min(page.Font * 12f, (page.Toolbar.width - page.Font * 9f - gap * (m_buttons.Count - 1)) / m_buttons.Count);
            float actionsWidth = actionWidth * m_buttons.Count + gap * (m_buttons.Count - 1);
            float actionCenter = page.ActionCenter;
            float actionLeft = Mathf.Clamp(actionCenter - actionsWidth * .5f, page.Toolbar.xMin, page.Toolbar.xMax - actionsWidth);
            for (int i = 0; i < m_buttons.Count; i++)
            {
                ComboSceneGeometry.FaceCaption(m_buttons[i].transform, m_buttons[i].m_ButtonText, camera);
                ComboSceneGeometry.Fit(m_buttons[i].transform, camera,
                    new Rect(actionLeft + i * (actionWidth + gap), page.Toolbar.yMin, actionWidth, page.Toolbar.height));
                if (ComboSceneGeometry.ScreenBounds(m_buttons[i].gameObject, camera, out Rect hitArea))
                    FitFieldHitArea(m_buttons[i], camera, hitArea);
            }

            m_editorFont = page.Font;
            m_fontProbe.gameObject.SetActive(true); m_fontProbe.Show();
            m_fontProbe.transform.rotation = camera.transform.rotation;
            m_fontProbe.transform.position = camera.ScreenToWorldPoint(new Vector3(-Screen.width, -Screen.height, depth));
            m_fontProbe.UpdateNow(true);
            if (CounterTextLayout.TryScreenInkRect(m_fontProbe, camera, out Rect probe) && probe.height > .001f)
                m_editorTextScale = m_fontProbe.transform.lossyScale * (m_editorFont / probe.height);
            m_fontProbe.gameObject.SetActive(false);
            m_inspectTitle.gameObject.SetActive(false);
            if (page.TotalPages > 1 && m_leftArrow == null)
            {
                m_arrowTemplate = new ComboNativeArrow.Template();
                m_leftArrow = new ComboNativeArrow(m_menu.m_buttonContainer.transform, true, () => ChangePage(-1), template: m_arrowTemplate);
                m_rightArrow = new ComboNativeArrow(m_menu.m_buttonContainer.transform, false, () => ChangePage(1), template: m_arrowTemplate);
            }
            float arrowWidth = Math.Min(page.Font * 10.5f, Math.Max(page.Font, page.Panel.xMin - page.Font * 1.1f));
            float arrowHeight = page.Font * 15.6f, arrowY = (page.Cards.Length > 0 ? page.Cards[0].center.y : page.Panel.center.y) - arrowHeight * .5f;
            m_leftArrow?.Layout(camera, new Rect(page.Panel.xMin - page.Font * .8f - arrowWidth, arrowY, arrowWidth, arrowHeight), depth,
                page.TotalPages > 1 && page.Number > 0);
            m_rightArrow?.Layout(camera, new Rect(page.Panel.xMax + page.Font * .8f, arrowY, arrowWidth, arrowHeight), depth,
                page.TotalPages > 1 && page.Number + 1 < page.TotalPages);
            m_inspectDescription.gameObject.SetActive(false);
            if (m_flavor == null) m_flavor = new ComboFlavorPane(m_menu.m_buttonContainer.transform);
            string category = m_combo.Category.ToString();
            foreach (CategoryCatalog.Category item in CategoryCatalog.Categories)
                if (item.Value == m_combo.Category) { category = item.Name; break; }
            m_flavor.Layout(camera, page.Description, depth, category, m_combo.Description ?? "");
            if (m_inspectZooms.Count == 0)
                for (int i = 0; i < m_actors.Count && i < page.Cards.Length; i++)
                {
                    Actor actor = m_actors[i];
                    if (actor.GetNameText() != null) ComboSceneGeometry.FaceCaption(actor.transform, actor.GetNameText(), camera);
                    ComboSceneGeometry.FitCardFace(actor, camera, page.Cards[i]);
                }
            OrderContents(camera);
            m_width = Screen.width; m_height = Screen.height;
        }
        private void Update()
        {
            BeginStableLayout();
            try { UpdateCore(); }
            finally { EndStableLayout(); }
        }
        private void UpdateCore()
        {
            if (!IsShown()) return;
            m_leftArrow?.Update(); m_rightArrow?.Update();
            bool memberScrollInput = m_cardsEditing && UpdateMemberScroll();
            bool dropdownScrollInput = m_options?.UpdateDropdownScroll() ?? false;
            // Card colliders are disabled on preview actors. Keep the native
            // off-click behavior even when no PegUI release reaches its blocker.
            if (m_combo != null && Time.frameCount > m_openedFrame + 1
                && Input.GetMouseButtonUp(0) && !IsInspectControlAt(Input.mousePosition)) m_offClickPending = true;
            if (!memberScrollInput && !dropdownScrollInput && m_combo == null && Time.frameCount > m_openedFrame + 1 && Input.GetMouseButtonUp(0)
                && !IsInsideEditor(Input.mousePosition)) m_editorOffClickPending = true;
            SuppressMenuSurface();
            if (m_combo == null) SuppressNativeFieldText();
            if (!PreferredQualityMode.IsEnabled) { Hide(); return; }
            if (m_deck != null && (!CollectionManager.Get().IsInEditMode() || !ReferenceEquals(m_editingDeck, CollectionManager.Get().GetEditedDeck()))) { Hide(); return; }
            if (m_width != Screen.width || m_height != Screen.height)
            {
                string editing = m_inputField;
                if (editing != null) StopInput();
                if (m_combo != null) BuildActors(); else LayoutMenu();
                if (editing != null) BeginInput(editing);
            }
        }
        private void LateUpdate()
        {
            BeginStableLayout();
            try { LateUpdateCore(); }
            finally { ComboSceneGeometry.EndArtworkMeasurements(); EndStableLayout(); }
        }
        private void LateUpdateCore()
        {
            // Fonts/actor assets can finish a frame after Show. Refit the visible
            // models so late geometry never escapes its reserved preview cell.
            if (!IsShown()) return;
            // Let PegUI finish dispatching this release before deactivating its
            // blocker, so the click cannot fall through to collection cards.
            if (m_offClickPending && m_combo != null) { Hide(); return; }
            if (m_editorOffClickPending && m_combo == null) { m_editorOffClickPending = false; Cancel(true); return; }
            if (m_unfocusPending && !Input.GetMouseButton(0)) { m_unfocusPending = false; StopInput(); }
            if (m_cardsEditing && m_memberRowsDirty) LayoutMenu();
            SuppressMenuSurface();
            if (m_combo == null) SuppressNativeFieldText();
            if (m_logAfterLayout)
            {
                LayoutMenu(); LogVisibility("next-frame"); m_logAfterLayout = false;
            }
            if (m_logSettled && Time.unscaledTime >= m_settledAt)
            {
                LogVisibility("settled"); m_logSettled = false;
            }
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            if (camera == null) return;
            if (m_combo == null) ComboSceneGeometry.BeginArtworkMeasurements(camera, m_skin.Body, m_skin.Frame);
            if (m_combo == null)
            {
                if (m_cardsEditing) UpdateMemberPreview(camera);
                else if (m_package && !m_folderOnly) UpdateArtworkPreview(camera);
                foreach (UberText label in EditorLabels)
                {
                    ComboSceneGeometry.InFrontOf(label.gameObject, m_skin.Body, camera);
                    ComboSceneGeometry.TextAfterArtwork(label, m_skin.Body);
                    GameObject plate = label == m_nameDisplay ? m_skin.InputSurface(false)
                        : label == m_descriptionDisplay ? m_skin.InputSurface(true) : label == m_cardTextDisplay ? m_skin.InputSurface("cardText")
                        : label == m_attackDisplay ? m_skin.InputSurface("attack") : label == m_healthDisplay ? m_skin.InputSurface("health") : label == m_manaDisplay ? m_skin.InputSurface("mana") : null;
                    if (plate != null)
                    {
                        ComboSceneGeometry.InFrontOf(label.gameObject, plate, camera);
                        ComboSceneGeometry.TextAfterArtwork(label, plate);
                    }
                }
                m_options.OrderOpenDropdown(camera, m_menu.m_buttonContainer.gameObject);
                return;
            }
            if (m_inspectZooms.Count > 0) { AdvanceInspectZoom(); return; }
            var page = InspectPage(Screen.width, Screen.height, m_combo.Cards.Count, m_page, true);
            for (int i = 0; i < m_actors.Count && i < page.Cards.Length; i++)
            {
                Actor actor = m_actors[i];
                if (!actor.gameObject.activeSelf) actor.gameObject.SetActive(true);
                Renderer face = actor.GetMeshRenderer();
                if (face != null && !face.enabled) { actor.Hide(); actor.Show(); }
                if (ComboSceneGeometry.CardFaceBounds(m_actors[i], camera, out Rect bounds))
                {
                    Rect cell = page.Cards[i];
                    bool outside = bounds.xMin < cell.xMin || bounds.xMax > cell.xMax || bounds.yMin < cell.yMin || bounds.yMax > cell.yMax;
                    bool undersized = bounds.width < cell.width * .65f && bounds.height < cell.height * .65f;
                    if (outside || undersized) ComboSceneGeometry.FitCardFace(m_actors[i], camera, cell);
                }
            }
        }
        public override void Hide()
        {
            m_nameAccepted = null; m_nameConfirmText = null;
            if (m_editorExit != null) { m_afterEditorExit = null; m_editorExit.Complete(); }
            m_memberScrollbar?.Hide();
            m_options?.CloseDropdown();
            StopInput(); RestoreEditorPose(); m_editorOpening = false;
            if (IsShown() && m_editorPresented && !m_immediateHide && PreferredQualityMode.IsEnabled) StartEditorExit();
            else ClearActors();
            StopEffects();
            if (IsShown())
            {
                if (m_combo != null) SoundManager.Get()?.LoadAndPlay("Card_Transition_In.prefab:3f3fbe896b8b260448e8c7e5d028d971");
                LogVisibility("hide"); base.Hide();
            }
            m_logAfterLayout = false; m_logSettled = false;
            m_offClickPending = false;
            m_editorOffClickPending = m_unfocusPending = false;
            m_editorPresented = false;
            ClearInspectArrows();
            gameObject.SetActive(false);
        }
        private void ClearInspectArrows()
        {
            // Native PegUI keeps press/focus state on the component. Reusing
            // the hidden picker buttons can leave the next inspect blocked.
            m_leftArrow?.Dispose(); m_rightArrow?.Dispose(); m_arrowTemplate?.Dispose();
            m_leftArrow = m_rightArrow = null; m_arrowTemplate = null;
        }
        private void StopEffects()
        {
            if (!m_effectActive) return;
            m_effectActive = false; m_screenEffects.StopEffect();
        }
        private void OnDisable() { StopEffects(); }
        public static void Close()
        {
            if (s_instance == null) return;
            s_instance.m_afterEditorExit = null;
            s_instance.m_immediateHide = true;
            try { s_instance.Hide(); }
            finally { s_instance.m_immediateHide = false; }
        }
        protected override void OnDestroy()
        {
            m_immediateHide = true; m_afterEditorExit = null; m_editorExit?.Complete();
            if (IsShown()) Hide();
            StopEffects(); m_memberScrollbar?.Dispose();
            foreach (ComboQuantityControl quantity in m_memberQuantities) quantity?.Dispose();
            m_quantityTemplate?.Dispose();
            ClearInspectArrows(); m_gameMenuAsset?.Dispose();
            m_options?.Dispose(); m_flavor?.Dispose(); m_skin?.Dispose(); base.OnDestroy();
        }
    }
    internal sealed class ComboEditorExitMotion : MonoBehaviour
    {
        private Transform m_content;
        private Vector3 m_pivot, m_position, m_scale;
        private float m_started;
        private Action m_complete;
        private readonly List<Collider> m_enabledHits = new List<Collider>();
        internal void Begin(Transform content, Vector3 pivot, Action complete)
        {
            m_content = content; m_pivot = pivot; m_position = content.position; m_scale = content.localScale;
            m_started = Time.unscaledTime; m_complete = complete;
            foreach (Collider hit in content.GetComponentsInChildren<Collider>(true))
                if (hit.enabled) { m_enabledHits.Add(hit); hit.enabled = false; }
        }
        private void LateUpdate()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - m_started) / .16f);
            if (t >= 1f) { Complete(); return; }
            float scale = 1f - .99f * t * t * (3f - 2f * t);
            m_content.position = m_pivot + (m_position - m_pivot) * scale;
            m_content.localScale = m_scale * scale;
        }
        internal void Complete()
        {
            Action complete = m_complete; m_complete = null;
            if (complete == null) return;
            foreach (Collider hit in m_enabledHits) if (hit != null) hit.enabled = true;
            m_enabledHits.Clear(); complete(); UnityEngine.Object.Destroy(gameObject);
        }
        private void OnDestroy() => Complete();
    }
}
