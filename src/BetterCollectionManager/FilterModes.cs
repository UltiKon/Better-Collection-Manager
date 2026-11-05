using System;
using System.Collections.Generic;

namespace BetterCollectionManager
{
    internal enum FilterMode { Single, And, Or, Not, AndNot, OrNot }
    internal enum FilterModeBehavior { Hidden, Category, Expression }

    internal enum FilterTrayKind
    {
        None = 0, Keywords = 1, Type = 2, Race = 3, School = 4,
        Quality = 5, Rarity = 6, Menu = 7, Expansions = 8, Categories = 9, Packages = 10, PackageExpansions = 11,
    }

    internal static class FilterModes
    {
        internal static readonly FilterTrayKind[] Kinds = {
            FilterTrayKind.Expansions, FilterTrayKind.Keywords, FilterTrayKind.Type,
            FilterTrayKind.Race, FilterTrayKind.School, FilterTrayKind.Rarity, FilterTrayKind.Categories, FilterTrayKind.Packages
        };
        private static readonly Dictionary<FilterTrayKind, FilterMode> s_modes =
            new Dictionary<FilterTrayKind, FilterMode>();
        private static bool s_loaded;
        private static readonly Dictionary<FilterTrayKind, int> s_revisions = new Dictionary<FilterTrayKind, int>();
        public static int ResultsRevision { get; private set; }
        public static void InvalidateResults() { unchecked { ResultsRevision++; } }
        private static FilterTrayKind Context(FilterTrayKind kind) => kind == FilterTrayKind.Expansions && PackageViewState.IsActive ? FilterTrayKind.PackageExpansions : kind;
        public static int Revision(FilterTrayKind kind) => s_revisions.TryGetValue(Context(kind), out int value) ? value : 0;
        public static bool IsExpression => CollectionQualitySettings.FilterModeBehavior == FilterModeBehavior.Expression;
        public static bool IsNegative(FilterMode mode) => mode == FilterMode.Not || mode == FilterMode.AndNot || mode == FilterMode.OrNot;
        public static bool IsOr(FilterMode mode) => mode == FilterMode.Or || mode == FilterMode.OrNot;
        public static bool IsAnd(FilterMode mode) => mode == FilterMode.And || mode == FilterMode.AndNot;
        public static bool IsSingle(FilterMode mode) => mode == FilterMode.Single || mode == FilterMode.Not;
        public static FilterMode WithoutNot(FilterMode mode) => mode == FilterMode.Not ? FilterMode.Single
            : mode == FilterMode.AndNot ? FilterMode.And : mode == FilterMode.OrNot ? FilterMode.Or : mode;
        public static FilterMode WithNot(FilterMode mode, bool negative) => !negative ? WithoutNot(mode)
            : IsSingle(mode) ? FilterMode.Not : IsAnd(mode) ? FilterMode.AndNot : FilterMode.OrNot;
        public static FilterMode ToggleNot(FilterTrayKind kind, int termCount = 0)
        {
            FilterMode mode = Operation(kind, termCount);
            FilterMode toggled = WithNot(mode, !IsNegative(mode));
            return toggled == FilterMode.And && !SupportsAnd(kind) ? FilterMode.Or : toggled;
        }

        public static FilterMode Operation(FilterTrayKind kind, int termCount = 0)
        {
            FilterMode mode = Get(kind);
            if (IsExpression && mode == FilterMode.Single && termCount > 1)
                return SupportsAnd(kind) ? FilterMode.And : FilterMode.Or;
            if (IsExpression && mode == FilterMode.Not && termCount > 1) return FilterMode.AndNot;
            return IsExpression && mode == FilterMode.And && !SupportsAnd(kind) ? FilterMode.Or : mode;
        }
        public static bool SupportsAnd(FilterTrayKind kind) => kind == FilterTrayKind.Keywords || kind == FilterTrayKind.Race
            || kind == FilterTrayKind.Categories;
        public static FilterMode Next(FilterTrayKind kind, int termCount = 0)
        {
            FilterMode current = Operation(kind, termCount);
            bool negative = IsNegative(current);
            var cycle = SupportsAnd(kind) || negative
                ? new[] { FilterMode.Single, FilterMode.And, FilterMode.Or }
                : new[] { FilterMode.Single, FilterMode.Or };
            int index = Array.IndexOf(cycle, WithoutNot(current));
            for (int step = 1; step <= cycle.Length; step++)
            {
                FilterMode next = WithNot(cycle[(Math.Max(0, index) + step) % cycle.Length], negative);
                if (!IsExpression || termCount <= 1 || !IsSingle(next)) return next;
            }
            return current;
        }

