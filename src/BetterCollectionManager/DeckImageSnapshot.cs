using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterCollectionManager
{
    internal static class DeckImageSnapshot
    {
        internal sealed class Card
        {
            internal string Id;
            internal EntityDef Definition;
            internal TAG_PREMIUM Premium;
            internal int Count;
            internal bool HighestQuality;
            internal int DisplayCount => SideboardOwnerId == "JAIL_397" ? 10 : Count;
            internal string SideboardOwnerId;
            internal int SideboardNumber, OwnedSideboardNumber;
            internal string SideboardOwner;
            internal int? NormalCraftCost;
            internal bool Uncraftable;
        }
        internal sealed class Section
        {
            internal string Title;
            internal string OwnerId, OwnerName;
            internal readonly List<Card> Cards = new List<Card>();
        }
        internal static List<Section> Capture(CollectionDeck deck, bool includePrices = true, DeckImageQuality quality = DeckImageQuality.Deck)
        {
            var sections = new List<Section> { ReadSection(deck, "Main deck", includePrices, quality) };
            foreach (var pair in deck.GetAllSideboards().OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string ownerName = PackageArtwork.Resolve(pair.Key)?.GetName() ?? pair.Key;
                Section section = ReadSection(pair.Value, "Sideboard: " + ownerName, includePrices, quality);
                section.OwnerId = pair.Key; section.OwnerName = ownerName;
                if (section.Cards.Count > 0) sections.Add(section);
            }
            return sections;
        }
        internal static List<Card> OrderedCards(List<Section> sections)
        {
            var cards = new List<Card>();
            var inserted = new HashSet<Section>();
            var numbers = new Dictionary<Section, int>();
            for (int i = 1; i < sections.Count; i++) numbers[sections[i]] = i;
            void Append(Section section)
            {
                inserted.Add(section);
                foreach (Card card in section.Cards)
                {
                    card.SideboardNumber = numbers[section]; card.SideboardOwner = section.OwnerName; card.SideboardOwnerId = section.OwnerId;
                    cards.Add(card);
                }
            }
            foreach (Card main in sections[0].Cards)
            {
                cards.Add(main);
                foreach (Section section in sections)
                    if (section.OwnerId == main.Id && !inserted.Contains(section))
                    { main.OwnedSideboardNumber = numbers[section]; Append(section); }
            }
            // Preserve even orphaned sideboards in incomplete decks.
            foreach (Section section in sections)
                if (section.OwnerId != null && !inserted.Contains(section)) Append(section);
            return cards;
        }
        internal static Card BackgroundCard(List<Section> sections)
        {
            var candidates = sections[0].Cards.Count > 0 ? sections[0].Cards : sections.SelectMany(section => section.Cards);
            return candidates.OrderByDescending(card => card.Definition.GetTag(GAME_TAG.RARITY))
                .ThenByDescending(card => card.Definition.GetTag(GAME_TAG.COST))
                .ThenBy(card => card.Id, StringComparer.Ordinal).First();
        }
        private static Section ReadSection(CollectionDeck deck, string title, bool includePrices, DeckImageQuality quality)
        {
            var section = new Section { Title = title };
            foreach (CollectionDeckSlot slot in deck.GetSlots())
            {
                if (slot == null || slot.Count <= 0) continue;
                EntityDef def = slot.GetEntityDef();
                if (def == null) throw new InvalidOperationException("Card data is still loading: " + slot.CardID);
                foreach (TAG_PREMIUM premium in new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE })
                {
                    int count = slot.GetCount(premium);
                    if (count <= 0) continue;
                    TAG_PREMIUM exported = quality switch {
                        DeckImageQuality.Normal => TAG_PREMIUM.NORMAL, DeckImageQuality.Golden => TAG_PREMIUM.GOLDEN,
                        DeckImageQuality.Highest => TAG_PREMIUM.NORMAL,
                        _ => premium
                    };
                    Card existing = section.Cards.Find(card => card.Id == slot.CardID && card.Premium == exported);
                    if (existing != null) existing.Count += count;
                    else
                    {
                        var card = new Card { Id = slot.CardID, Definition = def.Clone(), Premium = exported, Count = count, HighestQuality = quality == DeckImageQuality.Highest };
                        if (includePrices) ReadPrice(card);
                        section.Cards.Add(card);
                    }
                }
            }
            section.Cards.Sort((a, b) => {
                int order = a.Definition.GetTag(GAME_TAG.COST).CompareTo(b.Definition.GetTag(GAME_TAG.COST));
                if (order == 0) order = StringComparer.CurrentCultureIgnoreCase.Compare(a.Definition.GetName(), b.Definition.GetName());
                if (order == 0) order = StringComparer.Ordinal.Compare(a.Id, b.Id);
                return order != 0 ? order : a.Premium.CompareTo(b.Premium);
            });
            return section;
        }
        private static void ReadPrice(Card card)
        {
            try
            {
                CollectibleCard normal = CollectionManager.Get()?.GetCard(card.Id, TAG_PREMIUM.NORMAL);
                // Reward-only cards can be missing from the normal collection
                // index. Check native eligibility before reading the rarity's
                // generic dust price, even when there is no normal catalog tile.
                bool craftable = normal != null ? normal.IsCraftable
                    : CraftingUI.IsCraftingEventForCardActive(card.Id, TAG_PREMIUM.NORMAL, out _)
                        && FixedRewardsMgr.Get().CanCraftCard(card.Id, TAG_PREMIUM.NORMAL);
                if (!craftable) { card.Uncraftable = true; return; }
                var value = CraftingManager.GetCardValue(card.Id, TAG_PREMIUM.NORMAL);
                if (value != null) card.NormalCraftCost = Math.Max(0, value.GetBuyValue());
            }
            catch (Exception ex) { DebugLog.Warning("Deck image pricing unavailable for " + card.Id + ": " + ex.Message); }
        }
    }
}
