using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboPoseAndVisibility(CollectibleRenderFixture renderer, CollectibleCard proxy,
        CollectibleCard carrier, CategoryCatalog.Combo combo)
    {
        var loader = AssetLoader.Get(); var previousFactory = loader.MeshFactory;
        float previousTime = Time.unscaledTime;
        var actorRoot = new GameObject("Native actor parent");
        actorRoot.transform.localPosition = new Vector3(8, 3, -2);
        actorRoot.transform.localRotation = new Quaternion(0, 1, 0, 0);
        actorRoot.transform.localScale = new Vector3(.5f, .75f, 1.25f);
        var tileRoot = new GameObject("Native collection collider");
        tileRoot.transform.localPosition = new Vector3(-40, -20, 15);
        tileRoot.transform.localRotation = new Quaternion(1, 0, 0, 0);
        tileRoot.transform.localScale = Vector3.one * 2;
        CollectionCardVisual visual = tileRoot.AddComponent<CollectionCardVisual>();
        visual.EmulateNativeActorCache = true; visual.Shown = false;
        Actor source = null;
        var glows = new System.Collections.Generic.Dictionary<Actor, bool>();
        bool SamePose(Actor actor, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Quaternion q = actor.transform.rotation;
            float dot = Math.Abs(q.x*rotation.x + q.y*rotation.y + q.z*rotation.z + q.w*rotation.w);
            return Vector3.Distance(actor.transform.position, position) < .001f
                && Vector3.Distance(actor.transform.lossyScale, scale) < .001f && Math.Abs(dot - 1f) < .001f;
        }
        try
        {
            loader.MeshFactory = () => {
                Mesh mesh = previousFactory();
                Actor created = loader.LastActor;
                created.IgnoreRepeatedState = true; glows[created] = false;
                created.StateChanged += state => {
                    glows[created] = state == ActorStateType.CARD_MOUSE_OVER;
                    created.transform.localRotation = new Quaternion(0, 0, 1, 0);
                    created.transform.localPosition = new Vector3(100, 100, 100);
                    created.transform.localScale = Vector3.one * .001f;
                };
                return mesh;
            };
            source = loader.InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
            source.SetEntityDef(carrier.GetEntityDef()); source.SetActorState(ActorStateType.CARD_MOUSE_OVER);
            source.transform.SetParent(actorRoot.transform, false);
            source.transform.localPosition = new Vector3(1, 2, 3);
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = new Vector3(.7f, .8f, .9f);
            source.gameObject.SetActive(false);
            Vector3 position = source.transform.position, scale = source.transform.lossyScale;
            Quaternion rotation = source.transform.rotation;
            var cached = new CollectionCardActors(source);
            renderer.Render(proxy, visual, cached);
            Actor current = visual.GetActor();
            Check(current.transform.parent == source.transform.parent && SamePose(current, position, rotation, scale)
                && current.GetActorStateType() == source.GetActorStateType(),
                "Initial combo model matches the native parent and world pose after state initialization instead of inheriting the collider's inverted rotation");
            visual.Show();
            Check(visual.IsShown() && current.gameObject.activeInHierarchy && visual.CachedRendererActor == current,
                "The native Show establishes a visible combo with its actual cached renderer");
            // Native hide/show and actor-state initialization may reset a pose.
            // Switching artwork must preserve the pose seen before those calls.
            void ResetPose(Actor actor)
            {
                glows[actor] = false; // Show resets the effect while retaining the cached actor state.
                actor.transform.localPosition = new Vector3(90, 80, 70);
                actor.transform.localRotation = new Quaternion(0, 0, 1, 0);
                actor.transform.localScale = Vector3.one * .01f;
            }
            visual.OnNativeActorHidden = ResetPose; visual.OnNativeActorShown = ResetPose;
            for (int i = 0; i < 4; i++)
            {
                ActorStateType hover = i == 3 ? ActorStateType.CARD_IDLE : ActorStateType.CARD_MOUSE_OVER;
                current.SetActorState(ActorStateType.CARD_IDLE);
                current.SetActorState(hover);
                // State callbacks can move the model just as a native hover can.
                position = current.transform.position; rotation = current.transform.rotation; scale = current.transform.lossyScale;
                Actor old = current; CollectionCardActors oldContainer = visual.Actors;
                int shows = visual.NativeActorShows, hides = visual.NativeActorHides;
                Time.unscaledTime += ComboCollectionCards.ArtworkInterval + .01f;
                ComboCollectionCards.OnUpdate();
                current = visual.GetActor();
                Check(current != old && !old.Destroyed && !old.gameObject.activeSelf && visual.Actors != oldContainer
                    && visual.CachedRendererActor == current && !current.Destroyed && current.gameObject.activeInHierarchy
                    && visual.IsShown() && visual.NativeActorShows == shows + 1 && visual.NativeActorHides == hides + 1,
                    "Each artwork switch replaces the container and refreshes native Show/render caches before parking the previous model");
                Check(SamePose(current, position, rotation, scale) && current.NameText.Text == combo.Name
                    && current.CostText.Text == "8" && ComboCollectionCards.IsCombo(visual),
                    "Repeated artwork switches remain upright, at the original position and scale, with stable combo metadata");
                Check(current.GetActorStateType() == hover && glows[current] == (hover == ActorStateType.CARD_MOUSE_OVER),
                    "Artwork changes preserve a stationary pointer's hover glow after native Show resets the actor, and preserve idle after mouse-out");
            }
            Actor retained = current;
            retained.SetActorState(ActorStateType.CARD_MOUSE_OVER);
            position = retained.transform.position; rotation = retained.transform.rotation; scale = retained.transform.lossyScale;
            int discardedBefore = glows.Keys.Count(actor => actor.Destroyed);
            visual.FailNextSetActors = true;
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval + .01f;
            ComboCollectionCards.OnUpdate();
            Check(visual.GetActor() == retained && visual.CachedRendererActor == retained && !retained.Destroyed
                && retained.gameObject.activeInHierarchy && visual.IsShown() && SamePose(retained, position, rotation, scale)
                && retained.NameText.Text == combo.Name && glows.Keys.Count(actor => actor.Destroyed) == discardedBefore + 1
                && retained.GetActorStateType() == ActorStateType.CARD_MOUSE_OVER && glows[retained],
                "A rejected artwork switch restores the previous visible renderer and world pose instead of leaving the slot hidden");
            Check(!source.Destroyed && !source.gameObject.activeSelf,
                "Combo rotation neither destroys nor reveals the native cached source model");
        }
        finally
        {
            ComboCollectionCards.ReleaseHiddenActors(visual);
            Check(glows.Keys.Where(actor => actor != source).All(actor => actor.Destroyed),
                "Releasing a combo disposes every active and parked private model in its bounded artwork cache");
            if (source != null) source.Destroy();
            UnityEngine.Object.Destroy(tileRoot); UnityEngine.Object.Destroy(actorRoot);
            loader.MeshFactory = previousFactory; Time.unscaledTime = previousTime;
        }
    }
}
