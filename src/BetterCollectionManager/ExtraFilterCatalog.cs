using System.Collections.Generic;

namespace BetterCollectionManager
{
    internal static class ExtraFilterCatalog
    {
        public readonly struct TypeEntry
        {
            public readonly TAG_CARDTYPE Type;
            public readonly string Name;
            public TypeEntry(TAG_CARDTYPE type, string name) { Type = type; Name = name; }
        }

        public readonly struct RaceEntry
        {
            public readonly TAG_RACE Race;
            public readonly string Name;
            public RaceEntry(TAG_RACE race, string name) { Race = race; Name = name; }
        }

        public readonly struct SchoolEntry
        {
            public readonly TAG_SPELL_SCHOOL School;
            public readonly string Name;
            public SchoolEntry(TAG_SPELL_SCHOOL school, string name) { School = school; Name = name; }
        }

        public readonly struct RarityEntry
        {
            public readonly TAG_RARITY Rarity;
            public readonly string Name;
            public RarityEntry(TAG_RARITY rarity, string name) { Rarity = rarity; Name = name; }
        }

        public static readonly TypeEntry[] Types =
        {
            new TypeEntry(TAG_CARDTYPE.MINION, "Minion"),
            new TypeEntry(TAG_CARDTYPE.SPELL, "Spell"),
            new TypeEntry(TAG_CARDTYPE.WEAPON, "Weapon"),
            new TypeEntry(TAG_CARDTYPE.HERO, "Hero"),
            new TypeEntry(TAG_CARDTYPE.LOCATION, "Location"),
            // Hero Power excluded — not a collectible CM filter.
        };

        public static readonly RaceEntry[] Races =
        {
            new RaceEntry(TAG_RACE.MURLOC, "Murloc"),
            new RaceEntry(TAG_RACE.DEMON, "Demon"),
            new RaceEntry(TAG_RACE.MECHANICAL, "Mech"),
            new RaceEntry(TAG_RACE.ELEMENTAL, "Elemental"),
            new RaceEntry(TAG_RACE.BEAST, "Beast"),
            new RaceEntry(TAG_RACE.TOTEM, "Totem"),
            new RaceEntry(TAG_RACE.PIRATE, "Pirate"),
            new RaceEntry(TAG_RACE.DRAGON, "Dragon"),
            new RaceEntry(TAG_RACE.ALL, "All"),
            new RaceEntry(TAG_RACE.QUILBOAR, "Quilboar"),
            new RaceEntry(TAG_RACE.NAGA, "Naga"),
            new RaceEntry(TAG_RACE.UNDEAD, "Undead"),
            new RaceEntry(TAG_RACE.DRAENEI, "Draenei"),
        };

        public static readonly SchoolEntry[] Schools =
        {
            new SchoolEntry(TAG_SPELL_SCHOOL.ARCANE, "Arcane"),
            new SchoolEntry(TAG_SPELL_SCHOOL.FIRE, "Fire"),
            new SchoolEntry(TAG_SPELL_SCHOOL.FROST, "Frost"),
            new SchoolEntry(TAG_SPELL_SCHOOL.NATURE, "Nature"),
            new SchoolEntry(TAG_SPELL_SCHOOL.HOLY, "Holy"),
            new SchoolEntry(TAG_SPELL_SCHOOL.SHADOW, "Shadow"),
            new SchoolEntry(TAG_SPELL_SCHOOL.FEL, "Fel"),
        };

        /// <summary>Highest → lowest rarity (not A–Z).</summary>
        public static readonly RarityEntry[] Rarities =
        {
            new RarityEntry(TAG_RARITY.LEGENDARY, "Legendary"),
            new RarityEntry(TAG_RARITY.EPIC, "Epic"),
            new RarityEntry(TAG_RARITY.RARE, "Rare"),
            new RarityEntry(TAG_RARITY.COMMON, "Common"),
        };
    }
}
