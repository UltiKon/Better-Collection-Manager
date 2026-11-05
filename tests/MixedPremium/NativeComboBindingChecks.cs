using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

// Execute installed hooks against a native-shaped renderer: visuals receive
// only actor containers, and inventory cards can share a cached actor container.
internal static class NativeComboRuntime
{
    internal static HarmonyLib.Harmony Active;
    internal static void Invoke(MethodInfo method, object instance, object[] args, Action<object[]> original)
        => Invoke<object>(method, instance, args, values => { original(values); return null; });

    internal static T Invoke<T>(MethodInfo method, object instance, object[] args, Func<object[], T> original)
    {
        if (Active == null || !Active.Hooks.TryGetValue(method, out var hooks)) return original(args);
        object state = null, result = default(T);
        object Hook(HarmonyLib.HarmonyMethod hook)
        {
            if (hook == null) return null;
            ParameterInfo[] parameters = hook.Method.GetParameters();
            var values = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                switch (parameters[i].Name)
                {
                    case "__instance": values[i] = instance; break;
                    case "__args": values[i] = args; break;
                    case "__state": values[i] = state; break;
                    case "__result": values[i] = result; break;
                    case "__originalMethod": values[i] = method; break;
                    default:
                        if (parameters[i].Name.StartsWith("__", StringComparison.Ordinal)
                            && int.TryParse(parameters[i].Name.Substring(2), out int index)) values[i] = args[index];
                        else throw new InvalidOperationException("Unsupported fixture hook parameter: " + parameters[i].Name);
                        break;
                }
            object returned;
            try { returned = hook.Method.Invoke(null, values); }
            catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].Name == "__state") state = values[i];
                else if (parameters[i].Name == "__result") result = values[i];
                else if (parameters[i].ParameterType.IsByRef && parameters[i].Name.StartsWith("__", StringComparison.Ordinal)
                    && int.TryParse(parameters[i].Name.Substring(2), out int index)) args[index] = values[i];
            }
            return returned;
        }
        try { if (!(Hook(hooks.Prefix) is bool run) || run) result = original(args); }
        finally { Hook(hooks.Finalizer); }
        return (T)result;
    }
}

internal sealed class CollectibleRenderFixture
{
    internal void Parallel(CollectibleCard misleading, CollectiblePageDisplay page, List<CollectionCardActors> actors, List<ICollectible> cards)
    {
        NativeComboRuntime.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), this, new object[] { misleading, page, actors, cards }, _ => {
            misleading.GetEntityDef();
            page.UpdateCollectionItems(actors, cards, CollectionUtils.ViewMode.CARDS);
        });
    }
    internal void Render(CollectibleCard card, CollectionCardVisual visual, CollectionCardActors cached, CollectibleCard unrelated = null)
    {
        NativeComboRuntime.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), this, new object[] { card, visual, cached, unrelated }, _ => {
            card.GetEntityDef();
            unrelated?.GetEntityDef(); // Count/inventory lookups must not replace an explicit render argument.
            visual.SetActors(cached, CollectionUtils.ViewMode.CARDS);
        });
    }
    internal void Page(List<CollectibleCard> cards, List<CollectionCardVisual> visuals, CollectionCardActors cached, bool fail)
    {
        NativeComboRuntime.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), this, new object[] { cards, visuals, cached, fail }, _ => {
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].GetEntityDef();
                if (fail) throw new InvalidOperationException("Native renderer failed");
                visuals[i].SetActors(cached, CollectionUtils.ViewMode.CARDS);
            }
        });
    }
}

// API shape observed in 26-10-30_3-0-47.log. The second list can be absent;
// slot identity must also work from native page contents without getter reads.
internal sealed class CollectiblePageDisplay
{
    internal readonly List<CollectionCardVisual> Tiles = new List<CollectionCardVisual>();
    internal void UpdateCollectionItems(List<CollectionCardActors> actors, List<ICollectible> cards, CollectionUtils.ViewMode mode)
    {
        NativeComboRuntime.Invoke((MethodInfo)MethodBase.GetCurrentMethod(), this, new object[] { actors, cards, mode }, _ => {
            for (int i = 0; i < actors.Count; i++)
            {
                if (actors[i] == null) { Tiles[i].Hide(); continue; }
                Tiles[i].SetActors(actors[i], mode);
                Tiles[i].Hide(); // Native pooling can hide after binding, before showing the page.
                Tiles[i].Show();
            }
        });
    }
}