        public static FilterMode Default(FilterTrayKind kind) => FilterMode.Single;
        public static bool HasNonSingleModes
        {
            get { foreach (FilterTrayKind kind in Kinds) if (Get(kind) != FilterMode.Single) return true; return false; }
        }

        public static FilterMode Get(FilterTrayKind kind)
        {
            if (!s_loaded) StartSession();
            kind = Context(kind);
            return s_modes.TryGetValue(kind, out FilterMode mode) ? mode : Default(kind);
        }

        public static void Set(FilterTrayKind kind, FilterMode mode)
        {
            kind = Context(kind);
            if ((Array.IndexOf(Kinds, kind) < 0 && kind != FilterTrayKind.PackageExpansions) || !Enum.IsDefined(typeof(FilterMode), mode)) return;
            if (Get(kind) == mode) return;
            s_modes[kind] = mode;
            InvalidateResults();
            s_revisions[kind] = Revision(kind) + 1;
            if (CollectionQualitySettings.RememberFilterModes && kind != FilterTrayKind.PackageExpansions)
                CollectionQualitySettings.SavedFilterModes = Serialize();
        }

        public static void StartSession()
        {
            s_loaded = true;
            InvalidateResults();
            s_modes.Clear();
            if (!CollectionQualitySettings.RememberFilterModes) return;
            foreach (string pair in CollectionQualitySettings.SavedFilterModes.Split(';'))
            {
                string[] parts = pair.Split('=');
                if (parts.Length == 2 && Enum.TryParse(parts[0], out FilterTrayKind kind)
                    && Array.IndexOf(Kinds, kind) >= 0 && Enum.TryParse(parts[1], out FilterMode mode)
                    && Enum.IsDefined(typeof(FilterMode), mode))
                    s_modes[kind] = mode == FilterMode.And && !SupportsAnd(kind) ? FilterMode.Or : mode;
            }
        }

        public static string Serialize()
        {
            if (!s_loaded) StartSession();
            var parts = new List<string>();
            // Package expansion controls are temporary navigation state. Never
            // serialize them in place of the suspended card expansion mode.
            foreach (FilterTrayKind kind in Kinds) parts.Add(kind + "=" + (s_modes.TryGetValue(kind, out var mode) ? mode : Default(kind)));
            return string.Join(";", parts.ToArray());
        }

        public static string Label(FilterMode mode)
        {
            switch (mode)
            {
                case FilterMode.Single: return "Single";
                case FilterMode.And: return "AND";
                case FilterMode.Or: return "OR";
                case FilterMode.Not: return "NOT";
                case FilterMode.AndNot: return "AND NOT";
                default: return "OR NOT";
            }
        }
    }

    internal readonly struct FilterTerm<T>
    {
        public readonly T Value;
        public readonly FilterMode Operator;
        public FilterTerm(T value, FilterMode operation) { Value = value; Operator = operation; }
    }

