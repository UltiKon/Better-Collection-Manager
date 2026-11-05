using MelonLoader;

namespace BetterCollectionManager
{
    public class BetterCollectionManagerMod : MelonMod
    {
        public static MelonLogger.Instance SharedLogger;
        private BetterCollectionManagerRuntime m_runtime;

        public override void OnInitializeMelon()
        {
            SharedLogger = LoggerInstance;
            DebugLog.Logger = LoggerInstance;
            try
            {
                LegacySettings.ImportDirectory(System.IO.Path.GetDirectoryName(typeof(BetterCollectionManagerMod).Assembly.Location));
                LegacySettings.ImportSharedConfig(System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg"));
            }
            catch (System.Exception ex) { DebugLog.Exception("Previous settings import failed", ex); }
            m_runtime = new BetterCollectionManagerRuntime();
            m_runtime.Initialize(HarmonyInstance);
        }

        public override void OnDeinitializeMelon() => m_runtime?.Shutdown();
        public override void OnLateUpdate() => m_runtime?.LateUpdate();
        public override void OnUpdate() => m_runtime?.Update();
    }
}