internal sealed class NativeComboPageCollection
{
    internal readonly List<List<CollectibleCard>> Pages = new List<List<CollectibleCard>>();
    public int GetTotalNumPages() => Pages.Count;
    public List<CollectibleCard> GetPageContents(int page) => Pages[page - 1];
}

internal static partial class Program
{
    private static void CheckNativeComboBindings(CollectibleCard proxy, CollectibleCard ordinary, CategoryCatalog.Combo combo)
    {
        var harmony = new HarmonyLib.Harmony();
        ComboCollectionCards.Install(harmony);
        Check(ComboCollectionCards.Ready && harmony.Patched.Contains(typeof(CollectibleRenderFixture).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic))
            && harmony.Patched.Contains(typeof(CollectibleRenderFixture).GetMethod("Page", BindingFlags.Instance | BindingFlags.NonPublic)),
            "Installing combo hooks discovers actual render callers even though native visuals have no collectible-card binding");
        var renderer = new CollectibleRenderFixture();
        var source = AssetLoader.Get().InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
        source.SetEntityDef(ordinary.GetEntityDef());
        var cached = new CollectionCardActors(source);
        var visual = new GameObject("NativeComboTile").AddComponent<CollectionCardVisual>();
        var normal = new GameObject("NativeOrdinaryTile").AddComponent<CollectionCardVisual>();
        string name = source.NameText.Text, rules = source.RulesText.Text;
        string cost = source.CostText.Text;
        float previousTime = Time.unscaledTime;
        NativeComboRuntime.Active = harmony;
        try
        {
            renderer.Render(ordinary, normal, cached);
            renderer.Render(proxy, visual, cached, ordinary);
            Actor privateActor = visual.GetActor();
            Check(ComboCollectionCards.IsCombo(visual) && !ReferenceEquals(visual.Actors, cached) && privateActor != source
                && privateActor.NameText.Text == combo.Name && privateActor.RulesText.Text == combo.Appearance.CardText,
                "The real actor-only binding path renders combo metadata on private models without manually capturing a binding");
            Check(privateActor.CostText.Text == "8" && !privateActor.m_rarityGemMesh.activeSelf
                && source.CostText.Text == cost && source.m_rarityGemMesh.activeSelf,
                "Combo mana sums every captured copy and hides rarity only on the private combo model");
            string firstArtwork = privateActor.GetEntityDef().GetCardId();
            int beforeRotation = AssetLoader.Get().InstantiateCalls;
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval - .1f;
            ComboCollectionCards.OnUpdate();
            Check(visual.GetActor() == privateActor && AssetLoader.Get().InstantiateCalls == beforeRotation,
                "Combo artwork does not allocate models or switch before its rotation interval");
            Time.unscaledTime += .2f;
            ComboCollectionCards.OnUpdate();
            Actor rotated = visual.GetActor();
            Check(rotated != privateActor && !privateActor.Destroyed && !privateActor.gameObject.activeSelf && rotated.GetEntityDef().GetCardId() != firstArtwork
                && rotated.NameText.Text == combo.Name && rotated.RulesText.Text == combo.Appearance.CardText && rotated.CostText.Text == "8"
                && !rotated.m_rarityGemMesh.activeSelf && source.NameText.Text == name && !source.Destroyed,
                "Artwork rotates between real component models while combo captions, total mana and no-rarity presentation remain stable");
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval;
            ComboCollectionCards.OnUpdate();
            Check(visual.GetActor() == privateActor && visual.GetActor().GetEntityDef().GetCardId() == firstArtwork
                && !rotated.Destroyed && !rotated.gameObject.activeSelf && AssetLoader.Get().InstantiateCalls == beforeRotation + 1,
                "Artwork wraps using its bounded inactive model cache without another prefab allocation");
            privateActor = visual.GetActor();
            visual.FailNextSetActors = true;
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval;
            ComboCollectionCards.OnUpdate();
            Check(visual.GetActor() == privateActor && visual.CachedRendererActor == privateActor && !privateActor.Destroyed
                && AssetLoader.Get().LastActor.Destroyed && privateActor.NameText.Text == combo.Name && privateActor.CostText.Text == "8",
                "A failed artwork switch restores the previous model and native renderer cache and frees the rejected replacement");
            Check(normal.Actors == cached && source.NameText.Text == name && source.RulesText.Text == rules && !source.Destroyed
                && !ComboCollectionCards.IsCombo(normal),
                "A combo and its ordinary carrier can coexist with the same native cached assets without renaming the ordinary tile");
            Check(!ComboCollectionCards.PreventGrab(new object[] { visual }) && ComboCollectionCards.PreventGrab(new object[] { normal }),
                "Only the combo slot suppresses native inventory pickup after render binding");
            visual.Hide();
            Check(!privateActor.Destroyed && visual.GetActor() == privateActor && ComboCollectionCards.IsCombo(visual) && !source.Destroyed,
                "Native Hide preserves the combo's model and slot identity for the subsequent pooled Show");
            beforeRotation = AssetLoader.Get().InstantiateCalls;
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval;
            ComboCollectionCards.OnUpdate();
            Check(AssetLoader.Get().InstantiateCalls == beforeRotation,
                "Hidden collection slots stop artwork rotation and allocate no further combo models");
            visual.Show();
            Check(visual.GetActor() == privateActor && privateActor.NameText.Text == combo.Name && privateActor.CostText.Text == "8",
                "Showing a temporarily hidden native slot restores its combo metadata rather than the carrier's identity");
            renderer.Render(proxy, visual, cached);
            privateActor = visual.GetActor();
            renderer.Render(ordinary, visual, cached);
            Check(privateActor.Destroyed && visual.Actors == cached && !ComboCollectionCards.IsCombo(visual) && !source.Destroyed,
                "Rebinding a combo tile to an ordinary card releases its private models without destroying native cached actors");
            renderer.Page(new List<CollectibleCard> { ordinary, proxy }, new List<CollectionCardVisual> { normal, visual }, cached, false);
            Check(!ComboCollectionCards.IsCombo(normal) && ComboCollectionCards.IsCombo(visual),
                "A native page loop without card parameters identifies each slot through its actual card reads");
            privateActor = visual.GetActor();
            ComboCollectionCards.ReleaseHiddenActors(visual);
            bool failed = false;
            try { renderer.Page(new List<CollectibleCard> { proxy }, new List<CollectionCardVisual> { visual }, cached, true); }
            catch (InvalidOperationException) { failed = true; }
            proxy.GetEntityDef(); var ignored = proxy.CardId;
            visual.SetActors(cached, CollectionUtils.ViewMode.CARDS);
            Check(failed && !ComboCollectionCards.IsCombo(visual) && visual.Actors == cached,
                "A failed native render unwinds its context and unrelated card reads cannot contaminate a later ordinary tile");
            var empty = new CollectionCardActors(null);
            renderer.Render(proxy, visual, empty);
            Check(visual.Actors == empty && ComboCollectionCards.IsCombo(visual)
                && !ComboCollectionCards.PreventGrab(new object[] { visual }) && !source.Destroyed && source.NameText.Text == name,
                "Unavailable private actors leave the native page usable and preserve combo input without renaming shared assets");
            CheckParallelComboSlots(renderer, proxy, ordinary, cached, combo);
            CheckNativePageComboSlots(renderer, proxy, ordinary, combo);
            CheckComboPoseAndVisibility(renderer, proxy, ordinary, combo);
            CheckComboManaSorting(combo);
            CheckComboPresentation(renderer, proxy, ordinary, combo);
            CheckComboLocksAndDrag(renderer, proxy, ordinary, combo);
            CheckComboDragQualities(renderer, proxy, ordinary, combo);
            renderer.Render(proxy, visual, cached);
            privateActor = visual.GetActor();
            ComboCollectionCards.Reset();
            Check(privateActor.Destroyed && visual.Actors == cached && normal.Actors == cached && !source.Destroyed
                && source.NameText.Text == name && source.RulesText.Text == rules,
                "Collection teardown restores native actor containers and releases only owned combo resources");
        }
        finally
        {
            NativeComboRuntime.Active = null;
            Time.unscaledTime = previousTime;
            ComboCollectionCards.ReleaseHiddenActors(visual);
            ComboCollectionCards.Forget(normal);
            UnityEngine.Object.Destroy(visual.gameObject); UnityEngine.Object.Destroy(normal.gameObject); source.Destroy();
        }
    }
    private static void CheckParallelComboSlots(CollectibleRenderFixture renderer, CollectibleCard proxy,
        CollectibleCard ordinary, CollectionCardActors cached, CategoryCatalog.Combo combo)
    {
        var page = new CollectiblePageDisplay();
        for (int i = 0; i < 4; i++) page.Tiles.Add(new GameObject("NativeSlot" + i).AddComponent<CollectionCardVisual>());
        var cards = new List<ICollectible> { ordinary, proxy, ordinary, proxy };
        try
        {
            renderer.Parallel(proxy, page, new List<CollectionCardActors> { cached, cached, cached, cached }, cards);
            Check(page.Tiles[0].Actors == cached && page.Tiles[2].Actors == cached
                && !ComboCollectionCards.IsCombo(page.Tiles[0]) && !ComboCollectionCards.IsCombo(page.Tiles[2]),
                "The live parallel-list renderer keeps ordinary slots intact even when the last card read was a combo");
            Check(ComboCollectionCards.IsCombo(page.Tiles[1]) && ComboCollectionCards.IsCombo(page.Tiles[3])
                && page.Tiles[1].GetActor() != cached.Primary && page.Tiles[3].GetActor() != page.Tiles[1].GetActor()
                && page.Tiles[1].GetActor().NameText.Text == combo.Name && page.Tiles[3].GetActor().CostText.Text == "8",
                "Each exact combo slot survives native Hide/Show pooling with its own model even when all cached actor arguments are identical");
            CollectionManager manager = CollectionManager.Get();
            CollectionDeck previousDeck = manager.EditedDeck;
            CollectionDeckTray previousTray = CollectionDeckTray.Instance;
            try
            {
                manager.EditedDeck = new CollectionDeck();
                CollectionDeckTray.Instance = new CollectionDeckTray { Context = manager.EditedDeck };
                MethodInfo release = typeof(CollectionCardVisual).GetMethod("OnRelease", BindingFlags.Instance | BindingFlags.NonPublic);
                bool native = ComboCollectionCards.HandleInput(page.Tiles[1], release);
                Check(!native && manager.EditedDeck.Slots.Values.Sum() == 3,
                    "Left-click on a combo produced by the live native list signature adds its components after Hide/Show pooling");
                Check(ComboCollectionCards.HandleInput(page.Tiles[0], release),
                    "An ordinary neighboring slot retains native input routing after combo insertion");
            }
            finally { manager.EditedDeck = previousDeck; CollectionDeckTray.Instance = previousTray; }
            Actor first = page.Tiles[1].GetActor(), second = page.Tiles[3].GetActor();
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval;
            ComboCollectionCards.OnUpdate();
            Check((page.Tiles[1].GetActor() != first ? 1 : 0) + (page.Tiles[3].GetActor() != second ? 1 : 0) == 1,
                "Simultaneously due combo slots perform at most one native model switch per update");
            Time.frameCount++; ComboCollectionCards.OnUpdate();
            Check(page.Tiles[1].GetActor() != first && page.Tiles[3].GetActor() != second
                && !first.Destroyed && !second.Destroyed && !first.gameObject.activeSelf && !second.gameObject.activeSelf
                && page.Tiles[1].GetActor().NameText.Text == combo.Name && page.Tiles[0].Actors == cached && !cached.Primary.Destroyed,
                "Deferred combo slots rotate on the next update and retain only hidden reusable models without replacing neighbors");
        }
        finally
        {
            foreach (CollectionCardVisual tile in page.Tiles)
            { ComboCollectionCards.ReleaseHiddenActors(tile); UnityEngine.Object.Destroy(tile.gameObject); }
        }
    }
}
