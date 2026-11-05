using System;
using System.IO;
using System.Text;

namespace BetterCollectionManager
{
    [Flags]
    internal enum QualitySettingsChange { CardDisplay = 1, Input = 2, FilterLayout = 4, FilterResults = 8, MenuLayout = 16, Disenchant = 32 }
    internal static class CollectionQualitySettings
    {
        private static bool s_loaded;
        private static CollectionOpenSearchMode s_openSearch;
        private static PreferenceEntry<string> s_openSearchEntry;
        public static CollectionOpenSearchMode OpenSearch
        {
            get { EnsureLoaded(); return s_openSearch; }
            set { EnsureLoaded(); if (Enum.IsDefined(typeof(CollectionOpenSearchMode), value) && value != s_openSearch)
                { s_openSearch = value; Save(changes: QualitySettingsChange.Disenchant); } }
        }
        private static int s_customFrameRate = 144;
        private static bool s_useCustomFrameRate;
        private static PreferenceEntry<int> s_customFrameRateEntry;
        private static PreferenceEntry<bool> s_useCustomFrameRateEntry;
        internal const int MaxCustomFrameRate = 1000;
        public static int CustomFrameRate { get { EnsureLoaded(); return s_customFrameRate; } }
        public static bool UseCustomFrameRate
        {
            get { EnsureLoaded(); return s_useCustomFrameRate; }
            set { EnsureLoaded(); if (value != s_useCustomFrameRate) { s_useCustomFrameRate = value; Save(false); } }
        }
        public static bool SetCustomFrameRate(int value)
        {
            EnsureLoaded();
            if (value < 1 || value > MaxCustomFrameRate) return false;
            if (s_customFrameRate != value || !s_useCustomFrameRate)
            { s_customFrameRate = value; s_useCustomFrameRate = true; Save(false); }
            return true;
        }
        private static QualityDisplayMode s_display;
        private static ClickQualityMode s_click;
        private static DragQualityMode s_drag;
        private static bool s_hideRedundant;
        private static bool s_colorCodeCounter;
        private static bool s_disenchantPopup;
        private static PreferenceEntry<bool> s_disenchantPopupEntry;
        internal static int DisenchantPopupRevision { get; private set; }
        private static bool s_neverDustSignaturesDiamonds = true;
        private static PreferenceEntry<bool> s_neverDustSignaturesDiamondsEntry;
        private static bool s_collapseInCrafting;
        private static PreferenceEntry<bool> s_collapseInCraftingEntry;
        private static bool s_rememberFilterModes;
        private static FilterModeBehavior s_filterModeBehavior;
        private static bool s_showFilterClearAll = true, s_hideEmptyFilters, s_showLogicControls = true, s_showEditControls = true;
        private static PreferenceEntry<bool> s_showLogicControlsEntry, s_showEditControlsEntry;
        private static PreferenceEntry<bool> s_hideEmptyFiltersEntry;
        private static PreferenceEntry<string> s_filterModeBehaviorEntry;
        private static PreferenceEntry<bool> s_showFilterClearAllEntry;
        private static string s_savedFilterModes = "";
        private static PreferenceEntry<bool> s_rememberFilterModesEntry;
        private static PreferenceEntry<string> s_savedFilterModesEntry;
        private static int s_filterRows, s_filterColumns;
        private static PreferenceEntry<int> s_filterRowsEntry, s_filterColumnsEntry;
        public const int MaxFilterRows = 16, MaxFilterColumns = 4;
        private static PreferenceEntry<string> s_displayEntry, s_clickEntry, s_dragEntry;
        private static PreferenceEntry<bool> s_hideRedundantEntry;
        private static PreferenceEntry<bool> s_colorCodeCounterEntry;
        public static event Action Changed;
        internal static event Action<QualitySettingsChange> ChangedKind;
        private static int s_preferencesSaveFrame = -1;
        private static bool s_preferencesDirty;
        private static float s_preferencesRetryAt;
        internal static void FlushPreferences(bool force = false)
        {
            if (!s_preferencesDirty || (!force && (s_preferencesSaveFrame == UnityEngine.Time.frameCount || UnityEngine.Time.unscaledTime < s_preferencesRetryAt))) return;
            try { LoaderPreferences.Save(); s_preferencesDirty = false; s_preferencesSaveFrame = UnityEngine.Time.frameCount; s_preferencesRetryAt = 0f; }
            catch (Exception ex) { s_preferencesRetryAt = UnityEngine.Time.unscaledTime + 1f; DebugLog.Exception("Collection quality preferences save failed", ex); }
        }

