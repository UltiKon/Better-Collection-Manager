using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Blizzard.GameService.SDK.Client.Integration;

namespace BetterCollectionManager
{
    internal enum CollectionOpenSearchMode { Off, New, Refund, NewOrRefund }

    internal static class CollectionOpenSearch
    {
        private sealed class Opening
        {
            internal CollectibleDisplay Display;
            internal float Deadline, QuietUntil, ReadySince = -1f, ObserveUntil = -1f, NextProbe;
            internal string Account, Path;
            internal SearchHistory History;
            internal string SubmittedToken;
            internal List<SearchObservation> SubmittedMatches;
        }
        private static Opening s_pending;
        private const float SettleSeconds = .35f, ObserveSeconds = 2f, RetrySeconds = 1f;
        internal static Func<string> AccountKey = NativeAccountKey;
        internal static Func<string, string> HistoryPath = account => Path.Combine(Path.GetDirectoryName(CollectionQualitySettings.SettingsPath), "auto_search", account + ".tsv");
        private static readonly FieldInfo InsertDate = AccessToolsField(typeof(CollectibleCard), "m_LatestInsertDate");
        private static FieldInfo AccessToolsField(Type type, string name) => HarmonyLib.AccessTools.Field(type, name);

        internal static void Enter(CollectibleDisplay display)
        {
            StableNewSearch.Reset();
            s_pending = new Opening { Display = display, Deadline = UnityEngine.Time.unscaledTime + 30f };
        }
        internal static void Leave()
        {
            if (s_pending != null) Finish(s_pending);
            StableNewSearch.Reset();
        }
        // Native startup and the shared filter tray can finish in different frames.
        // Wait for their result updates to settle before querying or acknowledging a search.
        internal static void ResultsUpdated(CollectibleCardClassFilter filter)
        {
            Opening opening = s_pending;
            if (opening == null || !ReferenceEquals(filter, StableNewSearch.CurrentFilter())) return;
            float now = UnityEngine.Time.unscaledTime;
            opening.QuietUntil = now + SettleSeconds;
            if (opening.ObserveUntil >= 0f) opening.ObserveUntil = now + ObserveSeconds;
        }
        private static void Wait(Opening opening) { opening.ReadySince = -1f; }
        private static void Finish(Opening opening)
        { if (ReferenceEquals(s_pending, opening)) s_pending = null; }

        internal static void OnUpdate()
        {
            Opening opening = s_pending;
            if (opening == null) return;
            try { Update(opening); }
            catch (Exception ex)
            {
                DebugLog.Exception("Collection opening search failed", ex);
                Finish(opening);
            }
        }
        private static void Update(Opening opening)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (CollectionQualitySettings.OpenSearch == CollectionOpenSearchMode.Off)
            { Finish(opening); return; }
            if (now > opening.Deadline)
            { Finish(opening); return; }
            var display = opening.Display;
            if (display == null) { Finish(opening); return; }
            var manager = CollectionManager.Get();
            if (manager == null || manager.GetCollectibleDisplay() == null)
            { Wait(opening); return; }
            if (!ReferenceEquals(manager.GetCollectibleDisplay(), display))
            { Finish(opening); return; }
            if (!manager.IsFullyLoaded() || !display.IsReady()) { Wait(opening); return; }
            if (!display.IsBookOpened()) { Wait(opening); return; }
            var pages = display.GetPageManager();
            if (pages == null || !pages.IsFullyLoaded()) { Wait(opening); return; }
            if (pages.ArePagesTurning()) { Wait(opening); return; }
            if (manager.IsInEditMode() || manager.GetEditedDeck() != null || display.GetViewMode() != CollectionUtils.ViewMode.CARDS
                || display.GetViewSubmode() != CollectionUtils.ViewSubmode.DEFAULT || PackageViewState.IsActive
                || CraftingManager.GetIsInCraftingMode() || (CraftingTray.Get()?.IsShown() ?? false))
            { Finish(opening); return; }
            var filter = StableNewSearch.CurrentFilter();
            if (filter == null) { Wait(opening); return; }
            string text = StableNewSearch.SearchText(filter);
            if (opening.SubmittedToken == null && !string.IsNullOrWhiteSpace(text))
            { Finish(opening); return; }
            string account = AccountKey();
            if (string.IsNullOrEmpty(account)) { Wait(opening); return; }
            if (opening.Account != null && opening.Account != account)
            { Finish(opening); return; }
            if (opening.ReadySince < 0f) opening.ReadySince = now;
            if (now < Math.Max(opening.QuietUntil, opening.ReadySince + SettleSeconds))
            { return; }

