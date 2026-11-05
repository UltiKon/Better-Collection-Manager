using System;
using System.Collections.Generic;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static string ClipboardImportLabel => s_activeKind == FilterTrayKind.Packages ? "Paste packages" : "Paste categories";
        private static string ClipboardExportLabel => s_activeKind == FilterTrayKind.Packages ? "Copy packages" : "Copy categories";
        private enum ControlGroup { None, Logic, Edit }
        private static readonly Dictionary<FilterTrayKind, ControlGroup> s_openControls = new Dictionary<FilterTrayKind, ControlGroup>();
        private static bool HasLogicControls => CollectionQualitySettings.ShowLogicControls
            && (!PackageViewState.IsActive || s_activeKind == FilterTrayKind.Expansions)
            && (CollectionQualitySettings.FilterModeBehavior != FilterModeBehavior.Hidden || CollectionQualitySettings.ShowFilterClearAll);
        private static bool HasEditControls => CollectionQualitySettings.ShowEditControls
            && (s_activeKind == FilterTrayKind.Categories || s_activeKind == FilterTrayKind.Packages);
        private static ControlGroup OpenControls
        {
            get
            {
                if (!s_openControls.TryGetValue(s_activeKind, out ControlGroup group)) return ControlGroup.None;
                return group == ControlGroup.Logic && HasLogicControls || group == ControlGroup.Edit && HasEditControls ? group : ControlGroup.None;
            }
        }
        private static void ToggleControls(ControlGroup group)
        {
            s_openControls[s_activeKind] = OpenControls == group ? ControlGroup.None : group;
            RebuildRows();
        }
        private sealed class PinnedControl
        {
            internal string Text, Name;
            internal Action Action;
            internal bool Selected, Detail;
        }
        private static List<PinnedControl> PinnedControls(bool controls, bool clearAll)
        {
            var result = new List<PinnedControl>();
            void Add(string text, string name, Action action, bool selected = false, bool detail = false)
                => result.Add(new PinnedControl { Text = text, Name = name, Action = action, Selected = selected, Detail = detail });
            if (clearAll)
            { Add("Clear All", "BCM_PinnedClearAllFilters", ClearFilters); return result; }
            Add("Back", BackItemName, null);
            if (!controls) return result;
            ControlGroup open = OpenControls;
            bool needsReset = (!PackageViewState.IsActive || s_activeKind == FilterTrayKind.Expansions) && FilterModes.Get(s_activeKind) != FilterMode.Single;
            if (needsReset && open != ControlGroup.Logic)
                Add("Clear All", "BCM_PinnedFilterClear", ClearFilters);
            if (HasLogicControls)
                Add(open == ControlGroup.Logic ? "Hide Logic Controls" : "Show Logic Controls", "BCM_ShowLogicControls",
                    () => ToggleControls(ControlGroup.Logic), open == ControlGroup.Logic);
            if (PackageViewState.IsActive && s_activeKind == FilterTrayKind.Packages)
            {
                Add("Format: " + (PackageViewState.Format == PegasusShared.FormatType.FT_STANDARD ? "Standard" : "Wild"), "BCM_PackageFormat", PackageBrowser.ToggleFormat);
                Add(ExpansionFilterState.SelectedCount > 0 ? "Expansions *" : "Expansions", "BCM_PackageExpansions", CollectionFilterMenu.ShowExpansions);
            }
            if (HasEditControls)
                Add(open == ControlGroup.Edit ? "Hide Edit Controls" : "Show Edit Controls", "BCM_ShowEditControls",
                    () => ToggleControls(ControlGroup.Edit), open == ControlGroup.Edit);
            if (open == ControlGroup.Logic)
            {
                if (CollectionQualitySettings.FilterModeBehavior != FilterModeBehavior.Hidden)
                {
                    Add(ModeLabel(), "BCM_PinnedFilterMode", ChangeFilterMode, detail: true);
                    Add(NotLabel(), "BCM_PinnedFilterNot", ToggleFilterNot, NotEnabled, true);
                }
                if (CollectionQualitySettings.ShowFilterClearAll || needsReset) Add("Clear All", "BCM_PinnedFilterClear", ClearFilters, detail: true);
            }
            if (open == ControlGroup.Edit)
            {
                Add(s_clipboardImportStatus ?? ClipboardImportLabel, "BCM_ImportClipboard", () => TransferClipboard(true), detail: true);
                Add(s_clipboardExportStatus ?? ClipboardExportLabel, "BCM_ExportClipboard", () => TransferClipboard(false), detail: true);
                Add(s_activeKind == FilterTrayKind.Packages ? "New package folder" : "New category", "BCM_AddFilterFolder", () => {
                    NotifyItemClicked();
                    try { ComboPopup.AddFolder(s_activeKind == FilterTrayKind.Packages); }
                    catch (Exception ex) { CategoryActionFailed(ex); }
                }, detail: true);
                if (s_activeKind == FilterTrayKind.Packages)
                    Add("Turn Deck Into Package", "BCM_AddPackage", () => OpenCategoryEditor(true), detail: true);
                else
                {
                    Add(s_categoryImportStatus ?? "Add deck cards to category", "BCM_ImportCategoryDeck", ImportCategoryDeck, detail: true);
                    Add(s_categoryRemoveStatus ?? "Remove deck cards from category", "BCM_RemoveCategoryDeck", RemoveCategoryDeck, detail: true);
                    Add("Turn Deck Into Combo", "BCM_AddCombo", () => OpenCategoryEditor(false), detail: true);
                }
            }
            return result;
        }
        private static string ControlShape
        {
            get
            {
                if (!s_pinAllControls) return "";
                if (s_activeKind == FilterTrayKind.Menu) return ShowRootClearAll ? "clear" : "";
                var names = new List<string>();
                foreach (PinnedControl control in PinnedControls(true, false)) names.Add(control.Name + (control.Detail ? ":detail" : ":top"));
                return string.Join(";", names.ToArray());
            }
        }
        private static void OpenCategoryEditor(bool package)
        {
            NotifyItemClicked();
            try { if (package) ComboPopup.AddPackage(); else ComboPopup.Add(); }
            catch (Exception ex) { CategoryActionFailed(ex); }
        }
        private static void CategoryActionFailed(Exception ex)
        {
            DebugLog.Warning("Category action: " + ex.Message);
            s_categoryImportStatus = ex.Message; s_categoryImportStatusUntil = UnityEngine.Time.unscaledTime + CategoryActionStatusSeconds;
            RebuildRows();
        }
        private static string s_clipboardImportStatus, s_clipboardExportStatus;
        private static float s_clipboardImportUntil, s_clipboardExportUntil;
        private static SetFilterItem s_clipboardImportItem, s_clipboardExportItem;
        private static void TransferClipboard(bool importing)
        {
            NotifyItemClicked();
            bool packages = s_activeKind == FilterTrayKind.Packages;
            try
            {
                if (importing)
                {
                    string snapshot = UnityEngine.GUIUtility.systemCopyBuffer;
                    if (!CategoryCatalog.TransferNeedsChoice(snapshot, packages))
                    {
                        TransferImport(snapshot, packages, false);
                        return;
                    }
                    CatalogTransferMenu.Open(packages ? "Existing packages differ" : "Existing categories differ", "Merge", () => TransferImport(snapshot, packages, false),
                        "Replace", () => TransferImport(snapshot, packages, true), () => CollectionFilterMenu.ShowFilter(packages ? FilterTrayKind.Packages : FilterTrayKind.Categories));
                }
                else
                {
                    string folder = null;
                    int count = 0;
                    if (packages)
                    {
                        foreach (var group in PackageCatalog.Groups)
                            if (PackageViewState.IsActive ? PackageViewState.Group == group.Kind : ExtraFilterState.HasPackage(group.Kind))
                            { folder = group.Name; count++; }
                    }
                    else
                    {
                        foreach (var category in CategoryCatalog.Categories)
                            if ((CategoryCatalog.IsComboCategory(category.Value) || category.Value == CuratedCategory.Combos) && ExtraFilterState.HasCategory(category.Value))
                            { folder = category.Name; count++; }
                    }
                    string selected = count == 1 ? folder : null;
                    CatalogTransferMenu.Open(packages ? "Copy packages to clipboard" : "Copy categories to clipboard", "All " + (packages ? "packages" : "categories"), () => TransferExport(packages, null),
                        selected == null ? (packages ? "Select one package folder first" : "Select one category first") : (packages ? "Package folder: " : "Category: ") + selected, () => TransferExport(packages, selected, true), () => CollectionFilterMenu.ShowFilter(packages ? FilterTrayKind.Packages : FilterTrayKind.Categories));
                }
            }
            catch (Exception ex) { TransferStatus(importing, ex.Message); }
        }
        private static void TransferImport(string snapshot, bool packages, bool overwrite)
        {
            string status; CategoryCatalog.Combo target = null;
            try
            {
                status = CategoryCatalog.ImportTransfer(snapshot, packages, overwrite);
                target = CategoryCatalog.FindTransferredEntry(snapshot, packages);
                if (target != null) NavigateToImportedEntry(target);
                else KeywordFilterApplier.RefreshCollectionPages();
                KeywordFilterUi.NotifyFilterChangedFromChrome();
            }
            catch (Exception ex) { status = ex.InnerException?.Message ?? ex.Message; target = null; }
            TransferStatus(true, status, reopen: target == null);
        }
        private static void NavigateToImportedEntry(CategoryCatalog.Combo target)
        {
            var previous = KeywordFilterApplier.CapturePageAnchor();
            ComboCollectionCards.CloseBrowserFolder();
            CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
            if (display is CollectionManagerDisplay cards) cards.ResetFilters(true);
            if (target.IsPackage)
            {
                PackageBrowser.Enter();
                PackageViewState.SetFormat(PegasusShared.FormatType.FT_WILD);
                ExpansionFilterState.Clear();
                PackageViewState.SelectGroup(target.PackageGroup);
                s_activeKind = FilterTrayKind.Packages;
            }
            else
            {
                if (PackageViewState.IsActive) PackageBrowser.Leave();
                ExtraFilterState.ClearAll(); KeywordFilterState.Clear();
                FilterModes.Set(FilterTrayKind.Categories, FilterMode.Single);
                ExtraFilterState.ToggleCategory(target.Category);
                CollectionFilterMenu.ClearExpansions();
                s_activeKind = FilterTrayKind.Categories;
            }
            CollectionFilterMenu.RefreshExpansionIcon();
            var destination = CollectionPageAnchor.ForCardKey("BCM_COMBO_" + target.Id);
            FilterButtonFeedback.RunFilterAction(() => KeywordFilterApplier.RefreshCollectionPages(destination, transitionFrom: previous));
        }
        private static void TransferExport(bool packages, string folder, bool requireFolder = false)
        {
            string status;
            try
            {
                if (requireFolder && folder == null) throw new InvalidOperationException("Select exactly one folder first.");
                UnityEngine.GUIUtility.systemCopyBuffer = CategoryCatalog.ExportTransfer(packages, folder);
                status = "Copied to clipboard";
            }
            catch (Exception ex) { status = ex.InnerException?.Message ?? ex.Message; }
            TransferStatus(false, status);
        }
        private static void TransferStatus(bool importing, string status, bool reopen = true)
        {
            if (importing) { s_clipboardImportStatus = status; s_clipboardImportUntil = UnityEngine.Time.unscaledTime + CategoryActionStatusSeconds; }
            else { s_clipboardExportStatus = status; s_clipboardExportUntil = UnityEngine.Time.unscaledTime + CategoryActionStatusSeconds; }
            if (reopen) { CollectionFilterMenu.ShowFilter(s_activeKind); RebuildRows(); }
            else HideImmediately();
        }
        private static int PinnedRows(List<PinnedControl> controls, int columns)
        {
            columns = Math.Max(1, columns); int top = 0;
            foreach (PinnedControl control in controls) if (!control.Detail) top++;
            return (top + columns - 1) / columns + (controls.Count - top + columns - 1) / columns;
        }
        private static void TrackPinnedCategoryAction(SetFilterItem item)
        {
            switch (item.gameObject.name)
            {
                case "BCM_ImportCategoryDeck": s_categoryImportItem = item; break;
                case "BCM_RemoveCategoryDeck": s_categoryRemoveItem = item; break;
                case "BCM_AddCombo": s_categoryComboItem = item; break;
                case "BCM_ImportClipboard": s_clipboardImportItem = item; break;
                case "BCM_ExportClipboard": s_clipboardExportItem = item; break;
            }
        }
    }
}
