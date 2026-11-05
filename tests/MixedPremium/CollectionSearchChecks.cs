using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static object SearchHook(string method, params object[] args) => typeof(StableNewSearch)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    private static List<CollectibleCard> StableMatches(CollectibleCardFilter filter, List<CollectibleCard> candidates)
    {
        object[] scope = { filter, false };
        SearchHook("Begin", scope);
        try
        {
            return candidates.Where(card => {
                object[] value = { card, card.IsNewCollectible };
                SearchHook("IsNewPostfix", value); return (bool)value[1];
            }).ToList();
        }
        finally { SearchHook("End", null, scope[1]); }
    }
    private static void CheckCollectionSearch()
    {
        var harmony = new HarmonyLib.Harmony();
        StableNewSearch.Install(harmony); StableNewSearch.Install(harmony);
        Check(harmony.Patched.Count == 3 && harmony.Hooks.Values.Count(value => value.Finalizer != null) == 1,
            "New-search membership hooks install once and restore their native query scope");
        StableNewSearch.Reset();
        CollectionManager.Instance = new CollectionManager();
        PreferredQualityMode.IsEnabled = PreferredPremiumPrefs.CollapseEnabled = true;
        CollectionQualitySettings.Display = QualityDisplayMode.HighestOnly;
        var filter = new CollectibleCardClassFilter { m_filterText = "new" };
        CollectionManager.Get().Display.m_cardsCollection = filter;
        var cards = new List<CollectibleCard>();
        for (int i = 0; i < 25; i++) foreach (TAG_PREMIUM premium in new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN })
            cards.Add(Card("new-" + i, premium, 2));
        for (int page = 0; page < 4; page++)
        {
            var results = StableMatches(filter, cards);
            filter.FindCardsResult = new CollectionManager.FindCardsResult { m_cards = results };
            PreferredQualityCollapse.BeginUpdate(); PreferredQualityCollapse.CollapseInPlace(results, "new search", null);
            Check(results.Count == 25 && results.Skip(page * 8).Take(8).Select(c => c.CardId)
                .SequenceEqual(Enumerable.Range(page * 8, Math.Min(8, 25 - page * 8)).Select(i => "new-" + i)),
                "Marking earlier cards seen cannot compact away or skip a collapsed New page");
            foreach (var card in cards.Where(c => results.Skip(page * 8).Take(8).Any(tile => tile.CardId == c.CardId))) card.SeenCount = card.OwnedCount;
        }
        Check(cards.All(c => !c.IsNewCard), "Stable search never falsifies real card seen state or new glows");
        Check(StableMatches(filter, cards).Count == 50, "Backwards paging can still reach all original matches");
        Check(StableNewSearch.WithoutSnapshot(() => StableMatches(filter, cards)).Count == 0,
            "Automatic probes read real unread matches rather than the current stabilized page snapshot");
        Check(StableMatches(filter, cards).Count == 50, "A native probe never destroys the user's stable New search");
        cards[0].OwnedCount = 0;
        Check(!StableMatches(filter, cards).Contains(cards[0]), "A sold final copy cannot be resurrected by the stable New snapshot");
        filter.m_filterCardSets = new[] { TAG_CARD_SET.CORE };
        Check(StableMatches(filter, cards).Count == 0, "Changing native filter context starts a fresh New search");
        cards[2].SeenCount = 0;
        Check(StableMatches(filter, cards).Count == 1, "New acquisitions can join an existing New search");
        cards[2].SeenCount = cards[2].OwnedCount;
        Check(StableMatches(filter, cards).Count == 1, "Seen membership persists within one exact query");
        SearchHook("SearchChanged", filter);
        Check(StableMatches(filter, cards).Count == 0, "Explicitly rerunning New refreshes the native unread matches");
        GameStrings.Strings["GLUE_COLLECTION_MANAGER_SEARCH_NEW"] = "neu";
        Check(StableNewSearch.HasNew("legendary neu") && !StableNewSearch.HasNew("-neu") && !StableNewSearch.HasNew("new"),
            "New detection respects localized whole search tokens and negation");
        GameStrings.Strings.Clear(); StableNewSearch.Reset();
        filter.m_filterText = "-new"; cards[2].SeenCount = 0;
        StableMatches(filter, cards); cards[2].SeenCount = cards[2].OwnedCount;
        Check(StableMatches(filter, cards).Count == 0, "Negative New queries never retain seen matches");
        filter.m_filterText = "new"; cards[2].SeenCount = 0;
        StableMatches(filter, cards); cards[2].SeenCount = cards[2].OwnedCount;
        SearchHook("End", new InvalidOperationException("native search failed"), false);
        Check(StableMatches(filter, cards).Count == 0, "A failed native query releases its snapshot and query scope");
        StableNewSearch.Reset();

        CheckOpenSearch();
    }
    private static void CheckOpenSearch()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bcm-search-" + Guid.NewGuid().ToString("N"));
        var oldKey = CollectionOpenSearch.AccountKey; var oldPath = CollectionOpenSearch.HistoryPath;
        try
        {
            // Native EntityIdExtensions.GetGameAccountRegion reads bits 32..39
            // of High; hold program and account number constant across regions.
            ulong programAndKind = (2UL << 56) | 0x57335443UL;
            Blizzard.GameService.SDK.Client.Integration.BattleNet.Account = new Blizzard.GameService.SDK.Client.Integration.BnetGameAccountId { High = programAndKind | (1UL << 32), Low = 9 };
            string firstRegion = CollectionOpenSearch.AccountKey();
            Blizzard.GameService.SDK.Client.Integration.BattleNet.Account.High = programAndKind | (2UL << 32);
            Check(CollectionOpenSearch.AccountKey() != firstRegion, "Native account history keys distinguish regions as well as game-account IDs");
            Blizzard.GameService.SDK.Client.Integration.BattleNet.Account = null;
            Check(CollectionOpenSearch.AccountKey() == null, "Invalid native account identities have no shared fallback history");
            string account = "account-a";
            CollectionOpenSearch.AccountKey = () => account;
            CollectionOpenSearch.HistoryPath = id => Path.Combine(directory, id + ".tsv");
            CollectionManager.Instance = new CollectionManager();
            var manager = CollectionManager.Get(); var display = manager.Display;
            display.ViewMode = CollectionUtils.ViewMode.CARDS; display.Submode = CollectionUtils.ViewSubmode.DEFAULT;
            var fresh = Card("fresh", TAG_PREMIUM.NORMAL, 1); fresh.LatestInsertDate = new DateTime(100);
            var refundable = Card("refund", TAG_PREMIUM.GOLDEN, 2); refundable.Refundable = true;
            var candidates = new List<CollectibleCard> { fresh, refundable };
            var filter = new CollectibleCardClassFilter { Query = text => candidates.Where(c => c.OwnedCount > 0
                && (text == "refund" ? c.Refundable : text == "new" && c.IsNewCard)).ToList() };
            display.m_cardsCollection = filter;
            CraftingManager.Instance = new CraftingManager(); CraftingTray.Instance = null;
            KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear(); PackageViewState.Leave();
            void Pump(int frames = 10)
            {
                for (int i = 0; i < frames; i++) { Time.unscaledTime += .4f; CollectionOpenSearch.OnUpdate(); }
            }
            void Open()
            {
                filter.m_filterText = ""; display.LastSearch = null; display.BookOpened = true;
                CollectionOpenSearch.Enter(display); CollectionOpenSearch.OnUpdate(); Pump();
            }
            CollectionQualitySettings.OpenSearch = CollectionOpenSearchMode.Off;
            Open(); Check(display.LastSearch == null && !Directory.Exists(directory), "Disabled opening search performs no history writes or searches");
            CollectionQualitySettings.OpenSearch = CollectionOpenSearchMode.Refund;
            Open(); Check(display.LastSearch == "refund" && File.Exists(Path.Combine(directory, account + ".tsv")), "Refund opening search uses the real search and persists matching copies");
            Open(); Check(display.LastSearch == null, "Unchanged refund results do not reopen the search across sessions");
            refundable.OwnedCount--;
            Open(); Check(display.LastSearch == null, "Fewer owned copies do not count as new matches");
            refundable.OwnedCount = 3;
            Open(); Check(display.LastSearch == "refund", "Additional owned copies can trigger the opening search again");
            CraftingManager.Get().SellValues[TAG_PREMIUM.GOLDEN]++;
            Open(); Check(display.LastSearch == "refund", "Changed refund value is a new observation");
            CraftingManager.Get().RefundEvent++;
            Open(); Check(display.LastSearch == "refund", "A new refund event is detected even when the sale value stays the same");
            account = "account-b";
            Open(); Check(display.LastSearch == "refund", "A different account has independent persisted match history");
            account = null;
            Open(); Check(display.LastSearch == null, "Unknown account identity cannot share another account's observations");
            account = "account-c";
            CollectionQualitySettings.OpenSearch = CollectionOpenSearchMode.NewOrRefund;
            Open(); Check(display.LastSearch == "refund", "Combined mode prioritizes novel refund matches");
            Open(); Check(display.LastSearch == "new", "Combined mode falls back to novel New matches after known refunds");
            Open(); Check(display.LastSearch == null, "Combined mode leaves the normal collection when both result sets are known");
            fresh.LatestInsertDate = new DateTime(200);
            Open(); Check(display.LastSearch == "new", "New receipt of the same card is recognized even at unchanged copy count");
            filter.m_filterText = "dragon"; display.LastSearch = null;
            CollectionOpenSearch.Enter(display); CollectionOpenSearch.OnUpdate();
            Check(filter.m_filterText == "dragon" && display.LastSearch == null, "Opening search never overwrites an existing manual query");
            account = "account-d"; display.IgnoreSearch = true;
            Open(); Check(!File.Exists(Path.Combine(directory, account + ".tsv")), "A search that was not applied cannot consume persisted observations");
            display.IgnoreSearch = false; display.Turning = true; filter.m_filterText = ""; display.LastSearch = null;
            CollectionOpenSearch.Enter(display); CollectionOpenSearch.OnUpdate();
            Check(display.LastSearch == null, "Opening search waits for native page transitions to finish");
            display.Turning = false; CollectionOpenSearch.OnUpdate(); Pump();
            Check(display.LastSearch == "refund", "The queued search runs once after the native collection is ready");
            account = "account-e"; filter.m_filterText = ""; display.LastSearch = null;
            CollectionOpenSearch.Enter(display); CollectionOpenSearch.Leave(); CollectionOpenSearch.OnUpdate();
            Check(display.LastSearch == null, "Leaving the collection cancels a pending automatic search");
            account = "account-f"; manager.Editing = true;
            Open(); Check(display.LastSearch == null, "Opening directly into deck editing does not change the search");
            manager.Editing = false;

            account = "late-account"; display.Ready = false; display.LastSearch = null; filter.m_filterText = "";
            CollectionOpenSearch.Enter(display); Pump();
            Check(display.LastSearch == null, "Native UI readiness cannot consume the pending opening search");
            display.Ready = true; string validAccount = account; account = null; Pump();
            Check(display.LastSearch == null, "Temporarily missing account identity leaves the opening search pending");
            account = validAccount; Pump();
            Check(display.LastSearch == "refund", "The search runs after a late account identity appears, without reopening collection");

            account = "late-filter"; filter.m_filterText = ""; display.LastSearch = null; display.m_cardsCollection = null;
            CollectionOpenSearch.Enter(display); Pump(); display.m_cardsCollection = filter; Pump();
            Check(display.LastSearch == "refund", "A filter initialized several frames late does not lose the opening search");

            account = "late-results"; CollectionQualitySettings.OpenSearch = CollectionOpenSearchMode.New;
            filter.m_filterText = ""; display.LastSearch = null;
            var saved = candidates.ToList(); candidates.Clear();
            CollectionOpenSearch.Enter(display); CollectionOpenSearch.OnUpdate(); Pump(1);
            Check(display.LastSearch == null, "An empty first native result does not trigger a search or consume observations");
            candidates.AddRange(saved); CollectionOpenSearch.ResultsUpdated(filter); Pump();
            Check(display.LastSearch == "new", "Late native result updates are retried during the opening window");

            account = "unconfirmed"; filter.m_filterText = ""; display.LastSearch = null;
            CollectionOpenSearch.Enter(display); CollectionOpenSearch.OnUpdate(); Pump(1);
            Check(display.LastSearch == "new" && !File.Exists(Path.Combine(directory, account + ".tsv")),
                "Calling the native setter does not consume history before page confirmation");
            display.Turning = true; Pump();
            Check(!File.Exists(Path.Combine(directory, account + ".tsv")), "An unfinished native page transition cannot acknowledge observations");
            display.Turning = false; Pump();
            Check(File.Exists(Path.Combine(directory, account + ".tsv")), "History is saved only after the native searched page is ready");

            account = "native-reset"; filter.m_filterText = ""; display.LastSearch = null;
            int calls = display.SearchCalls;
            CollectionOpenSearch.Enter(display); CollectionOpenSearch.OnUpdate(); Pump(1);
            filter.m_filterText = ""; CollectionOpenSearch.ResultsUpdated(filter); Pump(1);
            Check(!File.Exists(Path.Combine(directory, account + ".tsv")), "Initialization resetting a submitted query cannot mark its matches handled");
            Pump();
            Check(display.SearchCalls == calls + 2 && File.Exists(Path.Combine(directory, account + ".tsv")),
                "A search reset by native initialization is retried and confirmed once");

            account = "timeout"; filter.m_filterText = ""; display.LastSearch = null; manager.Loaded = false;
            CollectionOpenSearch.Enter(display); Time.unscaledTime += 31f; CollectionOpenSearch.OnUpdate();
            manager.Loaded = true; Pump();
            Check(display.LastSearch == null, "Opening retries are bounded and cannot become an ongoing search override");
        }
        finally
        {
            CollectionOpenSearch.Leave(); CollectionOpenSearch.AccountKey = oldKey; CollectionOpenSearch.HistoryPath = oldPath;
            CollectionQualitySettings.OpenSearch = CollectionOpenSearchMode.Off;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
