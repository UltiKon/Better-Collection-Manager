using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckEfficiencyRegressions()
    {
        foreach (int count in new[] { 0, 1, 63, 64, 65, 127, 129 })
        {
            FilterBitSet a = FilterBitSet.Build(count, i => i % 3 == 0);
            foreach (FilterBitSet b in new[] { FilterBitSet.Empty(count), FilterBitSet.Full(count), FilterBitSet.Build(count, i => i % 2 == 0) })
                Check(a.Intersects(b, true) == a.Not().Intersects(b),
                    "Allocation-free negative availability retains complement/intersection semantics, including partial machine words");
        }
        FilterBitSet left = FilterBitSet.Build(10000, i => i % 2 == 0), right = FilterBitSet.Full(10000);
        left.Intersects(right, true);
        long allocated = GC.GetAllocatedBytesForCurrentThread(); int matches = 0;
        for (int i = 0; i < 1000; i++) if (left.Intersects(right, true)) matches++;
        Check(matches == 1000 && GC.GetAllocatedBytesForCurrentThread() - allocated < 128,
            "Repeated negative availability checks do not allocate word arrays");

        var clipRoot = new GameObject("Reusable clip buffers");
        var points = new Vector3[128 * 3]; var triangles = new List<int>();
        for (int i = 0; i < 128; i++)
        {
            points[i * 3] = new Vector3(-1, 0, -1); points[i * 3 + 1] = new Vector3(1, 0, -1); points[i * 3 + 2] = new Vector3(0, 0, 1);
            triangles.Add(i * 3); triangles.Add(i * 3 + 1); triangles.Add(i * 3 + 2);
        }
        var clipSource = new Mesh { vertices = points, subMeshCount = 1 }; clipSource.SetTriangles(triangles, 0, false); clipSource.RecalculateBounds();
        Rect sourceArea = ComboSceneGeometry.ProjectBounds(clipSource.bounds, CameraUtils.Camera);
        var cut = new Rect(sourceArea.xMin + sourceArea.width * .25f, sourceArea.yMin, sourceArea.width * .75f, sourceArea.height);
        Mesh output = null;
        try
        {
            for (int i = 0; i < 3; i++) output = FilterTrayGeometry.ClipMesh(clipSource, clipRoot.transform, CameraUtils.Camera, cut, output);
            allocated = GC.GetAllocatedBytesForCurrentThread();
            output = FilterTrayGeometry.ClipMesh(clipSource, clipRoot.transform, CameraUtils.Camera, cut, output);
            Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 250000 && output.vertexCount > 0,
                "A warmed 128-triangle clip keeps temporary allocations bounded instead of allocating polygon buffers for every edge");
        }
        finally { UnityEngine.Object.Destroy(output); UnityEngine.Object.Destroy(clipSource); UnityEngine.Object.Destroy(clipRoot); }

        CollectionManager previous = CollectionManager.Get(); bool ready = ComboCollectionCards.Ready;
        CollectionManager manager = new CollectionManager(); CollectionManager.Instance = manager;
        var definitions = new List<string>();
        try
        {
            ComboCollectionCards.Reset();
            for (int i = 0; i < 10000; i++)
            {
                string id = "EFF_" + i;
                var card = Card(id, TAG_PREMIUM.NORMAL, 2);
                manager.Cards[(id, TAG_PREMIUM.NORMAL)] = card;
            }
            KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
            ComboCollectionCards.Ready = true;
            var idleBucket = new List<CollectibleCard>(manager.GetAllCards());
            var idleResults = new Dictionary<int, List<CollectibleCard>> { [1] = idleBucket };
            ComboCollectionCards.Prepare(idleResults)();
            allocated = GC.GetAllocatedBytesForCurrentThread();
            var prepareTimer = System.Diagnostics.Stopwatch.StartNew();
            ComboCollectionCards.Prepare(idleResults)();
            prepareTimer.Stop();
            long idleAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(idleBucket.Count == 10000 && idleAllocated < 2000000,
                "An unselected recipe catalog preserves native results without allocating per-card carrier indexes");
            Console.WriteLine($"Idle recipe preparation, 10000 cards: {prepareTimer.Elapsed.TotalMilliseconds:F1} ms; {idleAllocated} bytes (managed fixture).");
            var exact = new CategoryCatalog.Combo { ExactVersions = true };
            int scans = manager.GetAllCardsCalls;
            for (int i = 0; i < 100; i++)
                Check(ComboCollectionCards.ComponentCards(exact, "EFF_9999").Single().CardId == "EFF_9999", "Exact component editions resolve through the native quality index");
            Check(manager.GetAllCardsCalls == scans, "Exact component lookups never enumerate the full inventory");
            var legacy = new CategoryCatalog.Combo { ExactVersions = false };
            ComboCollectionCards.ComponentCards(legacy, "EFF_9999"); scans = manager.GetAllCardsCalls;
            for (int i = 0; i < 100; i++) ComboCollectionCards.ComponentCards(legacy, "EFF_9999");
            Check(manager.GetAllCardsCalls == scans, "Legacy component lookups reuse one normalized-edition index");
            var core = Card("CORE_EFF_9999", TAG_PREMIUM.NORMAL, 2);
            manager.Cards[(core.CardId, core.PremiumType)] = core;
            KeywordFilterState.Clear(); ExtraFilterState.ClearAll(); ExpansionFilterState.Clear();
            ComboCollectionCards.Ready = false;
            ComboCollectionCards.Prepare(new Dictionary<int,List<CollectibleCard>> { [1] = new List<CollectibleCard> { core } })();
            Check(ComboCollectionCards.ComponentCards(legacy, "EFF_9999").Single() == core,
                "A native results refresh invalidates the edition index and retains the newly selected concrete edition");

            PackageArtwork.Clear();
            for (int i = 0; i <= PackageArtwork.MaxCachedCatalogs + 2; i++)
            {
                string id = "EFF_ART_" + i;
                var def = new EntityDef { CardId = id, Name = id };
                def.SetTag(GAME_TAG.CARDTYPE, (int)TAG_CARDTYPE.MINION); def.SetTag(GAME_TAG.COLLECTIBLE, 1);
                DefLoader.Get().Definitions[id] = def; definitions.Add(id);
            }
            PackageArtwork.Catalog held = PackageArtwork.For(new[] { "EFF_ART_0" });
            for (int i = 1; i <= PackageArtwork.MaxCachedCatalogs + 2; i++) PackageArtwork.For(new[] { "EFF_ART_" + i });
            var catalogs = (System.Collections.IDictionary)typeof(PackageArtwork).GetField("s_catalogs", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(catalogs.Count <= PackageArtwork.MaxCachedCatalogs && held.Contains("EFF_ART_0"),
                "Artwork caches stay bounded while an editor can keep using an evicted catalog instance");

            string root = "EFF_TOKEN_ROOT";
            for (int i = 0; i <= 600; i++)
            {
                string id = i == 0 ? root : "EFF_TOKEN_" + i;
                var def = new EntityDef { CardId = id, Name = id };
                def.SetTag(GAME_TAG.CARDTYPE, (int)TAG_CARDTYPE.MINION); def.SetTag(GAME_TAG.COLLECTIBLE, i == 0 ? 1 : 0);
                DefLoader.Get().Definitions[id] = def; definitions.Add(id);
                if (i < 600) GameUtils.RelatedCards[id] = new[] { "EFF_TOKEN_" + (i + 1) };
            }
            var package = new CategoryCatalog.Combo { IsPackage = true, Artwork = "EFF_TOKEN_600",
                Cards = new[] { new CategoryCatalog.CardRequirement(root, 1, Array.Empty<string>()) } };
            Check(PackageArtwork.ResolveFor(package) == null,
                "Resolving saved deep token artwork yields after a bounded step instead of exhausting the graph synchronously");
            PackageArtwork.Catalog pending = PackageArtwork.For(new[] { root });
            Check(!pending.Complete, "Unfinished saved-artwork resolution retains pending search work");
            for (int i = 0; i < 100 && !pending.Complete; i++) pending.Advance();
            Check(pending.Complete && PackageArtwork.ResolveFor(package)?.GetCardId() == package.Artwork,
                "Incremental artwork resolution preserves the saved selection once its token family is ready");
            ComboCollectionCards.Reset();
            Check(catalogs.Count == 0, "Leaving the collection releases cached artwork membership combinations");
        }
        finally
        {
            foreach (string id in definitions) { DefLoader.Get().Definitions.Remove(id); GameUtils.RelatedCards.Remove(id); }
            PackageArtwork.Clear(); CollectionManager.Instance = previous; ComboCollectionCards.Ready = ready;
        }

        QualitySettingsChange observed = 0;
        Action<QualitySettingsChange> changed = value => observed |= value;
        CollectionQualitySettings.ChangedKind += changed;
        bool edit = CollectionQualitySettings.ShowEditControls;
        DragQualityMode drag = CollectionQualitySettings.Drag;
        try
        {
            CollectionQualitySettings.ShowEditControls = !edit;
            Check(observed == QualitySettingsChange.FilterLayout, "Filter chrome changes invalidate only the filter layout");
            observed = 0;
            CollectionQualitySettings.Drag = drag == DragQualityMode.SameAsClick ? DragQualityMode.DraggedHalf : DragQualityMode.SameAsClick;
            Check(observed == QualitySettingsChange.Input, "Input policy changes do not request card-result or filter-layout rebuilds");
        }
        finally
        {
            CollectionQualitySettings.ShowEditControls = edit; CollectionQualitySettings.Drag = drag;
            CollectionQualitySettings.ChangedKind -= changed; CollectionQualitySettings.FlushPreferences(true);
        }
    }
}
