using System;
using System.Collections.Generic;
using System.Diagnostics;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckAvailabilityPerformance()
    {
        if (Environment.GetEnvironmentVariable("BCM_FILTER_BENCHMARK") != "1") return;
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        CollectionQualitySettings.HideEmptyFilters = true;
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        FilterModes.Set(FilterTrayKind.Keywords, FilterMode.And);
        CollectionManager.Get().Display.m_setFilterTray = null;
        var cards = CollectionManager.Get().Cards;
        cards.Clear(); GameDbf.KeywordText.Records.Clear(); GameStrings.Names.Clear();
        const int options = 96;
        for (int k = 0; k < options; k++) AddKeyword((GAME_TAG)(20000 + k), "Benchmark " + k);
        for (int i = 0; i < 4096; i++)
        {
            var card = Card("benchmark-" + i, (GAME_TAG)(20000 + i % options));
            if (i % 64 == 0) card.Definition.Tags.Add(GAME_TAG.TAUNT);
            card.Set = i % 2 == 0 ? TAG_CARD_SET.CORE : TAG_CARD_SET.JAIL;
            card.Definition.CardType = card.CardType = i % 4 == 0 ? TAG_CARDTYPE.SPELL : TAG_CARDTYPE.MINION;
            card.Definition.School = i % 8 == 0 ? TAG_SPELL_SCHOOL.FIRE : TAG_SPELL_SCHOOL.FROST;
            card.Definition.Races.Add(i % 3 == 0 ? TAG_RACE.BEAST : TAG_RACE.DRAGON);
            card.Rarity = i % 5 == 0 ? TAG_RARITY.RARE : TAG_RARITY.COMMON;
            cards.Add(card);
        }
        KeywordCatalog.Invalidate(); KeywordCatalog.GetEntries();
        FilterAvailability.Capture(cards);
        KeywordFilterState.SetSelected(GAME_TAG.TAUNT, true);
        var cold = Stopwatch.StartNew();
        for (int k = 0; k < options; k++) FilterAvailability.Keyword((GAME_TAG)(20000 + k));
        cold.Stop();
        int reads = CollectibleCard.EntityReads;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        int visible = 0;
        for (int edit = 0; edit < 12; edit++)
        {
            KeywordFilterState.SetSelected((GAME_TAG)20000, edit % 2 == 0);
            for (int k = 0; k < options; k++) if (FilterAvailability.Keyword((GAME_TAG)(20000 + k))) visible++;
        }
        watch.Stop();
        Console.WriteLine($"Availability benchmark: {watch.Elapsed.TotalMilliseconds:F1} ms; {CollectibleCard.EntityReads - reads} definition reads; {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0:F0} KiB allocated; visible={visible}; initial index={cold.Elapsed.TotalMilliseconds:F1} ms.");
        void Capture()
        {
            FilterAvailability.BeginCustomRefresh();
            try { FilterAvailability.Capture(cards); }
            finally { FilterAvailability.EndCustomRefresh(); }
        }
        Capture(); // Warm the reusable capture buffers for both comparisons.
        int scalarMatches = 0, indexedMatches = 0;
        reads = CollectibleCard.EntityReads; allocated = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
        for (int edit = 0; edit < 12; edit++)
        {
            KeywordFilterState.SetSelected((GAME_TAG)20000, edit % 2 == 0); Capture();
            foreach (var card in cards)
                if (KeywordFilterState.Matches(card) && ExtraFilterState.Matches(card) && ExpansionFilterState.Matches(card)) scalarMatches++;
        }
        watch.Stop();
        Console.WriteLine($"Scalar filtering benchmark: {watch.Elapsed.TotalMilliseconds:F1} ms; {CollectibleCard.EntityReads - reads} definition reads; {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0:F0} KiB allocated.");
        reads = CollectibleCard.EntityReads; allocated = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
        for (int edit = 0; edit < 12; edit++)
        {
            KeywordFilterState.SetSelected((GAME_TAG)20000, edit % 2 == 0); Capture();
            foreach (var card in cards)
                if (FilterAvailability.TryMatchCurrent(card, out bool match) && match) indexedMatches++;
        }
        watch.Stop();
        Check(scalarMatches == indexedMatches && CollectibleCard.EntityReads == reads,
            "Indexed filter refreshes preserve scalar results without rereading warmed card definitions");
        Console.WriteLine($"Indexed filtering benchmark: {watch.Elapsed.TotalMilliseconds:F1} ms; {CollectibleCard.EntityReads - reads} definition reads; {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0:F0} KiB allocated.");
        KeywordFilterState.Clear();
        var core = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
        var jail = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.JAIL } };
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Single);
        int freshCount = 0, scopedCount = 0;
        reads = CollectibleCard.EntityReads; watch.Restart();
        for (int edit = 0; edit < 12; edit++)
        {
            ExpansionFilterState.SetSelected(edit % 2 == 0 ? core : jail, true);
            KeywordCatalog.Invalidate();
            freshCount += KeywordCatalog.GetEntries().Count;
        }
        watch.Stop();
        Console.WriteLine($"Full expansion catalog benchmark: {watch.Elapsed.TotalMilliseconds:F1} ms; {CollectibleCard.EntityReads - reads} definition reads.");
        reads = CollectibleCard.EntityReads; watch.Restart();
        for (int edit = 0; edit < 12; edit++)
        {
            ExpansionFilterState.SetSelected(edit % 2 == 0 ? core : jail, true);
            KeywordCatalog.InvalidateScope();
            scopedCount += KeywordCatalog.GetEntries().Count;
        }
        watch.Stop();
        Check(freshCount == scopedCount && CollectibleCard.EntityReads == reads,
            "Repeated indexed expansion catalogs return the same entries without rereading metadata");
        Console.WriteLine($"Indexed expansion catalog benchmark: {watch.Elapsed.TotalMilliseconds:F1} ms; {CollectibleCard.EntityReads - reads} definition reads.");
        ExpansionFilterState.Clear(); Capture();
        // Warm every category once, then edit all categories with empty-option hiding off.
        FilterAvailability.Extra(FilterTrayKind.Type, (int)TAG_CARDTYPE.SPELL, false);
        FilterAvailability.Extra(FilterTrayKind.Race, (int)TAG_RACE.BEAST, false);
        FilterAvailability.Extra(FilterTrayKind.School, (int)TAG_SPELL_SCHOOL.FIRE, false);
        FilterAvailability.Extra(FilterTrayKind.Rarity, (int)TAG_RARITY.RARE, false);
        FilterAvailability.Expansion(core, false); FilterAvailability.Expansion(jail, false);
        FilterAvailability.Keyword((GAME_TAG)20000);
        CollectionQualitySettings.HideEmptyFilters = false;
        reads = CollectibleCard.EntityReads; watch.Restart();
        int allCategoryMatches = 0;
        for (int edit = 0; edit < 12; edit++)
        {
            KeywordFilterState.SetSelected((GAME_TAG)20000, edit % 2 == 0);
            ExtraFilterState.SetType(TAG_CARDTYPE.SPELL, edit % 3 == 0);
            ExtraFilterState.SetRace(TAG_RACE.BEAST, edit % 4 == 0);
            ExtraFilterState.SetSchool(TAG_SPELL_SCHOOL.FIRE, edit % 5 == 0);
            ExtraFilterState.SetRarity(TAG_RARITY.RARE, edit % 6 == 0);
            ExpansionFilterState.SetSelected(edit % 2 == 0 ? core : jail, true);
            Capture();
            foreach (var card in cards)
                if (FilterAvailability.TryMatchCurrent(card, out bool match) && match) allCategoryMatches++;
        }
        watch.Stop();
        Check(CollectibleCard.EntityReads == reads, "All filter categories reuse warmed indexes with empty-option hiding disabled");
        Console.WriteLine($"All-category indexed filtering (hiding off): {watch.Elapsed.TotalMilliseconds:F1} ms; {CollectibleCard.EntityReads - reads} definition reads; matched={allCategoryMatches}.");
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear(); FilterAvailability.Clear();
    }
}
