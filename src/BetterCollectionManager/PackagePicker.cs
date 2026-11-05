using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class PackagePicker : ButtonListMenu
    {
        private static PackagePicker s_instance;
        private readonly List<UIBButton> m_buttons = new List<UIBButton>();
        private readonly List<UIBButton> m_pool = new List<UIBButton>();
        private readonly List<Action> m_actions = new List<Action>();
        private int m_page;
        private bool m_filterGroups;
        protected override void Awake()
        {
            m_targetLayer = GameLayer.HighPriorityUI; base.Awake();
            Transform source = GameMenu.Get()?.m_menuBone ?? m_menu.m_buttonContainer.transform;
            Transform bone = new GameObject("BCM_PackagePickerBone").transform;
            bone.SetParent(source.parent, false); bone.localRotation = source.localRotation; bone.localScale = Vector3.one;
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.m_buttonContainer.gameObject);
            if (camera != null) bone.position = camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, Screen.height * .7f, camera.WorldToScreenPoint(source.position).z));
            bone.SetParent(transform, true); m_menuParent = bone; SetTransform();
            m_useTextSizesInSlices = m_menu.m_useTextSizesInSlices;
            gameObject.SetActive(false);
        }
        internal static void Open()
        {
            bool found = false; foreach (var combo in PackageCatalog.Packages) if (combo.IsPackage) found = true;
            if (!found) throw new InvalidOperationException("No packages saved yet.");
            if (s_instance == null) s_instance = new GameObject("BCM_PackagePicker").AddComponent<PackagePicker>();
            Close(); s_instance.m_page = 0;
            s_instance.m_filterGroups = ExtraFilterState.IsActiveForKind(FilterTrayKind.Packages);
            s_instance.Build(); KeywordSetTray.HideImmediately();
            s_instance.gameObject.SetActive(true); s_instance.Show();
        }
        internal static void Close() { if (s_instance != null && s_instance.IsShown()) s_instance.Hide(); }
        private void Build()
        {
            foreach (UIBButton button in m_pool) button.gameObject.SetActive(false);
            m_buttons.Clear();
            var packages = new List<CategoryCatalog.Combo>();
            foreach (var combo in PackageCatalog.Packages)
                if (!m_filterGroups || ExtraFilterState.MatchesPackage(combo)) packages.Add(combo);
            packages.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
            const int pageSize = 6;
            m_page = Math.Max(0, Math.Min(m_page, Math.Max(0, (packages.Count - 1) / pageSize)));
            m_menu.m_headerText.GameStringLookup = false; m_menu.m_headerText.Text = "Edit Package";
            void Button(string id, string text, Action click)
            {
                int index = m_buttons.Count;
                if (index == m_pool.Count)
                {
                    m_actions.Add(null);
                    m_pool.Add(CreateMenuButton("BCM_PackagePickerSlot" + index, "GLOBAL_BACK", _ => m_actions[index]?.Invoke()));
                }
                UIBButton button = m_pool[index]; FilterButtonFeedback.BindPressSound(button); m_actions[index] = click; button.gameObject.name = id;
                button.m_ButtonText.GameStringLookup = false; button.SetText(text); m_buttons.Add(button);
            }
            for (int i = m_page * pageSize; i < packages.Count && i < (m_page + 1) * pageSize; i++)
            {
                var package = packages[i];
                Button("BCM_EditPackage_" + package.Id, package.Name, () => { Hide(); ComboPopup.EditPackage(package); });
            }
            if (m_page > 0) Button("BCM_PackagesPrevious", "Previous", () => { m_page--; Build(); LayoutMenu(); });
            if ((m_page + 1) * pageSize < packages.Count) Button("BCM_PackagesNext", "Next", () => { m_page++; Build(); LayoutMenu(); });
            Button("BCM_PackagesCancel", "Back", Hide);
        }
        protected override List<UIBButton> GetButtons() => m_buttons;
    }
}
