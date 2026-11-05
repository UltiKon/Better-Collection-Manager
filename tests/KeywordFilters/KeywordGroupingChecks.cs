using System;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckKeywordGrouping()
    {
        CollectionManager.Get().Display.m_setFilterTray = null;
        CollectionManager.Get().Cards.Clear();
        GameDbf.KeywordText.Records.Clear();
        GameStrings.Names.Clear();
        KeywordFilterState.Clear();

        GAME_TAG fabled = (GAME_TAG)4085, fabledPlus = (GAME_TAG)4184;
        GAME_TAG tourist = (GAME_TAG)3228, mageTourist = (GAME_TAG)3606;
        GAME_TAG rogueTourist = (GAME_TAG)3597;
        AddKeyword(fabled, "Sagenhaft");
        AddKeyword(fabledPlus, "Sagenhaft+");
        AddKeyword(tourist, "Tourist");
        AddKeyword(mageTourist, "Magiertourist");
        AddKeyword(rogueTourist, "Schurkentourist");
        var plus = Card("fabled-plus", fabledPlus);
        var mage = Card("mage-tourist", mageTourist);
        var rogue = Card("rogue-tourist", rogueTourist);
        var touristRef = Card("tourist-ref");
        touristRef.Definition.References.Add(rogueTourist);
        var fabledRef = Card("fabled-ref");
        fabledRef.Definition.References.Add(fabled);
        CollectionManager.Get().Cards.AddRange(new[] { plus, mage, rogue, touristRef, fabledRef });
        KeywordCatalog.Invalidate();
        var entries = KeywordCatalog.GetEntries();
        Check(entries.Count == 2, "Fabled variants and all localized class Tourists form two families");
        var fabledEntry = entries.Single(e => e.Name == "Sagenhaft");
        var touristEntry = entries.Single(e => e.Name == "Tourist");
        Check(fabledEntry.Tags.Count == 2 && fabledEntry.HasCards && fabledEntry.HasReferencedCards,
            "Fabled base without own matches still supplies localization and references");
        Check(touristEntry.Tags.Count == 3 && touristEntry.HasCards && touristEntry.HasReferencedCards,
            "Tourist families include the generic tag and every class alias");
        Check(entries.All(e => e.Group == KeywordGroup.Expansion), "both families are expansion mechanics");

        KeywordFilterState.SetSelected(touristEntry.Tag, true);
        Check(KeywordFilterState.Matches(mage) && KeywordFilterState.Matches(rogue),
            "one Tourist selection matches either class, using OR within the family");
        Check(!KeywordFilterState.Matches(plus) && KeywordFilterState.Matches(touristRef),
            "Tourist includes references while excluding other mechanics");
        KeywordFilterState.Clear();
        KeywordFilterState.SetSelected(touristEntry.Tag, true);
        Check(KeywordFilterState.Matches(touristRef) && KeywordFilterState.Matches(mage),
            "grouped Tourist selection includes direct and reference aliases");
        KeywordFilterState.Clear();
        KeywordFilterState.SetSelected(fabledEntry.Tag, true);
        Check(KeywordFilterState.Matches(plus), "Fabled filter includes Fabled+");
        KeywordFilterState.SetSelected(touristEntry.Tag, true);
        Check(!KeywordFilterState.Matches(plus), "different grouped mechanics are still ANDed");
        plus.Definition.Tags.Add(mageTourist);
        Check(KeywordFilterState.Matches(plus), "cards can satisfy multiple selected families");
        KeywordFilterState.Clear();
        KeywordFilterState.SetSelected(fabledEntry.Tag, true);
        Check(KeywordFilterState.Matches(fabledRef), "Fabled references also use the base/plus family");

        // Change the representative and availability exactly as a set change does.
        KeywordFilterState.SetSelected(fabledEntry.Tag, true);
        CollectionManager.Get().Cards.Clear();
        var baseFabled = Card("base-fabled", fabled);
        CollectionManager.Get().Cards.Add(baseFabled);
        KeywordCatalog.Invalidate();
        entries = KeywordCatalog.GetEntries();
        KeywordFilterState.PruneToAvailable(entries);
        Check(entries.Single().Tag == fabled && KeywordFilterState.IsSelected(fabled)
            && KeywordFilterState.IsActive && KeywordFilterState.SelectedTags.Count() == 1,
            "family selection survives a representative change to an own-only card pool");
        Check(KeywordFilterState.Matches(baseFabled) && entries[0].Group == KeywordGroup.Expansion,
            "family classification remains stable as the card pool changes");
        KeywordFilterState.Clear();

        foreach (int id in new[] { 190, 218, 443, 791, 1085, 1211, 1333, 1720, 1725, 2821, 2934 })
            Check(KeywordGroups.GetGroup(new[] { (GAME_TAG)id }) == KeywordGroup.Evergreen,
                "reusable base/class mechanics are Evergreen: " + id);
        foreach (int id in new[] { 546, 849, 1427, 2456, 3318, 4028, 4354, 99999 })
            Check(KeywordGroups.GetGroup(new[] { (GAME_TAG)id }) == KeywordGroup.Expansion,
                "expansion mechanics, occasional returns and unknown new mechanics stay Expansion: " + id);
        foreach (int id in new[] { 482, 483, 484, 3457, 3458, 3469 })
            Check(KeywordGroups.GetGroup(new[] { (GAME_TAG)id }) == KeywordGroup.Factions,
                "Gadgetzan gangs and StarCraft factions are Factions: " + id);
        Check(KeywordGroups.GetGroup(new[] { (GAME_TAG)99999, GAME_TAG.TAUNT }) == KeywordGroup.Evergreen,
            "classification considers every same-name alias, independent of representative tag");

        AddKeyword((GAME_TAG)546, "Anpassen");
        var adapt = Card("original-adapt", (GAME_TAG)546);
        var reusedAdapt = Card("reused-adapt", (GAME_TAG)546);
        reusedAdapt.Set = TAG_CARD_SET.CORE;
        CollectionManager.Get().Cards.AddRange(new[] { adapt, reusedAdapt });
        KeywordCatalog.Invalidate();
        Check(KeywordCatalog.GetEntries().Single(e => e.Name == "Anpassen").Group == KeywordGroup.Expansion,
            "Adapt does not become Evergreen when matching cards come from multiple sets");
    }
}
