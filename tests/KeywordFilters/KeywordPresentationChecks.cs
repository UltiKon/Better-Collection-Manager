using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckKeywordPresentation()
    {
        CollectionManager.Get().Display.m_setFilterTray = null;
        CollectionManager.Get().Cards.Clear();
        GameDbf.KeywordText.Records.Clear();
        GameStrings.Names.Clear();
        KeywordFilterState.Clear();

        GAME_TAG quest = (GAME_TAG)462, questline = (GAME_TAG)1725;
        GAME_TAG runes = (GAME_TAG)4947, adapt = (GAME_TAG)546, adaptAlias = (GAME_TAG)9001;
        GAME_TAG colossal = (GAME_TAG)2247, colossalAlias = (GAME_TAG)9002;
        AddKeyword(quest, "Quest");
        AddKeyword(questline, "Questline");
        AddKeyword(runes, "Runen benötigt");
        AddKeyword(adapt, "Adapt");
        AddKeyword(adaptAlias, "adapt");
        AddKeyword(colossal, "Colossal +X");
        AddKeyword(colossalAlias, "Colossal");
        AddKeyword(GAME_TAG.TAUNT, "Taunt");

        var questCard = Card("quest", quest);
        var questlineCard = Card("questline", questline);
        var runeCard = Card("tracker", runes);
        runeCard.Definition.References.Add(runes);
        var adaptCard = Card("adapt-reference-only");
        adaptCard.Definition.References.Add(adapt);
        var adaptAliasCard = Card("adapt-alias-reference-only");
        adaptAliasCard.Definition.References.Add(adaptAlias);
        var cosmeticAdapt = Card("cosmetic-adapt", adapt);
        cosmeticAdapt.IsHeroSkin = true;
        var tauntCard = Card("own-taunt-outside-scope", GAME_TAG.TAUNT);
        tauntCard.Set = TAG_CARD_SET.CORE;
        var tauntReference = Card("real-taunt-reference");
        tauntReference.Definition.References.Add(GAME_TAG.TAUNT);
        var colossalCard = Card("colossal", colossal);
        var colossalAliasCard = Card("colossal-alias", colossalAlias);
        var cards = CollectionManager.Get().Cards;
        cards.AddRange(new[] { questCard, questlineCard, runeCard, adaptCard, adaptAliasCard,
            cosmeticAdapt, tauntCard, tauntReference, colossalCard, colossalAliasCard });
        KeywordCatalog.Invalidate();
        var entries = KeywordCatalog.GetEntries();
        Check(entries.All(e => !e.Tags.Contains(runes)), "Runes Required is hidden regardless of localization or matching cards");
        var questEntry = entries.Single(e => e.Name == "Quest");
        Check(questEntry.Tags.Count == 2 && questEntry.Group == KeywordGroup.Evergreen,
            "Quest and Questline share an Evergreen family");
        KeywordFilterState.SetSelected(questEntry.Tag, true);
        Check(KeywordFilterState.Matches(questCard) && KeywordFilterState.Matches(questlineCard)
            && !KeywordFilterState.Matches(adaptCard), "Quest selection matches Quest or Questline");
        KeywordFilterState.Clear();

        cards.Remove(questCard);
        KeywordCatalog.Invalidate();
        entries = KeywordCatalog.GetEntries();
        questEntry = entries.Single(e => e.Name == "Quest");
        Check(questEntry.Tag == questline && questEntry.Group == KeywordGroup.Evergreen,
            "Quest family keeps its name and group when only Questline has matches");

        var adaptEntry = entries.Single(e => e.Tags.Contains(adapt));
        Check(adaptEntry.HasCards && !adaptEntry.HasReferencedCards && adaptEntry.UsesReferencesAsHas,
            "reference-only Adapt stays available, ignoring cosmetic own-keyword markers");
        KeywordFilterState.SetSelected(adaptAlias, true);
        KeywordFilterState.PruneToAvailable(entries);
        Check(KeywordFilterState.IsSelected(adaptEntry.Tag) && KeywordFilterState.SelectedTags.Count() == 1,
            "a reference-only alias remaps to its canonical keyword selection");
        Check(KeywordFilterState.Matches(adaptCard) && KeywordFilterState.Matches(adaptAliasCard)
            && !KeywordFilterState.Matches(tauntReference), "Adapt matches reference metadata across aliases");
        KeywordFilterState.SetSelected(GAME_TAG.TAUNT, true);
        Check(!KeywordFilterState.Matches(adaptCard), "reference-only mechanics still AND with other selected keywords");
        adaptCard.Definition.Tags.Add(GAME_TAG.TAUNT);
        Check(KeywordFilterState.Matches(adaptCard), "promoted mechanics combine with normal own keywords");
        KeywordFilterState.Clear();

        var colossalEntry = entries.Single(e => e.Name == "Colossal");
        Check(colossalEntry.Tags.Count == 2, "parameterized and plain keyword labels deduplicate together");
        KeywordFilterState.SetSelected(colossalEntry.Tag, true);
        Check(KeywordFilterState.Matches(colossalCard) && KeywordFilterState.Matches(colossalAliasCard),
            "removing numeric placeholders preserves all matching tags");
        KeywordFilterState.Clear();

        foreach (var pair in new[]
        {
            ("Colossal +X", "Colossal"), ("Manathirst (X)", "Manathirst"),
            ("Overload: X", "Overload"), ("Keyword X", "Keyword"),
            ("Manadurst ( X )", "Manadurst"), ("Keyword (+X)", "Keyword"),
            ("Hex", "Hex"), ("Excavate", "Excavate"), ("X Marks the Spot", "X Marks the Spot"),
        })
            Check(KeywordGroups.GetDisplayName((GAME_TAG)99999, pair.Item1) == pair.Item2,
                "only trailing parameter placeholders are removed: " + pair.Item1);

        adaptCard.Definition.Tags.Remove(GAME_TAG.TAUNT);
        CollectionManager.Get().Display.m_setFilterTray = new SetFilterTray
        {
            m_selected = new SetFilterItem { Text = "Jail", CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.JAIL } }
        };
        KeywordCatalog.Invalidate();
        var tauntEntry = KeywordCatalog.GetEntries().Single(e => e.Tag == GAME_TAG.TAUNT);
        Check(!tauntEntry.HasCards && tauntEntry.HasReferencedCards && !tauntEntry.UsesReferencesAsHas,
            "own cards outside the selected set prevent genuine references from moving to Has");
    }
}
