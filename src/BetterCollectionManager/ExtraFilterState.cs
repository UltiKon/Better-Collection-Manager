using System.Collections.Generic;
using System.Text;

namespace BetterCollectionManager
{
    /// <summary>
    /// Each category combines selections using its current mode.
    /// Across groups: AND when multiple groups active.
    /// Empty group = no filter for that dimension. Session-only (cleared on CM exit).
    /// </summary>
    internal static class ExtraFilterState
    {
        private static readonly FilterSelection<TAG_CARDTYPE> s_types = new FilterSelection<TAG_CARDTYPE>(FilterTrayKind.Type);
        private static readonly FilterSelection<TAG_RACE> s_races = new FilterSelection<TAG_RACE>(FilterTrayKind.Race);
        private static readonly FilterSelection<TAG_SPELL_SCHOOL> s_schools = new FilterSelection<TAG_SPELL_SCHOOL>(FilterTrayKind.School);
        private static readonly FilterSelection<TAG_RARITY> s_rarities = new FilterSelection<TAG_RARITY>(FilterTrayKind.Rarity);
        private static readonly FilterSelection<CuratedCategory> s_categories = new FilterSelection<CuratedCategory>(FilterTrayKind.Categories);

        private static readonly FilterSelection<PackageKind> s_packages = new FilterSelection<PackageKind>(FilterTrayKind.Packages);
        public static bool HasPackage(PackageKind kind) => s_packages.Contains(kind);
        public static bool IsCurrentPackage(PackageKind kind) => s_packages.IsCurrent(kind);
        public static bool MatchesPackage(CategoryCatalog.Combo package) => s_packages.Count > 0 && s_packages.Matches(kind => PackageCatalog.Matches(package, kind));
        public static void TogglePackage(PackageKind kind) { s_categories.Clear(); s_packages.Toggle(kind); }
        public static void ClearPackages() => s_packages.Clear();
        public static bool MatchesEntry(CategoryCatalog.Combo entry) => entry.IsPackage ? MatchesPackage(entry)
            : s_packages.Count == 0 && s_categories.Count > 0 && s_categories.Matches(value => value == entry.Category || value == CuratedCategory.Combos);
        public static bool IsActive =>
            s_types.Count > 0 || s_races.Count > 0 || s_schools.Count > 0 || s_rarities.Count > 0 || s_categories.Count > 0 || s_packages.Count > 0;

        public static bool HasCategory(CuratedCategory category) => s_categories.Contains(category);
        public static void SetCategory(CuratedCategory category, bool on) { if (on) s_packages.Clear(); s_categories.Set(category, on); }
        public static void ToggleCategory(CuratedCategory category) { s_packages.Clear(); s_categories.Toggle(category); }
        public static bool IsCurrentCategory(CuratedCategory category) => s_categories.IsCurrent(category);
        public static void ClearCategories() => s_categories.Clear();
        public static void PruneCategories(IEnumerable<CuratedCategory> available)
        {
            var keep = new Dictionary<CuratedCategory, CuratedCategory>();
            foreach (CuratedCategory category in available) keep[category] = category;
            s_categories.Remap(keep);
        }
        public static bool MatchesComboCategory(CuratedCategory category)
            => s_categories.Count > 0 && s_categories.Matches(value => value == category || value == CuratedCategory.Combos);
        public static bool MatchesComboCandidate(CuratedCategory candidate, CuratedCategory category)
            => s_categories.MatchesCandidate(candidate, value => value == category || value == CuratedCategory.Combos);
        public static bool IsExcluded(FilterTrayKind kind, int value)
        {
            switch (kind)
            {
                case FilterTrayKind.Type: return s_types.IsExcluded((TAG_CARDTYPE)value);
                case FilterTrayKind.Race: return s_races.IsExcluded((TAG_RACE)value);
                case FilterTrayKind.School: return s_schools.IsExcluded((TAG_SPELL_SCHOOL)value);
                case FilterTrayKind.Rarity: return s_rarities.IsExcluded((TAG_RARITY)value);
                case FilterTrayKind.Packages: return s_packages.IsExcluded((PackageKind)value);
                case FilterTrayKind.Categories: return s_categories.IsExcluded((CuratedCategory)value);
                default: return false;
            }
        }

