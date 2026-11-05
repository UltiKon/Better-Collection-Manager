using System;
using System.Collections.Generic;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private sealed class DerivedPreviewVisual : CollectionCardVisual
    {
        public int DerivedPreviewCalls;
        protected override void OnRightClick() { DerivedPreviewCalls++; base.OnRightClick(); }
    }

    private static void LeftClick(CollectionCardVisual visual)
    {
        // Native Harmony order: retain the pressed premium, then optionally
        // replace the native release with its existing right-click action.
        MixedPremiumCard.Choose(visual, false, true);
        if (!CollectionClickInspect.TryInspect(visual)) visual.NativeLeftRelease();
    }

    private static void CheckCollectionClickInspect()
    {
        MixedPremiumCard.ResetSession();
        var manager = CollectionManager.Instance = new CollectionManager();
        CollectionInputMgr.Instance = new CollectionInputMgr();
        UniversalInputManager.Instance = new UniversalInputManager();
        CraftingTray.Instance = null;
        var root = new GameObject("InspectTile");
        var visual = root.AddComponent<DerivedPreviewVisual>();
        var actor = Child(root.transform, "Actor").AddComponent<Actor>();
        actor.Def = Card("inspect", TAG_PREMIUM.NORMAL, 1).GetEntityDef();
        visual.Actors = new CollectionCardActors(actor);
        var modes = new[] { CollectionUtils.ViewMode.CARDS, CollectionUtils.ViewMode.HERO_SKINS,
            CollectionUtils.ViewMode.CARD_BACKS, CollectionUtils.ViewMode.COINS,
            CollectionUtils.ViewMode.BATTLEGROUNDS_GUIDE_SKINS, CollectionUtils.ViewMode.BATTLEGROUNDS_HERO_SKINS,
            CollectionUtils.ViewMode.PETS };
        foreach (var mode in modes)
        {
            visual.ViewMode = manager.Display.ViewMode = mode;
            int previews = visual.NativeInspectCalls, left = visual.NativeLeftCalls;
            LeftClick(visual);
            Check(visual.NativeInspectCalls == previews + 1 && visual.NativeLeftCalls == left
                && visual.InspectedActor == actor && visual.SawRoutingFlag,
                "A mouse click while browsing must invoke the native card/cosmetic preview exactly once");
            Check(!CollectionClickInspect.IsRoutingLeftClick, "The routing flag must clear after each preview");

            foreach (bool editing in new[] { false, true })
            foreach (bool deckSelected in new[] { false, true })
            {
                if (!editing && !deckSelected) continue;
                manager.Editing = editing;
                manager.EditedDeck = deckSelected ? new CollectionDeck() : null;
                previews = visual.NativeInspectCalls;
                int deckActions = visual.NativeDeckActions;
                LeftClick(visual);
                Check(visual.NativeInspectCalls == previews && visual.NativeDeckActions == deckActions + 1,
                    "Deck editing and deck selection must preserve native add/equip input in every cosmetic/card view");
            }
            manager.Editing = false; manager.EditedDeck = null;
        }
        Check(visual.DerivedPreviewCalls == modes.Length,
            "Native virtual preview overrides must remain active for collection visual subclasses");

        // Card backs use their native cosmetic preview without requiring the
        // card actor/entity definition used by the other collection tiles.
        visual.ViewMode = manager.Display.ViewMode = CollectionUtils.ViewMode.CARD_BACKS;
        foreach (Actor cardBackActor in new Actor[] { null, actor })
        {
            visual.Actors = new CollectionCardActors(cardBackActor);
            actor.Def = null;
            int previews = visual.NativeInspectCalls, left = visual.NativeLeftCalls;
            LeftClick(visual);
            Check(visual.NativeInspectCalls == previews + 1 && visual.NativeLeftCalls == left
                && visual.SawRoutingFlag && !CollectionClickInspect.IsRoutingLeftClick,
                "Card backs must open the native preview without a card actor or entity definition");

            visual.IgnoreInput = true;
            Check(!CollectionClickInspect.TryInspect(visual), "Card backs must still respect native input locks");
            visual.IgnoreInput = false;
            manager.Editing = true;
            int deckActions = visual.NativeDeckActions;
            LeftClick(visual);
            Check(visual.NativeInspectCalls == previews + 1 && visual.NativeDeckActions == deckActions + 1,
                "Card backs without card data must retain native equip actions while editing a deck");
            manager.Editing = false;

            visual.ViewMode = manager.Display.ViewMode = CollectionUtils.ViewMode.CARDS;
            Check(!CollectionClickInspect.TryInspect(visual), "Card previews must still require a valid card entity");
            visual.ViewMode = manager.Display.ViewMode = CollectionUtils.ViewMode.CARD_BACKS;
        }
        visual.Actors = new CollectionCardActors(actor);
        actor.Def = Card("inspect", TAG_PREMIUM.NORMAL, 1).GetEntityDef();

        foreach (var mode in new[] { CollectionUtils.ViewMode.DECK_TEMPLATE, CollectionUtils.ViewMode.MASS_DISENCHANT,
            CollectionUtils.ViewMode.HERO_PICKER, CollectionUtils.ViewMode.BATTLEGROUNDS_EMOTES,
            CollectionUtils.ViewMode.BATTLEGROUNDS_BOARD_SKINS, CollectionUtils.ViewMode.BATTLEGROUNDS_FINISHERS })
        {
            visual.ViewMode = manager.Display.ViewMode = mode;
            Check(!CollectionClickInspect.TryInspect(visual), "Selection tools and views without this native preview must keep native input");
        }
        visual.ViewMode = manager.Display.ViewMode = CollectionUtils.ViewMode.CARDS;
        UniversalInputManager.Instance.Touch = true;
        int nativeCalls = visual.NativeLeftCalls;
        LeftClick(visual);
        Check(visual.NativeLeftCalls == nativeCalls + 1, "Touch input must retain its existing native release behavior");
        UniversalInputManager.Instance.Touch = false;
        CollectionInputMgr.Instance.HoldingCard = true;
        Check(!CollectionClickInspect.TryInspect(visual), "Dropping a held card must not open a preview");
        CollectionInputMgr.Instance.HoldingCard = false;
        CollectionInputMgr.Instance.DraggingScrollbar = true;
        Check(!CollectionClickInspect.TryInspect(visual), "Scrolling must not trigger preview");
        CollectionInputMgr.Instance.DraggingScrollbar = false;
        manager.Display.Submode = CollectionUtils.ViewSubmode.CARD_ZILLIAX_MODULES;
        Check(!CollectionClickInspect.TryInspect(visual), "Module selection must remain a selection action");
        manager.Display.Submode = CollectionUtils.ViewSubmode.DEFAULT;
        manager.Display.ViewMode = CollectionUtils.ViewMode.HERO_SKINS;
        Check(!CollectionClickInspect.TryInspect(visual), "Stale tiles from another view must not open previews");
        manager.Display.ViewMode = CollectionUtils.ViewMode.CARDS;
        visual.IgnoreInput = true;
        Check(!CollectionClickInspect.TryInspect(visual), "Native page-turn/preview input locks must remain respected");
        visual.IgnoreInput = false;
        visual.Shown = false;
        Check(!CollectionClickInspect.TryInspect(visual), "A hidden tile must not open a preview");
        visual.Shown = true;
        visual.m_disableCraftingMode = true;
        Check(!CollectionClickInspect.TryInspect(visual), "A tile with previews explicitly disabled must retain native behavior");
        visual.m_disableCraftingMode = false;
        CraftingTray.Instance = new CraftingTray { Shown = true };
        Check(!CollectionClickInspect.TryInspect(visual), "The existing crafting-tray click path must remain native");
        CraftingTray.Instance = null;
        visual.PendingTransaction = true;
        int before = visual.NativeInspectCalls;
        Check(CollectionClickInspect.TryInspect(visual) && visual.NativeInspectCalls == before,
            "Pending transactions must still be blocked by the native right-click guard");
        visual.PendingTransaction = false;
        visual.ThrowOnInspect = true;
        Check(!CollectionClickInspect.TryInspect(visual) && !CollectionClickInspect.IsRoutingLeftClick,
            "Preview failures must release routing state and allow the native release path");
        visual.ThrowOnInspect = false;

        // Crossing the seam between press/release must still inspect the
        // selected real premium, even though the native right-click hook runs.
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE };
        var normal = Card("inspect", TAG_PREMIUM.NORMAL, 1);
        var golden = Card("inspect", TAG_PREMIUM.GOLDEN, 1);
        manager.Counts[("inspect", TAG_PREMIUM.NORMAL)] = manager.Counts[("inspect", TAG_PREMIUM.GOLDEN)] = 1;
        PreferredQualityCollapse.CollapseInPlace(new List<CollectibleCard> { normal, golden }, "inspect", null);
        actor.Premium = TAG_PREMIUM.GOLDEN;
        actor.gameObject.AddComponent<MeshFilter>().sharedMesh = CardMesh();
        actor.gameObject.AddComponent<MeshRenderer>();
        AssetLoader.Get().MeshFactory = CardMesh;
        MixedPremiumCard.Attach(visual);
        foreach (TAG_PREMIUM premium in new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL })
        {
            Input.mousePosition = new Vector3(0, premium == TAG_PREMIUM.GOLDEN ? 0.8f : -0.8f, 0);
            MixedPremiumCard.Choose(visual, true, false);
            Input.mousePosition = new Vector3(0, premium == TAG_PREMIUM.GOLDEN ? -0.8f : 0.8f, 0);
            LeftClick(visual);
            Check(visual.InspectedActor.GetPremium() == premium && !CollectionClickInspect.IsRoutingLeftClick,
                "Left-click preview must keep the premium pressed on a split tile when the pointer crosses the cut");
        }
        MixedPremiumCard.ResetSession();
        CollectionManager.Instance = null;
        CollectionInputMgr.Instance = new CollectionInputMgr();
        UniversalInputManager.Instance = new UniversalInputManager();
    }
}
