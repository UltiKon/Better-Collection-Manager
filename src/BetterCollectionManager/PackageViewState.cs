using PegasusShared;

namespace BetterCollectionManager
{
    // Navigation state, independent of the card filter selections. Entering the
    // browser starts a fresh package query; leaving never clears card filters.
    internal static class PackageViewState
    {
        internal static bool IsActive { get; private set; }
        internal static FormatType Format { get; private set; } = FormatType.FT_STANDARD;
        internal static PackageKind? Group { get; private set; }
        internal static void Enter(FormatType format)
        {
            IsActive = true; Group = null;
            Format = format == FormatType.FT_WILD ? FormatType.FT_WILD : FormatType.FT_STANDARD;
            ExpansionFilterState.Clear();
            FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Single);
            FilterModes.InvalidateResults();
        }
        internal static void Leave()
        {
            if (IsActive) ExpansionFilterState.Clear();
            IsActive = false; Group = null;
            FilterModes.InvalidateResults();
        }
        internal static void SetFormat(FormatType format)
        {
            if (format != FormatType.FT_STANDARD && format != FormatType.FT_WILD) return;
            Format = format; FilterModes.InvalidateResults();
        }
        internal static void SelectGroup(PackageKind? group)
        { Group = group; FilterModes.InvalidateResults(); }
        internal static bool Matches(CategoryCatalog.Combo entry)
            => entry != null && entry.IsPackage && (!Group.HasValue || entry.PackageGroup == Group.Value);
    }
}
