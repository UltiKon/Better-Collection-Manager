using System;
using BepInEx;
using HarmonyLib;

namespace BetterCollectionManager
{
    [BepInPlugin(PluginGuid, ModMetadata.Name, ModMetadata.Version)]
    public sealed class BetterCollectionManagerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = ModMetadata.Guid;
        private BetterCollectionManagerRuntime m_runtime;
        private Harmony m_harmony;
        private bool m_stopped;

        private void Awake()
        {
            DebugLog.Logger = Logger;
            try
            {
                if (LegacySettings.ImportStandaloneConfig(Config.ConfigFilePath)) Config.Reload();
                LegacySettings.ImportDirectory(Paths.ConfigPath);
            }
            catch (Exception ex) { DebugLog.Exception("Previous settings import failed", ex); }
            LoaderPreferences.Initialize(Config);
            ModMetadata.BindFirestoneMetadata(Config);
            LoaderCoroutines.Host = this;
            m_harmony = new Harmony(PluginGuid);
            m_runtime = new BetterCollectionManagerRuntime();
            try
            {
                m_runtime.Initialize(m_harmony);
                LoaderPreferences.Save();
            }
            catch (Exception ex)
            {
                DebugLog.Exception("Plugin initialization failed", ex);
                Shutdown();
                enabled = false;
            }
        }

        private void Update() { if (!m_stopped) m_runtime?.Update(); }
        private void LateUpdate() { if (!m_stopped) m_runtime?.LateUpdate(); }
        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            if (m_stopped) return;
            m_stopped = true;
            try { m_runtime?.Shutdown(); }
            finally
            {
                StopAllCoroutines();
                LoaderCoroutines.Host = null;
                m_harmony?.UnpatchSelf();
            }
        }
    }
}
