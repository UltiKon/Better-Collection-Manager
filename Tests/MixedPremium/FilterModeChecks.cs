using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using PegasusShared;
using UnityEngine;

internal static partial class Program
{
    private static void CheckFilterModePersistence()
    {
        var soundHarmony = new HarmonyLib.Harmony();
        FilterButtonFeedback.InstallSoundGuard(soundHarmony);
        Check(soundHarmony.Patched.Any(method => method.DeclaringType == typeof(SoundManager) && method.Name == "Play"
            && method.GetParameters()[0].ParameterType == typeof(int)),
            "Native asset-reference playback overloads are hooked without requiring a string argument or click asset name");
        var loaded = typeof(CollectionQualitySettings).GetField("s_loaded", BindingFlags.Static | BindingFlags.NonPublic);
        void Reload()
        {
            MelonLoader.MelonPreferences.Categories.Clear();
            loaded.SetValue(null, false);
            CollectionQualitySettings.EnsureLoaded();
            FilterModes.StartSession();
        }

        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Expression;
        CollectionQualitySettings.ShowFilterClearAll = false;
        CollectionQualitySettings.HideEmptyFilters = true;
        Reload();
        Check(CollectionQualitySettings.FilterModeBehavior == FilterModeBehavior.Expression
            && !CollectionQualitySettings.ShowFilterClearAll && CollectionQualitySettings.HideEmptyFilters,
            "Filter behavior, optional Clear All and empty-option hiding must survive a fresh process");
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Hidden;
        Reload();
        Check(CollectionQualitySettings.FilterModeBehavior == FilterModeBehavior.Hidden,
            "The hidden Mode setting must persist independently of remembering per-category operators");
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        CollectionQualitySettings.ShowFilterClearAll = true;
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionQualitySettings.RememberFilterModes = false;
        FilterModes.StartSession();
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Or);
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.And);
        FilterModes.StartSession();
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Single
            && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Single,
            "Session changes must reset to existing defaults while remembering is disabled");
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.Single);
        FilterModes.Set(FilterTrayKind.Race, FilterMode.Or);
        CollectionQualitySettings.RememberFilterModes = true;
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Or);
        string json = File.ReadAllText(CollectionQualitySettings.SettingsPath);
        Check(json.Contains("\"rememberFilterModes\":true") && json.Contains("Keywords=Single")
            && json.Contains("Race=Or") && json.Contains("Expansions=Or"),
            "Opt-in must snapshot existing modes and save subsequent mode changes in JSON");
        Reload();
        Check(CollectionQualitySettings.RememberFilterModes && FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Single
            && FilterModes.Get(FilterTrayKind.Race) == FilterMode.Or && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Or,
            "A fresh process must recover independently saved category modes");
        CollectionQualitySettings.RememberFilterModes = false;
        Check(FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Or, "Turning remembering off preserves current modes");
        Reload();
        Check(!CollectionQualitySettings.RememberFilterModes
            && FilterModes.Kinds.All(kind => FilterModes.Get(kind) == FilterModes.Default(kind)),
            "Disabling remembering must restore defaults on the next session and process");
        File.WriteAllText(CollectionQualitySettings.SettingsPath,
            "{\"rememberFilterModes\":true,\"filterModes\":\"Keywords=invalid;Race=999;Menu=Or;Expansions=Or;Type=And\"}");
        Reload();
        Check(FilterModes.Get(FilterTrayKind.Keywords) == FilterMode.Single && FilterModes.Get(FilterTrayKind.Race) == FilterMode.Single
            && FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Or && FilterModes.Get(FilterTrayKind.Type) == FilterMode.Or,
            "Invalid modes and unsupported categories must be ignored while valid saved modes survive");
        CollectionQualitySettings.RememberFilterModes = false;
        FilterModes.StartSession();
        File.Delete(CollectionQualitySettings.SettingsPath);
    }

    private static void CheckExpansionModeMenu(KeywordSetTray.GridFixture fixture)
    {
        SetFilterTray native = fixture.Native;
        SetFilterItem original = native.m_selected;
        var callbacks = native.m_items.Select(item => item.Callback).ToArray();
        var originalSets = native.m_items[2].CardSets;
        native.m_items[2].CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE };
        int callbacksRun = 0;
        List<TAG_CARD_SET> lastSets = null;
        FormatType lastFormat = FormatType.FT_UNKNOWN;
        foreach (SetFilterItem item in native.m_items)
            item.Callback = (sets, cards, format, source, transition) => {
                Check(!transition && native.m_selected == source && CollectionFilterMenu.IsSelectingExpansion,
                    "Mode changes must use the same guarded immediate native callback");
                SoundManager.Get().LoadAndPlay(CollectionFilterMenu.ButtonPressSound);
                SoundManager.Get().Play(7); // Native asset-reference overload, not a string path.
                callbacksRun++;
                lastSets = sets;
                lastFormat = format;
                KeywordFilterUi.OnSetFilterChanged();
            };

        fixture.UseModeRows = true;
        KeywordSetTray.RebuildRows();
        SetFilterItem ModeRow() => fixture.Items.Single(row => row.gameObject.name == "BCM_FilterMode");
        void ClickMode()
        {
            int sounds = SoundManager.Get().Played.Count;
            var peg = ModeRow().GetComponent<PegUIElement>();
            peg.Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == sounds + 1, "Mode plays one sound on press before refreshing");
            peg.Release();
            Check(SoundManager.Get().Played.Count == sounds + 1,
                "Skipping unsupported AND never adds the native expansion callback's second click");
        }
        Check(fixture.Items[0] == ModeRow() && ModeRow().Text == "Mode: Single" && !ModeRow().IsHeader
            && ModeRow().GetComponent<PegUIElement>().IsEnabled(),
            "Every filter submenu must begin with an enabled mode switch");
        Check(ModeRow().GetComponentsInChildren<MeshRenderer>(false).Single(renderer => renderer.gameObject.name == "Row face").bounds.size.x
            < fixture.Scroller.m_ScrollBounds.bounds.size.x * .6f,
            "The mode switch occupies one column instead of leaving an empty part of a full row");
        int refreshes = KeywordFilterApplier.Refreshes;
        ClickMode();
        Check(FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Or && ModeRow().Text == "Mode: OR"
            && ExpansionFilterState.IsSelected(original) && callbacksRun == 1
            && KeywordFilterApplier.Refreshes == refreshes + 1 && lastFormat == FormatType.FT_WILD,
            "Expansions skip AND and retain the current expansion when switching to OR");
        SetFilterItem core = native.m_items[2];
        fixture.Items.Single(row => row.Text == core.Text).GetComponent<PegUIElement>().Release();
        Check(ExpansionFilterState.IsSelected(core) && ExpansionFilterState.IsSelected(original)
            && fixture.Items.Count(row => row.Selected) == 2,
            "Multi-selection must show a selection glow on every selected expansion");
        var coreCard = new CollectibleCard { CardId = "core", Set = TAG_CARD_SET.CORE };
        var specificCard = new CollectibleCard { CardId = "dbf-42", Set = TAG_CARD_SET.TEST_EXPANSION };
        Check(ExpansionFilterState.Matches(coreCard) && ExpansionFilterState.Matches(specificCard),
            "Expansion OR includes both native groups");
        ClickMode();
        Check(FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Single && ExpansionFilterState.IsActive
            && native.m_selected == core && ReferenceEquals(lastSets, core.CardSets)
            && lastFormat == core.FormatType && fixture.Items.Count(row => row.Selected) == 1,
            "OR to Single restores the latest expansion's original native filter and removes other glows");
        int singleRefreshes = KeywordFilterApplier.Refreshes;
        CollectionFilterMenu.SelectExpansion(core);
        Check(KeywordFilterApplier.Refreshes == singleRefreshes, "Re-selecting the active Single expansion is not an edit");
        ClickMode();
        Check(FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Or
            && ExpansionFilterState.IsSelected(core) && ExpansionFilterState.IsSelected(original)
            && fixture.Items.Count(row => row.Selected) == 2 && KeywordFilterApplier.Refreshes == singleRefreshes + 1,
            "Cycling out of Single restores both native expansion selections with one refresh");
        ClickMode();
        // A native callback failure must retain the previous working mode and selections.
        core.Callback = (sets, cards, format, source, transition) => { throw new InvalidOperationException("mode failure"); };
        ClickMode();
        Check(FilterModes.Get(FilterTrayKind.Expansions) == FilterMode.Single && ExpansionFilterState.IsActive
            && native.m_selected == core && ModeRow().Text == "Mode: Single",
            "Failed mode changes must roll back the mode, selections and visible mode label");

        core.Callback = (sets, cards, format, source, transition) => { lastSets = sets; lastFormat = format; };
        CollectionFilterMenu.SelectExpansion(original);
        ClickMode();
        Check(ExpansionFilterState.SelectedItems.Count() == 1 && ExpansionFilterState.IsSelected(original)
            && !ExpansionFilterState.IsSelected(core), "Editing Single commits its selection before cycling back to OR");
        ClickMode();
        CollectionFilterMenu.SelectExpansion(core);
        CollectionQualitySettings.HideEmptyFilters = true;
        Check(CollectionFilterMenu.EnsureWideExpansionPool() && ExpansionFilterState.IsActive
            && ExpansionFilterState.IsSelected(core) && lastFormat == FormatType.FT_WILD,
            "Enabling empty-option hiding must prepare a broad native pool while retaining the actual selected expansion");
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionFilterMenu.SelectExpansion(original);
        Check(!ExpansionFilterState.IsActive && native.m_selected == original && ReferenceEquals(lastSets, original.CardSets),
            "A later single expansion choice must restore ordinary native filtering after availability hiding is disabled");
        CollectionQualitySettings.RememberFilterModes = false;
        FilterModes.StartSession();
        ExpansionFilterState.Clear();
        native.m_items[2].CardSets = originalSets;
        for (int i = 0; i < callbacks.Length; i++) native.m_items[i].Callback = callbacks[i];
        native.m_selected = original;
        fixture.UseModeRows = false;
        KeywordSetTray.RebuildRows();
        int afterClicks = SoundManager.Get().Played.Count;
        FilterButtonFeedback.RunFilterAction(() => {
            SoundManager.Get().LoadAndPlay(CollectionFilterMenu.ButtonPressSound);
            SoundManager.Get().LoadAndPlay("card_flip.prefab");
        });
        Check(SoundManager.Get().Played.Count == afterClicks,
            "Filter application suppresses all secondary UI audio, including assets without click in their name");
        FilterButtonFeedback.RunFilterAction(() => {
            Check(FilterButtonFeedback.IsFilterAction, "A user filter callback enables the animated refresh path");
            FilterButtonFeedback.RunPageRefresh(() => SoundManager.Get().LoadAndPlay("card_flip.prefab"));
            SoundManager.Get().LoadAndPlay(CollectionFilterMenu.ButtonPressSound);
        });
        Check(SoundManager.Get().Played.Count == afterClicks + 1,
            "Native page effects remain audible during result refresh while a duplicate filter click stays suppressed");
        afterClicks++;
        SoundManager.Get().LoadAndPlay("card_flip.prefab");
        Check(SoundManager.Get().Played.Count == afterClicks + 1,
            "Audio outside the filter action stays unchanged");
        try { FilterButtonFeedback.RunFilterAction(() => throw new InvalidOperationException("test sound cleanup")); }
        catch (InvalidOperationException) { }
        Check(FilterButtonFeedback.AllowNativeSound(CollectionFilterMenu.ButtonPressSound),
            "Native click audio is restored after a filter action throws");
        Check(!FilterButtonFeedback.IsFilterAction, "A completed or failed filter action cannot animate later background refreshes");
        FilterButtonFeedback.RunFilterAction(() => {
            try { FilterButtonFeedback.RunPageRefresh(() => throw new InvalidOperationException("page refresh failure")); }
            catch (InvalidOperationException) { }
            Check(!FilterButtonFeedback.ShouldPlaySound(), "A failed page refresh cannot leave the audio exemption active");
        });
    }
}
