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

            int selectedArtwork = -1, artworkCanceled = 0;
            Action<int> selectArtwork = index => selectedArtwork = index;
            var artworkLabels = Enumerable.Range(0, 35).Select(index => index == 0 ? "Automatic" : "Artwork " + index).ToList();
            Action closeArtwork = CatalogTransferMenu.OpenChoices("Choose background artwork", artworkLabels, selectArtwork, () => artworkCanceled++);
            popup = ButtonListMenu.Visible;
            Check(popup is CatalogTransferMenu && popup.Buttons.Count == 32
                && popup.Buttons[0].m_ButtonText.Text == "Automatic" && selectedArtwork == -1,
                "Export artwork opens a modal with Automatic, 30 choices per page in five columns, navigation and Cancel.");
            var firstChoice = popup.Buttons.Single(button => button.gameObject.name == "BCM_ArtworkChoice0");
            var secondChoice = popup.Buttons.Single(button => button.gameObject.name == "BCM_ArtworkChoice1");
            var sixthChoice = popup.Buttons.Single(button => button.gameObject.name == "BCM_ArtworkChoice5");
            Check(ComboSceneGeometry.ScreenBounds(firstChoice.m_RootObject, CameraUtils.Camera, out Rect firstRect)
                && ComboSceneGeometry.ScreenBounds(secondChoice.m_RootObject, CameraUtils.Camera, out Rect secondRect)
                && ComboSceneGeometry.ScreenBounds(sixthChoice.m_RootObject, CameraUtils.Camera, out Rect sixthRect)
                && firstRect.xMax < secondRect.xMin && sixthRect.yMax < firstRect.yMin,
                "Artwork choices have separate hit faces in five columns and six rows.");
            Check(firstChoice.GetComponent<BoxCollider>().enabled
                && CounterTextLayout.TryScreenInkRect(firstChoice.m_ButtonText, CameraUtils.Camera, out Rect choiceInk)
                && choiceInk.height >= Screen.height * .02f,
                "Artwork captions remain legible at the screen height, with an active matching click target.");
            var artworkLate = typeof(CatalogTransferMenu).GetMethod("LateUpdate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Vector3 restingScale = firstChoice.transform.localScale;
            Quaternion restingRotation = firstChoice.m_RootObject.transform.localRotation;
            firstChoice.Hover();
            for (int frame = 0; frame < 60; frame++) artworkLate.Invoke(popup, null);
            Check(firstChoice.m_RootObject.transform.localRotation.Equals(restingRotation)
                && Vector3.Distance(firstChoice.transform.localScale, restingScale) < .001f
                && CounterTextLayout.TryScreenInkRect(firstChoice.m_ButtonText, CameraUtils.Camera, out choiceInk)
                && choiceInk.height >= Screen.height * .02f,
                "Hover leaves artwork faces upright and captions legible across repeated layout frames.");
            int captionUpdates = firstChoice.m_ButtonText.UpdateCalls;
            foreach (UIBButton button in popup.Buttons) button.Hover();
            for (int frame = 0; frame < 60; frame++) artworkLate.Invoke(popup, null);
            Check(firstChoice.m_ButtonText.UpdateCalls == captionUpdates,
                "Hovering artwork, navigation and Cancel does not regenerate or rescale captions every frame.");
            var cancelArtwork = popup.Buttons.Single(button => button.gameObject.name == "BCM_TransferCancel");
            ComboSceneGeometry.ScreenBounds(cancelArtwork.m_RootObject, CameraUtils.Camera, out Rect cancelBefore);
            artworkLabels[31] = "changed after opening";
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ArtworkNext").Click();
            Check(popup.Buttons.Single(button => button.gameObject.name == "BCM_ArtworkChoice1").m_ButtonText.Text == "Artwork 31",
                "Artwork pagination retains the captured labels while the popup is open.");
            Check(ComboSceneGeometry.ScreenBounds(cancelArtwork.m_RootObject, CameraUtils.Camera, out Rect cancelAfter)
                && Vector3.Distance(new Vector3(cancelBefore.center.x, cancelBefore.center.y, 0),
                    new Vector3(cancelAfter.center.x, cancelAfter.center.y, 0)) < .1f,
                "A shorter last page keeps the Cancel button in the same place.");
            popup.Buttons.Single(button => button.gameObject.name == "BCM_ArtworkChoice1").Click();
            Check(selectedArtwork == 31 && artworkCanceled == 0 && !popup.IsShown(),
                "A paginated choice invokes the captured artwork index exactly once after closing the modal.");
            Action closeNewArtwork = CatalogTransferMenu.OpenChoices("Choose background artwork", artworkLabels, selectArtwork, () => artworkCanceled++);
            closeArtwork();
            Check(ButtonListMenu.Visible != null && artworkCanceled == 0,
                "An old cancellation handle cannot dismiss a later popup, even with the same selection callback.");
            closeNewArtwork(); closeNewArtwork();
            Check(artworkCanceled == 1 && ButtonListMenu.Visible == null, "Canceling a pending artwork popup reports cancellation exactly once.");
            CatalogTransferMenu.OpenChoices("Choose background artwork", artworkLabels, selectArtwork, () => artworkCanceled++);
            CatalogTransferMenu.DestroyMenu();
            Check(artworkCanceled == 2 && ButtonListMenu.Visible == null, "Teardown also cancels a pending artwork choice.");
            CheckArtworkPickerResize();
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
