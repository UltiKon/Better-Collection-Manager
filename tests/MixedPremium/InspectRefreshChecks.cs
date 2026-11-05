using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;

internal static partial class Program
{
    private sealed class InspectFixture
    {
        internal CollectionManager Manager;
        internal CollectibleCardClassFilter Filter;
        internal List<CollectibleCard> Raw, Visible, Sibling, Other;
        internal CollectibleCard Normal, Golden, Signature, Neighbor;
    }

    private static InspectFixture InspectCase(QualityDisplayMode mode, int normal = 2, int golden = 2, int signature = 0)
    {
        InspectCollectionRefresh.Reset(); ComboCollectionCards.Reset();
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        PreferredQualityMode.IsEnabled = PreferredPremiumPrefs.CollapseEnabled = true;
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        CollectionQualitySettings.Display = mode; CraftingTray.Instance = null;
        var f = new InspectFixture { Manager = new CollectionManager(), Filter = new CollectibleCardClassFilter() };
        CollectionManager.Instance = f.Manager;
        f.Manager.Display.ViewMode = CollectionUtils.ViewMode.CARDS;
        f.Manager.Display.m_cardsCollection = f.Filter;
        f.Normal = Card("inspected", TAG_PREMIUM.NORMAL, normal);
        f.Golden = Card("inspected", TAG_PREMIUM.GOLDEN, golden);
        f.Signature = Card("inspected", TAG_PREMIUM.SIGNATURE, signature);
        f.Neighbor = Card("neighbor", TAG_PREMIUM.NORMAL, 1);
        var neighborGold = Card("neighbor", TAG_PREMIUM.GOLDEN, 1);
        foreach (var card in new[] { f.Normal, f.Golden, f.Signature, f.Neighbor, neighborGold })
        {
            f.Manager.Cards[(card.CardId, card.PremiumType)] = card;
            f.Manager.Counts[(card.CardId, card.PremiumType)] = card.OwnedCount;
        }
        f.Raw = new List<CollectibleCard>();
        for (int i = 0; i < 7; i++) f.Raw.Add(Card("before-" + i, TAG_PREMIUM.NORMAL, 2));
        f.Raw.AddRange(new[] { f.Normal, f.Golden, f.Signature }.Where(card => card.OwnedCount > 0));
        f.Raw.AddRange(new[] { f.Neighbor, neighborGold });
        for (int i = 0; i < 8; i++) f.Raw.Add(Card("after-" + i, TAG_PREMIUM.NORMAL, 2));
        f.Filter.FindCardsResult = new CollectionManager.FindCardsResult { m_cards = f.Raw };
        f.Visible = new List<CollectibleCard>(f.Raw);
        f.Sibling = new List<CollectibleCard>(f.Raw);
        f.Other = new List<CollectibleCard> { f.Neighbor, neighborGold };
        PreferredQualityCollapse.BeginUpdate();
        foreach (var list in new[] { f.Visible, f.Sibling, f.Other }) PreferredQualityCollapse.CollapseInPlace(list, "fixture", null);
        InspectCollectionRefresh.RecordResults(f.Filter, new[] { f.Visible, f.Sibling, f.Other, f.Sibling });
        return f;
    }

    private static MethodInfo InspectHook(string name) => typeof(InspectCollectionRefresh).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

    private static bool RemoveInspected(InspectFixture f, params (CollectibleCard Card, int Count)[] removals)
    {
        var changes = removals.Select(value => new CollectionManager.CardModification { cardID = value.Card.CardId,
            premium = value.Card.PremiumType, count = value.Count,
            modificationType = CollectionManager.CardModification.ModificationType.Remove }).ToList();
        object[] args = { changes, false };
        InspectHook("ChangesPrefix").Invoke(null, args);
        try
        {
            foreach (var value in removals)
            {
                value.Card.OwnedCount -= value.Count;
                f.Manager.Counts[(value.Card.CardId, value.Card.PremiumType)] = value.Card.OwnedCount;
            }
            InspectCollectionRefresh.BeginCollectionRefresh();
            try
            {
                // Execute the real Harmony boundary: a scoped prefix must skip
                // both the native method and the full mod postfix/cache reset.
                bool runNative = BetterCollectionManager.Patches.CollectibleCardClassFilter_UpdateResults_Patch.Prefix(f.Filter, out long state);
                if (runNative) return false;
                BetterCollectionManager.Patches.CollectibleCardClassFilter_UpdateResults_Patch.Postfix(f.Filter, state);
                return true;
            }
            finally { InspectCollectionRefresh.EndCollectionRefresh(); }
        }
        finally { InspectHook("ChangesFinalizer").Invoke(null, new object[] { null, args[1] }); }
    }