    /// <summary>Each expression term owns its preceding operator; removing it removes that operator too.</summary>
    internal sealed class FilterSelection<T>
    {
        private readonly FilterTrayKind m_kind;
        private readonly List<FilterTerm<T>> m_terms = new List<FilterTerm<T>>();
        private bool m_expression;
        private int m_revision = -1;
        private static readonly EqualityComparer<T> Equal = EqualityComparer<T>.Default;
        public FilterSelection(FilterTrayKind kind) { m_kind = kind; m_expression = FilterModes.IsExpression; }
        private bool Single => !m_expression && FilterModes.IsSingle(FilterModes.Get(m_kind));
        private FilterMode Operation => FilterModes.Operation(m_kind, m_terms.Count);
        private int ActiveStart => Single ? System.Math.Max(0, m_terms.Count - 1) : 0;
        public int Count { get { Normalize(); return Single && m_terms.Count > 0 ? 1 : m_terms.Count; } }
        public IEnumerable<T> Values
        {
            get { Normalize(); var values = new List<T>(); for (int i = ActiveStart; i < m_terms.Count; i++) values.Add(m_terms[i].Value); return values; }
        }
        public List<FilterTerm<T>> Capture() { Normalize(); return new List<FilterTerm<T>>(m_terms); }
        public void Restore(IEnumerable<FilterTerm<T>> terms)
        {
            var before = new List<FilterTerm<T>>(m_terms);
            m_terms.Clear(); m_terms.AddRange(terms); m_expression = FilterModes.IsExpression;
            if (!SameTerms(before)) FilterModes.InvalidateResults();
            m_revision = FilterModes.Revision(m_kind); Normalize();
        }
        public bool Contains(T value) { Normalize(); return Find(value, ActiveStart) >= 0; }
        public bool IsExcluded(T value)
        {
            Normalize(); int index = Find(value, ActiveStart);
            return index >= 0 && FilterModes.IsNegative(m_expression ? m_terms[index].Operator : Operation);
        }
        public bool IsCurrent(T value)
        {
            Normalize();
            if (!m_expression) return Find(value, ActiveStart) >= 0;
            if (CanAppend(value)) return false;
            int current = CurrentStart();
            for (int i = m_terms.Count - 1; i >= 0; i--)
            {
                if (!Equal.Equals(m_terms[i].Value, value)) continue;
                // Without a pending reuse operation, blue wins if an
                // occurrence is current or stands alone in its OR branch.
                bool joinedBefore = i > 0 && !FilterModes.IsOr(m_terms[i].Operator);
                bool joinedAfter = i + 1 < m_terms.Count && !FilterModes.IsOr(m_terms[i + 1].Operator);
                if (i >= current || (!joinedBefore && !joinedAfter)) return true;
            }
            return false;
        }
        private bool Joined(int index) => (index > 0 && !FilterModes.IsOr(m_terms[index].Operator))
            || (index + 1 < m_terms.Count && !FilterModes.IsOr(m_terms[index + 1].Operator));
        private bool HasJoinedOccurrence(T value, bool negative)
        {
            for (int i = 0; i < m_terms.Count; i++)
                if (Equal.Equals(m_terms[i].Value, value) && FilterModes.IsNegative(m_terms[i].Operator) == negative && Joined(i)) return true;
            return false;
        }
        private bool CanAppend(T value)
        {
            if (!m_expression || m_revision == FilterModes.Revision(m_kind)) return false;
            FilterMode mode = Operation;
            bool negative = FilterModes.IsNegative(mode);
            if (FilterModes.IsSingle(mode)) return Find(value, 0, negative) < 0 && Find(value, 0) >= 0;
            if (FilterModes.IsAnd(mode)) return Find(value, CurrentStart(), negative) < 0 && Find(value, 0) >= 0;
            return FilterModes.IsOr(mode) && (HasJoinedOccurrence(value, negative)
                || (Find(value, 0, negative) < 0 && Find(value, 0) >= 0));
        }
        private bool SameTerms(List<FilterTerm<T>> before)
        {
            if (before.Count != m_terms.Count) return false;
            for (int i = 0; i < before.Count; i++)
                if (!Equal.Equals(before[i].Value, m_terms[i].Value) || before[i].Operator != m_terms[i].Operator) return false;
            return true;
        }
        public void Clear()
        {
            if (m_terms.Count > 0) FilterModes.InvalidateResults();
            m_terms.Clear(); m_revision = -1;
        }
        private int CurrentStart()
        {
            if (!m_expression) return 0;
            for (int i = m_terms.Count - 1; i > 0; i--) if (FilterModes.IsOr(m_terms[i].Operator)) return i;
            return 0;
        }
        private int Find(T value, int start, bool? negative = null)
        {
            for (int i = m_terms.Count - 1; i >= start; i--)
                if (Equal.Equals(m_terms[i].Value, value) && (!negative.HasValue || FilterModes.IsNegative(m_terms[i].Operator) == negative.Value)) return i;
            return -1;
        }
        public void Toggle(T value)
        {
            Normalize();
            Set(value, CanAppend(value) || !Contains(value));
        }
        public void Set(T value, bool on)
        {
            Normalize();
            FilterMode mode = Operation;
            var before = new List<FilterTerm<T>>(m_terms);
            if (Single)
            {
                bool selected = Contains(value);
                if (on == selected) return; // Re-selecting the active value is not an edit.
                // Only a real filter edit commits Single and discards the suspended choices.
                if (m_terms.Count > 1)
                {
                    var latest = m_terms[m_terms.Count - 1]; m_terms.Clear(); m_terms.Add(latest);
                }
            }
            if (!on)
            {
                int index = Find(value, 0, m_expression ? FilterModes.IsNegative(mode) : (bool?)null);
                if (index < 0) index = Find(value, 0);
                if (index >= 0) m_terms.RemoveAt(index);
            }
            else if (m_expression)
            {
                if (FilterModes.IsSingle(mode))
                {
                    m_terms.Clear(); m_terms.Add(new FilterTerm<T>(value, FilterModes.IsNegative(mode) ? FilterMode.AndNot : FilterMode.And));
                }
                else if (FilterModes.IsOr(mode) ? Find(value, 0, FilterModes.IsNegative(mode)) < 0 || HasJoinedOccurrence(value, FilterModes.IsNegative(mode))
                    : Find(value, CurrentStart(), FilterModes.IsNegative(mode)) < 0)
                    m_terms.Add(new FilterTerm<T>(value, m_terms.Count > 0 ? mode : FilterModes.IsNegative(mode) ? FilterMode.AndNot : FilterMode.And));
            }
            else if (!Contains(value))
            {
                if (FilterModes.IsSingle(mode)) m_terms.Clear();
                m_terms.Add(new FilterTerm<T>(value, mode));
            }
            m_revision = FilterModes.Revision(m_kind);
            if (!SameTerms(before)) FilterModes.InvalidateResults();
        }
        public void Remap(Dictionary<T, T> available)
        {
            Normalize();
            var before = new List<FilterTerm<T>>(m_terms);
            for (int i = m_terms.Count - 1; i >= 0; i--)
                if (available.TryGetValue(m_terms[i].Value, out T value))
                    m_terms[i] = new FilterTerm<T>(value, m_terms[i].Operator);
                else m_terms.RemoveAt(i);
            // Keep the most recent alias in category mode, including suspended Single choices.
            var seen = new HashSet<T>();
            if (!m_expression)
            {
                for (int i = m_terms.Count - 1; i >= 0; i--)
                    if (!seen.Add(m_terms[i].Value)) m_terms.RemoveAt(i);
                if (!SameTerms(before)) FilterModes.InvalidateResults();
                return;
            }
            // Deduplicate aliases inside a group while preserving occurrences in other groups.
            var signed = new HashSet<(T Value, bool Negative)>();
            for (int i = 0; i < m_terms.Count; i++)
            {
                if (FilterModes.IsOr(m_terms[i].Operator)) signed.Clear();
                if (!signed.Add((m_terms[i].Value, FilterModes.IsNegative(m_terms[i].Operator)))) m_terms.RemoveAt(i--);
            }
            if (!SameTerms(before)) FilterModes.InvalidateResults();
        }
        private void Normalize()
        {
            bool expression = FilterModes.IsExpression;
            FilterMode mode = Operation;
            if (m_expression != expression)
            {
                if (expression)
                    for (int i = 0; i < m_terms.Count; i++)
                        m_terms[i] = new FilterTerm<T>(m_terms[i].Value, i > 0 && FilterModes.IsOr(mode)
                            ? (FilterModes.IsNegative(mode) ? FilterMode.OrNot : FilterMode.Or)
                            : (FilterModes.IsNegative(mode) ? FilterMode.AndNot : FilterMode.And));
                else
                {
                    var seen = new HashSet<T>();
                    for (int i = m_terms.Count - 1; i >= 0; i--) if (!seen.Add(m_terms[i].Value)) m_terms.RemoveAt(i);
                }
                m_expression = expression;
            }
        }
        public bool MatchesCandidate(T value, Func<T, bool> matches)
        {
            Normalize();
            if (matches(value) == FilterModes.IsNegative(Operation)) return false;
            if (!FilterModes.IsAnd(Operation)) return true;
            int start = m_expression ? CurrentStart() : 0;
            for (int i = start; i < m_terms.Count; i++)
                if (matches(m_terms[i].Value) == FilterModes.IsNegative(m_expression ? m_terms[i].Operator : Operation)) return false;
            return true;
        }

