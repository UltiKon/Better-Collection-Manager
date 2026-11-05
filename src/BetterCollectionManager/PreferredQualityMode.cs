using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;

namespace BetterCollectionManager
{
    /// <summary>
    /// Tracks whether preferred-quality collapse is active for the Collection Manager session,
    /// and decides when collapse must be skipped (crafting / premium search).
    /// Deck-building / deck-edit in CM stays enabled.
    /// </summary>
    internal static class PreferredQualityMode
    {
        private static bool s_enabled;
        private static bool s_collectionListenerRegistered;
        private static readonly CollectionManager.DelOnCollectionChanged s_onCollectionChanged = OnCollectionChanged;

        private static AccessTools.FieldRef<CollectibleCardFilter, List<CollectibleCardFilter.FilterMask>> s_filterMasks;
        private static AccessTools.FieldRef<CollectibleCardFilter, string> s_filterText;
        private static FieldInfo s_craftableFilterField;

        private static string s_goldenToken;
        private static string s_diamondToken;
        private static string s_signatureToken;
        private static bool s_tokensLoaded;

        public static bool IsEnabled => s_enabled;

        public static void InitFieldAccess()
        {
            try
            {
                s_filterMasks = AccessTools.FieldRefAccess<CollectibleCardFilter, List<CollectibleCardFilter.FilterMask>>("m_filterMasks");
                s_filterText = AccessTools.FieldRefAccess<CollectibleCardFilter, string>("m_filterText");
                s_craftableFilterField = AccessTools.Field(typeof(CollectibleCardFilter), "m_craftableFilterValue");
                DebugLog.Msg("Field accessors ready (m_filterMasks, m_filterText, m_craftableFilterValue)");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("InitFieldAccess failed", ex);
            }
        }

        public static void Enable(string reason)
        {
            if (s_enabled)
            {
                DebugLog.Msg($"Preferred mode already ON (re-enable ignored: {reason})");
                return;
            }

            s_enabled = true;
            DebugLog.Msg($"Preferred mode ON ({reason})");
            RegisterCollectionListener();
        }

        public static void Disable(string reason)
        {
            if (!s_enabled)
            {
                DebugLog.Msg($"Preferred mode already OFF (disable ignored: {reason})");
                return;
            }

            s_enabled = false;
            InspectCollectionRefresh.Reset();
            QualityDeckInput.ResetSession();
            MixedPremiumCard.ResetSession();
            DebugLog.Msg($"Preferred mode OFF ({reason})");
            UnregisterCollectionListener();
        }

        public enum SkipReason
        {
            None,
            ModeOff,
            UserDisabled,
            CraftingTray,
            CraftingManagerMode,
            CraftabilityFilter,
            CraftingFilterMasks,
            PremiumSearchToken,
        }

