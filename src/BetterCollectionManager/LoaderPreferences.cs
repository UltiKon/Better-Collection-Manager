using System;
#if BEPINEX
using BepInEx.Configuration;
#else
using MelonLoader;
#endif

namespace BetterCollectionManager
{
    // Keep preference names and save batching identical across loaders.
    internal static class LoaderPreferences
    {
#if BEPINEX
        internal static ConfigFile Config { get; private set; }
        internal static bool LoadJsonBackups { get; private set; }

        internal static void Initialize(ConfigFile config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            LoadJsonBackups = !System.IO.File.Exists(config.ConfigFilePath);
            Config.SaveOnConfigSet = false;
        }

        internal static PreferenceCategory CreateCategory(string name) => new PreferenceCategory(name);
        // ConfigFile loads at construction; Bind also restores previously unbound entries.
        internal static void Load() { }
        internal static void Save() => Config.Save();
#else
        internal static bool LoadJsonBackups => true;
        internal static PreferenceCategory CreateCategory(string name) =>
            new PreferenceCategory(MelonPreferences.CreateCategory(name));
        internal static void Load() => MelonPreferences.Load();
        internal static void Save() => MelonPreferences.Save();
#endif
    }

    internal sealed class PreferenceCategory
    {
#if BEPINEX
        private readonly string m_name;
        internal PreferenceCategory(string name) { m_name = name; }
        internal PreferenceEntry<T> CreateEntry<T>(string key, T value, string description) =>
            new PreferenceEntry<T>(LoaderPreferences.Config.Bind(m_name, key, value, description));
#else
        private readonly MelonPreferences_Category m_category;
        internal PreferenceCategory(MelonPreferences_Category category) { m_category = category; }
        internal PreferenceEntry<T> CreateEntry<T>(string key, T value, string description) =>
            new PreferenceEntry<T>(m_category.CreateEntry(key, value, description));
#endif
    }

    internal sealed class PreferenceEntry<T>
    {
#if BEPINEX
        private readonly ConfigEntry<T> m_entry;
        internal PreferenceEntry(ConfigEntry<T> entry) { m_entry = entry; }
#else
        private readonly MelonPreferences_Entry<T> m_entry;
        internal PreferenceEntry(MelonPreferences_Entry<T> entry) { m_entry = entry; }
#endif
        internal T Value { get => m_entry.Value; set => m_entry.Value = value; }
    }
}
