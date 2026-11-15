using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PegasusShared;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class DeckArchiveUi
    {
        private const int PageSize = 26; // Leave the final native slot for pagination.
        private static bool s_archived;
        private static int s_page;
        private static string s_account;
        private static DeckTrayDeckListContent s_content;
        private static CollectionDeckTrayButton s_selector;
        private static GameObject s_selectorRoot;
        private static DeckTrayDeckListContent s_shiftedContent;
        private static Vector3 s_originalStart;
        private static CollectionDeckTray s_titleTray;
        private static string s_originalTitle;
        private static long s_nextId;
        private static readonly Dictionary<string, long> Ids = new Dictionary<string, long>(StringComparer.Ordinal);
        private static float s_nextUiUpdate;
        private static List<ArchivedDeck> s_entries;
        private static readonly Dictionary<long, ArchivedDeck> Entries = new Dictionary<long, ArchivedDeck>();
        private static readonly Dictionary<long, CollectionDeck> Headers = new Dictionary<long, CollectionDeck>();
        private static int Pages => Math.Max(1, ((s_entries?.Count ?? 0) + PageSize - 1) / PageSize);
        private static readonly System.Reflection.MethodInfo UpdateTrays = AccessTools.Method(typeof(DeckTrayDeckListContent), "UpdateAllTrays");
        private static readonly System.Reflection.MethodInfo CountChanged = AccessTools.Method(typeof(DeckTrayDeckListContent), "FireDeckCountChangedEvent");
        private static readonly System.Reflection.MethodInfo PositionTrays = AccessTools.Method(typeof(DeckTrayDeckListContent), "RefreshTraySectionPositions");
        private static readonly System.Reflection.MethodInfo PositionNew = AccessTools.Method(typeof(DeckTrayDeckListContent), "UpdateNewDeckButton");
        private static readonly System.Reflection.MethodInfo FormatElements = AccessTools.Method(typeof(CollectionDeckBoxVisual), "GetActiveFormatElements");
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            void Patch(Type type, string method, string prefix = null, string postfix = null, Type[] arguments = null)
                => harmony.Patch(AccessTools.Method(type, method, arguments), prefix == null ? null : new HarmonyMethod(typeof(DeckArchiveUi), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(DeckArchiveUi), postfix));
            Patch(typeof(DeckOptionsMenu), "OnDeleteButtonReleased", nameof(DeleteFromMenu));
            Patch(typeof(CollectionDeckBoxVisual), "OnDeleteButtonPressed", nameof(DeleteFromBox));
            Patch(typeof(DeckTrayDeckListContent), "GetActiveDecks", nameof(ActiveDecks));
            Patch(typeof(DeckTrayDeckListContent), "UpdateDeckTrayVisuals", postfix: nameof(Visuals), arguments: new[] { typeof(bool), typeof(TAG_CLASS) });
            Patch(typeof(CollectionDeckBoxVisual), "GetCollectionDeck", nameof(BoxDeck));
            Patch(typeof(DeckTrayDeckListContent), "OnDeckBoxVisualRelease", nameof(OpenArchived));
            Patch(typeof(DeckTrayDeckListContent), "get_CanDragToReorderDecks", postfix: nameof(CanDrag));
            Patch(typeof(DeckTrayDeckListContent), "CanShowNewDeckButton", nameof(CanShowNew));
            Patch(typeof(DeckTrayDeckListContent), "OnNewDeckButtonPress", nameof(NewButton));
            Patch(typeof(DeckTrayDeckListContent), "DeleteDeck", nameof(GuardDelete));
            Patch(typeof(CollectionDeck), "SendChanges", nameof(GuardSend));
            Patch(typeof(DeckTrayDeckListContent), "OnDeckDeleted", nameof(DeletedWhileArchived));
            Patch(typeof(DeckTrayDeckListContent), "HideTraySectionsNotInBounds", postfix: nameof(ClipSelector));
            DeckArchivePopup.Install(harmony);
        }
        private static bool Eligible()
        {
            CollectionManager manager = CollectionManager.Get();
            return DeckArchive.Available && NetCache.Get() != null && manager != null && manager.IsFullyLoaded()
                && manager.GetCollectibleDisplay() is CollectionManagerDisplay && KeywordFilterUi.IsCollectionSessionActive
                && SceneMgr.Get() != null && !SceneMgr.Get().IsInTavernBrawlMode() && !SceneMgr.Get().IsInLettuceMode()
                && !CollectionManagerDisplay.IsSpecialOneDeckMode();
        }
        private static bool ArchiveView(DeckTrayDeckListContent content) => CollectionQualitySettings.DeckArchiveEnabled && s_archived && content == s_content && Eligible();
        internal static void Invalidate() { s_entries = null; }
        private static void Load()
        {
            if (s_entries != null) return;
            int invalid = 0;
            var entries = DeckArchive.Store().ReadAll((file, ex) => { invalid++; DebugLog.Warning("Deck archive ignored: " + file + ": " + ex.Message); });
            Entries.Clear(); Headers.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!Ids.TryGetValue(entries[i].Key, out long id)) Ids[entries[i].Key] = id = long.MaxValue - s_nextId++;
                // Models stay outside the account's server deck dictionaries.
                try { Headers[id] = DeckArchive.Header(entries[i], id); Entries[id] = entries[i]; }
                catch (Exception ex) { invalid++; DebugLog.Warning("Deck archive header ignored: " + ex.Message); }
            }
            s_entries = entries.Where(e => Entries.ContainsValue(e)).ToList();
            s_page = Math.Min(s_page, Pages - 1);
            if (invalid > 0) DialogManager.Get().ShowPopup(new AlertPopup.PopupInfo { m_headerText = "Deck archive",
                m_text = invalid + " archive file(s) could not be read. The files have been kept; see the mod log for details.",
                m_responseDisplay = AlertPopup.ResponseDisplay.OK });
        }
        internal static void ShowSaved() { s_archived = false; s_page = 0; Refresh(); }
        private static void ShowArchived()
        {
            DeckArchive.Run(() => { Invalidate(); Load(); s_archived = true; s_page = 0; Refresh(); });
        }
        internal static void Refresh()
        {
            if (s_content == null || !Eligible()) return;
            DeckArchive.Run(() =>
            {
                if (s_archived) Load();
                UpdateTrays.Invoke(s_content, new object[] { true, true });
                CountChanged.Invoke(s_content, null);
                s_content.m_deckTray.m_scrollbar?.SetScrollImmediate(0f);
                UpdateCaption();
            });
        }
        public static bool ActiveDecks(DeckTrayDeckListContent __instance, ref List<CollectionDeck> __result)
        {
            if (!ArchiveView(__instance)) return true;
            __result = s_entries.Skip(s_page * PageSize).Take(PageSize).Select(entry => Headers[Ids[entry.Key]]).ToList(); return false;
        }
        public static void Visuals(DeckTrayDeckListContent __instance, int __result)
        {
            if (__instance != s_content) return;
            for (int i = __result; ; i++)
            {
                TraySection section = __instance.GetTraySection(i);
                if (section == null) break;
                section.m_deckBox.SetDeckID(-1);
            }
        }
        public static bool BoxDeck(CollectionDeckBoxVisual __instance, ref CollectionDeck __result)
        {
            if (!Headers.TryGetValue(__instance.GetDeckID(), out CollectionDeck header)) return true;
            __result = header; return false;
        }
        public static bool OpenArchived(DeckTrayDeckListContent __instance, TraySection traySection)
        {
            if (DeckArchive.Busy) return false;
            if (!Entries.TryGetValue(traySection.m_deckBox.GetDeckID(), out ArchivedDeck entry)) return true;
            if (CollectionManager.Get().IsInEditMode()) return true;
            if (!__instance.IsModeActive() || __instance.m_deckTray.IsUpdatingTrayMode()) return false;
            bool ready = false;
            DeckArchive.Run(() => {
                long id = traySection.m_deckBox.GetDeckID();
                Headers[id] = DeckArchiveEditor.Prepare(entry.Key, id); ready = true;
            });
            return ready; // Continue through the native deck-entry path.
        }
        internal static bool IsLocalId(long id) => Headers.ContainsKey(id);
        internal static void Edited(CollectionDeck deck, string key)
        {
            if (!Headers.ContainsKey(deck.ID)) return;
            Headers[deck.ID] = deck;
            var snapshot = DeckArchive.Capture(deck); snapshot.Key = key; Entries[deck.ID] = snapshot;
            int index = s_entries?.FindIndex(entry => entry.Key == key) ?? -1;
            if (index >= 0) s_entries[index] = snapshot;
        }
        public static void CanDrag(DeckTrayDeckListContent __instance, ref bool __result)
        { if (ArchiveView(__instance) || DeckArchive.Busy) __result = false; }
        public static bool GuardDelete(long deckID) => !DeckArchiveEditor.IsLocalId(deckID);
        public static bool GuardSend(CollectionDeck __instance) => DeckArchiveEditor.Send(__instance);
        public static bool DeletedWhileArchived(DeckTrayDeckListContent __instance)
        {
            // A server delete may finish after the view was switched. Its native
            // animation must not remove an unrelated archived tray section.
            if (!ArchiveView(__instance)) return true;
            AccessTools.Field(typeof(DeckTrayDeckListContent), "m_waitingToDeleteDeck").SetValue(__instance, false);
            return false;
        }
        public static bool CanShowNew(DeckTrayDeckListContent __instance, ref bool __result)
        {
            if (!ArchiveView(__instance)) return true;
            __result = Pages > 1; return false;
        }
        public static bool NewButton(DeckTrayDeckListContent __instance)
        {
            if (DeckArchive.Busy) return false;
            if (!ArchiveView(__instance)) return true;
            if (Pages > 1) { s_page = (s_page + 1) % Pages; Refresh(); }
            return false;
        }
        public static bool DeleteFromMenu(DeckOptionsMenu __instance)
        {
            CollectionDeck deck = AccessTools.Field(typeof(DeckOptionsMenu), "m_deck").GetValue(__instance) as CollectionDeck;
            if (!CollectionQualitySettings.DeckArchiveEnabled && DeckArchiveEditor.Owns(deck)) return false;
            if (deck != null && Entries.TryGetValue(deck.ID, out ArchivedDeck entry))
            {
                (AccessTools.Field(typeof(DeckOptionsMenu), "m_deckInfo").GetValue(__instance) as CollectionDeckInfo)?.Hide();
                ConfirmArchived(entry); return false;
            }
            if (!CanArchive(deck)) return true;
            if (DeckArchive.Busy) return false;
            (AccessTools.Field(typeof(DeckOptionsMenu), "m_deckInfo").GetValue(__instance) as CollectionDeckInfo)?.Hide();
            ConfirmSaved(deck, () =>
            {
                if (CollectionManager.Get().GetEditedDeck()?.ID != deck.ID) return;
                CollectionDeckTray.Get().DeleteEditingDeck();
                (CollectionManager.Get().GetCollectibleDisplay() as CollectionManagerDisplay)?.OnDoneEditingDeck();
            });
            return false;
        }
        public static bool DeleteFromBox(CollectionDeckBoxVisual __instance)
        {
            // OnDeleteButtonPressed normally plays this before showing AlertPopup.
            // Our prefix replaces that handler, so preserve its native feedback.
            const string pressSound = "tiny_button_press_1.prefab:44fc68b7418870b4797b85f0ca88a8db";
            if (Entries.TryGetValue(__instance.GetDeckID(), out ArchivedDeck entry))
            {
                if (!DeckArchive.Busy && CollectionQualitySettings.DeckArchiveEnabled)
                { SoundManager.Get().LoadAndPlay(pressSound, __instance.gameObject); ConfirmArchived(entry); }
                return false;
            }
            if (!Eligible() || CollectionDeckTray.Get().IsShowingDeckContents()) return true;
            CollectionDeck deck = __instance.GetCollectionDeck();
            if (!CanArchive(deck)) return true;
            if (DeckArchive.Busy) return false;
            long id = deck.ID;
            SoundManager.Get().LoadAndPlay(pressSound, __instance.gameObject);
            ConfirmSaved(deck, () => { if (CollectionManager.Get().GetDeck(id) == deck) s_content.DeleteDeck(id); });
            return false;
        }
        private static bool CanArchive(CollectionDeck deck) => CollectionQualitySettings.DeckArchiveEnabled && Eligible() && deck != null && deck.Type == DeckType.NORMAL_DECK && !deck.IsDeckTemplate;
        private static void ConfirmSaved(CollectionDeck deck, Action delete)
        {
            string account = DeckArchive.AccountKey();
            DialogManager.Get().ShowPopup(new AlertPopup.PopupInfo {
                m_id = DeckArchivePopup.Id, m_headerText = GameStrings.Get("GLUE_COLLECTION_DELETE_CONFIRM_HEADER"),
                m_text = deck.Name + "\n\n" + GameStrings.Get("GLUE_COLLECTION_DELETE_CONFIRM_DESC"),
                m_showAlertIcon = false, m_richTextEnabled = false, m_okText = "Archive instead",
                m_alertTextAlignment = UberText.AlignmentOptions.Center,
                m_responseDisplay = AlertPopup.ResponseDisplay.CONFIRM_CANCEL,
                m_responseCallback = (response, _) =>
                {
                    if (response == AlertPopup.Response.CANCEL || account != DeckArchive.AccountKey() || DeckArchive.Busy) return;
                    if (response == AlertPopup.Response.OK) { if (CollectionQualitySettings.DeckArchiveEnabled) DeckArchive.Archive(deck, delete); }
                    else DeckArchive.Run(delete);
                } });
        }
        private static bool FinishLocalEditing()
        {
            if (!DeckArchiveEditor.Owns(CollectionManager.Get().GetEditedDeck())) return true;
            if (!DeckArchiveEditor.TrySave()) return false;
            if (Navigation.CanGoBack) Navigation.GoBack();
            return !CollectionManager.Get().IsInEditMode();
        }
        private static void ConfirmArchived(ArchivedDeck entry)
        {
            if (DeckArchive.Busy || !CollectionQualitySettings.DeckArchiveEnabled) return;
            string account = DeckArchive.AccountKey();
            DialogManager.Get().ShowPopup(new AlertPopup.PopupInfo {
                m_id = DeckArchivePopup.Id, m_headerText = GameStrings.Get("GLUE_COLLECTION_DELETE_CONFIRM_HEADER"),
                m_text = entry.Name + "\n\nDelete this local archive or restore it to Saved.",
                m_showAlertIcon = false, m_richTextEnabled = false, m_okText = "Restore instead", m_confirmText = "Delete permanently",
                m_alertTextAlignment = UberText.AlignmentOptions.Center,
                m_responseDisplay = AlertPopup.ResponseDisplay.CONFIRM_CANCEL,
                m_responseCallback = (response, _) =>
                {
                    if (response == AlertPopup.Response.CANCEL || account != DeckArchive.AccountKey() || DeckArchive.Busy) return;
                    if (response == AlertPopup.Response.OK)
                    { if (FinishLocalEditing()) DeckArchive.RestoreDeck(entry.Key); }
                    else DeckArchive.Run(() =>
                    {
                        if (!FinishLocalEditing()) return;
                        DeckArchive.Store().Delete(entry.Key); DeckArchiveEditor.Delete(entry.Key);
                        Invalidate(); Load(); Refresh();
                    });
                } });
        }
        internal static void Update()
        {
            if (!CollectionQualitySettings.DeckArchiveEnabled && !DeckArchive.Busy
                && !DeckArchiveEditor.Owns(CollectionManager.Get()?.GetEditedDeck()))
            {
                if (s_content != null) { ShowSaved(); Shutdown(); }
                return;
            }
            CollectionDeckTray tray = CollectionDeckTray.Get();
            bool eligible = tray != null && Eligible();
            DeckTrayDeckListContent content = eligible ? tray.GetDecksContent() : null;
            string account = eligible ? DeckArchive.AccountKey() : null;
            if (s_content != content || s_account != account)
            {
                bool refresh = content != null && s_content == content && s_archived;
                Shutdown(); s_content = content; s_account = account;
                if (refresh) Refresh();
            }
            if (content == null || account == null || content.GetTraySection(0) == null) return;
            bool visible = CollectionQualitySettings.DeckArchiveEnabled && content.IsModeActive() && !CollectionManager.Get().IsInEditMode();
            if (s_selector == null && visible) DeckArchive.Run(() => CreateSelector(tray));
            if (s_selector == null) return;
            s_selectorRoot.SetActive(visible);
            s_selector.SetIsUsable(visible);
            if (!visible) return;
            if (Time.realtimeSinceStartup >= s_nextUiUpdate)
            {
                s_nextUiUpdate = Time.realtimeSinceStartup + .1f;
                UpdateCaption(); PositionSelector();
            }
            if (tray.m_scrollbar != null) ClipSelector(content, tray.m_scrollbar.m_ScrollBounds.bounds);
        }
        private static void CreateSelector(CollectionDeckTray tray)
        {
            s_titleTray = tray;
            s_originalTitle = tray.m_myDecksLabel.Text;
            // Keep the complete New Deck asset at its authored size. A live
            // source can be clipped or folded down when it is copied.
            GameObject original = s_content.m_newDeckButtonContainer;
            s_selectorRoot = UnityEngine.Object.Instantiate(original, original.transform.parent, false);
            s_selectorRoot.name = "BCM_DeckArchiveSwitchRow";
            s_selector = s_selectorRoot.GetComponentInChildren<CollectionDeckTrayButton>(true);
            s_selectorRoot.SetActive(true); s_selector.gameObject.SetActive(true);
            s_selector.ClearEventListeners(); FilterButtonFeedback.RestoreNativeListeners(s_selector);
            s_selector.m_buttonText.GameStringLookup = s_selector.m_buttonText.RichText = false;
            s_selector.m_buttonText.ResizeToFit = true; s_selector.m_buttonText.ResizeToFitAndGrow = false;
            s_selectorRoot.transform.localScale = original.transform.localScale;
            s_selector.SetIsUsable(true);
            DeckArchiveSwitchPose.Open(s_selector);
            foreach (UIBScrollableItem item in s_selectorRoot.GetComponentsInChildren<UIBScrollableItem>(true))
                item.SetCustomActiveState(() => s_selectorRoot != null && s_selectorRoot.activeInHierarchy);
            s_selector.AddEventListener(UIEventType.RELEASE, _ =>
            {
                if (!CanSwitch()) return;
                bool previous = s_archived;
                if (s_archived) ShowSaved(); else ShowArchived();
                if (previous != s_archived)
                {
                    // Use the native format's authored selection sound, including asset ID.
                    var elements = FormatElements.Invoke(s_content.GetTraySection(0).m_deckBox, null) as CollectionDeckBoxVisual.FormatElements;
                    FilterButtonFeedback.PlayClickSound(string.IsNullOrEmpty(elements?.deckSelectSound) ? null : elements.deckSelectSound);
                }
            });
            // Reserve the first native row for the switch. Changing the native
            // start anchor also covers reorder/delete/entrance layout passes.
            s_shiftedContent = s_content;
            s_originalStart = s_content.m_traySectionStartPos.localPosition;
            Vector3 step = s_content.GetTraySection(1).transform.localPosition - s_content.GetTraySection(0).transform.localPosition;
            s_content.m_traySectionStartPos.localPosition = s_originalStart + step;
            PositionTrays.Invoke(s_content, new object[] { false });
            PositionNew.Invoke(s_content, new object[] { null });
            PositionSelector(); UpdateCaption();
            s_content.m_deckTray.m_scrollbar?.SetScrollImmediate(0f);
        }
        private static void PositionSelector()
        {
            if (s_selectorRoot == null || s_content == null) return;
            Transform parent = s_content.m_traySectionStartPos.parent;
            s_selectorRoot.transform.position = parent.TransformPoint(s_originalStart + s_content.DeckButtonOffset);
        }
        public static void ClipSelector(DeckTrayDeckListContent __instance, Bounds bounds)
        {
            if (__instance != s_content || s_selector == null || s_selectorRoot == null || !s_selectorRoot.activeInHierarchy) return;
            UIBScrollableItem item = s_selectorRoot.GetComponent<UIBScrollableItem>();
            if (item == null) return;
            item.GetWorldBounds(out Vector3 min, out Vector3 max);
            var area = new Bounds(); area.SetMinMax(min, max);
            s_selector.gameObject.SetActive(bounds.Intersects(area));
        }
        private static void UpdateCaption()
        {
            if (s_selector == null || s_content == null) return;
            string caption = s_archived ? "Show saved decks" : "Show archived decks";
            if (s_selector.m_buttonText.Text != caption) s_selector.m_buttonText.Text = caption;
            s_selector.SetEnabled(CanSwitch());
            s_content.m_newDeckButton.m_buttonText.GameStringLookup = false;
            string newCaption = s_archived ? "Next archive page" : GameStrings.Get("GLUE_COLLECTION_NEW_DECK");
            if (s_content.m_newDeckButton.m_buttonText.Text != newCaption) s_content.m_newDeckButton.m_buttonText.Text = newCaption;
            var tray = CollectionDeckTray.Get();
            tray.m_myDecksLabel.Text = s_archived ? (Pages > 1 ? "Archived " + (s_page + 1) + "/" + Pages : "Archived") : s_originalTitle;
            if (s_archived)
            {
                s_content.m_newDeckButton.SetEnabled(!DeckArchive.Busy && Pages > 1);
                tray.m_countText.Text = (s_entries?.Count ?? 0).ToString();
            }
        }
        private static bool CanSwitch() => CollectionQualitySettings.DeckArchiveEnabled && Eligible() && !DeckArchive.Busy && s_content != null
            && !CollectionManager.Get().IsInEditMode() && !s_content.IsDeletingDecks() && !s_content.IsWaitingToDeleteDeck()
            && s_content.NumDecksToDelete() == 0 && !CollectionManager.Get().GetDecks(DeckType.NORMAL_DECK).Any(deck => deck.IsBeingDeleted());
        internal static void Shutdown()
        {
            if (s_selectorRoot != null) { s_selectorRoot.SetActive(false); UnityEngine.Object.Destroy(s_selectorRoot); }
            if (s_shiftedContent != null)
            {
                s_shiftedContent.m_traySectionStartPos.localPosition = s_originalStart;
                // The display may outlive NetCache during shutdown. Restore the
                // anchor, but never rebuild native deck UI after account teardown.
                if (Eligible())
                {
                    PositionTrays.Invoke(s_shiftedContent, new object[] { false });
                    PositionNew.Invoke(s_shiftedContent, new object[] { null });
                }
            }
            if (s_titleTray != null && s_originalTitle != null) s_titleTray.m_myDecksLabel.Text = s_originalTitle;
            s_selector = null; s_selectorRoot = null; s_titleTray = null; s_originalTitle = null; s_content = null; s_account = null;
            s_shiftedContent = null;
            s_archived = false; s_page = 0; s_nextUiUpdate = 0; s_entries = null; Entries.Clear(); Headers.Clear(); Ids.Clear(); s_nextId = 0;
        }
    }
}
