using System;
using System.Collections.Generic;

public enum TAG_PREMIUM { NORMAL, GOLDEN, DIAMOND, SIGNATURE }
public enum TAG_RARITY { COMMON, LEGENDARY }
public enum GAME_TAG { RARITY }
public sealed class EntityDef { public int GetTag(GAME_TAG tag) => 0; }
public sealed class CollectibleCard
{
    public string CardId;
    public TAG_PREMIUM PremiumType;
    public int OwnedCount;
    public TAG_RARITY Rarity;
    public EntityDef GetEntityDef() => null;
}
public sealed class CollectionManager
{
    public static CollectionManager Get() => null;
    public int GetOwnedCount(string id, TAG_PREMIUM premium) => 0;
}
namespace UnityEngine
{
    public static class Time { public static int frameCount = 1; public static float unscaledTime = 1f; }
}
namespace BetterCollectionManager
{
    internal enum CollectionOpenSearchMode { Off, New, Refund, NewOrRefund }
    internal enum FilterModeBehavior { Hidden, Category, Expression }
    internal static class FilterModes { public static string Serialize() => ""; }
    internal static class DisenchantChoiceMenu { public static void DestroyMenu() { } }
    internal static class DebugLog
    {
        public static void Msg(string text) { }
        public static void Exception(string context, Exception exception) => throw new Exception(context, exception);
    }
}
namespace MelonLoader
{
    public sealed class MelonPreferences_Entry<T> { public T Value; }
    public sealed class MelonPreferences_Category
    {
        public readonly Dictionary<string, object> Entries = new Dictionary<string, object>();
        public MelonPreferences_Entry<T> CreateEntry<T>(string key, T value, string description)
        {
            if (!Entries.TryGetValue(key, out object entry))
                Entries[key] = entry = new MelonPreferences_Entry<T> { Value = value };
            return (MelonPreferences_Entry<T>)entry;
        }
    }
    public static class MelonPreferences
    {
        public static readonly Dictionary<string, MelonPreferences_Category> Categories = new Dictionary<string, MelonPreferences_Category>();
        public static MelonPreferences_Category CreateCategory(string name)
        {
            if (!Categories.TryGetValue(name, out var category))
                Categories[name] = category = new MelonPreferences_Category();
            return category;
        }
        public static void Load() { }
        public static void Save() { }
    }
}
