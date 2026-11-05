using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;

internal static class Program
{
    private static int s_checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); s_checks++; }

    private static void Main()
    {
        CheckFilterCompaction();
        CollectiblePageManager manager = CollectionManager.Get().Display.Manager;
        manager.Results = new Filter(Page("DK", "dk1", "dk2"), Page("Druid", "d1", "d2"),
            Page("Druid", "d3", "d4"), Page("Priest", "p1", "p2"));
        manager.Current = 3;
        manager.Next = new Filter(Page("Druid", "d2", "d3"), Page("Priest", "p1", "p2"));
        ApplyFilter();
        Check(manager.Current == 1 && manager.Refreshes == 1 && manager.Updates == 1,
            "Filtering must follow a surviving visible Druid instead of retaining Priest's page number");
        Check(manager.AnimatedRefreshes == 1 && manager.LastTransition == BookPageManager.PageTransitionType.SINGLE_PAGE_LEFT,
            "Filter clicks animate the resolved earlier page without moving away from the surviving card");

        manager.Current = 1;
        manager.Next = new Filter(Page("Druid", "d2", "d3"), Page("Priest", "p1", "p2"));
        ApplyFilter();
        Check(manager.Current == 1, "An unchanged anchor must not move");
        Check(manager.AnimatedRefreshes == 1 && manager.ImmediateRefreshes == 1,
            "Refreshing an unchanged page does not request an unnecessary page turn");

        manager.Results = new Filter(Page("DK", "dk"), Page("Druid", "d0", "d1"),
            Page("Druid", "d2", "d3"), Page("Druid", "d4", "d5"), Page("Priest", "p1"));
        manager.Current = 3;
        manager.Next = new Filter(Page("Druid", "d0", "d1"), Page("Druid", "d4", "d5"), Page("Priest", "p1"));
        ApplyFilter();
        Check(manager.Current == 2, "Removing all visible cards must find their nearest surviving neighbor");

        // Expansion callbacks rebuild data before the shared refresh starts.
        manager.Results = new Filter(Page("DK", "dk"), Page("Druid", "d0", "d1"), Page("Druid", "d2", "d3"));
        manager.Current = 3;
        var beforeExpansion = KeywordFilterApplier.CapturePageAnchor();
        manager.Results = manager.Next = new Filter(Page("Druid", "d0", "d2"));
        ApplyFilter(beforeExpansion);
        Check(manager.Current == 1, "Expansion changes must resolve the snapshot captured before their callback");

        manager.Results = new Filter(Page("Druid", "d1"), Page("Priest", "p1"));
        manager.Current = 1;
        manager.Next = new Filter(Page("DK", "new-dk"), Page("Druid", "new-druid"), Page("Priest", "p1"));
        ApplyFilter();
        Check(manager.Current == 2, "A disjoint expansion must retain the class when it still has cards");

        manager.Next = new Filter(Page("Priest", "p1"));
        ApplyFilter();
        Check(manager.Current == 1, "An empty class must fall back to a surviving result");
        manager.Next = new Filter();
        ApplyFilter();
        Check(manager.Current == 1, "No results must use the native empty first page");

        manager.Results = new Filter(Page("Druid", "dual"), Page("Priest", "dual"));
        manager.Current = 2;
        manager.Next = new Filter(Page("DK", "dk"), Page("Druid", "dual"), Page("Priest", "dual"));
        ApplyFilter();
        Check(manager.Current == 3, "A dual-class card must anchor in its original class tab");
        Check(manager.LastTransition == BookPageManager.PageTransitionType.SINGLE_PAGE_RIGHT,
            "A later resolved page animates to the right");

        manager.Next = new Filter(Page("Druid", "dual"), Page("Priest", "dual"));
        CollectionManager.Get().Display.ViewMode = CollectionUtils.ViewMode.HERO_SKINS;
        manager.Current = 2;
        int animations = manager.AnimatedRefreshes, immediate = manager.ImmediateRefreshes;
        ApplyFilter();
        Check(manager.Current == 2 && manager.AnimatedRefreshes == animations,
            "Non-card views must retain their native refresh behavior");

        CollectionManager.Get().Display.ViewMode = CollectionUtils.ViewMode.CARDS;
        KeywordFilterApplier.RefreshCollectionPages();
        Check(manager.AnimatedRefreshes == animations && manager.ImmediateRefreshes == immediate + 2,
            "Startup/background refreshes update the page without a filter-click animation");
        Check(KeywordFilterApplier.FindAnimatedRefresh(typeof(UnsupportedPageFixture)) == null,
            "Games without the traced transition signature keep the immediate refresh path");
        CheckSameNumberTransitions(manager);
        CheckRestoredBrowserTransitions(manager);
        manager.Results = new Filter(Page("Mage", "before")); manager.Current = 1;
        manager.Next = new Filter(Page("Mage", "unrelated"), Page("Priest", "BCM_COMBO_imported"));
        ApplyFilter(CollectionPageAnchor.ForCardKey("BCM_COMBO_imported"));
        Check(manager.Current == 2, "An imported entry resolves to its destination page across class tabs after rebuilding results");
        manager.Next = new Filter(Page("Mage", "unrelated"));
        ApplyFilter(CollectionPageAnchor.ForCardKey("missing imported entry"));
        Check(manager.Current == 1, "A target unavailable in native results retains a valid page");
        Check(manager.Updates == manager.Refreshes,
            "Each refresh filters exactly once; native RefreshCurrentPageContents must not reset the resolved page with a second update");
        CheckCraftingTransitions(manager);
        Console.WriteLine($"Passed {s_checks} pagination checks.");
    }

    private static void ApplyFilter(CollectionPageAnchor anchor = null)
        => FilterButtonFeedback.RunFilterAction(() => KeywordFilterApplier.RefreshCollectionPages(anchor));

    private static void CheckCraftingTransitions(CollectiblePageManager manager)
    {
        var harmony = new HarmonyLib.Harmony();
        CraftingPageAnchor.Install(harmony); CraftingPageAnchor.Install(harmony);
        Check(harmony.Patched.Count == 3 && harmony.Patched.All(method => method != null),
            "Crafting anchors hook native entry, exit and page assembly exactly once");
        void Change(Filter before, int page, Filter after, bool enter = false, bool updatePage = true)
        {
            manager.Results = before; manager.Current = page; manager.Next = after;
            CraftingPageAnchor.Begin(manager, updatePage, out var state);
            try
            {
                if (enter) ((CollectionPageManager)manager).ShowCraftingModeCards(updatePage);
                else manager.HideCraftingModeCards(BookPageManager.PageTransitionType.NONE, updatePage);
            }
            finally { CraftingPageAnchor.End(null, state); }
        }
        var gold = Page("Mage", "first", "second"); gold.Cards[0].PremiumType = 1;
        int updates = manager.Updates, transitions = manager.Refreshes;
        Change(new Filter(Page("Druid", "before"), gold), 2,
            new Filter(Page("Druid", "before"), Page("Mage", "second"), Page("Mage", "first")));
        Check(manager.Current == 3 && manager.Updates == updates + 1 && manager.Refreshes == transitions + 1,
            "Exiting Crafting follows the first old card across premium changes in one native rebuild/transition");
        Change(new Filter(Page("Mage", "before"), Page("Mage", "missing", "owned")), 2,
            new Filter(Page("Mage", "before"), Page("Mage", "owned")));
        Check(manager.Current == 2, "A vanished missing first card falls back to another card from the same old page");
        Change(new Filter(Page("Mage", "before"), Page("Mage", "missing"), Page("Mage", "next")), 2,
            new Filter(Page("Mage", "before"), Page("Mage", "next")));
        Check(manager.Current == 2, "An entirely missing page follows the nearest surviving successor");
        Change(new Filter(Page("Druid", "dual"), Page("Priest", "dual")), 2,
            new Filter(Page("Druid", "dual"), Page("Mage", "other"), Page("Priest", "dual")));
        Check(manager.Current == 3, "Crafting exit preserves the class tab of a dual-class first card");
        Change(new Filter(Page("Mage", "first")), 1,
            new Filter(Page("Mage", "new-missing"), Page("Mage", "first")), enter: true);
        Check(manager.Current == 2, "Entering Crafting also follows the first previously visible card");
        Change(new Filter(Page("Mage", "missing")), 1, new Filter());
        Check(manager.Current == 1, "Exiting to no owned results keeps a valid empty first page");
        Change(new Filter(Page("Mage", "first")), 1, new Filter(Page("Mage", "other"), Page("Mage", "first")), updatePage: false);
        bool useCurrent = false;
        CraftingPageAnchor.KeepCardPage(manager, ref useCurrent);
        Check(!useCurrent, "Background crafting filter rebuilds leave no pending page anchor");

        manager.Results = new Filter(Page("Mage", "first")); manager.Current = 1;
        CraftingPageAnchor.Begin(manager, true, out var outer);
        CraftingPageAnchor.Begin(manager, false, out var inner);
        manager.Results = new Filter(Page("Mage", "other"), Page("Mage", "first"));
        useCurrent = false; CraftingPageAnchor.KeepCardPage(manager, ref useCurrent);
        Check(!useCurrent, "A nested background operation cannot consume its caller's crafting anchor");
        CraftingPageAnchor.End(null, inner);
        useCurrent = false; CraftingPageAnchor.KeepCardPage(manager, ref useCurrent);
        Check(useCurrent && manager.Current == 2, "Finishing a nested operation restores the outer anchor");
        var failure = new InvalidOperationException("native failure");
        Check(ReferenceEquals(CraftingPageAnchor.End(failure, outer), failure), "Native exceptions remain intact while the anchor scope unwinds");
        useCurrent = false; CraftingPageAnchor.KeepCardPage(manager, ref useCurrent);
        Check(!useCurrent, "A failed native crafting change cannot affect later page transitions");

        foreach (string excluded in new[] { "mode", "view", "submode", "package" })
        {
            PreferredQualityMode.IsEnabled = excluded != "mode";
            CollectionManager.Get().Display.ViewMode = excluded == "view" ? CollectionUtils.ViewMode.HERO_SKINS : CollectionUtils.ViewMode.CARDS;
            CollectionManager.Get().Display.Submode = excluded == "submode" ? CollectionUtils.ViewSubmode.OTHER : CollectionUtils.ViewSubmode.DEFAULT;
            PackageViewState.IsActive = excluded == "package";
            Change(new Filter(Page("Mage", "first")), 1,
                new Filter(Page("Mage", "other"), Page("Mage", "first")));
            Check(manager.Current == 1, "Unrelated views retain native crafting navigation: " + excluded);
        }
        PreferredQualityMode.IsEnabled = true; CollectionManager.Get().Display.ViewMode = CollectionUtils.ViewMode.CARDS;
        CollectionManager.Get().Display.Submode = CollectionUtils.ViewSubmode.DEFAULT; PackageViewState.IsActive = false;
    }

    private static void CheckSameNumberTransitions(CollectiblePageManager manager)
    {
        manager.Current = 1;
        manager.Results = new Filter(Page("Druid", "a", "b"));
        manager.Next = new Filter(Page("Druid", "a", "c"));
        int animations = manager.AnimatedRefreshes;
        ApplyFilter();
        Check(manager.Current == 1 && manager.AnimatedRefreshes == ++animations
            && manager.LastTransition == BookPageManager.PageTransitionType.SINGLE_PAGE_RIGHT,
            "Replacing visible cards on the same numbered page still turns the page");
        manager.Next = new Filter(Page("Druid", "a", "c"), Page("Priest", "new"));
        ApplyFilter();
        Check(manager.AnimatedRefreshes == animations,
            "Changes on another page and new instances of the same visible cards do not turn this page");
        manager.Next = new Filter(Page("Druid", "c", "a"));
        ApplyFilter();
        Check(manager.AnimatedRefreshes == ++animations, "Reordering the visible cards turns the page");
        manager.Next = new Filter(Page("Druid", "c", "a"));
        manager.Next.GetPageContents(1)[0].PremiumType = 1;
        ApplyFilter();
        Check(manager.AnimatedRefreshes == ++animations, "A different visible card quality turns the page");
        manager.Next = new Filter();
        ApplyFilter();
        Check(manager.AnimatedRefreshes == ++animations, "Removing the last results turns to the empty page");
        manager.Next = new Filter();
        ApplyFilter();
        Check(manager.AnimatedRefreshes == animations, "An unchanged empty page has no transition");
        manager.Next = new Filter(Page("Druid", "returned"));
        ApplyFilter();
        Check(manager.Current == 1 && manager.AnimatedRefreshes == ++animations,
            "Returning from no results turns the page even though both use page one");
        var anchor = KeywordFilterApplier.CapturePageAnchor();
        manager.Results = manager.Next = new Filter(Page("Priest", "returned"));
        ApplyFilter(anchor);
        Check(manager.AnimatedRefreshes == ++animations,
            "Expansion callbacks compare with the visible cards and class captured before native filtering");
        manager.Next = new Filter(Page("Priest", "background-change"));
        KeywordFilterApplier.RefreshCollectionPages();
        Check(manager.AnimatedRefreshes == animations, "Background card changes still refresh without a filter animation");
    }

    private static void CheckRestoredBrowserTransitions(CollectiblePageManager manager)
    {
        var cards = new Filter(Page("Druid", "d1", "d2"), Page("Priest", "p1", "p2"));
        manager.Results = cards; manager.Current = 2;
        var savedCards = KeywordFilterApplier.CapturePageAnchor();
        manager.Results = new Filter(Page("Druid", "package-a"), Page("Priest", "package-b"));
        var packages = KeywordFilterApplier.CapturePageAnchor();
        manager.Next = cards;
        int animations = manager.AnimatedRefreshes;
        FilterButtonFeedback.RunFilterAction(() => KeywordFilterApplier.RefreshCollectionPages(savedCards, transitionFrom: packages));
        Check(manager.Current == 2 && manager.AnimatedRefreshes == ++animations,
            "Returning from Packages turns the page even when the saved Cards location has the same page number and unchanged cards");

        var currentCards = KeywordFilterApplier.CapturePageAnchor();
        FilterButtonFeedback.RunFilterAction(() => KeywordFilterApplier.RefreshCollectionPages(savedCards, transitionFrom: currentCards));
        Check(manager.Current == 2 && manager.AnimatedRefreshes == animations,
            "Restoring the already visible Cards page still avoids an unnecessary animation");

        manager.Results = new Filter(Page("Druid", "package-a")); manager.Current = 1;
        packages = KeywordFilterApplier.CapturePageAnchor();
        FilterButtonFeedback.RunFilterAction(() => KeywordFilterApplier.RefreshCollectionPages(savedCards, transitionFrom: packages));
        Check(manager.Current == 2 && manager.AnimatedRefreshes == ++animations
            && manager.LastTransition == BookPageManager.PageTransitionType.SINGLE_PAGE_RIGHT,
            "Returning from Packages preserves the saved Cards position when its page number differs");

        manager.Results = new Filter(); manager.Current = 1;
        packages = KeywordFilterApplier.CapturePageAnchor();
        manager.Next = new Filter(Page("Priest", "p1", "p2"));
        FilterButtonFeedback.RunFilterAction(() => KeywordFilterApplier.RefreshCollectionPages(savedCards, transitionFrom: packages));
        Check(manager.Current == 1 && manager.AnimatedRefreshes == ++animations,
            "Returning from an empty package browser animates to Cards even when both views use page one");

        manager.Results = new Filter(Page("Priest", "package-b")); manager.Current = 1;
        packages = KeywordFilterApplier.CapturePageAnchor();
        KeywordFilterApplier.RefreshCollectionPages(savedCards, transitionFrom: packages);
        Check(manager.AnimatedRefreshes == animations,
            "Supplying a departure snapshot does not animate background refreshes");
    }

    private sealed class UnsupportedPageFixture
    {
        public void RefreshCurrentPageContents() { }
        public void RefreshCurrentPageContents(int page) { }
    }

    private static void CheckFilterCompaction()
    {
        KeywordFilterState.Active = true;
        KeywordFilterState.Matcher = card => card != null && card.CardId.EndsWith("7", StringComparison.Ordinal);
        var cards = Enumerable.Range(0, 100000).Select(i => new CollectibleCard { CardId = i.ToString() }).ToList();
        var expected = cards.Where(KeywordFilterState.Matcher).ToArray();
        int calls = KeywordFilterState.Evaluations;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        int removed = KeywordFilterApplier.ApplyInPlace(cards);
        elapsed.Stop();
        Check(removed == 90000 && cards.SequenceEqual(expected),
            "Large result lists retain surviving card identities and order while reporting the exact removal count");
        Check(KeywordFilterState.Evaluations - calls == 100000,
            "Filtering evaluates each input card once in a single compacting pass");
        KeywordFilterState.Matcher = card => false;
        Check(KeywordFilterApplier.ApplyInPlace(cards) == 10000 && cards.Count == 0,
            "A fully rejected result list becomes empty");
        KeywordFilterState.Active = false;
        cards.Add(new CollectibleCard { CardId = "untouched" });
        Check(KeywordFilterApplier.ApplyInPlace(cards) == 0 && cards.Count == 1,
            "Inactive filters leave native results untouched");
        KeywordFilterState.Active = true;
        KeywordFilterState.Matcher = card => card != null && card.CardId.EndsWith("7", StringComparison.Ordinal);
        FilterAvailability.Indexed = card => card?.CardId == "indexed-keep" ? true
            : card?.CardId == "indexed-drop" ? false : (bool?)null;
        cards = new List<CollectibleCard> { new CollectibleCard { CardId = "indexed-keep" },
            new CollectibleCard { CardId = "indexed-drop" }, new CollectibleCard { CardId = "outside-7" }, null };
        calls = KeywordFilterState.Evaluations;
        Check(KeywordFilterApplier.ApplyInPlace(cards) == 2 && cards.Select(card => card.CardId).SequenceEqual(new[] { "indexed-keep", "outside-7" })
            && KeywordFilterState.Evaluations - calls == 2,
            "Indexed cards bypass scalar predicates while outside-universe cards keep scalar filtering and survivor order");
        FilterAvailability.Indexed = null; KeywordFilterState.Active = false;
        Console.WriteLine($"Compacted 100000 cards in {elapsed.Elapsed.TotalMilliseconds:F1} ms (managed fixture).");
    }

    private static Page Page(string tab, params string[] ids) => new Page
        { Tab = new Tab { tagClass = tab }, Cards = ids.Select(id => new CollectibleCard { CardId = id }).ToList() };
}

