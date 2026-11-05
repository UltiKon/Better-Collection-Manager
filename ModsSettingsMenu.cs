using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Native collection, quality priority and filter settings together in one grouped panel.</summary>
    internal sealed partial class ModsSettingsMenu : ButtonListMenu
    {
        private static ModsSettingsMenu s_shown;
        private const string MenuTitle = "Collection Mod Settings";
        private readonly List<UIBButton> m_buttons = new List<UIBButton>();
        private readonly List<GameObject> m_rows = new List<GameObject>();
        private readonly List<UberText> m_labels = new List<UberText>();
        internal enum Section { Collection, PreferredQuality, Filters, Crafting, Other }
        private static readonly int SectionCount = Enum.GetValues(typeof(Section)).Length;
        private DropdownControl m_openSearch, m_display, m_click, m_drag, m_filterRows, m_filterColumns, m_filterModeBehavior;
        private readonly UberText[] m_sectionTitles = new UberText[SectionCount];
        private readonly GameObject[] m_sectionRows = new GameObject[SectionCount];
        private readonly List<MeshRenderer>[] m_sectionArtwork = new List<MeshRenderer>[SectionCount];
        private sealed class SectionBanner
        {
            public UberText Heading;
            public Transform Root;
            public List<MeshRenderer> Artwork;
        }
        private SectionBanner m_nativeSectionBanner, m_nativeHeader;
        private List<MeshRenderer> m_headerArtwork;
        private readonly FilterTrayGeometry[] m_sectionGeometry = new FilterTrayGeometry[SectionCount];
        private readonly Bounds[] m_sectionBounds = new Bounds[SectionCount];
        private float m_headerInkOffsetRatio, m_checkboxInsetRatio = 0.04f;
        private readonly DropdownControl[] m_priorities = new DropdownControl[4];
        private readonly List<DropdownControl> m_controls = new List<DropdownControl>();
        private readonly Dictionary<DropdownControl, Section> m_controlSections = new Dictionary<DropdownControl, Section>();
        private readonly List<Section> m_checkboxSections = new List<Section>();
        private CheckBox m_disenchantPopup, m_neverDustSignaturesDiamonds, m_collapseInCrafting, m_hideRedundant, m_colorCodeCounter, m_collapse, m_rememberFilterModes, m_showClearAll, m_hideEmptyFilters, m_showLogicControls, m_showEditControls;
        private readonly List<CheckBox> m_checkboxes = new List<CheckBox>();
        private readonly List<GameObject> m_checkboxRows = new List<GameObject>();
        private bool m_syncing, m_layoutPending;
        private int m_layoutWidth, m_layoutHeight;
        private Rect m_settingsPanel;
        private int m_openedFrame = -1, m_dropdownClosedFrame = -1;
        private Vector3 m_containerScale;
        private OptionsMenu m_owner;
        private float m_captionGapRatio = 0.1f;
        private const float BottomPaddingRatio = 0.25f;
        public OptionsMenu Owner
        {
            get => m_owner;
            set
            {
                m_owner = value;
                CaptureReferenceSpacing();
                CaptureCheckboxAlignment();
                m_nativeSectionBanner = FindNativeSectionBanner();
                m_nativeHeader = FindNativeHeader();
                if (m_owner != null && m_nativeSectionBanner == null)
                    DebugLog.Warning("Native Options section banner unavailable; keeping plain section headings");
            }
        }
        public bool Ready { get; private set; }

        protected override void Awake()
        {
            try
            {
                m_targetLayer = GameLayer.HighPriorityUI;
                base.Awake();
                Transform nativeBone = GameMenu.Get()?.m_menuBone;
                if (nativeBone == null) throw new InvalidOperationException("Native game menu bone is missing");
                Transform bone = new GameObject("BCM_ModsMenuBone").transform;
                bone.SetParent(nativeBone.parent, false);
                bone.localPosition = nativeBone.localPosition;
                bone.localRotation = nativeBone.localRotation;
                bone.localScale = nativeBone.localScale;
                bone.SetParent(transform, true);
                m_menuParent = bone;
                SetTransform();
                m_containerScale = m_menu.m_buttonContainer.transform.localScale;
                m_useTextSizesInSlices = m_menu.m_useTextSizesInSlices;
                m_menu.m_headerText.GameStringLookup = false;
                m_menu.m_headerText.Text = MenuTitle;
                Ready = true;
            }
            catch (Exception ex) { DebugLog.Exception("Mods menu initialization failed", ex); }
            gameObject.SetActive(false);
        }

        private void CreateControls()
        {
            if (m_display != null) return;
            CopyNativeHeader();
            DropdownControl template = Owner?.m_graphicsQuality ?? Owner?.m_signatureTextDisplayDropdown;
            if (template == null) throw new InvalidOperationException("Native settings dropdown template is missing");
            foreach (Section section in Enum.GetValues(typeof(Section)))
            {
                UberText title = CreateLabel(m_nativeSectionBanner?.Heading ?? FindGraphicsHeading() ?? template.m_selectedItem.m_text,
                    "Section" + section, SectionLabel(section));
                if (m_nativeSectionBanner == null) title.FontSize = (int)(title.FontSize * 1.15f);
                m_sectionTitles[(int)section] = title;
                m_sectionRows[(int)section] = WrapRow(title.gameObject);
                m_sectionArtwork[(int)section] = CopySectionBanner(title, m_sectionRows[(int)section].transform);
                if (m_sectionArtwork[(int)section].Count > 0)
                {
                    GameObject row = m_sectionRows[(int)section];
                    m_sectionGeometry[(int)section] = new FilterTrayGeometry(row, title.transform);
                    bool first = true;
                    foreach (MeshRenderer art in m_sectionArtwork[(int)section])
                    {
                        Bounds part = FilterTrayGeometry.TransformBounds(art.GetComponent<MeshFilter>().sharedMesh.bounds,
                            point => row.transform.InverseTransformPoint(art.transform.TransformPoint(point)));
                        if (first) m_sectionBounds[(int)section] = part;
                        else m_sectionBounds[(int)section].Encapsulate(part);
                        first = false;
                    }
                }
            }
            m_display = CreateDropdown(template, "Card display", "Only one copy at highest quality");
            m_click = CreateDropdown(template, "Click quality", "Add to deck by clicking");
            m_drag = CreateDropdown(template, "Drag quality", "Add to deck by dragging");
            m_display.setItemTextCallback(value => QualityOptions.Label((QualityDisplayMode)value));
            m_click.setItemTextCallback(value => QualityOptions.Label((ClickQualityMode)value));
            m_drag.setItemTextCallback(value => QualityOptions.Label((DragQualityMode)value));
            m_display.setItemChosenCallback((value, previous) => {
                if (m_syncing) return;
                CollectionQualitySettings.Display = (QualityDisplayMode)value;
                RefreshControls();
            });
            m_click.setItemChosenCallback((value, previous) => {
                if (!m_syncing)
                {
                    CollectionQualitySettings.Click = (ClickQualityMode)value;
                    RefreshControls();
                }
            });
            m_drag.setItemChosenCallback((value, previous) => {
                if (!m_syncing) CollectionQualitySettings.Drag = (DragQualityMode)value;
            });
            m_collapse = CreateCheckbox("Collapse", "Collapse quality variants", Section.Collection, value => {
                PreferredPremiumPrefs.CollapseEnabled = value;
                QualityDeckInput.SettingsChanged();
            });
            m_openSearch = CreateDropdown(template, "Collection open search", "Search on collection open");
            m_controlSections[m_openSearch] = Section.Crafting;
            m_openSearch.setItemTextCallback(value => (CollectionOpenSearchMode)value == CollectionOpenSearchMode.NewOrRefund
                ? "New or Refund (Refund first)" : ((CollectionOpenSearchMode)value).ToString());
            m_openSearch.setItemChosenCallback((value, previous) => { if (!m_syncing) CollectionQualitySettings.OpenSearch = (CollectionOpenSearchMode)value; });
            m_disenchantPopup = CreateCheckbox("DisenchantPopup", "Disenchant popup\nExperimental: use with care", Section.Crafting, value => {
                CollectionQualitySettings.DisenchantPopup = value;
                RefreshControls();
            });
            m_neverDustSignaturesDiamonds = CreateCheckbox("NeverDustSignaturesDiamonds", "Never dust\nSignatures/Diamonds", Section.Crafting,
                value => {
                    if (CollectionQualitySettings.DisenchantPopup) CollectionQualitySettings.NeverDustSignaturesDiamonds = value;
                    else RefreshControls();
                });
            m_collapseInCrafting = CreateCheckbox("CollapseInCrafting", "Collapse while crafting", Section.Crafting,
                value => CollectionQualitySettings.CollapseInCrafting = value);
            m_colorCodeCounter = CreateCheckbox("ColorCodeCounter", "Color code counter", Section.Collection, value => {
                CollectionQualitySettings.ColorCodeCounter = value;
            });
            for (int rank = 0; rank < m_priorities.Length; rank++)
            {
                int index = rank;
                DropdownControl control = CreateDropdown(template, "Priority" + (rank + 1),
                    "Priority " + (rank + 1));
                m_priorities[rank] = control;
                m_controlSections[control] = Section.PreferredQuality;
                control.setItemTextCallback(value => PreferredPremiumHelper.PremiumName((TAG_PREMIUM)value));
                control.setItemChosenCallback((value, previous) => {
                    if (m_syncing) return;
                    PreferredPremiumPrefs.SetPriority(index, (TAG_PREMIUM)value);
                    QualityDeckInput.SettingsChanged();
                    RefreshControls();
                });
            }
            m_hideRedundant = CreateCheckbox("HideRedundant", "Hide redundant settings", Section.Other, value => {
                CollectionQualitySettings.HideRedundant = value;
                RefreshControls();
            });
            m_rememberFilterModes = CreateCheckbox("RememberFilterModes", "Remember filter modes", Section.Filters, value => {
                CollectionQualitySettings.RememberFilterModes = value;
            });
            m_filterModeBehavior = CreateDropdown(template, "Filter mode", "Filter mode control");
            m_controlSections[m_filterModeBehavior] = Section.Filters;
            m_filterModeBehavior.setItemTextCallback(value => (FilterModeBehavior)value == FilterModeBehavior.Hidden ? "Hidden"
                : (FilterModeBehavior)value == FilterModeBehavior.Category ? "Whole category" : "Next filter");
            m_filterModeBehavior.setItemChosenCallback((value, previous) => {
                if (!m_syncing) { CollectionQualitySettings.FilterModeBehavior = (FilterModeBehavior)value; RefreshControls(); }
            });
            m_showClearAll = CreateCheckbox("ShowFilterClearAll", "Show Clear All", Section.Filters,
                value => CollectionQualitySettings.ShowFilterClearAll = value);
            m_showLogicControls = CreateCheckbox("ShowLogicControls", "Show Logic Controls", Section.Filters,
                value => CollectionQualitySettings.ShowLogicControls = value);
            m_showEditControls = CreateCheckbox("ShowEditControls", "Show Edit Controls", Section.Filters,
                value => CollectionQualitySettings.ShowEditControls = value);
            m_hideEmptyFilters = CreateCheckbox("HideEmptyFilters", "Hide filters without results", Section.Filters, value => {
                CollectionQualitySettings.HideEmptyFilters = value;
                if (value) CollectionFilterMenu.EnsureWideExpansionPool();
            });
            m_filterRows = CreateDropdown(template, "Filter rows", "Visible filter rows");
            m_filterColumns = CreateDropdown(template, "Filter columns", "Filter columns");
            m_controlSections[m_filterRows] = m_controlSections[m_filterColumns] = Section.Filters;
            m_filterRows.setItemTextCallback(value => (int)value == 0 ? "Automatic" : value.ToString());
            m_filterColumns.setItemTextCallback(value => (int)value == 0 ? "Automatic" : value.ToString());
            m_filterRows.setItemChosenCallback((value, previous) => {
                if (!m_syncing) CollectionQualitySettings.FilterMenuRows = (int)value;
            });
            m_filterColumns.setItemChosenCallback((value, previous) => {
                if (!m_syncing) CollectionQualitySettings.FilterMenuColumns = (int)value;
            });
        }

        private static string SectionLabel(Section section)
            => section == Section.PreferredQuality ? "Preferred Quality" : section == Section.Filters ? "Filter menu" : section == Section.Other ? "Other" : section == Section.Crafting ? "Crafting" : "Card Display / Handling";

        private void CaptureCheckboxAlignment()
        {
            CheckBox source = Owner?.m_fullScreenCheckbox ?? Owner?.m_screenShakeCheckbox;
            DropdownControl field = Owner?.m_graphicsQuality ?? Owner?.m_signatureTextDisplayDropdown;
            Camera camera = field == null ? null : CameraUtils.FindProjectionCameraForObject(field.gameObject);
            if (source == null || camera == null || !TryClosedFieldRect(field, camera, out Rect reference)
                || !TryColliderRect(source.GetComponentsInChildren<Collider>(true), camera, out Rect box)) return;
            float inset = (box.xMin - reference.xMin) / reference.width;
            // The right-hand Screen Shake control can belong to a different
            // column when the Fullscreen reference is unavailable.
            if (inset >= -0.1f && inset <= 0.3f) m_checkboxInsetRatio = inset;
        }

        private CheckBox CreateCheckbox(string name, string caption, Section section, Action<bool> changed)
        {
            CheckBox template = Owner.m_fullScreenCheckbox ?? Owner.m_screenShakeCheckbox;
            if (template == null) throw new InvalidOperationException("Native settings checkbox template is missing");
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
            go.name = "BCM_Mods" + name;
            go.transform.SetParent(m_menu.m_buttonContainer.transform, true);
            CheckBox checkbox = go.GetComponent<CheckBox>();
            if (checkbox.m_uberText != null)
            {
                checkbox.m_uberText.GameStringLookup = false;
                checkbox.m_uberText.WordWrap = false;
                checkbox.m_uberText.ResizeToFit = false;
                checkbox.m_uberText.ResizeToFitAndGrow = false;
                checkbox.m_uberText.Alignment = UberText.AlignmentOptions.Left;
            }
            checkbox.SetButtonText(caption);
            checkbox.SetEnabled(true, true);
            FilterButtonFeedback.BindPressSound(checkbox);
            checkbox.AddEventListener(UIEventType.RELEASE, _ => {
                if (!m_syncing) changed(checkbox.IsChecked());
            });
            go.SetActive(true);
            LayerUtils.SetLayer(checkbox, m_targetLayer);
            m_checkboxes.Add(checkbox);
            m_checkboxSections.Add(section);
            m_checkboxRows.Add(WrapRow(go));
            return checkbox;
        }

        private DropdownControl CreateDropdown(DropdownControl template, string id, string caption)
        {
            // Preserve the native control's orientation and scale across the
            // Options hierarchy and our independent native menu rig.
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
            go.name = "BCM_Mods" + id.Replace(" ", "");
            go.transform.SetParent(m_menu.m_buttonContainer.transform, true);
            DropdownControl control = go.GetComponent<DropdownControl>();
            KeepDropdownParts(control);
            control.setItemChosenCallback(null);
            control.setMenuShownCallback(shown => { if (!shown) m_dropdownClosedFrame = Time.frameCount; });
            control.clearItems();
            // Source items are cloned as children, but are not in the clone's
            // freshly initialized, nonserialized runtime item list.
            Transform items = control.m_menuItemContainer.transform;
            for (int i = items.childCount - 1; i >= 0; i--)
            {
                Transform child = items.GetChild(i);
                if (child == control.m_menuItemTemplate.transform) continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
            foreach (UberText text in go.GetComponentsInChildren<UberText>(true))
            {
                text.GameStringLookup = false;
                if (text != control.m_selectedItem.m_text && !text.transform.IsChildOf(control.m_menu.transform)
                    && !text.transform.IsChildOf(control.m_menuItemTemplate.transform)) text.Text = string.Empty;
            }
            control.m_menu.gameObject.SetActive(false);
            control.m_cancelCatcher.gameObject.SetActive(false);
            DropdownScrollbar.Attach(control);
            go.SetActive(true);
            LayerUtils.SetLayer(control, m_targetLayer);

            // A dropdown's selected-value text can share its object with the
            // framed field. Use the plain Graphics caption for headings instead.
            UberText heading = FindGraphicsHeading() ?? control.m_selectedItem.m_text;
            UberText label = CreateLabel(heading, id, caption);
            label.Alignment = UberText.AlignmentOptions.Left;
            label.UpdateNow(true);
            GameObject labelGo = label.gameObject;
            m_controls.Add(control);
            m_controlSections[control] = Section.Collection;
            m_rows.Add(WrapRow(labelGo));
            m_rows.Add(WrapRow(go));
            m_labels.Add(label);
            return control;
        }

        private UberText CreateLabel(UberText heading, string id, string caption)
        {
            // Fresh text avoids copying the source caption's stale glyph meshes.
            GameObject labelGo = new GameObject("BCM_ModsLabel" + id.Replace(" ", ""));
            labelGo.transform.SetParent(heading.transform.parent, false);
            labelGo.transform.localRotation = heading.transform.localRotation;
            labelGo.transform.localScale = heading.transform.localScale;
            labelGo.transform.SetParent(m_menu.m_buttonContainer.transform, true);
            UberText label = labelGo.AddComponent<UberText>();
            label.TrueTypeFont = heading.TrueTypeFont;
            label.FontSize = heading.FontSize;
            label.CharacterSize = heading.CharacterSize;
            label.TextColor = heading.TextColor;
            label.BoldSize = heading.BoldSize;
            label.RenderQueue = heading.RenderQueue;
            label.RenderToTexture = false;
            label.WordWrap = false;
            label.ResizeToFitAndGrow = false;
            label.ResizeToFit = false;
            label.RichText = false;
            label.GameStringLookup = false;
            label.Alignment = heading.Alignment;
            label.Anchor = UberText.AnchorOptions.Middle;
            label.Text = caption;
            label.UpdateNow(true);
            foreach (Collider collider in labelGo.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            labelGo.SetActive(true);
            LayerUtils.SetLayer(label, m_targetLayer);
            return label;
        }

        private GameObject WrapRow(GameObject content)
        {
            // A neutral parent lets placement move the complete field or caption
            // without altering the native artwork's rotation or scale.
            GameObject row = new GameObject(content.name + "Row");
            row.layer = content.layer;
            row.transform.SetParent(m_menu.m_buttonContainer.transform, false);
            content.transform.SetParent(row.transform, true);
            content.transform.localPosition = Vector3.zero;
            return row;
        }

        private UberText FindGraphicsHeading() => FindPlainHeading(Owner?.m_graphicsQuality);

        private SectionBanner FindNativeSectionBanner()
        {
            DropdownControl reference = Owner?.m_graphicsQuality;
            Camera camera = reference == null ? null : CameraUtils.FindProjectionCameraForObject(reference.gameObject);
            if (camera == null || !TryClosedFieldRect(reference, camera, out Rect field)) return null;
            SectionBanner best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (UberText heading in Owner.GetComponentsInChildren<UberText>(true))
            {
                // Field captions are gold and can overlap a nearby banner's
                // bounds. Only the native white section heading is a template.
                Color color = heading.TextColor;
                if (string.IsNullOrEmpty(heading.Text) || IsNativeControlPart(heading.transform)
                    || color.r < 0.7f || color.g < 0.7f || color.b < 0.7f
                    || !CounterTextLayout.TryScreenInkRect(heading, camera, out Rect ink)
                    || Mathf.Abs(ink.center.x - field.center.x) > field.width * 0.6f) continue;
                float distance = ink.yMin - field.yMax;
                // The section banner sits above the field's own small caption.
                if (distance < field.height * 0.15f || distance >= bestDistance) continue;
                for (Transform scope = heading.transform.parent; scope != null; scope = scope.parent)
                {
                    var artwork = new List<MeshRenderer>();
                    Rect complete = default(Rect);
                    foreach (MeshRenderer renderer in scope.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!renderer.enabled || renderer.GetComponentInParent<UberText>() != null
                            || IsNativeControlPart(renderer.transform) || !TryArtworkRect(renderer, camera, out Rect art)) continue;
                        if (art.height > field.height * 2f || art.width > field.width * 3f
                            || art.yMin > ink.yMax + field.height * 0.2f || art.yMax < ink.yMin - field.height * 0.2f
                            || Mathf.Abs(art.center.x - ink.center.x) > field.width * 0.7f) continue;
                        complete = artwork.Count == 0 ? art : UnionRect(complete, art);
                        artwork.Add(renderer);
                    }
                    float left = ink.center.x - complete.xMin, right = complete.xMax - ink.center.x;
                    if (artwork.Count > 0 && left > 0f && right > 0f
                        && Mathf.Min(left, right) >= Mathf.Max(left, right) * 0.6f
                        && Mathf.Abs(ink.center.y - complete.center.y) <= complete.height * 0.45f)
                    {
                        best = new SectionBanner { Heading = heading, Root = scope, Artwork = artwork };
                        bestDistance = distance;
                        break;
                    }
                    if (scope == Owner.transform) break;
                }
            }
            return best;
        }

        private SectionBanner FindNativeHeader()
        {
            Camera camera = Owner == null ? null : CameraUtils.FindProjectionCameraForObject(Owner.gameObject);
            DropdownControl field = Owner?.m_graphicsQuality ?? Owner?.m_signatureTextDisplayDropdown;
            if (camera == null || field == null || !TryClosedFieldRect(field, camera, out Rect reference)) return null;
            float above = m_nativeSectionBanner != null
                && CounterTextLayout.TryScreenInkRect(m_nativeSectionBanner.Heading, camera, out Rect section)
                ? section.yMax : reference.yMax;
            foreach (UberText title in Owner.GetComponentsInChildren<UberText>(true))
            {
                if (IsNativeControlPart(title.transform) || string.IsNullOrEmpty(title.Text)
                    || !CounterTextLayout.TryScreenInkRect(title, camera, out Rect ink) || ink.yMin <= above) continue;
                for (Transform scope = title.transform.parent; scope != null; scope = scope.parent)
                {
                    var art = new List<MeshRenderer>();
                    foreach (MeshRenderer renderer in scope.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!renderer.enabled || renderer.GetComponentInParent<UberText>() != null
                            || IsNativeControlPart(renderer.transform) || !TryArtworkRect(renderer, camera, out Rect bounds)
                            || bounds.height > reference.height * 2.5f
                            || bounds.yMin > ink.yMax || bounds.yMax < ink.yMin) continue;
                        art.Add(renderer);
                    }
                    if (TryBannerRect(art, camera, out Rect banner) && banner.width > reference.width
                        && banner.width < reference.width * 3f
                        && ink.center.x > banner.xMin && ink.center.x < banner.xMax)
                        return new SectionBanner { Heading = title, Root = scope, Artwork = art };
                    if (scope == Owner.transform) break;
                }
            }
            return null;
        }

        private static bool TryBannerRect(List<MeshRenderer> artwork, Camera camera, out Rect rect)
        {
            rect = default(Rect);
            bool found = false;
            foreach (MeshRenderer renderer in artwork)
                if (TryArtworkRect(renderer, camera, out Rect art))
                {
                    rect = found ? UnionRect(rect, art) : art;
                    found = true;
                }
            return found;
        }

        private void CopyNativeHeader()
        {
            if (m_nativeHeader == null) return;
            UberText title = m_menu.m_headerText, source = m_nativeHeader.Heading;
            Camera camera = CameraUtils.FindProjectionCameraForObject(source.gameObject);
            if (camera != null && CounterTextLayout.TryScreenInkRect(source, camera, out Rect ink)
                && TryBannerRect(m_nativeHeader.Artwork, camera, out Rect banner))
                m_headerInkOffsetRatio = (ink.center.y - banner.center.y) / banner.height;
            foreach (MeshRenderer old in m_menu.m_header.GetComponentsInChildren<MeshRenderer>(true))
                if (old.GetComponentInParent<UberText>() == null) old.enabled = false;
            title.TrueTypeFont = source.TrueTypeFont;
            title.FontSize = source.FontSize;
            title.CharacterSize = source.CharacterSize;
            title.BoldSize = source.BoldSize;
            title.TextColor = source.TextColor;
            title.GradientUpperColor = source.GradientUpperColor;
            title.GradientLowerColor = source.GradientLowerColor;
            title.Alignment = UberText.AlignmentOptions.Center;
            title.Anchor = UberText.AnchorOptions.Middle;
            title.WordWrap = title.ResizeToFit = title.ResizeToFitAndGrow = title.RenderToTexture = false;
            title.transform.rotation = source.transform.rotation;
            Vector3 scale = source.transform.lossyScale, parent = title.transform.parent.lossyScale;
            title.transform.localScale = new Vector3(scale.x / parent.x, scale.y / parent.y, scale.z / parent.z);
            title.Text = MenuTitle;
            title.UpdateNow(true);
            m_headerArtwork = CopyBanner(m_nativeHeader, title, m_menu.m_header.transform, "BCM_SettingsHeaderBanner");
        }

        private static bool IsNativeControlPart(Transform node)
        {
            for (; node != null; node = node.parent)
                if (node.GetComponent<DropdownControl>() != null || node.GetComponent<CheckBox>() != null
                    || node.GetComponent<UIBButton>() != null) return true;
            return false;
        }

        private List<MeshRenderer> CopySectionBanner(UberText title, Transform row)
            => CopyBanner(m_nativeSectionBanner, title, row, "BCM_SectionBanner");

        private List<MeshRenderer> CopyBanner(SectionBanner template, UberText title, Transform row, string name)
        {
            var copies = new List<MeshRenderer>();
            if (template == null) return copies;
            Transform root = new GameObject(name).transform;
            root.SetParent(row, false);
            Transform sourceRoot = template.Root;
            root.position = title.transform.position + sourceRoot.position - template.Heading.transform.position;
            root.rotation = sourceRoot.rotation;
            Vector3 parentScale = root.parent.lossyScale, sourceScale = sourceRoot.lossyScale;
            root.localScale = new Vector3(sourceScale.x / parentScale.x,
                sourceScale.y / parentScale.y, sourceScale.z / parentScale.z);
            var nodes = new Dictionary<Transform, Transform> { { sourceRoot, root } };
            foreach (MeshRenderer source in template.Artwork)
            {
                // Keep the banner's local transform chain. Its left/right
                // halves can share a mesh and use mirrored parent transforms.
                Transform part = CopyBannerTransform(source.transform, sourceRoot, root, nodes);
                part.gameObject.layer = title.gameObject.layer;
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                MeshRenderer copy = part.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = source.sharedMaterials;
                copy.sortingLayerID = source.sortingLayerID;
                copy.sortingOrder = source.sortingOrder;
                var properties = new MaterialPropertyBlock();
                source.GetPropertyBlock(properties);
                copy.SetPropertyBlock(properties);
                copies.Add(copy);
            }
            return copies;
        }

        private static Transform CopyBannerTransform(Transform source, Transform sourceRoot, Transform root,
            Dictionary<Transform, Transform> nodes)
        {
            if (nodes.TryGetValue(source, out Transform copy)) return copy;
            Transform parent = CopyBannerTransform(source.parent, sourceRoot, root, nodes);
            copy = new GameObject(source.gameObject.name).transform;
            copy.gameObject.layer = root.gameObject.layer;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            nodes.Add(source, copy);
            return copy;
        }

        private static Rect UnionRect(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin),
            Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        private static bool TryArtworkRect(MeshRenderer renderer, Camera camera, out Rect rect)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            rect = default(Rect);
            if (mesh == null) return false;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            Bounds bounds = mesh.bounds;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 point = renderer.transform.TransformPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z));
                Vector3 screen = camera.WorldToScreenPoint(point);
                left = Mathf.Min(left, screen.x); right = Mathf.Max(right, screen.x);
                bottom = Mathf.Min(bottom, screen.y); top = Mathf.Max(top, screen.y);
            }
            rect = Rect.MinMaxRect(left, bottom, right, top);
            return rect.width > 0f && rect.height > 0f;
        }

        private void CaptureReferenceSpacing()
        {
            DropdownControl reference = Owner?.m_signatureTextDisplayDropdown;
            Camera camera = reference == null ? null : CameraUtils.FindProjectionCameraForObject(reference.gameObject);
            if (camera == null || !TryClosedFieldRect(reference, camera, out Rect field)) return;
            UberText caption = FindPlainHeading(reference, camera, field);
            if (caption != null && CounterTextLayout.TryScreenInkRect(caption, camera, out Rect heading))
            {
                float ratio = Mathf.Max(0f, heading.yMin - field.yMax) / field.height;
                if (!float.IsInfinity(ratio) && !float.IsNaN(ratio)) m_captionGapRatio = ratio;
            }
        }

        private UberText FindPlainHeading(DropdownControl reference, Camera camera = null, Rect fieldRect = default(Rect))
        {
            Transform scope = reference?.transform;
            while (scope != null && scope != Owner.transform)
            {
                UberText closest = null;
                float distance = float.PositiveInfinity;
                DropdownControl[] controls = scope.GetComponentsInChildren<DropdownControl>(true);
                foreach (UberText text in scope.GetComponentsInChildren<UberText>(true))
                {
                    if (!text.gameObject.activeSelf || string.IsNullOrEmpty(text.Text)
                        || text.GetComponentsInChildren<DropdownControl>(true).Length > 0) continue;
                    bool fieldText = false;
                    foreach (DropdownControl field in controls)
                    {
                        foreach (Component part in new Component[] { field.m_selectedItem, field.m_button,
                            field.m_menu, field.m_menuItemTemplate, field.m_cancelCatcher })
                            if (part != null && text.transform.IsChildOf(part.transform)) fieldText = true;
                    }
                    if (fieldText) continue;
                    if (camera == null) return text;
                    if (CounterTextLayout.TryScreenInkRect(text, camera, out Rect heading) && heading.yMin >= fieldRect.yMax)
                    {
                        float gap = heading.yMin - fieldRect.yMax;
                        // A caption belongs just above this field, rather than
                        // to a different section farther up the Options pane.
                        if (gap > fieldRect.height || heading.xMax < fieldRect.xMin || heading.xMin > fieldRect.xMax) continue;
                        if (gap < distance) { distance = gap; closest = text; }
                    }
                }
                if (closest != null) return closest;
                scope = scope.parent;
            }
            return null;
        }

        private static void KeepDropdownParts(DropdownControl control)
        {
            // Options fields may contain a complete section, including its
            // heading, help text and unrelated controls. Only retain the native
            // dropdown's explicitly referenced parts and their parent paths.
            var parts = new List<Transform>();
            foreach (Component part in new Component[] { control.m_selectedItem, control.m_button,
                control.m_cancelCatcher, control.m_menu, control.m_menuItemContainer, control.m_menuItemTemplate })
                if (part != null) parts.Add(part.transform);
            if (control.m_menuMiddle != null) parts.Add(control.m_menuMiddle.transform);
            PruneDropdown(control.transform, parts);
        }

        private static void PruneDropdown(Transform parent, List<Transform> parts)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                bool keep = false, complete = false;
                foreach (Transform part in parts)
                {
                    if (child == part) { keep = complete = true; break; }
                    if (part.IsChildOf(child)) keep = true;
                }
                if (!keep)
                {
                    child.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else if (!complete) PruneDropdown(child, parts);
            }
        }

        private void RefreshControls()
        {
            RestoreSettingsScrollRows();
            m_syncing = true;
            try
            {
                CloseDropdowns();
                m_display.clearItems();
                foreach (QualityDisplayMode value in new[] { QualityDisplayMode.Split, QualityDisplayMode.Stacking,
                    QualityDisplayMode.HighestOnly, QualityDisplayMode.TwoHighest, QualityDisplayMode.PreferTwoCopies }) m_display.addItem(value);
                m_display.setSelection(CollectionQualitySettings.Display);
                bool split = QualityOptions.HasMergedTiles;
                m_click.clearItems();
                foreach (ClickQualityMode value in Enum.GetValues(typeof(ClickQualityMode)))
                    if (split || value != ClickQualityMode.ClickedHalf) m_click.addItem(value);
                m_click.setSelection(QualityOptions.EffectiveClick);
                m_click.SetEnabled(true, true);
                m_click.m_selectedItem.SetEnabled(true, true);
                m_click.m_button.SetEnabled(true, true);
                m_drag.clearItems();
                foreach (DragQualityMode value in new[] { DragQualityMode.DraggedHalf, DragQualityMode.ChooseOnDrop, DragQualityMode.SameAsClick })
                    if (split || value != DragQualityMode.DraggedHalf) m_drag.addItem(value);
                m_drag.setSelection(QualityOptions.EffectiveDrag);
                // Separate quality tiles already provide an explicit choice.
                // Preserve the saved policies for returning to a merged mode.
                for (int rank = 0; rank < m_priorities.Length; rank++)
                {
                    DropdownControl control = m_priorities[rank];
                    control.clearItems();
                    foreach (TAG_PREMIUM premium in PreferredPremiumPrefs.Order) control.addItem(premium);
                    control.setSelection(PreferredPremiumPrefs.Order[rank]);
                }
                m_filterRows.clearItems();
                m_filterRows.addItem(0);
                for (int rows = 1; rows <= CollectionQualitySettings.MaxFilterRows; rows++) m_filterRows.addItem(rows);
                m_filterRows.setSelection(CollectionQualitySettings.FilterMenuRows);
                m_filterColumns.clearItems();
                m_filterColumns.addItem(0);
                for (int columns = 1; columns <= CollectionQualitySettings.MaxFilterColumns; columns++) m_filterColumns.addItem(columns);
                m_filterColumns.setSelection(CollectionQualitySettings.FilterMenuColumns);
                for (int i = 0; i < m_controls.Count; i++)
                {
                    DropdownControl control = m_controls[i];
                    bool visible = true;
                    if (control == m_click) visible &= QualityOptions.ShowClickSettings;
                    if (control == m_drag) visible &= QualityOptions.ShowDragSettings;
                    m_rows[i * 2].SetActive(visible);
                    m_rows[i * 2 + 1].SetActive(visible);
                }
                for (int i = 0; i < m_checkboxes.Count; i++)
                    m_checkboxRows[i].SetActive(m_checkboxes[i] != m_neverDustSignaturesDiamonds
                        || CollectionQualitySettings.DisenchantPopup || !CollectionQualitySettings.HideRedundant);
                m_disenchantPopup.SetChecked(CollectionQualitySettings.DisenchantPopup);
                m_neverDustSignaturesDiamonds.SetEnabled(CollectionQualitySettings.DisenchantPopup, true);
                m_hideRedundant.SetChecked(CollectionQualitySettings.HideRedundant);
                m_openSearch.clearItems();
                foreach (CollectionOpenSearchMode value in Enum.GetValues(typeof(CollectionOpenSearchMode))) m_openSearch.addItem(value);
                m_openSearch.setSelection(CollectionQualitySettings.OpenSearch);
                m_neverDustSignaturesDiamonds.SetChecked(CollectionQualitySettings.NeverDustSignaturesDiamonds);
                m_collapseInCrafting.SetChecked(CollectionQualitySettings.CollapseInCrafting);
                m_colorCodeCounter.SetChecked(CollectionQualitySettings.ColorCodeCounter);
                m_collapse.SetChecked(PreferredPremiumPrefs.CollapseEnabled);
                m_rememberFilterModes.SetChecked(CollectionQualitySettings.RememberFilterModes);
                m_filterModeBehavior.clearItems();
                foreach (FilterModeBehavior value in Enum.GetValues(typeof(FilterModeBehavior))) m_filterModeBehavior.addItem(value);
                m_filterModeBehavior.setSelection(CollectionQualitySettings.FilterModeBehavior);
                m_showClearAll.SetChecked(CollectionQualitySettings.ShowFilterClearAll);
                m_showLogicControls.SetChecked(CollectionQualitySettings.ShowLogicControls);
                m_showEditControls.SetChecked(CollectionQualitySettings.ShowEditControls);
                m_hideEmptyFilters.SetChecked(CollectionQualitySettings.HideEmptyFilters);
                m_layoutPending = true;
            }
            finally { m_syncing = false; }
            if (IsShown()) LayoutMenu();
        }

        protected override List<UIBButton> GetButtons() => m_buttons;

        protected override void LayoutMenu()
        {
            if (m_labels.Count == 0) { base.LayoutMenu(); return; }
            RestoreSettingsScrollRows();
            // Raw UberText meshes can live in an offscreen render scene. They
            // must not contribute thousands of units to native slice heights.
            m_menu.m_buttonContainer.ClearSlices();
            Transform container = m_menu.m_buttonContainer.transform;
            container.localScale = m_containerScale;
            Camera camera = CameraUtils.FindProjectionCameraForObject(container.gameObject);
            var visible = new List<int>();
            var all = new List<int>();
            for (int i = 0; i < m_labels.Count; i++)
            {
                all.Add(i);
                if (m_rows[i * 2].activeSelf) visible.Add(i);
            }
            var visibleChecks = new List<int>();
            for (int i = 0; i < m_checkboxes.Count; i++) if (m_checkboxRows[i].activeSelf) visibleChecks.Add(i);
            var headings = new Rect[m_controls.Count];
            var fields = new Rect[m_controls.Count];
            var checkboxes = new Rect[visibleChecks.Count];
            if (camera == null || !MeasureRows(camera, all, visibleChecks, headings, fields, checkboxes))
            {
                m_layoutPending = true;
                return;
            }
            var titles = new Rect[m_sectionTitles.Length];
            if (!MeasureSectionTitles(camera, titles)) { m_layoutPending = true; return; }
            Vector3 origin = camera.WorldToScreenPoint(container.position);
            float unit = fields[visible[0]].height;
            float columnWidth = ColumnWidth(camera, all, headings, fields, checkboxes, titles, unit);
            float width = columnWidth * 2f + unit * 0.6f;
            var originalFields = all.FindAll(i => m_controls[i] != m_filterModeBehavior && m_controls[i] != m_openSearch);
            var originalChecks = visibleChecks.FindAll(i => m_checkboxes[i] != m_showClearAll && m_checkboxes[i] != m_hideEmptyFilters
                && m_checkboxes[i] != m_showLogicControls && m_checkboxes[i] != m_showEditControls && m_checkboxes[i] != m_neverDustSignaturesDiamonds && m_checkboxes[i] != m_disenchantPopup);
            // Keep the established viewport; Crafting and additional filter controls scroll inside it.
            float height = MenuHeight(originalFields, originalChecks, headings, fields, OriginalCheckboxBounds(originalChecks, visibleChecks, checkboxes), titles, unit);
            bool hasHeader = TryMenuHeaderRect(camera, out Rect header);
            float headerHeight = hasHeader ? Mathf.Max(0f, header.yMax - origin.y) : 0f;
            float available = Screen.height * 0.8f - headerHeight;
            float scale = Mathf.Min(1f, Mathf.Min(available / height, Screen.width * 0.9f / width));
            if (scale > 0f && scale < 1f)
            {
                container.localScale = m_containerScale * scale;
                if (!MeasureRows(camera, all, visibleChecks, headings, fields, checkboxes)
                    || !MeasureSectionTitles(camera, titles)) { m_layoutPending = true; return; }
                unit = fields[visible[0]].height;
                columnWidth = ColumnWidth(camera, all, headings, fields, checkboxes, titles, unit);
                width = columnWidth * 2f + unit * 0.6f;
                height = MenuHeight(originalFields, originalChecks, headings, fields, OriginalCheckboxBounds(originalChecks, visibleChecks, checkboxes), titles, unit);
            }
            float left = origin.x - (columnWidth + unit * 0.6f) * 0.5f;
            float right = origin.x + (columnWidth + unit * 0.6f) * 0.5f;
            float collectionBottom = PlaceSection(camera, Section.Collection, left, origin.y,
                visible, visibleChecks, headings, fields, checkboxes, titles, unit);
            float qualityBottom = PlaceSection(camera, Section.PreferredQuality, right, origin.y,
                visible, visibleChecks, headings, fields, checkboxes, titles, unit);
            // The left column flows independently of quality priorities, so
            // hiding redundant input fields also moves the filter group up.
            PlaceSection(camera, Section.Filters, left, collectionBottom - unit * 0.75f,
                visible, visibleChecks, headings, fields, checkboxes, titles, unit);
            float craftingBottom = PlaceSection(camera, Section.Crafting, right, qualityBottom - unit * 0.75f,
                visible, visibleChecks, headings, fields, checkboxes, titles, unit);
            PlaceSection(camera, Section.Other, right, craftingBottom - unit * 0.75f,
                visible, visibleChecks, headings, fields, checkboxes, titles, unit);
            float bottom = origin.y - height;
            Vector3 topLeft = camera.ScreenToWorldPoint(new Vector3(origin.x - width * 0.5f, origin.y, origin.z));
            Vector3 topRight = camera.ScreenToWorldPoint(new Vector3(origin.x + width * 0.5f, origin.y, origin.z));
            Vector3 bottomLeft = camera.ScreenToWorldPoint(new Vector3(origin.x - width * 0.5f, bottom, origin.z));
            float worldWidth = Vector3.Distance(topLeft, topRight), worldHeight = Vector3.Distance(topLeft, bottomLeft);
            m_menu.m_background.SetSize(worldWidth, worldHeight);
            m_menu.m_border.SetSize(worldWidth, worldHeight);
            CenterPanel(camera, Rect.MinMaxRect(origin.x - width * 0.5f, bottom,
                origin.x + width * 0.5f, origin.y));
            CaptureSettingsScroll(camera, width, height, unit);
            m_settingsPanel = m_settingsViewport;
            foreach (Component frame in new Component[] { m_menu.m_background, m_menu.m_border, m_menu.m_header })
                foreach (MeshRenderer renderer in frame.GetComponentsInChildren<MeshRenderer>(false))
                    if (renderer.enabled && TryArtworkRect(renderer, camera, out Rect art))
                        m_settingsPanel = UnionRect(m_settingsPanel, art);
            if (TryMenuHeaderRect(camera, out Rect panelHeader)) m_settingsPanel = UnionRect(m_settingsPanel, panelHeader);
            m_layoutWidth = Screen.width;
            m_layoutHeight = Screen.height;
            m_layoutPending = false;
        }

        private bool TryMenuHeaderRect(Camera camera, out Rect rect)
        {
            if (m_headerArtwork != null) PrepareSectionTitle(m_menu.m_headerText, m_headerArtwork, camera, false);
            else m_menu.m_headerText.UpdateNow(true);
            bool found = CounterTextLayout.TryScreenInkRect(m_menu.m_headerText, camera, out rect);
            if (found && m_headerArtwork != null && TryBannerRect(m_headerArtwork, camera, out Rect banner))
            {
                MoveRow(camera, m_menu.m_headerText.transform, rect, banner.center.x,
                    banner.center.y + banner.height * m_headerInkOffsetRatio + rect.height * 0.5f);
                found = CounterTextLayout.TryScreenInkRect(m_menu.m_headerText, camera, out rect);
            }
            foreach (MeshRenderer renderer in m_menu.m_header.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!renderer.enabled || renderer.GetComponentInParent<UberText>() != null
                    || !TryArtworkRect(renderer, camera, out Rect art)) continue;
                rect = found ? UnionRect(rect, art) : art;
                found = true;
            }
            return found;
        }

        private void CenterPanel(Camera camera, Rect body)
        {
            Rect complete = TryMenuHeaderRect(camera, out Rect header) ? UnionRect(body, header) : body;
            // Move the common native panel rig, so header, border, fields and
            // hit areas stay together. The game-menu bone is a top anchor,
            // not the center of a larger settings dialog.
            Transform root = m_menu.m_buttonContainer.transform;
            while (root != null && (!m_menu.m_header.transform.IsChildOf(root)
                || !m_menu.m_headerText.transform.IsChildOf(root))) root = root.parent;
            if (root == null) return;
            Vector3 current = camera.WorldToScreenPoint(root.position);
            Vector3 target = current + new Vector3(Screen.width * 0.5f - complete.center.x,
                Screen.height * 0.5f - complete.center.y, 0f);
            root.position += camera.ScreenToWorldPoint(target) - camera.ScreenToWorldPoint(current);
        }

        private bool MeasureRows(Camera camera, List<int> visible, List<int> visibleChecks, Rect[] headings, Rect[] fields, Rect[] checkboxes)
        {
            DropdownControl[] controls = m_controls.ToArray();
            foreach (int i in visible)
            {
                m_labels[i].UpdateNow(true);
                if (!CounterTextLayout.TryScreenInkRect(m_labels[i], camera, out headings[i])
                    || !TryClosedFieldRect(controls[i], camera, out fields[i], true)) return false;
            }
            for (int n = 0; n < visibleChecks.Count; n++)
            {
                int i = visibleChecks[n];
                if (!TryColliderRect(m_checkboxes[i].GetComponentsInChildren<Collider>(true), camera, out Rect checkbox)) return false;
                UberText label = m_checkboxes[i].m_uberText;
                if (label != null)
                {
                    label.UpdateNow(true);
                    if (!CounterTextLayout.TryScreenInkRect(label, camera, out Rect ink)) return false;
                    checkbox = Rect.MinMaxRect(Mathf.Min(checkbox.xMin, ink.xMin), Mathf.Min(checkbox.yMin, ink.yMin),
                        Mathf.Max(checkbox.xMax, ink.xMax), Mathf.Max(checkbox.yMax, ink.yMax));
                }
                checkboxes[n] = checkbox;
            }
            return true;
        }

        private bool MeasureSectionTitles(Camera camera, Rect[] titles)
        {
            for (int i = 0; i < titles.Length; i++)
            {
                m_sectionGeometry[i]?.Restore();
                m_sectionTitles[i].UpdateNow(true);
                if (m_sectionGeometry[i] != null
                    && CounterTextLayout.TryScreenInkRect(m_sectionTitles[i], camera, out Rect ink)
                    && TryBannerRect(m_sectionArtwork[i], camera, out Rect original))
                {
                    // Leave space inside both ornaments, not just between the
                    // outer mesh bounds and the text.
                    float width = Mathf.Max(original.width, ink.width + original.height * 3f);
                    if (width > original.width + 0.01f)
                    {
                        Bounds source = m_sectionBounds[i];
                        Bounds target = new Bounds(source.center, new Vector3(source.size.x * width / original.width,
                            source.size.y, source.size.z));
                        // Even narrow pieces must use the same vertex map as
                        // the middle, or shared edges open up at the slices.
                        m_sectionGeometry[i].Resize(source, target, source.size.z * 0.9f, null, wideThreshold: 0f);
                    }
                }
                PrepareSectionTitle(m_sectionTitles[i], m_sectionArtwork[i], camera);
                if (!CounterTextLayout.TryScreenInkRect(m_sectionTitles[i], camera, out titles[i])) return false;
                bool found = false;
                Rect banner = default(Rect);
                foreach (MeshRenderer renderer in m_sectionArtwork[i])
                    if (TryArtworkRect(renderer, camera, out Rect art))
                    {
                        banner = found ? UnionRect(banner, art) : art;
                        found = true;
                    }
                if (found)
                {
                    // Place the glyphs inside the banner rather than treating
                    // them as a separate caption below its artwork.
                    MoveRow(camera, m_sectionTitles[i].transform, titles[i], banner.center.x,
                        banner.center.y + titles[i].height * 0.5f);
                    if (!CounterTextLayout.TryScreenInkRect(m_sectionTitles[i], camera, out Rect centered)) return false;
                    titles[i] = UnionRect(banner, centered);
                }
            }
            return true;
        }

        private void PrepareSectionTitle(UberText title, List<MeshRenderer> artwork, Camera camera, bool section = true)
        {
            if (section)
            {
                title.TextColor = Color.white;
                title.GradientUpperColor = title.GradientLowerColor = Color.white;
            }
            int order = 0, layer = 0;
            float depth = float.PositiveInfinity;
            foreach (MeshRenderer renderer in artwork)
            {
                if (renderer.sortingOrder >= order) { order = renderer.sortingOrder; layer = renderer.sortingLayerID; }
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                Bounds bounds = mesh.bounds;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                    depth = Mathf.Min(depth, camera.WorldToScreenPoint(renderer.transform.TransformPoint(new Vector3(
                        x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y,
                        z == 0 ? bounds.min.z : bounds.max.z))).z);
            }
            if (artwork.Count > 0)
            {
                Vector3 point = camera.WorldToScreenPoint(title.transform.position);
                // The translucent brown banner must sit behind the glyphs,
                // including when both renderers share the same UI plane.
                point.z = Mathf.Max(0.05f, depth - 0.001f);
                title.transform.position = camera.ScreenToWorldPoint(point);
                title.RenderQueue = Math.Max((section ? m_nativeSectionBanner : m_nativeHeader)?.Heading.RenderQueue ?? 0, order + 1);
            }
            title.UpdateNow(true);
            if (artwork.Count == 0) return;
            foreach (Renderer output in CounterTextLayout.VisibleRenderers(title))
            {
                output.sortingLayerID = layer;
                output.sortingOrder = order + 1;
                output.gameObject.layer = artwork[0].gameObject.layer;
            }
        }

        private float ColumnWidth(Camera camera, List<int> visible, Rect[] headings, Rect[] fields,
            Rect[] checkboxes, Rect[] titles, float unit)
        {
            float width = 0f;
            foreach (int i in visible)
                width = Mathf.Max(width, Mathf.Max(headings[i].width,
                    2f * ClosedFieldHalfWidth(m_controls[i], camera, fields[i].center.x, fields[i].width * 0.5f)));
            foreach (Rect checkbox in checkboxes) width = Mathf.Max(width, checkbox.width);
            foreach (Rect title in titles) width = Mathf.Max(width, title.width);
            return width + unit * 0.5f;
        }

        private float MenuHeight(List<int> all, List<int> checks, Rect[] headings, Rect[] fields,
            Rect[] checkboxes, Rect[] titles, float unit)
        {
            float left = SectionHeight(Section.Collection, all, checks, headings, fields, checkboxes, titles, unit)
                + unit * 0.75f + SectionHeight(Section.Filters, all, checks, headings, fields, checkboxes, titles, unit);
            float right = SectionHeight(Section.PreferredQuality, all, checks, headings, fields, checkboxes, titles, unit)
                + unit * 0.75f + SectionHeight(Section.Other, all, checks, headings, fields, checkboxes, titles, unit);
            return Mathf.Max(left, right) + unit * BottomPaddingRatio;
        }

        private float FieldHeight(int i, Rect[] headings, Rect[] fields)
            => headings[i].height + fields[i].height * (1f + m_captionGapRatio);

        private float SectionHeight(Section section, List<int> visible, List<int> visibleChecks,
            Rect[] headings, Rect[] fields, Rect[] checkboxes, Rect[] titles, float unit)
        {
            float height = titles[(int)section].height + unit * 0.35f;
            bool first = true;
            foreach (int i in visible)
            {
                if (m_controlSections[m_controls[i]] != section) continue;
                if (!first) height += unit * m_captionGapRatio;
                height += FieldHeight(i, headings, fields);
                first = false;
            }
            first = true;
            for (int n = 0; n < visibleChecks.Count; n++)
            {
                if (m_checkboxSections[visibleChecks[n]] != section) continue;
                height += unit * (first ? 0.5f : 0.25f) + checkboxes[n].height;
                first = false;
            }
            return height;
        }

        private float PlaceField(Camera camera, int i, float centerX, float top, Rect[] headings, Rect[] fields)
        {
            // Native gold captions share the checkbox column's left inset.
            // Place by glyph edges rather than centering captions of different lengths.
            float captionLeft = centerX - fields[i].width * 0.5f + fields[i].width * m_checkboxInsetRatio;
            MoveRow(camera, m_rows[i * 2].transform, headings[i], captionLeft + headings[i].width * 0.5f, top);
            float fieldTop = top - headings[i].height - fields[i].height * m_captionGapRatio;
            MoveRow(camera, m_rows[i * 2 + 1].transform, fields[i], centerX, fieldTop);
            return fieldTop - fields[i].height;
        }

        private float PlaceSection(Camera camera, Section section, float centerX, float top,
            List<int> visible, List<int> visibleChecks, Rect[] headings, Rect[] fields, Rect[] checkboxes,
            Rect[] titles, float unit)
        {
            MoveRow(camera, m_sectionRows[(int)section].transform, titles[(int)section], centerX, top);
            float cursor = top - titles[(int)section].height - unit * 0.35f;
            bool first = true;
            foreach (int i in visible)
            {
                if (m_controlSections[m_controls[i]] != section) continue;
                if (!first) cursor -= unit * m_captionGapRatio;
                cursor = PlaceField(camera, i, centerX, cursor, headings, fields);
                first = false;
            }
            first = true;
            int reference = visible.FindIndex(i => m_controlSections[m_controls[i]] == section);
            reference = reference >= 0 ? visible[reference] : m_controls.IndexOf(m_priorities[0]);
            float checkboxLeft = centerX - fields[reference].width * 0.5f + fields[reference].width * m_checkboxInsetRatio;
            for (int n = 0; n < visibleChecks.Count; n++)
            {
                int i = visibleChecks[n];
                if (m_checkboxSections[i] != section) continue;
                cursor -= unit * (first ? 0.5f : 0.25f);
                if (TryColliderRect(m_checkboxes[i].GetComponentsInChildren<Collider>(true), camera, out Rect hit))
                    MoveRow(camera, m_checkboxRows[i].transform, checkboxes[n],
                        checkboxes[n].center.x + checkboxLeft - hit.xMin, cursor);
                cursor -= checkboxes[n].height;
                first = false;
            }
            return cursor;
        }

        private static void MoveRow(Camera camera, Transform row, Rect current, float centerX, float top)
        {
            Vector3 point = camera.WorldToScreenPoint(row.position);
            Vector3 target = point + new Vector3(centerX - current.center.x, top - current.yMax, 0f);
            row.position += camera.ScreenToWorldPoint(target) - camera.ScreenToWorldPoint(point);
        }

        private static bool TryClosedFieldRect(DropdownControl control, Camera camera, out Rect rect, bool includeInactive = false)
        {
            var colliders = new List<Collider>();
            foreach (Component part in new Component[] { control.m_selectedItem, control.m_button })
                if (part != null) colliders.AddRange(part.GetComponentsInChildren<Collider>(true));
            return TryColliderRect(colliders, camera, out rect, includeInactive);
        }

        private static bool TryColliderRect(IEnumerable<Collider> colliders, Camera camera, out Rect rect, bool includeInactive = false)
        {
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (Collider collider in colliders)
            {
                if (!collider.enabled || (!includeInactive && !collider.gameObject.activeInHierarchy)) continue;
                BoxCollider box = collider as BoxCollider;
                Bounds bounds = box != null ? new Bounds(box.center, box.size) : collider.bounds;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                        y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z);
                    if (box != null) corner = box.transform.TransformPoint(corner);
                    Vector3 screen = camera.WorldToScreenPoint(corner);
                    left = Mathf.Min(left, screen.x); right = Mathf.Max(right, screen.x);
                    bottom = Mathf.Min(bottom, screen.y); top = Mathf.Max(top, screen.y);
                }
            }
            rect = Rect.MinMaxRect(left, bottom, right, top);
            return right > left && top > bottom && !float.IsInfinity(rect.height);
        }

        private static float ClosedFieldHalfWidth(DropdownControl control, Camera camera, float center, float halfWidth)
        {
            foreach (MeshRenderer renderer in control.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                bool excluded = false;
                foreach (Component part in new Component[] { control.m_menu, control.m_cancelCatcher,
                    control.m_menuItemTemplate })
                    if (part != null && renderer.transform.IsChildOf(part.transform)) excluded = true;
                // Raw text meshes can be in a remote render scene. They are
                // unrelated to the visible field's frame dimensions.
                for (Transform node = renderer.transform; node != null; node = node.parent)
                {
                    if (node.GetComponent<UberText>() != null) excluded = true;
                    if (node == control.transform) break;
                }
                if (excluded) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                bool local = filter != null && filter.sharedMesh != null;
                Bounds bounds = local ? filter.sharedMesh.bounds : renderer.bounds;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 corner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                        y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z);
                    if (local) corner = renderer.transform.TransformPoint(corner);
                    float extent = Mathf.Abs(camera.WorldToScreenPoint(corner).x - center);
                    if (!float.IsInfinity(extent) && !float.IsNaN(extent)) halfWidth = Mathf.Max(halfWidth, extent);
                }
            }
            return halfWidth;
        }

        private void LateUpdate()
        {
            if (!IsShown()) return;
            m_updatingSettings = true;
            try
            {
                if (m_layoutPending || m_layoutWidth != Screen.width || m_layoutHeight != Screen.height) LayoutMenu();
                UpdateSettingsScroll();
                foreach (DropdownControl control in m_controls) control.GetComponent<DropdownScrollbar>()?.Tick();
                UpdateSettingsClipping();
                // Native frame colliders can extend beyond the resized art.
                // Use the visible panel even when that collider swallows release.
                if (Input.GetMouseButtonUp(0) && !m_settingsPanel.Contains(Input.mousePosition))
                    OnBlockerRelease(null);
            }
            finally { m_updatingSettings = false; }
        }

        private bool CloseDropdowns()
        {
            bool closed = false;
            foreach (DropdownControl control in m_controls)
            {
                if (control == null || !control.isMenuShown()) continue;
                control.onUserCancelled();
                closed = true;
            }
            return closed;
        }

        public override void Show(bool playSound = true)
        {
            if (IsShown()) return;
            CreateControls();
            RefreshControls();
            m_menu.m_headerText.GameStringLookup = false;
            m_menu.m_headerText.Text = MenuTitle;
            try
            {
                base.Show(playSound);
                m_openedFrame = Time.frameCount;
                s_shown = this;
            }
            catch
            {
                // Native Show activates the controller and acquires its dialog
                // claim before LayoutMenu, but sets IsShown only afterwards.
                // Calling our guarded Hide/OnDestroy cannot undo a failed layout.
                if (gameObject.activeSelf) base.Hide();
                StopSettingsScrollbarDrag();
                if (m_settingsScrollbar != null) m_settingsScrollbar.SetActive(false);
                if (s_shown == this) s_shown = null;
                throw;
            }
        }

        public override void Hide()
        {
            if (!IsShown()) return;
            CloseDropdowns();
            RestoreSettingsScrollRows();
            StopSettingsScrollbarDrag();
            base.Hide();
            if (s_shown == this) s_shown = null;
            if (Owner != null && SceneMgr.Get() != null
                && SceneMgr.Get().GetNextMode() != SceneMgr.Mode.FATAL_ERROR) Owner.Show();
        }

        protected override void OnBlockerRelease(UIEvent e)
        {
            if (!IsShown() || Time.frameCount <= m_openedFrame + 1 || m_dropdownClosedFrame == Time.frameCount) return;
            if (ConsumeSettingsScrollbarRelease()) return;
            foreach (DropdownControl control in m_controls)
                if (control.isMenuShown() && TryColliderRect(control.m_menu.GetComponentsInChildren<Collider>(false), m_settingsCamera, out Rect dropdown)
                    && dropdown.Contains(Input.mousePosition)) return;
            if (!CloseDropdowns() && !m_settingsPanel.Contains(Input.mousePosition)) base.OnBlockerRelease(e);
        }

        public static bool ReturnToOptions()
        {
            if (s_shown == null || !s_shown.IsShown()) return false;
            if (!s_shown.CloseDropdowns()) s_shown.Hide();
            return true;
        }

        protected override void OnDestroy()
        {
            DestroySettingsClipping();
            DestroySettingsScrollbar();
            foreach (FilterTrayGeometry geometry in m_sectionGeometry) geometry?.Dispose();
            if (s_shown == this) s_shown = null;
            if (IsShown()) base.Hide();
            base.OnDestroy();
        }
    }
}
