using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboDeckInput(CollectibleCard proxy, CollectibleCard a, CollectibleCard b, CategoryCatalog.Combo combo)
    {
        CollectionManager manager = CollectionManager.Get();
        CollectionDeck previousDeck = manager.EditedDeck;
        CollectionDeckTray previousTray = CollectionDeckTray.Instance;
        CollectionInputMgr previousInput = CollectionInputMgr.Instance;
        var previousOrder = PreferredPremiumPrefs.Order;
        var previousCounts = manager.Counts.ToArray();
        float previousTime = Time.unscaledTime;
        ClickQualityMode previousClick = CollectionQualitySettings.Click;
        bool previousLeft = Input.LeftReleased, previousRight = Input.RightReleased;
        var actor = AssetLoader.Get().InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
        actor.SetEntityDef(a.GetEntityDef());
        actor.transform.localPosition = new Vector3(12, -3, 7); actor.transform.localScale = new Vector3(.7f, .8f, .9f);
        var trays = new System.Collections.Generic.List<CollectionDeckTray>();
        var visual = new GameObject("ComboDeckTile").AddComponent<CollectionCardVisual>();
        visual.Actors = new CollectionCardActors(actor);
        ComboCollectionCards.CaptureBinding(visual, new object[] { proxy });
        GameObject Part(string name, Transform parent)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
        var counter = visual.m_count = Part("Combo counter", visual.transform).AddComponent<CollectionCardCount>();
        counter.m_normalTab = Part("Owned tab", counter.transform);
        counter.m_normalCountText = Part("Counter text", counter.m_normalTab.transform).AddComponent<UberText>();
        counter.m_normalBorder = Part("Narrow border", counter.transform); counter.m_normalWideBorder = Part("Wide border", counter.transform);
        counter.m_trialTab = Part("Trial tab", counter.transform); counter.m_trialWideBorder = Part("Trial border", counter.transform);
        counter.m_trialAndOwnedBorder = Part("Combined border", counter.transform); counter.SetNative(7, 1);
        MethodInfo release = typeof(CollectionCardVisual).GetMethod("OnRelease", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo right = typeof(CollectionCardVisual).GetMethod("OnRightClick", BindingFlags.Instance | BindingFlags.NonPublic);
        var golden = Card(a.CardId, TAG_PREMIUM.GOLDEN, 1);
        golden.ManaCost = a.ManaCost;
        manager.Cards[(a.CardId, TAG_PREMIUM.GOLDEN)] = golden;
        manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = 1;
        manager.Counts[(a.CardId, TAG_PREMIUM.NORMAL)] = 2;
        manager.Counts[(b.CardId, TAG_PREMIUM.NORMAL)] = 2;
        try
        {
            CollectionInputMgr.Instance = new CollectionInputMgr();
            Input.LeftReleased = Input.RightReleased = false;
            PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
            CollectionQualitySettings.Click = ClickQualityMode.PreferredOrder;
            CollectionDeck NewDeck()
            {
                manager.Editing = true; manager.Rules.Banned = manager.Rules.NotPlayable = false; manager.Rules.Capacity = 30;
                var deck = manager.EditedDeck = new CollectionDeck();
                CollectionDeckTray.Instance = new CollectionDeckTray { Context = deck };
                trays.Add(CollectionDeckTray.Instance);
                ComboCollectionCards.Apply(visual);
                return deck;
            }
            CollectionDeck deck = NewDeck();
            ComboCollectionCards.Apply(visual);
            Check(counter.gameObject.activeInHierarchy && counter.m_normalCountText.Text == "x1"
                && counter.GetCount() == 7 && counter.GetTrialCount() == 1 && !counter.m_trialTab.activeSelf,
                "The native combo counter shows complete owned bundles across qualities without changing inventory/trial data");
            int lockSounds = visual.LockSounds;
            bool native = ComboCollectionCards.HandleInput(visual, release);
            Check(!native && deck.LocalCount(a.CardId) == 2 && deck.LocalCount(b.CardId) == 1
                && deck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.GOLDEN) == 1
                && deck.GetCardCountAllMatchingSlots(a.CardId, TAG_PREMIUM.NORMAL) == 1 && CollectionDeckTray.Get().Added.Count == 3
                && !UniversalInputManager.Get().DialogActive,
                "Left-click on a combo adds every captured copy through native deck insertion, using preferred qualities and available fallback");
            Check(CollectionDeckTray.Get().AnimationSources.Count == 3
                && CollectionDeckTray.Get().AnimationSources.All(source => source != actor && source.Destroyed && !source.gameObject.activeSelf)
                && CollectionDeckTray.Get().Animations.Count == 3
                && CollectionDeckTray.Get().AnimationDeckCounts.SequenceEqual(new[] { 3, 3, 3 })
                && CollectionDeckTray.Get().Animations.All(flight => flight != actor && !flight.Destroyed && flight.gameObject.activeInHierarchy
                    && flight.transform.position.Equals(actor.transform.position)
                    && flight.transform.lossyScale.Equals(actor.transform.lossyScale)
                    && (flight.GetEntityDef().GetCardId() == a.CardId || flight.GetEntityDef().GetCardId() == b.CardId))
                && CollectionDeckTray.Get().Animations.Select(flight => flight.GetPremium()).SequenceEqual(CollectionDeckTray.Get().Added)
                && !actor.Destroyed && visual.GetActor() == actor && actor.GetNameText().Text == combo.Name,
                "Flights start only after all deck rows are stable; temporary sources are cleaned up while native flights and the combo tile remain intact");
            Check(counter.m_normalCountText.Text == "x1" && visual.LockSounds == lockSounds,
                "Adding deck cards does not subtract from total-owned combo counts or play a premature limit sound");
            CollectionDeckTray.Get().AfterAdd = () => {
                Time.unscaledTime += .2f;
                var type = CollectionCardVisual.LockType.NONE; string reason = null;
                ComboCollectionCards.TryAdjustComboLock(visual, ref type, ref reason);
                visual.ShowLock(type, reason, true, deck);
            };
            ComboCollectionCards.HandleInput(visual, release);
            Check(CollectionDeckTray.Get().Added.Count == 4 && deck.LocalCount(a.CardId) == 2 && deck.LocalCount(b.CardId) == 2,
                "A repeated combo click adds another bundle where native copy limits still permit it");
            Check(CollectionDeckTray.Get().AnimationSources.All(source => source.Destroyed && !source.gameObject.activeSelf)
                && !actor.Destroyed && actor.gameObject.activeInHierarchy && actor.GetNameText().Text == combo.Name,
                "Repeated clicks leave no original component actors stacked on the combo tile");
            Check(visual.LockSounds == lockSounds + 1 && counter.m_normalCountText.Text == "x1",
                "Completing the last addable bundle plays the native ShowLock sound once and retains the owned counter");
            for (int i = 0; i < 20; i++) ComboCollectionCards.Apply(visual);
            ComboCollectionCards.HandleInput(visual, release);
            Check(visual.LockSounds == lockSounds + 1, "Stable refreshes and clicks on an already locked combo do not replay the limit sound");
            deck = NewDeck(); deck.Add(a.CardId, TAG_PREMIUM.GOLDEN);
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.LocalCount(a.CardId) == 2 && deck.LocalCount(b.CardId) == 1 && CollectionDeckTray.Get().Added.Count == 2,
                "A combo adds captured quantities up to native copy limits while counting all premium variants");
            deck = NewDeck(); manager.Rules.Capacity = 1;
            lockSounds = visual.LockSounds;
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.Slots.Values.Sum() == 1, "Combo bulk insertion respects native deck capacity for every individual card");
            Check(visual.LockSounds == lockSounds + 1 && visual.LastLockReason == "DECK_SIZE",
                "A partial combo that reaches the deck-size limit triggers the recorded native lock feedback once");
            deck = NewDeck(); manager.Rules.Banned = true;
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.Slots.Count == 0, "Combo bulk insertion cannot bypass native format or class restrictions");
            deck = NewDeck(); manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = 0; manager.Counts[(a.CardId, TAG_PREMIUM.NORMAL)] = 1;
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.LocalCount(a.CardId) == 1 && deck.LocalCount(b.CardId) == 1,
                "A combo never creates unowned copies when its captured quantities exceed current ownership");
            Check(counter.gameObject.activeInHierarchy && counter.m_normalCountText.Text == "x0",
                "A missing required copy shows zero complete owned combos even while partial additions remain possible");
            manager.Counts[(a.CardId, TAG_PREMIUM.NORMAL)] = 2; manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = 3;
            manager.Counts[(b.CardId, TAG_PREMIUM.NORMAL)] = 3;
            bool hadTrial = manager.Trials.TryGetValue((a.CardId, TAG_PREMIUM.NORMAL), out int previousTrial);
            manager.Trials[(a.CardId, TAG_PREMIUM.NORMAL)] = 100;
            Time.unscaledTime += .2f; ComboCollectionCards.Apply(visual);
            Check(ComboCollectionCards.CountOwnedCombos(combo) == 2 && counter.m_normalCountText.Text == "x2",
                "Five owned A and three B yield two complete 2A+1B bundles; deck usage and trial copies do not change permanent ownership");
            manager.Editing = false; Time.unscaledTime += .2f; ComboCollectionCards.Apply(visual);
            Check(counter.m_normalCountText.Text == "x2", "Owned combo counts remain correct when leaving deck editing");
            ComboCollectionCards.Forget(visual); ComboCollectionCards.CaptureBinding(visual, new object[] { proxy });
            ComboCollectionCards.Apply(visual);
            Check(counter.m_normalCountText.Text == "x2", "A newly bound combo outside deck editing calculates its owned count immediately");
            manager.Editing = true;
            if (hadTrial) manager.Trials[(a.CardId, TAG_PREMIUM.NORMAL)] = previousTrial;
            else manager.Trials.Remove((a.CardId, TAG_PREMIUM.NORMAL));
            manager.Counts[(a.CardId, TAG_PREMIUM.NORMAL)] = manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = int.MaxValue;
            manager.Counts[(b.CardId, TAG_PREMIUM.NORMAL)] = int.MaxValue;
            Time.unscaledTime += .2f; ComboCollectionCards.Apply(visual);
            Check(ComboCollectionCards.CountOwnedCombos(combo) == int.MaxValue && counter.m_normalCountText.Text == "9+"
                && counter.m_normalWideBorder.activeSelf && !counter.m_normalBorder.activeSelf,
                "Large inventories do not overflow; the native wide counter uses its normal 9+ display");
            manager.Counts[(a.CardId, TAG_PREMIUM.NORMAL)] = manager.Counts[(b.CardId, TAG_PREMIUM.NORMAL)] = 2;
            manager.Counts[(a.CardId, TAG_PREMIUM.GOLDEN)] = 1;
            deck = NewDeck();
            ComboCollectionCards.HandleInput(visual, right);
            Check(deck.Slots.Count == 0 && UniversalInputManager.Get().DialogActive && ButtonListMenu.Visible is ComboPopup,
                "Right-click in deck editing opens component inspect without adding any cards");
            var popup = (ComboPopup)ButtonListMenu.Visible;
            Actor[] previews = popup.GetComponentsInChildren<Actor>(false);
            float[] starts = previews.Select(model => Math.Abs(model.transform.localScale.x)).ToArray();
            var lateUpdate = typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Time.unscaledTime += ComboPopup.InspectZoomDuration * .5f; lateUpdate.Invoke(popup, null);
            float[] middles = previews.Select(model => Math.Abs(model.transform.localScale.x)).ToArray();
            Time.unscaledTime += ComboPopup.InspectZoomDuration * .5f + .01f; lateUpdate.Invoke(popup, null);
            Check(previews.Length == combo.Cards.Count && previews.Select((model, i) => starts[i] < middles[i]
                    && middles[i] < Math.Abs(model.transform.localScale.x)).All(growing => growing)
                && visual.GetActor() == actor && !actor.Destroyed,
                "Inspect grows smoothly from the clicked combo through an intermediate size to the final preview without changing the collection actor");
            ComboPopup.Close();
            visual.m_lastClickLeft = false;
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.Slots.Count == 0 && UniversalInputManager.Get().DialogActive,
                "The native right-button release flag preserves inspect instead of bulk adding");
            ComboPopup.Close(); visual.m_lastClickLeft = true;
            deck = NewDeck(); visual.m_lastClickLeft = false; Input.LeftReleased = true;
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.Slots.Values.Sum() > 0 && !UniversalInputManager.Get().DialogActive,
                "The current left-button release adds the combo even if native last-click state still describes an earlier right-click");
            deck = NewDeck(); visual.m_lastClickLeft = true; Input.LeftReleased = false; Input.RightReleased = true;
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.Slots.Count == 0 && UniversalInputManager.Get().DialogActive,
                "The current right-button release inspects even when the previous click was left");
            ComboPopup.Close(); Input.RightReleased = false;
            visual.IgnoreInput = true;
            ComboCollectionCards.HandleInput(visual, release);
            Check(deck.Slots.Count == 0 && !UniversalInputManager.Get().DialogActive, "Native input guards also block combo deck insertion");
            visual.IgnoreInput = false;
            manager.Editing = false; manager.EditedDeck = null;
            ComboCollectionCards.HandleInput(visual, release);
            Check(UniversalInputManager.Get().DialogActive && deck.Slots.Count == 0,
                "Left-click while browsing still opens combo inspect without an edited deck");
            ComboPopup.Close();
            ComboCollectionCards.Forget(visual);
            Check(counter.m_normalCountText.Text == "x7" && counter.GetCount() == 7 && counter.GetTrialCount() == 1 && counter.m_trialTab.activeSelf,
                "Reusing the tile restores the original native count text and trial visibility");
        }
        finally
        {
            foreach (CollectionDeckTray tray in trays)
                foreach (Actor flight in tray.Animations) flight.Destroy();
            ComboPopup.Close(); ComboCollectionCards.Forget(visual);
            UnityEngine.Object.Destroy(visual.gameObject); actor.Destroy();
            manager.EditedDeck = previousDeck; manager.Editing = true;
            manager.Rules.Banned = manager.Rules.NotPlayable = false; manager.Rules.Capacity = 30;
            manager.Cards.Remove((a.CardId, TAG_PREMIUM.GOLDEN));
            manager.Counts.Clear(); foreach (var pair in previousCounts) manager.Counts.Add(pair.Key, pair.Value);
            Time.unscaledTime = previousTime;
            PreferredPremiumPrefs.Order = previousOrder; CollectionQualitySettings.Click = previousClick;
            Input.LeftReleased = previousLeft; Input.RightReleased = previousRight;
            CollectionDeckTray.Instance = previousTray; CollectionInputMgr.Instance = previousInput;
        }
    }
}
