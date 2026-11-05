using System;
using System.Reflection;
using HarmonyLib;
using BetterCollectionManager.Patches;

namespace BetterCollectionManager
{
    internal sealed class BetterCollectionManagerRuntime
    {
        private HarmonyLib.Harmony HarmonyInstance;
        private object m_startupCoroutine;
        private static BetterCollectionManagerRuntime s_instance;
        private bool m_collectionInitialized, m_startupWaitScheduled;

        public void Initialize(HarmonyLib.Harmony harmony)
        {
            HarmonyInstance = harmony;
            s_instance = this;

            DebugLog.Msg("Better Collection Manager loading...");
            DebugLog.Msg($"VerboseDebug={DebugLog.VerboseDebug}");

            PreferredPremiumPrefs.EnsureLoaded();
            CollectionQualitySettings.EnsureLoaded();
            HarmonyInstance.PatchAll(typeof(CollectionStartPatch));
            SplashScreen.SplashScreenShown += OnStartupImageShown;
            DebugLog.Msg("Waiting for the native startup image before installing collection hooks.");
        }

        [HarmonyPatch(typeof(CollectionManagerDisplay), "Start")]
        private static class CollectionStartPatch
        {
            // Install before native Start builds its first collection page.
            [HarmonyPrefix]
            public static void Prefix()
            {
                if (s_instance == null) return;
                s_instance.InitializeCollection();
                // Inventory changes also run during login/bootstrap. The
                // inspect-only transaction hooks are needed only once the
                // native collection page itself is being constructed.
                InspectCollectionRefresh.Install(s_instance.HarmonyInstance);
                DisenchantChoices.Install(s_instance.HarmonyInstance);
                StableNewSearch.Install(s_instance.HarmonyInstance);
            }
        }

        private void OnStartupImageShown(SplashScreen screen)
        {
            if (m_collectionInitialized || m_startupWaitScheduled) return;
            m_startupWaitScheduled = true;
            SplashScreen.SplashScreenShown -= OnStartupImageShown;
            m_startupCoroutine = LoaderCoroutines.Start(InitializeAfterStartupImage());
        }

        private System.Collections.IEnumerator InitializeAfterStartupImage()
        {
            // Native Show fades the startup artwork in over 0.25 seconds.
            // Wait for that fade and a presented frame before doing hook work.
            yield return new UnityEngine.WaitForSecondsRealtime(0.3f);
            yield return new UnityEngine.WaitForEndOfFrame();
            yield return null;
            InitializeCollection();
        }

        private void InitializeCollection()
        {
            if (m_collectionInitialized) return;
            m_collectionInitialized = true;
            SplashScreen.SplashScreenShown -= OnStartupImageShown;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                PreferredQualityMode.InitFieldAccess();
                CollectionQualitySettings.ChangedKind += SettingsChanged;
                FilterAvailability.Changed += KeywordSetTray.SettingsChanged;
                QualityDeckInput.Initialize();
                CollectibleCardClassFilter_UpdateResults_Patch.EnsureFields();
                KeywordCatalog.EnsureFields();
                MixedPremiumPatches.Install(HarmonyInstance);
                CollectionClickInspectPatches.Install(HarmonyInstance);
                QualityDeckInputPatches.Install(HarmonyInstance);
                ModsSettingsPatches.Install(HarmonyInstance);
                DropdownReadabilityPatches.Install(HarmonyInstance);
                FilterButtonFeedback.InstallSoundGuard(HarmonyInstance);
                CollectionPageDiagnostics.Install(HarmonyInstance);
                CraftingPageAnchor.Install(HarmonyInstance);
                ComboCollectionCards.Install(HarmonyInstance);
                ComboPopup.Install(HarmonyInstance);
                // DFT and Battlegrounds/Tavern Brawl experiments disabled at user request.
                // HubMenuPatches.Install(HarmonyInstance);
                HarmonyInstance.PatchAll(typeof(CollectibleCardClassFilter_UpdateResults_Patch));
                HarmonyInstance.PatchAll(typeof(CollectionManagerDisplayPatches));
                HarmonyInstance.PatchAll(typeof(CollectionThemePatches));
                foreach (Type nested in typeof(CollectionManagerDisplayPatches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                    HarmonyInstance.PatchAll(nested);
                DebugLog.Msg($"Collection hooks ready after startup image; initialization took {timer.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("Collection initialization failed", ex);
            }
        }

        private static void SettingsChanged(QualitySettingsChange changes)
        {
            InspectCollectionRefresh.Reset();
            if ((changes & (QualitySettingsChange.CardDisplay | QualitySettingsChange.FilterResults)) != 0) QualityDeckInput.SettingsChanged();
            else if ((changes & QualitySettingsChange.Input) != 0) QualityDeckInput.CancelChoice();
            if ((changes & (QualitySettingsChange.FilterLayout | QualitySettingsChange.FilterResults)) != 0) KeywordSetTray.SettingsChanged();
        }

        public void Shutdown()
        {
            s_instance = null;
            SplashScreen.SplashScreenShown -= OnStartupImageShown;
            LoaderCoroutines.Stop(m_startupCoroutine);
            m_startupCoroutine = null;
            FilterAvailability.Changed -= KeywordSetTray.SettingsChanged;
            CollectionQualitySettings.FlushPreferences(true);
            CollectionQualitySettings.ChangedKind -= SettingsChanged;
            DisenchantChoiceMenu.DestroyMenu();
            NativeScrollbarArt.ReleasePrefabSources();
        }

        public void LateUpdate()
        {
            CustomFrameRate.Update();
            if (!PreferredQualityMode.IsEnabled) return;
            CollectionFilterMenu.UpdateExpansionMarker();
            ComboCollectionCards.OnLateUpdate();
        }

        public void Update()
        {
            CollectionQualitySettings.FlushPreferences();
            CollectionOpenSearch.OnUpdate();
            // Cover a splash that was already shown before the event subscription.
            if (!m_collectionInitialized && !m_startupWaitScheduled)
            {
                SplashScreen splash = SplashScreen.Get();
                if (splash != null && splash.gameObject.activeInHierarchy) OnStartupImageShown(splash);
            }
            if (!PreferredQualityMode.IsEnabled) return;
            // DFT and Battlegrounds/Tavern Brawl experiments disabled at user request.
            // HubMenuPatches.OnUpdate();
            CollectionPageDiagnostics.OnUpdate();
            KeywordFilterUi.OnUpdate();
            ComboCollectionCards.OnUpdate();
            MixedPremiumCard.OnUpdate();
            CollapsedPremiumCount.OnUpdate();
            QualityDeckInput.OnUpdate();
        }
    }
}
