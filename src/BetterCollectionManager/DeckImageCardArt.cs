using System;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class DeckImageCardArt : IDisposable
    {
        private readonly DeckImageSnapshot.Card m_card;
        private DefLoader.DisposableCardDef m_definition;
        private AssetHandle<Texture> m_portrait;
        private AssetHandle<Material> m_premiumMaterial;
        private Material m_material;
        private bool m_ready;
        internal Texture Portrait => m_portrait?.Asset;
        internal TAG_PREMIUM Premium { get; private set; }
        internal DefLoader.DisposableCardDef Definition => m_definition;
        internal DeckImageCardArt(DeckImageSnapshot.Card card) { m_card = card; }

        internal bool Prepare()
        {
            if (m_definition == null) m_definition = DefLoader.Get()?.GetCardDef(m_card.Id);
            CardDef definition = m_definition?.CardDef;
            if (definition == null) return false;
            TAG_PREMIUM requested = m_card.Premium;
            if (m_card.HighestQuality)
                foreach (TAG_PREMIUM candidate in new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL })
                {
                    string path = definition.GetPortraitTexturePath(candidate, false, false, out TAG_PREMIUM used);
                    if ((!string.IsNullOrEmpty(path) && used == candidate) || !string.IsNullOrEmpty(definition.GetPortraitMaterialPath(candidate)))
                    { requested = candidate; break; }
                }
            string texturePath = definition.GetPortraitTexturePath(requested, false, false, out _);
            string materialPath = definition.GetPortraitMaterialPath(requested);
            // Many golden cards have their entire portrait in the material and
            // no standalone golden texture. The native client treats those as
            // Golden too; absence of a texture path is not a Normal fallback.
            Premium = !string.IsNullOrEmpty(texturePath) || !string.IsNullOrEmpty(materialPath)
                ? requested : TAG_PREMIUM.NORMAL;
            if ((Premium == TAG_PREMIUM.GOLDEN || Premium == TAG_PREMIUM.SIGNATURE)
                && !string.IsNullOrEmpty(definition.GetPortraitMaterialPath(Premium)))
            {
                if (m_premiumMaterial == null) m_premiumMaterial = definition.GetPortraitMaterial(Premium);
                if (m_premiumMaterial?.Asset == null) return false;
                if (m_premiumMaterial.Asset.mainTexture != null) { m_ready = true; return true; }
            }
            if (m_portrait == null) m_portrait = definition.GetPortraitTexture(Premium, true, false);
            m_ready = m_portrait?.Asset != null;
            return m_ready;
        }

        internal bool Bind(Actor actor)
        {
            if (!actor.HasCardDef || !m_ready) return false;
            using (DefLoader.DisposableCardDef definition = actor.ShareDisposableCardDef())
                if (definition?.CardDef != m_definition?.CardDef || actor.GetEntityDef()?.GetCardId() != m_card.Id)
                    throw new InvalidOperationException("Card artwork identity changed: " + m_card.Id);
            Material native = actor.GetPortraitMaterial();
            if (native == null) return false;
            // Golden/signature shaders can combine several different textures.
            // Their main texture is not necessarily CardDef's flat portrait.
            // Keep the complete native material and its animation state.
            Material premium = m_premiumMaterial?.Asset;
            if (premium != null)
            {
                if (premium.mainTexture == null) return false;
                if (native.shader != premium.shader || native.mainTexture != premium.mainTexture)
                    actor.SetPortraitMaterial(premium);
                Material actual = actor.GetPortraitMaterial();
                return actual != null && actual.shader == premium.shader && actual.mainTexture == premium.mainTexture;
            }
            if (Premium == TAG_PREMIUM.NORMAL)
            {
                if (m_material == null || m_material.shader != native.shader)
                {
                    if (m_material != null) UnityEngine.Object.Destroy(m_material);
                    m_material = new Material(native);
                }
                m_material.mainTexture = m_portrait.Asset;
                actor.SetPortraitMaterial(m_material);
                return actor.GetPortraitTexture() == m_portrait.Asset;
            }
            // Diamond owns a native render texture; other premium variants can
            // temporarily use the actor's native normal-art fallback. Never put
            // an animation atlas into that fallback's single-texture shader.
            return actor.PortraitTexture != null && native.mainTexture != null;
        }

        public void Dispose()
        {
            if (m_material != null) UnityEngine.Object.Destroy(m_material);
            m_material = null;
            m_portrait?.Dispose(); m_portrait = null;
            m_premiumMaterial?.Dispose(); m_premiumMaterial = null;
            m_definition?.Dispose(); m_definition = null;
            m_ready = false;
        }
    }
}
