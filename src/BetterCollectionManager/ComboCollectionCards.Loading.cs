using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class ComboCollectionCards
    {
        private static readonly Dictionary<CollectionCardActors, OwnedActors> s_preparedPackages = new Dictionary<CollectionCardActors, OwnedActors>();
        private static GameObject s_packageStaging;
        private static int s_packageLoadGeneration;

        private static void InstallPackageLoading(HarmonyLib.Harmony harmony)
        {
            // CollectionPageManager.AssembleCollectionPage calls the
            // ICollectible specialization. Its callback binds the destination
            // page and then starts TransitionPageNextFrame.
            Type display = typeof(CollectibleDisplay).Assembly.GetType("CollectionManagerDisplay");
            if (display == null) return;
            foreach (MethodInfo method in display.GetMethods(NativeMembers))
                if (method.Name == "CollectionPageContentsChanged" && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 3)
                {
                    harmony.Patch(method.MakeGenericMethod(typeof(ICollectible)),
                        prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(PreparePackagePage))));
                    DebugLog.Msg("Package loading: synchronous preparation; native page-ready callback is never delayed.");
                    return;
                }
        }

        public static void PreparePackagePage(IEnumerable __0, ref CollectibleDisplay.CollectionActorsReadyCallback __1)
        {
            int generation = ++s_packageLoadGeneration;
            ReleasePreparedPackages();
            if (__0 == null || __1 == null || !PreferredQualityMode.IsEnabled) return;
            var cards = new List<CollectibleCard>();
            bool hasPackage = false;
            foreach (object value in __0)
                if (value is CollectibleCard card)
                {
                    cards.Add(card);
                    if (s_proxies.TryGetValue(card, out var combo) && combo.IsPackage)
                    { hasPackage = true; PackageNativeTile.Preload(combo); }
                }
            if (!hasPackage) return;
            CollectibleDisplay.CollectionActorsReadyCallback callback = __1;
            __1 = (actors, other, data) => {
                if (generation != s_packageLoadGeneration) return;
                if (actors == null || actors.Count != cards.Count) { callback(actors, other, data); return; }
                for (int i = 0; i < cards.Count; i++)
                {
                    if (actors[i] == null || !s_proxies.TryGetValue(cards[i], out var package) || !package.IsPackage) continue;
                    OwnedActors owned = null;
                    try
                    {
                        owned = ReuseHiddenPackage(actors[i], package) ?? CopyNativeActors(actors[i], null, package);
                        Actor actor = owned.Actors[0];
                        if (s_packageStaging == null)
                        {
                            s_packageStaging = new GameObject("BCM_PackageLoading");
                            s_packageStaging.transform.position = new Vector3(10000, 10000, 10000);
                        }
                        actor.transform.SetParent(s_packageStaging.transform, false);
                        actor.transform.localPosition = Vector3.zero;
                        actor.gameObject.SetActive(true);
                        // Keep recipe art beside the carrier even during loading:
                        // Actor.Start must never cache or hide its renderers.
                        if (!s_packageTiles.TryGetValue(actor, out PackageNativeTile tile))
                            tile = PackageNativeTile.TryCreate(actor, package, s_packageStaging.transform);
                        else tile.AttachTo(s_packageStaging.transform);
                        if (tile == null) { DisposeActors(owned); continue; }
                        s_packageTiles[actor] = tile;
                        tile.PrepareForPage();
                        s_preparedPackages.Add(actors[i], owned);
                        DebugLog.Msg("Package prepared before page-ready: " + package.Name);
                    }
                    catch (Exception ex)
                    {
                        if (owned != null) DisposeActors(owned);
                        DebugLog.Exception("Package page preparation failed", ex);
                    }
                }
                // Text rendering can depend on the visible destination page.
                // Never gate native navigation on offscreen text/asset readiness.
                try { callback(actors, other, data); }
                finally { if (generation == s_packageLoadGeneration) ReleasePreparedPackages(); }
            };
        }

        private static OwnedActors ReuseHiddenPackage(CollectionCardActors source, CategoryCatalog.Combo combo)
        {
            // Book pages retain their hidden slots. Returning to a package used
            // to instantiate another full card actor, recipe and text meshes
            // synchronously just before the page turn. Transfer the hidden copy.
            foreach (var pair in new List<KeyValuePair<CollectionCardVisual, OwnedActors>>(s_ownedActors))
            {
                CollectionCardVisual visual = pair.Key;
                OwnedActors owned = pair.Value;
                if (visual == null || visual.IsShown() || owned.Combo != combo || owned.Premium != CommonPremium(combo)
                    || visual.GetCollectionCardActors() != owned.Private || owned.Actors.Count == 0
                    || !s_packageTiles.ContainsKey(owned.Actors[0])) continue;
                var privateActors = (CollectionCardActors)s_clone.Invoke(source, null);
                s_nativeActorList.SetValue(privateActors, owned.Actors);
                Forget(visual);
                s_ownedActors.Remove(visual);
                s_visualActors.SetValue(visual, owned.Source);
                s_actorCombos.Remove(owned.Private);
                owned.Source = source;
                owned.Private = privateActors;
                s_actorCombos.Add(owned.Private, combo);
                owned.BindingLogged = false;
                DebugLog.Msg("Package page preparation reused hidden recipe: " + combo.Name);
                return owned;
            }
            return null;
        }

        private static OwnedActors TakePreparedPackage(CollectionCardActors source, CategoryCatalog.Combo combo)
        {
            if (!s_preparedPackages.TryGetValue(source, out var owned) || owned.Combo != combo) return null;
            s_preparedPackages.Remove(source);
            Actor original = source.GetPreferredActor();
            foreach (Actor actor in owned.Actors)
            {
                actor.transform.SetParent(original.transform.parent, false);
                actor.transform.localPosition = original.transform.localPosition;
                actor.transform.localRotation = original.transform.localRotation;
                actor.transform.localScale = original.transform.localScale;
                actor.gameObject.SetActive(original.gameObject.activeSelf);
            }
            DebugLog.Msg("Package binding reused prepared artwork: " + combo.Name);
            return owned;
        }

        private static void ReleasePreparedPackages()
        {
            foreach (OwnedActors owned in s_preparedPackages.Values) DisposeActors(owned);
            s_preparedPackages.Clear();
            if (s_packageStaging != null) UnityEngine.Object.Destroy(s_packageStaging);
            s_packageStaging = null;
        }
    }
}