        public static bool HasType(TAG_CARDTYPE t) => s_types.Contains(t);
        public static bool HasRace(TAG_RACE r) => s_races.Contains(r);
        public static bool HasSchool(TAG_SPELL_SCHOOL s) => s_schools.Contains(s);
        public static bool HasRarity(TAG_RARITY r) => s_rarities.Contains(r);

        public static void SetType(TAG_CARDTYPE t, bool on)
        {
            s_types.Set(t, on);
        }

        public static void SetRace(TAG_RACE r, bool on)
        {
            s_races.Set(r, on);
        }

        public static void SetSchool(TAG_SPELL_SCHOOL s, bool on)
        {
            s_schools.Set(s, on);
        }

        public static void SetRarity(TAG_RARITY r, bool on)
        {
            s_rarities.Set(r, on);
        }

        public static void ToggleType(TAG_CARDTYPE value) => s_types.Toggle(value);
        public static void ToggleRace(TAG_RACE value) => s_races.Toggle(value);
        public static void ToggleSchool(TAG_SPELL_SCHOOL value) => s_schools.Toggle(value);
        public static void ToggleRarity(TAG_RARITY value) => s_rarities.Toggle(value);
        public static bool IsCurrentType(TAG_CARDTYPE value) => s_types.IsCurrent(value);
        public static bool IsCurrentRace(TAG_RACE value) => s_races.IsCurrent(value);
        public static bool IsCurrentSchool(TAG_SPELL_SCHOOL value) => s_schools.IsCurrent(value);
        public static bool IsCurrentRarity(TAG_RARITY value) => s_rarities.IsCurrent(value);

        public static void ClearTypes() => s_types.Clear();
        public static void ClearRaces() => s_races.Clear();
        public static void ClearSchools() => s_schools.Clear();
        public static void ClearRarities() => s_rarities.Clear();

        public static void ClearAll()
        {
            s_types.Clear();
            s_races.Clear();
            s_schools.Clear();
            s_rarities.Clear();
            s_categories.Clear(); s_packages.Clear();
        }

        public static bool Matches(CollectibleCard card) => MatchesExcept(card, FilterTrayKind.None);

        public static bool MatchesExcept(CollectibleCard card, FilterTrayKind except, bool ignorePackages = false)
        {
            if (!ignorePackages && except != FilterTrayKind.Packages && s_packages.Count > 0) return false;
            if (!IsActive)
                return true;
            if (card == null)
                return false;
            EntityDef def = card.GetEntityDef();
            if (def == null)
                return false;

            if (except != FilterTrayKind.Type && s_types.Count > 0)
            {
                TAG_CARDTYPE ct = def.GetCardType();
                if (!s_types.Matches(t => t == ct))
                    return false;
            }

            if (except != FilterTrayKind.Race && s_races.Count > 0 && !CardMatchesSelectedRaces(card, def))
                return false;

            if (except != FilterTrayKind.School && s_schools.Count > 0)
            {
                TAG_SPELL_SCHOOL school = (TAG_SPELL_SCHOOL)def.GetTag(GAME_TAG.SPELL_SCHOOL);
                if (!s_schools.Matches(s => s == school))
                    return false;
            }

            if (except != FilterTrayKind.Rarity && s_rarities.Count > 0 && !CardMatchesSelectedRarity(card))
                return false;

            if (except != FilterTrayKind.Categories && s_categories.Count > 0
                && !s_categories.Matches(category => CategoryCatalog.Matches(card, category))) return false;

            return true;
        }

