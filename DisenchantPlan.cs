using System;
using System.Collections.Generic;

namespace BetterCollectionManager
{
    internal enum DisenchantMode { All, KeepTwo, One }

    internal sealed class DisenchantVariant
    {
        internal TAG_PREMIUM Premium;
        internal int Owned, SellValue;
        internal bool CanDisenchant;
    }

    /// <summary>Variants are ordered from highest to lowest preference. Trial copies never enter this list.</summary>
    internal sealed class DisenchantPlan
    {
        internal readonly Dictionary<TAG_PREMIUM, int> Counts = new Dictionary<TAG_PREMIUM, int>();
        internal int Count, Dust;

        internal bool SameRemovals(DisenchantPlan other)
        {
            if (Counts.Count != other.Counts.Count) return false;
            foreach (var entry in Counts)
                if (!other.Counts.TryGetValue(entry.Key, out int count) || count != entry.Value) return false;
            return true;
        }

        internal static DisenchantPlan Create(IReadOnlyList<DisenchantVariant> variants, DisenchantMode mode, TAG_PREMIUM selected)
        {
            var plan = new DisenchantPlan();
            int keep = mode == DisenchantMode.KeepTwo ? 2 : 0;
            foreach (var variant in variants)
            {
                int owned = Math.Max(0, variant.Owned);
                int retained = Math.Min(keep, owned);
                keep -= retained;
                int count = mode == DisenchantMode.One ? (variant.Premium == selected ? Math.Min(1, owned) : 0) : owned - retained;
                if (!variant.CanDisenchant || count == 0) continue;
                plan.Counts.Add(variant.Premium, count);
                plan.Count = checked(plan.Count + count);
                plan.Dust = checked(plan.Dust + count * variant.SellValue);
            }
            return plan;
        }
    }
}
