using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    // The native editor receives a local model through scoped lookups. Neither
    // this model nor its baseline enters the account's server deck dictionaries.
    internal static class DeckArchiveEditor
    {
        private sealed class Session
        {
            internal string Key, Account, Saved;
            internal DeckArchiveStore Store;
            internal CollectionDeck Deck, Baseline;
            internal bool Deleted;
            internal bool ErrorShown;
        }
        private static Session s_session;
        private static float s_nextSave;
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            void Prefix(Type type, string name, string handler) => harmony.Patch(AccessTools.Method(type, name),
                prefix: new HarmonyMethod(typeof(DeckArchiveEditor), handler));
            Prefix(typeof(CollectionManager), "GetDeck", nameof(GetDeck));
            Prefix(typeof(CollectionManager), "GetBaseDeck", nameof(GetBaseline));
            Prefix(typeof(CollectionManager), "SetEditedDeck", nameof(BeforeSelection));
            Prefix(typeof(CollectionManager), "DoneEditing", nameof(BeforeDone));
            Prefix(typeof(CollectionDeck), "SendDeckRenameChange", nameof(SendRename));
            Prefix(typeof(CollectionDeckTray), "SaveDeck", nameof(SaveDeck));
            Prefix(typeof(CollectionDeckTray), "OnBackOutOfDeckContentsImpl", nameof(BeforeBack));
            Prefix(typeof(Network), "DeleteDeck", nameof(BlockNetwork));
            Prefix(typeof(Network), "RenameDeck", nameof(BlockNetwork));
            Prefix(typeof(Network), "SendDeckData", nameof(BlockDeckData));
            Prefix(typeof(Network), "RequestDeckContents", nameof(BlockContents));
        }
        internal static CollectionDeck Prepare(string key, long id)
        {
            if (!CollectionQualitySettings.DeckArchiveEnabled) throw new InvalidOperationException("Deck archive is disabled in Settings > Mods > Deck archive.");
            if (s_session != null && !s_session.Deleted && s_session.Key == key && s_session.Deck.ID == id && s_session.Account == DeckArchive.AccountKey()) return s_session.Deck;
            if (!TrySave()) throw new InvalidOperationException("Save the current local deck before opening another archive.");
            DeckArchiveStore store = DeckArchive.Store();
            ArchivedDeck snapshot = store.Read(key);
            CollectionDeck deck = DeckArchive.Header(snapshot, id);
            void Fill(CollectionDeck target, string owner)
            {
                foreach (var card in snapshot.Cards.Where(c => c.Owner == owner))
                    if (target.InsertSlotWithCard(card.Id, (TAG_PREMIUM)card.Premium,
                        target.CanCardBeAddedAsOwned(card.Id, (TAG_PREMIUM)card.Premium), card.Count) == null)
                        throw new InvalidOperationException("An archived card could not be loaded: " + card.Id);
            }
            Fill(deck, "");
            var sideboards = (SideboardManager)AccessTools.Field(typeof(CollectionDeck), "m_sideboardManager").GetValue(deck);
            foreach (string owner in snapshot.Cards.Where(c => c.Owner.Length > 0).Select(c => c.Owner).Distinct())
            {
                var ownerCard = snapshot.Cards.FirstOrDefault(c => c.Owner.Length == 0 && c.Id == owner);
                CollectionDeck sideboard = sideboards.GetOrCreateSideboard(owner, (TAG_PREMIUM)(ownerCard?.Premium ?? 0), false);
                if (sideboard == null) throw new InvalidOperationException("The archived sideboard is unavailable: " + owner);
                Fill(sideboard, owner);
            }
            // Adding slots does not rewrite qualities, quantities or rune order.
            // Reject an incomplete load before the editor can save anything.
            if (DeckArchive.Capture(deck).Serialize() != snapshot.Serialize()) throw new InvalidOperationException("The complete archive could not be loaded.");
            deck.MarkNetworkContentsLoaded();
            var baseline = new CollectionDeck(); baseline.CopyFrom(deck);
            s_session = new Session { Key = key, Account = DeckArchive.AccountKey(), Store = store,
                Deck = deck, Baseline = baseline, Saved = snapshot.Serialize() };
            s_nextSave = Time.realtimeSinceStartup + 1f;
            return deck;
        }
        internal static bool Owns(CollectionDeck deck) => deck != null && s_session != null && ReferenceEquals(s_session.Deck, deck);
        internal static bool IsLocalId(long id) => s_session?.Deck.ID == id || DeckArchiveUi.IsLocalId(id);
        internal static bool GetDeck(long __0, ref CollectionDeck __result)
        {
            if (s_session == null || s_session.Deck.ID != __0 || s_session.Account != DeckArchive.AccountKey()) return true;
            __result = s_session.Deck; return false;
        }
        internal static bool GetBaseline(long __0, ref CollectionDeck __result)
        {
            if (s_session == null || s_session.Deck.ID != __0 || s_session.Account != DeckArchive.AccountKey()) return true;
            __result = s_session.Baseline; return false;
        }
        internal static bool TrySave()
        {
            if (s_session == null || s_session.Deleted) return true;
            try
            {
                ArchivedDeck snapshot = DeckArchive.Capture(s_session.Deck);
                string text = snapshot.Serialize();
                if (text != s_session.Saved)
                {
                    s_session.Store.Update(s_session.Key, snapshot);
                    s_session.Saved = text;
                    s_session.Baseline.CopyFrom(s_session.Deck);
                    DeckArchiveUi.Edited(s_session.Deck, s_session.Key);
                }
                s_session.Deck.OnNameChangeComplete(); s_session.Deck.OnContentChangesComplete();
                s_session.ErrorShown = false; return true;
            }
            catch (Exception ex)
            {
                if (!s_session.ErrorShown) { s_session.ErrorShown = true; DeckArchive.Error(ex); }
                else DebugLog.Exception("Local deck save is still pending", ex);
                return false;
            }
        }
        internal static bool Send(CollectionDeck deck)
        {
            if (!Owns(deck)) return !IsLocalId(deck.ID);
            TrySave(); return false;
        }
        internal static bool SendRename(CollectionDeck __instance) => Send(__instance);
        internal static bool SaveDeck(CollectionDeck deck)
        {
            if (!Owns(deck)) return true;
            TrySave(); return false;
        }
        internal static bool BeforeSelection(CollectionDeck deck) => s_session == null || ReferenceEquals(deck, s_session.Deck) || TrySave();
        internal static bool BeforeDone() => TrySave();
        internal static bool BeforeBack(ref bool __result)
        {
            if (!Owns(CollectionManager.Get()?.GetEditedDeck()) || TrySave()) return true;
            __result = false; return false;
        }
        internal static bool BlockNetwork(long __0) => !IsLocalId(__0);
        internal static bool BlockDeckData(long __2) => !IsLocalId(__2);
        internal static bool BlockContents(ref long[] __0)
        {
            __0 = __0.Where(id => !IsLocalId(id)).ToArray(); return __0.Length > 0;
        }
        internal static bool Delete(string key)
        {
            if (s_session?.Key != key) return true;
            // A deliberate archive deletion must not be recreated by autosave.
            s_session.Deleted = true; return true;
        }
        internal static void Update()
        {
            if (s_session == null || s_session.Deleted || !Owns(CollectionManager.Get()?.GetEditedDeck())
                || Time.realtimeSinceStartup < s_nextSave) return;
            s_nextSave = Time.realtimeSinceStartup + 1f;
            TrySave();
        }
        internal static void Shutdown() { TrySave(); s_session = null; }
    }
}