        public static bool MatchesCandidate(CollectibleCard card, FilterTrayKind kind, int value, bool otherCategoriesMatched = false)
        {
            if (card == null || card.GetEntityDef() == null || (!otherCategoriesMatched && !MatchesExcept(card, kind))) return false;
            EntityDef def = card.GetEntityDef();
            switch (kind)
            {
                case FilterTrayKind.Type: return s_types.MatchesCandidate((TAG_CARDTYPE)value, t => t == def.GetCardType());
                case FilterTrayKind.Race: return CardMatchesSelectedRaces(card, def, (TAG_RACE)value);
                case FilterTrayKind.School: return s_schools.MatchesCandidate((TAG_SPELL_SCHOOL)value,
                    school => school == (TAG_SPELL_SCHOOL)def.GetTag(GAME_TAG.SPELL_SCHOOL));
                case FilterTrayKind.Rarity: return CardMatchesSelectedRarity(card, (TAG_RARITY)value);
                case FilterTrayKind.Packages: return false;
                case FilterTrayKind.Categories: return s_categories.MatchesCandidate((CuratedCategory)value,
                    category => CategoryCatalog.Matches(card, category));
                default: return true;
            }
        }

        public static bool MatchesValue(CollectibleCard card, FilterTrayKind kind, int value)
        {
            EntityDef def = card?.GetEntityDef();
            if (def == null) return false;
            switch (kind)
            {
                case FilterTrayKind.Type: return (TAG_CARDTYPE)value == def.GetCardType();
                case FilterTrayKind.Race: return CardMatchesSelectedRaces(card, def, (TAG_RACE)value, true);
                case FilterTrayKind.School: return value == def.GetTag(GAME_TAG.SPELL_SCHOOL);
                case FilterTrayKind.Rarity: return CardMatchesSelectedRarity(card, (TAG_RARITY)value, true);
                case FilterTrayKind.Packages: return false;
                case FilterTrayKind.Categories: return CategoryCatalog.Matches(card, (CuratedCategory)value);
                default: return true;
            }
        }
        public static FilterBitSet MatchSet(FilterTrayKind kind, System.Func<int, FilterBitSet> lookup, int count)
        {
            switch (kind)
            {
                case FilterTrayKind.Type: return s_types.MatchSet(value => lookup((int)value), count);
                case FilterTrayKind.Race: return s_races.MatchSet(value => lookup((int)value), count);
                case FilterTrayKind.School: return s_schools.MatchSet(value => lookup((int)value), count);
                case FilterTrayKind.Rarity: return s_rarities.MatchSet(value => lookup((int)value), count);
                case FilterTrayKind.Packages: return s_packages.Count > 0 ? FilterBitSet.Empty(count) : FilterBitSet.Full(count);
                case FilterTrayKind.Categories: return s_categories.MatchSet(value => lookup((int)value), count);
                default: return FilterBitSet.Full(count);
            }
        }
        public static FilterBitSet CandidateContextSet(FilterTrayKind kind, System.Func<int, FilterBitSet> lookup, int count)
        {
            switch (kind)
            {
                case FilterTrayKind.Type: return s_types.CandidateContextSet(item => lookup((int)item), count);
                case FilterTrayKind.Race: return s_races.CandidateContextSet(item => lookup((int)item), count);
                case FilterTrayKind.School: return s_schools.CandidateContextSet(item => lookup((int)item), count);
                case FilterTrayKind.Rarity: return s_rarities.CandidateContextSet(item => lookup((int)item), count);
                case FilterTrayKind.Packages: return s_packages.CandidateContextSet(_ => FilterBitSet.Empty(count), count);
                case FilterTrayKind.Categories: return s_categories.CandidateContextSet(item => lookup((int)item), count);
                default: return FilterBitSet.Full(count);
            }
        }

        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("types=").Append(s_types.Count);
            sb.Append(" races=").Append(s_races.Count);
            sb.Append(" schools=").Append(s_schools.Count);
            sb.Append(" rarities=").Append(s_rarities.Count);
            sb.Append(" categories=").Append(s_categories.Count);
            sb.Append(" packages=").Append(s_packages.Count);
            return sb.ToString();
        }

        public static int SelectedCount(FilterTrayKind kind)
        {
            switch (kind)
            {
                case FilterTrayKind.Type: return s_types.Count;
                case FilterTrayKind.Race: return s_races.Count;
                case FilterTrayKind.School: return s_schools.Count;
                case FilterTrayKind.Rarity: return s_rarities.Count;
                case FilterTrayKind.Packages: return s_packages.Count;
                case FilterTrayKind.Categories: return s_categories.Count;
                default: return 0;
            }
        }

