using System;
using System.Linq;
using BetterCollectionManager;
using PegasusShared;
using UnityEngine;

internal static partial class Program
{
    private static void CheckAllCategoryRowReuse(KeywordSetTray.GridFixture fixture)
    {
        void ClickFilter(SetFilterItem item)
        {
            int sounds = SoundManager.Get().Played.Count;
            var peg = item.GetComponent<PegUIElement>();
            peg.Send(UIEventType.PRESS);
            peg.Release();
            Check(SoundManager.Get().Played.Count == sounds + 1,
                "Selecting or removing a filter plays once despite native audio before and after callback dispatch");
        }
        var savedKind = KeywordSetTray.ActiveKind;
        int columns = CollectionQualitySettings.FilterMenuColumns;
        CollectionQualitySettings.FilterMenuColumns = 2;
        CollectionQualitySettings.HideEmptyFilters = true;
        foreach (var kind in new[] { FilterTrayKind.Type, FilterTrayKind.Race, FilterTrayKind.School, FilterTrayKind.Rarity,
            FilterTrayKind.Categories })
        {
            CollectionFilterMenu.ShowFilter(kind); MelonLoader.MelonCoroutines.Complete();
            var initial = fixture.Items.ToArray();
            bool IsChoice(SetFilterItem item) => !item.IsHeader && item.gameObject.name != "BCM_ImportCategoryDeck"
                && item.gameObject.name != "BCM_RemoveCategoryDeck" && item.gameObject.name != "BCM_AddCombo" && item.gameObject.name != "BCM_AddPackage" && item.gameObject.name != "BCM_EditPackage";
            var choices = initial.Where(IsChoice).ToArray();
            var pins = fixture.PinnedItems.ToArray();
            var mesh = fixture.Frame.sharedMesh;
            int rows = fixture.Rows, rebuilds = KeywordSetTray.FixtureRebuilds;
            var hidden = choices[1]; bool hide = true;
            // Catalog order and selection behavior run through the real population methods.
            FilterAvailability.ExtraVisible = (category, value) => !hide || category != kind ||
                value != ExtraValue(kind, hidden.Text);
            ClickFilter(choices[0]);
            Check(!fixture.Items.Contains(hidden) && !hidden.gameObject.activeSelf
                && fixture.Items.All(item => initial.Contains(item)) && pins.SequenceEqual(fixture.PinnedItems)
                && fixture.Frame.sharedMesh == mesh && fixture.Rows == rows && fixture.Columns == 2
                && KeywordSetTray.FixtureRebuilds == rebuilds,
                "Every extra category hides options without replacing native rows, pinned controls or layout meshes: " + kind);
            hide = false;
            ClickFilter(choices[0]);
            Check(initial.SequenceEqual(fixture.Items) && KeywordSetTray.FixtureRebuilds == rebuilds,
                "Every extra category restores hidden options from its local row pool: " + kind);
            // A row absent from the first build must be created without replacing its siblings.
            hide = true; KeywordSetTray.RebuildRows();
            var retained = fixture.Items.ToArray(); pins = fixture.PinnedItems.ToArray();
            rebuilds = KeywordSetTray.FixtureRebuilds;
            hide = false;
            ClickFilter(fixture.Items.First(IsChoice));
            Check(fixture.Items.Any(item => item.Text == hidden.Text) && retained.All(item => fixture.Items.Contains(item))
                && pins.SequenceEqual(fixture.PinnedItems) && KeywordSetTray.FixtureRebuilds == rebuilds,
                "An initially unavailable extra filter is created individually: " + kind);
            // Selected options remain visible even when adding that value would otherwise yield no result.
            FilterAvailability.ExtraVisible = (category, value) => false;
            KeywordSetTray.RefreshAfterSelection();
            Check(fixture.Items.Count(IsChoice) == 1 && fixture.Items.Single(IsChoice).Selected,
                "Selected extra filters stay removable when every new candidate is impossible: " + kind);
            if (kind == FilterTrayKind.Categories)
                Check(fixture.PinnedItems.Any(item => item.gameObject.name == "BCM_ShowEditControls"),
                    "Category editing remains available even when all unselected categories have no results");
            FilterAvailability.ExtraVisible = null; ExtraFilterState.ClearAll();
        }
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionQualitySettings.FilterMenuColumns = columns;
        CollectionFilterMenu.ShowFilter(savedKind); MelonLoader.MelonCoroutines.Complete();
    }

    private static int ExtraValue(FilterTrayKind kind, string label)
    {
        switch (kind)
        {
            case FilterTrayKind.Type: return (int)ExtraFilterCatalog.Types.Single(entry => entry.Name == label).Type;
            case FilterTrayKind.Race: return (int)ExtraFilterCatalog.Races.Single(entry => entry.Name == label).Race;
            case FilterTrayKind.School: return (int)ExtraFilterCatalog.Schools.Single(entry => entry.Name == label).School;
            case FilterTrayKind.Categories: return (int)CategoryCatalog.Categories.Single(entry => entry.Name == label).Value;
            default: return (int)ExtraFilterCatalog.Rarities.Single(entry => entry.Name == label).Rarity;
        }
    }

