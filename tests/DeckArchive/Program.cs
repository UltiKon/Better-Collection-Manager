using BetterCollectionManager;
using PegasusShared;
using Blizzard.GameService.SDK.Client.Integration;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
void Throws(Action action, string message) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, message); }
string root = Path.Combine(Path.GetTempPath(), "bcm-archive-tests-" + Guid.NewGuid().ToString("N"));
void Reset()
{
    DeckArchive.Shutdown();
    DeckArchiveUi.RestoredKeys.Clear(); DeckArchiveUi.Saved = 0;
    CollectionManager.Instance = new(); Network.Instance = new(); Network.Online = true;
    BattleNet.Current = new Account(); Time.realtimeSinceStartup = 0;
    CollectionQualitySettings.SettingsPath = Path.Combine(root, Guid.NewGuid().ToString("N"), "settings.json");
    CollectionQualitySettings.DeckArchiveEnabled = true;
    DialogManager.Instance.Errors.Clear();
}
CollectionDeck Fixture(long id = 7)
{
    var deck = new CollectionDeck { ID = id, Name = "Über Deck <1>", HeroCardID = "HERO_01", FormatType = FormatType.FT_WILD,
        UIHeroOverrideCardID = "HERO_01", UIHeroOverridePremium = TAG_PREMIUM.GOLDEN, CardBackID = 8, CosmeticCoinID = 9,
        PetID = 10, PetVariantID = 11, RandomHeroUseFavorite = false, RandomPetUseFavorite = false };
    deck.SetRuneOrder(RuneType.RT_BLOOD, RuneType.RT_FROST, RuneType.RT_UNHOLY);
    deck.AddCard("CARD_A", TAG_PREMIUM.NORMAL, true); deck.AddCard("CARD_A", TAG_PREMIUM.GOLDEN, true);
    deck.AddCard("CARD_B", TAG_PREMIUM.DIAMOND, true); deck.AddCard("OWNER", TAG_PREMIUM.SIGNATURE, true);
    deck.AddCardToSideboard("CARD_B", 4, TAG_PREMIUM.GOLDEN, true);
    return deck;
}
ArchivedDeck Save() => DeckArchive.Store().Save(DeckArchive.Capture(Fixture()));
CollectionDeck Created(ArchivedDeck snapshot, long id = 20)
{
    var target = new CollectionDeck { ID = id, Name = snapshot.Name, HeroCardID = "HERO_01", FormatType = FormatType.FT_WILD };
    CollectionManager.Get().Decks[id] = target;
    DeckArchive.Created(new NetCache.DeckHeader { ID = id }, Network.Get().Requests);
    return target;
}
void Ack(long id, bool success = true)
{
    DeckArchive.Response(new Network.DBAction { MetaData = id, Action = Network.DBAction.ActionType.SET_DECK,
        Result = success ? Network.DBAction.ResultType.SUCCESS : Network.DBAction.ResultType.CONSTRAINT });
    DeckArchive.ActionFinished();
}
try
{
    Reset();
    ArchivedDeck snapshot = DeckArchive.Capture(Fixture());
    string text = snapshot.Serialize();
    Check(ArchivedDeck.Parse(text).Serialize() == text, "Lossless qualities, sideboard, Unicode name and cosmetics");
    Check(snapshot.Cards.Count == 5 && snapshot.Cards.Any(c => c.Owner == "OWNER" && c.Premium == 1), "Sideboard owner and quality retained");
    snapshot.Cards.Reverse(); Check(snapshot.Serialize() == text, "Canonical serialization independent of card order");
    Check(DeckArchive.Capture(new CollectionDeck { Name = "Empty", HeroCardID = "HERO_01", FormatType = FormatType.FT_STANDARD }).Cards.Count == 0, "Empty decks supported");
    Throws(() => ArchivedDeck.Parse(text.Replace("BCM-DECK-ARCHIVE 1", "BCM-DECK-ARCHIVE 2")), "Unknown versions rejected");
    Throws(() => ArchivedDeck.Parse(text + "card\t\tQ0FSRF9B\t0\t0\n"), "Zero counts rejected");
    Throws(() => ArchivedDeck.Parse(text + text.Split('\n').First(l => l.StartsWith("card\t")) + "\n"), "Duplicate card entries rejected");
    Throws(() => ArchivedDeck.Parse(new string('x', ArchivedDeck.MaxBytes + 1)), "Oversized snapshots rejected");
    var store = DeckArchive.Store(); ArchivedDeck saved = store.Save(snapshot);
    Check(store.Read(saved.Key).Serialize() == text, "Durable file reload");
    Throws(() => store.Read("../escape"), "Path traversal rejected");
    Check(store.RestoreTarget(saved.Key) == null, "No restore receipt before creation");
    store.RememberRestore(saved.Key, 23); Check(store.RestoreTarget(saved.Key) == 23, "Restore target survives store recreation");
    store.Delete(saved.Key); Check(store.ReadAll((_, _) => { }).Count == 0, "Permanent deletion removes archive");
    Check(store.RestoreTarget(saved.Key) == null, "Deletion removes restore receipt");
    var sameNameA = store.Save(snapshot); var sameNameB = store.Save(snapshot);
    Check(sameNameA.Key != sameNameB.Key && store.ReadAll((_, _) => { }).Count == 2, "Identical deck names do not overwrite archives");
    string archiveDirectory = Path.GetDirectoryName(Directory.GetFiles(Path.GetDirectoryName(CollectionQualitySettings.SettingsPath), "*.deck", SearchOption.AllDirectories).First());
    string corrupt = Path.Combine(archiveDirectory, Guid.NewGuid().ToString("N") + ".deck");
    File.WriteAllText(corrupt, "truncated archive"); int invalidFiles = 0;
    Check(store.ReadAll((_, _) => invalidFiles++).Count == 2 && invalidFiles == 1 && File.Exists(corrupt), "Corrupt files isolated and retained");
    Check(Directory.GetFiles(archiveDirectory, "*.tmp").Length == 0, "Atomic writes leave no temporary files");

    Reset(); var original = Fixture(); CollectionManager.Get().Decks[original.ID] = original;
    bool deleted = false;
    DeckArchive.Archive(original, () => { Check(DeckArchive.Store().ReadAll((_, _) => { }).Count == 1, "Archive verified before delete callback"); deleted = true; });
    Check(deleted, "Archive deletes active deck only after saving");
    original.Saving = true; deleted = false;
    DeckArchive.Archive(original, () => deleted = true); Check(!deleted, "Saving deck cannot be archived");
    original.Saving = false; original.Loaded = false;
    DeckArchive.Archive(original, () => deleted = true); Check(!deleted, "Unloaded deck cannot be archived");
    original.Loaded = true;
    string blocker = Path.Combine(root, "blocked"); File.WriteAllText(blocker, "not a directory");
    CollectionQualitySettings.SettingsPath = Path.Combine(blocker, "settings.json");
    DeckArchive.Archive(original, () => deleted = true); Check(!deleted, "Disk failure never deletes active deck");

    Reset(); saved = Save();
    for (int i = 0; i < 27; i++) CollectionManager.Get().Decks[i] = Fixture(i);
    DeckArchive.RestoreDeck(saved.Key); Check(Network.Get().Requests == 0 && !DeckArchive.Busy, "Full slot limit blocks creation");
    CollectionManager.Get().Decks.Clear(); Network.Online = false;
    DeckArchive.RestoreDeck(saved.Key); Check(Network.Get().Requests == 0, "Offline restore blocked"); Network.Online = true;
    CollectionManager.Get().m_pendingDeckCreate = new object();
    DeckArchive.RestoreDeck(saved.Key); Check(Network.Get().Requests == 0, "Concurrent native deck creation blocked");
    CollectionManager.Get().m_pendingDeckCreate = null;
    DeckArchive.RestoreDeck(saved.Key);
    Check(DeckArchive.Busy && Network.Get().Requests == 1 && Network.Get().CreatedFormat == FormatType.FT_WILD, "Restore uses archived format");
    DeckArchive.RestoreDeck(saved.Key); Check(Network.Get().Requests == 1, "Double click does not create duplicate");
    var target = Created(saved);
    Check(DeckArchive.Capture(target).Serialize() == saved.Serialize(), "Restore reconstructs all contents and cosmetics");
    Check(DeckArchive.Store().Read(saved.Key) != null, "Create alone keeps archive");
    Ack(target.ID + 1); Check(Network.Get().Readbacks == 0, "Unrelated server reply ignored");
    DeckArchive.Contents(target.ID); Check(DeckArchive.Busy, "Unsolicited contents event cannot confirm restore");
    Ack(target.ID); Check(Network.Get().Readbacks == 1, "Success requests server readback");
    Check(DeckArchive.Store().Read(saved.Key) != null, "Save acknowledgment alone keeps archive");
    Check(DeckArchiveUi.RestoredKeys.Count == 0 && DeckArchiveUi.Saved == 0,
        "Unconfirmed restoration leaves the archived tray entry and view unchanged");
    DeckArchive.Contents(target.ID);
    Check(!DeckArchive.Busy && DeckArchive.Store().ReadAll((_, _) => { }).Count == 0, "Matching readback removes archive");
    Check(DeckArchiveUi.RestoredKeys.SequenceEqual(new[] { saved.Key }) && DeckArchiveUi.Saved == 0,
        "Confirmed restoration removes only its local tray entry without switching to Saved");
    Check(CollectionManager.Get().GetDeck(target.ID) == target && !target.IsBeingDeleted(),
        "Removing the archived presentation keeps the restored server deck intact");

    Reset(); saved = Save(); DeckArchive.RestoreDeck(saved.Key); target = Created(saved); Ack(target.ID, false);
    Check(!DeckArchive.Busy && DeckArchive.Store().Read(saved.Key) != null, "Server rejection keeps archive");
    Check(DeckArchiveUi.RestoredKeys.Count == 0 && DeckArchiveUi.Saved == 0,
        "Failed restoration does not remove a tray entry or switch the view");
    int requests = Network.Get().Requests; DeckArchive.RestoreDeck(saved.Key);
    Check(Network.Get().Requests == requests, "Retry recognizes already created identical deck without duplicate creation");
    Check(DeckArchive.Busy && DeckArchive.Store().Read(saved.Key) != null, "Retry still requires server readback before deleting archive");

    Reset(); saved = Save(); DeckArchive.RestoreDeck(saved.Key);
    DeckArchive.Response(new Network.DBAction { Action = Network.DBAction.ActionType.CREATE_DECK, Result = Network.DBAction.ResultType.CONSTRAINT });
    DeckArchive.ActionFinished();
    Check(!DeckArchive.Busy && DeckArchive.Store().Read(saved.Key) != null, "Create rejection releases busy state and keeps archive");

    Reset(); saved = Save(); DeckArchive.RestoreDeck(saved.Key); target = Created(saved); Ack(target.ID);
    target.Slots[0].Counts[0]++;
    DeckArchive.Contents(target.ID); Check(!DeckArchive.Busy && DeckArchive.Store().Read(saved.Key) != null, "Changed server contents keep original archive");
    requests = Network.Get().Requests; DeckArchive.RestoreDeck(saved.Key);
    Check(Network.Get().Requests == requests, "Partial restore receipt prevents duplicate deck creation");

    Reset(); saved = Save(); DeckArchive.RestoreDeck(saved.Key); Time.realtimeSinceStartup = 31; DeckArchive.Update();
    Check(!DeckArchive.Busy && DeckArchive.Store().Read(saved.Key) != null, "Timeout keeps archive");
    Reset(); saved = Save(); DeckArchive.RestoreDeck(saved.Key); target = Created(saved); Ack(target.ID);
    var oldStore = DeckArchive.Store(); BattleNet.Current.Low = 3; DeckArchive.Contents(target.ID); DeckArchive.Update();
    Check(!DeckArchive.Busy && oldStore.Read(saved.Key) != null, "Account switch never removes previous account archive");
    Check(DeckArchive.Store().ReadAll((_, _) => { }).Count == 0, "Account archives isolated");

    Reset(); saved = Save(); DeckArchive.RestoreDeck(saved.Key); target = Created(saved); Ack(target.ID);
    var baseDeck = Fixture(target.ID); CollectionManager.Get().Bases[target.ID] = baseDeck;
    DeckArchive.PrepareReadback(new() { new PegasusUtil.DeckContents { DeckId = target.ID + 1 } });
    Check(target.Sideboards.Count == 1, "Unrelated readback leaves sideboards alone");
    DeckArchive.PrepareReadback(new() { new PegasusUtil.DeckContents { DeckId = target.ID } });
    Check(target.Sideboards.Count == 0 && baseDeck.Sideboards.Count == 0, "Restore readback clears sideboards before native refill");

    Reset(); saved = Save(); Network.Online = false;
    for (int i = 0; i < 27; i++) CollectionManager.Get().Decks[i] = Fixture(i);
    var local = DeckArchiveEditor.Prepare(saved.Key, long.MaxValue);
    Check(DeckArchive.Capture(local).Serialize() == saved.Serialize(), "Opening a local deck preserves all unowned premiums and sideboards");
    Check(Network.Get().Requests == 0 && CollectionManager.Get().GetDecks(DeckType.NORMAL_DECK).Count == 27, "Local editing works offline with all server slots full");
    CollectionDeck lookup = null;
    Check(!DeckArchiveEditor.GetDeck(local.ID, ref lookup) && ReferenceEquals(lookup, local), "Scoped lookup supplies full local editor model");
    lookup = null; Check(DeckArchiveEditor.GetDeck(7, ref lookup), "Real deck lookup remains native");
    Check(!DeckArchiveEditor.GetBaseline(local.ID, ref lookup) && !ReferenceEquals(lookup, local), "Local baseline is a separate model");
    Check(!DeckArchiveEditor.BlockNetwork(local.ID) && !DeckArchiveEditor.BlockDeckData(local.ID), "Local IDs cannot reach server rename, delete or save");
    Check(DeckArchiveEditor.BlockNetwork(7) && DeckArchiveEditor.BlockDeckData(7), "Native server deck writes remain available");
    long[] requested = { local.ID, 7 };
    Check(DeckArchiveEditor.BlockContents(ref requested) && requested.SequenceEqual(new long[] { 7 }), "Mixed content requests filter only local decks");
    requested = new[] { local.ID };
    Check(!DeckArchiveEditor.BlockContents(ref requested), "Pure local content request sends nothing");
    local.Name = "Edited locally";
    local.AddCard("CARD_B", TAG_PREMIUM.NORMAL, true);
    local.Sideboards["OWNER"].AddCard("CARD_A", TAG_PREMIUM.DIAMOND, true);
    local.FormatType = FormatType.FT_STANDARD;
    local.SetRuneOrder(RuneType.RT_FROST, RuneType.RT_FROST, RuneType.RT_UNHOLY);
    DeckArchive.Store().RememberRestore(saved.Key, 12);
    Check(!DeckArchiveEditor.Send(local), "Native SendChanges is replaced by local persistence");
    var edited = DeckArchive.Store().Read(saved.Key);
    Check(edited.Name == "Edited locally" && edited.Serialize() == DeckArchive.Capture(local).Serialize(), "Name, cards, sideboards, format and runes persist to the same archive");
    Check(DeckArchive.Store().RestoreTarget(saved.Key) == null, "Editing clears the receipt for an earlier different restoration");
    Check(!DeckArchiveEditor.SendRename(local), "Native rename remains local");
    Check(!DeckArchiveEditor.SaveDeck(local), "Native tray save remains local");
    DeckArchiveEditor.Shutdown();
    local = DeckArchiveEditor.Prepare(saved.Key, long.MaxValue);
    Check(DeckArchive.Capture(local).Serialize() == edited.Serialize(), "Edited local deck survives editor restart");
    Check(DeckArchive.Store().ReadAll((_, _) => { }).Count == 1, "Editing does not create extra archives");
    string editedPath = Directory.GetFiles(Path.GetDirectoryName(CollectionQualitySettings.SettingsPath), saved.Key + ".deck", SearchOption.AllDirectories).Single();
    Check(File.Exists(editedPath + ".bak"), "Atomic edit keeps a previous-version backup");
    local.Name = "Unsaved after disk failure";
    Directory.CreateDirectory(editedPath + ".update.tmp");
    int errorsBefore = DialogManager.Instance.Errors.Count;
    Check(!DeckArchiveEditor.TrySave() && DeckArchive.Store().Read(saved.Key).Name == "Edited locally", "Write failure preserves last verified archive");
    Check(DeckArchiveEditor.Owns(local), "Write failure retains editable model in memory");
    CollectionManager.Get().Edited = local;
    bool back = true;
    Check(!DeckArchiveEditor.BeforeBack(ref back) && !back, "Failed local save prevents leaving the editor");
    Check(!DeckArchiveEditor.BeforeSelection(null) && !DeckArchiveEditor.BeforeDone(), "Failed save cannot discard current local editor selection");
    Check(DialogManager.Instance.Errors.Count == errorsBefore + 1, "Repeated save failure does not flood popups");
    Directory.Delete(editedPath + ".update.tmp");
    Check(DeckArchiveEditor.TrySave() && DeckArchive.Store().Read(saved.Key).Name == "Unsaved after disk failure", "Saving recovers after disk error is resolved");
    local.Name = "Autosaved"; Time.realtimeSinceStartup += 2; DeckArchiveEditor.Update();
    Check(DeckArchive.Store().Read(saved.Key).Name == "Autosaved", "Periodic autosave preserves local edits");
    DeckArchive.Store().Delete(saved.Key); DeckArchiveEditor.Delete(saved.Key); DeckArchiveEditor.TrySave();
    Check(DeckArchive.Store().ReadAll((_, _) => { }).Count == 0, "Autosave never recreates a deliberately deleted archive");
    Check(!File.Exists(editedPath + ".bak"), "Permanent deletion removes the previous-version backup too");

    // Regression: a clipped live New Deck source can start folded/inactive.
    // The switch must be visibly open before any Unity animation tick occurs.
    var switchButton = new CollectionDeckTrayButton(); switchButton.gameObject.SetActive(false);
    DeckArchiveSwitchPose.Open(switchButton);
    Check(switchButton.gameObject.activeSelf && switchButton.gameObject.FaceVisible, "Native switch face is sampled open synchronously");
    Check(switchButton.Animation.Clip.SampledTime == switchButton.Animation.Clip.length && !switchButton.Animation.enabled,
        "Switch holds the actual native end pose without relying on playback");
    Check(switchButton.Enabled && switchButton.IsPoppedUp(), "Switch is usable immediately after cloning a closed source");
    switchButton.gameObject.SetActive(false); switchButton.gameObject.SetActive(true);
    Check(switchButton.gameObject.FaceVisible, "Clipping and reactivation preserve the switch face");

    var popup = new AlertPopup { Info = new AlertPopup.PopupInfo { m_id = DeckArchivePopup.Id, m_okText = "Archive instead" } };
    popup.m_buttonContainer.AddSlice(popup.m_confirmButton.gameObject);
    popup.m_buttonContainer.AddSlice(popup.m_cancelButton.gameObject);
    popup.m_buttonContainer.AddSlice(popup.m_okayButton.gameObject);
    var nativeSlices = popup.m_buttonContainer.m_slices;
    popup.m_okayButton.gameObject.SetActive(false);
    DeckArchivePopup.Buttons(popup);
    var extendedSlices = popup.m_buttonContainer.m_slices;
    Check(extendedSlices.Count == 3 && extendedSlices[0].m_slice == popup.m_confirmButton.gameObject
        && extendedSlices[2].m_slice == popup.m_cancelButton.gameObject, "Native popup retains Confirm and Cancel around the added action");
    Check(!popup.m_okayButton.gameObject.activeSelf && extendedSlices[1].m_slice != popup.m_okayButton.gameObject,
        "Archive extension never displays the differently framed native OK button");
    Check(extendedSlices[1].m_slice.Button.Skin == popup.m_confirmButton.Skin
        && extendedSlices[1].m_slice.Button.m_ButtonText.Text == "Archive instead", "Extra action uses the native wide button asset and requested caption");
    extendedSlices[1].m_slice.Button.Click(null);
    Check(popup.LastResponse == AlertPopup.Response.OK, "Archive action uses the native popup response and dismissal path");
    Check(extendedSlices[1].m_slice.activeSelf && popup.m_buttonContainer.m_localSliceSpacing.x > 0,
        "Three native faces are visible with explicit even spacing");
    DeckArchivePopup.Layout(popup);
    Check(popup.m_body.Width > 600, "Native popup body reserves three full-width buttons and margins");
    popup.Info = new AlertPopup.PopupInfo();
    DeckArchivePopup.Buttons(popup);
    Check(ReferenceEquals(popup.m_buttonContainer.m_slices, nativeSlices) && !extendedSlices[1].m_slice.activeSelf
        && popup.m_buttonContainer.m_localSliceSpacing.x == 0, "Reusing AlertPopup for another dialog restores its native slices and spacing");
    popup.Info = new AlertPopup.PopupInfo { m_id = DeckArchivePopup.Id, m_okText = "Restore instead" };
    DeckArchivePopup.Buttons(popup);
    Check(ReferenceEquals(popup.m_buttonContainer.m_slices, extendedSlices) && extendedSlices[1].m_slice.activeSelf,
        "Reopening an archived confirmation reuses a single native extension");
    Check(extendedSlices[1].m_slice.Button.m_ButtonText.Text == "Restore instead", "Archived confirmation updates the added action caption");
    DeckArchivePopup.Shutdown();
    Check(ReferenceEquals(popup.m_buttonContainer.m_slices, nativeSlices) && extendedSlices[1].m_slice.Destroyed,
        "Shutdown removes the added action and restores the native popup");
    Reset(); saved = Save(); original = Fixture(); CollectionManager.Get().Decks[original.ID] = original;
    CollectionQualitySettings.DeckArchiveEnabled = false; deleted = false;
    DeckArchive.Archive(original, () => deleted = true);
    Check(!deleted && DeckArchive.Store().ReadAll((_, _) => { }).Count == 1, "Disabled archive never deletes a saved deck or changes existing files");
    DeckArchive.RestoreDeck(saved.Key);
    Check(Network.Get().Requests == 0 && !DeckArchive.Busy, "Disabled archive cannot start a server restoration");
    Throws(() => DeckArchiveEditor.Prepare(saved.Key, long.MaxValue), "Disabled archive does not open a new local editor session");
    Check(DeckArchive.Store().Read(saved.Key) != null, "Disabling preserves the saved archive");
    CollectionQualitySettings.DeckArchiveEnabled = true;
    Check(DeckArchiveEditor.Prepare(saved.Key, long.MaxValue) != null, "Re-enabling opens the existing archive without migration");
    Console.WriteLine($"Deck archive: {checks} checks passed.");
}
finally { DeckArchive.Shutdown(); if (Directory.Exists(root)) Directory.Delete(root, true); }