        public static bool IsActiveForKind(FilterTrayKind kind)
        {
            switch (kind)
            {
                case FilterTrayKind.Type: return s_types.Count > 0;
                case FilterTrayKind.Race: return s_races.Count > 0;
                case FilterTrayKind.School: return s_schools.Count > 0;
                case FilterTrayKind.Rarity: return s_rarities.Count > 0;
                case FilterTrayKind.Packages: return s_packages.Count > 0;
                case FilterTrayKind.Categories: return s_categories.Count > 0;
                default: return false;
            }
        }

        private static bool CardMatchesSelectedRarity(CollectibleCard card, TAG_RARITY? candidate = null, bool rawCandidate = false)
        {
            TAG_RARITY rarity = TAG_RARITY.INVALID;
            try { rarity = card.Rarity; }
            catch
            {
                try
                {
                    EntityDef def = card.GetEntityDef();
                    if (def != null)
                        rarity = (TAG_RARITY)def.GetTag(GAME_TAG.RARITY);
                }
                catch { /* ignore */ }
            }

            if (rarity == TAG_RARITY.INVALID)
                return !rawCandidate && (candidate.HasValue
                    ? s_rarities.MatchesCandidate(candidate.Value, _ => false) : s_rarities.Matches(_ => false));
            // FREE/basic cards count as Common when Common is selected.
            if (rawCandidate && candidate.HasValue) return candidate.Value == rarity
                || (rarity == TAG_RARITY.FREE && candidate.Value == TAG_RARITY.COMMON);
            System.Func<TAG_RARITY, bool> matches = r => r == rarity || (rarity == TAG_RARITY.FREE && r == TAG_RARITY.COMMON);
            return candidate.HasValue ? s_rarities.MatchesCandidate(candidate.Value, matches) : s_rarities.Matches(matches);
        }

        /// <summary>
        /// Race selections use the category mode, including multi-race cards.
        /// TAG_RACE.ALL on the card counts as every race (passes any non-empty selection).
        /// The catalog "All" option matches ALL-type cards; OR can also match another selected race.
        /// </summary>
        private static bool CardMatchesSelectedRaces(CollectibleCard card, EntityDef def, TAG_RACE? candidate = null, bool rawCandidate = false)
        {
            if (s_races.Count == 0 && !candidate.HasValue)
                return true;

            TAG_RACE single = TAG_RACE.INVALID;
            bool hasRace = false;
            List<TAG_RACE> cardRaces = null;
            try
            {
                if (card != null)
                    cardRaces = card.Races;
            }
            catch { /* fall through */ }

            if (cardRaces == null || cardRaces.Count == 0)
            {
                try
                {
                    if (def != null)
                        cardRaces = def.GetRaces();
                }
                catch { /* fall through */ }
            }

            if (cardRaces != null)
            {
                for (int i = 0; i < cardRaces.Count; i++)
                {
                    if (cardRaces[i] != TAG_RACE.INVALID)
                        hasRace = true;
                }
            }

            if (!hasRace)
            {
                try
                {
                    if (def != null)
                    {
                        single = (TAG_RACE)def.GetTag(GAME_TAG.CARDRACE);
                        hasRace = single != TAG_RACE.INVALID;
                    }
                }
                catch { /* ignore */ }
            }

            bool allRaces = single == TAG_RACE.ALL || (cardRaces != null && cardRaces.Contains(TAG_RACE.ALL));
            bool Contains(TAG_RACE required) => hasRace && (allRaces || (required != TAG_RACE.ALL
                && (required == single || (cardRaces != null && cardRaces.Contains(required)))));
            if (rawCandidate && candidate.HasValue) return Contains(candidate.Value);
            System.Func<TAG_RACE, bool> matches = Contains;
            return candidate.HasValue ? s_races.MatchesCandidate(candidate.Value, matches) : s_races.Matches(matches);
        }
    }
}