    private static void CheckWideExpansionPoolReuse(KeywordSetTray.GridFixture fixture)
    {
        var savedKind = KeywordSetTray.ActiveKind;
        var native = fixture.Native;
        var original = native.m_selected;
        var callbacks = native.m_items.Select(item => item.Callback).ToArray();
        var behavior = CollectionQualitySettings.FilterModeBehavior;
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Single);
        ExpansionFilterState.Clear();
        CollectionQualitySettings.HideEmptyFilters = true;
        int nativeCalls = 0;
        foreach (var source in native.m_items)
            if (!source.IsHeader) source.Callback = (sets, cards, format, item, transition) => {
                nativeCalls++;
                Check(format == FormatType.FT_WILD && cards == null && !transition
                    && CollectionFilterMenu.IsSelectingExpansion,
                    "Preparing the reusable expansion pool retains native callback arguments and recursion guard");
                KeywordFilterUi.OnSetFilterChanged();
            };
        CollectionFilterMenu.InvalidateNativeExpansionPool();
        Check(CollectionFilterMenu.EnsureWideExpansionPool() && nativeCalls == 1,
            "The first expansion edit initializes the broad native universe once");
        CollectionFilterMenu.ShowExpansions(); MelonLoader.MelonCoroutines.Complete();
        var items = fixture.Items.ToArray(); var pins = fixture.PinnedItems.ToArray();
        var choices = native.m_items.Where(item => !item.IsHeader && item != original).Take(3).ToArray();
        int rebuilds = KeywordSetTray.FixtureRebuilds, refreshes = KeywordFilterApplier.Refreshes;
        bool hide = true;
        FilterAvailability.ExpansionVisible = source => !hide || source != choices[2];
        fixture.Items.Single(item => item.Text == choices[0].Text).GetComponent<PegUIElement>().Release();
        Check(nativeCalls == 1 && KeywordFilterApplier.Refreshes == refreshes + 1
            && native.m_selected == choices[0] && ExpansionFilterState.IsSelected(choices[0]),
            "Warm expansion selection changes the chosen expansion and publishes results once without repeating native pool setup");
        Check(!fixture.Items.Any(item => item.Text == choices[2].Text)
            && fixture.Items.All(item => items.Contains(item)) && pins.SequenceEqual(fixture.PinnedItems)
            && KeywordSetTray.FixtureRebuilds == rebuilds,
            "Expansion availability changes reuse native rows and pinned controls");
        hide = false;
        fixture.Items.Single(item => item.Text == choices[1].Text).GetComponent<PegUIElement>().Release();
        Check(nativeCalls == 1 && items.SequenceEqual(fixture.Items) && native.m_selected == choices[1]
            && KeywordFilterApplier.Refreshes == refreshes + 2 && KeywordSetTray.FixtureRebuilds == rebuilds,
            "A second expansion change restores pooled rows without native setup or a complete menu rebuild");
        var special = choices[2]; var savedSets = special.CardSets;
        special.CardSets = new System.Collections.Generic.List<TAG_CARD_SET> { TAG_CARD_SET.SPECIAL_EXPANSION };
        fixture.Items.Single(item => item.Text == special.Text).GetComponent<PegUIElement>().Release();
        Check(nativeCalls == 2 && ExpansionFilterState.IsSelected(special),
            "Selecting a special expansion outside Wild rebuilds the native pool instead of reusing an incomplete universe");
        KeywordFilterUi.OnSetFilterChanged();
        Check(nativeCalls == 3 && !CollectionFilterMenu.IsSelectingExpansion,
            "A set-filter change outside the bridge invalidates and prepares the native expansion universe again");
        CollectionFilterMenu.ClearExpansions();
        Check(nativeCalls == 4 && ExpansionFilterState.SelectedCount == 0,
            "Clearing a special expansion restores the ordinary broad universe and removes every custom selection");
        CollectionFilterMenu.ClearExpansions();
        Check(nativeCalls == 4, "Repeated Clear All reuses the identical broad expansion universe");
        special.CardSets = savedSets;
        FilterAvailability.ExpansionVisible = null;
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionQualitySettings.FilterModeBehavior = behavior;
        ExpansionFilterState.Clear();
        CollectionFilterMenu.InvalidateNativeExpansionPool();
        for (int i = 0; i < callbacks.Length; i++) native.m_items[i].Callback = callbacks[i];
        native.m_selected = original;
        FilterModes.StartSession();
        CollectionFilterMenu.ShowFilter(savedKind); MelonLoader.MelonCoroutines.Complete();
    }
}