        /// <summary>
        /// Returns whether collapse should run for this UpdateResults call.
        /// </summary>
        public static bool ShouldCollapse(CollectibleCardClassFilter filter, out SkipReason reason, out string detail)
        {
            reason = SkipReason.None;
            detail = null;

            if (!s_enabled)
            {
                reason = SkipReason.ModeOff;
                detail = "preferred mode is off (not in CM session)";
                return false;
            }

            PreferredPremiumPrefs.EnsureLoaded();
            if (!PreferredPremiumPrefs.CollapseEnabled)
            {
                reason = SkipReason.UserDisabled;
                detail = "CollapseEnabled=false (Quality tray toggle)";
                return false;
            }

            try
            {
                CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
                bool collapseCrafting = CollectionQualitySettings.CollapseInCrafting;
                if (!collapseCrafting && display != null && display.InCraftingMode())
                {
                    reason = SkipReason.CraftingTray;
                    detail = "crafting tray is shown (InCraftingMode)";
                    return false;
                }

                if (!collapseCrafting && CraftingManager.GetIsInCraftingMode())
                {
                    reason = SkipReason.CraftingManagerMode;
                    detail = "CraftingManager.GetIsInCraftingMode()";
                    return false;
                }

                if (!collapseCrafting && filter != null && s_craftableFilterField != null)
                {
                    bool? craftable = (bool?)s_craftableFilterField.GetValue(filter);
                    if (craftable.HasValue)
                    {
                        reason = SkipReason.CraftabilityFilter;
                        detail = $"m_craftableFilterValue={craftable.Value}";
                        return false;
                    }
                }

                if (!collapseCrafting && filter != null && IsCraftingStyleFilterMask(filter, out string maskDetail))
                {
                    reason = SkipReason.CraftingFilterMasks;
                    detail = maskDetail;
                    return false;
                }

                if (filter != null && HasPremiumSearchToken(filter, out string tokenDetail))
                {
                    reason = SkipReason.PremiumSearchToken;
                    detail = tokenDetail;
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("ShouldCollapse check failed; allowing collapse", ex);
            }

            return true;
        }

        private static bool IsCraftingStyleFilterMask(CollectibleCardFilter filter, out string detail)
        {
            detail = null;
            if (s_filterMasks == null)
                return false;

            List<CollectibleCardFilter.FilterMask> masks = s_filterMasks(filter);
            if (masks == null || masks.Count == 0)
                return false;

            // Normal browsing uses ALL (or a single ALL). Crafting tray builds OWNED/UNOWNED
            // combinations per premium — detect those.
            bool hasOwnedSplit = false;
            bool hasUnownedSplit = false;
            bool onlyAll = true;

            for (int i = 0; i < masks.Count; i++)
            {
                CollectibleCardFilter.FilterMask mask = masks[i];
                if (mask != CollectibleCardFilter.FilterMask.ALL)
                    onlyAll = false;

                if ((mask & CollectibleCardFilter.FilterMask.OWNED) != 0
                    && (mask & CollectibleCardFilter.FilterMask.PREMIUM_ALL) != 0)
                    hasOwnedSplit = true;

                if ((mask & CollectibleCardFilter.FilterMask.UNOWNED) != 0
                    && (mask & CollectibleCardFilter.FilterMask.PREMIUM_ALL) != 0)
                    hasUnownedSplit = true;
            }

            if (onlyAll)
                return false;

            if (hasOwnedSplit || hasUnownedSplit)
            {
                detail = $"FilterByMask crafting-style masks (count={masks.Count}, ownedSplit={hasOwnedSplit}, unownedSplit={hasUnownedSplit})";
                return true;
            }

            return false;
        }

        private static bool HasPremiumSearchToken(CollectibleCardFilter filter, out string detail)
        {
            detail = null;
            if (s_filterText == null)
                return false;

            string text = s_filterText(filter);
            if (string.IsNullOrEmpty(text))
                return false;

            EnsureSearchTokens();

            string lower = text.ToLowerInvariant();
            if (!string.IsNullOrEmpty(s_goldenToken) && ContainsToken(lower, s_goldenToken))
            {
                detail = $"search contains golden token '{s_goldenToken}' (text='{text}')";
                return true;
            }
            if (!string.IsNullOrEmpty(s_diamondToken) && ContainsToken(lower, s_diamondToken))
            {
                detail = $"search contains diamond token '{s_diamondToken}' (text='{text}')";
                return true;
            }
            if (!string.IsNullOrEmpty(s_signatureToken) && ContainsToken(lower, s_signatureToken))
            {
                detail = $"search contains signature token '{s_signatureToken}' (text='{text}')";
                return true;
            }

            return false;
        }

        private static void EnsureSearchTokens()
        {
            if (s_tokensLoaded)
                return;

            try
            {
                s_goldenToken = SafeGameString("GLUE_COLLECTION_MANAGER_SEARCH_GOLDEN");
                s_diamondToken = SafeGameString("GLUE_COLLECTION_MANAGER_SEARCH_DIAMOND");
                s_signatureToken = SafeGameString("GLUE_COLLECTION_MANAGER_SEARCH_SIGNATURE");
                s_tokensLoaded = true;
                DebugLog.Msg($"Premium search tokens: golden='{s_goldenToken}', diamond='{s_diamondToken}', signature='{s_signatureToken}'");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("EnsureSearchTokens failed", ex);
                // English fallbacks
                s_goldenToken = "golden";
                s_diamondToken = "diamond";
                s_signatureToken = "signature";
                s_tokensLoaded = true;
            }
        }

        private static string SafeGameString(string key)
        {
            string value = GameStrings.Get(key);
            return string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();
        }

        private static bool ContainsToken(string haystackLower, string tokenLower)
        {
            if (string.IsNullOrEmpty(tokenLower))
                return false;

            // Match as a whole search token (whitespace / punctuation separated), not as a substring of a card name.
            int index = 0;
            while (index < haystackLower.Length)
            {
                int found = CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                    haystackLower, tokenLower, index, CompareOptions.Ordinal);
                if (found < 0)
                    return false;

                bool startOk = found == 0 || IsTokenBoundary(haystackLower[found - 1]);
                int end = found + tokenLower.Length;
                bool endOk = end >= haystackLower.Length || IsTokenBoundary(haystackLower[end]);
                if (startOk && endOk)
                    return true;

                index = found + 1;
            }
            return false;
        }

        private static bool IsTokenBoundary(char c)
        {
            return char.IsWhiteSpace(c) || c == ':' || c == ',' || c == ';' || c == '|' || c == '/';
        }

        private static void RegisterCollectionListener()
        {
            if (s_collectionListenerRegistered)
                return;

            try
            {
                CollectionManager cm = CollectionManager.Get();
                if (cm == null)
                {
                    DebugLog.Warning("RegisterCollectionListener: CollectionManager.Get() is null");
                    return;
                }

                cm.RegisterCollectionChangedListener(s_onCollectionChanged);
                s_collectionListenerRegistered = true;
                DebugLog.Msg("Registered CollectionChanged listener (refresh page on collection change)");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("RegisterCollectionListener failed", ex);
            }
        }

        private static void UnregisterCollectionListener()
        {
            if (!s_collectionListenerRegistered)
                return;

            try
            {
                CollectionManager cm = CollectionManager.Get();
                cm?.RemoveCollectionChangedListener(s_onCollectionChanged);
                s_collectionListenerRegistered = false;
                DebugLog.Msg("Unregistered CollectionChanged listener");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("UnregisterCollectionListener failed", ex);
            }
        }

        private static void OnCollectionChanged()
        {
            if (!s_enabled)
                return;

            try
            {
                InspectCollectionRefresh.BeginCollectionRefresh();
                DebugLog.Msg("Collection changed while preferred mode ON -> refreshing filtered cards / current page");
                CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
                if (display == null)
                {
                    DebugLog.Warning("OnCollectionChanged: CollectibleDisplay is null");
                    return;
                }

                CollectiblePageManager pageManager = display.GetPageManager();
                if (pageManager == null)
                {
                    DebugLog.Warning("OnCollectionChanged: page manager is null");
                    return;
                }

                pageManager.RefreshCurrentPageContents();
                DebugLog.Msg("Page refresh triggered (RefreshCurrentPageContents)");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("OnCollectionChanged refresh failed", ex);
            }
            finally { InspectCollectionRefresh.EndCollectionRefresh(); }
        }
    }
}