        public static QualityDisplayMode Display
        {
            get { EnsureLoaded(); return s_display; }
            set { EnsureLoaded(); if (Enum.IsDefined(typeof(QualityDisplayMode), value) && value != s_display)
                { s_display = value; Save(changes: QualitySettingsChange.CardDisplay | QualitySettingsChange.Input); } }
        }
        public static ClickQualityMode Click
        {
            // Keep manual preferences while redundant fields are hidden;
            // showing them again restores editable overrides.
            get { EnsureLoaded(); return s_click; }
            set { EnsureLoaded(); if (!QualityOptions.ClickLocked && Enum.IsDefined(typeof(ClickQualityMode), value) && value != s_click)
                { s_click = value; Save(changes: QualitySettingsChange.Input); } }
        }
        public static DragQualityMode Drag
        {
            get { EnsureLoaded(); return s_drag; }
            set { EnsureLoaded(); if (Enum.IsDefined(typeof(DragQualityMode), value) && value != s_drag)
                { s_drag = value; Save(changes: QualitySettingsChange.Input); } }
        }
        public static bool HideRedundant
        {
            get { EnsureLoaded(); return s_hideRedundant; }
            set { EnsureLoaded(); if (value != s_hideRedundant) { s_hideRedundant = value; Save(changes: QualitySettingsChange.MenuLayout | QualitySettingsChange.Input); } }
        }

        public static bool DisenchantPopup
        {
            get { EnsureLoaded(); return s_disenchantPopup; }
            set
            {
                EnsureLoaded();
                if (value == s_disenchantPopup) return;
                s_disenchantPopup = value;
                DisenchantPopupRevision++;
                if (!value) DisenchantChoiceMenu.DestroyMenu();
                Save(changes: QualitySettingsChange.Disenchant | QualitySettingsChange.MenuLayout);
            }
        }

        public static bool NeverDustSignaturesDiamonds
        {
            get { EnsureLoaded(); return s_neverDustSignaturesDiamonds; }
            set { EnsureLoaded(); if (value != s_neverDustSignaturesDiamonds)
                { s_neverDustSignaturesDiamonds = value; Save(changes: QualitySettingsChange.Disenchant); } }
        }

        public static bool CollapseInCrafting
        {
            get { EnsureLoaded(); return s_collapseInCrafting; }
            set { EnsureLoaded(); if (value != s_collapseInCrafting)
                { s_collapseInCrafting = value; Save(changes: QualitySettingsChange.CardDisplay); } }
        }

        public static bool ColorCodeCounter
        {
            get { EnsureLoaded(); return s_colorCodeCounter; }
            set { EnsureLoaded(); if (value != s_colorCodeCounter) { s_colorCodeCounter = value; Save(changes: QualitySettingsChange.CardDisplay); } }
        }

        public static FilterModeBehavior FilterModeBehavior
        {
            get { EnsureLoaded(); return s_filterModeBehavior; }
            set { EnsureLoaded(); if (Enum.IsDefined(typeof(FilterModeBehavior), value) && value != s_filterModeBehavior)
                { s_filterModeBehavior = value; Save(changes: QualitySettingsChange.FilterResults | QualitySettingsChange.FilterLayout); } }
        }
        public static bool ShowFilterClearAll
        {
            get { EnsureLoaded(); return s_showFilterClearAll; }
            set { EnsureLoaded(); if (value != s_showFilterClearAll) { s_showFilterClearAll = value; Save(changes: QualitySettingsChange.FilterLayout); } }
        }

        public static bool ShowLogicControls
        {
            get { EnsureLoaded(); return s_showLogicControls; }
            set { EnsureLoaded(); if (value != s_showLogicControls) { s_showLogicControls = value; Save(changes: QualitySettingsChange.FilterLayout); } }
        }
        public static bool ShowEditControls
        {
            get { EnsureLoaded(); return s_showEditControls; }
            set { EnsureLoaded(); if (value != s_showEditControls) { s_showEditControls = value; Save(changes: QualitySettingsChange.FilterLayout); } }
        }

