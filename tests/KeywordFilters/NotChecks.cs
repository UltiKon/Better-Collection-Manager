using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckNotExpressions()
    {
        foreach (FilterModeBehavior behavior in new[] { FilterModeBehavior.Category, FilterModeBehavior.Expression })
        foreach (FilterTrayKind kind in FilterModes.Kinds)
        foreach (FilterMode positive in new[] { FilterMode.Single, FilterMode.And, FilterMode.Or })
        {
            if (positive == FilterMode.And && !FilterModes.SupportsAnd(kind)) continue;
            CollectionQualitySettings.FilterModeBehavior = behavior;
            FilterModes.Set(kind, positive);
            var terms = new FilterSelection<int>(kind);
            terms.Set(1, true);
            var before = terms.Capture();
            FilterModes.Set(kind, FilterModes.ToggleNot(kind, terms.Count));
            Check(FilterModes.Get(kind) == FilterModes.WithNot(positive, true)
                && FilterModes.IsNegative(FilterModes.Next(kind, terms.Count)),
                "NOT toggles independently and stays enabled while cycling the connector: " + kind);
            Check(terms.IsExcluded(1) == (behavior == FilterModeBehavior.Category)
                && terms.Capture().Select(term => term.Operator).SequenceEqual(before.Select(term => term.Operator)),
                "NOT changes category polarity or the pending operator without rewriting expression terms");
            FilterModes.Set(kind, FilterModes.ToggleNot(kind, terms.Count));
            Check(FilterModes.Get(kind) == positive && !FilterModes.IsNegative(FilterModes.Next(kind, terms.Count)),
                "Toggling NOT off restores the positive connector");
        }
        foreach (int count in new[] { 0, 1, 63, 64, 65, 129 })
        {
            var values = FilterBitSet.Build(count, index => index % 3 == 0);
            var complement = values.Not();
            Check(FilterBitSet.Empty(count).Not().IntersectionCount(FilterBitSet.Full(count)) == count
                && !FilterBitSet.Full(count).Not().Intersects(FilterBitSet.Full(count))
                && !complement.Contains(count) && !values.Intersects(complement),
                "NOT masks unused bits and complements only its captured card universe");
            for (int i = 0; i < count; i++) Check(complement.Contains(i) == (i % 3 != 0) && values.Contains(i) == (i % 3 == 0),
                "Bitset NOT is immutable and matches scalar negation");
        }
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.AndNot);
        var selection = new FilterSelection<int>(FilterTrayKind.Keywords);
        selection.Restore(new[] { new FilterTerm<int>(0, FilterMode.And), new FilterTerm<int>(1, FilterMode.AndNot),
            new FilterTerm<int>(2, FilterMode.OrNot), new FilterTerm<int>(3, FilterMode.And) });
        FilterBitSet Lookup(int value) => FilterBitSet.Build(32, card => (card & (1 << value)) != 0);
        var indexed = selection.MatchSet(Lookup, 32);
        var candidate = Lookup(4).Not().And(selection.CandidateContextSet(Lookup, 32));
        for (int bits = 0; bits < 32; bits++)
        {
            int card = bits;
            bool Match(int value) => (card & (1 << value)) != 0;
            bool expected = ((bits & 1) != 0 && (bits & 2) == 0) || ((bits & 4) == 0 && (bits & 8) != 0);
            Check(selection.Matches(Match) == expected && indexed.Contains(bits) == expected,
                "A AND NOT B OR NOT C AND D uses AND precedence and preserves per-term negation");
            bool viable = (bits & 4) == 0 && (bits & 8) != 0 && (bits & 16) == 0;
            Check(selection.MatchesCandidate(4, Match) == viable && candidate.Contains(bits) == viable,
                "AND NOT candidate availability uses the signed current branch and the candidate's complement");
        }
        var snapshot = selection.Capture();
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.OrNot);
        Check(selection.Capture().Select(term => term.Operator).SequenceEqual(snapshot.Select(term => term.Operator)),
            "Changing the next negated operator leaves the existing expression intact");
        selection.Restore(snapshot); selection.Remap(new Dictionary<int, int> { [0] = 10, [1] = 11, [2] = 12, [3] = 13 });
        Check(selection.Capture().Select(term => term.Operator).SequenceEqual(snapshot.Select(term => term.Operator)),
            "Alias remapping preserves each term's negation and OR boundary");

        selection.Clear(); FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Not); selection.Toggle(0);
        Check(selection.IsExcluded(0) && !selection.Matches(_ => true) && selection.Matches(_ => false), "Unary NOT excludes its selected value");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or); selection.Toggle(0);
        Check(selection.Count == 2 && selection.Matches(_ => true) && selection.Matches(_ => false),
            "NOT A OR A retains opposite-polarity occurrences and matches the whole universe");
        selection.Clear(); FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And); selection.Toggle(0);
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.AndNot); selection.Toggle(0);
        selection.Remap(new Dictionary<int, int> { [0] = 5 });
        Check(selection.Count == 2 && !selection.Matches(_ => true) && !selection.Matches(_ => false),
            "A AND NOT A stays contradictory after remapping without deduplicating opposite signs");
        selection.Toggle(5);
        Check(selection.Count == 1 && selection.Matches(_ => true) && !selection.IsExcluded(5),
            "Removing a negated occurrence restores the positive term and its polarity");

        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        foreach (FilterTrayKind kind in FilterModes.Kinds)
        foreach (FilterMode mode in new[] { FilterMode.Not, FilterMode.AndNot, FilterMode.OrNot })
        {
            FilterModes.Set(kind, mode); var choices = new FilterSelection<int>(kind); choices.Set(0, true); choices.Set(1, true);
            var set = choices.MatchSet(Lookup, 32);
            for (int bits = 0; bits < 4; bits++)
            {
                int current = bits;
                bool expected = mode == FilterMode.Not ? (bits & 2) == 0 : mode == FilterMode.AndNot ? bits == 0 : bits != 3;
                Check(choices.Matches(value => (current & (1 << value)) != 0) == expected && set.Contains(bits) == expected,
                    "Category NOT, AND NOT and OR NOT have explicit complement truth tables: " + kind);
            }
            var seen = new HashSet<FilterMode>(); var next = FilterMode.Not;
            for (int i = 0; i < 7; i++) { FilterModes.Set(kind, next); seen.Add(next); next = FilterModes.Next(kind); }
            Check(seen.SetEquals(new[] { FilterMode.Not, FilterMode.AndNot, FilterMode.OrNot }),
                "With NOT enabled every category cycles all negative modes without switching NOT off");
        }

        ExtraFilterState.ClearAll(); KeywordFilterState.Clear(); ExpansionFilterState.Clear();
        var plain = Card("no-race"); var all = Card("all-races"); all.Definition.Race = TAG_RACE.ALL;
        var dragon = Card("dragon"); dragon.Definition.Race = TAG_RACE.DRAGON;
        FilterModes.Set(FilterTrayKind.Race, FilterMode.Not); ExtraFilterState.SetRace(TAG_RACE.BEAST, true);
        Check(ExtraFilterState.Matches(plain) && ExtraFilterState.Matches(dragon) && !ExtraFilterState.Matches(all),
            "NOT Beast includes raceless cards and Dragons while excluding All-race minions");
        ExtraFilterState.ClearAll();
        CollectionQualitySettings.RememberFilterModes = true;
        FilterModes.Set(FilterTrayKind.Race, FilterMode.AndNot); FilterModes.Set(FilterTrayKind.Keywords, FilterMode.OrNot);
        FilterModes.Set(FilterTrayKind.Type, FilterMode.Not); FilterModes.StartSession();
        Check(FilterModes.Get(FilterTrayKind.Race) == FilterMode.AndNot && FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.OrNot
            && FilterModes.Get(FilterTrayKind.Type) == FilterMode.Not, "Negative filter modes survive opt-in mode persistence");
        CollectionQualitySettings.RememberFilterModes = false; CollectionQualitySettings.SavedFilterModes = ""; FilterModes.StartSession();
    }
}