            if (opening.SubmittedToken != null)
            {
                if (string.Equals(text, opening.SubmittedToken, StringComparison.OrdinalIgnoreCase))
                {
                    // Do not consume observations merely because the search setter ran:
                    // the native page transition and startup filters must have finished.
                    foreach (SearchObservation match in opening.SubmittedMatches) opening.History.Remember(match);
                    opening.History.Save(opening.Path);
                    Finish(opening);
                    return;
                }
                if (!string.IsNullOrWhiteSpace(text))
                { Finish(opening); return; }
                opening.SubmittedToken = null; opening.SubmittedMatches = null;
                opening.QuietUntil = now + SettleSeconds; opening.ObserveUntil = -1f;
                return;
            }
            if (now < opening.NextProbe) return;
            if (opening.Account == null)
            {
                opening.Account = account; opening.Path = HistoryPath(account);
                opening.History = SearchHistory.Load(opening.Path);
            }
            if (opening.ObserveUntil < 0f) opening.ObserveUntil = now + ObserveSeconds;
            opening.NextProbe = now + RetrySeconds;
            Apply(opening, filter);
        }
        private static string NativeAccountKey()
        {
            var id = BattleNet.GetMyGameAccountId();
            return id == null || !id.IsValid() ? null : id.High.ToString("X16") + id.Low.ToString("X16");
        }
        private static List<CollectibleCard> Probe(CollectibleCardFilter filter, string text)
        {
            FieldInfo search = AccessToolsField(typeof(CollectibleCardFilter), "m_filterText");
            object previous = search.GetValue(filter);
            try
            {
                search.SetValue(filter, text);
                var cards = new List<CollectibleCard>(StableNewSearch.WithoutSnapshot(() => filter.GenerateResults()).m_cards);
                cards.RemoveAll(card => card == null || card.OwnedCount <= 0);
                KeywordFilterApplier.ApplyInPlace(cards);
                return cards;
            }
            finally { search.SetValue(filter, previous); }
        }
        private static void Apply(Opening opening, CollectibleCardFilter filter)
        {
            CollectionOpenSearchMode mode = CollectionQualitySettings.OpenSearch;
            foreach (string kind in mode == CollectionOpenSearchMode.NewOrRefund ? new[] { "refund", "new" }
                : mode == CollectionOpenSearchMode.New ? new[] { "new" } : mode == CollectionOpenSearchMode.Refund ? new[] { "refund" } : Array.Empty<string>())
            {
                string token = StableNewSearch.Token("GLUE_COLLECTION_MANAGER_SEARCH_" + kind.ToUpperInvariant(), kind);
                List<CollectibleCard> cards = Probe(filter, token);
                var matches = cards.Select(card => new SearchObservation
                {
                    Key = kind + "/" + card.CardId + "/" + (int)card.PremiumType, Owned = card.OwnedCount,
                    InsertTicks = InsertDate?.GetValue(card) is DateTime date ? date.Ticks : 0,
                    RefundEvent = kind == "refund" ? Convert.ToInt32(CraftingManager.GetCardValue(card.CardId, card.PremiumType)?.OverrideEvent) : 0,
                    Sell = kind == "refund" && CraftingManager.Get().TryGetCardSellValue(card.CardId, card.PremiumType, out int sell) ? sell : 0
                }).ToList();
                bool novel = matches.Any(opening.History.IsNovel);
                if (!novel) continue;
                opening.Display.FilterBySearchText(token);
                if (!string.Equals(StableNewSearch.SearchText(filter), token, StringComparison.OrdinalIgnoreCase))
                { Finish(opening); return; }
                opening.SubmittedToken = token; opening.SubmittedMatches = matches;
                opening.QuietUntil = UnityEngine.Time.unscaledTime + SettleSeconds;
                return;
            }
            if (UnityEngine.Time.unscaledTime >= opening.ObserveUntil)
                Finish(opening);
        }

    }

    internal sealed class SearchObservation
    {
        internal string Key;
        internal int Owned, Sell, RefundEvent;
        internal long InsertTicks;
    }
    internal sealed class SearchHistory
    {
        private readonly Dictionary<string, SearchObservation> m_seen = new Dictionary<string, SearchObservation>(StringComparer.Ordinal);
        internal bool IsNovel(SearchObservation next) => !m_seen.TryGetValue(next.Key, out var old)
            || next.Owned > old.Owned || next.InsertTicks > old.InsertTicks || next.Sell != old.Sell || next.RefundEvent != old.RefundEvent;
        internal void Remember(SearchObservation next)
        {
            m_seen.TryGetValue(next.Key, out var old);
            m_seen[next.Key] = new SearchObservation { Key = next.Key, Owned = Math.Max(next.Owned, old?.Owned ?? 0),
                InsertTicks = Math.Max(next.InsertTicks, old?.InsertTicks ?? 0), Sell = next.Sell, RefundEvent = next.RefundEvent };
        }
        internal static SearchHistory Load(string path)
        {
            var history = new SearchHistory();
            if (!File.Exists(path)) return history;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split('\t');
                if ((parts.Length != 4 && parts.Length != 5) || !int.TryParse(parts[1], out int owned) || !long.TryParse(parts[2], out long ticks)
                    || !int.TryParse(parts[3], out int sell) || owned < 0 || ticks < 0) continue;
                if (parts.Length == 5 && !int.TryParse(parts[4], out int _)) continue;
                history.Remember(new SearchObservation { Key = parts[0], Owned = owned, InsertTicks = ticks, Sell = sell, RefundEvent = parts.Length == 5 ? int.Parse(parts[4], CultureInfo.InvariantCulture) : 0 });
            }
            return history;
        }
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllLines(temporary, m_seen.Values.OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => value.Key + "\t"
                + value.Owned.ToString(CultureInfo.InvariantCulture) + "\t" + value.InsertTicks.ToString(CultureInfo.InvariantCulture) + "\t"
                + value.Sell.ToString(CultureInfo.InvariantCulture) + "\t" + value.RefundEvent.ToString(CultureInfo.InvariantCulture)), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