        public static bool HideEmptyFilters
        {
            get { EnsureLoaded(); return s_hideEmptyFilters; }
            set { EnsureLoaded(); if (value != s_hideEmptyFilters) { s_hideEmptyFilters = value; Save(changes: QualitySettingsChange.FilterResults | QualitySettingsChange.FilterLayout); } }
        }

        public static bool RememberFilterModes
        {
            get { EnsureLoaded(); return s_rememberFilterModes; }
            set
            {
                EnsureLoaded();
                if (value == s_rememberFilterModes) return;
                // Snapshot the current session before enabling persistence.
                string modes = value ? FilterModes.Serialize() : "";
                s_rememberFilterModes = value;
                s_savedFilterModes = modes;
                Save(false);
            }
        }

        internal static string SavedFilterModes
        {
            get { EnsureLoaded(); return s_savedFilterModes; }
            set { EnsureLoaded(); if (s_rememberFilterModes && value != s_savedFilterModes)
                { s_savedFilterModes = value ?? ""; Save(false); } }
        }

        // Zero selects Automatic; each menu resolves its own dimensions.
        public static int FilterMenuRows
        {
            get { EnsureLoaded(); return s_filterRows; }
            set { EnsureLoaded(); if (ValidRows(value) && value != s_filterRows) { s_filterRows = value; Save(changes: QualitySettingsChange.FilterLayout); } }
        }
        public static int FilterMenuColumns
        {
            get { EnsureLoaded(); return s_filterColumns; }
            set { EnsureLoaded(); if (value >= 0 && value <= MaxFilterColumns && value != s_filterColumns)
                { s_filterColumns = value; Save(changes: QualitySettingsChange.FilterLayout); } }
        }
        private static bool ValidRows(int value) => value == 0 || (value >= 1 && value <= MaxFilterRows);

        internal static string SettingsPath
        {
            get
            {
#if BEPINEX
                return Path.Combine(BepInEx.Paths.ConfigPath, "BetterCollectionManager", "quality_settings.json");
#else
                string dir = Path.GetDirectoryName(typeof(CollectionQualitySettings).Assembly.Location);
                if (string.IsNullOrEmpty(dir)) dir = "Mods";
                return Path.Combine(dir, "BetterCollectionManager", "quality_settings.json");
#endif
            }
        }

