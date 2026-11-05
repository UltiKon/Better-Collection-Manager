using System;
using System.Collections.Generic;
using BetterCollectionManager;
using UnityEngine;

namespace BetterCollectionManager
{
    // Catalog construction is the expensive external dependency here; the
    // production filter-change handler and selection/pruning logic run intact.
    internal static class KeywordCatalog
    {
        internal readonly struct Entry
        {
            public readonly GAME_TAG Tag;
            public readonly string Name;
            public readonly KeywordGroup Group;
            public readonly IReadOnlyList<GAME_TAG> Tags;
            public readonly bool HasCards, HasReferencedCards;
            public readonly bool UsesReferencesAsHas;
            public Entry(GAME_TAG tag, IReadOnlyList<GAME_TAG> tags, bool hasCards, bool hasReferencedCards, string name = "Keyword")
            { Tag = tag; Tags = tags; HasCards = hasCards; HasReferencedCards = hasReferencedCards; Name = name; Group = KeywordGroups.GetGroup(tags); UsesReferencesAsHas = false; }
        }
        public static int Builds, Invalidations;
        public static void EnsureFields() { }
        public static IReadOnlyList<Entry> Entries = Array.Empty<Entry>();
        public static void Invalidate() => Invalidations++;
        public static void InvalidateScope() => Invalidations++;
        public static IReadOnlyList<Entry> GetEntries() { Builds++; return Entries; }
        public static bool MatchesKeyword(EntityDef def, GAME_TAG tag, bool referenced) => true;
        public static string FormatSelectedDebug(IEnumerable<GAME_TAG> tags) => "fixture keywords";
    }
}

internal static partial class Program
{
    private static void CheckExpansionRefreshCost(KeywordSetTray.GridFixture fixture)
    {
        SetFilterTray native = fixture.Native;
        SetFilterItem current = native.m_selected;
        SetFilterItem target = native.m_items[5];
        SetFilterItem another = native.m_items[6];
        var savedCallback = target.Callback;
        var otherCallback = another.Callback;
        int nativeFilters = 0;
        SetFilterItem pendingFilter = current, visibleFilter = current;
        Action previousRefresh = KeywordFilterApplier.OnRefresh;
        KeywordFilterApplier.OnRefresh = () => visibleFilter = pendingFilter;
        SetFilterItem[] oldRows = new SetFilterItem[fixture.Items.Count];
        for (int i = 0; i < oldRows.Length; i++) oldRows[i] = fixture.Items[i];
        int rebuilds = KeywordSetTray.FixtureRebuilds;
        float scroll = fixture.Scroller.ScrollValue = 0.8f;
        int builds = KeywordCatalog.Builds, refreshes = KeywordFilterApplier.Refreshes;
        void Apply(List<TAG_CARD_SET> sets, List<int> cards, PegasusShared.FormatType format, SetFilterItem source, bool transition)
        {
            Check(!transition && CollectionFilterMenu.IsSelectingExpansion && native.m_selected == source,
                "Expansion filtering must see the new selected source before executing, without a page-transition animation");
            // transitionPage=false updates the filter data, not the displayed
            // page. Only the common page-refresh path publishes those results.
            pendingFilter = source;
            nativeFilters++;
            // The native display callback does not select the tray's row.
            KeywordFilterUi.OnSetFilterChanged();
        }
        target.Callback = Apply;
        another.Callback = Apply;
        KeywordFilterState.Clear();
        CollectionFilterMenu.SelectExpansion(target);
        Check(nativeFilters == 1 && native.m_selected == target && !CollectionFilterMenu.IsSelectingExpansion
            && KeywordCatalog.Builds == builds && KeywordFilterApplier.Refreshes == refreshes + 1
            && visibleFilter == target,
            "A normal expansion change must apply its data and refresh the visible cards exactly once through the common page-refresh path");
        Check(KeywordSetTray.FixtureRebuilds == rebuilds && Near(fixture.Scroller.ScrollValue, scroll)
            && fixture.Items.Count == oldRows.Length,
            "Expansion selection must not rebuild the menu or reset the scroll position");
        for (int i = 0; i < oldRows.Length; i++)
            Check(fixture.Items[i] == oldRows[i], "Expansion selection must preserve every button, text surface and hit target");
        Check(fixture.Items[5].Selected && fixture.Items[5].m_selectedGlow.activeSelf
            && !fixture.Items[1].Selected && !fixture.Items[1].m_selectedGlow.activeSelf,
            "Changing away from Wild must transfer both selected state and actual glow visibility to the chosen row");
        CollectionFilterMenu.SelectExpansion(target);
        Check(nativeFilters == 1 && KeywordFilterApplier.Refreshes == refreshes + 1,
            "Choosing the already selected expansion must not refilter or refresh the collection again");

        GAME_TAG tag = (GAME_TAG)100;
        KeywordCatalog.Entries = new[] { new KeywordCatalog.Entry(tag, new[] { tag }, true, true) };
        KeywordFilterState.SetSelected(tag, true);
        CollectionFilterMenu.SelectExpansion(another);
        Check(nativeFilters == 2 && KeywordCatalog.Builds == builds + 1
            && KeywordFilterState.IsSelected(tag) && KeywordFilterApplier.Refreshes == refreshes + 2 && visibleFilter == another,
            "An active keyword must be checked against the new expansion and the new results must become visible after exactly one refresh");
        KeywordCatalog.Entries = Array.Empty<KeywordCatalog.Entry>();
        CollectionFilterMenu.SelectExpansion(target);
        Check(nativeFilters == 3 && !KeywordFilterState.IsActive && KeywordCatalog.Builds == builds + 2
            && KeywordFilterApplier.Refreshes == refreshes + 3 && visibleFilter == target,
            "Unavailable active keywords must be removed before the single page refresh publishes the new expansion results");

        target.Callback = (sets, cards, format, source, transition) => { throw new InvalidOperationException("native selection failure"); };
        CollectionFilterMenu.SelectExpansion(another);
        KeywordFilterState.SetSelected(tag, true);
        CollectionFilterMenu.SelectExpansion(target);
        Check(!CollectionFilterMenu.IsSelectingExpansion && native.m_selected == another
            && fixture.Items[6].Selected && !fixture.Items[5].Selected && KeywordFilterState.IsSelected(tag)
            && KeywordFilterApplier.Refreshes == refreshes + 4 && visibleFilter == another,
            "Failed native selection must release the guard and restore both the last valid row and its previous keyword filters");
        target.Callback = savedCallback;
        another.Callback = otherCallback;
        KeywordFilterApplier.OnRefresh = previousRefresh;
        // Restore the expansion used by the enclosing reopening check.
        native.m_selected = current;
        KeywordSetTray.RefreshExpansionSelection();
        KeywordFilterState.Clear();
        KeywordCatalog.Entries = Array.Empty<KeywordCatalog.Entry>();
        builds = KeywordCatalog.Builds;
        refreshes = KeywordFilterApplier.Refreshes;
        rebuilds = KeywordSetTray.FixtureRebuilds;
        KeywordFilterUi.OnSetFilterChanged();
        Check(KeywordCatalog.Builds == builds + 1 && KeywordFilterApplier.Refreshes == refreshes + 1
            && KeywordSetTray.FixtureRebuilds == rebuilds + 1,
            "Filter changes outside the expansion bridge must retain their usual catalog, menu and page refresh path");
    }
}
