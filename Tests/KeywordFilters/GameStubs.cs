// Minimal game boundaries for exercising the real catalog and selection code.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace PegasusShared
{
    public enum FormatType { FT_UNKNOWN, FT_STANDARD, FT_WILD }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref TValue FieldRef<T, TValue>(T instance);
        public static FieldRef<T, TValue> FieldRefAccess<T, TValue>(string name)
        {
            FieldInfo field = typeof(T).GetField(name);
            // A shared ref slot is sufficient: these tests read the fields only.
            return (T instance) =>
            {
                Slot<T, TValue>.Value = (TValue)field.GetValue(instance);
                return ref Slot<T, TValue>.Value;
            };
        }
        private static class Slot<T, TValue> { public static TValue Value; }
    }
}

public enum GAME_TAG
{
    CARDRACE = 20001, SPELL_SCHOOL = 20002, RARITY = 20003, PREPARE = 4354, DECK_ACTION_COST = 1743,
    PREPARE_ALIAS = 7001, PREPARE_REF_ALIAS = 7002, ROGUE_TOURIST = 7003, TAUNT = 190
}
public enum TAG_CARD_SET { JAIL = 1988, CORE = 1637 }
public enum TAG_CARDTYPE
{
    INVALID, HERO, WEAPON, LOCATION,
    MINION, SPELL, ENCHANTMENT, GAME_MODE_BUTTON, MOVE_MINION_HOVER_TARGET,
    LETTUCE_ABILITY, BATTLEGROUND_HERO_BUDDY, BATTLEGROUND_QUEST_REWARD,
    BATTLEGROUND_ANOMALY, BATTLEGROUND_SPELL, BATTLEGROUND_TRINKET, HERO_POWER
}

public enum TAG_RACE { INVALID, ALL, BEAST, DRAGON, MURLOC }
public enum TAG_SPELL_SCHOOL { NONE, FIRE, FROST, NATURE }
public enum TAG_RARITY { INVALID, FREE, COMMON, RARE, EPIC, LEGENDARY }
public sealed class EntityDef
{
    public TAG_CARDTYPE CardType = TAG_CARDTYPE.MINION;
    public TAG_RACE Race;
    public List<TAG_RACE> Races = new List<TAG_RACE>();
    public TAG_SPELL_SCHOOL School;
    public TAG_CARDTYPE GetCardType() => CardType;
    public List<TAG_RACE> GetRaces() => Races;
    public readonly HashSet<GAME_TAG> Tags = new HashSet<GAME_TAG>();
    public readonly HashSet<GAME_TAG> References = new HashSet<GAME_TAG>();
    public bool HasTag(GAME_TAG tag) => Tags.Contains(tag);
    public bool HasReferencedTag(GAME_TAG tag) => References.Contains(tag);
    public int GetTag(GAME_TAG tag) => tag == GAME_TAG.CARDRACE ? (int)Race
        : tag == GAME_TAG.SPELL_SCHOOL ? (int)School : Tags.Contains(tag) ? 1 : 0;
}

public sealed class CollectibleCard
{
    public static int EntityReads;
    public TAG_RARITY Rarity;
    public List<TAG_RACE> Races => Definition?.Races;
    public string CardId;
    public bool IsHeroSkin;
    public bool IsMercenaryAbility;
    public TAG_CARDTYPE CardType = TAG_CARDTYPE.MINION;
    public TAG_CARD_SET Set = TAG_CARD_SET.JAIL;
    public EntityDef Definition = new EntityDef();
    public EntityDef GetEntityDef() { EntityReads++; return Definition; }
}

public class CollectibleDisplay { public SetFilterTray m_setFilterTray; }
public sealed class CollectionManagerDisplay : CollectibleDisplay
{
    public PegasusShared.FormatType CurrentSetFilterFormatType = PegasusShared.FormatType.FT_STANDARD;
}
public sealed class SetFilterTray { public SetFilterItem m_selected; }
public sealed class SetFilterItem
{
    public List<int> SpecificCards { get; set; }
    public PegasusShared.FormatType FormatType = PegasusShared.FormatType.FT_UNKNOWN;
    public List<TAG_CARD_SET> CardSets;
    public bool IsAllStandard;
    public string Text;
}
public sealed class CollectionManager
{
    public static CollectionManager Instance = new CollectionManager();
    public readonly List<CollectibleCard> Cards = new List<CollectibleCard>();
    public readonly CollectionManagerDisplay Display = new CollectionManagerDisplay();
    public static CollectionManager Get() => Instance;
    public CollectibleDisplay GetCollectibleDisplay() => Display;
    public List<CollectibleCard> GetAllCards() => Cards;
}
public static class GameUtils
{
    public static string GetCardIdFromDbfId(int id) => "dbf-" + id;
    public static TAG_CARD_SET[] GetStandardSets() => new[] { TAG_CARD_SET.JAIL, TAG_CARD_SET.CORE };
    public static TAG_CARD_SET[] GetCardSetsInFormat(PegasusShared.FormatType format) => GetStandardSets();
    public static bool IsCardValidForFormat(PegasusShared.FormatType format, EntityDef def) => true;
}
public sealed class KeywordTextDbfRecord { public int Tag; }
public sealed class KeywordTable
{
    public readonly List<KeywordTextDbfRecord> Records = new List<KeywordTextDbfRecord>();
    public List<KeywordTextDbfRecord> GetRecords() => Records;
}
public static class GameDbf { public static KeywordTable KeywordText = new KeywordTable(); }
public static class GameStrings
{
    public static readonly Dictionary<GAME_TAG, string> Names = new Dictionary<GAME_TAG, string>();
    public static bool HasKeywordName(GAME_TAG tag) => Names.ContainsKey(tag);
    public static string GetKeywordName(GAME_TAG tag) => Names[tag];
}
namespace BetterCollectionManager
{
    internal static class DebugLog
    {
        public static void Msg(string text) { }
        public static void Warning(string text) { }
        public static void Exception(string text, Exception ex) => throw new Exception(text, ex);
    }
}

namespace BetterCollectionManager
{
    internal static class CollectionQualitySettings
    {
        public static bool RememberFilterModes;
        public static bool HideEmptyFilters;
        public static FilterModeBehavior FilterModeBehavior = FilterModeBehavior.Category;
        public static string SavedFilterModes = "";
    }
}
