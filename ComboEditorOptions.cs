using System;
using System.Collections.Generic;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class ComboEditorOptions : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly DropdownControl Frame, MinionType, SpellSchool, Category, Artwork;
        internal string CategoryName { get; private set; }
        internal readonly CheckBox Dragon, Attack, Health;
        private readonly AssetHandle<GameObject> m_source;
        private CategoryCatalog.ComboAppearance m_value;
        private readonly List<Material> m_materials = new List<Material>();
        private bool m_ordered;
        private readonly Dictionary<Renderer, Material[]> m_dropdownMaterials = new Dictionary<Renderer, Material[]>();
        private IEnumerable<DropdownControl> Dropdowns { get { yield return Frame; yield return MinionType; yield return SpellSchool; yield return Category; yield return Artwork; } }
        internal ComboEditorOptions(Transform parent, Action stopInput, Action changed)
        {
            m_source = AssetLoader.Get().LoadAsset<GameObject>("OptionsMenu.prefab:a6e5621068fd7c8429475b3e1a1aa991");
            OptionsMenu source = m_source?.Asset?.GetComponent<OptionsMenu>();
            if (source == null) throw new InvalidOperationException("Native combo controls are unavailable.");
            Root = new GameObject("BCM_ComboOptions"); Root.SetActive(false); Root.transform.SetParent(parent, false);
            DropdownControl template = source.m_graphicsQuality ?? source.m_signatureTextDisplayDropdown;
            DropdownControl Dropdown(string id)
            {
                var control = UnityEngine.Object.Instantiate(template.gameObject, Root.transform, false).GetComponent<DropdownControl>();
                control.gameObject.name = "BCM_Combo" + id;
                control.setItemChosenCallback(null); control.setMenuShownCallback(_ => { }); control.clearItems();
                Transform items = control.m_menuItemContainer.transform;
                for (int i = items.childCount - 1; i >= 0; i--)
                {
                    Transform child = items.GetChild(i);
                    if (child == control.m_menuItemTemplate.transform) continue;
                    child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject);
                }
                foreach (UberText text in control.GetComponentsInChildren<UberText>(true)) text.GameStringLookup = false;
                control.setMenuShownCallback(shown => {
                    if (!shown) return;
                    stopInput();
                    foreach (DropdownControl other in Dropdowns)
                        if (other != null && other != control && other.isMenuShown()) other.onUserCancelled();
                    OrderOpenDropdown(CameraUtils.FindProjectionCameraForObject(Root), parent.gameObject);
                });
                control.m_menu.gameObject.SetActive(false); control.m_cancelCatcher.gameObject.SetActive(false);
                DropdownScrollbar.Attach(control);
                control.gameObject.SetActive(true);
                return control;
            }
            Artwork = Dropdown("Artwork");
            Artwork.setItemTextCallback(choice => m_artworkNames.TryGetValue((string)choice, out string name) ? name : (string)choice);
            Artwork.setItemChosenCallback((choice, previous) => m_artworkChanged?.Invoke((string)choice));
            Category = Dropdown("Category");
            Category.setItemChosenCallback((choice, previous) => CategoryName = (string)choice);
            Frame = Dropdown("Frame");
            foreach (string frame in CategoryCatalog.ComboAppearance.Frames) Frame.addItem(frame);
            Frame.setItemChosenCallback((choice, previous) => {
                if (m_value == null) return;
                m_value.Frame = (string)choice; changed();
            });
            MinionType = Dropdown("MinionType");
            MinionType.setItemTextCallback(choice => {
                foreach (var entry in ExtraFilterCatalog.Races) if (entry.Race == (TAG_RACE)choice) return entry.Name;
                return "None";
            });
            MinionType.addItem((TAG_RACE)0);
            foreach (var entry in ExtraFilterCatalog.Races) MinionType.addItem(entry.Race);
            MinionType.setItemChosenCallback((choice, previous) => { if (m_value != null) m_value.MinionType = (TAG_RACE)choice; });
            SpellSchool = Dropdown("SpellSchool");
            SpellSchool.setItemTextCallback(choice => {
                foreach (var entry in ExtraFilterCatalog.Schools) if (entry.School == (TAG_SPELL_SCHOOL)choice) return entry.Name;
                return "None";
            });
            SpellSchool.addItem((TAG_SPELL_SCHOOL)0);
            foreach (var entry in ExtraFilterCatalog.Schools) SpellSchool.addItem(entry.School);
            SpellSchool.setItemChosenCallback((choice, previous) => { if (m_value != null) m_value.SpellSchool = (TAG_SPELL_SCHOOL)choice; });
            CheckBox Check(string id, string caption, Action<bool> change)
            {
                CheckBox box = UnityEngine.Object.Instantiate((source.m_fullScreenCheckbox ?? source.m_screenShakeCheckbox).gameObject,
                    Root.transform, false).GetComponent<CheckBox>();
                box.gameObject.name = "BCM_Combo" + id;
                if (box.m_uberText != null)
                {
                    box.m_uberText.GameStringLookup = box.m_uberText.WordWrap = box.m_uberText.ResizeToFit = box.m_uberText.ResizeToFitAndGrow = false;
                    box.m_uberText.TextColor = box.m_uberText.GradientUpperColor = box.m_uberText.GradientLowerColor = new Color(.22f, .16f, .1f, 1f);
                }
                box.SetButtonText(caption); box.SetEnabled(true, true);
                FilterButtonFeedback.BindPressSound(box);
                box.AddEventListener(UIEventType.RELEASE, _ => { stopInput(); if (m_value != null) change(box.IsChecked()); });
                box.gameObject.SetActive(true); return box;
            }
            Dragon = Check("Dragon", "Dragon Ornament", value => m_value.Dragon = value);
            Attack = Check("ShowAttack", "Attack", value => m_value.ShowAttack = value);
            Health = Check("ShowHealth", "Health", value => m_value.ShowHealth = value);
            Frame.gameObject.SetActive(true);
            LayerUtils.SetLayer(Root.transform, GameLayer.HighPriorityUI);
        }
        internal void SetCategory(string selected)
        {
            Category.clearItems(); Category.SetEnabled(true, true); Category.m_button.SetEnabled(true, true);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CategoryCatalog.Category category in CategoryCatalog.ComboCategories)
                if (names.Add(category.Name)) Category.addItem(category.Name);
            if (string.IsNullOrEmpty(selected) || !names.Contains(selected)) selected = "Combos";
            CategoryName = selected;
            Category.setSelection(selected);
        }
        internal void SetPackageGroup(PackageKind kind)
        {
            Category.clearItems(); Category.SetEnabled(true, true); Category.m_button.SetEnabled(true, true);
            foreach (var group in PackageCatalog.Groups) Category.addItem(group.Name);
            CategoryName = PackageCatalog.GroupName(kind); Category.setSelection(CategoryName);
        }
        internal void LayoutCategory(Camera camera, Rect area, float depth, GameObject paper, bool only = false)
        {
            Root.SetActive(true);
            if (only)
                foreach (Component control in new Component[] { Frame, MinionType, SpellSchool, Dragon, Attack, Health, Artwork }) control.gameObject.SetActive(false);
            Category.gameObject.SetActive(true);
            Fit(Category.gameObject, Category.m_selectedItem.m_text, area, camera, depth, true);
            ComboSceneGeometry.InFrontOf(Category.gameObject, paper, camera);
            foreach (UberText text in Category.GetComponentsInChildren<UberText>(false))
                ComboSceneGeometry.TextAfterArtwork(text, paper);
        }
        private readonly Dictionary<string, string> m_artworkNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private Action<string> m_artworkChanged;
        internal void SetArtworkChoices(List<PackageArtwork.Choice> choices, string selected, Action<string> changed)
        {
            m_artworkChanged = changed;
            if (Artwork.isMenuShown()) return;
            bool same = m_artworkNames.Count == choices.Count;
            foreach (var choice in choices) same &= m_artworkNames.TryGetValue(choice.Id, out string name) && name == choice.Name;
            if (!same)
            {
                Artwork.clearItems(); m_artworkNames.Clear();
                foreach (var choice in choices) { m_artworkNames[choice.Id] = choice.Name; Artwork.addItem(choice.Id); }
            }
            string selection = selected != null && m_artworkNames.ContainsKey(selected) ? selected : null;
            if (!same || !Equals(Artwork.getSelection(), selection)) Artwork.setSelection(selection);
            bool enabled = choices.Count > 0;
            if (Artwork.IsEnabled() != enabled) Artwork.SetEnabled(enabled, true);
            if (Artwork.m_button.IsEnabled() != enabled) Artwork.m_button.SetEnabled(enabled, true);
        }
        internal void LayoutArtwork(Camera camera, Rect area, float depth, GameObject paper)
        {
            Artwork.gameObject.SetActive(true);
            Fit(Artwork.gameObject, Artwork.m_selectedItem.m_text, area, camera, depth, true);
            ComboSceneGeometry.InFrontOf(Artwork.gameObject, paper, camera);
            foreach (UberText text in Artwork.GetComponentsInChildren<UberText>(false)) ComboSceneGeometry.TextAfterArtwork(text, paper);
        }
        internal void SetValue(CategoryCatalog.ComboAppearance value)
        {
            m_value = value; Frame.setSelection(value.Frame); MinionType.setSelection(value.MinionType); SpellSchool.setSelection(value.SpellSchool);
            Dragon.SetChecked(value.Dragon); Attack.SetChecked(value.ShowAttack); Health.SetChecked(value.ShowHealth);
        }
        internal bool CloseDropdown()
        {
            bool closed = false;
            foreach (DropdownControl control in Dropdowns)
                if (control.isMenuShown()) { control.onUserCancelled(); closed = true; }
            return closed;
        }
        internal bool UpdateDropdownScroll()
        {
            bool consumed = false;
            foreach (DropdownControl control in Dropdowns)
                if (control != null)
                {
                    DropdownScrollbar scroll = control.GetComponent<DropdownScrollbar>();
                    if (scroll != null) { consumed |= scroll.Tick(); consumed |= scroll.ConsumeRelease(); }
                }
            return consumed;
        }
        internal bool ContainsDropdown(Camera camera, Vector3 pointer)
        {
            if (camera == null) return false;
            foreach (DropdownControl control in Dropdowns)
                if (control.isMenuShown() && ComboSceneGeometry.ScreenBounds(control.m_menu.gameObject, camera, out Rect menu)
                    && menu.Contains(pointer)) return true;
            return false;
        }
        internal void OrderOpenDropdown(Camera camera, GameObject dialog)
        {
            if (camera == null || !Root.activeInHierarchy) return;
            foreach (DropdownControl control in Dropdowns)
            {
                if (control == null || !control.isMenuShown()) continue;
                GameObject menu = control.m_menu.gameObject;
                int queue = 0, order = 0;
                foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(dialog))
                {
                    if (renderer.transform.IsChildOf(menu.transform)) continue;
                    order = Math.Max(order, renderer.sortingOrder);
                    if (!ComboSceneGeometry.IsGlyphRenderer(renderer))
                        foreach (Material material in renderer.sharedMaterials)
                            if (material != null) queue = Math.Max(queue, material.renderQueue);
                    ComboSceneGeometry.InFrontOf(menu, renderer, camera);
                }
                // Native rows are created when opening the menu, after the initial layout.
                foreach (Renderer renderer in menu.GetComponentsInChildren<Renderer>(true))
                {
                    if (ComboSceneGeometry.IsGlyphRenderer(renderer)) continue;
                    if (!m_dropdownMaterials.TryGetValue(renderer, out Material[] materials))
                    {
                        materials = renderer.sharedMaterials;
                        for (int i = 0; i < materials.Length; i++)
                            if (materials[i] != null) { materials[i] = new Material(materials[i]); m_materials.Add(materials[i]); }
                        m_dropdownMaterials.Add(renderer, materials);
                    }
                    renderer.sharedMaterials = materials;
                    foreach (Material material in materials) if (material != null) material.renderQueue = queue + 10;
                    renderer.sortingOrder = order + 1;
                }
                foreach (UberText text in menu.GetComponentsInChildren<UberText>(false))
                    ComboSceneGeometry.TextAfterArtwork(text, menu);
                ComboSceneGeometry.TextInFront(menu, camera);
            }
        }
        internal void Layout(Camera camera, Rect frame, Rect minionType, Rect dragon, Rect attack, Rect health, float depth, GameObject paper)
        {
            Root.SetActive(true);
            Frame.gameObject.SetActive(true);
            Dragon.gameObject.SetActive(true);
            Artwork.gameObject.SetActive(false);
            MinionType.gameObject.SetActive(m_value.Frame == "Minion");
            SpellSchool.gameObject.SetActive(m_value.Frame == "Spell");
            Attack.gameObject.SetActive(m_value.HasAttack);
            Health.gameObject.SetActive(m_value.HasSecondary);
            Health.SetButtonText(m_value.SecondaryCaption);
            Fit(Frame.gameObject, Frame.m_selectedItem.m_text, frame, camera, depth, true);
            if (MinionType.gameObject.activeSelf) Fit(MinionType.gameObject, MinionType.m_selectedItem.m_text, minionType, camera, depth, true);
            if (SpellSchool.gameObject.activeSelf) Fit(SpellSchool.gameObject, SpellSchool.m_selectedItem.m_text, minionType, camera, depth, true);
            FitCheck(Dragon, dragon);
            if (m_value.HasAttack) FitCheck(Attack, attack);
            if (m_value.HasSecondary) FitCheck(Health, health);
            void FitCheck(CheckBox checkbox, Rect area)
            {
                float height = area.height * .68f;
                Fit(checkbox.gameObject, checkbox.m_uberText, new Rect(area.xMin, area.center.y - height * .5f, area.width, height), camera, depth);
            }
            if (!m_ordered)
            {
                int queue = 0;
                foreach (Renderer renderer in ComboSceneGeometry.VisibleRenderers(paper))
                    foreach (Material material in renderer.sharedMaterials) if (material != null) queue = Math.Max(queue, material.renderQueue);
                foreach (Renderer renderer in Root.GetComponentsInChildren<Renderer>(true))
                {
                    if (ComboSceneGeometry.IsGlyphRenderer(renderer)) continue;
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null) { var copy = new Material(materials[i]); copy.renderQueue = Math.Max(queue + 10, copy.renderQueue); materials[i] = copy; m_materials.Add(copy); }
                    renderer.sharedMaterials = materials;
                }
                m_ordered = true;
            }
            ComboSceneGeometry.InFrontOf(Root, paper, camera);
            foreach (UberText text in Root.GetComponentsInChildren<UberText>(false))
            {
                ComboSceneGeometry.TextAfterArtwork(text, paper);
                ComboSceneGeometry.TextAfterArtwork(text, Root);
            }
        }
        private static void Fit(GameObject root, UberText text, Rect target, Camera camera, float depth, bool dropdown = false)
        {
            root.transform.position = camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth));
            if (text != null) ComboSceneGeometry.FaceCaption(root.transform, text, camera);
            var control = dropdown ? root.GetComponent<DropdownControl>() : null;
            bool shown = control?.isMenuShown() ?? false;
            if (control != null) { control.m_menu.gameObject.SetActive(false); control.m_cancelCatcher.gameObject.SetActive(false); }
            ComboSceneGeometry.Fit(root.transform, camera, target);
            if (ComboSceneGeometry.ScreenBounds(root, camera, out Rect actual))
                root.transform.position += camera.ScreenToWorldPoint(new Vector3(target.xMin, target.center.y, depth))
                    - camera.ScreenToWorldPoint(new Vector3(actual.xMin, actual.center.y, depth));
            if (dropdown && text != null && CounterTextLayout.TryScreenInkRect(text, camera, out Rect ink) && ink.height > .001f)
            {
                float width = actual.width > .001f ? actual.width : target.width;
                float captionScale = Math.Min(target.height * .38f / ink.height, width * .72f / Math.Max(.001f, ink.width));
                text.transform.localScale *= captionScale;
            }
            if (shown) { control.m_menu.gameObject.SetActive(true); control.m_cancelCatcher.gameObject.SetActive(true); }
        }
        public void Dispose()
        {
            foreach (Material material in m_materials) UnityEngine.Object.Destroy(material);
            if (Root != null) UnityEngine.Object.Destroy(Root);
            m_source?.Dispose();
        }
    }
}
