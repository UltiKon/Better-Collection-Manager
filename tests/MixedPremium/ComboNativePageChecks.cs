using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckNativePageComboSlots(CollectibleRenderFixture renderer, CollectibleCard proxy,
        CollectibleCard carrier, CategoryCatalog.Combo combo)
    {
        TestDisplay display = CollectionManager.Get().Display;
        object previousCollection = display.m_cardsCollection; int previousPage = display.CurrentPageNum;
        var sun = Card("BE_153", TAG_PREMIUM.GOLDEN, 2); sun.ManaCost = 1;
        Actor Model(CollectibleCard card)
        {
            Actor actor = AssetLoader.Get().InstantiatePrefab(card.PremiumType.ToString(), Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
            actor.SetEntityDef(card.GetEntityDef()); actor.NameText.Text = card == sun ? "Sun's Reckoning" : "Equality";
            actor.RulesText.Text = "Native rules"; actor.CostText.Text = card.ManaCost.ToString();
            return actor;
        }
        Actor sunActor = Model(sun), carrierActor = Model(carrier);
        carrierActor.SetPremium(TAG_PREMIUM.GOLDEN);
        var sunModels = new CollectionCardActors(sunActor); var carrierModels = new CollectionCardActors(carrierActor);
        var page = new CollectiblePageDisplay();
        for (int i = 0; i < 4; i++) page.Tiles.Add(new GameObject("NativePageSlot" + i).AddComponent<CollectionCardVisual>());
        var collection = new NativeComboPageCollection();
        collection.Pages.Add(new List<CollectibleCard> { carrier });
        collection.Pages.Add(new List<CollectibleCard> { sun, carrier, proxy });
        display.m_cardsCollection = collection; display.CurrentPageNum = 2;
        try
        {
            var models = new List<CollectionCardActors> { sunModels, carrierModels, carrierModels, null };
            // Exercise a card page without the optional ICollectible list.
            // There is a native actor for Sun, an ordinary carrier, a proxy,
            // and an unused trailing slot. No getter order identifies the proxy.
            renderer.Parallel(proxy, page, models, null);
            Check(page.Tiles[0].Actors == sunModels && sunActor.NameText.Text == "Sun's Reckoning"
                && sunActor.RulesText.Text == "Native rules" && sunActor.CostText.Text == "1"
                && !ComboCollectionCards.IsCombo(page.Tiles[0]),
                "The real page contents preserve Sun's Reckoning when the ICollectible argument is null and the last card read was a combo");
            Check(page.Tiles[1].Actors == carrierModels && !ComboCollectionCards.IsCombo(page.Tiles[1])
                && ComboCollectionCards.IsCombo(page.Tiles[2]) && page.Tiles[2].GetActor() != carrierActor
                && page.Tiles[2].GetActor().NameText.Text == combo.Name,
                "The appended combo keeps its own native page slot rather than renaming Sun or the ordinary carrier");
            Check(combo.Cards.Any(card => card.Id == CategoryCatalog.NormalizeCardId(page.Tiles[2].GetActor().GetEntityDef().GetCardId()))
                && page.Tiles[2].GetActor().GetEntityDef().GetCardId() != sun.CardId
                && page.Tiles[2].GetActor().GetPremium() == TAG_PREMIUM.NORMAL && carrierActor.GetPremium() == TAG_PREMIUM.GOLDEN,
                "The bound model belongs to an available combo component and does not inherit another native actor's artwork or premium");
            Actor first = page.Tiles[2].GetActor();
            string firstArtwork = first.GetEntityDef().GetCardId();
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval;
            ComboCollectionCards.OnUpdate();
            Check(!first.Destroyed && !first.gameObject.activeSelf && page.Tiles[2].GetActor().GetEntityDef().GetCardId() != firstArtwork
                && sunActor.NameText.Text == "Sun's Reckoning" && !sunActor.Destroyed,
                "Native page lookup supports component artwork rotation while leaving Sun's model intact");
            display.CurrentPageNum = 1; // A page transition is assembling destination page 2.
            renderer.Parallel(proxy, page, models, new List<ICollectible>());
            Check(!ComboCollectionCards.IsCombo(page.Tiles[0]) && ComboCollectionCards.IsCombo(page.Tiles[2])
                && sunActor.NameText.Text == "Sun's Reckoning",
                "A transition resolves the page by its full model/card sequence when the current page number still refers to the previous page");
            display.m_cardsCollection = null;
            renderer.Parallel(proxy, page, models, null);
            Check(page.Tiles.Take(3).All(tile => !ComboCollectionCards.IsCombo(tile))
                && sunActor.NameText.Text == "Sun's Reckoning",
                "An unresolved card page never falls back to decorating the last-read ordinary tile as a combo");
            var rejected = new GameObject("RejectedReplacement").AddComponent<CollectionCardVisual>();
            try
            {
                rejected.Actors = sunModels; rejected.IgnoreNextSetActorsReplacement = true;
                renderer.Render(proxy, rejected, sunModels);
                Check(rejected.Actors == sunModels && !ComboCollectionCards.IsCombo(rejected)
                    && sunActor.NameText.Text == "Sun's Reckoning" && sunActor.CostText.Text == "1" && sunActor.m_rarityGemMesh.activeSelf,
                    "A rejected private model cannot rename or hide rarity on a shared native actor, including from a postfix before the finalizer");
            }
            finally { ComboCollectionCards.ReleaseHiddenActors(rejected); UnityEngine.Object.Destroy(rejected.gameObject); }
        }
        finally
        {
            foreach (CollectionCardVisual tile in page.Tiles)
            { ComboCollectionCards.ReleaseHiddenActors(tile); UnityEngine.Object.Destroy(tile.gameObject); }
            sunActor.Destroy(); carrierActor.Destroy();
            display.m_cardsCollection = previousCollection; display.CurrentPageNum = previousPage;
        }
    }
}
