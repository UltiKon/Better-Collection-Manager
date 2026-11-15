using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using Blizzard.GameService.SDK.Client.Integration;
using PegasusShared;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class DeckArchive
    {
        private sealed class Restore
        {
            internal ArchivedDeck Snapshot;
            internal DeckArchiveStore Store;
            internal string Account;
            internal int? Request;
            internal long DeckId;
            internal float Started;
            internal bool Readback;
        }
        private static Restore s_restore;
        private static Network.DBAction s_response;
        internal static bool Available { get; private set; }
        internal static bool Busy => s_restore != null;
        internal static string AccountKey()
        {
            var id = BattleNet.GetMyGameAccountId();
            return id == null || !id.IsValid() ? null : id.High.ToString("X16") + id.Low.ToString("X16");
        }
        internal static DeckArchiveStore Store()
        {
            string account = AccountKey();
            if (account == null) throw new InvalidOperationException("Wait until your account has loaded.");
            return new DeckArchiveStore(Path.Combine(Path.GetDirectoryName(CollectionQualitySettings.SettingsPath), "deck_archive", account));
        }
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(CollectionManager), "OnDeckCreated", new[] { typeof(NetCache.DeckHeader), typeof(int?) }),
                    postfix: new HarmonyMethod(typeof(DeckArchive), nameof(Created)));
                harmony.Patch(AccessTools.Method(typeof(Network), "GetDeckResponse"), postfix: new HarmonyMethod(typeof(DeckArchive), nameof(Response)));
                harmony.Patch(AccessTools.Method(typeof(CollectionManager), "OnDBAction"), postfix: new HarmonyMethod(typeof(DeckArchive), nameof(ActionFinished)));
                harmony.Patch(AccessTools.Method(typeof(CollectionManager), "FireDeckContentsEvent"), postfix: new HarmonyMethod(typeof(DeckArchive), nameof(Contents)));
                harmony.Patch(AccessTools.Method(typeof(CollectionManager), "UpdateFromDeckContents"), prefix: new HarmonyMethod(typeof(DeckArchive), nameof(PrepareReadback)));
                DeckArchiveUi.Install(harmony);
                DeckArchiveEditor.Install(harmony);
                Available = true;
            }
            catch (Exception ex) { DebugLog.Exception("Deck archive unavailable", ex); }
        }
        internal static ArchivedDeck Capture(CollectionDeck deck)
        {
            var snapshot = new ArchivedDeck();
            void Set(string key, object value) => snapshot.Metadata[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            Set("name", deck.Name); Set("hero", deck.HeroCardID); Set("format", (int)deck.FormatType);
            Set("heroOverridden", deck.HeroOverridden); Set("randomHero", deck.RandomHeroUseFavorite);
            Set("overrideHero", deck.UIHeroOverrideCardID); Set("overridePremium", (int)deck.UIHeroOverridePremium);
            Set("cardBack", deck.CardBackID); Set("coin", deck.CosmeticCoinID); Set("randomCoin", deck.RandomCoinUseFavorite);
            Set("pet", deck.PetID); Set("petVariant", deck.PetVariantID); Set("randomPet", deck.RandomPetUseFavorite);
            Set("runes", string.Join(",", deck.GetRuneOrder().Select(r => ((int)r).ToString(CultureInfo.InvariantCulture))));
            var cards = new Dictionary<string, ArchivedDeck.Card>(StringComparer.Ordinal);
            void Read(CollectionDeck section, string owner)
            {
                foreach (CollectionDeckSlot slot in section.GetSlots())
                {
                    int total = 0;
                    foreach (TAG_PREMIUM premium in new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE })
                    {
                        int count = slot.GetCount(premium); total += count;
                        if (count == 0) continue;
                        string key = owner + "\t" + slot.CardID + "\t" + (int)premium;
                        if (!cards.TryGetValue(key, out var card)) cards[key] = card = new ArchivedDeck.Card { Id = slot.CardID, Owner = owner, Premium = (int)premium };
                        card.Count += count;
                    }
                    if (total != slot.Count) throw new InvalidOperationException("A card quality could not be archived.");
                }
            }
            Read(deck, "");
            foreach (var sideboard in deck.GetAllSideboards()) Read(sideboard.Value, sideboard.Key);
            snapshot.Cards.AddRange(cards.Values); snapshot.Serialize(); return snapshot;
        }
        internal static CollectionDeck Header(ArchivedDeck snapshot, long id)
        {
            var deck = new CollectionDeck { ID = id, Name = snapshot.Name, HeroCardID = snapshot.Metadata["hero"],
                FormatType = (FormatType)int.Parse(snapshot.Metadata["format"], CultureInfo.InvariantCulture) };
            ApplyMetadata(snapshot, deck); return deck;
        }
        private static void ApplyMetadata(ArchivedDeck snapshot, CollectionDeck deck)
        {
            string Get(string key) => snapshot.Metadata.TryGetValue(key, out string value) ? value : "";
            int? Number(string key) => string.IsNullOrEmpty(Get(key)) ? (int?)null : int.Parse(Get(key), CultureInfo.InvariantCulture);
            bool Flag(string key) => bool.Parse(Get(key));
            deck.HeroOverridden = Flag("heroOverridden"); deck.RandomHeroUseFavorite = Flag("randomHero");
            deck.UIHeroOverrideCardID = Get("overrideHero"); deck.UIHeroOverridePremium = (TAG_PREMIUM)(Number("overridePremium") ?? 0);
            deck.CardBackID = Number("cardBack"); deck.CosmeticCoinID = Number("coin"); deck.RandomCoinUseFavorite = Flag("randomCoin");
            deck.PetID = Number("pet"); deck.PetVariantID = Number("petVariant"); deck.RandomPetUseFavorite = Flag("randomPet");
            deck.SetRuneOrder(Get("runes").Length == 0 ? Array.Empty<RuneType>() : Get("runes").Split(',').Select(r => (RuneType)int.Parse(r, CultureInfo.InvariantCulture)).ToArray());
        }
        internal static void Archive(CollectionDeck deck, Action delete)
        {
            Run(() =>
            {
                if (!CollectionQualitySettings.DeckArchiveEnabled) throw new InvalidOperationException("Enable Deck archive in Settings > Mods > Deck archive first.");
                if (Busy || deck == null || deck.Type != DeckType.NORMAL_DECK || deck.IsDeckTemplate || deck.IsBeingDeleted()
                    || deck.IsSavingChanges() || !deck.NetworkContentsLoaded() || CollectionManager.Get().GetDeck(deck.ID) != deck)
                    throw new InvalidOperationException("Wait until the deck has finished loading or saving.");
                Store().Save(Capture(deck)); // Durable write and readback must precede the native deletion.
                DeckArchiveUi.Invalidate(); delete();
            });
        }
        internal static void RestoreDeck(string key)
        {
            Run(() =>
            {
                if (!CollectionQualitySettings.DeckArchiveEnabled) throw new InvalidOperationException("Enable Deck archive in Settings > Mods > Deck archive first.");
                if (Busy) throw new InvalidOperationException("A deck is already being restored.");
                if (!Network.IsLoggedIn()) throw new InvalidOperationException("Connect to Hearthstone to restore a deck.");
                CollectionManager manager = CollectionManager.Get();
                if (manager.IsInEditMode() || !manager.AreAllDeckContentsReady()
                    || AccessTools.Field(typeof(CollectionManager), "m_pendingDeckCreate").GetValue(manager) != null)
                    throw new InvalidOperationException("Finish editing and wait until all decks have loaded.");
                DeckArchiveStore store = Store(); ArchivedDeck snapshot = store.Read(key);
                long? previous = store.RestoreTarget(key);
                if (previous.HasValue && manager.GetDeck(previous.Value) != null)
                {
                    // A matching local model is insufficient: the earlier save
                    // may still be pending or may have failed. Verify the known
                    // server deck again without creating or overwriting a deck.
                    var retry = new Restore { Snapshot = snapshot, Store = store, Account = AccountKey(),
                        DeckId = previous.Value, Started = Time.realtimeSinceStartup };
                    s_restore = retry;
                    try { Readback(retry); } catch { s_restore = null; throw; }
                    return;
                }
                if (manager.GetDecks(DeckType.NORMAL_DECK).Count >= 27) throw new InvalidOperationException("Free a saved deck slot before restoring this deck.");
                // Resolve everything before creating a server deck. Missing or
                // outdated definitions must never turn an archive into an empty deck.
                if (GameUtils.TranslateCardIdToDbId(snapshot.Metadata["hero"]) <= 0
                    || snapshot.Cards.Any(c => GameUtils.TranslateCardIdToDbId(c.Id) <= 0 || c.Owner.Length > 0 && GameUtils.TranslateCardIdToDbId(c.Owner) <= 0))
                    throw new InvalidOperationException("Some archived cards are unavailable in this client.");
                var pending = new Restore { Snapshot = snapshot, Store = store, Account = AccountKey(), Started = Time.realtimeSinceStartup };
                s_restore = pending;
                try
                {
                    Network.Get().CreateDeck(DeckType.NORMAL_DECK, snapshot.Name, GameUtils.TranslateCardIdToDbId(snapshot.Metadata["hero"]),
                        bool.Parse(snapshot.Metadata["heroOverridden"]), (FormatType)int.Parse(snapshot.Metadata["format"], CultureInfo.InvariantCulture),
                        -100L, DeckSourceType.DECK_SOURCE_TYPE_NORMAL, null, null, out pending.Request);
                    if (!pending.Request.HasValue) throw new InvalidOperationException("The deck could not be created. The archive has been kept.");
                }
                catch { s_restore = null; throw; }
            });
        }
        public static void Created(NetCache.DeckHeader deck, int? requestId)
        {
            Restore pending = s_restore;
            if (pending == null || !requestId.HasValue || pending.Request != requestId || pending.Account != AccountKey()) return;
            RunRestore(() =>
            {
                pending.DeckId = deck.ID;
                pending.Store.RememberRestore(pending.Snapshot.Key, deck.ID);
                CollectionDeck target = CollectionManager.Get().GetDeck(deck.ID);
                target.FormatType = (FormatType)int.Parse(pending.Snapshot.Metadata["format"], CultureInfo.InvariantCulture);
                ApplyMetadata(pending.Snapshot, target);
                foreach (var card in pending.Snapshot.Cards.Where(c => c.Owner.Length == 0))
                    for (int i = 0; i < card.Count; i++)
                        if (!target.AddCard(card.Id, (TAG_PREMIUM)card.Premium, true)) throw new InvalidOperationException("An archived card could not be restored.");
                foreach (var card in pending.Snapshot.Cards.Where(c => c.Owner.Length > 0))
                    for (int i = 0; i < card.Count; i++) target.AddCardToSideboard(card.Id, GameUtils.TranslateCardIdToDbId(card.Owner), (TAG_PREMIUM)card.Premium, true);
                target.SendChanges(CollectionDeck.ChangeSource.SaveCurrentDeck);
                if (!target.IsSavingChanges()) Readback(pending);
            });
        }
        public static void Response(Network.DBAction __result) { if (Busy) s_response = __result; }
        public static void ActionFinished()
        {
            Network.DBAction response = s_response; s_response = null;
            Restore pending = s_restore;
            if (pending != null && response != null && pending.DeckId == 0 && response.Action == Network.DBAction.ActionType.CREATE_DECK
                && response.Result != Network.DBAction.ResultType.SUCCESS)
            {
                RunRestore(() => { throw new InvalidOperationException("The server could not create the deck. The archive has been kept."); });
                return;
            }
            if (pending == null || response == null || pending.DeckId == 0 || response.MetaData != pending.DeckId
                || response.Action != Network.DBAction.ActionType.SET_DECK) return;
            RunRestore(() =>
            {
                if (response.Result != Network.DBAction.ResultType.SUCCESS) throw new InvalidOperationException("The server did not save the restored deck. The archive has been kept.");
                Readback(pending);
            });
        }
        private static void Readback(Restore pending)
        { pending.Readback = true; Network.Get().RequestDeckContents(pending.DeckId); }
        public static void PrepareReadback(List<PegasusUtil.DeckContents> __0)
        {
            Restore pending = s_restore;
            if (pending == null || !pending.Readback || pending.Account != AccountKey() || __0 == null || !__0.Any(d => d != null && d.DeckId == pending.DeckId)) return;
            // Native reload clears main slots but retains existing sideboards.
            // This new deck must start its readback with empty sideboards too.
            CollectionManager.Get().GetDeck(pending.DeckId)?.ClearSideboards();
            CollectionManager.Get().GetBaseDeck(pending.DeckId)?.ClearSideboards();
        }
        public static void Contents(long __0)
        {
            long deckId = __0;
            Restore pending = s_restore;
            if (pending == null || !pending.Readback || pending.DeckId != deckId || pending.Account != AccountKey()) return;
            RunRestore(() =>
            {
                if (Capture(CollectionManager.Get().GetDeck(deckId)).Serialize() != pending.Snapshot.Serialize())
                    throw new InvalidOperationException("The client changed some restored cards or cosmetics. The saved deck is available; the original archive has been kept.");
                pending.Store.Delete(pending.Snapshot.Key); DeckArchiveEditor.Delete(pending.Snapshot.Key); s_restore = null;
                DeckArchiveUi.Restored(pending.Snapshot.Key);
            });
        }
        private static void RunRestore(Action action)
        {
            try { action(); }
            catch (Exception ex) { s_restore = null; DeckArchiveUi.Invalidate(); DeckArchiveUi.Refresh(); Error(ex); }
        }
        internal static void Run(Action action) { try { action(); } catch (Exception ex) { Error(ex); } }
        internal static void Error(Exception ex)
        {
            DebugLog.Exception("Deck archive operation failed", ex);
            DialogManager.Get().ShowPopup(new AlertPopup.PopupInfo { m_headerText = "Deck archive", m_text = ex.Message,
                m_richTextEnabled = false, m_responseDisplay = AlertPopup.ResponseDisplay.OK });
        }
        internal static void Update()
        {
            DeckArchiveEditor.Update();
            if (s_restore != null && (s_restore.Account != AccountKey() || Time.realtimeSinceStartup - s_restore.Started > 30f))
            {
                bool sameAccount = s_restore.Account == AccountKey(); s_restore = null;
                if (sameAccount) Error(new TimeoutException("Restore was not confirmed in time. The archive has been kept. Check Saved before trying again."));
            }
            if (Available) DeckArchiveUi.Update();
        }
        internal static void Shutdown()
        {
            DeckArchiveEditor.Shutdown();
            s_restore = null; s_response = null;
            if (Available) DeckArchiveUi.ShowSaved();
            DeckArchivePopup.Shutdown();
            Available = false; DeckArchiveUi.Shutdown();
        }
    }
}