        public static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_customFrameRate = 144; s_useCustomFrameRate = false;
            s_display = QualityDisplayMode.Split;
            s_click = ClickQualityMode.PreferredOrder;
            s_drag = DragQualityMode.ChooseOnDrop;
            s_hideRedundant = true;
            s_colorCodeCounter = false;
            s_disenchantPopup = false;
            s_neverDustSignaturesDiamonds = true;
            s_collapseInCrafting = false;
            s_openSearch = CollectionOpenSearchMode.NewOrRefund;
            s_rememberFilterModes = false;
            s_filterModeBehavior = FilterModeBehavior.Category;
            s_showFilterClearAll = true; s_showLogicControls = s_showEditControls = true;
            s_hideEmptyFilters = true;
            s_savedFilterModes = "";
            s_filterRows = 0;
            s_filterColumns = 0;
            try
            {
                var category = LoaderPreferences.CreateCategory("BetterCollectionManagerBehavior");
                s_displayEntry = category.CreateEntry("DisplayMode", s_display.ToString(), "Collection card display");
                s_clickEntry = category.CreateEntry("ClickQuality", s_click.ToString(), "Quality priority when adding by click");
                s_dragEntry = category.CreateEntry("DragQuality", s_drag.ToString(), "Quality selection when adding by drag");
                s_hideRedundantEntry = category.CreateEntry("HideRedundantSettings", s_hideRedundant, "Hide redundant settings");
                s_openSearchEntry = category.CreateEntry("CollectionOpenSearch", s_openSearch.ToString(), "Search on collection opening: Off, New, Refund, NewOrRefund (only new matches per account)");
                s_disenchantPopupEntry = category.CreateEntry("DisenchantPopup", s_disenchantPopup, "Experimental disenchant amount popup; review quantities carefully. Off uses native disenchant behavior and disables Never dust protection.");
                s_neverDustSignaturesDiamondsEntry = category.CreateEntry("NeverDustSignaturesDiamonds", s_neverDustSignaturesDiamonds, "Never disenchant Signature or Diamond copies of the inspected card");
                s_collapseInCraftingEntry = category.CreateEntry("CollapseInCrafting", s_collapseInCrafting, "Collapse quality variants while crafting, keeping missing cards visible");
                s_colorCodeCounterEntry = category.CreateEntry("ColorCodeCounter", s_colorCodeCounter, "Color code counter");
                s_filterRowsEntry = category.CreateEntry("FilterMenuRows", s_filterRows, "Visible filter rows (0 = automatic)");
                s_filterColumnsEntry = category.CreateEntry("FilterMenuColumns", s_filterColumns, "Filter menu columns (0 = automatic)");
                s_rememberFilterModesEntry = category.CreateEntry("RememberFilterModes", s_rememberFilterModes, "Remember filter modes between collection sessions");
                s_savedFilterModesEntry = category.CreateEntry("FilterModes", s_savedFilterModes, "Saved mode for each filter category");
                s_filterModeBehaviorEntry = category.CreateEntry("FilterModeBehavior", s_filterModeBehavior.ToString(), "Filter mode control: Hidden, Category, Expression");
                s_showFilterClearAllEntry = category.CreateEntry("ShowFilterClearAll", s_showFilterClearAll, "Show the pinned Clear All filter button");
                s_showLogicControlsEntry = category.CreateEntry("ShowLogicControls", s_showLogicControls, "Show the logic controls group in filter menus");
                s_showEditControlsEntry = category.CreateEntry("ShowEditControls", s_showEditControls, "Show the editing controls group in Categories");
                s_hideEmptyFiltersEntry = category.CreateEntry("HideEmptyFilters", s_hideEmptyFilters, "Hide filter options with no matching cards");
                s_customFrameRateEntry = category.CreateEntry("CustomFrameRate", s_customFrameRate, "Custom FPS target (1-1000)");
                s_useCustomFrameRateEntry = category.CreateEntry("UseCustomFrameRate", s_useCustomFrameRate, "Use the custom FPS target");
                LoaderPreferences.Load();
                if (s_customFrameRateEntry.Value >= 1 && s_customFrameRateEntry.Value <= MaxCustomFrameRate)
                { s_customFrameRate = s_customFrameRateEntry.Value; s_useCustomFrameRate = s_useCustomFrameRateEntry.Value; }
                s_display = Parse(s_displayEntry.Value, s_display);
                s_click = Parse(s_clickEntry.Value, s_click);
                s_drag = Parse(s_dragEntry.Value, s_drag);
                s_hideRedundant = s_hideRedundantEntry.Value;
                s_colorCodeCounter = s_colorCodeCounterEntry.Value;
                s_disenchantPopup = s_disenchantPopupEntry.Value;
                s_neverDustSignaturesDiamonds = s_neverDustSignaturesDiamondsEntry.Value;
                s_collapseInCrafting = s_collapseInCraftingEntry.Value;
                s_openSearch = Parse(s_openSearchEntry.Value, s_openSearch);
                s_filterModeBehavior = Parse(s_filterModeBehaviorEntry.Value, s_filterModeBehavior);
                s_showFilterClearAll = s_showFilterClearAllEntry.Value;
                s_showLogicControls = s_showLogicControlsEntry.Value; s_showEditControls = s_showEditControlsEntry.Value;
                s_hideEmptyFilters = s_hideEmptyFiltersEntry.Value;
                s_rememberFilterModes = s_rememberFilterModesEntry.Value;
                s_savedFilterModes = s_savedFilterModesEntry.Value ?? "";
                if (ValidRows(s_filterRowsEntry.Value)) s_filterRows = s_filterRowsEntry.Value;
                if (s_filterColumnsEntry.Value >= 0 && s_filterColumnsEntry.Value <= MaxFilterColumns)
                    s_filterColumns = s_filterColumnsEntry.Value;
            }
            catch (Exception ex) { DebugLog.Exception("Collection quality preferences load failed", ex); }
            try
            {
                if (LoaderPreferences.LoadJsonBackups && File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    string fpsText = Read(json, "customFrameRate");
                    if (fpsText != null)
                    {
                        bool valid = int.TryParse(fpsText, out int fps) && fps >= 1 && fps <= MaxCustomFrameRate;
                        if (valid) s_customFrameRate = fps;
                        s_useCustomFrameRate = valid && bool.TryParse(Read(json, "useCustomFrameRate"), out bool useFps) && useFps;
                    }
                    s_openSearch = Parse(Read(json, "collectionOpenSearch"), s_openSearch);
                    s_display = Parse(Read(json, "display"), s_display);
                    s_click = Parse(Read(json, "click"), s_click);
                    s_drag = Parse(Read(json, "drag"), s_drag);
                    if (bool.TryParse(Read(json, "hideRedundant"), out bool hideRedundant)) s_hideRedundant = hideRedundant;
                    if (bool.TryParse(Read(json, "disenchantPopup"), out bool disenchantPopup)) s_disenchantPopup = disenchantPopup;
                    if (bool.TryParse(Read(json, "neverDustSignaturesDiamonds"), out bool neverDust)) s_neverDustSignaturesDiamonds = neverDust;
                    if (bool.TryParse(Read(json, "collapseInCrafting"), out bool collapseCrafting)) s_collapseInCrafting = collapseCrafting;
                    if (bool.TryParse(Read(json, "colorCodeCounter"), out bool colorCodeCounter)) s_colorCodeCounter = colorCodeCounter;
                    if (bool.TryParse(Read(json, "rememberFilterModes"), out bool rememberModes)) s_rememberFilterModes = rememberModes;
                    s_savedFilterModes = Read(json, "filterModes") ?? s_savedFilterModes;
                    s_filterModeBehavior = Parse(Read(json, "filterModeBehavior"), s_filterModeBehavior);
                    if (bool.TryParse(Read(json, "showFilterClearAll"), out bool showClear)) s_showFilterClearAll = showClear;
                    if (bool.TryParse(Read(json, "showLogicControls"), out bool showLogic)) s_showLogicControls = showLogic;
                    if (bool.TryParse(Read(json, "showEditControls"), out bool showEdit)) s_showEditControls = showEdit;
                    if (bool.TryParse(Read(json, "hideEmptyFilters"), out bool hideEmpty)) s_hideEmptyFilters = hideEmpty;
                    if (int.TryParse(Read(json, "filterRows"), out int rows) && ValidRows(rows)) s_filterRows = rows;
                    if (int.TryParse(Read(json, "filterColumns"), out int columns) && columns >= 0 && columns <= MaxFilterColumns)
                        s_filterColumns = columns;
                }
            }
            catch (Exception ex) { DebugLog.Exception("Collection quality JSON load failed", ex); }
            s_loaded = true;
#if BEPINEX
            if (LoaderPreferences.LoadJsonBackups) Save(false);
#endif
        }

