using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

// Localization boundary used by the shared mechanic-family helper.
public static class GameStrings
{
    public static readonly System.Collections.Generic.Dictionary<string,string> Strings = new System.Collections.Generic.Dictionary<string,string>();
    public static string Get(string key) => Strings.TryGetValue(key, out string text) ? text : key;
    public static readonly System.Collections.Generic.Dictionary<GAME_TAG, string> Names = new System.Collections.Generic.Dictionary<GAME_TAG, string>();
    public static bool HasKeywordName(GAME_TAG tag) => Names.ContainsKey(tag);
    public static string GetKeywordName(GAME_TAG tag) => Names.TryGetValue(tag, out string name) ? name : null;
}

internal static partial class Program
{
    private static void CheckKeywordMenus(KeywordSetTray.GridFixture fixture)
    {
        GAME_TAG taunt = (GAME_TAG)190, adapt = (GAME_TAG)546, kabal = (GAME_TAG)1637;
        GAME_TAG battlecry = (GAME_TAG)218;
        var entries = new[]
        {
            new KeywordCatalog.Entry(taunt, new[] { taunt }, true, true, "Taunt"),
            new KeywordCatalog.Entry(adapt, new[] { adapt }, true, false, "Adapt"),
            new KeywordCatalog.Entry(kabal, new[] { kabal }, false, true, "Frenzy"),
            new KeywordCatalog.Entry(battlecry, new[] { battlecry }, true, false, "Battlecry"),
        };
        var savedEntries = KeywordCatalog.Entries;
        fixture.UseKeywordMenus = true;
        KeywordCatalog.Entries = entries;
        KeywordFilterState.Clear();
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Keywords);
        MelonLoader.MelonCoroutines.Complete();
        Check(fixture.Items.Select(i => i.Text).SequenceEqual(new[]
            { "Evergreen", "Battlecry", "Taunt", "Expansion-Keywords", "Adapt", "Frenzy" }),
            "Keywords opens one list with ordered headings and alphabetically sorted, unique keyword rows");
        Check(fixture.Items.Where(i => i.IsHeader).Select(i => i.Text).SequenceEqual(new[]
            { "Evergreen", "Expansion-Keywords" }),
            "The two keyword categories are headings rather than submenu navigation rows");
        var tauntRow = fixture.Items.Single(i => i.Text == "Taunt");
        int refreshes = KeywordFilterApplier.Refreshes;
        tauntRow.GetComponent<PegUIElement>().Release();
        Check(KeywordFilterState.IsSelected(taunt) && tauntRow.Selected
            && KeywordFilterApplier.Refreshes == refreshes + 1
            && KeywordSetTray.ActiveKind == FilterTrayKind.Keywords,
            "Selecting a keyword applies one filter and updates its chrome in the same list");
        fixture.Items.Single(i => i.Text == "Adapt").GetComponent<PegUIElement>().Release();
        fixture.Items.Single(i => i.Text == "Frenzy").GetComponent<PegUIElement>().Release();
        Check(KeywordFilterState.IsSelected(adapt) && KeywordFilterState.IsSelected(taunt)
            && KeywordFilterState.IsSelected(kabal) && KeywordFilterState.SelectedTags.Count() == 3,
            "Direct-only, reference-only and mixed keywords select together across both sections");

        fixture.Scroller.ScrollValue = 0.4f;
        KeywordSetTray.HideImmediately();
        CollectionFilterMenu.Reopen();
        MelonLoader.MelonCoroutines.Complete();
        Check(KeywordSetTray.ActiveKind == FilterTrayKind.Keywords
            && Near(fixture.Scroller.ScrollValue, 0.4f)
            && fixture.Items.Where(i => !i.IsHeader && i.Text != "Battlecry").All(i => i.Selected),
            "Reopening restores the common list, its scroll position and selections in all sections");
        tauntRow = fixture.Items.Single(i => i.Text == "Taunt");
        tauntRow.GetComponent<PegUIElement>().Release();
        Check(!KeywordFilterState.IsSelected(taunt) && !tauntRow.Selected
            && KeywordFilterState.IsSelected(adapt) && KeywordFilterState.IsSelected(kabal),
            "Deselecting a merged row clears its selection without affecting the other sections");
        CollectionFilterMenu.GoBack();
        Check(KeywordSetTray.ActiveKind == FilterTrayKind.Menu,
            "One Back returns directly from the keyword list to the filter root");

