using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

// Same generic loading/callback boundary used by the installed page manager.
public sealed class CollectionManagerDisplay : CollectibleDisplay
{
    public List<CollectionCardActors> Loaded;
    public int FilterResets;
    public void ResetFilters(bool updateVisuals) { FilterResets++; }
    public void CollectionPageContentsChanged<T>(ICollection<T> cards, CollectionActorsReadyCallback callback, object data) where T : ICollectible
    {
        MethodInfo method = typeof(CollectionManagerDisplay).GetMethod(nameof(CollectionPageContentsChanged)).MakeGenericMethod(typeof(T));
        NativeComboRuntime.Invoke(method, this, new object[] { cards, callback, data }, arguments => {
            Loaded = new List<CollectionCardActors>();
            foreach (object item in cards)
                if (item is CollectibleCard card)
                {
                    Actor actor = AssetLoader.Get().InstantiatePrefab(card.PremiumType.ToString(), Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
                    actor.SetPremium(card.PremiumType); actor.SetEntityDef(card.GetEntityDef()); actor.UpdateAllComponents();
                    Loaded.Add(new CollectionCardActors(actor));
                }
            ((CollectionActorsReadyCallback)arguments[1])?.Invoke(Loaded, new List<ICollectible>(), arguments[2]);
        });
    }
}

internal static partial class Program
{
    private static void CheckPackageArtworkFailure(CollectibleCard proxy, CollectibleCard ordinary, CategoryCatalog.Combo package)
    {
        var originalCards = package.Cards;
        float originalTime = Time.unscaledTime;
        var page = new GameObject("PackageWithMissingFirstMember").AddComponent<CollectionCardVisual>();
        var second = new GameObject("SecondPackageWhilePortraitMissing").AddComponent<CollectionCardVisual>();
        var display = new CollectionManagerDisplay();
        try
        {
            package.Cards = new[] { new CategoryCatalog.CardRequirement("A_UNAVAILABLE_MEMBER", 1, Array.Empty<string>()) }
                .Concat(originalCards).ToArray();
            PackageNativeTile.ReleaseAssets(); CardDef.FailingId = package.Artwork;
            int reads = CollectionManager.Get().GetAllCardsCalls;
            bool called = false;
            display.CollectionPageContentsChanged<ICollectible>(new List<ICollectible> { proxy, proxy }, (actors, other, data) => {
                called = true;
                Check(CollectionManager.Get().GetAllCardsCalls == reads,
                    "Preparing visible package actors does not scan all inventory members for premium, mana or runes");
                new CollectibleRenderFixture().Render(proxy, page, actors[0]);
                new CollectibleRenderFixture().Render(proxy, second, actors[1]);
            }, null);
            Check(called && page.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.name == "BCM_PackageRecipe")
                && second.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.name == "BCM_PackageRecipe"),
                "A missing first member and failed portrait retain both package shells without a global five-second delay");
            Check(page.GetActor().GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled),
                "The underlying carrier card stays hidden while the package portrait is unavailable");
            CardDef.FailingId = null; Time.unscaledTime += .3f; ComboCollectionCards.OnLateUpdate();
            Check(page.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.name == "BCM_PackagePortrait")
                .All(r => r.sharedMaterial.mainTexture?.name == "Portrait:" + package.Artwork),
                "The existing recipe shell retries its portrait in place when the asset becomes available");
        }
        finally
        {
            CardDef.FailingId = null; package.Cards = originalCards; Time.unscaledTime = originalTime;
            ComboCollectionCards.ReleaseHiddenActors(page); ComboCollectionCards.ReleaseHiddenActors(second);
            if (display.Loaded != null) foreach (var actors in display.Loaded) actors.GetPreferredActor()?.Destroy();
            UnityEngine.Object.Destroy(page.gameObject); UnityEngine.Object.Destroy(second.gameObject);
        }
    }

    private static void CheckPackagePageLoading(CollectibleCard proxy, CollectibleCard ordinary, CategoryCatalog.Combo package)
    {
        var display = new CollectionManagerDisplay();
        var page = new GameObject("PreparedPackagePageSlot").AddComponent<CollectionCardVisual>();
        var nativeActors = new List<Actor>();
        var returnedPage = new GameObject("ReturningPackagePageSlot").AddComponent<CollectionCardVisual>();
        Actor prepared = null;
        bool callbackCalled = false;
        PackageNativeTile.ReleaseAssets();
        CardDef.PendingId = package.Artwork;
        try
        {
            Transform preparedTile = null;
            int routines = MelonLoader.MelonCoroutines.ActiveCount;
            display.CollectionPageContentsChanged<ICollectible>(new List<ICollectible> { proxy, ordinary }, (actors, other, data) => {
                callbackCalled = true;
                prepared = AssetLoader.Get().LastActor;
                preparedTile = prepared.transform.parent.GetComponentsInChildren<Transform>(true).Single(t => t.gameObject.name == "BCM_PackageRecipe");
                Check(preparedTile.GetComponentsInChildren<UberText>(true).Any(t => t.Text == package.Name),
                    "The package graphic and captions exist before native page-ready");
                int beforeBind = AssetLoader.Get().InstantiateCalls;
                new CollectibleRenderFixture().Render(proxy, page, actors[0]);
                Check(page.GetActor() == prepared && AssetLoader.Get().InstantiateCalls == beforeBind,
                    "The native callback binds the already prepared actor without another allocation");
            }, null);
            nativeActors.AddRange(display.Loaded.Select(models => models.GetPreferredActor()));
            Check(callbackCalled && MelonLoader.MelonCoroutines.ActiveCount == routines && CardDef.Pending.Count > 0,
                "A pending offscreen portrait/text cannot delay page-ready or create a navigation coroutine");
            Check(preparedTile.parent == page.transform
                && page.GetComponentsInChildren<Transform>(true).Count(t => t.gameObject.name == "BCM_PackageRecipe") == 1,
                "The same package is transferred into its slot before the native call returns");
            foreach (var handle in CardDef.Pending) handle.Asset = new Texture { name = "ReadyAfterNativeCallback" };
            ComboCollectionCards.OnLateUpdate();
            Check(preparedTile.GetComponentsInChildren<Renderer>(true).Where(r => r.gameObject.name == "BCM_PackagePortrait")
                .All(r => r.sharedMaterial.mainTexture?.name == "ReadyAfterNativeCallback"),
                "A later portrait completes on the existing tile without holding the page transition");
            bool ordinaryCalled = false;
            display.CollectionPageContentsChanged<ICollectible>(new List<ICollectible> { ordinary }, (a, b, c) => ordinaryCalled = true, null);
            nativeActors.AddRange(display.Loaded.Select(models => models.GetPreferredActor()));
            Check(ordinaryCalled && !prepared.Destroyed, "An ordinary page's callback remains immediate and preserves the bound package");
            page.Hide();
            int beforeReturn = AssetLoader.Get().InstantiateCalls;
            display.CollectionPageContentsChanged<ICollectible>(new List<ICollectible> { proxy, ordinary }, (actors, other, data) => {
                Check(AssetLoader.Get().InstantiateCalls == beforeReturn + 2 && !prepared.Destroyed,
                    "Returning to a hidden package only loads native page actors; its private actor, recipe and text are reused");
                new CollectibleRenderFixture().Render(proxy, returnedPage, actors[0]);
                Check(returnedPage.GetActor() == prepared && preparedTile.parent == returnedPage.transform,
                    "Page return transfers the same hidden package into the destination slot");
            }, null);
            nativeActors.AddRange(display.Loaded.Select(models => models.GetPreferredActor()));
            Check(!page.GetComponentsInChildren<Transform>(true).Any(t => t == preparedTile)
                && returnedPage.GetComponentsInChildren<Transform>(true).Count(t => t.gameObject.name == "BCM_PackageRecipe") == 1,
                "A transferred package leaves no recipe on the old page and no duplicate on the new page");
            ComboCollectionCards.ReleaseHiddenActors(page);
            Check(!prepared.Destroyed && preparedTile.gameObject.activeInHierarchy,
                "Releasing the former slot cannot destroy the transferred package");
        }
        finally
        {
            CardDef.PendingId = null; CardDef.Pending.Clear();
            ComboCollectionCards.ReleaseHiddenActors(page);
            ComboCollectionCards.ReleaseHiddenActors(returnedPage);
            UnityEngine.Object.Destroy(returnedPage.gameObject);
            foreach (Actor actor in nativeActors) actor.Destroy();
            UnityEngine.Object.Destroy(page.gameObject);
        }
    }
}
