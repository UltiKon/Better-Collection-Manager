using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetterCollectionManager
{
    internal sealed class DeckImageStatistics
    {
        internal readonly int[] Mana = new int[11];
        internal readonly Dictionary<TAG_CARDTYPE, int> Types = new Dictionary<TAG_CARDTYPE, int>();
        internal readonly Dictionary<TAG_RARITY, int> Rarities = new Dictionary<TAG_RARITY, int>();
        internal int MainCopies, SideboardCopies, UncraftableCopies, UnknownPriceCopies;
        internal long CraftingDust, TotalMana;
        internal string AverageMana => MainCopies == 0 ? "0" : ((double)TotalMana / MainCopies).ToString("0.0", CultureInfo.InvariantCulture);
        internal static int[] NativeManaBins(int[] mana)
        {
            var bins = new int[8];
            for (int i = 0; i < mana.Length; i++) bins[Math.Min(7, i)] += mana[i];
            return bins;
        }
        internal DeckImageStatistics(List<DeckImageSnapshot.Section> sections)
        {
            for (int i = 0; i < sections.Count; i++)
                foreach (var card in sections[i].Cards)
                {
                    if (i == 0)
                    {
                        MainCopies += card.Count;
                        int mana = Math.Max(0, card.Definition.GetTag(GAME_TAG.COST));
                        Mana[Math.Min(10, mana)] += card.Count;
                        TotalMana += (long)mana * card.Count;
                        var type = (TAG_CARDTYPE)card.Definition.GetTag(GAME_TAG.CARDTYPE);
                        var rarity = (TAG_RARITY)card.Definition.GetTag(GAME_TAG.RARITY);
                        Types.TryGetValue(type, out int types); Types[type] = types + card.Count;
                        Rarities.TryGetValue(rarity, out int rarities); Rarities[rarity] = rarities + card.Count;
                    }
                    else SideboardCopies += card.Count;
                    if (card.Uncraftable) UncraftableCopies += card.Count;
                    else if (card.NormalCraftCost.HasValue)
                    {
                        CraftingDust += (long)card.NormalCraftCost.Value * card.Count;
                    }
                    else UnknownPriceCopies += card.Count;
                }
        }
        internal static string TypeName(TAG_CARDTYPE type) => type switch {
            TAG_CARDTYPE.MINION => "Minions", TAG_CARDTYPE.SPELL => "Spells", TAG_CARDTYPE.WEAPON => "Weapons",
            TAG_CARDTYPE.HERO => "Heroes", TAG_CARDTYPE.LOCATION => "Locations", _ => type.ToString()
        };
        internal static string RarityName(TAG_RARITY rarity) => rarity switch {
            TAG_RARITY.COMMON => "Common", TAG_RARITY.RARE => "Rare", TAG_RARITY.EPIC => "Epic",
            TAG_RARITY.LEGENDARY => "Legendary", _ => "Free / Other"
        };
    }
}
