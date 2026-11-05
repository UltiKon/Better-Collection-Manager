using System;
using System.Collections.Generic;

namespace BetterCollectionManager
{
    internal enum QualityDisplayMode { Split, TwoHighest, PreferTwoCopies, HighestOnly, Stacking }
    internal enum ClickQualityMode { ClickedHalf, PreferredOrder, HighestFullyAvailable }
    internal enum DragQualityMode { SameAsClick = 0, ChooseOnDrop = 3, DraggedHalf = 4 }

    internal static class QualityOptions
    {
        public static bool HasMergedTiles => CollectionQualitySettings.Display == QualityDisplayMode.Split
            || CollectionQualitySettings.Display == QualityDisplayMode.Stacking;
        public static bool HasAddSettings => CollectionQualitySettings.Display != QualityDisplayMode.TwoHighest;
        public static bool ShowClickSettings => !CollectionQualitySettings.HideRedundant
            || CollectionQualitySettings.Display == QualityDisplayMode.Split;
        public static bool ShowDragSettings => !CollectionQualitySettings.HideRedundant || HasAddSettings;
        public static bool ClickLocked => CollectionQualitySettings.HideRedundant
            && (CollectionQualitySettings.Display == QualityDisplayMode.PreferTwoCopies
                || CollectionQualitySettings.Display == QualityDisplayMode.HighestOnly
                || CollectionQualitySettings.Display == QualityDisplayMode.Stacking);

        public static ClickQualityMode EffectiveClick => ClickLocked
            ? (CollectionQualitySettings.Display == QualityDisplayMode.PreferTwoCopies
                ? ClickQualityMode.HighestFullyAvailable : ClickQualityMode.PreferredOrder)
            : !HasMergedTiles
            && CollectionQualitySettings.Click == ClickQualityMode.ClickedHalf ? ClickQualityMode.PreferredOrder : CollectionQualitySettings.Click;
        public static DragQualityMode EffectiveDrag => !HasMergedTiles
            && CollectionQualitySettings.Drag == DragQualityMode.DraggedHalf ? DragQualityMode.SameAsClick : CollectionQualitySettings.Drag;

        public static string Label(QualityDisplayMode mode)
        {
            switch (mode)
            {
                case QualityDisplayMode.Stacking: return "Stacking";
                case QualityDisplayMode.TwoHighest: return "Two highest qualities";
                case QualityDisplayMode.PreferTwoCopies: return "Prefer two owned copies";
                case QualityDisplayMode.HighestOnly: return "Highest quality only";
                default: return "Split card";
            }
        }

        public static string Label(ClickQualityMode mode)
        {
            switch (mode)
            {
                case ClickQualityMode.PreferredOrder: return "Quality order";
                case ClickQualityMode.HighestFullyAvailable: return "Highest fully available";
                default: return "Clicked half";
            }
        }

        public static string Label(DragQualityMode mode)
        {
            switch (mode)
            {
                case DragQualityMode.ChooseOnDrop: return "Choose after dropping";
                case DragQualityMode.DraggedHalf: return "Dragged half";
                default: return "Same as click";
            }
        }

        // Availability is supplied by the current deck, not a click counter.
        // The requested half is a preference; an exhausted half falls back.
        public static List<TAG_PREMIUM> Available(IReadOnlyList<TAG_PREMIUM> order,
            TAG_PREMIUM requested, bool useRequested, bool preferComplete, Func<TAG_PREMIUM, bool> canAdd,
            Func<TAG_PREMIUM, bool> hasCompleteSet)
        {
            var result = new List<TAG_PREMIUM>();
            if (useRequested && Contains(order, requested) && canAdd(requested)) result.Add(requested);
            if (preferComplete)
                foreach (TAG_PREMIUM premium in order)
                    if (hasCompleteSet(premium) && canAdd(premium)) result.Add(premium);
            for (int step = 0; step < order.Count; step++)
            {
                TAG_PREMIUM premium = order[step];
                if (!result.Contains(premium) && canAdd(premium)) result.Add(premium);
            }
            return result;
        }

        private static bool Contains(IReadOnlyList<TAG_PREMIUM> order, TAG_PREMIUM premium)
        {
            for (int i = 0; i < order.Count; i++) if (order[i] == premium) return true;
            return false;
        }
    }
}
