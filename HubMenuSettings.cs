
namespace BetterCollectionManager
{
    // Separate switches allow a fresh-start A/B comparison without changing
    // collection preferences. Changes to these switches require a restart.
    internal static class HubMenuSettings
    {
        private static bool s_loaded;
        private static PreferenceEntry<bool> s_staticDarkmoon, s_tavernBrawl;
        public static bool StaticDarkmoonButton { get { EnsureLoaded(); return s_staticDarkmoon?.Value ?? false; } }
        public static bool TavernBrawlInMainMenu { get { EnsureLoaded(); return s_tavernBrawl?.Value ?? false; } }

        public static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            try
            {
                var category = LoaderPreferences.CreateCategory("BetterCollectionManagerHub");
                s_staticDarkmoon = category.CreateEntry("StaticDarkmoonButton", true,
                    "Use a static Darkmoon Faire button instead of loading its animated main-menu prefab (restart required)");
                s_tavernBrawl = category.CreateEntry("TavernBrawlInMainMenu", true,
                    "Move Tavern Brawl to the Battlegrounds slot and keep Battlegrounds in Modes (restart required)");
                LoaderPreferences.Load();
            }
            catch (System.Exception ex) { DebugLog.Exception("Hub preferences unavailable", ex); }
        }
    }
}