        KeywordCatalog.Entries = new[] { entries[1] };
        CollectionFilterMenu.ShowFilter(FilterTrayKind.Keywords);
        Check(fixture.Items.Count == 2 && fixture.Items[0].IsHeader
            && fixture.Items[0].Text == "Expansion-Keywords" && fixture.Items[1].Text == "Adapt"
            && fixture.Items[1].Selected && !KeywordFilterState.IsSelected(kabal),
            "Empty sections disappear and unavailable selections are pruned across the whole catalog");
        KeywordCatalog.Entries = Array.Empty<KeywordCatalog.Entry>();
        KeywordSetTray.RebuildRows();
        Check(fixture.Items.Count == 0 && !KeywordFilterState.IsActive,
            "An empty catalog leaves no orphaned headings or stale selections");
        KeywordCatalog.Entries = entries;
        fixture.UseModeRows = true;
        KeywordSetTray.RebuildRows();
        var mode = fixture.Items.Single(i => i.gameObject.name == "BCM_FilterMode");
        Check(mode.Text == "Mode: AND" && fixture.Items[0] == mode,
            "Keywords must show its explicitly selected mode at the top of the menu");
        fixture.Items.Single(i => i.Text == "Taunt").GetComponent<PegUIElement>().Release();
        fixture.Items.Single(i => i.Text == "Adapt").GetComponent<PegUIElement>().Release();
        mode.GetComponent<PegUIElement>().Release();
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Or
            && fixture.Items.Count(i => i.Selected) == 2,
            "Changing Keywords to OR must retain both selected families");
        fixture.Items[0].GetComponent<PegUIElement>().Release();
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Single
            && KeywordFilterState.IsSelected(adapt) && !KeywordFilterState.IsSelected(taunt)
            && fixture.Items.Count(i => i.Selected) == 1,
            "Changing Keywords to Single retains the latest family");
        var not = fixture.Items.Single(i => i.gameObject.name == "BCM_FilterNot");
        not.GetComponent<PegUIElement>().Release();
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Not && not.Text == "NOT: On" && not.Selected
            && fixture.Items.Any(i => i.Text == "NOT · Adapt"),
            "The separate NOT button turns Single into a visible unary exclusion");
        int singleRefreshes = KeywordFilterApplier.Refreshes;
        fixture.Items[0].GetComponent<PegUIElement>().Release();
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.AndNot && KeywordFilterState.IsSelected(taunt)
            && KeywordFilterState.IsSelected(adapt) && fixture.Items.Count(i => i.Selected) == 3
            && KeywordFilterApplier.Refreshes == singleRefreshes + 1,
            "Cycling from NOT to AND NOT restores all choices as exclusions with one refresh");
        not.GetComponent<PegUIElement>().Release();
        fixture.Items[0].GetComponent<PegUIElement>().Release();
        fixture.Items[0].GetComponent<PegUIElement>().Release();
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Single && fixture.Items.Count(i => i.Selected) == 1
            && KeywordFilterApplier.Refreshes == singleRefreshes + 4,
            "Repeated category mode cycling must refresh and retain an exclusive Single view");
        fixture.Items.Single(i => i.Text == "Taunt").GetComponent<PegUIElement>().Release();
        Check(KeywordFilterState.IsSelected(taunt) && !KeywordFilterState.IsSelected(adapt)
            && fixture.Items.Single(i => i.Text == "Taunt").Selected
            && !fixture.Items.Single(i => i.Text == "Adapt").Selected,
            "A Single keyword click must clear the old row's glow without rebuilding the menu");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        fixture.UseModeRows = false;
        KeywordFilterState.Clear();
        CheckPinnedFilterChrome(fixture);
        KeywordCatalog.Entries = savedEntries;
        fixture.UseKeywordMenus = false;
        CollectionFilterMenu.ShowMenu();
    }
}
