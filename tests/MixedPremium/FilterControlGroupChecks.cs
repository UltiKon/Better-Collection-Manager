using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void ClickFilterTool(KeywordSetTray.GridFixture fixture, string name)
    {
        if (!fixture.PinnedItems.Any(item => item.gameObject.name == name))
        {
            string group = name.StartsWith("BCM_PinnedFilter") ? "BCM_ShowLogicControls" : "BCM_ShowEditControls";
            if (group == name) throw new Exception("Missing filter group: " + name);
            ClickFilterTool(fixture, group);
        }
        fixture.ClearGesture();
        var pin = fixture.PinnedItems.Single(item => item.gameObject.name == name);
        Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(pin.GetComponent<BoxCollider>().bounds.center);
        Input.LeftPressed = Input.LeftHeld = true;
        int sounds = SoundManager.Get().Played.Count;
        Time.frameCount++; KeywordSetTray.PollOutsideClick();
        pin.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
        Check(SoundManager.Get().Played.Count == sounds + 1, "A grouped tool sounds exactly once on press, before its action");
        Input.LeftPressed = Input.LeftHeld = false; Input.LeftReleased = true;
        Time.frameCount++; pin.GetComponent<PegUIElement>().Release(); KeywordSetTray.PollOutsideClick();
        Input.LeftReleased = false; Time.frameCount++; KeywordSetTray.PollOutsideClick();
    }

    private static void CheckFilterControlGroups(KeywordSetTray.GridFixture fixture)
    {
        int rows = CollectionQualitySettings.FilterMenuRows, columns = CollectionQualitySettings.FilterMenuColumns;
        FilterTrayKind kind = KeywordSetTray.ActiveKind;
        FilterModeBehavior behavior = CollectionQualitySettings.FilterModeBehavior;
        bool logic = CollectionQualitySettings.ShowLogicControls, edit = CollectionQualitySettings.ShowEditControls;
        bool showClear = CollectionQualitySettings.ShowFilterClearAll;
        bool gradients = fixture.GradientArtwork;
        var categoryMode = FilterModes.Get(FilterTrayKind.Categories);
        try
        {
            fixture.GradientArtwork = true;
            CollectionQualitySettings.ShowLogicControls = CollectionQualitySettings.ShowEditControls = true;
            CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Single);
            CollectionQualitySettings.FilterMenuRows = 6; CollectionQualitySettings.FilterMenuColumns = 2;
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Categories); MelonLoader.MelonCoroutines.Complete();
            fixture.CloseControls(); fixture.ClearGesture();
            bool Pin(string name) => fixture.PinnedItems.Any(item => item.gameObject.name == name);
            bool ActionRow(SetFilterItem item) => item.gameObject.name == "BCM_ImportCategoryDeck" || item.gameObject.name == "BCM_AddCombo";
            Check(fixture.PinnedItems.Length == 3 && Pin("BCM_PinnedFilterBack") && Pin("BCM_ShowLogicControls") && Pin("BCM_ShowEditControls")
                && !fixture.Items.Any(ActionRow), "Collapsed Categories shows only Back and two group switches; editing actions do not leak into the scrolling list");
            float closedViewport = fixture.Scroller.m_ScrollBounds.bounds.size.z;
            float closedExtent = fixture.Frame.GetComponent<Renderer>().bounds.size.z;
            var mode = FilterModes.Get(FilterTrayKind.Categories);
            int refreshes = KeywordFilterApplier.Refreshes;
            ClickFilterTool(fixture, "BCM_ShowLogicControls");
            Check(Pin("BCM_PinnedFilterMode") && Pin("BCM_PinnedFilterNot") && Pin("BCM_PinnedFilterClear") && !Pin("BCM_ImportCategoryDeck")
                && fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_ShowLogicControls").Selected
                && fixture.Scroller.m_ScrollBounds.bounds.size.z < closedViewport,
                "Opening Logic expands a single pinned area and reserves its full height above the list");
            var retired = fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_PinnedFilterMode");
            ClickFilterTool(fixture, "BCM_ShowEditControls");
            Check(!Pin("BCM_PinnedFilterMode") && Pin("BCM_AddFilterFolder") && Pin("BCM_ImportCategoryDeck") && Pin("BCM_RemoveCategoryDeck") && Pin("BCM_AddCombo")
                && !Pin("BCM_AddPackage") && !Pin("BCM_EditPackage") && !retired.gameObject.activeSelf && !fixture.Items.Any(ActionRow),
                "Opening Edit closes Logic, retires its input targets and groups the three category editing actions with the same header");
            Check(FilterModes.Get(FilterTrayKind.Categories) == mode && KeywordFilterApplier.Refreshes == refreshes,
                "Switching control groups leaves filter logic, selected cards and collection results unchanged");
            foreach (int count in new[] { 1, 2, 3, 4 })
            {
                CollectionQualitySettings.FilterMenuColumns = count; KeywordSetTray.RebuildRows();
                var controls = fixture.PinnedItems;
                CheckRenderedFilterGradients(controls, count, true);
                CheckRenderedFilterGradients(fixture.Items, count, false);
                var editToggle = controls.Single(item => item.gameObject.name == "BCM_ShowEditControls");
                var editActions = controls.Where(item => item.gameObject.name == "BCM_AddFilterFolder" || item.gameObject.name == "BCM_ImportCategoryDeck"
                    || item.gameObject.name == "BCM_RemoveCategoryDeck" || item.gameObject.name == "BCM_AddCombo");
                Check(editActions.All(item => item.transform.position.z < editToggle.transform.position.z),
                    "Category editing actions appear below their Show Edit Controls switch in every column layout");
                var poses = controls.ToDictionary(item => item, item => item.transform.position);
                fixture.ShiftContents(.2f); fixture.RefreshVisible();
                Check(poses.All(pair => pair.Key.transform.position.Equals(pair.Value)), "Every grouped editing action stays fixed while its category list scrolls");
                fixture.ShiftContents(-.2f); fixture.RefreshVisible();
                Check(controls.All(item => item.GetComponent<BoxCollider>().enabled), "All visible group actions retain their own input collider in every column layout");
            }
            CollectionQualitySettings.FilterMenuColumns = 2; KeywordSetTray.RebuildRows();
            ClickFilterTool(fixture, "BCM_ShowEditControls");
            Check(fixture.PinnedItems.Length == 3 && Math.Abs(fixture.Scroller.m_ScrollBounds.bounds.size.z - closedViewport) < .001f
                && Math.Abs(fixture.Frame.GetComponent<Renderer>().bounds.size.z - closedExtent) < .001f,
                "Clicking the active group closes it and restores the original viewport without accumulating empty header space");
            ClickFilterTool(fixture, "BCM_ShowLogicControls");
            CollectionQualitySettings.ShowLogicControls = false; KeywordSetTray.RebuildRows();
            Check(!Pin("BCM_ShowLogicControls") && !Pin("BCM_PinnedFilterMode") && Pin("BCM_ShowEditControls") && Pin("BCM_PinnedFilterBack")
                && FilterModes.Get(FilterTrayKind.Categories) == mode,
                "Disabling Logic hides its entire open group while preserving mode, Edit and Back");
            ClickFilterTool(fixture, "BCM_ShowEditControls");
            CollectionQualitySettings.ShowEditControls = false; KeywordSetTray.RebuildRows();
            Check(fixture.PinnedItems.Length == 1 && Pin("BCM_PinnedFilterBack") && !fixture.Items.Any(ActionRow),
                "Disabling both optional groups leaves only Back and the actual category choices");
            CollectionQualitySettings.ShowLogicControls = CollectionQualitySettings.ShowEditControls = true;
            KeywordSetTray.ResetMenuSession(); KeywordSetTray.RebuildRows();
            Check(fixture.PinnedItems.Length == 3, "A new collection session starts with both groups collapsed");
            CollectionQualitySettings.ShowFilterClearAll = false;
            FilterModes.Set(FilterTrayKind.Categories, FilterMode.Or); KeywordSetTray.RefreshAfterSelection();
            Check(Pin("BCM_PinnedFilterClear") && !Pin("BCM_PinnedFilterMode"),
                "Leaving Single inserts a visible Clear All into the collapsed toolbar even when optional Clear All is disabled");
            CollectionQualitySettings.ShowLogicControls = CollectionQualitySettings.ShowEditControls = false;
            KeywordSetTray.RebuildRows();
            Check(fixture.PinnedItems.Length == 2 && Pin("BCM_PinnedFilterBack") && Pin("BCM_PinnedFilterClear"),
                "Hiding both groups cannot hide Clear All while the submenu is not Single");
            ClickFilterTool(fixture, "BCM_PinnedFilterClear");
            Check(FilterModes.Get(FilterTrayKind.Categories) == FilterMode.Single
                && fixture.PinnedItems.Length == 1 && Pin("BCM_PinnedFilterBack"),
                "Clear All resets the mode to Single and removes its forced toolbar slot immediately");
            FilterModes.Set(FilterTrayKind.Packages, FilterMode.Or);
            CollectionFilterMenu.ShowMenu(); MelonLoader.MelonCoroutines.Complete();
            Check(Pin("BCM_PinnedClearAllFilters"),
                "A non-Single mode with no selections keeps root Clear All visible despite hidden tool groups");
            ClickFilterTool(fixture, "BCM_PinnedClearAllFilters");
            Check(FilterModes.Kinds.All(value => FilterModes.Get(value) == FilterMode.Single)
                && !Pin("BCM_PinnedClearAllFilters"),
                "Root Clear All restores every mode to Single and removes the forced global reset");
            CollectionQualitySettings.ShowLogicControls = CollectionQualitySettings.ShowEditControls = true;
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Packages); MelonLoader.MelonCoroutines.Complete();
            fixture.CloseControls();
            Check(fixture.Items.Select(item => item.Text).SequenceEqual(new[] { "Packages", "All Packages", "Parasitic", "Factions", "Custom" })
                && PackageViewState.IsActive && Pin("BCM_PackageFormat") && Pin("BCM_PackageExpansions") && !Pin("BCM_ShowLogicControls"),
                "Packages opens its independent overview with All Packages, a direct format toggle and expansion navigation, without group logic");
            Check(Pin("BCM_ShowEditControls") && !Pin("BCM_AddPackage") && !Pin("BCM_AddFilterFolder") && !Pin("BCM_EditPackage"),
                "Packages initially collapses its creation actions under Show Edit Controls");
            var initialFormat = PackageViewState.Format;
            ClickFilterTool(fixture, "BCM_PackageFormat");
            Check(PackageViewState.Format != initialFormat
                && fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_PackageFormat").Text
                    == "Format: " + (PackageViewState.Format == PegasusShared.FormatType.FT_STANDARD ? "Standard" : "Wild"),
                "The direct Standard/Wild button changes format and immediately updates its caption");
            ClickFilterTool(fixture, "BCM_ShowEditControls");
            Check(Pin("BCM_AddPackage") && Pin("BCM_AddFilterFolder") && !Pin("BCM_EditPackage")
                && !Pin("BCM_AddCombo") && !Pin("BCM_ImportCategoryDeck")
                && fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_AddPackage").Text == "Turn Deck Into Package",
                "Package Edit Controls contains Add Package and Add Folder without a separate editing list");
            var packageControls = fixture.PinnedItems.OrderByDescending(item => item.transform.position.z).ThenBy(item => item.transform.position.x).ToArray();
            int editIndex = Array.FindIndex(packageControls, item => item.gameObject.name == "BCM_ShowEditControls");
            Check(editIndex > Array.FindIndex(packageControls, item => item.gameObject.name == "BCM_PackageFormat")
                && editIndex > Array.FindIndex(packageControls, item => item.gameObject.name == "BCM_PackageExpansions")
                && packageControls.Where(item => item.gameObject.name == "BCM_AddPackage" || item.gameObject.name == "BCM_AddFilterFolder")
                    .All(item => item.transform.position.z < packageControls[editIndex].transform.position.z),
                "Package editing controls follow format/expansion navigation and place their action buttons directly below the switch");
            foreach (int count in new[] { 2, 3, 4 })
            {
                CollectionQualitySettings.FilterMenuColumns = count; KeywordSetTray.RebuildRows();
                CheckRenderedFilterGradients(fixture.PinnedItems, count, true);
                CheckRenderedFilterGradients(fixture.Items, count, false);
            }
            CollectionQualitySettings.ShowEditControls = false; KeywordSetTray.RebuildRows();
            Check(!Pin("BCM_AddPackage") && !Pin("BCM_AddFilterFolder") && !Pin("BCM_ShowEditControls"),
                "Disabling Edit Controls hides the complete package creation group");
            CollectionQualitySettings.ShowEditControls = true; KeywordSetTray.RebuildRows();
            ClickFilterTool(fixture, "BCM_PackageExpansions");
            fixture.CloseControls();
            Check(KeywordSetTray.ActiveKind == FilterTrayKind.Expansions && Pin("BCM_ShowLogicControls") && PackageViewState.IsActive && !Pin("BCM_PackageFormat"),
                "The package expansion submenu offers filter logic and native format choices without a redundant format toggle");
            var standardChoice = new GameObject("Package Standard choice").AddComponent<SetFilterItem>();
            standardChoice.Text = "Standard"; standardChoice.Height = .5f;
            standardChoice.IsAllStandard = true; standardChoice.FormatType = PegasusShared.FormatType.FT_STANDARD;
            fixture.Native.m_items.Add(standardChoice);
            try
            {
                KeywordSetTray.CaptureExpansionRows(fixture.Native);
                var wildChoice = fixture.Native.m_items.First(item => !item.IsHeader && item.FormatType == PegasusShared.FormatType.FT_WILD && item.CardSets == null);
                CollectionFilterMenu.SelectExpansion(wildChoice);
                Check(PackageViewState.Format == PegasusShared.FormatType.FT_WILD && !ExpansionFilterState.IsActive
                    && fixture.Items.Single(item => item.Text == wildChoice.Text).Selected,
                    "The existing Wild expansion choice switches package format and shows its selection without invoking native card filters");
                CollectionFilterMenu.SelectExpansion(standardChoice);
                Check(PackageViewState.Format == PegasusShared.FormatType.FT_STANDARD && !ExpansionFilterState.IsActive
                    && fixture.Items.Single(item => item.Text == standardChoice.Text).Selected
                    && !fixture.Items.Single(item => item.Text == wildChoice.Text).Selected,
                    "The existing Standard expansion choice restores Standard package format and updates both native format rows");
            }
            finally
            {
                fixture.Native.m_items.Remove(standardChoice); UnityEngine.Object.Destroy(standardChoice.gameObject);
                KeywordSetTray.CaptureExpansionRows(fixture.Native); KeywordSetTray.RebuildRows();
            }
            ClickFilterTool(fixture, "BCM_ShowLogicControls");
            Check(Pin("BCM_PinnedFilterMode") && Pin("BCM_PinnedFilterNot"), "Package expansion logic retains mode and NOT controls");
            CollectionFilterMenu.GoBack(); MelonLoader.MelonCoroutines.Complete();
            Check(PackageViewState.IsActive && KeywordSetTray.ActiveKind == FilterTrayKind.Packages,
                "Back from package expansions returns to the package overview");
            var previousRefresh = KeywordFilterApplier.OnRefresh;
            bool navigationAction = false;
            try
            {
                KeywordFilterApplier.OnRefresh = () => { navigationAction = FilterButtonFeedback.IsFilterAction; previousRefresh?.Invoke(); };
                ClickFilterTool(fixture, "BCM_PinnedFilterBack"); MelonLoader.MelonCoroutines.Complete();
                Check(!PackageViewState.IsActive && navigationAction, "The pinned package Back button restores Cards inside a user action so the page transition can animate");
            }
            finally { KeywordFilterApplier.OnRefresh = previousRefresh; }
            CheckCatalogClipboardChoices(fixture);
        }
        finally
        {
            fixture.GradientArtwork = gradients;
            CollectionQualitySettings.ShowLogicControls = logic; CollectionQualitySettings.ShowEditControls = edit;
            CollectionQualitySettings.ShowFilterClearAll = showClear;
            FilterModes.Set(FilterTrayKind.Categories, categoryMode);
            CollectionQualitySettings.FilterModeBehavior = behavior;
            CollectionQualitySettings.FilterMenuRows = rows; CollectionQualitySettings.FilterMenuColumns = columns;
            CollectionFilterMenu.ShowFilter(kind); MelonLoader.MelonCoroutines.Complete(); fixture.ClearGesture();
        }
    }

    private static void CheckRenderedFilterGradients(System.Collections.Generic.IEnumerable<SetFilterItem> buttons, int columns, bool pinned)
    {
        var rows = buttons.GroupBy(item => Math.Round(item.transform.position.z, 4)).OrderByDescending(group => group.Key).ToArray();
        for (int index = 0; index < rows.Length; index++)
        {
            var row = rows[index].OrderBy(item => item.transform.position.x).ToArray();
            int slots = row[0].IsHeader ? 1 : pinned && index == 0 ? row.Length : columns;
            for (int column = 0; column < row.Length; column++)
            {
                var button = row[column];
                var layers = new[] { FilterButtonFace(button), button.m_mouseOverGlow.GetComponentInChildren<MeshFilter>(true),
                    button.m_pressedShadow.GetComponentInChildren<MeshFilter>(true), button.m_selectedGlow.GetComponentInChildren<MeshFilter>(true) };
                foreach (var layer in layers)
                    Check(Near(layer.sharedMesh.colors.Min(color => color.r), 1f - .6f * (column + 1) / slots)
                        && Near(layer.sharedMesh.colors.Max(color => color.r), 1f - .6f * column / slots),
                        "Rendered " + button.gameObject.name + " shares its row's gradient across idle, hover, press and selection artwork at " + columns + " columns");
            }
        }
    }
}
