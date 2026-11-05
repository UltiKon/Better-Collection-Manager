using System;
using System.IO;

namespace BetterCollectionManager
{
    internal static partial class KeywordFilterUi
    {
        public static void OnCollectionManagerEnter()
        {
            if (!s_cmSessionActive)
            {
                FilterModes.StartSession();
                try
                {
                    CategoryCatalog.LoadUserFile(Path.Combine(
                        Path.GetDirectoryName(CollectionQualitySettings.SettingsPath), "categories.json"));
                }
                catch (Exception ex)
                {
                    // Optional curated data must not abort construction of all
                    // filter controls, even for a runtime/type-loading failure.
                    DebugLog.Warning("Categories initialization failed; continuing filter menu startup. " + ex);
                }
            }
            EnsureFields();
            KeywordCatalog.EnsureFields();
            StopBuildCoroutine();
            s_buildAborted = false;
            s_cmSessionActive = true;
            s_buildCoroutine = LoaderCoroutines.Start(BuildWhenReady());
            CollectionPageDiagnostics.CollectionEntered();
            DebugLog.Msg("KeywordFilterUi: CM session active — BuildWhenReady scheduled");
        }

        public static void PrepareExpansionFilterChange()
        {
            if (!s_cmSessionActive || !s_built) return;
            KeywordCatalog.InvalidateScope();
            if (KeywordFilterState.IsActive)
                KeywordFilterState.PruneToAvailable(KeywordCatalog.GetEntries());
        }

        public static void OnSetFilterChanged(bool refreshPages = true)
        {
            try
            {
                if (!s_cmSessionActive || !s_built) return;
                if (PackageViewState.IsActive)
                {
                    if (refreshPages) KeywordFilterApplier.RefreshCollectionPages();
                    return;
                }

                bool selectingExpansion = CollectionFilterMenu.IsSelectingExpansion;
                if (!selectingExpansion)
                {
                    CollectionFilterMenu.InvalidateNativeExpansionPool();
                    KeywordCatalog.Invalidate();
                    ExpansionFilterState.Clear();
                    if (CollectionQualitySettings.HideEmptyFilters && CollectionFilterMenu.EnsureWideExpansionPool())
                    {
                        if (KeywordSetTray.IsShown) KeywordSetTray.RebuildRows();
                        return;
                    }
                    KeywordFilterState.PruneToAvailable(KeywordCatalog.GetEntries());
                    if (KeywordSetTray.IsShown) KeywordSetTray.RebuildRows();
                }
                // Expansion selection prepared the keyword state already and
                // refreshes the visible cards once after its callback returns.
                CollectionFilterMenu.RefreshExpansionIcon();
                UpdateOpenButtonLabel();
                RefreshAllPlaqueLabels();
                if (!selectingExpansion && refreshPages) KeywordFilterApplier.RefreshCollectionPages();
                DebugLog.Msg($"KeywordFilterUi: set/format changed; {KeywordFilterState.Describe()}");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.OnSetFilterChanged failed", ex);
            }
        }
    }
}
