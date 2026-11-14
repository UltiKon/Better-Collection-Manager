using System.Linq;
using System.Collections.Generic;
using BetterCollectionManager;
using UnityEngine;

public sealed class ArcaneDustAmount : Component
{
    public GameObject m_dustJar;
    public UberText m_dustCount;
}
public enum CurrencyType { DUST }
public sealed class CurrencyFrame : Component
{
    public CurrencyType CurrentCurrencyType;
    public GameObject CurrencyIconContainer;
}

public class HeroPickerButton : Component
{
    public GameObject m_heroClassIcon;
    public UberText m_classLabel;
    public TAG_CLASS m_heroClass;
    public List<Material> CLASS_MATERIALS = new();
    protected Material GetClassIconMaterial(TAG_CLASS deckClass) => CLASS_MATERIALS[deckClass == TAG_CLASS.MAGE ? 7 : 3];
}
public sealed class DeckTrayDeckListContent : Component { public string m_deckInfoActorPrefab; }
public sealed class MatchingPopupDisplay : Component
{
    public GameObject m_wildVines;
    public UberText m_title;
}
public sealed class CollectionDeckInfo : Component
{
    public GameObject m_root, m_visualRoot, m_heroPowerParent;
    public PegUIElement m_offClicker;
    public List<DeckInfoManaBar> m_manaBars;
}
public sealed class DeckInfoManaBar { public int m_manaCostID; public GameObject m_barFill; public UberText m_costText; }
public sealed class ManaFilterTab : Component { public UberText m_costText; public ManaCrystal m_crystal; }
public sealed class ManaCrystal : Component
{
    public enum State { READY, USED }
    public GameObject gem;
    public State state;
}

