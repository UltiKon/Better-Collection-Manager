using System;
using System.Collections.Generic;
using PegasusShared;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private sealed class ExtraRow
        {
            public int Value;
            public string Name;
            public Action Toggle;
            public Func<bool> Selected, Current;
        }
        private static List<ExtraRow> s_extraRows;
        private static FormatType s_extraFormat;
        private static string s_extraHeading;
        private static SetFilterItem s_extraHeader;
        private static SetFilterItem s_categoryImportItem;
        private static SetFilterItem s_categoryRemoveItem;
        private static SetFilterItem s_categoryComboItem;
        private static string s_categoryImportStatus;
        private static string s_categoryRemoveStatus;
        private static float s_categoryImportStatusUntil, s_categoryRemoveStatusUntil;
        private const float CategoryActionStatusSeconds = 3f;
        private static readonly Dictionary<int, SetFilterItem> s_extraItems = new Dictionary<int, SetFilterItem>();
        private static void ResetExtraRowCache()
        {
            s_clipboardImportItem = null; s_clipboardExportItem = null;
            s_extraRows = null; s_extraHeader = null; s_categoryImportItem = null; s_categoryRemoveItem = null; s_categoryComboItem = null; s_extraItems.Clear();
        }
        private static void PopulateExtraRows()
        {
            RefreshCategoryActionLabels();
            if (s_activeKind == FilterTrayKind.Categories && !s_pinAllControls && CollectionQualitySettings.ShowEditControls)
            {
                if (s_categoryImportItem != null)
                { s_unfilteredHeaders.Add(false); s_items.Add(s_categoryImportItem); }
                else
                {
                    s_categoryImportItem = AddItem("Add deck cards to category", s_extraFormat, ImportCategoryDeck);
                    s_categoryImportItem.gameObject.name = "BCM_ImportCategoryDeck";
                }
                s_categoryImportItem.Text = s_categoryImportStatus ?? "Add deck cards to category";
                if (s_categoryRemoveItem != null)
                { s_unfilteredHeaders.Add(false); s_items.Add(s_categoryRemoveItem); }
                else
                {
                    s_categoryRemoveItem = AddItem("Remove deck cards from category", s_extraFormat, RemoveCategoryDeck);
                    s_categoryRemoveItem.gameObject.name = "BCM_RemoveCategoryDeck";
                }
                s_categoryRemoveItem.Text = s_categoryRemoveStatus ?? "Remove deck cards from category";
                if (s_categoryComboItem != null) { s_unfilteredHeaders.Add(false); s_items.Add(s_categoryComboItem); }
                else
                {
                    s_categoryComboItem = AddItem("Turn Deck Into Combo", s_extraFormat, () => {
                        NotifyItemClicked();
                        try { ComboPopup.Add(); }
                        catch (Exception ex) { DebugLog.Warning("Add combo: " + ex.Message); s_categoryImportStatus = ex.Message; s_categoryImportStatusUntil = Time.unscaledTime + CategoryActionStatusSeconds; RebuildRows(); }
                    });
                    s_categoryComboItem.gameObject.name = "BCM_AddCombo";
                }
            }
            if (s_extraHeader != null) { s_unfilteredHeaders.Add(true); s_items.Add(s_extraHeader); }
            else s_extraHeader = AddHeader(s_extraHeading, s_extraFormat);
            foreach (ExtraRow row in s_extraRows)
            {
                if (!(PackageViewState.IsActive && s_activeKind == FilterTrayKind.Packages)
                    && !FilterAvailability.Extra(s_activeKind, row.Value, row.Selected()))
                { s_unfilteredHeaders.Add(false); continue; }
                if (s_extraItems.TryGetValue(row.Value, out SetFilterItem item) && item != null)
                { s_unfilteredHeaders.Add(false); s_items.Add(item); }
                else
                {
                    item = AddItem(row.Name, s_extraFormat, () => {
                        NotifyItemClicked();
                        row.Toggle();
                        if (PackageViewState.IsActive && s_activeKind == FilterTrayKind.Packages) PackageBrowser.QueryChanged();
                        else KeywordFilterApplier.RefreshCollectionPages();
                        KeywordFilterUi.NotifyFilterChangedFromChrome();
                        RefreshAfterSelection();
                    });
                    s_extraItems[row.Value] = item;
                    SetFilterItem created = item;
                    s_extraSelectionRefresh.Add(() => SetFilterSelected(created, row.Selected(), row.Current(), ExtraFilterState.IsExcluded(s_activeKind, row.Value)));
                }
                SetFilterSelected(item, row.Selected(), row.Current(), ExtraFilterState.IsExcluded(s_activeKind, row.Value));
            }
        }

        private static void ImportCategoryDeck() => EditCategoryDeck(false);
        private static void RemoveCategoryDeck() => EditCategoryDeck(true);

        private static void EditCategoryDeck(bool remove)
        {
            NotifyItemClicked();
            try
            {
                if (remove && ExtraFilterState.HasCategory(CuratedCategory.Combos))
                    throw new InvalidOperationException("Choose a card-role category to remove standalone cards.");
                if (remove && ExtraFilterState.HasCategory(CuratedCategory.Uncategorized))
                    throw new InvalidOperationException("Assign cards to a named category to remove them from Uncategorized.");
                string name = EditedCategoryDeckImport.SelectedCategoryName();
                if (name == null)
                {
                    // Check the live deck before opening a modal, without using its name.
                    var snapshot = EditedCategoryDeckImport.Capture("Category");
                    if (snapshot.Cards.Count == 0) throw new InvalidOperationException("Add cards to the edited deck first.");
                    CollectionDeck edited = CollectionManager.Get().GetEditedDeck();
                    ComboPopup.PromptCategoryName(remove ? "Remove Deck Cards From Category" : "Add Deck Cards To Category",
                        remove ? "Remove cards" : "Add cards", entered => {
                            if (!CollectionManager.Get().IsInEditMode() || !ReferenceEquals(edited, CollectionManager.Get().GetEditedDeck()))
                                throw new InvalidOperationException("The edited deck changed.");
                            ApplyCategoryDeck(remove, entered);
                        });
                    return;
                }
                ApplyCategoryDeck(remove, name);
            }
            catch (Exception ex)
            {
                string message = ex.InnerException?.Message ?? ex.Message;
                DebugLog.Warning("Category " + (remove ? "removal" : "import") + " failed: " + message);
                SetCategoryDeckStatus(remove, remove ? message : "Import failed - " + message);
                RebuildRows();
            }
        }

        private static void ApplyCategoryDeck(bool remove, string name)
        {
            string status = remove ? EditedCategoryDeckImport.RemoveFromSelectedCategory(name) : EditedCategoryDeckImport.Import(name);
            DebugLog.Msg("Category " + (remove ? "removal: " : "import: ") + status);
            SetCategoryDeckStatus(remove, status);
            KeywordFilterApplier.RefreshCollectionPages();
            KeywordFilterUi.NotifyFilterChangedFromChrome();
            if (s_shown) RebuildRows();
            else s_rowsRevision = -1; // Rebuild when reopened, keeping the tray hidden behind the popup.
        }

        private static void SetCategoryDeckStatus(bool remove, string status)
        {
            if (remove) { s_categoryRemoveStatus = status; s_categoryRemoveStatusUntil = Time.unscaledTime + CategoryActionStatusSeconds; }
            else { s_categoryImportStatus = status; s_categoryImportStatusUntil = Time.unscaledTime + CategoryActionStatusSeconds; }
        }

        private static void RefreshCategoryActionLabels()
        {
            void Restore(SetFilterItem item, string label)
            {
                if (item == null) return;
                item.Text = label;
                item.m_uberText?.UpdateNow(true);
                if (s_wideRows.TryGetValue(item, out FilterTrayGeometry geometry) && geometry.FrameRenderer != null)
                    CenterRowText(item, geometry.FrameRenderer.bounds);
            }
            if (s_clipboardImportStatus != null && Time.unscaledTime >= s_clipboardImportUntil)
            {
                s_clipboardImportStatus = null;
                Restore(s_clipboardImportItem, ClipboardImportLabel);
            }
            if (s_clipboardExportStatus != null && Time.unscaledTime >= s_clipboardExportUntil)
            {
                s_clipboardExportStatus = null;
                Restore(s_clipboardExportItem, ClipboardExportLabel);
            }
            if (s_categoryImportStatus != null && Time.unscaledTime >= s_categoryImportStatusUntil)
            {
                s_categoryImportStatus = null;
                Restore(s_categoryImportItem, "Add deck cards to category");
            }
            if (s_categoryRemoveStatus != null && Time.unscaledTime >= s_categoryRemoveStatusUntil)
            {
                s_categoryRemoveStatus = null;
                Restore(s_categoryRemoveItem, "Remove deck cards from category");
            }
        }
        private static void PopulateTypeRows(FormatType format)
        {
            s_extraFormat = format; s_extraHeading = "Card Type";
            s_extraRows = new List<ExtraRow>();
            foreach (var entry in ExtraFilterCatalog.Types)
            {
                var value = entry.Type;
                s_extraRows.Add(new ExtraRow { Value = (int)value, Name = entry.Name,
                    Toggle = () => ExtraFilterState.ToggleType(value),
                    Selected = () => ExtraFilterState.HasType(value),
                    Current = () => ExtraFilterState.IsCurrentType(value) });
            }
            s_extraRows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            PopulateExtraRows();
        }
        private static void PopulateRaceRows(FormatType format)
        {
            s_extraFormat = format; s_extraHeading = "Minion Race";
            s_extraRows = new List<ExtraRow>();
            foreach (var entry in ExtraFilterCatalog.Races)
            {
                var value = entry.Race;
                s_extraRows.Add(new ExtraRow { Value = (int)value, Name = entry.Name,
                    Toggle = () => ExtraFilterState.ToggleRace(value),
                    Selected = () => ExtraFilterState.HasRace(value),
                    Current = () => ExtraFilterState.IsCurrentRace(value) });
            }
            s_extraRows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            PopulateExtraRows();
        }
        private static void PopulateSchoolRows(FormatType format)
        {
            s_extraFormat = format; s_extraHeading = "Spell School";
            s_extraRows = new List<ExtraRow>();
            foreach (var entry in ExtraFilterCatalog.Schools)
            {
                var value = entry.School;
                s_extraRows.Add(new ExtraRow { Value = (int)value, Name = entry.Name,
                    Toggle = () => ExtraFilterState.ToggleSchool(value),
                    Selected = () => ExtraFilterState.HasSchool(value),
                    Current = () => ExtraFilterState.IsCurrentSchool(value) });
            }
            s_extraRows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            PopulateExtraRows();
        }
        private static void PopulateRarityRows(FormatType format)
        {
            s_extraFormat = format; s_extraHeading = "Rarity";
            s_extraRows = new List<ExtraRow>();
            foreach (var entry in ExtraFilterCatalog.Rarities)
            {
                var value = entry.Rarity;
                s_extraRows.Add(new ExtraRow { Value = (int)value, Name = entry.Name,
                    Toggle = () => ExtraFilterState.ToggleRarity(value),
                    Selected = () => ExtraFilterState.HasRarity(value),
                    Current = () => ExtraFilterState.IsCurrentRarity(value) });
            }
            PopulateExtraRows();
        }

        private static void PopulatePackageRows(FormatType format)
        {
            s_extraFormat = format; s_extraHeading = "Packages"; s_extraRows = new List<ExtraRow>();
            if (PackageViewState.IsActive)
                s_extraRows.Add(new ExtraRow { Value = 0, Name = "All Packages", Toggle = () => PackageViewState.SelectGroup(null),
                    Selected = () => !PackageViewState.Group.HasValue, Current = () => !PackageViewState.Group.HasValue });
            foreach (var group in PackageCatalog.Groups)
            {
                PackageKind value = group.Kind;
                s_extraRows.Add(new ExtraRow { Value = (int)value, Name = group.Name,
                    Toggle = () => {
                        if (PackageViewState.IsActive) PackageViewState.SelectGroup(PackageViewState.Group == value ? (PackageKind?)null : value);
                        else ExtraFilterState.TogglePackage(value);
                    }, Selected = () => PackageViewState.IsActive ? PackageViewState.Group == value : ExtraFilterState.HasPackage(value),
                    Current = () => PackageViewState.IsActive ? PackageViewState.Group == value : ExtraFilterState.IsCurrentPackage(value) });
            }
            PopulateExtraRows();
        }
        private static void PopulateCategoryRows(FormatType format)
        {
            s_extraFormat = format; s_extraHeading = "Categories";
            s_extraRows = new List<ExtraRow>();
            foreach (var entry in CategoryCatalog.Categories)
            {
                var value = entry.Value;
                s_extraRows.Add(new ExtraRow { Value = (int)value, Name = entry.Name,
                    Toggle = () => ExtraFilterState.ToggleCategory(value),
                    Selected = () => ExtraFilterState.HasCategory(value),
                    Current = () => ExtraFilterState.IsCurrentCategory(value) });
            }
            PopulateExtraRows();
        }
    }
}