        public FilterBitSet MatchSet(Func<T, FilterBitSet> lookup, int count)
        {
            Normalize();
            if (m_terms.Count == 0) return FilterBitSet.Full(count);
            if (Single) { var set = lookup(m_terms[m_terms.Count - 1].Value); return FilterModes.IsNegative(Operation) ? set.Not() : set; }
            if (!m_expression)
            {
                bool and = FilterModes.IsAnd(FilterModes.Get(m_kind));
                var result = and ? FilterBitSet.Full(count) : FilterBitSet.Empty(count);
                foreach (var term in m_terms)
                {
                    var set = lookup(term.Value); if (FilterModes.IsNegative(Operation)) set = set.Not();
                    result = and ? result.And(set) : result.Or(set);
                }
                return result;
            }
            var union = FilterBitSet.Empty(count);
            var group = FilterBitSet.Full(count);
            for (int i = 0; i < m_terms.Count; i++)
            {
                if (i > 0 && FilterModes.IsOr(m_terms[i].Operator)) { union = union.Or(group); group = FilterBitSet.Full(count); }
                var set = lookup(m_terms[i].Value); if (FilterModes.IsNegative(m_terms[i].Operator)) set = set.Not();
                group = group.And(set);
            }
            return union.Or(group);
        }

        public FilterBitSet CandidateContextSet(Func<T, FilterBitSet> lookup, int count)
        {
            Normalize();
            var result = FilterBitSet.Full(count);
            if (FilterModes.IsAnd(Operation))
                for (int i = m_expression ? CurrentStart() : 0; i < m_terms.Count; i++)
                {
                    var set = lookup(m_terms[i].Value); if (FilterModes.IsNegative(m_expression ? m_terms[i].Operator : Operation)) set = set.Not();
                    result = result.And(set);
                }
            return result;
        }

        public bool Matches(Func<T, bool> matches)
        {
            Normalize();
            if (m_terms.Count == 0) return true;
            if (Single) return matches(m_terms[m_terms.Count - 1].Value) != FilterModes.IsNegative(Operation);
            if (!m_expression)
            {
                bool all = FilterModes.IsAnd(FilterModes.Get(m_kind));
                foreach (var term in m_terms) if ((matches(term.Value) != FilterModes.IsNegative(Operation)) != all) return !all;
                return all;
            }
            bool group = true;
            for (int i = 0; i < m_terms.Count; i++)
            {
                if (i > 0 && FilterModes.IsOr(m_terms[i].Operator)) { if (group) return true; group = true; }
                group = group && (matches(m_terms[i].Value) != FilterModes.IsNegative(m_terms[i].Operator));
            }
            return group;
        }
    }
}
