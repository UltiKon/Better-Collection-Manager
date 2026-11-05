using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckExpressions()
    {
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        var expression = new FilterSelection<string>(FilterTrayKind.Keywords);
        expression.Toggle("Battlecry");
        expression.Toggle("Immune");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        expression.Toggle("Battlecry");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        expression.Toggle("Stealth");
        Check(expression.Count == 4 && expression.Values.Count(v => v == "Battlecry") == 2,
            "A keyword may appear independently in both OR groups");
        Check(expression.IsCurrent("Battlecry") && expression.IsCurrent("Stealth") && !expression.IsCurrent("Immune"),
            "Only the last OR group is current; earlier groups remain selected");
        for (int bits = 0; bits < 8; bits++)
        {
            bool b = (bits & 1) != 0, i = (bits & 2) != 0, s = (bits & 4) != 0;
            bool Matches(string value) => value == "Battlecry" ? b : value == "Immune" ? i : s;
            Check(expression.Matches(Matches) == ((b && i) || (b && s)), "Grouped expression truth table " + bits);
        }
        var snapshot = expression.Capture();
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        Check(expression.Matches(v => v == "Battlecry") == false, "Choosing the next operator never rewrites previous AND terms");
        expression.Restore(snapshot);
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        expression.Toggle("Stealth");
        Check(expression.Count == 3 && !expression.Contains("Stealth"), "Removing a term removes its preceding AND");
        expression.Toggle("Battlecry");
        Check(expression.Count == 2 && expression.IsCurrent("Immune") && expression.Contains("Battlecry"),
            "Removing the last OR group's only term restores the preceding group without removing its duplicate");
        Check(!expression.Matches(v => v == "Battlecry") && expression.Matches(_ => true), "Rollback recovers the original AND result");
        expression.Restore(snapshot);
        expression.Toggle("Immune");
        Check(!expression.Contains("Immune") && expression.Count == 3 && expression.Matches(v => v == "Battlecry"),
            "A selected entry from an earlier yellow group can be removed with its own preceding operator");
        expression.Restore(snapshot);
        expression.Remap(new Dictionary<string, string> { ["Battlecry"] = "B", ["Immune"] = "I", ["Stealth"] = "S" });
        Check(expression.Count == 4 && expression.Matches(v => v == "B" || v == "S"), "Catalog remapping preserves operators and duplicates across groups");
        expression.Clear();
        Check(expression.Count == 0 && expression.Matches(_ => false), "Clear All removes every group and operator");

        foreach (FilterTrayKind kind in FilterModes.Kinds)
        {
            FilterModes.Set(kind, FilterMode.Or);
            var onlyOr = new FilterSelection<string>(kind);
            onlyOr.Toggle("A"); onlyOr.Toggle("B"); onlyOr.Toggle("C");
            Check(onlyOr.Values.All(onlyOr.IsCurrent), "Pure OR selections all retain blue glow: " + kind);
            onlyOr.Toggle("A");
            Check(!onlyOr.Contains("A") && onlyOr.Count == 2 && onlyOr.Values.All(onlyOr.IsCurrent),
                "Clicking an earlier OR selection removes it in Next mode: " + kind);
            if (FilterModes.SupportsAnd(kind))
            {
                FilterModes.Set(kind, FilterMode.And); FilterModes.Set(kind, FilterMode.Or);
                onlyOr.Toggle("B");
                Check(!onlyOr.Contains("B") && onlyOr.Count == 1,
                    "Cycling the next operator does not duplicate a pure OR selection: " + kind);
            }
            foreach (string selected in onlyOr.Values.ToArray()) onlyOr.Toggle(selected);
            Check(onlyOr.Count == 0 && onlyOr.Matches(_ => false), "Pure OR selections can be completely removed: " + kind);
        }
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        expression.Restore(snapshot);
        expression.Toggle("Immune"); expression.Toggle("Stealth");
        Check(expression.Values.All(expression.IsCurrent), "Removing all AND terms restores blue glows for a pure OR expression");
        CheckExpressionGlowGroups();
        CheckNextOperatorEdits();

        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Expansions, FilterTrayKind.Type, FilterTrayKind.School, FilterTrayKind.Rarity })
        {
            FilterModes.Set(kind, FilterMode.Single);
            Check(FilterModes.Next(kind) == FilterMode.Or && FilterModes.Operation(kind) == FilterMode.Single,
                "Categories with one value per card omit AND: " + kind);
        }
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        FilterModes.StartSession();
        CheckEmptyFilterOptions();
    }

    private static void CheckNextOperatorEdits()
    {
        foreach (FilterTrayKind kind in FilterModes.Kinds)
        {
            FilterModes.Set(kind, FilterMode.Or);
            var selection = new FilterSelection<string>(kind);
            Check(FilterModes.Next(kind, selection.Count) == FilterMode.Single, "Next Single is available without selections: " + kind);
            selection.Toggle("A");
            Check(FilterModes.Next(kind, selection.Count) == FilterMode.Single, "Next Single remains available with one selection: " + kind);
            selection.Toggle("B");
            FilterMode next = FilterModes.Next(kind, selection.Count);
            Check(next == (FilterModes.SupportsAnd(kind) ? FilterMode.And : FilterMode.Or), "Connected OR terms skip Single and retain their polarity: " + kind);
            FilterModes.Set(kind, next);
            Check(selection.Count == 2 && selection.Matches(value => value == "A") && selection.Matches(value => value == "B"),
                "Skipping Single changes only the next operator, preserving the connected expression: " + kind);
            for (int i = 0; i < 5; i++)
            {
                next = FilterModes.Next(kind, selection.Count);
                Check(!FilterModes.IsSingle(next), "Next never passes through unary modes while two terms are connected: " + kind);
                FilterModes.Set(kind, next);
            }
            selection.Set("B", false);
            FilterModes.Set(kind, FilterMode.Or);
            Check(FilterModes.Next(kind, selection.Count) == FilterMode.Single,
                "Removing the second term restores Single: " + kind);
            selection.Clear();
            Check(FilterModes.Next(kind, selection.Count) == FilterMode.Single,
                "Clearing all terms restores Single: " + kind);
            selection.Set("A", true); selection.Set("B", true);
            FilterModes.Set(kind, FilterMode.Single);
            Check(FilterModes.Operation(kind, selection.Count) != FilterMode.Single && selection.Count == 2,
                "A saved Single mode is suppressed when switching to Next with an existing connected expression: " + kind);
            CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
            FilterModes.Set(kind, FilterMode.Or);
            Check(FilterModes.Next(kind, selection.Count) == FilterMode.Single,
                "Whole-category mode retains Single even with two selections: " + kind);
            CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
        }
        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Keywords, FilterTrayKind.Race })
        {
            FilterModes.Set(kind, FilterMode.Or);
            var selection = new FilterSelection<string>(kind);
            selection.Toggle("A"); selection.Toggle("B");
            FilterModes.Set(kind, FilterMode.And);
            Check(!selection.IsCurrent("A") && selection.IsCurrent("B"),
                "Choosing AND makes an earlier standalone OR filter yellow and leaves the current branch blue");
            selection.Toggle("A");
            Check(selection.Count == 3 && selection.Values.Count(value => value == "A") == 2
                && selection.Capture()[2].Operator == FilterMode.And && selection.IsCurrent("A"),
                "Clicking the yellow filter appends AND without deleting its previous occurrence");
            selection.Toggle("A");
            Check(selection.Count == 2 && selection.Contains("A") && selection.IsCurrent("A"),
                "Clicking again removes only the newly appended occurrence and restores its standalone blue glow");
            selection.Toggle("C"); // A OR (B AND C).
            FilterModes.Set(kind, FilterMode.Or);
            Check(selection.IsCurrent("A") && !selection.IsCurrent("B") && !selection.IsCurrent("C"),
                "Choosing OR marks AND-bound filters for reuse, while an unrelated standalone OR filter stays blue");
            selection.Toggle("B");
            Check(selection.Count == 4 && selection.Values.Count(value => value == "B") == 2
                && selection.Capture()[3].Operator == FilterMode.Or,
                "Clicking an AND-bound yellow filter adds an OR branch without removing the old condition");
            selection.Toggle("B");
            Check(selection.Count == 3, "The appended OR branch can be rolled back by clicking again");
        }
    }

    private static void CheckExpressionGlowGroups()
    {
        foreach (FilterTrayKind kind in new[] { FilterTrayKind.Keywords, FilterTrayKind.Race })
        {
            var selection = new FilterSelection<string>(kind);
            void Load(string expression)
            {
                string[] tokens = expression.Split(' ');
                var terms = new List<FilterTerm<string>>();
                for (int i = 0; i < tokens.Length; i += 2)
                    terms.Add(new FilterTerm<string>(tokens[i], i > 0 && tokens[i - 1] == "OR" ? FilterMode.Or : FilterMode.And));
                selection.Restore(terms);
            }
            void Blue(params string[] expected)
            {
                var actual = selection.Values.Distinct().Where(selection.IsCurrent).OrderBy(value => value).ToArray();
                Check(actual.SequenceEqual(expected.OrderBy(value => value)) && !selection.IsCurrent("unselected"),
                    "Mixed expression blue glows for " + kind + ": expected " + string.Join(",", expected)
                    + ", got " + string.Join(",", actual));
            }
            Load("A OR B AND C OR D"); Blue("A", "D");
            FilterModes.Set(kind, FilterMode.Or); Blue("A", "D");
            FilterModes.Set(kind, FilterMode.And); Blue("D");
            selection.Set("C", false); Blue("A", "B", "D");
            Load("A OR B AND C OR D");
            selection.Set("B", false); Blue("D"); // C joins A through its own preceding AND.
            Load("A AND B OR C OR D"); Blue("C", "D");
            selection.Set("A", false); Blue("B", "C", "D");
            Load("A OR B OR C AND D"); Blue("A", "B", "C", "D");
            Load("A AND B AND C OR D AND E OR F"); Blue("F");
            selection.Set("F", false); Blue("D", "E");
            Load("A AND B OR A OR C AND D OR E"); Blue("A", "E");
            selection.Set("A", false); Blue("E"); // Removing the standalone occurrence reveals the older AND occurrence.
            Load("A AND B OR A AND C OR D"); Blue("D");
            Load("A AND B OR C AND A"); Blue("A", "C");
            Load("A AND B OR C");
            selection.Set("B", false); Blue("A", "C");
            Load("A AND B OR C");
            selection.Set("C", false); Blue("A", "B");
        }
    }

    private static void CheckEmptyFilterOptions()
    {
        CollectionQualitySettings.HideEmptyFilters = true;
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        GameDbf.KeywordText.Records.Clear(); GameStrings.Names.Clear();
        AddKeyword(GAME_TAG.TAUNT, "Taunt"); AddKeyword(GAME_TAG.PREPARE, "Prepare");
        var taunt = Card("native-search-hit", GAME_TAG.TAUNT);
        var prepare = Card("excluded-by-native-search", GAME_TAG.PREPARE);
        CollectionManager.Get().Cards.Clear(); CollectionManager.Get().Cards.AddRange(new[] { taunt, prepare });
        KeywordCatalog.Invalidate(); KeywordCatalog.GetEntries();
        FilterAvailability.Capture(new List<CollectibleCard> { taunt });
        Check(FilterAvailability.Keyword(GAME_TAG.TAUNT) && !FilterAvailability.Keyword(GAME_TAG.PREPARE),
            "Zero-result options respect native restrictions before custom filtering");
        KeywordFilterState.SetSelected(GAME_TAG.PREPARE, true);
        Check(FilterAvailability.Keyword(GAME_TAG.PREPARE), "Selected options remain visible even when their result is empty");
        KeywordFilterState.Clear();
        ExtraFilterState.SetType(TAG_CARDTYPE.SPELL, true);
        Check(!FilterAvailability.Keyword(GAME_TAG.TAUNT), "Candidate keywords must also satisfy the other active categories");
        ExtraFilterState.ClearAll();
        Check(FilterAvailability.Extra(FilterTrayKind.Type, (int)TAG_CARDTYPE.MINION, false)
            && !FilterAvailability.Extra(FilterTrayKind.Type, (int)TAG_CARDTYPE.SPELL, false), "Type candidates use actual card metadata");
        var missing = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
        var present = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.JAIL } };
        Check(!FilterAvailability.Expansion(missing, false) && FilterAvailability.Expansion(present, false),
            "Expansion candidates use the broad native pool without forgetting the other restrictions");
        var selection = new FilterSelection<string>(FilterTrayKind.Keywords);
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        selection.Set("old", true);
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        Check(!selection.MatchesCandidate("new", v => v == "old"),
            "An existing OR group does not make a new empty branch appear viable");
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        CollectionQualitySettings.HideEmptyFilters = false;
        Check(FilterAvailability.Keyword(GAME_TAG.PREPARE), "Turning hiding off restores all catalog options");
        FilterAvailability.Clear(); FilterModes.StartSession();
        CheckAvailabilityCache(taunt, prepare);
        CheckInspectAvailability(taunt, prepare);
    }

    private static void CheckInspectAvailability(CollectibleCard taunt, CollectibleCard prepare)
    {
        CollectionQualitySettings.HideEmptyFilters = true;
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear(); FilterModes.StartSession();
        FilterAvailability.Capture(new[] { taunt, prepare });
        Check(FilterAvailability.Keyword(GAME_TAG.TAUNT) && FilterAvailability.Keyword(GAME_TAG.PREPARE),
            "Warm both keyword metadata indexes before an inspected-card change");
        var remainingQuality = Card(taunt.CardId, GAME_TAG.TAUNT);
        int reads = CollectibleCard.EntityReads;
        FilterAvailability.InspectedCardOwnershipChanged(taunt.CardId, new[] { remainingQuality });
        Check(FilterAvailability.Keyword(GAME_TAG.TAUNT) && CollectibleCard.EntityReads == reads,
            "Removing one quality keeps the ID available and reuses all keyword indexes");
        FilterAvailability.InspectedCardOwnershipChanged(taunt.CardId, Array.Empty<CollectibleCard>());
        Check(!FilterAvailability.Keyword(GAME_TAG.TAUNT) && FilterAvailability.Keyword(GAME_TAG.PREPARE)
            && CollectibleCard.EntityReads == reads,
            "Removing the final inspected quality hides its options without rereading other card definitions");
        Check(FilterAvailability.TryMatchCurrent(remainingQuality, out bool removed) && !removed
            && FilterAvailability.TryMatchCurrent(prepare, out bool retained) && retained,
            "A removed ID cannot remain in cached custom-filter results");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Not);
        Check(FilterAvailability.Keyword(GAME_TAG.TAUNT) && !FilterAvailability.Keyword(GAME_TAG.PREPARE),
            "Negative availability excludes tombstoned IDs from its candidate universe");
        FilterAvailability.Capture(new[] { taunt, prepare });
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Single);
        Check(FilterAvailability.Keyword(GAME_TAG.TAUNT), "The next full native query restores its fresh universe");
        FilterAvailability.Clear();
        FilterAvailability.Capture(new[] { taunt, prepare });
        FilterAvailability.InspectedCardOwnershipChanged(taunt.CardId, Array.Empty<CollectibleCard>());
        Check(!FilterAvailability.Keyword(GAME_TAG.TAUNT) && FilterAvailability.Keyword(GAME_TAG.PREPARE),
            "An inspected-card removal also works before metadata indexes were first created");
        FilterAvailability.Clear(); CollectionQualitySettings.HideEmptyFilters = false; FilterModes.StartSession();
    }

    private static void CheckAvailabilityCache(CollectibleCard taunt, CollectibleCard prepare)
    {
        CollectionQualitySettings.HideEmptyFilters = true;
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        KeywordFilterState.SetSelected(GAME_TAG.TAUNT, true);
        FilterAvailability.Capture(new List<CollectibleCard> { taunt, prepare });
        Check(!FilterAvailability.Keyword(GAME_TAG.PREPARE), "AND availability requires the existing keyword and candidate on the same card");
        int reads = CollectibleCard.EntityReads;
        for (int i = 0; i < 1000; i++)
            Check(!FilterAvailability.Keyword(GAME_TAG.PREPARE), "Cached empty candidate remains unavailable");
        Check(CollectibleCard.EntityReads == reads, "Reopening or rechecking an unchanged option does not rescan any card definitions");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        Check(FilterAvailability.Keyword(GAME_TAG.PREPARE) && CollectibleCard.EntityReads == reads,
            "Changing the next operator recombines indexed card sets without rereading definitions");
        ExtraFilterState.SetType(TAG_CARDTYPE.SPELL, true);
        Check(!FilterAvailability.Keyword(GAME_TAG.PREPARE), "Changing another filter category invalidates candidate pools");
        ExtraFilterState.ClearAll();
        Check(FilterAvailability.Keyword(GAME_TAG.PREPARE), "Removing the other category restores viable options");
        FilterAvailability.Capture(new List<CollectibleCard> { taunt });
        Check(!FilterAvailability.Keyword(GAME_TAG.PREPARE), "Native search result changes invalidate cached candidate results");
        FilterAvailability.Capture(new List<CollectibleCard> { prepare });
        Check(FilterAvailability.Keyword(GAME_TAG.PREPARE), "A fresh native result pool makes matching options available again");
        reads = CollectibleCard.EntityReads;
        FilterAvailability.BeginCustomRefresh();
        try { FilterAvailability.Capture(new List<CollectibleCard> { prepare }); }
        finally { FilterAvailability.EndCustomRefresh(); }
        Check(FilterAvailability.Keyword(GAME_TAG.PREPARE) && CollectibleCard.EntityReads == reads,
            "Custom refreshes with unchanged native input retain the inverted card index");
        prepare.Definition.Tags.Remove(GAME_TAG.PREPARE);
        FilterAvailability.Capture(new List<CollectibleCard> { prepare });
        Check(!FilterAvailability.Keyword(GAME_TAG.PREPARE),
            "Native collection refreshes invalidate cached metadata even with unchanged card IDs and object identities");
        prepare.Definition.Tags.Add(GAME_TAG.PREPARE);
        FilterAvailability.Clear(); KeywordFilterState.Clear();
        CollectionQualitySettings.HideEmptyFilters = false; FilterModes.StartSession();
    }
}
