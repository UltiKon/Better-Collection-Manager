using System.Collections.Generic;

namespace BetterCollectionManager
{
    /// <summary>
    /// Picks the preferred owned premium for a CardId using <see cref="PreferredPremiumPrefs"/> order.
    /// Does NOT use CollectionManager.GetBestCardPremium (that is Diamond-first).
    /// </summary>
    internal static class PreferredPremiumHelper
    {
        public static bool MoveToPriority(TAG_PREMIUM[] order, int rank, TAG_PREMIUM premium)
        {
            if (order == null || rank < 0 || rank >= order.Length) return false;
            int from = System.Array.IndexOf(order, premium);
            if (from < 0 || from == rank) return false;
            int step = from < rank ? 1 : -1;
            for (int i = from; i != rank; i += step) order[i] = order[i + step];
            order[rank] = premium;
            return true;
        }

        public static CollectibleCard ChoosePreferred(IList<CollectibleCard> variants)
        {
            if (variants == null || variants.Count == 0)
                return null;
            if (variants.Count == 1)
                return variants[0];

            PreferredPremiumPrefs.EnsureLoaded();
            IReadOnlyList<TAG_PREMIUM> order = PreferredPremiumPrefs.Order;
            CollectionManager cm = CollectionManager.Get();
            string cardId = variants[0].CardId;

            if (cm != null)
            {
                for (int i = 0; i < order.Count; i++)
                {
                    TAG_PREMIUM premium = order[i];
                    CollectibleCard match = FindByPremium(variants, premium);
                    if (match == null)
                        continue;

                    int owned = cm.GetOwnedCount(cardId, premium);
                    if (owned > 0)
                        return match;
                }
            }
            else
            {
                for (int i = 0; i < order.Count; i++)
                {
                    CollectibleCard match = FindByPremium(variants, order[i]);
                    if (match != null && match.OwnedCount > 0)
                        return match;
                }
            }

            // Unowned fallback: keep NORMAL if present, else first available in the set.
            CollectibleCard normal = FindByPremium(variants, TAG_PREMIUM.NORMAL);
            return normal ?? variants[0];
        }

        /// <summary>
        /// For non-Legendary cards: if the preferred premium has only 1 owned copy, pick the next
        /// lower preferred premium that has owned copies so the UI can show 2 deck-capable tiles.
        /// Legendary always returns null (single-copy collapse).
        /// </summary>
        public static CollectibleCard ChooseSecondCopyForDeck(
            IList<CollectibleCard> variants,
            CollectibleCard preferred)
        {
            if (variants == null || preferred == null || IsLegendary(preferred))
                return null;

            PreferredPremiumPrefs.EnsureLoaded();
            IReadOnlyList<TAG_PREMIUM> order = PreferredPremiumPrefs.Order;
            CollectionManager cm = CollectionManager.Get();
            string cardId = preferred.CardId;
            if (string.IsNullOrEmpty(cardId))
                return null;

            int preferredOwned = GetOwnedCount(cm, preferred, cardId);
            if (preferredOwned >= 2)
                return null; // already have 2 at the top preferred quality
            if (preferredOwned < 1)
                return null;

            int start = -1;
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == preferred.PremiumType)
                {
                    start = i + 1;
                    break;
                }
            }
            if (start < 0)
                start = 0;

            for (int i = start; i < order.Count; i++)
            {
                TAG_PREMIUM premium = order[i];
                if (premium == preferred.PremiumType)
                    continue;
                CollectibleCard match = FindByPremium(variants, premium);
                if (match == null)
                    continue;
                int owned = GetOwnedCount(cm, match, cardId);
                if (owned > 0)
                    return match;
            }
            return null;
        }

        internal static bool IsLegendary(CollectibleCard card)
        {
            try
            {
                if (card == null)
                    return false;
                if (card.Rarity == TAG_RARITY.LEGENDARY)
                    return true;
            }
            catch
            {
                try
                {
                    EntityDef def = card.GetEntityDef();
                    if (def != null && (TAG_RARITY)def.GetTag(GAME_TAG.RARITY) == TAG_RARITY.LEGENDARY)
                        return true;
                }
                catch { /* ignore */ }
            }
            return false;
        }

        internal static int GetOwnedCount(CollectionManager cm, CollectibleCard card, string cardId)
        {
            if (cm != null)
            {
                try { return cm.GetOwnedCount(cardId, card.PremiumType); }
                catch { /* fall through */ }
            }
            try { return card.OwnedCount; }
            catch { return 0; }
        }

        private static CollectibleCard FindByPremium(IList<CollectibleCard> variants, TAG_PREMIUM premium)
        {
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i] != null && variants[i].PremiumType == premium)
                    return variants[i];
            }
            return null;
        }

        public static string PremiumName(TAG_PREMIUM premium)
        {
            switch (premium)
            {
                case TAG_PREMIUM.SIGNATURE: return "Signature";
                case TAG_PREMIUM.DIAMOND: return "Diamond";
                case TAG_PREMIUM.GOLDEN: return "Golden";
                case TAG_PREMIUM.NORMAL: return "Normal";
                default: return premium.ToString();
            }
        }
    }
}
