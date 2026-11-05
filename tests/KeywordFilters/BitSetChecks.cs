using System;
using System.Collections.Generic;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckBitSetExpressions()
    {
        foreach (int count in new[] { 0, 1, 63, 64, 65, 129 })
        {
            var full = FilterBitSet.Full(count);
            var empty = FilterBitSet.Empty(count);
            Check(!full.Intersects(empty) && !full.Contains(count) && !full.Contains(-1), "Empty sets and trailing bits are excluded");
            var sets = new Dictionary<int, FilterBitSet>();
            for (int value = 0; value < 4; value++)
            {
                int bit = value;
                sets[value] = FilterBitSet.Build(count, card => ((card >> bit) & 1) != 0);
            }
            int intersections = 0;
            for (int card = 0; card < count; card++) if ((card & 3) == 3) intersections++;
            Check(full.IntersectionCount(full) == count && full.IntersectionCount(empty) == 0
                && sets[0].IntersectionCount(sets[1]) == intersections,
                "Portable population counts include partial words and agree with scalar intersection counts");
            var original = sets[0];
            var intersection = original.And(sets[1]);
            var union = original.Or(sets[1]);
            for (int card = 0; card < count; card++)
                Check(original.Contains(card) == ((card & 1) != 0) && intersection.Contains(card) == ((card & 3) == 3)
                    && union.Contains(card) == ((card & 3) != 0), "Set operations preserve cached operands and implement AND/OR");
            foreach (FilterModeBehavior behavior in new[] { FilterModeBehavior.Category, FilterModeBehavior.Expression })
            foreach (FilterMode mode in new[] { FilterMode.Single, FilterMode.And, FilterMode.Or })
            for (int expression = 0; expression < 16; expression++)
            {
                CollectionQualitySettings.FilterModeBehavior = behavior;
                FilterModes.Set(FilterTrayKind.Keywords, mode);
                var terms = new List<FilterTerm<int>>();
                for (int i = 0; i < 5; i++) terms.Add(new FilterTerm<int>(i % 4,
                    i > 0 && (expression & (1 << (i - 1))) != 0 ? FilterMode.Or : FilterMode.And));
                var selection = new FilterSelection<int>(FilterTrayKind.Keywords);
                selection.Restore(terms);
                FilterBitSet result = selection.MatchSet(value => sets[value], count);
                FilterBitSet candidates = sets[3].And(selection.CandidateContextSet(value => sets[value], count));
                for (int card = 0; card < count; card++)
                {
                    int current = card;
                    bool Match(int value) => ((current >> value) & 1) != 0;
                    Check(result.Contains(card) == selection.Matches(Match), "Indexed expression agrees with scalar evaluation, including repeated values");
                    Check(candidates.Contains(card) == selection.MatchesCandidate(3, Match), "Indexed next-candidate context agrees with scalar evaluation");
                }
            }
        }
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        FilterModes.StartSession();
    }
}
