using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using PegasusShared;

internal static partial class Program
{
    private static void CheckFilterModes()
    {
        CollectionQualitySettings.RememberFilterModes = false;
        FilterModes.StartSession();
        foreach (FilterTrayKind kind in FilterModes.Kinds)
        {
            Check(FilterModes.Get(kind) == FilterMode.Single && FilterModes.Default(kind) == FilterMode.Single, "Every filter starts in Single: " + kind);
            foreach (FilterMode mode in Enum.GetValues(typeof(FilterMode)))
            {
                FilterModes.Set(kind, mode);
                var selection = new FilterSelection<int>(kind);
                Check(selection.Matches(_ => false), "Empty selections never filter, including AND");
                selection.Set(1, true);
                selection.Set(2, true);
                Check(selection.Matches(value => value == 1) == (mode == FilterMode.Or || mode == FilterMode.OrNot || mode == FilterMode.Not), "First-only match: " + kind + "/" + mode);
                Check(selection.Matches(value => value == 2) == (mode == FilterMode.Single || mode == FilterMode.Or || mode == FilterMode.OrNot), "Last-only match: " + kind + "/" + mode);
                Check(selection.Matches(_ => true) == !FilterModes.IsNegative(mode) && selection.Matches(_ => false) == FilterModes.IsNegative(mode), "All/none truth table: " + kind + "/" + mode);
                FilterModes.Set(kind, FilterMode.Single);
                Check(selection.Count == 1 && selection.Contains(2), "Switching to Single retains the latest choice");
                selection.Set(2, false);
                Check(selection.Count == 0 && selection.Matches(_ => false), "Single can be deselected");
                FilterModes.Set(kind, mode);
            }
        }
        FilterModes.StartSession();
        Check(FilterModes.Kinds.All(kind => FilterModes.Get(kind) == FilterModes.Default(kind)), "A new session restores defaults without persistence");
        CheckReversibleSingle();
        CheckExtraFilterModes();
        CheckKeywordModes();
        CheckExpansionModes();
        ExtraFilterState.ClearAll();
        KeywordFilterState.Clear();
        ExpansionFilterState.Clear();
        FilterModes.StartSession();
    }

