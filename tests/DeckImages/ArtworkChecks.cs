using BetterCollectionManager;
using UnityEngine;

internal static class ArtworkChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (TAG_PREMIUM best in new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND })
        {
            string id = "HIGHEST_" + best;
            var highestDefinition = new CardDef();
            highestDefinition.Portraits[TAG_PREMIUM.NORMAL] = new Texture();
            if (best != TAG_PREMIUM.NORMAL)
                highestDefinition.Materials[TAG_PREMIUM.GOLDEN] = new Material { mainTexture = new Texture() };
            if (best == TAG_PREMIUM.SIGNATURE || best == TAG_PREMIUM.DIAMOND)
                highestDefinition.Portraits[TAG_PREMIUM.SIGNATURE] = new Texture();
            if (best == TAG_PREMIUM.DIAMOND) highestDefinition.Portraits[TAG_PREMIUM.DIAMOND] = new Texture();
            DefLoader.Instance.Definitions[id] = highestDefinition;
            using var highest = new DeckImageCardArt(new DeckImageSnapshot.Card
            {
                Id = id, Definition = new EntityDef { Id = id }, HighestQuality = true, Premium = TAG_PREMIUM.NORMAL
            });
            check(highest.Prepare() && highest.Premium == best,
                "Highest selects each card's actual Diamond, Signature, Golden or Normal artwork, including material-only Golden.");
        }
        var card = new DeckImageSnapshot.Card { Id = "ASTRAL", Definition = new EntityDef { Id = "ASTRAL" }, Premium = TAG_PREMIUM.NORMAL };
        var art = new DeckImageCardArt(card);
        check(!art.Prepare(), "Missing CardDefs cannot pass the artwork readiness gate.");
        var definition = new CardDef();
        definition.Portraits[TAG_PREMIUM.NORMAL] = null;
        DefLoader.Instance.Definitions[card.Id] = definition;
        check(!art.Prepare(), "A CardDef with an unloaded portrait cannot pass the readiness gate.");
        var correct = new Texture();
        definition.Requests.Single().Asset = correct;
        definition.Portraits[TAG_PREMIUM.NORMAL] = correct;
        check(art.Prepare() && art.Premium == TAG_PREMIUM.NORMAL, "Ready artwork preserves an available requested quality.");
        var prefabMaterial = new Material { mainTexture = new Texture() };
        var actor = new Actor { Definition = definition, Entity = card.Definition, Material = prefabMaterial, PortraitTexture = correct };
        check(art.Bind(actor) && actor.GetPortraitTexture() == correct && actor.Material != prefabMaterial,
            "A loaded handle alongside wrong prefab artwork is repaired on a private portrait material.");
        check(prefabMaterial.mainTexture != correct, "Repairing export artwork never changes the prefab's shared material.");
        Material first = actor.Material;
        actor.Material = new Material { mainTexture = new Texture() };
        check(art.Bind(actor) && actor.GetPortraitTexture() == correct, "A native material reset before capture is rebound to the requested card.");
        check(first.Destroyed, "A changed native shader releases the previous private material.");
        actor.Material = prefabMaterial; actor.RefuseMaterial = true;
        check(!art.Bind(actor), "A correct texture handle cannot pass when the actual rendered material still has wrong artwork.");
        actor.RefuseMaterial = false;
        actor.Entity = new EntityDef { Id = "OTHER" };
        bool rejected = false;
        try { art.Bind(actor); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Mismatched card identity is rejected before rendering.");
        actor.Entity = card.Definition;
        actor.Definition = new CardDef(); rejected = false;
        try { art.Bind(actor); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "A stale native CardDef is rejected even if the EntityDef matches.");
        var held = art.Definition;
        art.Dispose();
        check(held.Disposed && definition.Requests.Single().Disposed, "Artwork handles are released after rendering or cancellation.");
        check(!prefabMaterial.Destroyed, "Disposal preserves the original native material.");

        card.Premium = TAG_PREMIUM.GOLDEN;
        definition.Portraits[TAG_PREMIUM.GOLDEN] = new Texture();
        definition.Materials[TAG_PREMIUM.GOLDEN] = null;
        using (var golden = new DeckImageCardArt(card))
        {
            check(!golden.Prepare(), "Golden cards wait for their full native material, not just a texture atlas.");
            var layered = new Material { mainTexture = new Texture(), LayerTexture = new Texture(), AnimationTime = 2.5f };
            definition.MaterialRequests.Single().Asset = layered;
            check(golden.Prepare(), "A loaded native layered material completes golden artwork preparation.");
            actor = new Actor { Definition = definition, Entity = card.Definition, Material = layered, PortraitTexture = definition.Portraits[TAG_PREMIUM.GOLDEN] };
            check(golden.Bind(actor) && actor.Material == layered && actor.GetPortraitTexture() != actor.PortraitTexture,
                "Golden layered artwork keeps its native main texture even when it differs from the flat portrait atlas.");
            check(actor.Material.LayerTexture != null && actor.Material.AnimationTime == 2.5f,
                "Golden artwork retains additional native texture layers and animation parameters.");
        }
        check(definition.MaterialRequests.Single().Disposed, "Golden native material handles are released after export.");
        definition.Portraits.Remove(TAG_PREMIUM.GOLDEN);
        using (var materialOnly = new DeckImageCardArt(card))
        {
            check(!materialOnly.Prepare(), "Material-only golden cards wait for their native material without reverting to Normal.");
            var material = new Material { mainTexture = new Texture(), LayerTexture = new Texture() };
            definition.MaterialRequests.Last().Asset = material;
            check(materialOnly.Prepare() && materialOnly.Premium == TAG_PREMIUM.GOLDEN, "A golden material is sufficient when there is no separate portrait texture path.");
            actor = new Actor { Definition = definition, Entity = card.Definition, Material = prefabMaterial };
            check(materialOnly.Bind(actor) && actor.Material == material, "Material-only golden cards render their complete premium artwork.");
        }
        definition.Portraits[TAG_PREMIUM.GOLDEN] = new Texture();
        definition.Materials.Clear();
        using (var nativeFallback = new DeckImageCardArt(card))
        {
            check(nativeFallback.Prepare(), "Golden cards without a separate premium material retain the native fallback path.");
            var nativeNormal = new Material { mainTexture = definition.Portraits[TAG_PREMIUM.NORMAL] };
            actor = new Actor { Definition = definition, Entity = card.Definition, Material = nativeNormal, PortraitTexture = nativeNormal.mainTexture };
            check(nativeFallback.Bind(actor) && actor.Material == nativeNormal && actor.Material.mainTexture == definition.Portraits[TAG_PREMIUM.NORMAL],
                "An animated atlas is never forced into the native single-texture fallback shader.");
        }

        foreach (TAG_PREMIUM premium in new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE })
        {
            card.Premium = premium;
            using var fallback = new DeckImageCardArt(card);
            check(fallback.Prepare() && fallback.Premium == TAG_PREMIUM.NORMAL,
                "Unavailable premium variants use the normal native artwork.");
        }
        card.Premium = TAG_PREMIUM.DIAMOND;
        definition.Portraits[TAG_PREMIUM.DIAMOND] = new Texture();
        using var diamond = new DeckImageCardArt(card);
        check(diamond.Prepare() && diamond.Premium == TAG_PREMIUM.DIAMOND, "An available Diamond variant is retained.");
        var diamondMaterial = new Material { mainTexture = new Texture() };
        actor = new Actor { Definition = definition, Entity = card.Definition, Material = diamondMaterial, PortraitTexture = definition.Portraits[TAG_PREMIUM.DIAMOND] };
        check(diamond.Bind(actor) && actor.Material == diamondMaterial, "Native Diamond render-to-texture output is preserved.");
    }
}
