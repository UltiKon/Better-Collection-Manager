using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BetterCollectionManager
{
    internal enum KeywordGroup
    {
        Evergreen,
        Expansion,
        Factions,
    }

    /// <summary>
    /// Curated by mechanic, independent of the selected format, localized name,
    /// or number of sets with a reprint. An occasional return (e.g. Adapt or
    /// Magnetic) does not make an expansion mechanic evergreen.
    /// Numeric IDs also support clients whose enum has not named a new tag yet.
    /// Tag IDs: https://github.com/HearthSim/python-hearthstone/blob/master/hearthstone/enums.py
    /// </summary>
    internal static class KeywordGroups
    {
        internal const GAME_TAG TouristTag = (GAME_TAG)3228;
        internal const GAME_TAG FabledTag = (GAME_TAG)4085;
        internal const GAME_TAG QuestTag = (GAME_TAG)462;
        internal const GAME_TAG CastsWhenDrawnTag = (GAME_TAG)1077;

        private static readonly Regex s_parameterSuffix = new Regex(
            @"(?:\s*\(\s*[+−-]?\s*X\s*\)|\s*[:+−-]\s*X|\s+X)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<int> s_evergreen = new HashSet<int>
        {
            189, // Windfury
            190, // Taunt
            191, // Stealth
            192, // Spell Damage
            194, // Divine Shield
            197, // Charge
            208, // Freeze
            215, // Overload
            217, // Deathrattle
            218, // Battlecry
            219, // Secret
            220, // Combo
            240, // Immune
            260, // Frozen
            339, // Silence
            340, // Counter
            363, // Poisonous
            415, // Discover
            419, // Choose Both
            443, // Choose One
            462, // Quest (recurs as a mechanic across expansion cycles)
            685, // Lifesteal
            791, // Rush
            968, // Start of Game
            1077, // Casts When Drawn
            1085, // Reborn
            1207, // Mega-Windfury
            1211, // Elusive
            1333, // Outcast
            1518, // Dormant
            1720, // Tradeable
            2559, // Corpse
            2821, // Overheal
            2934, // Bonus Effect
            3429, // Paladin Aura
            4058, // Corpse Spender
        };

        public static KeywordGroup GetGroup(IReadOnlyList<GAME_TAG> tags)
        {
            bool evergreen = false;
            for (int i = 0; i < tags.Count; i++)
            {
                switch ((int)tags[i])
                {
                    case 482: // Grimy Goons
                    case 483: // Jade Lotus
                    case 484: // Kabal
                    case 3457: // Zerg
                    case 3458: // Terran
                    case 3469: // Protoss
                        return KeywordGroup.Factions;
                }
                evergreen |= s_evergreen.Contains((int)GetFamilyTag(tags[i]));
            }
            return evergreen ? KeywordGroup.Evergreen : KeywordGroup.Expansion;
        }

        public static GAME_TAG GetFamilyTag(GAME_TAG tag)
        {
            int id = (int)tag;
            string key = tag.ToString();
            if (id == (int)TouristTag || (id >= 3597 && id <= 3607)
                || key.Equals("TOURIST", StringComparison.Ordinal)
                || key.EndsWith("_TOURIST", StringComparison.Ordinal))
                return TouristTag;
            if (id == (int)FabledTag || id == 4184
                || key.Equals("FABLED", StringComparison.Ordinal)
                || key.Equals("FABLED_PLUS", StringComparison.Ordinal))
                return FabledTag;
            if (id == (int)QuestTag || id == 1725)
                return QuestTag;
            if (id == (int)CastsWhenDrawnTag || id == 3128)
                return CastsWhenDrawnTag;
            return tag;
        }

        public static string GetDisplayName(GAME_TAG tag, string localizedName)
        {
            GAME_TAG family = GetFamilyTag(tag);
            string displayName = localizedName;
            // Use the base mechanic's localization even if only variants have
            // matching cards. Never strip class names from translated text.
            if (family == TouristTag || family == FabledTag || family == QuestTag
                || family == CastsWhenDrawnTag)
            {
                displayName = family == TouristTag ? "Tourist"
                    : family == FabledTag ? "Fabled"
                    : family == QuestTag ? "Quest" : "Casts When Drawn";
                if (GameStrings.HasKeywordName(family))
                {
                    string name = GameStrings.GetKeywordName(family);
                    if (!string.IsNullOrWhiteSpace(name))
                        displayName = name.Trim();
                }
            }
            return s_parameterSuffix.Replace(displayName, "").Trim();
        }
    }
}
