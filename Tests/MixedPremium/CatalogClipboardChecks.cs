using System;
using System.Linq;
using System.Text;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckCatalogClipboardChoices(KeywordSetTray.GridFixture fixture)
    {
        string clipboard = GUIUtility.systemCopyBuffer;
        bool logic = CollectionQualitySettings.ShowLogicControls, edit = CollectionQualitySettings.ShowEditControls;
        FilterTrayKind kind = KeywordSetTray.ActiveKind;
        SetFilterItem clicked = null;
        void Click(string name)
        {
            MelonLoader.MelonCoroutines.Complete(); fixture.ClearGesture();
            if (!fixture.PinnedItems.Any(item => item.gameObject.name == name)) ClickFilterTool(fixture, "BCM_ShowEditControls");
            clicked = fixture.PinnedItems.Single(item => item.gameObject.name == name);
            if (clicked.m_mouseOverGlow == null) { clicked.m_mouseOverGlow = new GameObject("Test hovered tool"); clicked.m_mouseOverGlow.transform.SetParent(clicked.transform, false); }
            if (clicked.m_pressedShadow == null) { clicked.m_pressedShadow = new GameObject("Test pressed tool"); clicked.m_pressedShadow.transform.SetParent(clicked.transform, false); }
            clicked.m_mouseOverGlow.SetActive(true); clicked.m_pressedShadow.SetActive(true);
            var highlight = clicked.GetComponent<UIBHighlight>() ?? clicked.gameObject.AddComponent<UIBHighlight>();
            if (highlight.m_MouseOverHighlight == null) { highlight.m_MouseOverHighlight = new GameObject("Native hover highlight"); highlight.m_MouseOverHighlight.transform.SetParent(clicked.transform, false); }
            highlight.m_MouseOverHighlight.SetActive(true);
            ClickFilterTool(fixture, name);
        }
        string Conflict(string snapshot, bool package)
        {
            var lines = snapshot.Split('\n');
            int index = Array.FindIndex(lines, line => line.StartsWith(package ? ".combo\t" : ".cards\t"));
            int separator = lines[index].IndexOf('\t');
            var entry = Encoding.UTF8.GetString(Convert.FromBase64String(lines[index].Substring(separator + 1))).Split('\n').ToList();
            if (package) entry[3] = Convert.ToBase64String(Encoding.UTF8.GetBytes("Remote description"));
            else entry.RemoveAt(2);
            lines[index] = lines[index].Substring(0, separator + 1) + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", entry)));
            return string.Join("\n", lines);
        }
        try
        {
            CollectionQualitySettings.ShowEditControls = true;
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Categories); MelonLoader.MelonCoroutines.Complete(); fixture.CloseControls();
            GUIUtility.systemCopyBuffer = "untouched clipboard";
            Click("BCM_ExportClipboard");
            var popup = ButtonListMenu.Visible;
            Check(popup is CatalogTransferMenu && popup.Buttons.Count == 3 && GUIUtility.systemCopyBuffer == "untouched clipboard",
                "Export opens a scope popup and leaves the clipboard untouched until a choice");
            Check(!clicked.m_mouseOverGlow.activeSelf && !clicked.m_pressedShadow.activeSelf && !clicked.GetComponent<UIBHighlight>().m_MouseOverHighlight.activeSelf,
                "Opening a popup immediately clears the source tool's hover glow and pressed shadow");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_TransferCancel").Click();
            Check(!popup.IsShown() && GUIUtility.systemCopyBuffer == "untouched clipboard", "Canceling scope selection leaves the clipboard untouched");
            Click("BCM_ExportClipboard"); popup = ButtonListMenu.Visible;
            popup.Buttons.Single(button => button.gameObject.name == "BCM_TransferFirst").Click();
            Check(GUIUtility.systemCopyBuffer == CategoryCatalog.ExportTransfer(false) && !popup.IsShown(),
                "The All choice exports the full category snapshot and closes its modal");
            string snapshot = GUIUtility.systemCopyBuffer;
            int generation = CategoryCatalog.Generation;
            Click("BCM_ImportClipboard");
            Check(ButtonListMenu.Visible == null && CategoryCatalog.Generation == generation && fixture.PinnedItems.Any(item => item.Text == "No new entries"),
                "Identical imports skip both the conflict popup and redundant writes");
            GUIUtility.systemCopyBuffer = Conflict(snapshot, false);
            Click("BCM_ImportClipboard"); popup = ButtonListMenu.Visible;
            Check(popup is CatalogTransferMenu && popup.Buttons.Any(button => button.m_ButtonText.Text == "Merge")
                && popup.Buttons.Any(button => button.m_ButtonText.Text == "Replace") && CategoryCatalog.Generation == generation,
                "An import offers Merge, Overwrite and Cancel when overwrite would remove local category members");
            Check(!clicked.m_mouseOverGlow.activeSelf && !clicked.m_pressedShadow.activeSelf, "The conflict popup also retires the clicked tool's feedback");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_TransferCancel").Click();
            Check(CategoryCatalog.Generation == generation, "Canceling a conflicting import preserves data");
            Click("BCM_ImportClipboard"); popup = ButtonListMenu.Visible;
            GUIUtility.systemCopyBuffer = "clipboard changed while the decision was open";
            popup.Buttons.Single(button => button.gameObject.name == "BCM_TransferFirst").Click();
            Check(CategoryCatalog.Generation == generation && fixture.PinnedItems.Any(item => item.Text == "No new entries"),
                "Merge uses the captured snapshot and keeps existing members absent from the export");
            Click("BCM_ImportClipboard");
            Check(ButtonListMenu.Visible == null && CategoryCatalog.Generation == generation && fixture.PinnedItems.Any(item => item.Text.Contains("BCM export")),
                "Malformed clipboard contents report an error without an import decision popup or catalog changes");
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Packages); MelonLoader.MelonCoroutines.Complete(); fixture.CloseControls();
            Click("BCM_ExportClipboard"); popup = ButtonListMenu.Visible;
            popup.Buttons.Single(button => button.gameObject.name == "BCM_TransferFirst").Click();
            Check(GUIUtility.systemCopyBuffer == CategoryCatalog.ExportTransfer(true), "The same popup exports packages from the package menu");
            snapshot = GUIUtility.systemCopyBuffer;
            Click("BCM_ImportClipboard");
            Check(ButtonListMenu.Visible == null && CategoryCatalog.Generation == generation, "Identical package imports also proceed without a popup");
            GUIUtility.systemCopyBuffer = Conflict(snapshot, true);
            Click("BCM_ImportClipboard"); popup = ButtonListMenu.Visible;
            Check(popup is CatalogTransferMenu, "Different metadata in an existing package requires a conflict decision");
            CatalogTransferMenu.DestroyMenu();
            Check(!popup.IsShown() && CategoryCatalog.Generation == generation, "Collection teardown cancels a pending import without applying it");
            var target = PackageCatalog.Packages.First();
            GUIUtility.systemCopyBuffer = CategoryCatalog.ExportTransfer(true, entryId: target.Id);
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Packages); MelonLoader.MelonCoroutines.Complete();
            Click("BCM_ImportClipboard");
            Check(ButtonListMenu.Visible == null && !KeywordSetTray.IsShown && PackageViewState.IsActive && PackageViewState.Group == target.PackageGroup
                && PackageViewState.Format == PegasusShared.FormatType.FT_WILD && KeywordFilterApplier.LastAnchor != null,
                "A single package import selects its folder, broadens the format, navigates to its stable entry and dismisses the tray");
            var pages = new FolderPages();
            pages.Pages.Add(new System.Collections.Generic.List<CollectibleCard> { new CollectibleCard { CardId = "before imported package" } });
            pages.Pages.Add(new System.Collections.Generic.List<CollectibleCard> { new CollectibleCard { CardId = "BCM_COMBO_" + target.Id } });
            var key = CollectionPageAnchor.CardKey;
            try
            {
                CollectionPageAnchor.CardKey = card => card.CardId;
                Check(KeywordFilterApplier.LastAnchor.Resolve(pages) == 2, "The import page anchor specifically targets the imported package");
            }
            finally { CollectionPageAnchor.CardKey = key; }
            var comboTarget = new CategoryCatalog.Combo { Id = "imported-combo-target", Category = CuratedCategory.Value };
            typeof(KeywordSetTray).GetMethod("NavigateToImportedEntry", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { comboTarget });
            Check(!PackageViewState.IsActive && ExtraFilterState.HasCategory(CuratedCategory.Value)
                && FilterModes.Get(FilterTrayKind.Categories) == FilterMode.Single && KeywordFilterApplier.LastAnchor != null,
                "A single combo destination returns to Cards and selects its category exclusively");
            pages.Pages[1][0].CardId = "BCM_COMBO_" + comboTarget.Id;
            key = CollectionPageAnchor.CardKey;
            try
            {
                CollectionPageAnchor.CardKey = card => card.CardId;
                Check(KeywordFilterApplier.LastAnchor.Resolve(pages) == 2, "Combo navigation also targets the imported entry's page");
            }
            finally { CollectionPageAnchor.CardKey = key; }


        }
        finally
        {
            CatalogTransferMenu.DestroyMenu(); GUIUtility.systemCopyBuffer = clipboard;
            CollectionQualitySettings.ShowLogicControls = logic; CollectionQualitySettings.ShowEditControls = edit;
            Time.unscaledTime += 4f;
            CollectionFilterMenu.ShowFilter(kind); MelonLoader.MelonCoroutines.Complete(); fixture.ClearGesture();
        }
    }
}
