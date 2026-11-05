using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static int s_assertions;

    private static void Main()
    {
        // The aliases are synthetic collision cases. Sawbones' real direct
        // PREPARE tag comes from HearthSim/hsdata CardDefs.xml (JAIL_444).
        AddKeyword(GAME_TAG.PREPARE, "Prepare");
        AddKeyword(GAME_TAG.PREPARE_ALIAS, "prepare");
        AddKeyword(GAME_TAG.PREPARE_REF_ALIAS, "Prepare");
        AddKeyword(GAME_TAG.TAUNT, "Taunt");

        CollectibleCard sawbones = Card("JAIL_444", GAME_TAG.PREPARE);
        CollectibleCard vanessa = Card("JAIL_407", GAME_TAG.PREPARE);
        CollectibleCard aliasCard = Card("synthetic-alias", GAME_TAG.PREPARE_ALIAS);
        CollectibleCard anotherAlias = Card("synthetic-another-alias", GAME_TAG.PREPARE_ALIAS);
        CollectibleCard fourthAlias = Card("synthetic-fourth-alias", GAME_TAG.PREPARE_ALIAS);
        CollectibleCard bothKeywords = Card("synthetic-both", GAME_TAG.PREPARE_ALIAS, GAME_TAG.TAUNT);
        CollectibleCard wantedPoster = Card("CAP_407");
        wantedPoster.Definition.References.Add(GAME_TAG.PREPARE);
        CollectibleCard jailbird = Card("JAIL_453");
        jailbird.Definition.References.Add(GAME_TAG.PREPARE);
        CollectibleCard auctioneer = Card("JAIL_718", GAME_TAG.DECK_ACTION_COST);
        auctioneer.Definition.References.Add(GAME_TAG.PREPARE);
        CollectibleCard otherDeckAction = Card("synthetic-non-prepare-deck-action", GAME_TAG.DECK_ACTION_COST);
        CollectibleCard aliasReference = Card("synthetic-reference");
        aliasReference.Definition.References.Add(GAME_TAG.PREPARE_REF_ALIAS);
        CollectibleCard neither = Card("synthetic-neither");
        CollectionManager.Get().Cards.AddRange(new[]
            { sawbones, vanessa, aliasCard, anotherAlias, fourthAlias, bothKeywords,
              wantedPoster, jailbird, auctioneer, otherDeckAction, aliasReference, neither });

        var entries = KeywordCatalog.GetEntries();
        Check(entries.Count == 2, "same-name keyword rows are deduplicated");
        var prepare = entries.Single(e => e.Name.Equals("Prepare", StringComparison.OrdinalIgnoreCase));
        Check(prepare.Tags.Count == 3, "all aliases survive, even reference-only aliases");
        Check(prepare.Tag == GAME_TAG.PREPARE_ALIAS, "the more common alias represents the row");
        Check(prepare.HasCards && prepare.HasReferencedCards, "Has and Referenced availability are counted separately");
        var taunt = entries.Single(e => e.Tag == GAME_TAG.TAUNT);
        Check(taunt.HasCards && !taunt.HasReferencedCards, "own-only keywords retain direct metadata availability");

        KeywordFilterState.SetSelected(prepare.Tag, true);
        foreach (var card in new[] { sawbones, vanessa, aliasCard, auctioneer,
            wantedPoster, jailbird, aliasReference, bothKeywords })
            Check(KeywordFilterState.Matches(card), "Prepare matches direct markers and references: " + card.CardId);
        Check(!KeywordFilterState.Matches(otherDeckAction), "deck action cost alone does not imply Prepare");
        Check(!KeywordFilterState.Matches(neither), "unrelated cards are excluded");
        Check(!KeywordFilterState.Matches(null), "null cards do not match an active filter");
        Check(!KeywordFilterState.Matches(new CollectibleCard { Definition = null }),
            "cards without metadata do not match an active filter");

        KeywordFilterState.SetSelected(GAME_TAG.TAUNT, true);
        Check(KeywordFilterState.Matches(bothKeywords), "different selected keywords are ANDed");
        Check(!KeywordFilterState.Matches(sawbones) && !KeywordFilterState.Matches(wantedPoster),
            "a second keyword is required on both direct and reference matches");
        aliasReference.Definition.Tags.Add(GAME_TAG.TAUNT);
        Check(KeywordFilterState.Matches(aliasReference), "one direct and one referenced keyword satisfy AND");
        wantedPoster.Definition.References.Add(GAME_TAG.TAUNT);
        Check(KeywordFilterState.Matches(wantedPoster), "two referenced keywords satisfy AND");
        aliasCard.Definition.References.Add(GAME_TAG.TAUNT);
        Check(KeywordFilterState.Matches(aliasCard), "direct aliases combine with a referenced second keyword");
        KeywordFilterState.SetSelected(GAME_TAG.TAUNT, false);
        aliasReference.Definition.Tags.Remove(GAME_TAG.TAUNT);
        wantedPoster.Definition.References.Remove(GAME_TAG.TAUNT);
        aliasCard.Definition.References.Remove(GAME_TAG.TAUNT);
        KeywordFilterState.SetSelected(GAME_TAG.PREPARE, true);
        KeywordFilterState.SetSelected(GAME_TAG.PREPARE_REF_ALIAS, true);
        KeywordFilterState.PruneToAvailable(entries);
        Check(KeywordFilterState.SelectedTags.Count() == 1,
            "own and reference aliases collapse into one canonical selection");

        // Force the canonical tag to change when the format's card pool changes.
        GAME_TAG previous = prepare.Tag;
        CollectionManager.Get().Cards.Clear();
        CollectionManager.Get().Cards.Add(previous == GAME_TAG.PREPARE ? aliasCard : sawbones);
        CollectionManager.Get().Cards.Add(wantedPoster);
        KeywordCatalog.Invalidate();
        entries = KeywordCatalog.GetEntries();
        prepare = entries.Single();
        Check(prepare.Tag != previous, "fixture changed the representative tag");
        KeywordFilterState.PruneToAvailable(entries);
        Check(KeywordFilterState.IsSelected(prepare.Tag) && KeywordFilterState.SelectedTags.Count() == 1,
            "the single selection survives a representative change");
        KeywordFilterState.SetSelected(prepare.Tag, false);
        Check(!KeywordFilterState.IsActive, "deselecting the new row clears the old selection");
        Check(KeywordFilterState.Matches(neither), "clearing restores unrelated cards");

        KeywordFilterState.SetSelected(GAME_TAG.PREPARE, true);
        KeywordFilterState.PruneToAvailable(new List<KeywordCatalog.Entry>());
        Check(!KeywordFilterState.IsActive, "unavailable keywords are pruned");
        KeywordFilterState.SetSelected(GAME_TAG.PREPARE, true);
        KeywordFilterState.PruneToAvailable(null);
        Check(!KeywordFilterState.IsActive, "null catalog clears selections");

        CollectionManager.Get().Cards.Clear();
        CollectionManager.Get().Cards.Add(auctioneer);
        KeywordCatalog.Invalidate();
        entries = KeywordCatalog.GetEntries();
        Check(entries.Count == 1, "Prepare remains available in a pool with only referenced deck-action metadata");
        Check(entries[0].HasCards && !entries[0].HasReferencedCards,
            "Prepare deck actions use direct keyword metadata");
        KeywordFilterState.SetSelected(entries[0].Tag, true);
        Check(KeywordFilterState.Matches(auctioneer), "catalog and filtering agree on own Prepare markers");
        KeywordFilterState.PruneToAvailable(entries);
        Check(KeywordFilterState.IsSelected(entries[0].Tag),
            "selection survives when only direct keyword metadata remains");
        KeywordFilterState.Clear();

        CollectionManager.Get().Cards.Clear();
        CollectionManager.Get().Cards.Add(aliasReference);
        KeywordCatalog.Invalidate();
        entries = KeywordCatalog.GetEntries();
        Check(entries.Count == 1 && entries[0].HasCards && !entries[0].HasReferencedCards
            && entries[0].UsesReferencesAsHas,
            "reference-only mechanics remain available in the catalog");
        KeywordFilterState.SetSelected(entries[0].Tag, true);
        KeywordFilterState.PruneToAvailable(entries);
        Check(KeywordFilterState.IsSelected(entries[0].Tag) && KeywordFilterState.SelectedTags.Count() == 1
            && KeywordFilterState.Matches(aliasReference), "selection survives when only reference metadata remains");
        KeywordFilterState.Clear();

        // Tourist is deliberately synthetic here: availability is determined by
        // matches, rather than a hardcoded blacklist of keyword names.
        AddKeyword(GAME_TAG.ROGUE_TOURIST, "Rogue Tourist");
        var tourist = Card("synthetic-tourist", GAME_TAG.ROGUE_TOURIST);
        tourist.Set = TAG_CARD_SET.CORE;
        var touristReference = Card("synthetic-tourist-reference");
        touristReference.Definition.References.Add(GAME_TAG.ROGUE_TOURIST);
        var cosmeticReference = Card("synthetic-cosmetic-reference");
        cosmeticReference.Set = TAG_CARD_SET.CORE;
        cosmeticReference.IsHeroSkin = true;
        cosmeticReference.Definition.References.Add(GAME_TAG.ROGUE_TOURIST);
        CollectionManager.Get().Cards.Clear();
        CollectionManager.Get().Cards.AddRange(new[] { tourist, touristReference, cosmeticReference, wantedPoster });
        var selected = new SetFilterItem
        {
            Text = "Core", CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE }
        };
        CollectionManager.Get().Display.m_setFilterTray = new SetFilterTray { m_selected = selected };
        KeywordCatalog.Invalidate();
        entries = KeywordCatalog.GetEntries();
        Check(entries.Count == 1 && entries[0].Tag == GAME_TAG.ROGUE_TOURIST,
            "catalog availability respects selected card sets");
        Check(entries[0].HasCards && !entries[0].HasReferencedCards,
            "references outside the selected set and cosmetics do not affect availability");
        KeywordFilterState.SetSelected(GAME_TAG.ROGUE_TOURIST, true);
        KeywordFilterState.PruneToAvailable(entries);
        Check(KeywordFilterState.IsSelected(GAME_TAG.ROGUE_TOURIST) && KeywordFilterState.SelectedTags.Count() == 1,
            "Tourist selection survives in an own-only card pool");

        selected.Text = "Violet Hold";
        selected.CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.JAIL };
        KeywordCatalog.Invalidate();
        entries = KeywordCatalog.GetEntries();
        Check(entries.Count == 2 && entries.Single(e => e.Tag == GAME_TAG.ROGUE_TOURIST).HasReferencedCards
            && !entries.Single(e => e.Tag == GAME_TAG.ROGUE_TOURIST).HasCards,
            "reference-only availability is scoped to the selected set");
        KeywordFilterState.PruneToAvailable(entries);
        Check(KeywordFilterState.IsSelected(GAME_TAG.ROGUE_TOURIST),
            "Tourist selection survives a set change from direct matches to references");
        Check(KeywordFilterState.Matches(touristReference), "the unified selection returns reference matches after the set change");
        KeywordFilterState.Clear();
        CheckKeywordGrouping();
        CheckKeywordPresentation();
        CheckDrawnKeywordFamily();
        CheckFilterModes();
        CheckExpressions();
        CheckBitSetExpressions();
        CheckNotExpressions();
        CheckIndexedAvailability();
        CheckCatalogScopeIndex();
        CheckAvailabilityPerformance();
        CheckCategories();
        CheckCategoryDeckImports();
        CheckCategoryRemoval();
        CheckCategoryCombos();
        CheckCatalogTransfers();
        CheckPackageCatalog();
        CheckUncategorized();
        Console.WriteLine($"Passed {s_assertions} keyword filter regression checks.");
    }

    private static void AddKeyword(GAME_TAG tag, string name)
    {
        GameDbf.KeywordText.Records.Add(new KeywordTextDbfRecord { Tag = (int)tag });
        GameStrings.Names[tag] = name;
    }

    private static CollectibleCard Card(string id, params GAME_TAG[] tags)
    {
        var card = new CollectibleCard { CardId = id };
        card.Definition.Tags.UnionWith(tags);
        return card;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception("FAIL: " + message);
        s_assertions++;
    }
}