        private static T Parse<T>(string text, T fallback) where T : struct
        {
            if (typeof(T) == typeof(ClickQualityMode)
                && string.Equals(text, "LowestFirst", StringComparison.OrdinalIgnoreCase)) text = "HighestFullyAvailable";
            if (typeof(T) == typeof(DragQualityMode))
                foreach (string legacy in new[] { "PreferredOrder", "LowestFirst", "HighestFullyAvailable", "1", "2" })
                    if (string.Equals(text, legacy, StringComparison.OrdinalIgnoreCase)) { text = "SameAsClick"; break; }
            return Enum.TryParse(text, true, out T value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
        }

        private static string Read(string json, string key)
        {
            int start = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            int colon = json.IndexOf(':', start);
            if (colon < 0) return null;
            int quote = colon + 1;
            while (quote < json.Length && char.IsWhiteSpace(json[quote])) quote++;
            if (quote >= json.Length) return null;
            if (json[quote] != '"')
            {
                int endToken = quote;
                while (endToken < json.Length && json[endToken] != ',' && json[endToken] != '}'
                    && !char.IsWhiteSpace(json[endToken])) endToken++;
                return json.Substring(quote, endToken - quote);
            }
            int end = json.IndexOf('"', quote + 1);
            return end < 0 ? null : json.Substring(quote + 1, end - quote - 1);
        }

        private static void Save(bool notify = true, QualitySettingsChange changes = QualitySettingsChange.CardDisplay)
        {
            try
            {
                if (s_displayEntry != null) s_displayEntry.Value = s_display.ToString();
                if (s_clickEntry != null) s_clickEntry.Value = s_click.ToString();
                if (s_dragEntry != null) s_dragEntry.Value = s_drag.ToString();
                if (s_hideRedundantEntry != null) s_hideRedundantEntry.Value = s_hideRedundant;
                if (s_openSearchEntry != null) s_openSearchEntry.Value = s_openSearch.ToString();
                if (s_disenchantPopupEntry != null) s_disenchantPopupEntry.Value = s_disenchantPopup;
                if (s_neverDustSignaturesDiamondsEntry != null) s_neverDustSignaturesDiamondsEntry.Value = s_neverDustSignaturesDiamonds;
                if (s_collapseInCraftingEntry != null) s_collapseInCraftingEntry.Value = s_collapseInCrafting;
                if (s_colorCodeCounterEntry != null) s_colorCodeCounterEntry.Value = s_colorCodeCounter;
                if (s_filterRowsEntry != null) s_filterRowsEntry.Value = s_filterRows;
                if (s_filterColumnsEntry != null) s_filterColumnsEntry.Value = s_filterColumns;
                if (s_rememberFilterModesEntry != null) s_rememberFilterModesEntry.Value = s_rememberFilterModes;
                if (s_savedFilterModesEntry != null) s_savedFilterModesEntry.Value = s_savedFilterModes;
                if (s_filterModeBehaviorEntry != null) s_filterModeBehaviorEntry.Value = s_filterModeBehavior.ToString();
                if (s_showFilterClearAllEntry != null) s_showFilterClearAllEntry.Value = s_showFilterClearAll;
                if (s_showLogicControlsEntry != null) s_showLogicControlsEntry.Value = s_showLogicControls;
                if (s_showEditControlsEntry != null) s_showEditControlsEntry.Value = s_showEditControls;
                if (s_hideEmptyFiltersEntry != null) s_hideEmptyFiltersEntry.Value = s_hideEmptyFilters;
                if (s_customFrameRateEntry != null) s_customFrameRateEntry.Value = s_customFrameRate;
                if (s_useCustomFrameRateEntry != null) s_useCustomFrameRateEntry.Value = s_useCustomFrameRate;
                s_preferencesDirty = true; FlushPreferences();
            }
            catch (Exception ex) { DebugLog.Exception("Collection quality preferences save failed", ex); }
            try
            {
                string path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "{\"display\":\"" + s_display + "\",\"click\":\"" + s_click
                    + "\",\"drag\":\"" + s_drag + "\",\"hideRedundant\":" + (s_hideRedundant ? "true" : "false")
                    + ",\"collectionOpenSearch\":\"" + s_openSearch + "\""
                    + ",\"disenchantPopup\":" + (s_disenchantPopup ? "true" : "false")
                    + ",\"neverDustSignaturesDiamonds\":" + (s_neverDustSignaturesDiamonds ? "true" : "false")
                    + ",\"collapseInCrafting\":" + (s_collapseInCrafting ? "true" : "false")
                    + ",\"colorCodeCounter\":" + (s_colorCodeCounter ? "true" : "false")
                    + ",\"rememberFilterModes\":" + (s_rememberFilterModes ? "true" : "false")
                    + ",\"filterModes\":\"" + s_savedFilterModes + "\""
                    + ",\"filterModeBehavior\":\"" + s_filterModeBehavior + "\""
                    + ",\"showFilterClearAll\":" + (s_showFilterClearAll ? "true" : "false")
                    + ",\"showLogicControls\":" + (s_showLogicControls ? "true" : "false")
                    + ",\"showEditControls\":" + (s_showEditControls ? "true" : "false")
                    + ",\"hideEmptyFilters\":" + (s_hideEmptyFilters ? "true" : "false")
                    + ",\"customFrameRate\":" + s_customFrameRate.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"useCustomFrameRate\":" + (s_useCustomFrameRate ? "true" : "false")
                    + ",\"filterRows\":" + s_filterRows.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"filterColumns\":" + s_filterColumns.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", Encoding.UTF8);
            }
            catch (Exception ex) { DebugLog.Exception("Collection quality JSON save failed", ex); }
            if (notify) { ChangedKind?.Invoke(changes); Changed?.Invoke(); }
        }
    }
}
