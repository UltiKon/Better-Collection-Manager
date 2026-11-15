using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckSettingsSections(ModsSettingsMenu menu)
    {
        var controls = menu.GetComponentsInChildren<DropdownControl>(true);
        TAG_PREMIUM[] saved = PreferredPremiumPrefs.Order.ToArray();
        bool collapse = PreferredPremiumPrefs.CollapseEnabled;
        Check(!controls.Any(c => c.gameObject.name == "BCM_ModsSettingssection"),
            "The common settings panel must not require a section picker");
        Check(menu.GetComponentsInChildren<DropdownControl>(true).Length == 15
            && menu.GetComponentsInChildren<CheckBox>(true).Length == 19,
            "Collection, priorities and filters must be available together on one page");
        Check(!controls.Any(control => control.gameObject.name == "BCM_ModsExportartwork"),
            "Artwork selection belongs to the export popup, not the settings panel.");
        var imageQuality = controls.Single(control => control.gameObject.name == "BCM_ModsExportquality");
        DeckImageQuality savedQuality = DeckImageSettings.Quality;
        Check(imageQuality.Items.Cast<DeckImageQuality>().SequenceEqual(new[] { DeckImageQuality.Deck, DeckImageQuality.Normal, DeckImageQuality.Golden, DeckImageQuality.Highest }),
            "Deck image quality offers As in deck, Normal, Golden and Highest.");
        imageQuality.SelectOption(DeckImageQuality.Normal);
        Check(DeckImageSettings.Quality == DeckImageQuality.Normal, "The image quality dropdown saves its preference.");
        imageQuality.SelectOption(savedQuality);
        var resolutionControl = controls.Single(control => control.gameObject.name == "BCM_ModsExportresolution");
        Check(resolutionControl.Items.Cast<DeckImageResolution>().Select(DeckImageSettings.ResolutionLabel)
            .SequenceEqual(new[] { "Low", "Medium", "High", "Max" }),
            "Image resolution exposes four plain preset labels without suffixes.");
        DeckImageResolution savedResolution = DeckImageSettings.Resolution;
        resolutionControl.SelectOption(DeckImageResolution.High);
        Check(DeckImageSettings.Resolution == DeckImageResolution.High && DeckImageSettings.Quality == savedQuality,
            "Image resolution saves separately from card premium quality.");
        resolutionControl.SelectOption(savedResolution);
        var imageFormat = controls.Single(control => control.gameObject.name == "BCM_ModsExportformat");
        DeckImageAspect savedFormat = DeckImageSettings.Aspect;
        Check(imageFormat.Items.Cast<DeckImageAspect>().Select(DeckImageSettings.AspectLabel).SequenceEqual(new[] { "Portrait", "Square", "Landscape" }),
            "Image format offers Portrait, Square and Landscape instead of numeric column settings.");
        imageFormat.SelectOption(DeckImageAspect.Square);
        Check(DeckImageSettings.Aspect == DeckImageAspect.Square, "Image format saves its preference.");
        imageFormat.SelectOption(savedFormat);
        // Model the observed client failure after native graphics quality changes:
        // valid native choices and a working closed field, but blank popup labels.
        var popupRows = imageFormat.Items.Select(value => {
            var row = new GameObject("GraphicsRebuiltChoice").AddComponent<DropdownMenuItem>();
            row.transform.SetParent(imageFormat.m_menuItemContainer.transform, false);
            row.m_text = new GameObject("LostNativeCaption").AddComponent<UberText>();
            row.m_text.transform.SetParent(row.transform, false);
            row.SetValue(value, string.Empty); row.m_text.TrueTypeFont = null;
            row.m_text.Hide(); row.m_text.enabled = false;
            return row;
        }).ToArray();
        imageFormat.onUserPressedButton();
        Check(popupRows.All(row => row.m_text.Text == DeckImageSettings.AspectLabel((DeckImageAspect)row.GetValue())
            && row.m_text.enabled && !row.m_text.isHidden() && row.m_text.TrueTypeFont != null
            && row.m_text.transform.IsChildOf(row.transform)),
            "Opening Mods repairs blank popup captions from native graphics changes using their actual values and a fresh text owner.");
        // Replay the actual log: popup artwork moves from order 51 to 102
        // after changing native quality, while direct glyphs remain at 52.
        typeof(ModsSettingsMenu).GetMethod("RestoreSettingsScrollRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
        var popupArt = new GameObject("NativeQualityDropdownBackground");
        popupArt.transform.SetParent(imageFormat.m_menu.transform, false);
        popupArt.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(4, .1f, 1)) };
        var popupRenderer = popupArt.AddComponent<MeshRenderer>();
        var sharedPopupMaterial = new Material { renderQueue = 3000 };
        popupRenderer.sharedMaterial = sharedPopupMaterial; popupRenderer.sortingLayerID = 7;
        int savedTemplateQueue = imageFormat.m_menuItemTemplate.m_text.RenderQueue;
        imageFormat.m_menuItemTemplate.m_text.RenderQueue = 52;
        foreach (var row in popupRows)
        { row.m_text.SimulateNativeQueue = true; row.m_text.RenderQueue = 52; row.m_text.UpdateNow(true); }
        popupRenderer.sortingOrder = 102;
        imageFormat.onUserCancelled(); imageFormat.onUserPressedButton();
        Check(popupRows.All(row => row.m_text.RenderQueue == 103
            && CounterTextLayout.GlyphRenderer(row.m_text).sortingOrder == 103
            && CounterTextLayout.GlyphRenderer(row.m_text).sortingLayerID == 7),
            "Native quality change: text must follow the background from order 52 to 103 when artwork moves from 51 to 102.");
        for (int repeat = 0; repeat < 3; repeat++) { imageFormat.onUserCancelled(); imageFormat.onUserPressedButton(); }
        Check(popupRows.All(row => row.m_text.RenderQueue == 103) && sharedPopupMaterial.renderQueue == 3000,
            "Repeated popup openings keep a stable text order and never change shared native background materials.");
        popupRenderer.sortingOrder = 153; // Native artwork can move after the opening callback.
        typeof(ModsSettingsMenu).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
        Check(popupRows.All(row => row.m_text.RenderQueue == 154 && CounterTextLayout.GlyphRenderer(row.m_text).sortingOrder == 154),
            "An already open popup follows a late native draw-order change without requiring either settings menu to close.");
        foreach (var row in popupRows) { row.UpdateTextColor(); row.m_text.UpdateNow(true); }
        Check(popupRows.All(row => CounterTextLayout.GlyphRenderer(row.m_text).sortingOrder == 154),
            "Native hover/color text rebuilds retain the corrected order.");
        var selectionMaterial = new Material { renderQueue = 3000 };
        foreach (var row in popupRows)
        {
            row.m_selected = new GameObject("NativeSelectedBackground");
            row.m_selected.transform.SetParent(row.transform, false);
            var selectionRenderer = row.m_selected.AddComponent<MeshRenderer>();
            selectionRenderer.sharedMaterial = selectionMaterial;
            selectionRenderer.sortingOrder = 52;
            row.m_selected.SetActive(Equals(row.GetValue(), imageFormat.getSelection()));
        }
        var chosenRow = popupRows.Single(row => row.m_selected.activeSelf);
        popupRenderer.sortingOrder = 102;
        imageFormat.onUserCancelled(); imageFormat.onUserPressedButton();
        Check(chosenRow.m_selected.GetComponent<Renderer>().sortingOrder == 103
            && popupRows.All(row => CounterTextLayout.GlyphRenderer(row.m_text).sortingOrder == 104),
            "After native quality changes the selected background must sit between popup artwork (102) and text (104).");
        for (int repeat = 0; repeat < 3; repeat++)
            typeof(ModsSettingsMenu).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
        Check(chosenRow.m_selected.GetComponent<Renderer>().sortingOrder == 103
            && popupRows.All(row => row.m_text.RenderQueue == 104)
            && popupRows.Count(row => row.m_selected.activeSelf) == 1 && selectionMaterial.renderQueue == 3000,
            "Selected artwork never feeds back into the base order; repeated updates preserve selection flags and shared materials.");
        popupRenderer.sortingOrder = 51;
        imageFormat.onUserCancelled(); imageFormat.onUserPressedButton();
        Check(chosenRow.m_selected.GetComponent<Renderer>().sortingOrder == 52
            && popupRows.All(row => row.m_text.RenderQueue == 53),
            "The complete background-selection-text stack resets together after fully reopening native Options.");
        foreach (var row in popupRows) { UnityEngine.Object.Destroy(row.m_selected); row.m_selected = null; }
        popupRenderer.sortingOrder = 51;
        imageFormat.onUserCancelled(); imageFormat.onUserPressedButton();
        Check(popupRows.All(row => row.m_text.RenderQueue == 52),
            "A full native menu reopen returns text to its reference order when the artwork returns to 51.");
        imageFormat.m_menuItemTemplate.m_text.RenderQueue = savedTemplateQueue;
        UnityEngine.Object.Destroy(popupArt);
        imageFormat.onUserCancelled();
        foreach (var row in popupRows) { row.m_text.Text = string.Empty; row.m_text.Hide(); }
        imageFormat.onUserPressedButton();
        Check(popupRows.All(row => !string.IsNullOrEmpty(row.m_text.Text) && !row.m_text.isHidden()),
            "Every popup opening restores labels after another native render-state invalidation.");
        imageFormat.onUserCancelled();
        foreach (var row in popupRows) UnityEngine.Object.Destroy(row.gameObject);

        var imageBackground = controls.Single(control => control.gameObject.name == "BCM_ModsExportbackground");
        DeckImageBackground savedBackground = DeckImageSettings.Background;
        Check(imageBackground.Items.Cast<DeckImageBackground>().SequenceEqual(Enum.GetValues(typeof(DeckImageBackground)).Cast<DeckImageBackground>()),
            "Background style offers native parchment, class artwork and blurred card artwork.");
        imageBackground.SelectOption(DeckImageBackground.Artwork);
        Check(DeckImageSettings.Background == DeckImageBackground.Artwork, "Background style saves independently.");
        imageBackground.SelectOption(savedBackground);
        var askArtwork = menu.GetComponentsInChildren<CheckBox>(true).Single(check => check.gameObject.name == "BCM_ModsDeckImageAskArtwork");
        bool savedAskArtwork = DeckImageSettings.AskArtwork;
        foreach (DeckImageBackground background in Enum.GetValues(typeof(DeckImageBackground)))
        {
            imageBackground.SelectOption(background);
            typeof(ModsSettingsMenu).GetMethod("RestoreSettingsScrollRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
            Check(askArtwork.transform.parent.gameObject.activeSelf == (background == DeckImageBackground.CardArtwork
                && DeckImageSettings.Enabled(DeckImageOption.Background)) && DeckImageSettings.AskArtwork == savedAskArtwork,
                "Only blurred card art exposes the artwork prompt, preserving its preference across background changes.");
        }
        imageBackground.SelectOption(DeckImageBackground.CardArtwork);
        askArtwork.Click();
        Check(DeckImageSettings.AskArtwork != savedAskArtwork, "The artwork prompt can be disabled in Deck image settings.");
        askArtwork.Click();
        imageBackground.SelectOption(savedBackground);
        foreach (DeckImageOption option in DeckImageSettings.Options)
        {
            var box = menu.GetComponentsInChildren<CheckBox>(true).Single(value => value.gameObject.name == "BCM_ModsDeckImage" + option);
            bool savedValue = DeckImageSettings.Enabled(option);
            Check(box.IsChecked() == (option == DeckImageOption.Background ? !savedValue : savedValue), "Remove background inverts only the UI check while preserving the saved background preference.");
            box.Click();
            Check(DeckImageSettings.Enabled(option) != savedValue, "Image checkboxes update their own saved preference.");
            box.Click();
        }
        var removeBackground = menu.GetComponentsInChildren<CheckBox>(true).Single(value => value.gameObject.name == "BCM_ModsDeckImageBackground");
        bool backgroundEnabled = DeckImageSettings.Enabled(DeckImageOption.Background);
        if (!backgroundEnabled) removeBackground.Click();
        DeckImageBackground keptBackground = DeckImageSettings.Background;
        bool keptAsk = DeckImageSettings.AskArtwork;
        removeBackground.Click();
        typeof(ModsSettingsMenu).GetMethod("RestoreSettingsScrollRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
        Check(removeBackground.IsChecked() && removeBackground.m_uberText.Text == "Remove background"
            && !DeckImageSettings.Capture().HasFlag(DeckImageOption.Background)
            && !imageBackground.transform.parent.gameObject.activeSelf && !askArtwork.transform.parent.gameObject.activeSelf,
            "Remove background hides redundant background style/artwork controls and removes decoration from exports.");
        removeBackground.Click();
        typeof(ModsSettingsMenu).GetMethod("RestoreSettingsScrollRows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
        Check(!removeBackground.IsChecked() && imageBackground.transform.parent.gameObject.activeSelf
            && DeckImageSettings.Background == keptBackground && DeckImageSettings.AskArtwork == keptAsk,
            "Restoring backgrounds brings back their saved style and artwork preference.");
        if (!backgroundEnabled) removeBackground.Click();
        var openSearch = controls.Single(control => control.gameObject.name == "BCM_ModsCollectionopensearch");
        Check(openSearch.Items.Cast<CollectionOpenSearchMode>().SequenceEqual(Enum.GetValues(typeof(CollectionOpenSearchMode)).Cast<CollectionOpenSearchMode>()),
            "Crafting exposes Off, New, Refund and combined opening searches");
        openSearch.SelectOption(CollectionOpenSearchMode.NewOrRefund);
        Check(CollectionQualitySettings.OpenSearch == CollectionOpenSearchMode.NewOrRefund, "Opening search selection updates its saved mode");
        openSearch.SelectOption(CollectionOpenSearchMode.Off);
        var protectionBox = menu.GetComponentsInChildren<CheckBox>(true).Single(box => box.gameObject.name == "BCM_ModsNeverDustSignaturesDiamonds");
        bool protection = CollectionQualitySettings.NeverDustSignaturesDiamonds;
        Check(protectionBox.IsChecked() == protection, "Premium protection checkbox reflects its saved value");
        protectionBox.Click();
        Check(protectionBox.m_uberText.Text == "Never dust\nSignatures/Diamonds", "Premium protection uses the full caption on two lines");
        var craftingBox = menu.GetComponentsInChildren<CheckBox>(true).Single(box => box.gameObject.name == "BCM_ModsCollapseInCrafting");
        bool crafting = CollectionQualitySettings.CollapseInCrafting;
        var nativeTray = CraftingTray.Instance;
        CraftingTray.Instance = new CraftingTray { Shown = true };
        int refreshes = KeywordFilterApplier.Refreshes;
        craftingBox.Click();
        Check(CollectionQualitySettings.CollapseInCrafting != crafting, "The Crafting collapse option updates its saved preference");
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 1, "Crafting display changes refresh while the crafting tray is open");
        craftingBox.Click();
        QualityDeckInput.OnUpdate();
        Check(KeywordFilterApplier.Refreshes == refreshes + 2, "Disabling crafting collapse also refreshes without closing the tray");
        CraftingTray.Instance = nativeTray;
        Check(CollectionQualitySettings.NeverDustSignaturesDiamonds != protection, "Premium protection checkbox updates the setting");
        protectionBox.Click();
        var popupBox = menu.GetComponentsInChildren<CheckBox>(true).Single(box => box.gameObject.name == "BCM_ModsDisenchantPopup");
        var redundantBox = menu.GetComponentsInChildren<CheckBox>(true).Single(box => box.gameObject.name == "BCM_ModsHideRedundant");
        bool popup = CollectionQualitySettings.DisenchantPopup, hideRedundant = CollectionQualitySettings.HideRedundant;
        Check(popupBox.IsChecked() == popup && popupBox.m_uberText.Text == "Disenchant popup\nExperimental: use with care",
            "Crafting exposes the saved disenchant-popup toggle with a visible English experimental warning");
        if (!popup) popupBox.Click();
        if (!CollectionQualitySettings.HideRedundant) redundantBox.Click();
        popupBox.Click();
        Check(!CollectionQualitySettings.DisenchantPopup && !protectionBox.transform.parent.gameObject.activeSelf
            && !protectionBox.IsEnabled() && CollectionQualitySettings.NeverDustSignaturesDiamonds == protection,
            "Disabling the popup hides redundant premium protection and preserves its saved value");
        redundantBox.Click();
        Check(protectionBox.transform.parent.gameObject.activeSelf && !protectionBox.IsEnabled(),
            "Showing redundant settings reveals premium protection as disabled while the popup is off");
        protectionBox.Click();
        Check(CollectionQualitySettings.NeverDustSignaturesDiamonds == protection && !protectionBox.IsEnabled(),
            "Clicking disabled redundant premium protection cannot change its saved value");
        popupBox.Click();
        Check(CollectionQualitySettings.DisenchantPopup && protectionBox.transform.parent.gameObject.activeSelf
            && protectionBox.IsEnabled() && protectionBox.IsChecked() == protection,
            "Re-enabling the popup restores premium protection with its saved value");
        if (CollectionQualitySettings.HideRedundant != hideRedundant) redundantBox.Click();
        if (CollectionQualitySettings.DisenchantPopup != popup) popupBox.Click();
        var logicBox = menu.GetComponentsInChildren<CheckBox>(true).Single(box => box.gameObject.name == "BCM_ModsShowLogicControls");
        var editBox = menu.GetComponentsInChildren<CheckBox>(true).Single(box => box.gameObject.name == "BCM_ModsShowEditControls");
        bool logic = CollectionQualitySettings.ShowLogicControls, edit = CollectionQualitySettings.ShowEditControls;
        logicBox.Click();
        Check(CollectionQualitySettings.ShowLogicControls != logic && CollectionQualitySettings.ShowEditControls == edit,
            "The Logic checkbox independently controls only the logic toolbar group");
        editBox.Click();
        Check(CollectionQualitySettings.ShowLogicControls != logic && CollectionQualitySettings.ShowEditControls != edit,
            "The Edit checkbox can independently hide the editing toolbar group");
        logicBox.Click(); editBox.Click();
        CheckVisibleRowsLayout(menu);
        var priorities = controls.Where(c => c.gameObject.name.StartsWith("BCM_ModsPriority", StringComparison.Ordinal)).ToArray();
        priorities[0].SelectOption(TAG_PREMIUM.NORMAL);
        Check(PreferredPremiumPrefs.Order[0] == TAG_PREMIUM.NORMAL && PreferredPremiumPrefs.Order.Distinct().Count() == 4,
            "Choosing a priority must move that quality to its rank without duplicating or dropping another quality");
        Check(priorities.Select(c => (TAG_PREMIUM)c.getSelection()).SequenceEqual(PreferredPremiumPrefs.Order),
            "All priority fields must reflect the updated saved order immediately");
        var collapseBox = menu.GetComponentsInChildren<CheckBox>(false).Single(c => c.m_uberText.Text == "Collapse quality variants");
        collapseBox.Click();
        Check(PreferredPremiumPrefs.CollapseEnabled == !collapse,
            "The moved collapse switch must keep controlling the existing preference");
        collapseBox.Click();
        PreferredPremiumPrefs.Order = saved;
        controls.Single(c => c.gameObject.name == "BCM_ModsFilterrows").SelectOption(7);
        controls.Single(c => c.gameObject.name == "BCM_ModsFiltercolumns").SelectOption(3);
        Check(CollectionQualitySettings.FilterMenuRows == 7 && CollectionQualitySettings.FilterMenuColumns == 3,
            "Filter grid fields must save their row and column counts independently");
        controls.Single(c => c.gameObject.name == "BCM_ModsFilterrows").SelectOption(0);
        controls.Single(c => c.gameObject.name == "BCM_ModsFiltercolumns").SelectOption(0);
        Check(CollectionQualitySettings.FilterMenuRows == 0 && CollectionQualitySettings.FilterMenuColumns == 0,
            "Both native dropdowns must offer Automatic as a selectable value");
        CollectionQualitySettings.FilterMenuColumns = 1;
        Check(PreferredPremiumPrefs.CollapseEnabled == collapse && PreferredPremiumPrefs.Order.SequenceEqual(saved),
            "Editing filter settings on the same page must not discard quality preferences");
        CheckPriorityMoves();
        CheckFilterGrid();
        CheckNativeFilterGrid();
        CheckFilterMenuFeatures();
    }

    private static void CheckPriorityMoves()
    {
        var original = new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        for (int from = 0; from < 4; from++)
        for (int rank = 0; rank < 4; rank++)
        {
            var order = original.ToArray();
            bool changed = PreferredPremiumHelper.MoveToPriority(order, rank, original[from]);
            Check(changed == (from != rank) && order[rank] == original[from] && order.Distinct().Count() == 4,
                "Moving between any priority ranks must preserve every quality exactly once");
            Check(order.Where(p => p != original[from]).SequenceEqual(original.Where(p => p != original[from])),
                "Changing one priority must retain the relative order of the other qualities");
        }
        var unchanged = original.ToArray();
        Check(!PreferredPremiumHelper.MoveToPriority(unchanged, -1, TAG_PREMIUM.NORMAL)
            && !PreferredPremiumHelper.MoveToPriority(unchanged, 4, TAG_PREMIUM.NORMAL)
            && !PreferredPremiumHelper.MoveToPriority(unchanged, 0, (TAG_PREMIUM)999)
            && unchanged.SequenceEqual(original), "Invalid priorities must not corrupt the saved order");
    }

    private static void CheckFilterGrid()
    {
        float[] heights = { 0.4f, 0.5f, 0.7f, 0.5f, 0.3f, 0.5f, 0.5f, 0.5f, 0.5f };
        bool[] headers = { true, false, false, false, true, false, false, false, false };
        for (int columns = 1; columns <= CollectionQualitySettings.MaxFilterColumns; columns++)
        {
            var cells = FilterGridLayout.Arrange(heights, headers, columns);
            Check(cells.Length == heights.Length && cells[0].Column == -1 && cells[4].Column == -1,
                "Every filter item must survive grid layout and headers must span their own row");
            Check(cells[5].Column == 0 && cells[5].Down >= cells[4].Down + cells[4].Height,
                "Every section must begin at the first column below its own heading");
            for (int i = 0; i < cells.Length; i++)
            {
                Check(cells[i].Column >= -1 && cells[i].Column < columns,
                    "Filter cells must stay within the requested column count");
                for (int j = i + 1; j < cells.Length; j++)
                {
                    bool sameLane = cells[i].Column < 0 || cells[j].Column < 0 || cells[i].Column == cells[j].Column;
                    Check(!sameLane || cells[j].Down >= cells[i].Down + cells[i].Height - 0.0001f,
                        "Filter rows and full-width headers must not overlap, including partial rows and mixed heights");
                }
            }
        }
        var overflow = FilterGridLayout.Arrange(Enumerable.Repeat(0.5f, 50).ToArray(), new bool[50], 3);
        Check(overflow.Length == 50 && overflow.Last().Down > 2 * 0.5f,
            "Grid overflow must remain available below the viewport for native scrolling");
    }
}