internal static partial class Program
{
    private static void CheckDeckImageNativeArt()
    {
        var stage = new GameObject("Deck image native art fixture");
        var source = new GameObject("Native collection fixture");
        var camera = new Camera();
        var mesh = new Mesh { name = "Native icon geometry", bounds = new Bounds(Vector3.zero, new Vector3(1, .1f, 2)) };
        var material = new Material { name = "Native icon shader" };
        GameObject Geometry(string name)
        {
            var obj = new GameObject(name); obj.transform.SetParent(source.transform, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            return obj;
        }
        var amount = source.AddComponent<ArcaneDustAmount>();
        amount.m_dustJar = Geometry("Native dust jar");
        amount.m_dustCount = new GameObject("Dust count").AddComponent<UberText>();
        amount.m_dustCount.transform.SetParent(amount.m_dustJar.transform, false);
        var actor = source.AddComponent<Actor>();
        actor.m_rarityGemMesh = Geometry("Native rarity gem");
        actor.Def = new EntityDef { Rarity = TAG_RARITY.RARE };
        actor.NameText = new GameObject("Card name text").AddComponent<UberText>();
        actor.NameText.transform.SetParent(actor.m_rarityGemMesh.transform, false);
        var frame = source.AddComponent<CurrencyFrame>(); frame.CurrentCurrencyType = CurrencyType.DUST;
        frame.CurrencyIconContainer = Geometry("Active native currency");
        var hidden = Geometry("Inactive native currency"); hidden.transform.SetParent(frame.CurrencyIconContainer.transform, false); hidden.SetActive(false);
        var disabled = Geometry("Disabled native currency"); disabled.transform.SetParent(frame.CurrencyIconContainer.transform, false); disabled.GetComponent<MeshRenderer>().enabled = false;
        var hero = source.AddComponent<HeroPickerButton>(); hero.m_heroClass = TAG_CLASS.MAGE;
        hero.m_heroClassIcon = Geometry("Class icon"); hero.m_classLabel = actor.NameText;
        // The real prefab can attach graphics underneath a text object. The
        // old copy rule wrongly classified all such descendants as glyphs.
        hero.m_heroClassIcon.transform.SetParent(hero.m_classLabel.transform, false);
        hero.CLASS_MATERIALS = Enumerable.Range(0, 12).Select(i => new Material { name = "Class " + i, color = new Color(1, 1, 1, 0) }).ToList();
        var classTexture = new Texture { width = 512, height = 256 };
        var classMesh = new Mesh { name = "Class atlas quad" };
        classMesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) };
        classMesh.SetTriangles(new List<int> { 0, 1, 2, 2, 1, 3 }, 0, false);
        classMesh.SetUVs(0, new List<Vector4> {
            new Vector4(.2f, .1f, 0, 0), new Vector4(.8f, .1f, 0, 0),
            new Vector4(.2f, .9f, 0, 0), new Vector4(.8f, .9f, 0, 0) });
        hero.m_heroClassIcon.GetComponent<MeshFilter>().sharedMesh = classMesh;
        hero.CLASS_MATERIALS[7].mainTexture = classTexture;
        hero.CLASS_MATERIALS[7].mainTextureScale = new Vector2(.25f, .5f);
        hero.CLASS_MATERIALS[7].mainTextureOffset = new Vector2(.5f, .25f);
        hero.CLASS_MATERIALS[3].mainTexture = classTexture;
        hero.CLASS_MATERIALS[3].mainTextureScale = new Vector2(.25f, .5f);
        hero.CLASS_MATERIALS[3].mainTextureOffset = new Vector2(.25f, 0);
        var fade = new MaterialPropertyBlock(); fade.SetColor("_Color", new Color(1, 1, 1, 0));
        hero.m_heroClassIcon.GetComponent<Renderer>().SetPropertyBlock(fade);
        actor.NameText.transform.SetParent(source.transform, false);
        var quantity = source.AddComponent<CollectionCardCount>(); quantity.m_normalTab = Geometry("Quantity tab");
        quantity.m_normalBorder = Geometry("Quantity border"); quantity.m_normalWideBorder = Geometry("Quantity wide border");
        quantity.m_normalBorder.transform.SetParent(quantity.m_normalTab.transform, false);
        quantity.m_normalWideBorder.transform.SetParent(quantity.m_normalTab.transform, false);
        quantity.m_normalCountText = new GameObject("Original quantity").AddComponent<UberText>();
        quantity.m_normalCountText.transform.SetParent(quantity.m_normalTab.transform, false);
        quantity.m_normalBorder.transform.SetParent(quantity.m_normalCountText.transform, false);
        quantity.m_normalWideBorder.transform.SetParent(quantity.m_normalCountText.transform, false);
        quantity.m_wildBorderMaterial.color = new Color(.8f, .7f, .5f, .05f);
        var info = source.AddComponent<CollectionDeckInfo>();
        info.m_visualRoot = new GameObject("Mana curve fixture");
        info.m_visualRoot.transform.SetParent(source.transform, false);
        var nativeTrack = Geometry("Native recessed mana track");
        nativeTrack.transform.SetParent(info.m_visualRoot.transform, false);
        info.m_heroPowerParent = Geometry("Unrelated hero power");
        info.m_heroPowerParent.transform.SetParent(info.m_visualRoot.transform, false);
        info.m_manaBars = new List<DeckInfoManaBar>();
        for (int cost = 0; cost < 3; cost++)
        {
            GameObject fill = Geometry("Native mana fill");
            fill.transform.SetParent(info.m_visualRoot.transform, false);
            fill.transform.localPosition = new Vector3(cost * 2, 0, 0);
            info.m_manaBars.Add(new DeckInfoManaBar { m_manaCostID = cost, m_barFill = fill, m_costText = actor.NameText });
        }
        material.SetFloat("_Percent", .7f);
        const string menuAsset = "ButtonListMenuDef.prefab:1ab57b5c429373a4b8b4e0c0c706ca3e";
        var menuTemplate = source.AddComponent<ButtonListMenuDef>();
        menuTemplate.m_headerText = actor.NameText;
        var nativeMenuTile = Geometry("Native menu center");
        menuTemplate.m_background.m_middle = new MultiSliceElement.Slice { m_slice = nativeMenuTile };
        AssetLoader.Get().Assets.Add(menuAsset, source);
        const string matchingAsset = "MatchingPopup3D.prefab:4f4a40d14d907e94da1b81d97c18a44f";
        var matching = source.AddComponent<MatchingPopupDisplay>(); matching.m_title = actor.NameText;
        var wildTile = Geometry("Native matchmaking Wild vines");
        var wildMaterial = new Material { name = "Native Wild vines", color = new Color(.4f, .6f, .2f, 0) };
        var wildMesh = new Mesh { name = "Native vine geometry", subMeshCount = 1,
            vertices = new[] { new Vector3(-2, 0, -1), new Vector3(2, 0, -1), new Vector3(0, 0, 1) } };
        wildMesh.SetTriangles(new List<int> { 0, 1, 2 }, 0, false);
        wildMesh.RecalculateBounds();
        wildMesh.SetUVs(0, Enumerable.Range(0, 3).Select(i => new Vector4(i / 3f, .5f, 0, 0)).ToList());
        wildTile.GetComponent<MeshFilter>().sharedMesh = wildMesh;
        wildTile.GetComponent<MeshRenderer>().sharedMaterials = new[] { wildMaterial };
        wildTile.SetActive(false);
        matching.m_wildVines = wildTile;
        var matchingPanel = Geometry("Original matchmaking panel");
        matchingPanel.GetComponent<MeshFilter>().sharedMesh = new Mesh { name = "Matching panel geometry",
            bounds = new Bounds(Vector3.zero, new Vector3(10, .1f, 6)) };
        var matchingShadow = Geometry("Original matchmaking shadow");
        matchingShadow.GetComponent<MeshFilter>().sharedMesh = new Mesh { name = "Matching shadow geometry",
            bounds = new Bounds(Vector3.zero, new Vector3(100, .1f, 100)) };
        AssetLoader.Get().Assets.Add(matchingAsset, source);
        UnityEngine.Object[] previous = Resources.Objects;
        Resources.Objects = new UnityEngine.Object[] { amount };
        try
        {
            Material owned;
            using (var art = new DeckImageNativeArt(stage.transform))
            {
                GameObject dust = art.DustIcon(camera);
                var output = dust.GetComponentsInChildren<MeshRenderer>(true).Single(); owned = output.sharedMaterial;
                Check(output.GetComponent<MeshFilter>().sharedMesh == mesh && owned != material && owned.name == material.name,
                    "Dust icons reuse native geometry and a private copy of the installed game's material.");
                Check(dust.GetComponentsInChildren<ArcaneDustAmount>(true).Length == 0 && dust.GetComponentsInChildren<UberText>(true).Length == 0,
                    "Copying a native dust icon never instantiates currency controllers or count text.");
                art.RememberRarity(actor, camera);
                GameObject first = art.RarityGem(TAG_RARITY.RARE), second = art.RarityGem(TAG_RARITY.RARE);
                Check(first != second && first.GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh == mesh
                    && second.GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh == mesh,
                    "Each rarity icon has independent transforms while retaining the native gem mesh.");
                Check(first.GetComponentsInChildren<UberText>(true).Length == 0 && !first.activeSelf && !second.activeSelf,
                    "Rarity gems exclude card text and remain hidden outside their capture.");
                Resources.Objects = new UnityEngine.Object[] { hero, quantity, info };
                int instantiatedBeforeVines = AssetLoader.Get().InstantiateCalls;
                GameObject wildFrame = art.WildFrame(camera, out Vector3[] originalPanel);
                Check(wildFrame != null,
                    "Wild export loads the matching popup asset when neither a deck tile nor matchmaking popup is resident.");
                Check(originalPanel.Length == 8 && Near(originalPanel.Min(point => point.x), -5)
                    && Near(originalPanel.Max(point => point.x), 5) && Near(originalPanel.Min(point => point.z), -3)
                    && Near(originalPanel.Max(point => point.z), 3),
                    "Original vine attachment positions use the native panel geometry, excluding its larger shadow.");
                Mesh copiedWild = wildFrame.GetComponent<MeshFilter>().sharedMesh;
                Material copiedWildMaterial = wildFrame.GetComponent<MeshRenderer>().sharedMaterials.Single();
                Check(copiedWild == wildMesh && copiedWild.subMeshCount == 1
                    && copiedWild.GetTriangles(0).SequenceEqual(new[] { 0, 1, 2 }) && copiedWild.bounds.max.x == 2
                    && copiedWildMaterial != wildMaterial && copiedWildMaterial.name == wildMaterial.name
                    && copiedWildMaterial.color.a == 1 && copiedWild.Uvs[0].SequenceEqual(wildMesh.Uvs[0]),
                    "Wild decoration retains the matching popup's native vine geometry, UVs and a private opaque material.");
                Check(wildFrame.GetComponentsInChildren<MatchingPopupDisplay>(true).Length == 0
                    && wildFrame.GetComponentsInChildren<UberText>(true).Length == 0
                    && !wildTile.activeSelf && wildMaterial.color.a == 0 && wildMesh.subMeshCount == 1
                    && AssetLoader.Get().InstantiateCalls == instantiatedBeforeVines,
                    "Copying Wild vines never instantiates matchmaking or changes the source popup's graphics.");
                var classIcon = art.ClassIcon(TAG_CLASS.MAGE);
                Check(classIcon.Texture == classTexture && Near(classIcon.Scale.x, .15f) && Near(classIcon.Scale.y, .4f)
                    && Near(classIcon.Offset.x, .55f) && Near(classIcon.Offset.y, .3f),
                    "Class symbols read the requested class texture using both mesh UVs and class-specific atlas transforms.");
                var classProperties = new MaterialPropertyBlock();
                hero.m_heroClassIcon.GetComponent<Renderer>().GetPropertyBlock(classProperties);
                Check(Near(hero.CLASS_MATERIALS[7].color.a, 0) && Near(classProperties.Color.a, 0),
                    "Class symbol extraction ignores live UI fades without changing the source material or property block.");
                hero.CLASS_MATERIALS[7].mainTextureScale = new Vector2(-.25f, .5f);
                var flipped = art.ClassIcon(TAG_CLASS.MAGE);
                Check(Near(flipped.Scale.x, -.15f) && Near(flipped.Offset.x, .45f),
                    "Flipped native class atlas regions retain their signed texture scale.");
                hero.CLASS_MATERIALS[7].mainTextureScale = new Vector2(.25f, .5f);
                classMesh.isReadable = false;
                var paladin = art.ClassIcon(TAG_CLASS.PALADIN);
                Check(paladin.Texture == classTexture && Near(paladin.Offset.x, .3f) && Near(paladin.Offset.y, .05f),
                    "A non-readable native icon mesh uses GPU UV readback and selects the deck's class rather than the source button's class.");
                actor.m_ghostCardGameObject = new GameObject("Private sideboard style");
                actor.m_ghostCardGameObject.transform.SetParent(actor.transform, false);
                var ghost = actor.m_ghostCardGameObject.AddComponent<GhostCard>();
                var ghostCapture = actor.m_ghostCardGameObject.AddComponent<RenderToTexture>();
                ghost.m_R2T_EffectGhost = actor.m_ghostCardGameObject.AddComponent<RenderToTexture>();
                ghost.m_GlowPlane = Geometry("Rune red glow plane"); ghost.m_GlowPlaneElite = Geometry("Elite rune red glow plane");
                Vector3 cardPosition = actor.transform.position;
                DeckImageNativeArt.SideboardStyle(actor, TAG_PREMIUM.GOLDEN);
                Check(ghost.GhostType == GhostCard.Type.NOT_VALID && ghost.Premium == TAG_PREMIUM.GOLDEN
                    && ghost.Force && !ghost.UsingRenderTexture && ghost.MaterialsApplied == 1,
                    "Sideboards apply the native invalid-rune mesh materials directly instead of rendering a cached ghost card quad.");
                Check(!ghostCapture.enabled && !ghost.m_R2T_EffectGhost.enabled
                    && !ghost.m_GlowPlane.GetComponent<Renderer>().enabled && !ghost.m_GlowPlaneElite.GetComponent<Renderer>().enabled
                    && Near(actor.transform.position.x, cardPosition.x) && Near(actor.transform.position.y, cardPosition.y),
                    "The direct sideboard style keeps the actor registered and disables rectangular glow/capture planes.");
                GameObject shield = art.CountShield(true, false, camera, out Color labelColor);
                Check(shield.GetComponentsInChildren<MeshRenderer>(true).Length == 1
                    && shield.GetComponentsInChildren<UberText>(true).Length == 0
                    && Near(labelColor.r, quantity.m_wildTextColor.r),
                    "Quantity shields copy their actual border beneath text ancestors, excluding wide borders and source glyphs.");
                Check(Near(shield.GetComponentsInChildren<MeshRenderer>(true).Single().sharedMaterial.color.a, 1)
                    && Near(quantity.m_wildBorderMaterial.color.a, .05f),
                    "A faded native shield becomes fully opaque without changing the live collection's material.");
                GameObject wideShield = art.CountShield(false, true, camera, out _);
                Check(wideShield.GetComponentsInChildren<MeshRenderer>(true).Length == 1
                    && wideShield.GetComponentsInChildren<CollectionCardCount>(true).Length == 0,
                    "Wide quantities copy their own border without collection-count controllers.");
                GameObject bar = art.ManaBar(.25f, camera);
                Check(Near(bar.GetComponentsInChildren<MeshRenderer>(true).Single().sharedMaterial.GetFloat("_Percent"), .25f)
                    && Near(material.GetFloat("_Percent"), .7f),
                    "Compact mana bars use the native fill shader without modifying the live curve's material.");
                GameObject well = art.ManaWell(camera, out var measure);
                Check(well.GetComponentsInChildren<MeshRenderer>(true).Single().gameObject.name == nativeTrack.name
                    && Near(measure().width, 2) && measure().height > 2,
                    "The native well samples between adjacent fills and omits the fill meshes and hero power.");
                var tab = source.AddComponent<ManaFilterTab>();
                tab.m_costText = actor.NameText;
                tab.m_crystal = source.AddComponent<ManaCrystal>();
                tab.m_crystal.gem = Geometry("Mana filter crystal");
                Geometry("Proposed_Quad").transform.SetParent(tab.m_crystal.gem.transform, false);
                Resources.Objects = new UnityEngine.Object[] { tab };
                GameObject crystal = art.ManaCrystal(camera);
                Check(crystal.GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh == mesh
                    && crystal.GetComponentsInChildren<ManaCrystal>(true).Length == 0,
                    "Curve ticks use the native collection crystal without its effect quad or controller.");
                Resources.Objects = new UnityEngine.Object[] { frame };
                GameObject fallback = art.DustIcon(camera);
                Check(fallback.GetComponentsInChildren<MeshRenderer>(true).Length == 1,
                    "The currency-header fallback copies only its active, enabled icon and excludes inactive currency variants.");
                GameObject paper = art.Parchment(camera);
                Check(paper.GetComponentsInChildren<MeshRenderer>(true).Length > 0 && paper.GetComponentsInChildren<StoreSendToBAM>(true).Length == 0,
                    "Native parchment is copied as graphics without running the shop dialog.");
                var infoSurface = art.InfoSurface(camera);
                var surface = infoSurface.GetComponentsInChildren<MeshRenderer>(true).Single();
                Check(surface.GetComponent<MeshFilter>().sharedMesh == mesh && surface.sharedMaterial.name == material.name
                    && surface.sharedMaterial != material && infoSurface.GetComponentsInChildren<ButtonListMenuDef>(true).Length == 0,
                    "The info box samples the game's menu center tile without copying the menu's trim or controllers.");
                var quantityFrame = art.QuantityFrame(camera);
                var frameMaterials = quantityFrame.GetComponentsInChildren<MeshRenderer>(true)
                    .Select(renderer => renderer.sharedMaterial.name).ToArray();
                Check(frameMaterials.Contains("Outer frame") && frameMaterials.Contains("Separate metal rim")
                    && !frameMaterials.Contains("Quantity drop shadow")
                    && quantityFrame.GetComponentsInChildren<StoreQuantityPrompt>(true).Length == 0
                    && quantityFrame.GetComponentsInChildren<UberText>(true).Length == 0,
                    "The pack quantity frame retains its metal rim and excludes the larger shadow mesh and dialog controls.");
            }
            Check(owned.Destroyed && !material.Destroyed && !mesh.Destroyed,
                "Native artwork disposal releases private materials and preserves installed meshes and materials.");
            var bodies = new List<Actor>();
            for (int i = 0; i < 2; i++)
            {
                var root = new GameObject("Equal body " + i);
                root.transform.SetParent(stage.transform, false);
                var body = root.AddComponent<Actor>();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>();
                bodies.Add(body);
            }
            bodies[1].m_eliteObject = Geometry("Oversized legendary dragon");
            bodies[1].m_eliteObject.transform.SetParent(bodies[1].transform, false);
            bodies[1].m_eliteObject.transform.localScale = new Vector3(1.4f, 1, 1.3f);
            foreach (Actor body in bodies) ComboSceneGeometry.FitCardBody(body, camera, 100, 100, 10);
            Check(Near(bodies[0].transform.localScale.x, bodies[1].transform.localScale.x)
                && Near(ComboSceneGeometry.ProjectBounds(bodies[1].GetMeshRenderer().bounds, camera).width, 100),
                "Body fitting gives normal and legendary actors the same scale while leaving the dragon intact.");
        }
        finally
        {
            AssetLoader.Get().Assets.Remove(menuAsset);
            AssetLoader.Get().Assets.Remove(matchingAsset);
            Resources.Objects = previous;
            UnityEngine.Object.Destroy(stage); UnityEngine.Object.Destroy(source);
        }
    }
}
