namespace BetterCollectionManager
{
    /// <summary>Two real inventory variants behind one collection tile.</summary>
    internal sealed class MixedPremiumPair
    {
        public CollectibleCard Normal { get; }
        public CollectibleCard Golden { get; }
        public CollectibleCard Preferred { get; private set; }
        public CollectibleCard Secondary { get; private set; }
        public bool IsStacking { get; private set; }

        private MixedPremiumPair(CollectibleCard normal, CollectibleCard golden)
        {
            Normal = normal;
            Golden = golden;
        }

        public static MixedPremiumPair Stack(CollectibleCard preferred, CollectibleCard second)
        {
            if (preferred == null || second == null || preferred.CardId != second.CardId
                || preferred.PremiumType == second.PremiumType) return null;
            return new MixedPremiumPair(null, null)
                { Preferred = preferred, Secondary = second, IsStacking = true };
        }

        public static MixedPremiumPair Find(CollectibleCard preferred, CollectibleCard second)
        {
            if (preferred == null || second == null || preferred.CardId != second.CardId
                || (preferred.PremiumType != TAG_PREMIUM.NORMAL && preferred.PremiumType != TAG_PREMIUM.GOLDEN)
                || (second.PremiumType != TAG_PREMIUM.NORMAL && second.PremiumType != TAG_PREMIUM.GOLDEN)
                || preferred.PremiumType == second.PremiumType
                || (preferred.Rarity != TAG_RARITY.COMMON && preferred.Rarity != TAG_RARITY.RARE
                    && preferred.Rarity != TAG_RARITY.EPIC))
                return null;

            // The caller supplies the two owned variants selected by the
            // existing deck-copy rule, including the user's premium order.
            return preferred.PremiumType == TAG_PREMIUM.NORMAL
                ? new MixedPremiumPair(preferred, second)
                : new MixedPremiumPair(second, preferred);
        }

        // Fallback seam through the name ribbon. Its separate Golden mesh
        // remains whole, while the surrounding frame is split horizontally.
        // Measure from the bottom of the card, independently of width/scale.
        internal const float NameBannerFraction = 0.45f;

        public static bool IsGoldenHalf(float up, float centerUp, float height, float upDirection = 1f,
            float splitFraction = NameBannerFraction)
        {
            return SignedDistance(up, centerUp, height, upDirection, splitFraction) >= 0f;
        }

        public static float SignedDistance(float up, float centerUp, float height, float upDirection = 1f,
            float splitFraction = NameBannerFraction)
        {
            return (up - centerUp) * upDirection - (splitFraction - 0.5f) * height;
        }
    }
}