    private static void CheckInspectRefresh()
    {
        var harmony = new HarmonyLib.Harmony();
        InspectCollectionRefresh.Install(harmony);
        Check(harmony.Patched.Count == 3 && harmony.Hooks.Values.Count(value => value.Finalizer != null) == 1,
            "Inspect refresh tracks native inspect entry, transaction deltas and successful disenchant confirmation");
        InspectCollectionRefresh.Install(harmony);
        Check(harmony.Patched.Count == 3, "Repeated native collection initialization must not install duplicate transaction hooks");
        foreach (QualityDisplayMode mode in Enum.GetValues(typeof(QualityDisplayMode)))
        foreach (int signature in new[] { 0, 1 })
        {
            var f = InspectCase(mode, signature: signature);
            var other = f.Other.ToArray();
            PreferredQualityCollapse.TryGetVariants("neighbor", out var neighborVariants);
            PreferredQualityCollapse.TryGetMixed("neighbor", out var neighborMixed);
            Check(InspectCollectionRefresh.BeginInspect("inspected"), "Normal collection browsing permits scoped removal refresh");
            int scans = f.Manager.GetAllCardsCalls;
            Check(RemoveInspected(f, (f.Golden, 1)), "Removing a second copy updates the inspected group");
            Check(RemoveInspected(f, (f.Golden, 1)), "Removing the last Golden keeps the remaining inspected qualities");
            Check(f.Raw.All(card => card != f.Golden) && f.Raw.Contains(f.Normal), "Native raw results drop only qualities with no owned or trial copies");
            Check(PreferredQualityCollapse.TryGetVariants("neighbor", out var unchanged) && ReferenceEquals(unchanged, neighborVariants)
                && f.Other.SequenceEqual(other), "Unrelated native lists and cached variant decisions are reused");
            PreferredQualityCollapse.TryGetMixed("neighbor", out var unchangedMixed);
            Check(ReferenceEquals(neighborMixed, unchangedMixed), "Unrelated split/stack rendering retains the same pair object");
            Check(f.Manager.GetAllCardsCalls == scans, "Scoped refresh never scans the full inventory");
            var actual = f.Visible.ToArray();
            var expected = new List<CollectibleCard>(f.Raw);
            PreferredQualityCollapse.CollapseInPlace(expected, "full oracle", null);
            Check(actual.SequenceEqual(expected) && f.Sibling.SequenceEqual(expected),
                "Scoped results match a full rebuild, including multiclass/rune sibling lists in " + mode);
            Check(f.Visible.Skip(8).Take(8).SequenceEqual(expected.Skip(8).Take(8)),
                "Native page slicing fills gaps after inspected qualities disappear");
            Check(RemoveInspected(f, (f.Normal, 2)), "Removing all Normal copies also stays scoped");
            if (signature != 0) Check(RemoveInspected(f, (f.Signature, 1)), "Removing the final special quality stays scoped");
            Check(!f.Visible.Any(card => card.CardId == "inspected") && !f.Raw.Any(card => card.CardId == "inspected")
                && !PreferredQualityCollapse.TryGetVariants("inspected", out _)
                && !PreferredQualityCollapse.IsSingleTile("inspected") && !PreferredQualityCollapse.TryGetMixed("inspected", out _),
                "The last copy removes the card and all of its tile caches");
            InspectHook("ConfirmPrefix").Invoke(null, new object[] { new Network.CardSaleResult { AssetName = "inspected" } });
            InspectCollectionRefresh.BeginCollectionRefresh();
            try { Check(InspectCollectionRefresh.TryApply(f.Filter) && !f.Visible.Any(card => card.CardId == "inspected"),
                "A server confirmation cannot resurrect a removed card or force another full scan"); }
            finally { InspectCollectionRefresh.EndCollectionRefresh(); }
        }

        var trial = InspectCase(QualityDisplayMode.Split, 1, 0);
        trial.Normal.TrialCount = 1;
        Check(InspectCollectionRefresh.BeginInspect("inspected") && RemoveInspected(trial, (trial.Normal, 1))
            && trial.Visible.Contains(trial.Normal), "Trial ownership keeps an inspected card visible after disenchanting its owned copy");

        foreach (Action<InspectFixture> mutate in new Action<InspectFixture>[] {
            f => f.Filter.m_filterText = "extra",
            f => f.Filter.m_filterCardSets[0] = TAG_CARD_SET.TEST_EXPANSION,
            f => f.Filter.m_filterMasks[0] = 2,
            f => FilterModes.InvalidateResults(),
            f => PreferredPremiumPrefs.Order = PreferredPremiumPrefs.Order.Reverse().ToArray(),
            f => f.Manager.Editing = true,
            f => CraftingTray.Instance = new CraftingTray { Shown = true },
            f => f.Manager.Display.ViewMode = CollectionUtils.ViewMode.MASS_DISENCHANT,
            f => f.Filter.FindCardsResult = new CollectionManager.FindCardsResult(),
            f => f.Visible.Add(f.Neighbor),
            f => f.Manager.Counts[("inspected", TAG_PREMIUM.SIGNATURE)] = 1
        })
        {
            var f = InspectCase(QualityDisplayMode.Split);
            f.Filter.m_filterCardSets = new[] { TAG_CARD_SET.CORE };
            Check(InspectCollectionRefresh.BeginInspect("inspected"), "Capture baseline before a guarded context change");
            mutate(f);
            Check(!RemoveInspected(f, (f.Golden, 1)), "Changed query, settings, inventory or result lists require a full rebuild");
        }

        var unknown = InspectCase(QualityDisplayMode.Split);
        Check(InspectCollectionRefresh.BeginInspect("inspected") && !RemoveInspected(unknown, (unknown.Neighbor, 1)),
            "Another card changing during inspect falls back to a full rebuild");
        var outside = InspectCase(QualityDisplayMode.Split);
        Check(InspectCollectionRefresh.BeginInspect("inspected"), "Prepare unrelated event fixture");
        InspectCollectionRefresh.BeginCollectionRefresh();
        try { Check(!InspectCollectionRefresh.TryApply(outside.Filter), "An unclassified collection event cannot reuse a partial result"); }
        finally { InspectCollectionRefresh.EndCollectionRefresh(); }
        var search = InspectCase(QualityDisplayMode.Split);
        search.Filter.m_filterText = "dragon";
        Check(!InspectCollectionRefresh.BeginInspect("inspected"), "Native search queries retain native full evaluation");
        search.Filter.m_filterText = null;
        GameUtils.CounterpartIds.Add(GameUtils.TranslateCardIdToDbId("inspected"));
        Check(!InspectCollectionRefresh.BeginInspect("inspected"), "Core/counterpart visibility dependencies retain native full evaluation");
        GameUtils.CounterpartIds.Clear();

        CheckInspectRefreshCost();
        InspectCollectionRefresh.Reset(); ComboCollectionCards.Reset(); CollectionManager.Instance = null;
        CraftingTray.Instance = null; CollectionQualitySettings.Display = QualityDisplayMode.Split;
    }

    private static void CheckInspectRefreshCost()
    {
        var f = InspectCase(QualityDisplayMode.Split);
        for (int i = 0; i < 10000; i++)
        {
            var card = Card("untouched-" + i, TAG_PREMIUM.NORMAL, 2);
            f.Raw.Add(card); f.Visible.Add(card); f.Sibling.Add(card);
        }
        Check(InspectCollectionRefresh.BeginInspect("inspected"), "Capture a large collection");
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Check(RemoveInspected(f, (f.Golden, 1)), "A large collection also takes the scoped path");
        timer.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 100000, "A single-card refresh does not allocate groups or indexes proportional to the collection size");
        Console.WriteLine($"Inspected-card refresh, 10000 unrelated cards: {timer.Elapsed.TotalMilliseconds:F2} ms; {allocated} bytes (managed fixture).");
    }
}
