using System;
using System.Collections.Generic;
using System.Reflection;
using BetterCollectionManager;

internal static class Program
{
    private static void Main()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); checks++; }
        PreferredQualityMode.InitFieldAccess();
        PreferredQualityMode.Enable("fixture");
        foreach (bool tray in new[] { false, true })
        foreach (bool inspecting in new[] { false, true })
        foreach (bool? craftable in new bool?[] { null, false, true })
        foreach (bool masks in new[] { false, true })
        {
            CollectionManager.Get().Display.Crafting = tray;
            CraftingManager.Inspecting = inspecting;
            var filter = new CollectibleCardClassFilter { m_craftableFilterValue = craftable };
            if (masks) filter.m_filterMasks = new List<CollectibleCardFilter.FilterMask>
                { CollectibleCardFilter.FilterMask.PREMIUM_ALL | CollectibleCardFilter.FilterMask.UNOWNED };
            bool craftingContext = tray || inspecting || craftable.HasValue || masks;
            CollectionQualitySettings.CollapseInCrafting = false;
            Check(PreferredQualityMode.ShouldCollapse(filter, out _, out _) == !craftingContext,
                "Default behavior preserves every native crafting bypass");
            CollectionQualitySettings.CollapseInCrafting = true;
            Check(PreferredQualityMode.ShouldCollapse(filter, out _, out _),
                "Opt-in collapse handles native crafting masks, craftability, tray and inspection");
            foreach (string text in new[] { "golden", "diamond", "signature" })
            {
                filter.m_filterText = text;
                Check(!PreferredQualityMode.ShouldCollapse(filter, out var reason, out _) && reason == PreferredQualityMode.SkipReason.PremiumSearchToken,
                    "An explicit premium search keeps its native quality variants even with crafting collapse enabled");
            }
            filter.m_filterText = "";
            PreferredPremiumPrefs.CollapseEnabled = false;
            Check(!PreferredQualityMode.ShouldCollapse(filter, out var disabled, out _) && disabled == PreferredQualityMode.SkipReason.UserDisabled,
                "Crafting opt-in cannot override the global collapse switch");
            PreferredPremiumPrefs.CollapseEnabled = true;
        }
        PreferredQualityMode.Disable("fixture exit");
        Check(!PreferredQualityMode.ShouldCollapse(new CollectibleCardClassFilter(), out var off, out _) && off == PreferredQualityMode.SkipReason.ModeOff,
            "No collapsing occurs outside a collection session");
        Console.WriteLine("Passed " + checks + " production collapse-mode checks.");
    }
}

public class CollectibleCardFilter
{
    [Flags] public enum FilterMask { ALL = -1, OWNED = 1, UNOWNED = 2, PREMIUM_ALL = 12 }
    public List<FilterMask> m_filterMasks = new List<FilterMask> { FilterMask.ALL };
    public string m_filterText;
    public bool? m_craftableFilterValue;
}
public sealed class CollectibleCardClassFilter : CollectibleCardFilter { }
public sealed class CollectionManager
{
    public delegate void DelOnCollectionChanged();
    private static readonly CollectionManager Instance = new CollectionManager();
    public readonly CollectibleDisplay Display = new CollectibleDisplay();
    public static CollectionManager Get() => Instance;
    public CollectibleDisplay GetCollectibleDisplay() => Display;
    public void RegisterCollectionChangedListener(DelOnCollectionChanged listener) { }
    public void RemoveCollectionChangedListener(DelOnCollectionChanged listener) { }
}
public sealed class CollectibleDisplay
{
    public bool Crafting;
    public bool InCraftingMode() => Crafting;
    public CollectiblePageManager GetPageManager() => new CollectiblePageManager();
}
public sealed class CollectiblePageManager { public void RefreshCurrentPageContents() { } }
public static class CraftingManager { public static bool Inspecting; public static bool GetIsInCraftingMode() => Inspecting; }
public static class GameStrings { public static string Get(string key) => key.Substring(key.LastIndexOf('_') + 1).ToLowerInvariant(); }

namespace BetterCollectionManager
{
    internal static class CollectionQualitySettings { public static bool CollapseInCrafting; }
    internal static class PreferredPremiumPrefs { public static bool CollapseEnabled = true; public static void EnsureLoaded() { } }
    internal static class InspectCollectionRefresh
    { public static void Reset() { } public static void BeginCollectionRefresh() { } public static void EndCollectionRefresh() { } }
    internal static class QualityDeckInput { public static void ResetSession() { } }
    internal static class MixedPremiumCard { public static void ResetSession() { } }
    internal static class DebugLog
    {
        public static void Msg(string text) { }
        public static void Warning(string text) { }
        public static void Exception(string text, Exception error) => throw new InvalidOperationException(text, error);
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public delegate ref TValue FieldRef<T, TValue>(T instance);
        private static class Slot<T, TValue> { internal static TValue Value; }
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        public static FieldRef<T, TValue> FieldRefAccess<T, TValue>(string name)
        {
            FieldInfo field = Field(typeof(T), name);
            return (T instance) => { Slot<T, TValue>.Value = (TValue)field.GetValue(instance); return ref Slot<T, TValue>.Value; };
        }
    }
}
