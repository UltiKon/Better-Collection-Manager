using System.Collections.Generic;
using PegasusShared;

namespace BetterCollectionManager
{
    internal static class PackageBrowser
    {
        private static CollectionPageAnchor s_cardsPage;
        internal static void Enter()
        {
            if (PackageViewState.IsActive) return;
            var format = CollectionFilterMenu.CardsFormat;
            s_cardsPage = KeywordFilterApplier.CapturePageAnchor();
            ComboCollectionCards.CloseBrowserFolder();
            PackageViewState.Enter(format);
            KeywordFilterApplier.RefreshCollectionPages(page: 1);
            CollectionFilterMenu.RefreshExpansionIcon();
        }
        internal static void Leave()
        {
            if (!PackageViewState.IsActive) return;
            var anchor = s_cardsPage;
            var transitionFrom = KeywordFilterApplier.CapturePageAnchor();
            Reset();
            KeywordFilterApplier.RefreshCollectionPages(anchor, transitionFrom: transitionFrom);
            CollectionFilterMenu.RefreshExpansionIcon();
        }
        internal static void Reset()
        {
            ComboCollectionCards.CloseBrowserFolder();
            PackageViewState.Leave(); s_cardsPage = null;
        }
        internal static void ToggleFormat()
        {
            ChangeFormat(PackageViewState.Format == FormatType.FT_STANDARD ? FormatType.FT_WILD : FormatType.FT_STANDARD, false);
        }
        private static void ChangeFormat(FormatType format, bool clearExpansions)
        {
            // Snapshot before invalidating results. An open member view has a
            // different visible page from the saved folder overview destination.
            var transitionFrom = KeywordFilterApplier.CapturePageAnchor();
            var anchor = ComboCollectionCards.OpenedPackage == null ? transitionFrom : ComboCollectionCards.PackageOverviewAnchor;
            PackageViewState.SetFormat(format);
            if (clearExpansions) ExpansionFilterState.Clear();
            QueryChanged(anchor, transitionFrom);
        }
        internal static bool IsFormatChoice(SetFilterItem item, out FormatType format)
        {
            format = FormatType.FT_STANDARD;
            if (item == null || item.IsHeader) return false;
            if (item.IsAllStandard) return true;
            if (item.FormatType != FormatType.FT_WILD || ExpansionFilterState.ReadSpecificCards(item)?.Count > 0) return false;
            format = FormatType.FT_WILD;
            return item.CardSets == null || item.CardSets.Count == 0
                || new HashSet<TAG_CARD_SET>(item.CardSets).SetEquals(GameUtils.GetCardSetsInFormat(format));
        }
        internal static void SelectExpansion(SetFilterItem item)
        {
            if (IsFormatChoice(item, out var format))
            {
                ChangeFormat(format, true);
                return;
            }
            ExpansionFilterState.Toggle(item);
            QueryChanged();
        }
        internal static void SelectGroup(PackageKind? group)
        {
            PackageViewState.SelectGroup(group.HasValue && PackageViewState.Group == group ? null : group);
            QueryChanged();
        }
        internal static void QueryChanged(CollectionPageAnchor anchor = null, CollectionPageAnchor transitionFrom = null)
        {
            // Query controls select folders. Open package contents are always
            // complete, so changing a query returns to the folder overview.
            ComboCollectionCards.CloseBrowserFolder();
            KeywordFilterApplier.RefreshCollectionPages(anchor, page: anchor == null ? (int?)1 : null, transitionFrom: transitionFrom);
            KeywordFilterUi.NotifyFilterChangedFromChrome();
            CollectionFilterMenu.RefreshExpansionIcon();
            if (KeywordSetTray.IsShown) KeywordSetTray.RebuildRows();
        }
    }
}
