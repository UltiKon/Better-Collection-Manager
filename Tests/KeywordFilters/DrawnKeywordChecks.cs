using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckDrawnKeywordFamily()
    {
        CollectionManager.Get().Display.m_setFilterTray = null;
        var cards = CollectionManager.Get().Cards;
        cards.Clear();
        GameDbf.KeywordText.Records.Clear();
        GameStrings.Names.Clear();
        KeywordFilterState.Clear();

        GAME_TAG casts = (GAME_TAG)1077, summoned = (GAME_TAG)3128;
        AddKeyword(casts, "Beim Ziehen wirken");
        AddKeyword(summoned, "Beim Ziehen herbeirufen");
        var spell = Card("casts-when-drawn", casts);
        var minion = Card("summoned-when-drawn", summoned);
        var spellReference = Card("casts-when-drawn-reference");
        spellReference.Definition.References.Add(casts);
        var minionReference = Card("summoned-when-drawn-reference");
        minionReference.Definition.References.Add(summoned);
        var unrelated = Card("unrelated");
        cards.AddRange(new[] { spell, minion, spellReference, minionReference, unrelated });
        KeywordCatalog.Invalidate();
        var entry = KeywordCatalog.GetEntries().Single();
        Check(entry.Name == "Beim Ziehen wirken" && entry.Tags.Count == 2
            && entry.Group == KeywordGroup.Evergreen,
            "Summoned When Drawn joins the localized Casts When Drawn Evergreen family");
        Check(entry.HasCards && entry.HasReferencedCards && !entry.UsesReferencesAsHas,
            "the drawn family includes both direct and referenced metadata");

        KeywordFilterState.SetSelected(entry.Tag, true);
        Check(KeywordFilterState.Matches(spell) && KeywordFilterState.Matches(minion)
            && !KeywordFilterState.Matches(unrelated) && KeywordFilterState.Matches(minionReference),
            "Casts When Drawn matches either drawn mechanic and its references");
        KeywordFilterState.Clear();
        KeywordFilterState.SetSelected(entry.Tag, true);
        Check(KeywordFilterState.Matches(spellReference) && KeywordFilterState.Matches(minionReference)
            && KeywordFilterState.Matches(spell) && KeywordFilterState.Matches(minion),
            "one drawn-family selection includes both own and reference matches");

        KeywordFilterState.SetSelected(entry.Tag, true);
        cards.Remove(spell);
        cards.Remove(spellReference);
        KeywordCatalog.Invalidate();
        var entries = KeywordCatalog.GetEntries();
        KeywordFilterState.PruneToAvailable(entries);
        entry = entries.Single();
        Check(entry.Tag == summoned && entry.Name == "Beim Ziehen wirken"
            && entry.Group == KeywordGroup.Evergreen,
            "the family keeps its base label and Evergreen group when only summoned cards remain");
        Check(KeywordFilterState.IsSelected(summoned) && KeywordFilterState.SelectedTags.Count() == 1,
            "the unified drawn-family selection survives a representative change");
        KeywordFilterState.Clear();
        GameStrings.Names.Remove(casts);
        Check(KeywordGroups.GetDisplayName(summoned, "Summoned When Drawn") == "Casts When Drawn",
            "missing base localization falls back to the shared drawn-family label");
    }
}