    private static void CheckReversibleSingle()
    {
        foreach (FilterTrayKind kind in FilterModes.Kinds)
        {
            FilterModes.Set(kind, FilterMode.Or);
            var selection = new FilterSelection<int>(kind);
            selection.Set(1, true); selection.Set(2, true);
            FilterModes.Set(kind, FilterMode.Single);
            Check(selection.Count == 1 && selection.Values.SequenceEqual(new[] { 2 }) && !selection.Contains(1)
                && selection.IsCurrent(2) && !selection.IsCurrent(1)
                && selection.Matches(value => value == 2) && !selection.Matches(value => value == 1),
                "Single immediately exposes and matches only the latest value: " + kind);
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.Count == 2 && selection.Matches(value => value == 1),
                "Passing through Single restores every suspended selection: " + kind);
            FilterModes.Set(kind, FilterMode.Single);
            selection.Set(2, true);
            selection.Set(1, false);
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.Count == 2, "No-op selection calls do not discard suspended choices: " + kind);
            FilterModes.Set(kind, FilterMode.Single);
            var snapshot = selection.Capture();
            var restored = new FilterSelection<int>(kind);
            restored.Restore(snapshot);
            Check(restored.Count == 1 && restored.Contains(2), "Snapshots preserve the current Single view: " + kind);
            FilterModes.Set(kind, FilterMode.Or);
            Check(restored.Count == 2, "Snapshots also preserve suspended selections: " + kind);
            FilterModes.Set(kind, FilterMode.Single);
            selection.Remap(new Dictionary<int, int> { [1] = 11, [2] = 22 });
            Check(selection.Values.SequenceEqual(new[] { 22 }), "Pruning/remapping keeps Single exclusive: " + kind);
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.Values.SequenceEqual(new[] { 11, 22 }), "Remapping retains suspended selections in order: " + kind);
            FilterModes.Set(kind, FilterMode.Single);
            selection.Set(3, true);
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.Values.SequenceEqual(new[] { 3 }), "An actual Single edit commits the new selection: " + kind);
            selection.Set(4, true);
            FilterModes.Set(kind, FilterMode.Single);
            selection.Toggle(4);
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.Count == 0, "Deselecting in Single cannot resurrect suspended filters: " + kind);
            selection.Set(1, true); selection.Set(2, true);
            FilterModes.Set(kind, FilterMode.Single);
            selection.Clear();
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.Count == 0, "Clear All also clears the suspended selection: " + kind);
            selection.Set(1, true); selection.Set(2, true); selection.Set(3, true);
            FilterModes.Set(kind, FilterMode.Single);
            selection.Remap(new Dictionary<int, int> { [1] = 11, [2] = 22, [3] = 11 });
            Check(selection.Values.SequenceEqual(new[] { 11 }), "Alias deduplication must keep the latest Single choice: " + kind);
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.Values.SequenceEqual(new[] { 22, 11 }), "Alias deduplication retains the latest occurrence in order: " + kind);
        }
        FilterModes.StartSession();
    }

    private static void CheckKeywordModes()
    {
        CollectionManager.Get().Display.m_setFilterTray = null;
        CollectionManager.Get().Cards.Clear();
        GameDbf.KeywordText.Records.Clear();
        GameStrings.Names.Clear();
        KeywordFilterState.Clear();
        AddKeyword(GAME_TAG.TAUNT, "Taunt");
        AddKeyword(GAME_TAG.PREPARE, "Prepare");
        var first = Card("first-keyword", GAME_TAG.TAUNT);
        var second = Card("second-keyword");
        second.Definition.References.Add(GAME_TAG.PREPARE);
        var both = Card("both-keywords", GAME_TAG.TAUNT, GAME_TAG.PREPARE);
        CollectionManager.Get().Cards.AddRange(new[] { first, second, both });
        KeywordCatalog.Invalidate();
        KeywordCatalog.GetEntries();
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        KeywordFilterState.SetSelected(GAME_TAG.TAUNT, true);
        KeywordFilterState.SetSelected(GAME_TAG.PREPARE, true);
        Check(KeywordFilterState.Matches(first) && KeywordFilterState.Matches(second), "Keyword OR accepts either a direct marker or a reference");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        Check(!KeywordFilterState.Matches(first) && !KeywordFilterState.Matches(second) && KeywordFilterState.Matches(both), "Keyword AND requires both families");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Single);
        Check(!KeywordFilterState.Matches(first) && KeywordFilterState.Matches(second)
            && KeywordFilterState.SelectedTags.SequenceEqual(new[] { GAME_TAG.PREPARE }), "Keyword Single keeps the last family");
        KeywordFilterState.SetSelected(GAME_TAG.TAUNT, true);
        Check(KeywordFilterState.SelectedTags.SequenceEqual(new[] { GAME_TAG.TAUNT }), "Selecting another keyword in Single replaces the old one");
    }

    private static void CheckExtraFilterModes()
    {
        ExtraFilterState.ClearAll();
        var minion = Card("beast");
        minion.Definition.Races.Add(TAG_RACE.BEAST);
        minion.Rarity = TAG_RARITY.COMMON;
        var dual = Card("dual-race");
        dual.Definition.Races.AddRange(new[] { TAG_RACE.BEAST, TAG_RACE.DRAGON });
        var all = Card("all-races");
        all.Definition.Races.Add(TAG_RACE.ALL);
        FilterModes.Set(FilterTrayKind.Race, FilterMode.And);
        ExtraFilterState.SetRace(TAG_RACE.BEAST, true);
        ExtraFilterState.SetRace(TAG_RACE.DRAGON, true);
        Check(!ExtraFilterState.Matches(minion) && ExtraFilterState.Matches(dual) && ExtraFilterState.Matches(all), "Explicit race AND supports dual and All minion types");
        FilterModes.Set(FilterTrayKind.Race, FilterMode.Or);
        Check(ExtraFilterState.Matches(minion) && ExtraFilterState.Matches(dual) && ExtraFilterState.Matches(all), "Race OR accepts any selected race");
        ExtraFilterState.SetRace(TAG_RACE.ALL, true);
        Check(ExtraFilterState.Matches(minion), "Selecting All in OR does not reject an ordinary selected race");
        FilterModes.Set(FilterTrayKind.Race, FilterMode.And);
        Check(!ExtraFilterState.Matches(dual) && ExtraFilterState.Matches(all), "All as a required race still requires an All-type card");
        ExtraFilterState.ClearRaces();

        FilterModes.Set(FilterTrayKind.Type, FilterMode.Or);
        ExtraFilterState.SetType(TAG_CARDTYPE.MINION, true);
        ExtraFilterState.SetType(TAG_CARDTYPE.SPELL, true);
        Check(ExtraFilterState.Matches(minion), "Explicit card type OR accepts a minion");
        FilterModes.Set(FilterTrayKind.Type, FilterMode.And);
        Check(!ExtraFilterState.Matches(minion), "Distinct card types cannot satisfy AND");
        FilterModes.Set(FilterTrayKind.Type, FilterMode.Single);
        Check(!ExtraFilterState.HasType(TAG_CARDTYPE.MINION) && ExtraFilterState.HasType(TAG_CARDTYPE.SPELL), "Type Single retains the latest type");
        ExtraFilterState.ClearTypes();

        FilterModes.Set(FilterTrayKind.School, FilterMode.Or);
        minion.Definition.School = TAG_SPELL_SCHOOL.FIRE;
        ExtraFilterState.SetSchool(TAG_SPELL_SCHOOL.FIRE, true);
        ExtraFilterState.SetSchool(TAG_SPELL_SCHOOL.FROST, true);
        Check(ExtraFilterState.Matches(minion), "Explicit school OR matches either school");
        FilterModes.Set(FilterTrayKind.School, FilterMode.And);
        Check(!ExtraFilterState.Matches(minion), "A single-school card cannot satisfy two schools with AND");
        ExtraFilterState.ClearSchools();

        FilterModes.Set(FilterTrayKind.Rarity, FilterMode.Or);
        minion.Rarity = TAG_RARITY.FREE;
        ExtraFilterState.SetRarity(TAG_RARITY.COMMON, true);
        Check(ExtraFilterState.Matches(minion), "Basic cards continue to count as Common");
        ExtraFilterState.SetRarity(TAG_RARITY.RARE, true);
        Check(ExtraFilterState.Matches(minion), "Explicit rarity OR preserves the Common alias");
        FilterModes.Set(FilterTrayKind.Rarity, FilterMode.And);
        Check(!ExtraFilterState.Matches(minion), "Rarity AND requires all selected rarities");
        ExtraFilterState.ClearAll();

        FilterModes.Set(FilterTrayKind.Type, FilterMode.Or);
        FilterModes.Set(FilterTrayKind.Race, FilterMode.Or);
        ExtraFilterState.SetType(TAG_CARDTYPE.SPELL, true);
        ExtraFilterState.SetRace(TAG_RACE.BEAST, true);
        Check(!ExtraFilterState.Matches(minion), "Different categories remain combined with AND even in OR mode");
        ExtraFilterState.ClearAll();
    }

    private static void CheckExpansionModes()
    {
        ExpansionFilterState.Clear();
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Or);
        var core = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
        var jail = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.JAIL } };
        var standard = new SetFilterItem { IsAllStandard = true, FormatType = FormatType.FT_STANDARD };
        var first = new CollectibleCard { CardId = "core", Set = TAG_CARD_SET.CORE };
        var second = new CollectibleCard { CardId = "dbf-42", Set = TAG_CARD_SET.JAIL };
        ExpansionFilterState.SetSelected(core, true);
        ExpansionFilterState.SetSelected(jail, true);
        Check(ExpansionFilterState.Matches(first) && ExpansionFilterState.Matches(second), "Expansion OR includes both sets");
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.And);
        Check(!ExpansionFilterState.Matches(first) && !ExpansionFilterState.Matches(second), "Disjoint expansions in AND produce an empty result");
        ExpansionFilterState.SetSelected(jail, false);
        ExpansionFilterState.SetSelected(standard, true);
        Check(ExpansionFilterState.Matches(first) && !ExpansionFilterState.Matches(second), "Overlapping native groups form a real intersection");
        ExpansionFilterState.Clear();
        var specific = new SetFilterItem { SpecificCards = new List<int> { 42 } };
        ExpansionFilterState.SetSelected(specific, true);
        ExpansionFilterState.SetSelected(jail, true);
        Check(ExpansionFilterState.Matches(second) && !ExpansionFilterState.Matches(new CollectibleCard { Set = TAG_CARD_SET.JAIL, CardId = "other" }), "Specific-card groups intersect correctly with set groups");
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Or);
        Check(ExpansionFilterState.Matches(new CollectibleCard { Set = TAG_CARD_SET.JAIL, CardId = "other" }), "Specific-card and set groups can also be combined with OR");
        ExpansionFilterState.SetSelected(specific, false);
        ExpansionFilterState.SetSelected(jail, false);
        Check(ExpansionFilterState.IsActive && ExpansionFilterState.Matches(first), "Deselecting every expansion restores the broad native pool");

        // Catalog availability follows the combined pool, including an empty intersection.
        CollectionManager.Get().Cards.Clear();
        GameDbf.KeywordText.Records.Clear();
        GameStrings.Names.Clear();
        AddKeyword(GAME_TAG.TAUNT, "Taunt");
        first.Definition.Tags.Add(GAME_TAG.TAUNT);
        CollectionManager.Get().Cards.AddRange(new[] { first, second });
        ExpansionFilterState.SetSelected(core, true);
        ExpansionFilterState.SetSelected(jail, true);
        KeywordCatalog.Invalidate();
        Check(KeywordCatalog.GetEntries().Count == 1, "Keyword availability sees all OR-selected expansions");
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.And);
        KeywordCatalog.Invalidate();
        Check(KeywordCatalog.GetEntries().Count == 0, "An empty expansion intersection offers no keywords");
    }
}
