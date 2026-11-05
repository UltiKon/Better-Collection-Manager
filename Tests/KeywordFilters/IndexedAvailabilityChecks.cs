using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;

internal static partial class Program
{
    private static void CheckIndexedAvailability()
    {
        CollectionQualitySettings.HideEmptyFilters = true;
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        var cards = CollectionManager.Get().Cards; cards.Clear();
        CollectionManager.Get().Display.m_setFilterTray = null;
        GameDbf.KeywordText.Records.Clear(); GameStrings.Names.Clear();
        AddKeyword(GAME_TAG.TAUNT, "Taunt"); AddKeyword(GAME_TAG.PREPARE, "Prepare");
        for (int i = 0; i < 70; i++)
        {
            var card = Card("indexed-" + i);
            if ((i & 1) != 0) card.Definition.Tags.Add(GAME_TAG.TAUNT);
            if ((i & 2) != 0) card.Definition.References.Add(GAME_TAG.PREPARE);
            card.Definition.CardType = card.CardType = (i & 4) != 0 ? TAG_CARDTYPE.MINION : TAG_CARDTYPE.SPELL;
            card.Definition.School = (i & 8) != 0 ? TAG_SPELL_SCHOOL.FIRE : TAG_SPELL_SCHOOL.FROST;
            card.Rarity = i % 3 == 0 ? TAG_RARITY.FREE : i % 3 == 1 ? TAG_RARITY.COMMON : TAG_RARITY.RARE;
            if (i % 5 == 0) card.Definition.Races.Add(TAG_RACE.ALL);
            else if (i % 5 == 1) card.Definition.Race = TAG_RACE.DRAGON;
            else
            {
                card.Definition.Races.Add(TAG_RACE.BEAST);
                if ((i & 16) != 0) card.Definition.Races.Add(TAG_RACE.DRAGON);
            }
            card.Set = (i & 32) == 0 ? TAG_CARD_SET.CORE : TAG_CARD_SET.JAIL;
            cards.Add(card);
        }
        var core = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.CORE } };
        var jail = new SetFilterItem { CardSets = new List<TAG_CARD_SET> { TAG_CARD_SET.JAIL } };
        KeywordCatalog.Invalidate(); KeywordCatalog.GetEntries();
        FilterAvailability.Capture(cards);
        foreach (FilterModeBehavior behavior in new[] { FilterModeBehavior.Category, FilterModeBehavior.Expression })
        for (int edit = 0; edit < 48; edit++)
        {
            CollectionQualitySettings.FilterModeBehavior = behavior;
            KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
            foreach (FilterTrayKind kind in FilterModes.Kinds) FilterModes.Set(kind, (FilterMode)(edit % 6));
            if ((edit & 1) != 0) KeywordFilterState.SetSelected(GAME_TAG.TAUNT, true);
            if ((edit & 2) != 0) KeywordFilterState.SetSelected(GAME_TAG.PREPARE, true);
            if ((edit & 4) != 0) { ExtraFilterState.SetRace(TAG_RACE.BEAST, true); ExtraFilterState.SetRace(TAG_RACE.DRAGON, true); }
            if ((edit & 8) != 0) ExtraFilterState.SetType(TAG_CARDTYPE.SPELL, true);
            if (edit % 4 == 0) ExtraFilterState.SetRarity(TAG_RARITY.COMMON, true);
            if (edit % 5 == 0) ExtraFilterState.SetSchool(TAG_SPELL_SCHOOL.FIRE, true);
            if (edit % 6 == 0) ExpansionFilterState.SetSelected(core, true);
            if (edit % 7 == 0) ExpansionFilterState.SetSelected(jail, true);
            foreach (GAME_TAG tag in new[] { GAME_TAG.TAUNT, GAME_TAG.PREPARE })
                Check(FilterAvailability.Keyword(tag) == (KeywordFilterState.IsSelected(tag) || cards.Any(card =>
                    ExpansionFilterState.Matches(card) && ExtraFilterState.Matches(card) && KeywordFilterState.MatchesCandidate(card, tag))),
                    "Indexed keyword availability matches scalar category and expression predicates");
            void Extra(FilterTrayKind kind, int value, bool selected)
            {
                Check(FilterAvailability.Extra(kind, value, selected) == (selected || cards.Any(card => ExpansionFilterState.Matches(card)
                    && KeywordFilterState.Matches(card) && ExtraFilterState.MatchesCandidate(card, kind, value))),
                    "Indexed extra availability matches scalar rules, including All races, dual races and Basic rarity");
            }
            Extra(FilterTrayKind.Type, (int)TAG_CARDTYPE.MINION, ExtraFilterState.HasType(TAG_CARDTYPE.MINION));
            Extra(FilterTrayKind.Type, (int)TAG_CARDTYPE.SPELL, ExtraFilterState.HasType(TAG_CARDTYPE.SPELL));
            Extra(FilterTrayKind.Race, (int)TAG_RACE.BEAST, ExtraFilterState.HasRace(TAG_RACE.BEAST));
            Extra(FilterTrayKind.Race, (int)TAG_RACE.DRAGON, ExtraFilterState.HasRace(TAG_RACE.DRAGON));
            Extra(FilterTrayKind.Race, (int)TAG_RACE.ALL, ExtraFilterState.HasRace(TAG_RACE.ALL));
            Extra(FilterTrayKind.School, (int)TAG_SPELL_SCHOOL.FROST, ExtraFilterState.HasSchool(TAG_SPELL_SCHOOL.FROST));
            Extra(FilterTrayKind.Rarity, (int)TAG_RARITY.COMMON, ExtraFilterState.HasRarity(TAG_RARITY.COMMON));
            Extra(FilterTrayKind.Rarity, (int)TAG_RARITY.RARE, ExtraFilterState.HasRarity(TAG_RARITY.RARE));
            foreach (var item in new[] { core, jail })
                Check(FilterAvailability.Expansion(item, ExpansionFilterState.IsSelected(item)) == (ExpansionFilterState.IsSelected(item)
                    || cards.Any(card => KeywordFilterState.Matches(card) && ExtraFilterState.Matches(card)
                        && ExpansionFilterState.MatchesCandidate(card, item))), "Indexed expansion candidates match native set groups");
            foreach (bool hide in new[] { true, false })
            {
            CollectionQualitySettings.HideEmptyFilters = hide;
            foreach (var card in cards)
                Check(FilterAvailability.TryMatchCurrent(card, out bool match) && match == (KeywordFilterState.Matches(card)
                    && ExtraFilterState.Matches(card) && ExpansionFilterState.Matches(card)),
                    "Indexed result membership agrees with scalar filtering across all categories");
            }
            CollectionQualitySettings.HideEmptyFilters = true;
            Check(!FilterAvailability.TryMatchCurrent(Card("outside-native-pool"), out _),
                "Cards outside the native index retain the scalar fallback path");
        }
        KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
        CollectionQualitySettings.HideEmptyFilters = false;
        CollectionQualitySettings.FilterModeBehavior = FilterModeBehavior.Category;
        FilterAvailability.Clear(); FilterModes.StartSession();
    }
}
