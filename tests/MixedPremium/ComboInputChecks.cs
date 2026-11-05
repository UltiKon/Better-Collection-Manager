using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboLocksAndDrag(CollectibleRenderFixture renderer, CollectibleCard proxy, CollectibleCard carrier, CategoryCatalog.Combo combo)
    {
        CollectionManager manager = CollectionManager.Get();
        var oldDeck = manager.EditedDeck; var oldTray = CollectionDeckTray.Instance; var oldInput = CollectionInputMgr.Instance;
        var counts = manager.Counts.ToArray(); bool editing = manager.Editing;
        int capacity = manager.Rules.Capacity, max = manager.Rules.MaxCopies, frame = Time.frameCount;
        bool banned = manager.Rules.Banned, notPlayable = manager.Rules.NotPlayable, released = Input.LeftReleased;
        float time = Time.unscaledTime; var click = CollectionQualitySettings.Click;
        var source = AssetLoader.Get().InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
        source.SetEntityDef(carrier.GetEntityDef());
        var cached = new CollectionCardActors(source);
        var visual = new GameObject("ComboInput").AddComponent<CollectionCardVisual>();
        var input = CollectionInputMgr.Instance = new CollectionInputMgr();
        var first = ComboCollectionCards.ComponentCards(combo, combo.Cards.Single(card => card.Count == 2).Id)[0];
        var second = ComboCollectionCards.ComponentCards(combo, combo.Cards.Single(card => card.Count == 1).Id)[0];
        var onDrag = typeof(CollectionCardVisual).GetMethod("OnDrag", BindingFlags.Instance | BindingFlags.NonPublic);
        var onRelease = typeof(CollectionCardVisual).GetMethod("OnRelease", BindingFlags.Instance | BindingFlags.NonPublic);
        var drop = typeof(CollectionInputMgr).GetMethod("DropCard", BindingFlags.Instance | BindingFlags.NonPublic);
        void Owned(int a, int b)
        {
            foreach (var pair in manager.Cards) manager.Counts[pair.Key] = 0;
            manager.Counts[(first.CardId, TAG_PREMIUM.NORMAL)] = a; manager.Counts[(second.CardId, TAG_PREMIUM.NORMAL)] = b;
        }
        CollectionDeck NewDeck()
        {
            manager.Editing = true; manager.Rules.Capacity = 30; manager.Rules.MaxCopies = 2;
            manager.Rules.Banned = manager.Rules.NotPlayable = false; Owned(2, 2);
            var deck = manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Instance = new CollectionDeckTray { Context = deck };
            Time.frameCount += 3; Time.unscaledTime += .2f; Input.LeftReleased = false;
            renderer.Render(proxy, visual, cached); return deck;
        }
        void Refresh() { Time.unscaledTime += .2f; ComboCollectionCards.Apply(visual); }
        void Begin()
        {
            Input.LeftReleased = false; input.OverDeck = false; onDrag.Invoke(visual, null);
            Check(input.HasHeldCard() && !ComboCollectionCards.PreventGrab(new object[] { visual }),
                "Native combo drag creates a held visual and closes its one-gesture pickup permission afterwards");
        }
        void FinishFrame() { Time.frameCount++; ComboCollectionCards.OnUpdate(); }
        try
        {
            CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
            var deck = NewDeck();
            Check(visual.LastLock == CollectionCardVisual.LockType.NONE, "An addable combo has no lock banner");
            deck.Add(first.CardId, TAG_PREMIUM.NORMAL); deck.Add(first.CardId, TAG_PREMIUM.NORMAL);
            Refresh();
            Check(visual.LastLock == CollectionCardVisual.LockType.NONE,
                "An exhausted component does not block a combo while another component can still be added");
            deck.Add(second.CardId, TAG_PREMIUM.NORMAL); deck.Add(second.CardId, TAG_PREMIUM.NORMAL); Refresh();
            Check(visual.LastLock == CollectionCardVisual.LockType.MAX_COPIES_IN_DECK && visual.m_cardLock.gameObject.activeSelf,
                "A combo shows the native maximum-copies banner when every component is at its deck copy limit");
            deck = NewDeck(); manager.Rules.Capacity = 0; Refresh();
            Check(visual.LastLock == CollectionCardVisual.LockType.MAX_COPIES_IN_DECK && visual.LastLockReason == "DECK_SIZE",
                "A full deck shows the native banner with the native deck-size failure reason");
            deck = NewDeck(); Owned(0, 0); Refresh();
            Check(visual.LastLock == CollectionCardVisual.LockType.NO_MORE_INSTANCES && visual.m_cardLock.gameObject.activeSelf,
                "No remaining owned copies shows the native insufficient-copies banner");
            Owned(2, 2); Refresh();
            Check(visual.LastLock == CollectionCardVisual.LockType.NONE && !visual.m_cardLock.gameObject.activeSelf,
                "Removing a limit or restoring inventory clears the combo banner again");
            var lockArgs = new object[] { visual, CollectionCardVisual.LockType.MAX_COPIES_IN_DECK, "Carrier is exhausted" };
            InvokeQualityHook("LockPatch", "Prefix", lockArgs);
            Check((CollectionCardVisual.LockType)lockArgs[1] == CollectionCardVisual.LockType.NONE && lockArgs[2] == null,
                "A native carrier-card lock refresh is replaced by aggregate combo availability");

            Begin(); input.OverDeck = true; Input.LeftReleased = true;
            bool nativeRelease = (bool)InvokeQualityHook("HeldReleasePatch", "Prefix", input);
            Check(!nativeRelease && !input.HasHeldCard() && input.LastDropCanceled && CollectionDeckTray.Get().Added.Count == 0,
                "A combo release first cancels the native held carrier and defers inserting the bundle until cleanup completes");
            ComboCollectionCards.HandleInput(visual, onRelease);
            FinishFrame();
            Check(deck.LocalCount(first.CardId) == 2 && deck.LocalCount(second.CardId) == 1 && CollectionDeckTray.Get().Added.Count == 3,
                "Dropping a combo over the deck inserts all captured quantities exactly once, including after the source tile's release");
            ComboCollectionCards.OnUpdate();
            Check(CollectionDeckTray.Get().Added.Count == 3, "Repeated updates cannot insert the completed combo drop again");

            deck = NewDeck(); input.PublishGrabBeforeHeld = true; Begin(); visual.Hide();
            input.OverDeck = true; Input.LeftReleased = true; InvokeQualityHook("HeldReleasePatch", "Prefix", input); FinishFrame();
            Check(CollectionDeckTray.Get().Added.Count == 3 && !input.HasHeldCard(),
                "A pickup event before the held visual is published and a temporarily hidden source tile still produce one complete combo drop");
            input.PublishGrabBeforeHeld = false; visual.Show();

            deck = NewDeck(); input.DeferHeldPublication = true; onDrag.Invoke(visual, null);
            FinishFrame(); // No held visual yet: native actor loading is still pending.
            input.DeferHeldPublication = false; input.NativeGrab(visual);
            input.OverDeck = true; Input.LeftReleased = true; InvokeQualityHook("HeldReleasePatch", "Prefix", input); FinishFrame();
            Check(CollectionDeckTray.Get().Added.Count == 3 && !input.HasHeldCard(),
                "A delayed held visual does not discard the captured bundle before native pickup finishes");

            deck = NewDeck(); Begin(); Input.LeftReleased = true;
            InvokeQualityHook("HeldReleasePatch", "Prefix", input); FinishFrame();
            Check(deck.Slots.Count == 0 && !input.HasHeldCard(), "Dropping outside the deck cancels the native visual without inserting cards");

            deck = NewDeck(); Begin(); input.OverDeck = true;
            var dropArgs = new object[] { input, false, null };
            InvokeQualityHook("DropPatch", "Prefix", dropArgs);
            Check((bool)dropArgs[1], "A native direct drop is converted to cancellation so it cannot insert the combo carrier");
            drop.Invoke(input, new[] { dropArgs[1] }); InvokeQualityHook("DropPatch", "Finalizer", null, dropArgs[2]); FinishFrame();
            Check(CollectionDeckTray.Get().Added.Count == 3 && deck.LocalCount(first.CardId) == 2 && deck.LocalCount(second.CardId) == 1,
                "The native direct-drop path also commits the entire combo once after held-card cleanup");

            deck = NewDeck(); Begin(); input.OverDeck = true;
            dropArgs = new object[] { input, true, null }; InvokeQualityHook("DropPatch", "Prefix", dropArgs);
            drop.Invoke(input, new[] { dropArgs[1] }); InvokeQualityHook("DropPatch", "Finalizer", null, dropArgs[2]); FinishFrame();
            Check(deck.Slots.Count == 0, "Canceling a combo drag over the deck never commits the bundle");

            deck = NewDeck(); Begin();
            var replacement = manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Get().Context = replacement;
            ComboCollectionCards.OnUpdate(); Input.LeftReleased = true; FinishFrame();
            Check(!input.HasHeldCard() && deck.Slots.Count == 0 && replacement.Slots.Count == 0,
                "Changing decks during a combo drag cancels its native carrier without inserting into either deck");

            deck = NewDeck(); Begin(); input.OverDeck = true; Input.LeftReleased = true;
            InvokeQualityHook("HeldReleasePatch", "Prefix", input);
            replacement = manager.EditedDeck = new CollectionDeck(); CollectionDeckTray.Get().Context = replacement; FinishFrame();
            Check(deck.Slots.Count == 0 && replacement.Slots.Count == 0,
                "A deck switch between release cleanup and deferred commit discards the queued combo bundle");

            deck = NewDeck(); Begin(); input.OverDeck = true; manager.Rules.Capacity = 1;
            Input.LeftReleased = true; InvokeQualityHook("HeldReleasePatch", "Prefix", input); FinishFrame(); Refresh();
            Check(CollectionDeckTray.Get().Added.Count == 1 && visual.LastLockReason == "DECK_SIZE",
                "Drag uses the same per-card validation as click and refreshes its full-deck banner after a partial bundle");
        }
        finally
        {
            ComboCollectionCards.PrepareComboDrop(true, false);
            if (input.HasHeldCard()) drop.Invoke(input, new object[] { true });
            ComboCollectionCards.ReleaseHiddenActors(visual); ComboCollectionCards.Forget(visual);
            UnityEngine.Object.Destroy(visual.gameObject); source.Destroy();
            manager.EditedDeck = oldDeck; manager.Editing = editing; manager.Rules.Capacity = capacity;
            manager.Rules.MaxCopies = max; manager.Rules.Banned = banned; manager.Rules.NotPlayable = notPlayable;
            manager.Counts.Clear(); foreach (var pair in counts) manager.Counts.Add(pair.Key, pair.Value);
            CollectionDeckTray.Instance = oldTray; CollectionInputMgr.Instance = oldInput;
            Time.frameCount = frame; Time.unscaledTime = time; Input.LeftReleased = released; CollectionQualitySettings.Click = click;
        }
    }
}
