using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using PegasusShared;

internal static partial class Program
{
    private static void CheckCatalogScopeIndex()
    {
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        CollectionQualitySettings.HideEmptyFilters = true;
        FilterModes.Set(FilterTrayKind.Expansions, FilterMode.Single);
        var cards = CollectionManager.Get().Cards; cards.Clear();
        GameDbf.KeywordText.Records.Clear(); GameStrings.Names.Clear();
        AddKeyword(GAME_TAG.PREPARE, "Prepare"); AddKeyword(GAME_TAG.PREPARE_ALIAS, "Prepare");
        AddKeyword(GAME_TAG.TAUNT, "Taunt"); AddKeyword(GAME_TAG.ROGUE_TOURIST, "Tourist");
        AddKeyword((GAME_TAG)546, "Adapt"); AddKeyword((GAME_TAG)9001, "Added mechanic");
        for (int i = 0; i < 130; i++)
        {
            var card = Card("scope-" + i, i < 65 ? GAME_TAG.PREPARE : GAME_TAG.PREPARE_ALIAS);
            card.Set = i < 65 ? TAG_CARD_SET.CORE : TAG_CARD_SET.JAIL;
            if (i < 65) card.Definition.Tags.Add(GAME_TAG.ROGUE_TOURIST);
            else card.Definition.References.Add(GAME_TAG.ROGUE_TOURIST);
            if (i % 3 == 0) card.Definition.Tags.Add(GAME_TAG.TAUNT);
            if (i % 5 == 0) card.Definition.References.Add((GAME_TAG)546);
            cards.Add(card);
        }
        var core = new SetFilterItem { Text = "Core", FormatType = FormatType.FT_WILD,
            CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
        var jail = new SetFilterItem { Text = "Jail", FormatType = FormatType.FT_WILD,
            CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.JAIL } };
        var tray = CollectionManager.Get().Display.m_setFilterTray = new SetFilterTray { m_selected = core };
        string Signature(IReadOnlyList<KeywordCatalog.Entry> entries) => string.Join(";", entries.Select(e =>
            $"{e.Tag}:{e.HasCards}:{e.HasReferencedCards}:{e.UsesReferencesAsHas}:" + string.Join(",", e.Tags)));
        var freshSignatures = new Dictionary<SetFilterItem, string>();
        foreach (var source in new[] { core, jail })
        {
            tray.m_selected = source; KeywordCatalog.Invalidate();
            freshSignatures[source] = Signature(KeywordCatalog.GetEntries());
        }
        tray.m_selected = core;
        KeywordCatalog.Invalidate(); KeywordCatalog.GetEntries(); FilterAvailability.Capture(cards);
        // Repeated changes keep the native universe and immutable keyword aliases stable.
        foreach (bool custom in new[] { false, true })
        for (int edit = 0; edit < 6; edit++)
        {
            SetFilterItem source = edit % 2 == 0 ? jail : core;
            tray.m_selected = source;
            if (custom) ExpansionFilterState.SetSelected(source, true);
            int reads = CollectibleCard.EntityReads, generation = KeywordCatalog.Generation;
            KeywordCatalog.InvalidateScope();
            var entries = KeywordCatalog.GetEntries();
            string indexed = Signature(entries);
            Check(CollectibleCard.EntityReads == reads && KeywordCatalog.Generation == generation,
                "Expansion scope changes reuse card metadata and retain the availability index generation");
            var prepare = entries.Single(entry => entry.Name == "Prepare");
            Check(prepare.Tag == (source == core ? GAME_TAG.PREPARE : GAME_TAG.PREPARE_ALIAS),
                "Scope counts preserve representative alias changes across expansions");
            var tourist = entries.Single(entry => entry.Tag == GAME_TAG.ROGUE_TOURIST);
            Check(tourist.HasCards == (source == core) && tourist.HasReferencedCards == (source == jail)
                && !tourist.UsesReferencesAsHas, "Global direct markers preserve reference semantics in a reference-only expansion");
            Check(entries.Single(entry => entry.Name == "Adapt").UsesReferencesAsHas,
                "Reference-only mechanics keep their Has semantics after scope changes");
            KeywordFilterState.SetSelected(prepare.Tag, true);
            KeywordFilterState.PruneToAvailable(entries);
            foreach (var card in cards)
                Check(FilterAvailability.TryMatchCurrent(card, out bool match)
                    && match == (KeywordFilterState.Matches(card) && ExpansionFilterState.Matches(card)),
                    "Warm result indexes keep alias and expansion filtering correct across scope rebuilds");
            KeywordFilterState.Clear();
            // Compare to independently built oracles without discarding the warm result masks.
            Check(freshSignatures[source] == indexed, "Scoped catalog is identical to a fresh metadata scan");
        }
        int priorGeneration = KeywordCatalog.Generation;
        var added = Card("added-card", (GAME_TAG)9001); added.Set = TAG_CARD_SET.CORE;
        cards.Add(added); tray.m_selected = core; ExpansionFilterState.Clear(); KeywordCatalog.InvalidateScope();
        Check(KeywordCatalog.GetEntries().Any(entry => (int)entry.Tag == 9001)
            && KeywordCatalog.Generation != priorGeneration,
            "A new collection card invalidates metadata indexes during the next scope update");
        // True collection changes must still rebuild metadata even if object identities stay unchanged.
        cards[0].Definition.Tags.Add((GAME_TAG)9000); AddKeyword((GAME_TAG)9000, "New mechanic");
        tray.m_selected = core; ExpansionFilterState.Clear(); KeywordCatalog.Invalidate();
        Check(KeywordCatalog.GetEntries().Any(entry => (int)entry.Tag == 9000),
            "A native metadata invalidation exposes new mechanics on existing card objects");
        CollectionManager.Get().Display.m_setFilterTray = null;
        CollectionQualitySettings.HideEmptyFilters = false;
        KeywordFilterState.Clear(); ExpansionFilterState.Clear(); FilterAvailability.Clear(); FilterModes.StartSession();
    }
}