public sealed class CollectibleCard { public string CardId; public int PremiumType; }
public sealed class Tab { public string tagClass; }
public sealed class Page { public Tab Tab; public List<CollectibleCard> Cards; }
public sealed class Filter
{
    private readonly Page[] m_pages;
    public Filter(params Page[] pages) { m_pages = pages; }
    public int GetTotalNumPages() => m_pages.Length;
    public List<CollectibleCard> GetPageContents(int page) => m_pages[page - 1].Cards;
    public Tab GetCurrentTabInfoFromPage(int page) => m_pages[page - 1].Tab;
}
public class BookPageManager
{
    protected int m_currentPageNum;
    public delegate void DelOnPageTransitionComplete(object data);
    public enum PageTransitionType { NONE, SINGLE_PAGE_RIGHT, SINGLE_PAGE_LEFT, MANY_PAGE_RIGHT, MANY_PAGE_LEFT }
    public void TransitionPageWhenReady(PageTransitionType transition, bool useCurrentPageNum, DelOnPageTransitionComplete callback, object data)
    {
        CraftingPageAnchor.KeepCardPage(this, ref useCurrentPageNum);
        if (!useCurrentPageNum) m_currentPageNum = 1; // Native premium-specific anchor lookup can fail.
        ((CollectiblePageManager)this).RecordTransition(transition);
        callback?.Invoke(data);
    }
}
public class CollectiblePageManager : BookPageManager
{
    protected Filter m_cardsCollection;
    public Filter Results { get => m_cardsCollection; set => m_cardsCollection = value; }
    public int Current { get => m_currentPageNum; set => m_currentPageNum = value; }
    public int CurrentPageNum => m_currentPageNum;
    public Filter Next;
    public int Updates, Refreshes;
    public int AnimatedRefreshes, ImmediateRefreshes;
    public PageTransitionType LastTransition;
    protected void UpdateFilteredCards() { m_cardsCollection = Next; Updates++; }
    public void NotifyOfCollectionChanged() => UpdateFilteredCards();
    public void RefreshCurrentPageContents() { UpdateFilteredCards(); m_currentPageNum = 1; Refreshes++; ImmediateRefreshes++; }
    protected void RefreshCurrentPageContents(PageTransitionType transition)
    { UpdateFilteredCards(); m_currentPageNum = 1; Refreshes++; AnimatedRefreshes++; LastTransition = transition; }
    public void RecordTransition(PageTransitionType transition)
    { Refreshes++; if (transition == PageTransitionType.NONE) ImmediateRefreshes++; else AnimatedRefreshes++; LastTransition = transition; }
    public void HideCraftingModeCards(PageTransitionType transition = PageTransitionType.NONE, bool updatePage = true)
    { UpdateFilteredCards(); if (updatePage) TransitionPageWhenReady(transition, false, null, null); }
    public void EnterCrafting(bool updatePage)
    { UpdateFilteredCards(); if (updatePage) TransitionPageWhenReady(PageTransitionType.NONE, false, null, null); }
}
public class CollectionPageManager : CollectiblePageManager
{ public void ShowCraftingModeCards(bool updatePage = true) => EnterCrafting(updatePage); }
public sealed class NativePageManager : CollectionPageManager { }
public sealed class CollectibleDisplay
{
    public readonly NativePageManager Manager = new NativePageManager();
    public CollectionUtils.ViewMode ViewMode = CollectionUtils.ViewMode.CARDS;
    public CollectionUtils.ViewSubmode Submode;
    public CollectiblePageManager GetPageManager() => Manager;
    public CollectionUtils.ViewMode GetViewMode() => ViewMode;
    public CollectionUtils.ViewSubmode GetViewSubmode() => Submode;
}
public sealed class CollectionManager
{
    private static readonly CollectionManager s_instance = new CollectionManager();
    public static CollectionManager Get() => s_instance;
    public readonly CollectibleDisplay Display = new CollectibleDisplay();
    public CollectibleDisplay GetCollectibleDisplay() => Display;
}
public static class CollectionUtils { public enum ViewMode { CARDS, HERO_SKINS } public enum ViewSubmode { DEFAULT, OTHER } }
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public readonly List<MethodInfo> Patched = new List<MethodInfo>();
        public void Patch(MethodInfo method, HarmonyMethod prefix = null, HarmonyMethod finalizer = null) => Patched.Add(method);
    }
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method) { } }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name)
        { for (; type != null; type = type.BaseType) { var info = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); if (info != null) return info; } return null; }
        public static FieldInfo Field(Type type, string name)
        { for (; type != null; type = type.BaseType) { var info = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); if (info != null) return info; } return null; }
    }
}
namespace BetterCollectionManager
{
    internal static class PreferredQualityMode { public static bool IsEnabled = true; }
    internal static class PackageViewState { public static bool IsActive; }
    internal static class CollectionPageDiagnostics
    {
        public static void Mark(string stage) { }
    }
    internal static class FilterButtonFeedback
    {
        private static int s_actionDepth;
        internal static bool IsFilterAction => s_actionDepth > 0;
        public static void RunFilterAction(Action action)
        { s_actionDepth++; try { action(); } finally { s_actionDepth--; } }
        public static void RunPageRefresh(Action action) => action();
    }
    internal static class FilterAvailability
    {
        public static void BeginCustomRefresh() { }
        public static void EndCustomRefresh() { }
        public static Func<CollectibleCard, bool?> Indexed;
        public static bool TryMatchCurrent(CollectibleCard card, out bool matches)
        {
            bool? result = Indexed?.Invoke(card); matches = result == true; return result.HasValue;
        }
    }
    internal static class KeywordFilterState
    {
        public static bool Active;
        public static int Evaluations;
        public static Func<CollectibleCard, bool> Matcher = card => true;
        public static bool IsActive => Active;
        public static bool Matches(CollectibleCard card) { Evaluations++; return Matcher(card); }
        public static string Describe() => "";
    }
    internal static class ExpansionFilterState
    {
        public static bool IsActive => false;
        public static bool Matches(CollectibleCard card) => true;
    }
    internal static class ExtraFilterState
    {
        public static bool IsActive => false;
        public static bool Matches(CollectibleCard card) => true;
        public static string Describe() => "";
    }
    internal static class DebugLog
    {
        public static void Warning(string text) { }
        public static void Msg(string text) { }
        public static void Exception(string text, Exception error) => throw new Exception(text, error);
    }
}
