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
        Check(menu.GetComponentsInChildren<DropdownControl>(true).Length == 11
            && menu.GetComponentsInChildren<CheckBox>(true).Length == 11,
            "Collection, priorities and filters must be available together on one page");
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
