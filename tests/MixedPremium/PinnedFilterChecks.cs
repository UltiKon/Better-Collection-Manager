using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static SetFilterTray MakeSettingsScrollbarTemplate()
    {
        var root = new GameObject("NativeFilterScrollbarTemplate");
        var tray = root.AddComponent<SetFilterTray>();
        tray.m_trayObject = root;
        var scroller = tray.m_scroller = root.AddComponent<UIBScrollable>();
        var hitBone = Child(root.transform, "ScrollTrackHitBone");
        hitBone.transform.localPosition = new Vector3(3f, 0f, 0f);
        scroller.m_ScrollTrack = hitBone.AddComponent<BoxCollider>();
        scroller.m_ScrollTrack.size = new Vector3(0.12f, 0.01f, 3.8f);
        var grip = Child(root.transform, "ScrollGripBone");
        grip.transform.localPosition = new Vector3(3f, 0.02f, 1.5f);
        scroller.m_ScrollThumb = grip.AddComponent<ScrollBarThumb>();
        var material = new Material { name = "NativeFilterScrollbarAtlas", renderQueue = 3100 };
        void Art(Transform parent, string name, Vector3 position, float width, float height)
        {
            GameObject go = Child(parent, name);
            go.transform.localPosition = position;
            var mesh = new Mesh { name = name, subMeshCount = 1,
                vertices = new[] { new Vector3(-width/2, 0, -height/2), new Vector3(width/2, 0, -height/2),
                    new Vector3(width/2, 0, height/2), new Vector3(-width/2, 0, height/2) },
                bounds = new Bounds(Vector3.zero, new Vector3(width, 0.001f, height)) };
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false);
            mesh.SetUVs(0, new List<Vector4> { new Vector4(.2f, .3f, 0, 0), new Vector4(.3f, .3f, 0, 0),
                new Vector4(.3f, .7f, 0, 0), new Vector4(.2f, .7f, 0, 0) });
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingLayerID = 8; renderer.sortingOrder = 23;
            renderer.SetPropertyBlock(new MaterialPropertyBlock { Color = new Color(.7f, .8f, 1f, 1f) });
        }
        Art(root.transform, "NativeFilterRail", new Vector3(3f, 0, 0), .12f, 3.6f);
        Art(root.transform, "NativeFilterTop", new Vector3(3f, 0, 1.85f), .16f, .1f);
        Art(root.transform, "NativeFilterBottom", new Vector3(3f, 0, -1.85f), .16f, .1f);
        Art(grip.transform, "NativeFilterGrip", Vector3.zero, .18f, .3f);
        // A hidden native list can cull its resting grip renderer. Its mesh is
        // still the correct template for a newly visible mod scrollbar.
        grip.GetComponentInChildren<MeshRenderer>(true).enabled = false;
        Art(grip.transform, "InactiveGripHover", Vector3.zero, .2f, .32f);
        grip.transform.GetChild(1).gameObject.SetActive(false);
        // The source prefab can be inactive, and its rail visuals can be siblings
        // of the hit bone. Neither prevents copying the native artwork.
        root.SetActive(false);
        Resources.Objects = new UnityEngine.Object[] { tray, scroller };
        return tray;
    }

    private static void CheckPinnedFilterChrome(KeywordSetTray.GridFixture fixture)
    {
        fixture.ClearPinnedFixtures();
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
        CollectionQualitySettings.ShowFilterClearAll = true;
        fixture.UsePinnedControls = true;
        fixture.ClearGesture();
        fixture.OpenLogicControls();
        SetFilterItem Pin(string name) => fixture.PinnedItems.Single(item => item.gameObject.name == name);
        void ClickPin(string name)
        {
            if (!fixture.PinnedItems.Any(item => item.gameObject.name == name) && name.StartsWith("BCM_PinnedFilter")) fixture.OpenLogicControls();
            fixture.ClearGesture();
            SetFilterItem pin = Pin(name);
            Input.mousePosition = CameraUtils.Camera.WorldToScreenPoint(pin.GetComponent<BoxCollider>().bounds.center);
            Input.LeftPressed = Input.LeftHeld = true;
            int pressSounds = SoundManager.Get().Played.Count;
            Time.frameCount++; KeywordSetTray.PollOutsideClick();
            Check(SoundManager.Get().Played.Count == pressSounds + 1, "Pinned controls play their click on the press frame even when PegUI does not deliver PRESS");
            pin.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == pressSounds + 1, "PegUI and physics fallback share one press sound");
            Input.LeftPressed = Input.LeftHeld = false; Input.LeftReleased = true;
            Time.frameCount++; pin.GetComponent<PegUIElement>().Release(); KeywordSetTray.PollOutsideClick();
            Input.LeftReleased = false;
            Time.frameCount++; KeywordSetTray.PollOutsideClick();
        }
        Check(fixture.PinnedItems.Length == 5 && Pin("BCM_PinnedFilterMode").Text == "Next: AND"
            && fixture.Items.All(item => !fixture.PinnedItems.Contains(item)),
            "Back, group toggle, next operator, NOT and Clear All must live outside the scrolling list: "
                + string.Join(",", fixture.PinnedItems.Select(item => item.gameObject.name + "=" + item.Text))
                + "; list=" + string.Join(",", fixture.Items.Select(item => item.gameObject.name)));
        void ChooseOperator(FilterMode mode)
        {
            for (int attempts = 0; FilterModes.Operation(FilterTrayKind.Keywords) != mode && attempts < 3; attempts++)
                ClickPin("BCM_PinnedFilterMode");
        }
        var positions = fixture.PinnedItems.ToDictionary(item => item, item => item.transform.position);
        fixture.ShiftContents(1f); fixture.RefreshVisible();
        Check(positions.All(pair => pair.Key.transform.position.Equals(pair.Value)), "Scrolling contents must leave every header button fixed");
        fixture.ShiftContents(-1f); fixture.RefreshVisible();
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        fixture.Items.Single(item => item.Text == "Adapt").GetComponent<PegUIElement>().Release();
        int refreshes = KeywordFilterApplier.Refreshes;
        ClickPin("BCM_PinnedFilterMode");
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Or && KeywordFilterState.SelectedTags.Count() == 2
            && KeywordFilterApplier.Refreshes == refreshes && KeywordSetTray.ActiveKind == FilterTrayKind.Keywords,
            "The pinned next-operator button changes future additions and cannot accidentally trigger Back");
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        var oldGlow = fixture.Items.Single(item => item.Text == "Adapt").m_selectedGlow.GetComponentsInChildren<Renderer>(true).Single();
        var tint = new MaterialPropertyBlock(); oldGlow.GetPropertyBlock(tint);
        Check(tint.Color.r > 0.9f && tint.Color.g > 0.6f && tint.Color.b < 0.2f
            && KeywordFilterState.SelectedTags.Count() == 3,
            "Earlier OR groups must glow yellow while a keyword can occur again in the current group");
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        oldGlow.GetPropertyBlock(tint);
        Check(KeywordFilterState.SelectedTags.Count() == 2 && !tint.Color.Equals(new Color(1f, 0.78f, 0.12f, 1f)),
            "Removing the last OR branch restores the previous group's original glow");
        ClickPin("BCM_PinnedFilterClear");
        Check(!KeywordFilterState.IsActive && fixture.Items.All(item => !item.Selected)
            && fixture.PinnedItems.Length == 5 && KeywordSetTray.ActiveKind == FilterTrayKind.Keywords,
            "Pinned Clear All clears every term and glow without navigating away");
        ChooseOperator(FilterMode.Or);
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        fixture.Items.Single(item => item.Text == "Adapt").GetComponent<PegUIElement>().Release();
        Check(KeywordFilterState.SelectedTags.Count() == 2 && fixture.Items.Where(item => item.Selected)
            .SelectMany(item => item.m_selectedGlow.GetComponentsInChildren<Renderer>(true)).All(renderer => {
                var color = new MaterialPropertyBlock(); renderer.GetPropertyBlock(color);
                return !color.Color.Equals(new Color(1f, 0.78f, 0.12f, 1f));
            }), "Pure OR selections keep their original blue glow throughout the Next menu");
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        Check(KeywordFilterState.SelectedTags.Count() == 1 && !fixture.Items.Single(item => item.Text == "Taunt").Selected,
            "A previously selected OR filter can be removed by clicking it again in the Next menu");
        ClickPin("BCM_PinnedFilterClear");
        ChooseOperator(FilterMode.Or);
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        fixture.Items.Single(item => item.Text == "Adapt").GetComponent<PegUIElement>().Release();
        ChooseOperator(FilterMode.And);
        fixture.Items.Single(item => item.Text == "Battlecry").GetComponent<PegUIElement>().Release();
        ChooseOperator(FilterMode.Or);
        fixture.Items.Single(item => item.Text == "Frenzy").GetComponent<PegUIElement>().Release();
        bool Yellow(string label)
        {
            var color = new MaterialPropertyBlock();
            fixture.Items.Single(item => item.Text == label).m_selectedGlow.GetComponentsInChildren<Renderer>(true).Single()
                .GetPropertyBlock(color);
            return color.Color.Equals(new Color(1f, 0.78f, 0.12f, 1f));
        }
        Check(!Yellow("Taunt") && Yellow("Adapt") && Yellow("Battlecry") && !Yellow("Frenzy"),
            "In A OR (B AND C) OR D, both standalone branches stay blue and only the older AND group turns yellow");
        fixture.Items.Single(item => item.Text == "Battlecry").GetComponent<PegUIElement>().Release();
        Check(!Yellow("Taunt") && !Yellow("Adapt") && !Yellow("Frenzy") && KeywordFilterState.SelectedTags.Count() == 3,
            "Removing the AND partner restores the surviving singleton's original blue glow without rebuilding");
        ClickPin("BCM_PinnedFilterClear");
        CheckPinnedControlRows(fixture, ClickPin);
        CheckInitialFilterRailCover(fixture);
        CheckReusableFilterRows(fixture, ClickPin);
        CheckIncrementalHiddenRows(fixture);
        CheckNotScrollRefresh(fixture, ClickPin);
        CheckAllCategoryRowReuse(fixture);
        CheckWideExpansionPoolReuse(fixture);
        CheckExpansionIcons(fixture);
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Hidden;
        CollectionQualitySettings.ShowFilterClearAll = false;
        KeywordSetTray.RebuildRows();
        Check(fixture.PinnedItems.Length == 1 && fixture.PinnedItems[0].Text == "Back", "Both optional controls can be hidden independently of Back");
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        CollectionQualitySettings.ShowFilterClearAll = true;
        CheckStableHiddenFilterGrid(fixture);
        CheckRootClearAll(fixture, ClickPin);
        CheckCuratedCategoryMenu(fixture, ClickPin);
        CheckCategoryDeckMenu(fixture);
        CheckFilterControlGroups(fixture);
        fixture.UsePinnedControls = false;
        KeywordFilterState.Clear();
        FilterModes.StartSession();
        fixture.ClearGesture();
    }

    private static void CheckIncrementalHiddenRows(KeywordSetTray.GridFixture fixture)
    {
        int columns = CollectionQualitySettings.FilterMenuColumns;
        CollectionQualitySettings.FilterMenuColumns = 2;
        CollectionQualitySettings.HideEmptyFilters = true;
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        KeywordFilterState.Clear();
        bool hideAdapt = false;
        FilterAvailability.KeywordVisible = tag => !hideAdapt || ((int)tag != 546 && (int)tag != 1637);
        KeywordSetTray.RebuildRows();
        var items = fixture.Items.ToDictionary(item => item.Text);
        var pins = fixture.PinnedItems.ToArray();
        var frame = fixture.Frame.sharedMesh;
        var kabalMesh = items["Frenzy"].GetComponentsInChildren<MeshFilter>(true).Single(filter => filter.gameObject.name == "Row face").sharedMesh;
        int rows = fixture.Rows, rebuilds = KeywordSetTray.FixtureRebuilds;
        hideAdapt = true;
        items["Taunt"].GetComponent<PegUIElement>().Release();
        Check(fixture.Items.All(item => item.Text != "Adapt" && item.Text != "Expansion-Keywords")
            && !items["Adapt"].gameObject.activeSelf && !items["Expansion-Keywords"].gameObject.activeSelf,
            "Incremental availability changes hide the empty option and its empty group heading");
        Check(fixture.Items.All(item => items[item.Text] == item) && pins.SequenceEqual(fixture.PinnedItems)
            && fixture.Frame.sharedMesh == frame && fixture.Rows == rows && fixture.Columns == 2
            && KeywordSetTray.FixtureRebuilds == rebuilds,
            "Hiding an option retains existing rows, pinned controls, frame meshes and the original grid dimensions");
        Check(items["Frenzy"].GetComponentsInChildren<MeshFilter>(true).Single(filter => filter.gameObject.name == "Row face").sharedMesh == kabalMesh,
            "Moving an unchanged row reuses its native sliced mesh instead of allocating it again");
        hideAdapt = false;
        items["Taunt"].GetComponent<PegUIElement>().Release();
        Check(fixture.Items.Single(item => item.Text == "Adapt") == items["Adapt"]
            && fixture.Items.Single(item => item.Text == "Expansion-Keywords") == items["Expansion-Keywords"]
            && KeywordSetTray.FixtureRebuilds == rebuilds,
            "An available option and its heading return from the local row pool without recreating buttons");
        // Also cover a row that was unavailable when the menu was first built.
        hideAdapt = true;
        KeywordSetTray.RebuildRows();
        var preserved = fixture.Items.ToDictionary(item => item.Text);
        var oldPins = fixture.PinnedItems.ToArray();
        rebuilds = KeywordSetTray.FixtureRebuilds;
        hideAdapt = false;
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        Check(fixture.Items.Any(item => item.Text == "Adapt") && preserved.All(pair => fixture.Items.Contains(pair.Value))
            && oldPins.SequenceEqual(fixture.PinnedItems) && KeywordSetTray.FixtureRebuilds == rebuilds,
            "Newly available options are created individually while the rest of the tray remains intact");
        FilterAvailability.KeywordVisible = null;
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionQualitySettings.FilterMenuColumns = columns;
        KeywordFilterState.Clear();
        KeywordSetTray.RebuildRows();
    }

    private static void CheckNotScrollRefresh(KeywordSetTray.GridFixture fixture, Action<string> clickPin)
    {
        var entries = KeywordCatalog.Entries;
        int columns = CollectionQualitySettings.FilterMenuColumns, rows = CollectionQualitySettings.FilterMenuRows;
        var behavior = CollectionQualitySettings.FilterModeBehavior;
        var scroller = fixture.Scroller;
        try
        {
            CollectionQualitySettings.FilterMenuColumns = 2; CollectionQualitySettings.FilterMenuRows = 4;
            CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
            CollectionQualitySettings.HideEmptyFilters = true;
            KeywordCatalog.Entries = Enumerable.Range(10000, 100).Select(i => new KeywordCatalog.Entry((GAME_TAG)i,
                new[] { (GAME_TAG)i }, true, false, "Scroll filter " + i)).ToArray();
            FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or); KeywordFilterState.Clear();
            FilterAvailability.KeywordVisible = tag => !FilterModes.IsNegative(FilterModes.Get(FilterTrayKind.Keywords)) || (int)tag < 10030;
            KeywordSetTray.RebuildRows();
            Transform contents = scroller.ScrollObject.transform;
            Vector3 top = contents.localPosition, nativeStart = top;
            scroller.OnResetScrollStartPosition = () => nativeStart = contents.localPosition;
            scroller.OnUpdateScroll = () => contents.localPosition = nativeStart + new Vector3(0f, 0f, fixture.ScrollRange * scroller.ScrollValue);
            scroller.SetScrollImmediate(.85f);
            for (int i = 0; i < 4; i++)
            {
                clickPin("BCM_PinnedFilterNot");
                Check(Vector3.Distance(nativeStart, top) < .001f
                    && Near(scroller.ScrollValue, .85f) && Near(scroller.ImmediateScrollValue, .85f)
                    && Near(contents.localPosition.z - top.z, fixture.ScrollRange * .85f),
                    "NOT refresh rebases at the unscrolled origin and applies the retained scrollbar fraction exactly once");
                scroller.UpdateScroll();
                Check(Near(contents.localPosition.z - top.z, fixture.ScrollRange * .85f),
                    "A subsequent native scroll update cannot jump beyond the refreshed list");
            }
        }
        finally
        {
            scroller.SetScrollImmediate(0f); scroller.OnUpdateScroll = null; scroller.OnResetScrollStartPosition = null;
            KeywordCatalog.Entries = entries; FilterAvailability.KeywordVisible = null;
            CollectionQualitySettings.FilterMenuColumns = columns; CollectionQualitySettings.FilterMenuRows = rows;
            CollectionQualitySettings.FilterModeBehavior = behavior; CollectionQualitySettings.HideEmptyFilters = false;
            FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or); KeywordFilterState.Clear(); KeywordSetTray.RebuildRows();
        }
    }

    private static void CheckReusableFilterRows(KeywordSetTray.GridFixture fixture, Action<string> clickPin)
    {
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        KeywordSetTray.RebuildRows();
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        fixture.Items.Single(item => item.Text == "Adapt").GetComponent<PegUIElement>().Release();
        var rows = fixture.Items.ToArray();
        var pins = fixture.PinnedItems.ToArray();
        int rebuilds = KeywordSetTray.FixtureRebuilds;
        fixture.Scroller.SetScrollImmediate(0.4f);
        KeywordSetTray.SetShown(false, "reuse test");
        MelonLoader.MelonCoroutines.Complete();
        KeywordSetTray.SetShown(true, FilterTrayKind.Keywords, "reuse test");
        MelonLoader.MelonCoroutines.Complete();
        Check(rows.SequenceEqual(fixture.Items) && pins.SequenceEqual(fixture.PinnedItems)
            && KeywordSetTray.FixtureRebuilds == rebuilds && Near(fixture.Scroller.ScrollValue, 0.4f),
            "Reopening an unchanged menu with multiple filters reuses every row and pinned button, preserving scroll");
        clickPin("BCM_PinnedFilterMode");
        Check(FilterModes.Operation(FilterTrayKind.Keywords) == FilterMode.And && KeywordFilterState.SelectedTags.Count() == 2,
            "Two connected Next filters cycle the connector without enabling NOT");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And); KeywordSetTray.RefreshAfterSelection();
        Check(rows.SequenceEqual(fixture.Items) && pins.SequenceEqual(fixture.PinnedItems)
            && KeywordSetTray.FixtureRebuilds == rebuilds,
            "Changing the next operator updates labels and glows without rebuilding the tray");
        var color = new MaterialPropertyBlock();
        fixture.Items.Single(item => item.Text == "Taunt").m_selectedGlow.GetComponentsInChildren<Renderer>(true).Single()
            .GetPropertyBlock(color);
        Check(color.Color.Equals(new Color(1f, 0.78f, 0.12f, 1f)),
            "Choosing AND immediately makes the reusable earlier OR filter yellow");
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        Check(KeywordFilterState.SelectedTags.Count() == 3 && rows.SequenceEqual(fixture.Items)
            && KeywordSetTray.FixtureRebuilds == rebuilds,
            "Clicking the yellow AND candidate appends a second occurrence without deleting or rebuilding the old row");
        clickPin("BCM_PinnedFilterMode"); // AND -> OR.
        fixture.Items.Single(item => item.Text == "Taunt").m_selectedGlow.GetComponentsInChildren<Renderer>(true).Single()
            .GetPropertyBlock(color);
        Check(color.Color.Equals(new Color(1f, 0.78f, 0.12f, 1f)),
            "Choosing OR immediately marks an AND-bound filter for a new branch");
        clickPin("BCM_PinnedFilterClear");
        clickPin("BCM_PinnedFilterMode");
        while (FilterModes.Get(FilterTrayKind.Keywords) != FilterMode.Single) clickPin("BCM_PinnedFilterMode");
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Single
            && fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_PinnedFilterMode").Text == "Next: Single",
            "Clearing connected filters restores Single to the Next button");
        fixture.Items.Single(item => item.Text == "Taunt").GetComponent<PegUIElement>().Release();
        clickPin("BCM_PinnedFilterMode"); // Single -> AND with one selection.
        clickPin("BCM_PinnedFilterMode"); // AND -> OR with one selection.
        clickPin("BCM_PinnedFilterMode");
        while (FilterModes.Get(FilterTrayKind.Keywords) != FilterMode.Single) clickPin("BCM_PinnedFilterMode");
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Single && KeywordFilterState.SelectedCount == 1,
            "One selected filter still allows the complete Next cycle including Single");
        clickPin("BCM_PinnedFilterClear");
        var expansionMode = FilterModes.Get(FilterTrayKind.Expansions);
        var choices = fixture.Native.m_items.Where(item => !item.IsHeader).Take(2).ToArray();
        ExpansionFilterState.Clear();
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Or);
        foreach (var choice in choices) ExpansionFilterState.SetSelected(choice, true);
        CollectionQualitySettings.HideEmptyFilters = true;
        CollectionFilterMenu.ShowExpansions();
        MelonLoader.MelonCoroutines.Complete();
        fixture.OpenLogicControls();
        int expansionRebuilds = KeywordSetTray.FixtureRebuilds, refreshes = KeywordFilterApplier.Refreshes;
        var expansionRows = fixture.Items.ToArray();
        Check(choices.Length == 2 && ExpansionFilterState.SelectedCount == 2
            && fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_PinnedFilterMode").Text == "Next: OR",
            "Two connected expansion filters retain their positive OR expression");
        clickPin("BCM_PinnedFilterNot");
        clickPin("BCM_PinnedFilterMode");
        Check(KeywordSetTray.FixtureRebuilds == expansionRebuilds && KeywordFilterApplier.Refreshes == refreshes
            && expansionRows.SequenceEqual(fixture.Items) && ExpansionFilterState.SelectedCount == 2
            && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.AndNot,
            "Choosing AND NOT updates the pending expansion operator without refreshing results or rebuilding unchanged rows");
        CollectionQualitySettings.HideEmptyFilters = false;
        ExpansionFilterState.Clear(); FilterModes.Set(FilterTrayKind.Expansions, expansionMode);
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Keywords);
        MelonLoader.MelonCoroutines.Complete();
    }

    private static void CheckInitialFilterRailCover(KeywordSetTray.GridFixture fixture)
    {
        int rows = CollectionQualitySettings.FilterMenuRows, columns = CollectionQualitySettings.FilterMenuColumns;
        var previous = fixture.Scroller.m_scrollTrackCover;
        var cover = new GameObject("Native closed filter rail");
        fixture.Scroller.m_scrollTrackCover = cover;
        try
        {
            fixture.ClearGesture(); CollectionQualitySettings.FilterMenuRows = 16; CollectionQualitySettings.FilterMenuColumns = 4;
            CollectionFilterMenu.ShowMenu(); MelonLoader.MelonCoroutines.Complete();
            Check(cover.activeSelf, "A fitting root filter menu closes its native rail on the first opening");
            cover.SetActive(false); // Native UIBScrollable.Start unconditionally does this.
            Time.frameCount++; KeywordSetTray.PollOutsideClick();
            Check(cover.activeSelf, "The first-frame native Start cannot leave the fitting menu's rail uncovered");
            CollectionQualitySettings.FilterMenuRows = 2; CollectionQualitySettings.FilterMenuColumns = 1;
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Keywords); MelonLoader.MelonCoroutines.Complete();
            Time.frameCount++; KeywordSetTray.PollOutsideClick();
            Check(!cover.activeSelf, "A submenu that needs scrolling opens its native rail");
            CollectionQualitySettings.FilterMenuRows = 16; CollectionQualitySettings.FilterMenuColumns = 4;
            CollectionFilterMenu.ShowMenu(); MelonLoader.MelonCoroutines.Complete();
            Check(cover.activeSelf, "Returning to the fitting root menu closes the same rail again");
        }
        finally
        {
            fixture.Scroller.m_scrollTrackCover = previous; UnityEngine.Object.Destroy(cover);
            CollectionQualitySettings.FilterMenuRows = rows; CollectionQualitySettings.FilterMenuColumns = columns;
            CollectionFilterMenu.ShowFilter(FilterTrayKind.Keywords); MelonLoader.MelonCoroutines.Complete();
            KeywordSetTray.RebuildRows();
        }
    }

    private static void CheckPinnedControlRows(KeywordSetTray.GridFixture fixture, Action<string> clickPin)
    {
        int rows = CollectionQualitySettings.FilterMenuRows, columns = CollectionQualitySettings.FilterMenuColumns;
        FilterMode mode = FilterModes.Get(FilterTrayKind.Keywords);
        Bounds Face(SetFilterItem item) => item.GetComponentsInChildren<MeshRenderer>(false)
            .Single(renderer => renderer.gameObject.name == "Row face").bounds;
        Bounds Pin(string name) => Face(fixture.PinnedItems.Single(item => item.gameObject.name == name));
        CollectionQualitySettings.FilterMenuRows = 4;
        float previousHeight = 0f;
        foreach (int count in new[] { 1, 2, 3, 4 })
        {
            CollectionQualitySettings.FilterMenuColumns = count;
            KeywordSetTray.RebuildRows();
            Bounds back = Pin("BCM_PinnedFilterBack"), next = Pin("BCM_PinnedFilterMode"), not = Pin("BCM_PinnedFilterNot"), clear = Pin("BCM_PinnedFilterClear");
            Bounds group = Pin("BCM_ShowLogicControls");
            Check(back.min.z > next.max.z && group.min.z > next.max.z,
                "Navigation and group toggles remain above their expanded actions");
            if (count == 1)
                Check(back.min.z > group.max.z && next.min.z > not.max.z && not.min.z > clear.max.z,
                    "Single-column tools form one consecutive stack");
            else
                Check(Near(back.center.z, group.center.z) && Near(next.center.z, not.center.z)
                    && Near(next.max.x, not.min.x), "Grouped operators share a contiguous row below the navigation toggles");
            if (count >= 3) Check(Near(next.center.z, clear.center.z) && Near(not.max.x, clear.min.x),
                "Three or more columns keep the entire logic group on one consecutive row");
            float frameHeight = fixture.Frame.GetComponent<Renderer>().bounds.size.z / fixture.Fit;
            if (count > 1) Check(frameHeight <= previousHeight + .001f, "Sharing tool rows never adds header height");
            previousHeight = frameHeight;
            var positions = fixture.PinnedItems.ToDictionary(item => item, item => item.transform.position);
            fixture.ShiftContents(1f); fixture.RefreshVisible();
            Check(positions.All(pair => pair.Key.transform.position.Equals(pair.Value)),
                "Every shared header button remains fixed while the list scrolls");
            fixture.ShiftContents(-1f); fixture.RefreshVisible();
            float clipTop = fixture.BackMask.transform.position.z + fixture.BackMask.m_Height * fixture.BackMask.transform.lossyScale.z * 0.5f;
            Check(clipTop < Math.Min(back.min.z, Math.Min(next.min.z, Math.Min(not.min.z, clear.min.z))),
                "The list's clipping mask starts below every pinned row");
            foreach (var pin in fixture.PinnedItems)
            {
                Bounds face = Face(pin), hit = pin.GetComponent<BoxCollider>().bounds;
                Check(hit.min.x >= face.min.x && hit.max.x <= face.max.x,
                    "Each shared button retains an independent hit target inside its own artwork");
                Check(CounterTextLayout.TryScreenInkRect(pin.m_uberText, CameraUtils.Camera, out Rect text),
                    "Shared header captions retain visible native glyphs");
                Rect background = BannerPartInk(pin.GetComponentsInChildren<MeshRenderer>(false)
                    .Single(renderer => renderer.gameObject.name == "Row face"));
                Check(text.xMin >= background.xMin - 0.1f && text.xMax <= background.xMax + 0.1f,
                    "Shared header captions fit inside their own buttons without shrinking the font");
            }
            FilterMode expected = FilterModes.Next(FilterTrayKind.Keywords);
            clickPin("BCM_PinnedFilterMode");
            Check(FilterModes.Get(FilterTrayKind.Keywords) == expected && KeywordSetTray.ActiveKind == FilterTrayKind.Keywords,
                "The shared Mode hit target changes the operator without triggering Back");
            int refreshes = KeywordFilterApplier.Refreshes;
            clickPin("BCM_PinnedFilterNot");
            var toggle = fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_PinnedFilterNot");
            Check(FilterModes.IsNegative(FilterModes.Get(FilterTrayKind.Keywords)) && toggle.Selected && toggle.Text == "NOT: On"
                && KeywordFilterApplier.Refreshes == refreshes && KeywordSetTray.ActiveKind == FilterTrayKind.Keywords,
                "The separate NOT hit target enables negation without applying or navigating the Next expression");
            clickPin("BCM_PinnedFilterNot");
            Check(FilterModes.Get(FilterTrayKind.Keywords) == expected && !toggle.Selected && toggle.Text == "NOT: Off",
                "A second NOT click restores the connector and removes the active toggle glow");
            KeywordFilterState.SetSelected((GAME_TAG)190, true);
            clickPin("BCM_PinnedFilterClear");
            Check(!KeywordFilterState.IsActive && KeywordSetTray.ActiveKind == FilterTrayKind.Keywords,
                "The shared Clear All hit target clears selections without triggering Back or Mode");
        }
        CollectionQualitySettings.FilterMenuColumns = 2;
        foreach (bool hideMode in new[] { false, true })
        foreach (bool showClear in new[] { false, true })
        {
            CollectionQualitySettings.FilterModeBehavior = hideMode ? FilterModeBehavior.Hidden : FilterModeBehavior.Expression;
            CollectionQualitySettings.ShowFilterClearAll = showClear;
            KeywordSetTray.RebuildRows();
            if (!hideMode && showClear) continue;
            var pins = fixture.PinnedItems.OrderBy(item => Face(item).center.x).ToArray();
            Check(pins.Length == 1 + (hideMode && !showClear ? 0 : 1 + (hideMode ? 0 : 2) + (showClear ? 1 : 0))
                && (hideMode || pins.Any(item => item.gameObject.name == "BCM_PinnedFilterNot")),
                "Hiding Mode hides both operator controls; hiding Clear All retains the independent NOT button");
        }
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
        CollectionQualitySettings.ShowFilterClearAll = true;
        CollectionQualitySettings.FilterMenuRows = rows; CollectionQualitySettings.FilterMenuColumns = columns;
        FilterModes.Set(FilterTrayKind.Keywords, mode);
        KeywordSetTray.RebuildRows();
    }

    private static void CheckRootClearAll(KeywordSetTray.GridFixture fixture, Action<string> clickPin)
    {
        var source = fixture.Native.m_selected;
        var callback = source.Callback;
        int nativeClears = 0, extraClears = ExtraFilterState.Clears;
        source.Callback = (sets, cards, format, item, transition) => nativeClears++;
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        KeywordFilterState.SetSelected((GAME_TAG)190, true);
        KeywordFilterState.SetSelected((GAME_TAG)546, true);
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Single);
        CollectionFilterMenu.ShowMenu();
        MelonLoader.MelonCoroutines.Complete();
        Check(fixture.PinnedItems.Length == 1 && fixture.PinnedItems[0].Text == "Clear All"
            && fixture.PinnedItems[0].gameObject.name == "BCM_PinnedClearAllFilters",
            "The root menu pins a global Clear All without adding Back or Mode");
        int refreshes = KeywordFilterApplier.Refreshes;
        clickPin("BCM_PinnedClearAllFilters");
        Check(!KeywordFilterState.IsActive && !ExpansionFilterState.SelectedItems.Any()
            && ExtraFilterState.Clears == extraClears + 1 && nativeClears == 1
            && KeywordFilterApplier.Refreshes == refreshes + 1 && KeywordSetTray.ActiveKind == FilterTrayKind.Menu,
            "Root Clear All clears every category and refreshes once without leaving the root menu");
        Check(fixture.PinnedItems.Length == 0, "An unfiltered root menu hides Clear All immediately after clearing");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        Check(!KeywordFilterState.IsActive, "Global Clear All also discards choices suspended by Single");
        CollectionQualitySettings.ShowFilterClearAll = false;
        KeywordSetTray.RebuildRows();
        Check(fixture.PinnedItems.Length == 1, "A non-Single mode keeps root Clear All visible even when optional Clear All is hidden");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Single); KeywordSetTray.RebuildRows();
        Check(fixture.PinnedItems.Length == 0, "Returning every mode to Single honors the hidden root Clear All setting");
        CollectionQualitySettings.ShowFilterClearAll = true;
        source.Callback = callback;
    }

    private static void CheckCuratedCategoryMenu(KeywordSetTray.GridFixture fixture, Action<string> clickPin)
    {
        var previousKind = KeywordSetTray.ActiveKind;
        ExtraFilterState.ClearAll();
        FilterModes.Set(FilterTrayKind.Categories, FilterMode.Or);
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Categories);
        MelonLoader.MelonCoroutines.Complete();
        Check(fixture.Items.Select(item => item.Text).SequenceEqual(new[] {
            "Categories", "AoE", "Single-Target Removal", "SMOrc", "Value", "Win Conditions", "Draw", "Unique Effects", "Combos", "TECHW", "Uncategorized"
        }), "Categories exposes the deck import action and the nine curated roles in the shared native filter tray");
        fixture.OpenLogicControls();
        Check(fixture.PinnedItems.Single(item => item.gameObject.name == "BCM_PinnedFilterMode").Text == "Mode: OR",
            "Curated categories start with their independent OR mode");
        int refreshes = KeywordFilterApplier.Refreshes;
        fixture.Items.Single(item => item.Text == "AoE").GetComponent<PegUIElement>().Release();
        fixture.Items.Single(item => item.Text == "Draw").GetComponent<PegUIElement>().Release();
        Check(ExtraFilterState.HasCategory(CuratedCategory.AoE) && ExtraFilterState.HasCategory(CuratedCategory.Draw)
            && fixture.Items.Count(item => item.Selected) == 2 && KeywordFilterApplier.Refreshes == refreshes + 2,
            "Curated row clicks refresh once each and retain their native selection glows");
        clickPin("BCM_PinnedFilterMode");
        clickPin("BCM_PinnedFilterNot");
        clickPin("BCM_PinnedFilterMode");
        Check(FilterModes.Get(FilterTrayKind.Categories) == FilterMode.AndNot
            && fixture.Items.Any(item => item.Text == "NOT · AoE") && fixture.Items.Any(item => item.Text == "NOT · Draw"),
            "Categories offers AND NOT and marks each excluded role explicitly");
        ExtraFilterState.ToggleType(TAG_CARDTYPE.SPELL);
        clickPin("BCM_PinnedFilterClear");
        Check(ExtraFilterState.SelectedCount(FilterTrayKind.Categories) == 0
            && ExtraFilterState.HasType(TAG_CARDTYPE.SPELL),
            "The Categories Clear All only clears curated selections");
        ExtraFilterState.ClearAll();
        fixture.Items.Single(item => item.Text == "Combos").GetComponent<PegUIElement>().Release();
        CollectionFilterMenu.ShowMenu(); MelonLoader.MelonCoroutines.Complete();
        Check(fixture.PinnedItems.Length == 1 && fixture.PinnedItems[0].Text == "Clear All",
            "A curated-only selection activates the root Clear All control");
        clickPin("BCM_PinnedClearAllFilters");
        Check(!ExtraFilterState.HasCategory(CuratedCategory.Combos) && fixture.PinnedItems.Length == 0,
            "Global Clear All also removes curated-only selections");
        FilterModes.Set(FilterTrayKind.Categories, FilterMode.Or);
        CollectionFilterMenu.ShowFilter(previousKind); MelonLoader.MelonCoroutines.Complete();
    }

    private static void CheckStableHiddenFilterGrid(KeywordSetTray.GridFixture fixture)
    {
        var entries = KeywordCatalog.Entries;
        int rows = CollectionQualitySettings.FilterMenuRows, columns = CollectionQualitySettings.FilterMenuColumns;
        KeywordCatalog.Entries = Enumerable.Range(10000, 110).Select(i => new KeywordCatalog.Entry((GAME_TAG)i,
            new[] { (GAME_TAG)i }, true, false, "Filter " + i)).ToArray();
        CollectionQualitySettings.FilterMenuRows = CollectionQualitySettings.FilterMenuColumns = 0;
        KeywordSetTray.RebuildRows();
        int stableRows = fixture.Rows, stableColumns = fixture.Columns;
        Rect frame = fixture.ScreenRect;
        CollectionQualitySettings.HideEmptyFilters = true;
        FilterAvailability.KeywordVisible = tag => (int)tag == 10000;
        KeywordSetTray.RebuildRows();
        Check(fixture.Rows == stableRows && fixture.Columns == stableColumns && fixture.Items.Count(item => !item.IsHeader) == 1
            && Near(fixture.ScreenRect.width, frame.width) && Near(fixture.ScreenRect.height, frame.height),
            "Hiding almost all options must retain the full list's automatic rows, columns and frame dimensions");
        FilterAvailability.KeywordVisible = _ => false;
        KeywordSetTray.RebuildRows();
        Check(fixture.Rows == stableRows && fixture.Columns == stableColumns && fixture.Items.Count == 0
            && fixture.PinnedItems.Length == 5, "Even a completely empty list keeps its frame and fixed controls");
        FilterAvailability.KeywordVisible = null;
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionQualitySettings.FilterMenuRows = rows; CollectionQualitySettings.FilterMenuColumns = columns;
        KeywordCatalog.Entries = entries;
        KeywordSetTray.RebuildRows();
    }

    private static void CheckSettingsScrolling(ModsSettingsMenu menu)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        float Read(string name) => (float)typeof(ModsSettingsMenu).GetField(name, flags).GetValue(menu);
        var update = typeof(ModsSettingsMenu).GetMethod("UpdateSettingsScroll", flags);
        var layout = typeof(ModsSettingsMenu).GetMethod("LayoutMenu", flags);
        Rect viewport = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsViewport", flags).GetValue(menu);
        Check(Read("m_settingsScrollRange") > 0f, "Settings beyond the visible panel surface must create a scroll range");
        float height = menu.BodyHeight, width = menu.BodyWidth;
        var all = menu.GetComponentsInChildren<CheckBox>(true);
        var empty = all.Single(box => box.m_uberText.Text == "Hide filters without results");
        Input.mousePosition = viewport.center;
        Input.mouseScrollDelta = new Vector2(0f, -100f);
        update.Invoke(menu, null);
        Check(Read("m_settingsScroll") > 0f && empty.gameObject.activeInHierarchy
            && Near(menu.BodyHeight, height) && Near(menu.BodyWidth, width),
            "Wheel scrolling reveals overflow settings while retaining the original menu dimensions");
        var craftingSearch = menu.GetComponentsInChildren<DropdownControl>(true).Single(control => control.gameObject.name == "BCM_ModsCollectionopensearch");
        var protection = all.Single(box => box.gameObject.name == "BCM_ModsNeverDustSignaturesDiamonds");
        var nativeBar = (GameObject)typeof(ModsSettingsMenu).GetField("m_settingsScrollbar", flags).GetValue(menu);
        Check(nativeBar != null && nativeBar.activeInHierarchy && craftingSearch.gameObject.activeInHierarchy && protection.gameObject.activeInHierarchy,
            "The actual settings scrollbar reveals both Crafting controls without shrinking the viewport");
        craftingSearch.SelectOption(CollectionOpenSearchMode.New);
        Check(CollectionQualitySettings.OpenSearch == CollectionOpenSearchMode.New, "Scrolling makes the native New opening-search setting selectable");
        craftingSearch.SelectOption(CollectionOpenSearchMode.Off);
        empty.Click();
        Check(CollectionQualitySettings.HideEmptyFilters, "The overflow checkbox remains interactive");
        empty.Click();
        Input.mouseScrollDelta = new Vector2(0f, 100f);
        update.Invoke(menu, null);
        Input.mouseScrollDelta = Vector2.zero;
        Check(Read("m_settingsScroll") == 0f && !CollectionQualitySettings.HideEmptyFilters, "Settings can scroll back to the start and disable hiding");
        CheckSettingsViewportClipping(menu);
        CheckSettingsScrollbar(menu);
        var behavior = menu.GetComponentsInChildren<DropdownControl>(true).Single(control => control.Items[0] is FilterModeBehavior);
        behavior.SelectOption(FilterModeBehavior.Hidden);
        Check(CollectionQualitySettings.FilterModeBehavior == FilterModeBehavior.Hidden, "The settings dropdown can hide Mode");
        behavior.SelectOption(FilterModeBehavior.Expression);
        Check(CollectionQualitySettings.FilterModeBehavior == FilterModeBehavior.Expression, "The settings dropdown selects next-filter operations");
        behavior.SelectOption(FilterModeBehavior.Category);
        var clear = all.Single(box => box.m_uberText.Text == "Show Clear All");
        clear.Click();
        Check(!CollectionQualitySettings.ShowFilterClearAll, "The independent checkbox can hide Clear All");
        clear.Click();
        layout.Invoke(menu, null);
    }

    private static void CheckSettingsViewportClipping(ModsSettingsMenu menu)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Read(string name) => typeof(ModsSettingsMenu).GetField(name, flags).GetValue(menu);
        var apply = typeof(ModsSettingsMenu).GetMethod("ApplySettingsScroll", flags);
        var scroll = typeof(ModsSettingsMenu).GetField("m_settingsScroll", flags);
        var viewport = (Rect)Read("m_settingsViewport");
        Check(menu.GetComponentsInChildren<CameraMask>(true).Length == 0
            && menu.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.gameObject.layer != (int)GameLayer.CameraMask),
            "Settings keeps its original render layers and never creates a camera mask");
        int partialRows = 0, partialHits = 0;
        for (int step = 0; step <= 10; step++)
        {
            float offset = (float)Read("m_settingsScrollRange") * step / 10f;
            scroll.SetValue(menu, offset); apply.Invoke(menu, null);
            foreach (object row in (System.Collections.IEnumerable)Read("m_scrollRows"))
            {
                Type type = row.GetType();
                Rect bounds = (Rect)type.GetField("Bounds").GetValue(row);
                var go = (GameObject)type.GetField("Object").GetValue(row);
                float bottom = bounds.yMin + offset, top = bounds.yMax + offset;
                bool partial = (bottom < viewport.yMin && top > viewport.yMin)
                    || (top > viewport.yMax && bottom < viewport.yMax);
                if (!partial) continue;
                partialRows++;
                Check(go.activeInHierarchy && go.layer == (int)GameLayer.HighPriorityUI,
                    "A partially visible row stays alive on its original UI layer instead of vanishing at the edge");
                foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(false))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || mesh.name?.EndsWith("_BCM_Clipped") != true) continue;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = CameraUtils.Camera.WorldToScreenPoint(filter.transform.TransformPoint(vertex));
                        Check(point.x >= viewport.xMin - .01f && point.x <= viewport.xMax + .01f
                            && point.y >= viewport.yMin - .01f && point.y <= viewport.yMax + .01f,
                            "Actual clipped triangles stay inside the panel on the ordinary UI renderer");
                    }
                }
                foreach (BoxCollider hit in go.GetComponentsInChildren<BoxCollider>(false))
                {
                    if (!hit.enabled) continue;
                    Rect area = ComboSceneGeometry.ProjectBounds(FilterTrayGeometry.TransformBounds(
                        new Bounds(hit.center, hit.size), hit.transform.TransformPoint), CameraUtils.Camera);
                    Check(area.yMin >= viewport.yMin - .01f && area.yMax <= viewport.yMax + .01f,
                        "A partially clipped control can only be clicked inside the visible panel");
                    partialHits++;
                }
            }
        }
        Check(partialRows > 0 && partialHits > 0, "The settings scroll range exercises real partial rows and clickable partial controls");
        foreach (DropdownControl control in menu.GetComponentsInChildren<DropdownControl>(true))
            Check(control.gameObject.layer == (int)GameLayer.HighPriorityUI
                && control.m_menu.gameObject.layer == (int)GameLayer.HighPriorityUI
                && control.m_cancelCatcher.gameObject.layer == (int)GameLayer.HighPriorityUI,
                "Expanded dropdowns and cancel catchers retain their unmasked overlay layer");
        scroll.SetValue(menu, 0f); apply.Invoke(menu, null);
        Check(menu.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.enabled && !renderer.forceRenderingOff
            && renderer.GetComponent<MeshFilter>()?.sharedMesh?.vertices.Length > 0),
            "Scrolling back leaves ordinary visible UI geometry; it cannot hide all settings renderers");
        CheckSettingsSkinClipping(menu);
        CheckSettingsTextSnapshot(menu);
    }

    private static void CheckSettingsSkinClipping(ModsSettingsMenu menu)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var viewport = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsViewport", flags).GetValue(menu);
        var rows = (System.Collections.IList)typeof(ModsSettingsMenu).GetField("m_scrollRows", flags).GetValue(menu);
        var update = typeof(ModsSettingsMenu).GetMethod("UpdateSettingsClipping", flags);
        var stop = typeof(ModsSettingsMenu).GetMethod("StopSettingsClipping", flags);
        var owner = new GameObject("NativeSkinnedSettingsControl"); owner.layer = (int)GameLayer.HighPriorityUI;
        owner.transform.SetParent(menu.transform, false);
        Rect area = new Rect(viewport.center.x, viewport.yMin - 20, 80, 40);
        Vector3 Point(float x, float y) => owner.transform.InverseTransformPoint(CameraUtils.Camera.ScreenToWorldPoint(new Vector3(x,y,1)));
        var mesh = new Mesh { name = "NativeControlSkin", subMeshCount = 1, vertices = new[] {
            Point(area.xMin,area.yMin), Point(area.xMax,area.yMin), Point(area.xMax,area.yMax), Point(area.xMin,area.yMax) } };
        mesh.SetTriangles(new List<int> { 0,1,2,0,2,3 },0,false); mesh.RecalculateBounds();
        var skin = owner.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
        skin.sharedMaterial = new Material { name = "NativeControlSkinMaterial" }; skin.renderingLayerMask = 8;
        skin.WorldBounds = FilterTrayGeometry.TransformBounds(mesh.bounds, owner.transform.TransformPoint);
        Type rowType = typeof(ModsSettingsMenu).GetNestedType("ScrollRow", flags);
        object row = Activator.CreateInstance(rowType, true);
        rowType.GetField("Object").SetValue(row, owner); rowType.GetField("Active").SetValue(row, true);
        rowType.GetField("Bounds").SetValue(row, area); rowType.GetField("Position").SetValue(row, owner.transform.position);
        rows.Add(row);
        try
        {
            update.Invoke(menu,null);
            var proxy = owner.GetComponentsInChildren<MeshRenderer>(false).Single(r => r.gameObject.name == "BCM_SettingsClippedSkin");
            Mesh clipped = proxy.GetComponent<MeshFilter>().sharedMesh;
            Check(skin.enabled && skin.forceRenderingOff && proxy.enabled && clipped.vertices.Length > 0
                && proxy.gameObject.layer == skin.gameObject.layer && proxy.sharedMaterial == skin.sharedMaterial
                && proxy.renderingLayerMask == skin.renderingLayerMask,
                "An edge skin has a visible clipped replacement on the same camera, material and rendering layers");
            foreach (Vector3 vertex in clipped.vertices)
            {
                Vector3 point = CameraUtils.Camera.WorldToScreenPoint(proxy.transform.TransformPoint(vertex));
                Check(point.y >= viewport.yMin - .001f && point.y <= viewport.yMax + .001f,
                    "Baked skin triangles are actually cut at the visible edge");
            }
            skin.enabled = false; update.Invoke(menu,null);
            Check(!proxy.gameObject.activeInHierarchy && !skin.forceRenderingOff,
                "Native control visibility remains authoritative while clipped");
            skin.enabled = true; update.Invoke(menu,null);
            Check(proxy.gameObject.activeInHierarchy && proxy.GetComponent<MeshFilter>().sharedMesh == clipped,
                "Native visibility changes reuse the same clipped mesh");
            stop.Invoke(menu,null);
            Check(!skin.forceRenderingOff && skin.sharedMesh == mesh && !mesh.Destroyed && clipped.Destroyed,
                "Clipping cleanup restores the original skin and releases only private geometry");
        }
        finally
        {
            rows.Remove(row); stop.Invoke(menu,null);
            UnityEngine.Object.Destroy(owner); UnityEngine.Object.Destroy(mesh);
            update.Invoke(menu,null);
        }
    }

    private static void CheckSettingsScrollbar(ModsSettingsMenu menu)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Read(string name) => typeof(ModsSettingsMenu).GetField(name, flags).GetValue(menu);
        Rect Track() => (Rect)Read("m_settingsTrackRect");
        Rect Thumb() => (Rect)Read("m_settingsThumbRect");
        float Scroll() => (float)Read("m_settingsScroll");
        float range = (float)Read("m_settingsScrollRange");
        Rect viewport = (Rect)Read("m_settingsViewport");
        var update = typeof(ModsSettingsMenu).GetMethod("UpdateSettingsScroll", flags);
        var blocker = typeof(ModsSettingsMenu).GetMethod("OnBlockerRelease", flags);
        void Frame(Vector3 pointer, bool down = false, bool held = false, bool up = false)
        {
            Input.mousePosition = pointer;
            Input.LeftPressed = down; Input.LeftHeld = held; Input.LeftReleased = up;
            Time.frameCount++; update.Invoke(menu, null);
        }
        var nativeSources = Resources.Objects;
        const string sceneAsset = "CollectionManager.prefab:native-scrollbar-test";
        const string barAsset = "CollectionDeckTray.prefab:native-scrollbar-test";
        var prefab = new GameObject("CollectionManagerAssetOnly"); prefab.SetActive(false);
        prefab.AddComponent<NestedPrefab>().m_Prefab = "CollectionDeckTrayPlatform.prefab:native-scrollbar-test";
        var variant = new GameObject("CollectionDeckTrayPlatformAssetOnly"); variant.SetActive(false);
        variant.AddComponent<NestedPrefabPlatformOverride>().m_Prefab = barAsset;
        AssetLoader.Get().Assets["CollectionDeckTrayPlatform.prefab:native-scrollbar-test"] = variant;
        AssetLoader.Get().Assets[sceneAsset] = prefab;
        AssetLoader.Get().Assets[barAsset] = nativeSources.OfType<UIBScrollable>().First().gameObject;
        int beforeLoad = AssetLoader.Get().InstantiateCalls;
        float beforeTime = Time.unscaledTime;
        SetFilterTray gripOnly = null;
        try
        {
            // Actual reported case: Settings opens in the hub, before any
            // collection scrollbar has been instantiated (loaded scrollers=0).
            Resources.Objects = Array.Empty<UnityEngine.Object>();
            NativeScrollbarArt.ReleasePrefabSources();
            typeof(ModsSettingsMenu).GetMethod("DestroySettingsScrollbar", flags).Invoke(menu, null);
            typeof(ModsSettingsMenu).GetMethod("LayoutSettingsScrollbar", flags).Invoke(menu, null);
            Check(Read("m_settingsScrollbar") is GameObject && AssetLoader.Get().InstantiateCalls == beforeLoad,
                "Cold-start Settings loads native rail/grip assets without opening or instantiating Collection");

            // The hub can also contain a valid native grip-only scroller.
            // Its input references must not suppress loading the complete art.
            gripOnly = MakeSettingsScrollbarTemplate();
            foreach (MeshRenderer renderer in gripOnly.GetComponentsInChildren<MeshRenderer>(true))
                if (!renderer.transform.IsChildOf(gripOnly.m_scroller.m_ScrollThumb.transform))
                    UnityEngine.Object.DestroyImmediate(renderer.GetComponent<MeshFilter>());
            NativeScrollbarArt.ReleasePrefabSources();
            typeof(ModsSettingsMenu).GetMethod("DestroySettingsScrollbar", flags).Invoke(menu, null);
            AssetLoader.Get().Assets.Remove(barAsset);
            typeof(ModsSettingsMenu).GetMethod("LayoutSettingsScrollbar", flags).Invoke(menu, null);
            Check(Read("m_settingsScrollbar") == null && !gripOnly.GetComponentsInChildren<MeshFilter>(true)
                .Single(filter => filter.gameObject.name == "NativeFilterGrip").sharedMesh.Destroyed,
                "Settings waits for a complete native rail without accepting a permanent grip-only bar or damaging its source");
            AssetLoader.Get().Assets[barAsset] = nativeSources.OfType<UIBScrollable>().First().gameObject;
            Time.unscaledTime += 1.1f;
            update.Invoke(menu, null);
            var completeBar = Read("m_settingsScrollbar") as GameObject;
            Check(completeBar != null && completeBar.GetComponentsInChildren<MeshRenderer>(true)
                .Any(renderer => renderer.gameObject.name == "NativeFilterRail")
                && AssetLoader.Get().InstantiateCalls == beforeLoad,
                "A live grip-only hub source cannot suppress loading the Settings rail from native prefab assets");
        }
        finally
        {
            Resources.Objects = nativeSources;
            Time.unscaledTime = beforeTime;
            AssetLoader.Get().Assets.Remove(sceneAsset); AssetLoader.Get().Assets.Remove(barAsset);
            AssetLoader.Get().Assets.Remove("CollectionDeckTrayPlatform.prefab:native-scrollbar-test");
            NativeScrollbarArt.ReleasePrefabSources(); UnityEngine.Object.Destroy(prefab); UnityEngine.Object.Destroy(variant);
            if (gripOnly != null) UnityEngine.Object.Destroy(gripOnly.gameObject);
        }
        var bar = (GameObject)Read("m_settingsScrollbar");
        var art = bar.GetComponentsInChildren<MeshRenderer>(true);
        Check(bar != null && bar.activeInHierarchy && art.Length == 4 && art.All(renderer => renderer.enabled
            && renderer.sharedMaterial.name == "NativeFilterScrollbarAtlas") && Track().width > 0f
            && Thumb().height > 0f && Thumb().height < Track().height,
            "Overflow settings must use the original Hearthstone rail, end caps and grip materials");
        Check(art.All(renderer => renderer.GetComponent<MeshFilter>().sharedMesh.name.StartsWith("NativeFilter"))
            && menu.NativeBackground.GetComponentsInChildren<Renderer>(false).All(background => art.All(renderer =>
                renderer.sortingLayerID == background.sortingLayerID && renderer.sortingOrder > background.sortingOrder)),
            "Native scrollbar meshes render above the Settings background rather than inheriting a hidden source layer");
        foreach (var renderer in art)
        {
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            Check(properties.Color.Equals(new Color(0.7f, 0.8f, 1f, 1f)), "Native scrollbar shader properties survive copying");
        }
        float dropdownHeight = (float)Read("m_settingsScrollStep") / 1.5f;
        Check(Thumb().width >= dropdownHeight,
            "Settings provides a readable grip at least one dropdown row high, independent of content length");
        Check(Math.Abs(Thumb().height / Thumb().width - 0.3f / 0.18f) < 0.001f,
            "The Hearthstone grip retains its native aspect ratio instead of stretching with the content");
        Check(Track().xMin >= viewport.xMin && Track().xMax <= viewport.xMax
            && Track().yMin >= viewport.yMin && Track().yMax <= viewport.yMax,
            "The scrollbar stays within the existing panel surface");
        Check(bar.GetComponentsInChildren<Collider>(true).Length == 0
            && bar.GetComponentsInChildren<ScrollBarThumb>(true).Length == 0,
            "Scrollbar artwork must not copy the source controller's input callbacks");
        Check(menu.GetComponentsInChildren<UberText>(true).All(text => text.Text != "Scroll for more settings"),
            "The native scrollbar needs no extra scrolling caption");
        foreach (MeshFilter filter in bar.GetComponentsInChildren<MeshFilter>(true))
        {
            var uv = new List<Vector4>(); filter.sharedMesh.GetUVs(0, uv);
            Check(uv.Count == filter.sharedMesh.vertexCount && uv.All(point => point.x >= .1999f && point.x <= .3001f
                && point.y >= .2999f && point.y <= .7001f), "Resizing the rail preserves the original atlas region");
        }
        float height = menu.BodyHeight, width = menu.BodyWidth;
        Frame(new Vector3(Track().center.x, Track().yMin + (Thumb().yMin - Track().yMin) * 0.25f, 0f), down: true, held: true);
        Check(Scroll() > 0f && (bool)Read("m_settingsScrollbarDragging"),
            "Pressing the track immediately scrolls and captures the drag: scroll=" + Scroll() + ", range=" + range
            + ", captured=" + Read("m_settingsScrollbarDragging") + ", hit=" + Track().Contains(Input.mousePosition)
            + ", down=" + Input.LeftPressed + ", open=" + menu.GetComponentsInChildren<DropdownControl>(true).Count(control => control.isMenuShown()));
        Frame(new Vector3(viewport.xMax + 200f, viewport.yMin - 200f, 0f), held: true);
        Check(Near(Scroll(), range) && Near(Thumb().yMin, Track().yMin), "Dragging below and outside the track clamps to the bottom");
        Input.LeftHeld = false; Input.LeftReleased = true;
        blocker.Invoke(menu, new object[] { new UIEvent() });
        Check(menu.IsShown() && !(bool)Read("m_settingsScrollbarDragging"), "Releasing a scrollbar drag outside cannot dismiss Settings");
        Frame(viewport.center);
        Input.mouseScrollDelta = new Vector2(0f, 100f); update.Invoke(menu, null); Input.mouseScrollDelta = Vector2.zero;
        Frame(Track().center, down: true, held: true);
        Frame(Input.mousePosition, up: true);
        Frame(viewport.center);
        Rect thumb = Thumb();
        Vector3 grab = new Vector3(thumb.center.x, thumb.yMax - thumb.height * 0.2f, 0f);
        Frame(grab, down: true, held: true);
        float travel = Track().height - thumb.height;
        float centeredScroll = (Track().yMax - grab.y - thumb.height * 0.5f) / travel * range;
        Check(Near(Thumb().center.y, grab.y) && Math.Abs(Scroll() - centeredScroll) < 0.1f,
            "Pressing an off-center point on the Settings grip centres it on the pointer like pressing the native track");
        Frame(new Vector3(viewport.xMax + 100f, grab.y - travel * 0.25f, 0f), held: true);
        Check(Math.Abs(Scroll() - (centeredScroll + range * 0.25f)) < 0.1f && Near(Thumb().center.y, Input.mousePosition.y),
            "Dragging the Settings grip keeps its centre at the pointer, including outside the track");
        Frame(Input.mousePosition, up: true);
        blocker.Invoke(menu, new object[] { new UIEvent() });
        Check(menu.IsShown() && Near(menu.BodyHeight, height) && Near(menu.BodyWidth, width),
            "Thumb release preserves the modal menu and its original frame dimensions");
        Frame(viewport.center);
        Input.mouseScrollDelta = new Vector2(0f, 100f); update.Invoke(menu, null); Input.mouseScrollDelta = Vector2.zero;
        var dropdown = menu.GetComponentsInChildren<DropdownControl>(true).First(control => control.Items[0] is DragQualityMode);
        dropdown.onUserPressedButton();
        Frame(Track().center, down: true, held: true);
        Check(Near(Scroll(), 0f) && !(bool)Read("m_settingsScrollbarDragging"), "An expanded native dropdown keeps its own input priority");
        blocker.Invoke(menu, new object[] { new UIEvent() });
        Check(menu.IsShown() && !dropdown.isMenuShown(), "Clicking the scrollbar while a dropdown is open first closes that dropdown");
        Frame(viewport.center);
        CheckSettingsScrollbarAlignment(menu);
    }

    private static void CheckSettingsScrollbarAlignment(ModsSettingsMenu menu)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var resources = Resources.Objects;
        var source = MakeSettingsScrollbarTemplate();
        var layout = typeof(ModsSettingsMenu).GetMethod("LayoutSettingsScrollbar", flags);
        var destroy = typeof(ModsSettingsMenu).GetMethod("DestroySettingsScrollbar", flags);
        var viewportField = typeof(ModsSettingsMenu).GetField("m_settingsViewport", flags);
        Rect viewport = (Rect)viewportField.GetValue(menu);
        try
        {
            var rail = source.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "NativeFilterRail");
            var grip = source.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "NativeFilterGrip");
            // Native input and art origins need not agree, and the grip's
            // separate shadow enlarges its aggregate bounds to the right.
            rail.transform.localPosition += new Vector3(.025f, 0, 0);
            grip.transform.localPosition += new Vector3(-.015f, 0, 0);
            var shadow = Child(grip.transform.parent, "NativeGripShadow");
            shadow.transform.localPosition = new Vector3(.08f, 0, 0);
            shadow.AddComponent<MeshFilter>().sharedMesh = grip.GetComponent<MeshFilter>().sharedMesh;
            shadow.AddComponent<MeshRenderer>().sharedMaterial = grip.sharedMaterial;
            var frame = Child(source.transform, "DeckTrayArea");
            frame.transform.localPosition = new Vector3(2.95f, 0, 0);
            frame.transform.localScale = new Vector3(1, 1, 1.2f);
            frame.AddComponent<MeshFilter>().sharedMesh = rail.GetComponent<MeshFilter>().sharedMesh;
            frame.AddComponent<MeshRenderer>().sharedMaterial = rail.sharedMaterial;
            destroy.Invoke(menu, null);
            foreach (bool shortTrack in new[] { false, true })
            {
                viewportField.SetValue(menu, shortTrack ? new Rect(viewport.xMin, viewport.yMin, viewport.width, 50f) : viewport);
                layout.Invoke(menu, null);
                var bar = (GameObject)typeof(ModsSettingsMenu).GetField("m_settingsScrollbar", flags).GetValue(menu);
                var copies = bar.GetComponentsInChildren<MeshRenderer>(true);
                Rect Rail(string name) => ComboSceneGeometry.ProjectBounds(copies.Single(r => r.gameObject.name == name).bounds, CameraUtils.Camera);
                Rect track = (Rect)typeof(ModsSettingsMenu).GetField("m_settingsTrackRect", flags).GetValue(menu);
                Check(Math.Abs(Rail("NativeFilterRail").center.x - track.center.x) < .01f
                    && Math.Abs(Rail("NativeFilterGrip").center.x - track.center.x) < .01f,
                    "Settings rail and visible grip share their centre despite offset hit bones and shadows, short track=" + shortTrack);
                Check(Rail("NativeGripShadow").center.x > Rail("NativeFilterGrip").center.x,
                    "Aligning Settings artwork preserves the grip shadow's native offset");
                layout.Invoke(menu, null);
                Check(Math.Abs(Rail("NativeFilterRail").center.x - Rail("NativeFilterGrip").center.x) < .01f,
                    "Repeated Settings layout cannot accumulate rail/grip alignment drift");
            }
        }
        finally
        {
            Resources.Objects = resources;
            viewportField.SetValue(menu, viewport);
            destroy.Invoke(menu, null);
            UnityEngine.Object.Destroy(source.gameObject);
            layout.Invoke(menu, null);
        }
    }
}
