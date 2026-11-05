using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BetterCollectionManager
{
    // Native New matches otherwise disappear as the displayed cards are marked seen.
    // Retain membership only while generating this exact collection query; glows and
    // server acknowledgements continue to use the real seen state.
    internal static class StableNewSearch
    {
        private static bool s_installed;
        private static CollectibleCardFilter s_filter;
        private static object[] s_query;
        private static readonly Dictionary<string, HashSet<TAG_PREMIUM>> s_members = new Dictionary<string, HashSet<TAG_PREMIUM>>(StringComparer.Ordinal);
        private static readonly FieldInfo[] QueryFields = typeof(CollectibleCardFilter)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => !field.Name.Contains("FindCardsResult")).ToArray();
        [ThreadStatic] private static bool s_generating;
        [ThreadStatic] private static bool s_probing;
        internal static T WithoutSnapshot<T>(Func<T> action)
        {
            bool previous = s_probing; s_probing = true;
            try { return action(); }
            finally { s_probing = previous; }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (s_installed) return;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(CollectibleCardFilter), "GenerateResults"), prefix: Hook(nameof(Begin)), finalizer: Hook(nameof(End)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(CollectibleCard), "IsNewCollectible"), postfix: Hook(nameof(IsNewPostfix)));
                harmony.Patch(AccessTools.Method(typeof(CollectibleCardFilter), "FilterSearchText"), postfix: Hook(nameof(SearchChanged)));
                s_installed = true;
            }
            catch (Exception ex) { DebugLog.Exception("Stable New search unavailable", ex); }
        }
        private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(StableNewSearch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        internal static string Token(string key, string fallback)
        {
            string value = GameStrings.Get(key);
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        internal static bool HasNew(string text) => !string.IsNullOrEmpty(text) && text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => string.Equals(word, Token("GLUE_COLLECTION_MANAGER_SEARCH_NEW", "new"), StringComparison.OrdinalIgnoreCase));
        internal static CollectibleCardFilter CurrentFilter()
        {
            object pages = CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager();
            return pages == null ? null : AccessTools.Field(pages.GetType(), "m_cardsCollection")?.GetValue(pages) as CollectibleCardFilter;
        }
        internal static string SearchText(CollectibleCardFilter filter)
            => filter == null ? null : AccessTools.Field(typeof(CollectibleCardFilter), "m_filterText")?.GetValue(filter) as string;
        internal static void Reset() { s_filter = null; s_query = null; s_members.Clear(); }
        private static void SearchChanged(CollectibleCardFilter __instance) { if (ReferenceEquals(__instance, s_filter)) Reset(); }
        private static void Begin(CollectibleCardFilter __instance, out bool __state)
        {
            __state = s_generating;
            s_generating = false;
            if (s_probing) return;
            if (!PreferredQualityMode.IsEnabled || !PreferredPremiumPrefs.CollapseEnabled
                || !ReferenceEquals(__instance, CurrentFilter()) || !HasNew(SearchText(__instance))
                || !(__instance is CollectibleCardClassFilter cards) || !PreferredQualityMode.ShouldCollapse(cards, out var _, out var _))
            { if (ReferenceEquals(__instance, s_filter)) Reset(); return; }
            object[] query = QueryFields.Select(field => Copy(field.GetValue(__instance))).ToArray();
            if (!ReferenceEquals(s_filter, __instance) || s_query == null || !Same(s_query, query))
            { Reset(); s_filter = __instance; s_query = query; }
            s_generating = true;
        }
        private static Exception End(Exception __exception, bool __state)
        { s_generating = __state; if (__exception != null) Reset(); return __exception; }
        private static void IsNewPostfix(CollectibleCard __instance, ref bool __result)
        {
            if (!s_generating || __instance == null || __instance.OwnedCount <= 0) return;
            if (__result)
            {
                if (!s_members.TryGetValue(__instance.CardId, out var premiums))
                    s_members.Add(__instance.CardId, premiums = new HashSet<TAG_PREMIUM>());
                premiums.Add(__instance.PremiumType);
            }
            else if (s_members.TryGetValue(__instance.CardId, out var premiums) && premiums.Contains(__instance.PremiumType)) __result = true;
        }
        private static object Copy(object value)
        {
            if (value is string || !(value is IEnumerable items)) return value;
            return items.Cast<object>().Select(Copy).ToArray();
        }
        private static bool Same(object left, object right)
        {
            if (left is object[] a && right is object[] b)
                return a.Length == b.Length && a.Zip(b, Same).All(value => value);
            return Equals(left, right);
        }
    }
}
