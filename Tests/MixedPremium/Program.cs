using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static int s_checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        s_checks++;
    }
    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
    private static CollectibleCard Card(string id, TAG_PREMIUM premium, int count, TAG_RARITY rarity = TAG_RARITY.COMMON)
        => new CollectibleCard { CardId = id, PremiumType = premium, OwnedCount = count, Rarity = rarity };
    private static MixedPremiumPair Pair(IList<CollectibleCard> variants, CollectibleCard preferred)
        => MixedPremiumPair.Find(preferred, PreferredPremiumHelper.ChooseSecondCopyForDeck(variants, preferred));

    private static void Main()
    {
        CollectionQualitySettings.Display = QualityDisplayMode.Split;
        CollectionQualitySettings.Click = ClickQualityMode.ClickedHalf;
        CollectionQualitySettings.Drag = DragQualityMode.SameAsClick;
        MixedPremiumCard.Ready = true;
        CheckCollapse();
        CheckReprintCollapse();
        CheckCraftingMissingCards();
        CheckGeometry();
        CheckSettingsMeshGeometry();
        CheckGpuReadback();
        CheckLifecycle();
        CheckRulesText();
        CheckCounts();
        CheckTextureSurface();
        CheckCollectionClickInspect();
        CheckQualitySettings();
        CheckDropdownReadability();
        CheckDropdownScroll();
        CheckNativeScrollbarArt();
        CheckCustomFps();
        CheckCategoryStartup();
        CheckPageDiagnostics();
        CheckComboCardText();
        CheckComboIntegration();
        CheckPressSoundTiming();
        CheckEfficiencyRegressions();
        CheckInspectRefresh();
        CheckDisenchantChoices();
        CheckCollectionSearch();
        CheckPackageBrowser();
        Console.WriteLine($"Passed {s_checks} collection quality, deck input, geometry and lifecycle checks.");
    }

    private static void CheckCollapse()
    {
        foreach (TAG_RARITY rarity in new[] { TAG_RARITY.COMMON, TAG_RARITY.RARE, TAG_RARITY.EPIC })
        foreach (int normalOwned in new[] { 1, 2, 9 })
        foreach (int goldenOwned in new[] { 1, 2, 5 })
        {
            var normal = Card("pair", TAG_PREMIUM.NORMAL, normalOwned, rarity);
            var golden = Card("pair", TAG_PREMIUM.GOLDEN, goldenOwned, rarity);
            var list = new List<CollectibleCard> { normal, golden };
            PreferredQualityCollapse.BeginUpdate();
            Check(PreferredQualityCollapse.CollapseInPlace(list, "test", null) == 1 && list.Count == 1, "Owned Normal and Golden must occupy one tile");
            Check(list[0] == golden, "Default premium preference changed");
            bool mixed = PreferredQualityCollapse.TryGetMixed("pair", out MixedPremiumPair pair);
            Check(mixed == (goldenOwned == 1), "Split must occur only when the old rule would show two versions");
            if (mixed) Check(pair.Normal == normal && pair.Golden == golden, "Both actual variants must remain selectable");
            Check(normal.OwnedCount == normalOwned && golden.OwnedCount == goldenOwned, "Merge must not change inventory counts");
        }
        var n = Card("scope", TAG_PREMIUM.NORMAL, 1);
        var g = Card("scope", TAG_PREMIUM.GOLDEN, 1);
        Check(Pair(new[] { n }, n) == null, "A premium-only search must not expose filtered-out variants");
        Check(Pair(new[] { n, Card("scope", TAG_PREMIUM.GOLDEN, 0) }, n) == null, "Unowned Golden must not be merged");
        Check(Pair(new[] { n, Card("different", TAG_PREMIUM.GOLDEN, 1) }, n) == null, "Different card IDs must never merge");
        foreach (TAG_RARITY rarity in new[] { TAG_RARITY.FREE, TAG_RARITY.LEGENDARY })
            Check(Pair(new[] { Card("x", TAG_PREMIUM.NORMAL, 1, rarity), Card("x", TAG_PREMIUM.GOLDEN, 1, rarity) }, Card("x", TAG_PREMIUM.GOLDEN, 1, rarity)) == null, "Split applies only to requested rarities");
        foreach (TAG_PREMIUM premium in new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE })
            Check(Pair(new[] { n, g, Card("scope", premium, 1) }, Card("scope", premium, 1)) == null, "Preferred special quality must retain its own artwork");

        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
        var reversed = new List<CollectibleCard> { g, n };
        PreferredQualityCollapse.CollapseInPlace(reversed, "reversed", null);
        Check(reversed.Count == 1 && reversed[0] == n, "Normal-first preference must also merge");
        Check(PreferredQualityCollapse.TryGetMixed("scope", out _), "One preferred Normal plus Golden should use a split");
        n.OwnedCount = 2;
        PreferredQualityCollapse.BeginUpdate();
        reversed = new List<CollectibleCard> { g, n };
        PreferredQualityCollapse.CollapseInPlace(reversed, "two preferred normal copies", null);
        Check(reversed.Count == 1 && reversed[0] == n && !PreferredQualityCollapse.TryGetMixed("scope", out _),
            "Two preferred Normal copies must remain a full Normal tile even with Golden owned");
        n.OwnedCount = 1;
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE };
        var specialSecond = new List<CollectibleCard> { n, g, Card("scope", TAG_PREMIUM.DIAMOND, 1) };
        PreferredQualityCollapse.CollapseInPlace(specialSecond, "special second", null);
        Check(specialSecond.Count == 2 && specialSecond[0].PremiumType == TAG_PREMIUM.DIAMOND,
            "Golden ownership must not replace the second version selected by the premium order");
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        MixedPremiumCard.Ready = false;
        var fallback = new List<CollectibleCard> { n, g };
        PreferredQualityCollapse.CollapseInPlace(fallback, "fallback", null);
        Check(fallback.Count == 2 && fallback[0] == n && fallback[1] == g, "Unavailable renderer must keep both deck copies accessible");
        MixedPremiumCard.Ready = true;
        PreferredQualityCollapse.BeginUpdate();
        Check(!PreferredQualityCollapse.TryGetMixed("scope", out _), "Filter refresh must discard stale mixed entries");

        CollectionManager.Instance = new CollectionManager();
        CollectionManager.Instance.Counts[("scope", TAG_PREMIUM.NORMAL)] = 0;
        CollectionManager.Instance.Counts[("scope", TAG_PREMIUM.GOLDEN)] = 1;
        Check(Pair(new[] { n, g }, g) == null, "Live collection ownership must override stale card counts");
        CollectionManager.Instance = null;

        Check(MixedPremiumPair.IsGoldenHalf(1.35f, 0, 2.7f), "Upper artwork selects Golden");
        Check(!MixedPremiumPair.IsGoldenHalf(-1.35f, 0, 2.7f), "Lower rules box selects Normal");
        Check(MixedPremiumPair.IsGoldenHalf(-0.134f, 0, 2.7f)
            && !MixedPremiumPair.IsGoldenHalf(-0.136f, 0, 2.7f), "Fallback seam must sit at the name banner");
    }

    private static void CheckGeometry()
    {
        // Rectangle with triangles crossing the horizontal seam. Vertex UVs
        // encode their original position, exposing wrong edge interpolation.
        var source = new Mesh
        {
            name = "card", subMeshCount = 2,
            vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) },
            normals = Enumerable.Repeat(new Vector3(0, 1, 0), 4).ToArray(),
            tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4).ToArray(),
            colors = Enumerable.Repeat(Color.white, 4).ToArray(),
            boneWeights = new[] {
                BoneWeight.TestWeight(0), BoneWeight.TestWeight(1),
                BoneWeight.TestWeight(2), BoneWeight.TestWeight(3) },
            bindposes = Enumerable.Repeat(Matrix4x4.identity, 4).ToArray()
        };
        source.Triangles[0] = new[] { 0, 1, 3 };
        source.Triangles[1] = new[] { 1, 2, 3 };
        for (int channel = 0; channel < 4; channel++)
            source.Uvs[channel] = source.vertices.Select(p => new Vector4(p.x, p.z, channel, 1)).ToList();
        foreach (float scale in new[] { 0.5f, 1f, 2f })
        {
            var transform = new Matrix4x4 { Scale = scale, Offset = new Vector3(4, 2, -3) };
            var center = new Vector3(4, 2, -3);
            var size = new Vector3(2 * scale, 1, 2 * scale);
            Mesh golden = MixedPremiumMesh.Clip(source, transform, center, size, true);
            Mesh normal = MixedPremiumMesh.Clip(source, transform, center, size, false);
            Check(Near(Area(golden), 2.2f) && Near(Area(normal), 1.8f), "The regions must cover the card exactly once at the name-banner seam");
            foreach ((Mesh mesh, bool isGolden) in new[] { (golden, true), (normal, false) })
            {
                Check(mesh.subMeshCount == 2 && mesh.Triangles.Count == 2, "Material/submesh slots must stay intact");
                Check(mesh.bindposes.Length == 4, "Skin bind poses must be preserved");
                for (int i = 0; i < mesh.vertices.Length; i++)
                {
                    Vector3 p = mesh.vertices[i];
                    Check((p.z + 0.1f) * (isGolden ? 1 : -1) >= -0.0001f, "Artwork crossed the horizontal clickable seam");
                    for (int channel = 0; channel < 4; channel++)
                    {
                        Vector4 uv = mesh.Uvs[channel][i];
                        Check(Near(uv.x, p.x) && Near(uv.y, p.z) && Near(uv.z, channel), "Clipped edge UVs were distorted");
                    }
                    BoneWeight w = mesh.boneWeights[i];
                    Check(Near(w.weight0 + w.weight1 + w.weight2 + w.weight3, 1), "Clipped skin weights must remain normalized");
                    Check(Near(mesh.normals[i].y, 1) && Near(mesh.tangents[i].w, 1) && Near(mesh.colors[i].a, 1), "Vertex lighting/alpha attributes changed");
                }
            }
            Check(source.vertices.Length == 4 && source.Triangles[0].SequenceEqual(new[] { 0, 1, 3 }), "Shared source mesh was modified");
        }
        source.isReadable = false;
        source.FailGpuRead = true;
        bool failed = false;
        try { MixedPremiumMesh.Clip(source, Matrix4x4.identity, Vector3.zero, new Vector3(2, 1, 2), true); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed, "Failed GPU readback must trigger the separate-tile fallback");
    }

    private static float Area(Mesh mesh)
    {
        float result = 0;
        foreach (int[] indices in mesh.Triangles.Values)
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = mesh.vertices[indices[i]], b = mesh.vertices[indices[i + 1]], c = mesh.vertices[indices[i + 2]];
                result += Math.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) * 0.5f;
            }
        return result;
    }

    private static Mesh CardMesh()
    {
        var mesh = new Mesh { name = "NativePrefabMesh", subMeshCount = 1,
            vertices = new[] { new Vector3(-1, 0, -1.35f), new Vector3(1, 0, -1.35f), new Vector3(1, 0, 1.35f), new Vector3(-1, 0, 1.35f) } };
        mesh.Triangles[0] = new[] { 0, 1, 2, 0, 2, 3 };
        return mesh;
    }

    private static void CheckGpuReadback()
    {
        foreach (UnityEngine.Rendering.IndexFormat format in new[] { UnityEngine.Rendering.IndexFormat.UInt16, UnityEngine.Rendering.IndexFormat.UInt32 })
        {
            Mesh source = CardMesh();
            source.name = "pPlane2";
            source.indexFormat = format;
            source.normals = Enumerable.Repeat(new Vector3(0, 1, 0), 4).ToArray();
            source.colors = Enumerable.Repeat(new Color(1, 128f / 255, 0, 1), 4).ToArray();
            source.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4).ToArray();
            source.Uvs[0] = source.vertices.Select(p => new Vector4(p.x, p.z, 0, 1)).ToList();
            source.Uvs[3] = source.vertices.Select(p => new Vector4(p.x, p.z, 3, 1)).ToList();
            source.subMeshCount = 2;
            source.Triangles[0] = new[] { 0, 1, 3 };
            source.Triangles[1] = new[] { 0, 1, 2 };
            source.SubMeshes[0] = new UnityEngine.Rendering.SubMeshDescriptor { indexStart = 2, indexCount = 3, baseVertex = 0 };
            source.SubMeshes[1] = new UnityEngine.Rendering.SubMeshDescriptor { indexStart = 9, indexCount = 3, baseVertex = 1 };
            source.isReadable = false;
            bool blocked = false;
            try { _ = source.vertices; } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "Fixture must reproduce pPlane2's discarded CPU vertex data");

            var size = new Vector3(2, 1, 2.7f);
            Mesh golden = MixedPremiumMesh.Clip(source, Matrix4x4.identity, Vector3.zero, size, true);
            Mesh normal = MixedPremiumMesh.Clip(source, Matrix4x4.identity, Vector3.zero, size, false);
            Check(Near(Area(golden), 2.97f) && Near(Area(normal), 2.43f), "GPU-only mesh must split horizontally without holes or overlaps");
            Check(golden.subMeshCount == 2 && normal.subMeshCount == 2, "GPU copy must retain submesh slots, index offsets and base vertices");
            foreach (Mesh mesh in new[] { golden, normal })
                for (int i = 0; i < mesh.vertices.Length; i++)
                {
                    Check(Near(mesh.Uvs[0][i].x, mesh.vertices[i].x) && Near(mesh.Uvs[3][i].y, mesh.vertices[i].z), "GPU copy corrupted secondary-stream UVs");
                    Check(Near(mesh.colors[i].g, 128f / 255) && Near(mesh.colors[i].a, 1), "GPU copy corrupted packed UNorm8 colors");
                    Check(Near(mesh.normals[i].y, 1) && Near(mesh.tangents[i].w, 1), "GPU copy corrupted normal/tangent attributes");
                }
            Check(!source.isReadable, "Readback must not change the game's mesh flags");
            Check(source.VertexReads == 2 && source.IndexReads == 1 && source.BufferDisposals == 3, "Shared geometry must be read only once and buffer handles must be disposed");
            MixedPremiumMesh.ClearReadbackCache();
            _ = MixedPremiumMesh.Clip(source, Matrix4x4.identity, Vector3.zero, size, true);
            Check(source.VertexReads == 4 && source.IndexReads == 2, "Session cache cleanup must permit a fresh snapshot");
            MixedPremiumMesh.ClearReadbackCache();
        }
    }

    private static void CheckLifecycle()
    {
        CollectionManager.Instance = new CollectionManager();
        CollectionManager.Instance.Counts[("mixed", TAG_PREMIUM.NORMAL)] = 1;
        CollectionManager.Instance.Counts[("mixed", TAG_PREMIUM.GOLDEN)] = 1;
        var normal = Card("mixed", TAG_PREMIUM.NORMAL, 1);
        var golden = Card("mixed", TAG_PREMIUM.GOLDEN, 1);
        var results = new List<CollectibleCard> { normal, golden };
        PreferredQualityCollapse.CollapseInPlace(results, "lifecycle", null);
        var root = new GameObject("Visual");
        var visual = root.AddComponent<CollectionCardVisual>();
        var actorRoot = new GameObject("Golden");
        actorRoot.transform.SetParent(root.transform, false);
        var actor = actorRoot.AddComponent<Actor>(); actor.Def = golden.GetEntityDef(); actor.Premium = TAG_PREMIUM.GOLDEN;
        Mesh shared = CardMesh();
        actorRoot.AddComponent<MeshFilter>().sharedMesh = shared;
        actorRoot.AddComponent<MeshRenderer>();
        visual.Actors = new CollectionCardActors(actor);
        AssetLoader.Get().MeshFactory = CardMesh;
        MixedPremiumCard.Attach(visual);
        Actor secondary = AssetLoader.Get().LastActor;
        Check(secondary != null && secondary.Premium == TAG_PREMIUM.NORMAL, "A real Normal actor must back the other half");
        Check(!ReferenceEquals(actor.GetComponent<MeshFilter>().sharedMesh, shared), "Splitting must use a private mesh copy");
        Check(!secondary.GetComponent<Collider>().enabled, "The artwork clone must not steal input");

        Input.mousePosition = new Vector3(0.9f, 0.1f, 0);
        MixedPremiumCard.Choose(visual, true, false);
        Check(visual.GetActor() == actor && visual.GetActor().Premium == TAG_PREMIUM.GOLDEN, "Press above the horizontal seam must choose Golden even at the right edge");
        Input.mousePosition = new Vector3(-0.9f, -0.6f, 0);
        MixedPremiumCard.Choose(visual, false, true);
        Check(visual.GetActor() == actor, "A drag across the seam must retain the pressed version");
        MixedPremiumCard.Choose(visual, false, false);
        Check(visual.GetActor() == secondary && visual.LastCountPremium == TAG_PREMIUM.NORMAL, "Click below the seam must choose Normal even at the left edge and refresh its native count");
        Check(CollectionManager.Instance.Display.LockRefreshes == 1, "Changing half must recalculate native deck locks");
        Check(normal.OwnedCount == 1 && golden.OwnedCount == 1, "Clicking halves must not merge inventory counts");
        // Reproduce native hover events updating the old preferred actor before
        // LateUpdate notices that the pointer entered the opposite half.
        void UpdateMixed() => typeof(MixedPremiumCard).GetMethod("LateUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(root.GetComponent<MixedPremiumCard>(), null);
        foreach (float firstHalf in new[] { 0.8f, -0.7f })
        {
            Input.mousePosition = new Vector3(0f, firstHalf, 0f);
            MixedPremiumCard.Choose(visual, false, false);
            visual.GetActor().SetActorState(ActorStateType.CARD_MOUSE_OVER);
            UpdateMixed();
            Check(actor.State == ActorStateType.CARD_MOUSE_OVER && secondary.State == ActorStateType.CARD_MOUSE_OVER,
                "Both halves must show the native hover state on first entry");
            Input.mousePosition = new Vector3(5f, 5f, 0f);
            visual.GetActor().SetActorState(ActorStateType.CARD_IDLE);
            UpdateMixed();
            Check(actor.State == ActorStateType.CARD_IDLE && secondary.State == ActorStateType.CARD_IDLE,
                "Leaving a split card must clear hover on both actors");
            Input.mousePosition = new Vector3(0f, firstHalf > 0f ? -0.7f : 0.8f, 0f);
            visual.GetActor().SetActorState(ActorStateType.CARD_MOUSE_OVER);
            UpdateMixed();
            Check(actor.State == ActorStateType.CARD_MOUSE_OVER && secondary.State == ActorStateType.CARD_MOUSE_OVER,
                "Re-entering only the other half must retain the hover glow instead of copying its stale idle state");
            visual.GetActor().SetActorState(ActorStateType.CARD_IDLE);
            UpdateMixed();
        }
        Mesh clipped = actor.GetComponent<MeshFilter>().sharedMesh;
        MixedPremiumCard.Release(visual);
        Check(ReferenceEquals(actor.GetComponent<MeshFilter>().sharedMesh, shared), "Hiding a tile must restore its original mesh");
        Check(clipped.Destroyed && secondary.Destroyed && !actor.Destroyed, "Cleanup must destroy only private artwork resources");
        Check(visual.GetActor() == actor, "The native preferred actor must be restored on release");
        MixedPremiumCard.Attach(visual);
        Actor secondClone = AssetLoader.Get().LastActor;
        MixedPremiumCard.Attach(visual);
        Check(secondClone.Destroyed, "Showing a reused tile must not accumulate clones");
        MixedPremiumCard.Release(visual);

        shared.isReadable = false;
        AssetLoader.Get().MeshFactory = () => { Mesh mesh = CardMesh(); mesh.isReadable = false; return mesh; };
        MixedPremiumCard.Attach(visual);
        Check(MixedPremiumCard.TryGetSelected(visual.Actors, out _) && MixedPremiumCard.CanMerge("mixed"), "Two imported GPU-only actors must stay merged");
        Input.mousePosition = new Vector3(0.5f, -0.7f, 0);
        MixedPremiumCard.Choose(visual, true, false);
        Check(visual.GetActor().Premium == TAG_PREMIUM.NORMAL, "GPU-only Normal half must remain selectable");
        MixedPremiumCard.Release(visual);
        Check(ReferenceEquals(actor.GetComponent<MeshFilter>().sharedMesh, shared) && !shared.isReadable, "Cleanup must restore the original GPU-only mesh");

        AssetLoader.Get().MeshFactory = () => { Mesh mesh = CardMesh(); mesh.isReadable = false; mesh.FailGpuRead = true; return mesh; };
        MixedPremiumCard.Attach(visual);
        Check(!MixedPremiumCard.CanMerge("mixed") && !MixedPremiumCard.TryGetSelected(visual.Actors, out _), "Unsupported artwork must disable only this card's split presentation");
        Check(ReferenceEquals(actor.GetComponent<MeshFilter>().sharedMesh, shared), "Failed setup must restore the original card");
        MixedPremiumCard.OnUpdate();
        Check(CollectionManager.Instance.Display.PageRefreshes == 1, "Failure must schedule a native list refresh");
        var fallback = new List<CollectibleCard> { normal, golden };
        PreferredQualityCollapse.CollapseInPlace(fallback, "fallback", null);
        Check(fallback.Count == 2, "A failed split must restore both separately selectable deck copies");
        MixedPremiumCard.ResetSession();
        Check(MixedPremiumCard.CanMerge("mixed"), "Session teardown must clear stale presentation failures");
        CollectionManager.Instance = null;
    }

    private static void CheckRulesText()
    {
        foreach (float screenUp in new[] { 1f, -1f })
        foreach (TAG_PREMIUM primaryPremium in new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL })
        {
            CameraUtils.Camera.UpDirection = screenUp;
            MixedPremiumCard.ResetSession();
            CollectionManager.Instance = new CollectionManager();
            CollectionManager.Instance.Counts[("rules", TAG_PREMIUM.NORMAL)] = 1;
            CollectionManager.Instance.Counts[("rules", TAG_PREMIUM.GOLDEN)] = 1;
            var normal = Card("rules", TAG_PREMIUM.NORMAL, 1);
            var golden = Card("rules", TAG_PREMIUM.GOLDEN, 1);
            PreferredPremiumPrefs.Order = primaryPremium == TAG_PREMIUM.GOLDEN
                ? new[] { TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND }
                : new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
            var results = new List<CollectibleCard> { normal, golden };
            PreferredQualityCollapse.CollapseInPlace(results, "rules", null);
            var root = new GameObject("RulesVisual");
            root.transform.localPosition = new Vector3(3, 0, 4);
            root.transform.localScale = new Vector3(2, 2, 2);
            var visual = root.AddComponent<CollectionCardVisual>();
            var actorRoot = new GameObject("Primary");
            actorRoot.transform.SetParent(root.transform, false);
            var primary = actorRoot.AddComponent<Actor>();
            primary.Def = normal.GetEntityDef(); primary.Premium = primaryPremium;
            Mesh primaryMesh = CardMesh();
            float primaryOffset = primaryPremium == TAG_PREMIUM.GOLDEN ? -0.035f : 0.015f;
            for (int i = 0; i < primaryMesh.vertices.Length; i++) primaryMesh.vertices[i].x += primaryOffset;
            primaryMesh.bounds = new Bounds { center = new Vector3(primaryOffset, 0, 0), size = new Vector3(2, 0.1f, 2.7f) };
            actorRoot.AddComponent<MeshFilter>().sharedMesh = primaryMesh;
            var primaryFrame = actorRoot.AddComponent<MeshRenderer>();
            primaryFrame.UseLocalBounds = true;
            primaryFrame.LocalBounds = primaryMesh.bounds;
            AssetLoader.AttachRulesText(primary);
            visual.Actors = new CollectionCardActors(primary);
            AssetLoader.Get().MeshFactory = CardMesh;
            AssetLoader.Get().CreateRulesText = true;
            AssetLoader.Get().OffsetFrames = true;
            MixedPremiumCard.Attach(visual);
            Actor secondary = AssetLoader.Get().LastActor;
            Check(MixedPremiumCard.TryGetSelected(visual.Actors, out _), "Rules text must not prevent split setup");
            Actor goldenActor = primaryPremium == TAG_PREMIUM.GOLDEN ? primary : secondary;
            Actor normalActor = primaryPremium == TAG_PREMIUM.NORMAL ? primary : secondary;
            Check(Near(goldenActor.GetMeshRenderer().bounds.center.x, normalActor.GetMeshRenderer().bounds.center.x),
                "Different prefab mesh origins must produce aligned visible frame centers");
            float goldenLeft = goldenActor.GetComponent<MeshFilter>().sharedMesh.vertices.Min(p => goldenActor.transform.TransformPoint(p).x);
            float normalLeft = normalActor.GetComponent<MeshFilter>().sharedMesh.vertices.Min(p => normalActor.transform.TransformPoint(p).x);
            Check(Near(goldenLeft, normalLeft), "The actual Golden and Normal frame edges must align across the cut");
            UberText goldenRules = goldenActor.RulesText;
            UberText normalRules = normalActor.RulesText;
            float screenCenter = CameraUtils.Camera.WorldToScreenPoint(root.transform.position).y;
            float screenHeight = 2.7f * 2f;
            float screenSeam = screenCenter - 0.1f * 2f;
            foreach (Actor actor in new[] { goldenActor, normalActor })
            {
                Mesh frame = actor.GetComponent<MeshFilter>().sharedMesh;
                Check(frame.vertices.Length > 0, "Both screen regions must retain artwork");
                foreach (Vector3 p in frame.vertices)
                {
                    float screenY = CameraUtils.Camera.WorldToScreenPoint(actor.transform.TransformPoint(p)).y;
                    Check((screenY - screenSeam) * (actor == goldenActor ? 1 : -1) >= -0.0001f,
                        "Golden must render above the screen seam and Normal below it, including reversed local Z");
                }
            }
            Input.mousePosition = new Vector3(root.transform.position.x, screenCenter + screenHeight * 0.25f, 0);
            MixedPremiumCard.Choose(visual, false, false);
            Check(visual.GetActor() == goldenActor, "Clicking the upper screen region must select Golden for either orientation");
            Input.mousePosition = new Vector3(root.transform.position.x, screenCenter - screenHeight * 0.25f, 0);
            MixedPremiumCard.Choose(visual, false, false);
            Check(visual.GetActor() == normalActor, "Clicking the lower screen region must select Normal for either orientation");
            Check(!goldenRules.gameObject.activeSelf && !goldenRules.GetComponent<Renderer>().enabled,
                "Golden rules must be hidden rather than overlaid on Normal");
            Check(normalRules.gameObject.activeSelf && normalRules.GetComponent<Renderer>().enabled,
                "Normal rules must remain fully visible for either premium order");
            Mesh normalTextMesh = normalRules.GetComponent<MeshFilter>().sharedMesh;
            Check(normalTextMesh.name == "RebuiltRulesText" && normalTextMesh.VertexReads == 0,
                "Normal rules geometry must be left intact without clipping/readback");
            foreach (Actor actor in new[] { goldenActor, normalActor })
                foreach (UberText text in actor.GetComponentsInChildren<UberText>(true))
                {
                    bool header = text == actor.NameText || text == actor.CostText;
                    bool expected = (actor == goldenActor) == header;
                    Check(text.gameObject.activeSelf == expected && text.GetComponent<Renderer>().enabled == expected,
                        "Every label must have one owner: Golden name/mana, Normal rules/attack/health/race");
                    Check(text.GetComponent<MeshFilter>().sharedMesh.VertexReads == 0,
                        "Native text meshes must never be clipped or read back");
                    // Native hover/state updates may turn a suppressed label on.
                    text.gameObject.SetActive(true);
                    text.GetComponent<Renderer>().enabled = true;
                }
            Check(goldenActor.m_nameBannerMesh.activeSelf
                && goldenActor.m_nameBannerMesh.GetComponent<Renderer>().enabled,
                "The complete Golden name banner must stay visible across the seam");
            Check(!normalActor.m_nameBannerMesh.activeSelf
                && !normalActor.m_nameBannerMesh.GetComponent<Renderer>().enabled,
                "The Normal name banner must not mix with the Golden banner");
            Mesh bannerMesh = goldenActor.m_nameBannerMesh.GetComponent<MeshFilter>().sharedMesh;
            Check(bannerMesh.name == "NativeNameBanner" && bannerMesh.VertexReads == 0,
                "The Golden banner must remain whole without clipping or GPU readback");
            foreach (Actor actor in new[] { goldenActor, normalActor })
            {
                Check(!actor.GetComponent<ProjectedShadow>().enabled,
                    "Split cards must disable native projected shadows before drawing");
                foreach (Transform child in actor.GetComponentsInChildren<Transform>(true))
                    if (child.gameObject.name.Contains("Shadow"))
                    {
                        bool fullGolden = actor == goldenActor && (child.gameObject.CompareTag("FakeShadow")
                            || child.gameObject.CompareTag("FakeShadowUnique"));
                        bool visible = actor == goldenActor && child.gameObject.CompareTag("FakeShadow");
                        Check(child.gameObject.activeSelf == fullGolden && child.GetComponent<Renderer>().enabled == visible
                            && child.GetComponent<MeshFilter>().sharedMesh.VertexReads == 0,
                            "Both halves must share one complete Golden shadow without clipping or double-darkening");
                        child.gameObject.SetActive(true);
                        child.GetComponent<Renderer>().enabled = true;
                    }
                actor.GetComponent<ProjectedShadow>().enabled = true;
            }
            normalActor.m_nameBannerMesh.SetActive(true);
            normalActor.m_nameBannerMesh.GetComponent<Renderer>().enabled = true;
            goldenActor.AttackGem.gameObject.SetActive(true);
            goldenActor.HealthGem.gameObject.SetActive(true);
            typeof(MixedPremiumCard).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(root.GetComponent<MixedPremiumCard>(), null);
            Check(Near(goldenActor.GetMeshRenderer().bounds.center.x, normalActor.GetMeshRenderer().bounds.center.x),
                "LateUpdate must maintain alignment after native hover/state changes");
            Check(!goldenRules.gameObject.activeSelf && !goldenRules.GetComponent<Renderer>().enabled,
                "A native text rebuild/hover must not restore overlapping Golden rules");
            foreach (Actor actor in new[] { goldenActor, normalActor })
                foreach (UberText text in actor.GetComponentsInChildren<UberText>(true))
                {
                    bool header = text == actor.NameText || text == actor.CostText;
                    Check(text.gameObject.activeSelf == ((actor == goldenActor) == header),
                        "Hover/rebuilding must not duplicate attack, health, or other labels");
                }
            Check(!goldenActor.AttackGem.gameObject.activeSelf && !goldenActor.HealthGem.gameObject.activeSelf
                && normalActor.AttackGem.gameObject.activeSelf && normalActor.HealthGem.gameObject.activeSelf,
                "Attack and health decorations must come from Normal only");
            Check(ReferenceEquals(normalRules.GetComponent<MeshFilter>().sharedMesh, normalTextMesh),
                "Updating a tile must not replace the complete Normal rules mesh");
            foreach (Actor actor in new[] { goldenActor, normalActor })
            {
                Check(!actor.GetComponent<ProjectedShadow>().enabled, "Hover must not restore a projected shadow");
                foreach (Transform child in actor.GetComponentsInChildren<Transform>(true))
                    if (child.gameObject.name.Contains("Shadow"))
                    {
                        bool fullGolden = actor == goldenActor && (child.gameObject.CompareTag("FakeShadow")
                            || child.gameObject.CompareTag("FakeShadowUnique"));
                        bool visible = actor == goldenActor && child.gameObject.CompareTag("FakeShadow");
                        Check(child.gameObject.activeSelf == fullGolden && child.GetComponent<Renderer>().enabled == visible,
                            "Hover must retain the single full shadow under both halves");
                    }
            }
            Check(!normalActor.m_nameBannerMesh.activeSelf
                && !normalActor.m_nameBannerMesh.GetComponent<Renderer>().enabled
                && ReferenceEquals(goldenActor.m_nameBannerMesh.GetComponent<MeshFilter>().sharedMesh, bannerMesh),
                "Hover must keep only the complete Golden banner");
            MixedPremiumCard.Release(visual);
            Check(primary.GetComponent<ProjectedShadow>().enabled,
                "Releasing a split tile must restore the primary actor's original projected shadow");
            foreach (Transform child in primary.GetComponentsInChildren<Transform>(true))
                if (child.gameObject.name.Contains("Shadow"))
                    Check(child.gameObject.activeSelf && child.GetComponent<Renderer>().enabled,
                        "Releasing a split tile must restore its native contact-shadow visibility");
            Check(normalActor.m_nameBannerMesh.activeSelf && normalActor.m_nameBannerMesh.GetComponent<Renderer>().enabled,
                "Release must restore the native Normal banner");
            Check(goldenRules.gameObject.activeSelf && goldenRules.GetComponent<Renderer>().enabled,
                "Cleanup must restore native rules visibility before tile reuse");
            foreach (Actor actor in new[] { goldenActor, normalActor })
                foreach (UberText text in actor.GetComponentsInChildren<UberText>(true))
                    Check(text.gameObject.activeSelf && text.GetComponent<Renderer>().enabled,
                        "Cleanup must restore every native label for tile reuse");

            CollectionManager.Instance.Counts[("rules", primaryPremium)] = 2;
            PreferredQualityCollapse.BeginUpdate();
            results = new List<CollectibleCard> { normal, golden };
            PreferredQualityCollapse.CollapseInPlace(results, "two preferred copies", null);
            MixedPremiumCard.Attach(visual);
            Check(!MixedPremiumCard.TryGetSelected(visual.Actors, out _) && primary.RulesText.gameObject.activeSelf,
                "Two preferred copies must keep a full native tile and native rules text");
            Check(AssetLoader.Get().LastActor == secondary, "An unneeded split must not allocate a second actor");
        }
        AssetLoader.Get().CreateRulesText = false;
        AssetLoader.Get().OffsetFrames = false;
        CameraUtils.Camera.UpDirection = 1f;
        MixedPremiumCard.ResetSession();
        CollectionManager.Instance = null;
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void CheckTextureSurface()
    {
        var root = new GameObject("CustomNativeTextSurface");
        var fixture = new GameObject("CustomNativeTextSurfaceFixture");
        root.transform.SetParent(fixture.transform, false);
        var label = root.AddComponent<UberText>();
        label.RenderToTexture = true;
        label.RichText = true;
        label.Width = 3;
        label.GlyphOffset = 0.13f;
        label.Text = CounterTextLayout.Decorate("1+2", 35);
        label.RenderOnObject = Child(root.transform, "OutputQuad");
        label.RenderOnObject.transform.localPosition = new Vector3(3.5f, 0, 0);
        var surface = label.RenderOnObject.AddComponent<MeshRenderer>();
        surface.UseLocalBounds = true;
        foreach (float width in new[] { 0.5f, 2f, 4f })
        {
            surface.LocalBounds = new Bounds { size = new Vector3(width, 0.1f, 0.1f) };
            label.UpdateNow(true);
            Check(CounterTextLayout.TryGlyphRange(label, out float left, out float right),
                "Native render-on-object text must have measurable visible glyphs");
            float expectedWidth = label.GetTextWorldSpaceBounds().size.x / label.Width * width;
            float expectedCenter = 3.5f + label.GlyphOffset / label.Width * width;
            Check(Math.Abs(right - left - expectedWidth) < 0.001f
                && Math.Abs((right + left) * 0.5f - expectedCenter) < 0.001f,
                "Glyphs must map through the native camera onto the actual output surface with their bearings intact");
            surface.sortingOrder = 41;
            surface.sortingLayerID = 7;
            var outline = new CounterOutline(label);
            Check(outline.Update("1+2", label.FontSize, true) && surface.sortingOrder == 42,
                "Render-on-object text must raise its real output surface above the outline");
            foreach (UberText stroke in fixture.GetComponentsInChildren<UberText>(true))
                if (stroke.gameObject.name == "BCM_CounterOutline")
                    Check(CounterTextLayout.VisibleRenderers(stroke).All(r => r.sortingLayerID == 7 && r.sortingOrder == 41),
                        "Outline texture quads must share the foreground layer and remain below its output surface");
            surface.enabled = false;
            Check(!outline.Update("1+2", label.FontSize, true) && surface.sortingOrder == 41,
                "A hidden foreground must not leave black outline copies visible on their own");
            surface.enabled = true;
            outline.Clear();
        }
    }

    private static void CheckSeparatorAlignment(CollectionCardCount count)
    {
        var labels = count.m_normalTab.GetComponentsInChildren<UberText>(true)
            .Where(t => t.gameObject.name == "BCM_CounterSeparator" && t.gameObject.activeInHierarchy).ToArray();
        Check(labels.Length == CountText(count).Count(c => c == '+'),
            "Each inline separator must have exactly one visible native label");
        Check(CounterTextLayout.TryScreenInkRect(count.m_normalCountText, CameraUtils.Camera, out Rect numbers),
            "The numeric body must have measurable screen bounds");
        foreach (UberText label in labels)
        {
            Check(label.Text == "+" && label.FontSize == count.m_normalCountText.FontSize
                && Near(label.CharacterSize, count.m_normalCountText.CharacterSize * 0.5f),
                "Only plus glyphs must be half-sized without changing the numeric font");
            Check(CounterTextLayout.TryScreenInkRect(label, CameraUtils.Camera, out Rect plus)
                && Math.Abs(plus.center.y - (numbers.center.y - numbers.height * 0.08f)) < 0.002f
                && plus.xMin >= numbers.xMin - 0.002f && plus.xMax <= numbers.xMax + 0.002f,
                "Small plus glyphs must sit slightly below the numeric ink center and stay inside their inline gaps");
        }
    }

    private static string CountText(CollectionCardCount count)
        => System.Text.RegularExpressions.Regex.Replace(count.m_normalCountText.Text, "<[^>]+>", "");

    private static void CheckCounterOutline(CollectionCardCount count)
    {
        UberText source = count.m_normalCountText;
        var strokes = count.m_normalTab.GetComponentsInChildren<UberText>(true)
            .Where(t => t.gameObject.name == "BCM_CounterOutline" && t.gameObject.activeInHierarchy).ToArray();
        Check(strokes.Length == 8, "The counter must have an opaque stroke around all eight directions");
        Check(CounterTextLayout.TryScreenInkRect(source, CameraUtils.Camera, out Rect face),
            "The colored counter face must have measurable ink");
        Renderer[] faces = CounterTextLayout.VisibleRenderers(source);
        Check(faces.Length > 0, "The foreground must have an actual visible text renderer");
        var directions = new HashSet<int>();
        foreach (UberText stroke in strokes)
        {
            Check(Near(stroke.TextColor.r, 0f) && Near(stroke.TextColor.g, 0f) && Near(stroke.TextColor.b, 0f)
                && Near(stroke.TextColor.a, source.TextColor.a),
                "The opaque black outline must preserve native fading");
            Renderer[] renderers = CounterTextLayout.VisibleRenderers(stroke);
            Check(renderers.Length > 0 && renderers.All(r => faces.All(f => r.sortingLayerID == f.sortingLayerID
                && r.sortingOrder < f.sortingOrder)),
                "The actual outline output must render behind the colored face, including native texture surfaces");
            Check(stroke.Text == CounterTextLayout.Decorate(CountText(count), source.FontSize),
                "Outline text must retain digit spacing and transparent separator gaps without quality tints");
            Check(CounterTextLayout.TryScreenInkRect(stroke, CameraUtils.Camera, out Rect ink)
                && (Near(Math.Abs(ink.center.x - face.center.x), 1.25f) || Near(ink.center.x, face.center.x))
                && (Near(Math.Abs(ink.center.y - face.center.y), 1.25f) || Near(ink.center.y, face.center.y))
                && !(Near(ink.center.x, face.center.x) && Near(ink.center.y, face.center.y)),
                "Outline offsets must form a 1.25-pixel edge in screen space");
            directions.Add(Math.Sign(Math.Round(ink.center.x - face.center.x, 2)) + 3 * Math.Sign(Math.Round(ink.center.y - face.center.y, 2)));
        }
        Check(directions.Count == 8, "The outline must surround every side of the face");
    }

    private static void CheckCounts()
    {
        MixedPremiumCard.ResetSession();
        var manager = CollectionManager.Instance = new CollectionManager();
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        var variants = new List<CollectibleCard>();
        foreach (TAG_PREMIUM premium in PreferredPremiumPrefs.Order)
        {
            int owned = premium == TAG_PREMIUM.GOLDEN ? 1 : premium == TAG_PREMIUM.NORMAL ? 2 : 0;
            var card = Card("counts", premium, owned);
            variants.Add(card);
            manager.Cards[("counts", premium)] = card;
            manager.Counts[("counts", premium)] = owned;
        }
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.GOLDEN) == "1+2", "Unowned qualities above the displayed premium must be omitted");
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.DIAMOND) == "0+0+1+2", "Existing qualities at or below the displayed premium must retain zero in collapse order");
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.DIAMOND, true)
            == "<color=#FFB300>1</color>+<color=#FFFFFF>2</color>",
            "Colored counters must omit every zero quality while keeping owned digits in quality order");
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.NORMAL, true)
            == "<color=#FFFFFF>2</color>+<color=#FFB300>1</color>",
            "Colors must follow quality rather than counter position when the preferred order changes");
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        manager.Cards.Remove(("counts", TAG_PREMIUM.DIAMOND));
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.SIGNATURE) == "0+1+2", "Nonexistent Diamond must be omitted without dropping displayed Signature zero");
        manager.Cards.Remove(("counts", TAG_PREMIUM.SIGNATURE));
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.GOLDEN) == "1+2", "Only existing Golden/Normal qualities should be shown");
        manager.Counts[("counts", TAG_PREMIUM.NORMAL)] = 0;
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.GOLDEN) == "1+0", "An unowned existing quality below the displayed premium must show zero");
        manager.Counts[("counts", TAG_PREMIUM.GOLDEN)] = 0;
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.NORMAL) == "0", "An all-unowned Normal tile must omit higher Golden zero");
        Check(CollapsedPremiumCount.Format("counts", TAG_PREMIUM.NORMAL, true) == null,
            "A colored counter with no owned copies must not leave a zero or a stray separator");
        Check(CollapsedPremiumCount.Format("missing", TAG_PREMIUM.NORMAL) == null, "Unknown cards must leave the native count alone");
        foreach (CollectibleCard card in variants) manager.Cards[(card.CardId, card.PremiumType)] = card;
        manager.Counts[("counts", TAG_PREMIUM.DIAMOND)] = 2;
        manager.Counts[("counts", TAG_PREMIUM.GOLDEN)] = 1;
        manager.Counts[("counts", TAG_PREMIUM.NORMAL)] = 2;
        var collapsed = new List<CollectibleCard>(variants);
        PreferredQualityCollapse.CollapseInPlace(collapsed, "count", null);
        Check(collapsed.Count == 1 && !PreferredQualityCollapse.TryGetMixed("counts", out _),
            "Counts must also apply to full preferred tiles without a visual split");
        var root = new GameObject("CountVisual");
        var visual = root.AddComponent<CollectionCardVisual>();
        var actor = Child(root.transform, "PreferredActor").AddComponent<Actor>();
        actor.Def = variants[0].GetEntityDef(); actor.Premium = TAG_PREMIUM.DIAMOND;
        visual.Actors = new CollectionCardActors(actor);
        var count = visual.m_count = Child(root.transform, "Count").AddComponent<CollectionCardCount>();
        count.m_normalTab = Child(count.transform, "OwnedTab");
        count.m_normalCountText = Child(count.m_normalTab.transform, "CountLabel").AddComponent<UberText>();
        count.m_normalBorder = Child(count.transform, "NarrowBorder");
        count.m_normalWideBorder = Child(count.transform, "WideBorder");
        var wideRenderer = count.m_normalWideBorder.AddComponent<MeshRenderer>();
        wideRenderer.UseLocalBounds = true;
        wideRenderer.LocalBounds = new Bounds { size = new Vector3(0.6f, 0.1f, 0.1f) };
        const float nativeBorderWidth = 0.6f;
        count.m_trialTab = Child(count.transform, "TrialTab");
        var center = Child(count.transform, "Center").transform;
        center.localPosition = new Vector3(0, 0, -1);
        count.Init(center);
        count.SetNative(2, 0);
        float nativeWidth = count.m_normalCountText.Width;
        int nativeFontSize = count.m_normalCountText.FontSize;
        // Exercise cleanup of native fitting settings as well as their removal
        // during the custom fixed-size presentation.
        count.m_normalCountText.ResizeToFit = true;
        count.m_normalCountText.ResizeToFitAndGrow = true;
        count.m_normalCountText.WordWrap = true;
        Vector3 nativeScale = count.m_normalWideBorder.transform.localScale;
        CollapsedPremiumCount.Attach(visual);
        Check(CountText(count) == "2+0+1+2", "A full preferred tile must display all existing qualities");
        Check(count.GetCount() == 2 && count.GetTrialCount() == 0, "Display formatting must not overwrite native inventory data");
        Check(count.m_normalCountText.Width > nativeWidth && !count.m_normalCountText.ResizeToFit
            && !count.m_normalCountText.ResizeToFitAndGrow && !count.m_normalCountText.WordWrap
            && count.m_normalCountText.FontSize == nativeFontSize && count.m_normalWideBorder.activeSelf,
            "Multi-quality totals must grow the tab while keeping the native font size and a single line");
        Check(count.m_normalCountText.RichText && count.m_normalCountText.Text.Contains("<size=17>+</size>"),
            "Separators must use half-size rich text while leaving digits at native font size");
        CheckSeparatorAlignment(count);
        float fixedPadding = wideRenderer.bounds.size.x - count.m_normalCountText.GetTextWorldSpaceBounds().size.x;
        Check(Near(fixedPadding, 0.60f), "The breakdown must have a fixed card-space inset on each side");
        string uncoloredText = count.m_normalCountText.Text;
        float uncoloredWidth = wideRenderer.bounds.size.x;
        int nativeQueue = count.m_normalCountText.RenderQueue;
        Renderer nativeFace = CounterTextLayout.VisibleRenderers(count.m_normalCountText).Single();
        nativeFace.sortingOrder = 17;
        nativeFace.sortingLayerID = 23;
        CollectionQualitySettings.ColorCodeCounter = true;
        count.Show();
        Check(count.m_normalCountText.Text.Contains("<color=#00D5FF>2</color>")
            && !count.m_normalCountText.Text.Contains("<color=#D291FF>")
            && count.m_normalCountText.Text.Contains("<color=#FFB300>1</color>")
            && count.m_normalCountText.Text.EndsWith("<color=#FFFFFF>2</color>") && CountText(count) == "2+1+2",
            "Enabling counter colors must omit zero entries without altering owned counts or their order");
        Check(wideRenderer.bounds.size.x < uncoloredWidth && count.m_normalCountText.FontSize == nativeFontSize
            && Near(wideRenderer.bounds.size.x - count.m_normalCountText.GetTextWorldSpaceBounds().size.x, fixedPadding),
            "Eliding zeros must shrink the colored frame with the same digit size and measured padding");
        CheckSeparatorAlignment(count);
        foreach (bool texture in new[] { false, true })
        {
            count.m_normalCountText.RenderToTexture = texture;
            count.Show();
            CheckCounterOutline(count);
        }
        count.m_normalCountText.RenderToTexture = false;
        manager.Counts[("counts", TAG_PREMIUM.GOLDEN)] = manager.Counts[("counts", TAG_PREMIUM.NORMAL)] = 0;
        foreach (int remaining in new[] { 1, 2 })
        {
            manager.Counts[("counts", TAG_PREMIUM.DIAMOND)] = remaining;
            count.SetNative(remaining, 0);
            if (remaining == 1)
                Check(!count.gameObject.activeSelf && !count.m_normalTab.activeSelf && count.GetCount() == 1,
                    "One owned copy must hide the counter even when unowned qualities were collapsed into the tile");
            else
            {
                Check(CountText(count) == "x2" && count.m_normalCountText.Text == "<color=#00D5FF>x2</color>"
                    && count.gameObject.activeSelf && count.GetCount() == remaining
                    && !count.m_normalTab.GetComponentsInChildren<UberText>(true)
                        .Any(t => t.gameObject.name == "BCM_CounterSeparator" && t.gameObject.activeSelf),
                    "A lone owned quality must show x2 with its color and no stale separators");
                CheckCounterOutline(count);
                foreach (TAG_PREMIUM quality in PreferredPremiumPrefs.Order)
                {
                    foreach (TAG_PREMIUM other in PreferredPremiumPrefs.Order) manager.Counts[("counts", other)] = other == quality ? 2 : 0;
                    count.SetNative(2, 0);
                    string styled = count.m_normalCountText.Text;
                    Check(styled.StartsWith("<color=#") && styled.EndsWith(">x2</color>") && CountText(count) == "x2",
                        "Every single-quality counter colors x and the quantity together: " + quality);
                    string rgb = styled.Substring(8, 6);
                    double Channel(int offset)
                    {
                        double value = Convert.ToInt32(rgb.Substring(offset, 2), 16) / 255.0;
                        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                    }
                    double luminance = .2126 * Channel(0) + .7152 * Channel(2) + .0722 * Channel(4);
                    Check((luminance + .05) / .05 >= 7,
                        "The quality fill has at least 7:1 luminance contrast against its opaque black outline: " + quality);
                }
                foreach (TAG_PREMIUM quality in PreferredPremiumPrefs.Order) manager.Counts[("counts", quality)] = quality == TAG_PREMIUM.DIAMOND ? 2 : 0;
                count.SetNative(2, 0);
            }
        }
        manager.Counts[("counts", TAG_PREMIUM.DIAMOND)] = 2;
        manager.Counts[("counts", TAG_PREMIUM.GOLDEN)] = 1;
        manager.Counts[("counts", TAG_PREMIUM.NORMAL)] = 2;
        CollectionQualitySettings.ColorCodeCounter = false;
        count.SetNative(2, 0);
        Check(count.m_normalCountText.Text == uncoloredText && Near(wideRenderer.bounds.size.x, uncoloredWidth)
            && count.m_normalCountText.RenderQueue == nativeQueue
            && nativeFace.sortingOrder == 17 && nativeFace.sortingLayerID == 23
            && !count.m_normalTab.GetComponentsInChildren<UberText>(true)
                .Any(t => t.gameObject.name == "BCM_CounterOutline" && t.gameObject.activeSelf),
            "Disabling counter colors must restore the original text and geometry");
        actor.Premium = TAG_PREMIUM.GOLDEN;
        visual.OnDoneCrafting();
        Check(count.GetCount() == 1 && count.gameObject.activeSelf && count.m_normalTab.activeSelf
            && CountText(count) == "2+0+1+2", "Changing selected half must not change the displayed breakdown or hide 1+1-style totals");
        count.SetNative(1, 3);
        Check(count.GetCount() == 1 && count.GetTrialCount() == 3 && count.m_trialTab.activeSelf
            && CountText(count) == "2+0+1+2", "Trial counts must stay separate and retain native data");
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
        count.Show();
        Check(CountText(count) == "2+1+2", "Changed order must omit zeros above the tile's displayed premium while retaining owned qualities");
        Check(Near(wideRenderer.bounds.size.x - count.m_normalCountText.GetTextWorldSpaceBounds().size.x, fixedPadding),
            "Shortening the breakdown must keep the same absolute padding");
        count.Hide();
        Check(!count.gameObject.activeSelf, "A native deck lock must still be able to hide the count");
        CollapsedPremiumCount.Release(visual);
        Check(!count.m_normalTab.GetComponentsInChildren<UberText>(true)
            .Any(t => t.gameObject.name == "BCM_CounterSeparator" && t.gameObject.activeSelf),
            "Releasing the counter must remove all private separator labels");
        Check(Near(count.m_normalCountText.Width, nativeWidth) && count.m_normalCountText.ResizeToFit
            && count.m_normalCountText.ResizeToFitAndGrow && count.m_normalCountText.WordWrap
            && count.m_normalCountText.FontSize == nativeFontSize && Near(count.m_normalWideBorder.transform.localScale.x, nativeScale.x)
            && Near(count.m_normalWideBorder.transform.localScale.y, nativeScale.y)
            && Near(count.m_normalWideBorder.transform.localScale.z, nativeScale.z), "Tile release must restore native count geometry");
        count.SetNative(1, 0);
        Check(CountText(count) == "x2" && !count.gameObject.activeSelf,
            "Native single-copy counters stay hidden and keep their last native text");

        CollapsedPremiumCount.Attach(visual);
        PreferredQualityCollapse.BeginUpdate();
        count.SetNative(1, 0);
        Check(!count.gameObject.activeSelf && Near(count.m_normalCountText.Width, nativeWidth),
            "Disabling collapse or changing filters must remove a stale breakdown");
        manager.Counts[("counts", TAG_PREMIUM.NORMAL)] = 5000;
        manager.Counts[("counts", TAG_PREMIUM.DIAMOND)] = 1000;
        manager.Counts[("counts", TAG_PREMIUM.GOLDEN)] = 12;
        collapsed = new List<CollectibleCard>(variants);
        PreferredQualityCollapse.CollapseInPlace(collapsed, "large count", null);
        CollapsedPremiumCount.Attach(visual);
        Check(CountText(count) == "5000+12+0+1000", "Large real counts must not become the native 9+ placeholder");
        Check(count.m_normalCountText.Width > nativeWidth * 3f && !count.m_normalCountText.ResizeToFit
            && !count.m_normalCountText.ResizeToFitAndGrow && !count.m_normalCountText.WordWrap
            && count.m_normalCountText.FontSize == nativeFontSize,
            "Large breakdowns must widen the tab instead of shrinking the font");
        Check(Near(count.m_normalWideBorder.transform.localScale.x * nativeBorderWidth
            - count.m_normalCountText.GetTextWorldSpaceBounds().size.x, fixedPadding),
            "Long breakdowns must keep the same absolute padding as short breakdowns");
        foreach (bool texture in new[] { false, true })
        foreach (float cardScale in new[] { 0.5f, 1f, 2f })
        foreach (float labelScale in new[] { 0.5f, 2f, 4f })
        {
            root.transform.localScale = Vector3.one * cardScale;
            count.m_normalCountText.transform.localScale = Vector3.one * labelScale;
            count.m_normalCountText.RenderToTexture = texture;
            count.Show();
            CheckSeparatorAlignment(count);
            float visibleTextWidth = count.m_normalCountText.GetTextWorldSpaceBounds().size.x;
            if (texture) visibleTextWidth *= count.m_normalCountText.transform.lossyScale.x;
            float tolerance = texture ? 0.0006f * count.m_normalCountText.transform.lossyScale.x : 0.0001f;
            Check(Math.Abs(wideRenderer.bounds.size.x - visibleTextWidth - fixedPadding * cardScale) < tolerance,
                "Texture-space glyph bounds and direct world bounds must both give fixed padding at every label/card scale");
            Check(Math.Abs(count.m_normalCountText.Width * count.m_normalCountText.transform.lossyScale.x
                - wideRenderer.bounds.size.x) < tolerance,
                "The text plane and counter frame must have the same width within offscreen float precision");
        }
        root.transform.localScale = Vector3.one;
        count.m_normalCountText.transform.localScale = Vector3.one;
        count.m_normalCountText.transform.localPosition = new Vector3(0.28f, 0, 0);
        count.m_normalCountText.GlyphOffset = 0.13f;
        count.m_normalCountText.SurfaceOffset = 0.21f;
        wideRenderer.LocalBounds = new Bounds { center = new Vector3(0.19f, 0, 0), size = new Vector3(0.6f, 0.1f, 0.1f) };
        foreach (bool texture in new[] { false, true })
        {
            count.m_normalCountText.RenderToTexture = texture;
            count.Show();
            float inkWidth = count.m_normalCountText.GetTextWorldSpaceBounds().size.x;
            float inkCenter = count.m_normalCountText.transform.position.x
                + count.m_normalCountText.GlyphOffset + (texture ? count.m_normalCountText.SurfaceOffset : 0);
            Check(Math.Abs(inkCenter - inkWidth * 0.5f - wideRenderer.bounds.min.x - fixedPadding * 0.5f) < 0.001f
                && Math.Abs(wideRenderer.bounds.max.x - inkCenter - inkWidth * 0.5f - fixedPadding * 0.5f) < 0.001f,
                "Both frame edges must keep absolute padding despite independent label, surface and frame offsets");
        }
        count.m_normalCountText.GlyphOffset = count.m_normalCountText.SurfaceOffset = 0f;
        count.m_normalCountText.transform.localPosition = Vector3.zero;
        wideRenderer.LocalBounds = new Bounds { size = new Vector3(0.6f, 0.1f, 0.1f) };
        count.m_normalCountText.RenderToTexture = false;
        Color wildText = count.m_wildTextColor;
        count.m_wildTextColor = new Color(wildText.r, wildText.g, wildText.b, 0.45f);
        foreach (PegasusShared.FormatType theme in new[] { PegasusShared.FormatType.FT_STANDARD,
            PegasusShared.FormatType.FT_WILD, PegasusShared.FormatType.FT_CLASSIC })
        {
            manager.Theme = theme;
            count.m_normalCountText.TextColor = Color.white;
            wideRenderer.sharedMaterial = null;
            count.SetNative(1, 0); // Native returns before any styling in this case.
            Color expectedColor = theme == PegasusShared.FormatType.FT_WILD ? count.m_wildTextColor
                : theme == PegasusShared.FormatType.FT_CLASSIC ? count.m_classicTextColor : count.m_standardTextColor;
            Material expectedMaterial = theme == PegasusShared.FormatType.FT_WILD ? count.m_wildBorderMaterial
                : theme == PegasusShared.FormatType.FT_CLASSIC ? count.m_classicBorderMaterial : count.m_standardBorderMaterial;
            Check(Near(count.m_normalCountText.TextColor.r, expectedColor.r)
                && Near(count.m_normalCountText.TextColor.g, expectedColor.g)
                && Near(count.m_normalCountText.TextColor.b, expectedColor.b)
                && ReferenceEquals(wideRenderer.sharedMaterial, expectedMaterial),
                "A forced single-copy breakdown must initialize theme color/material without waiting for hover");
            CollectionQualitySettings.ColorCodeCounter = true;
            count.SetNative(1, 0);
            Check(Near(count.m_normalCountText.TextColor.r, 1f) && Near(count.m_normalCountText.TextColor.g, 1f)
                && Near(count.m_normalCountText.TextColor.b, 1f) && Near(count.m_normalCountText.TextColor.a, expectedColor.a)
                && ReferenceEquals(wideRenderer.sharedMaterial, expectedMaterial),
                "Quality colors must use neutral RGB tint in every theme while preserving native alpha and frame material");
            foreach (UberText separator in count.m_normalTab.GetComponentsInChildren<UberText>(true)
                .Where(t => t.gameObject.name == "BCM_CounterSeparator" && t.gameObject.activeSelf))
                Check(separator.TextColor.Equals(expectedColor),
                    "Separators must retain the native theme color when the number tint is neutralized");
            CheckCounterOutline(count);
            CollapsedPremiumCount.Release(visual);
            Check(count.m_normalCountText.TextColor.Equals(expectedColor) && count.m_normalCountText.RenderQueue == nativeQueue
                && !count.m_normalTab.GetComponentsInChildren<UberText>(true)
                    .Any(t => t.gameObject.name == "BCM_CounterOutline" && t.gameObject.activeSelf),
                "Releasing a colored counter must restore native color/queue and remove its stroke");
            CollectionQualitySettings.ColorCodeCounter = false;
            CollapsedPremiumCount.Attach(visual);
        }
        manager.Theme = PegasusShared.FormatType.FT_STANDARD;
        count.m_normalCountText.TextGeometryReady = false;
        count.Show();
        Check(wideRenderer.bounds.size.x >= nativeBorderWidth,
            "A first-show counter without glyph geometry must retain a usable frame");
        count.m_normalCountText.TextGeometryReady = true;
        CollapsedPremiumCount.OnUpdate();
        Check(Near(wideRenderer.bounds.size.x - count.m_normalCountText.GetTextWorldSpaceBounds().size.x, fixedPadding),
            "A pending frame must gain its correct padded width when geometry becomes ready without hover");
        count.m_normalCountText.TextGeometryReady = false;
        count.Show();
        count.Hide();
        count.m_normalCountText.TextGeometryReady = true;
        CollapsedPremiumCount.OnUpdate();
        Check(!count.gameObject.activeSelf, "A pending layout retry must never unhide a native deck lock");
        count.Show();
        CollapsedPremiumCount.ResetSession();
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL, TAG_PREMIUM.SIGNATURE };
        manager.Counts[("counts", TAG_PREMIUM.DIAMOND)] = 1;
        PreferredQualityCollapse.BeginUpdate();
        collapsed = new List<CollectibleCard>(variants);
        PreferredQualityCollapse.CollapseInPlace(collapsed, "separate variants", null);
        Check(collapsed.Count == 2 && !PreferredQualityCollapse.IsSingleTile("counts"), "Unmerged dual-premium tiles must not share an aggregate counter");
        actor.Premium = TAG_PREMIUM.DIAMOND;
        count.SetNative(1, 0);
        CollapsedPremiumCount.Attach(visual);
        Check(!count.gameObject.activeSelf && count.GetCount() == 1,
            "Separate native premium tiles must keep native single-copy visibility");
        CollapsedPremiumCount.Release(visual);
        PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.DIAMOND, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        foreach (int owned in new[] { 0, 1, 2, 12 })
        foreach (int trials in new[] { 0, 3 })
        {
            manager.Cards.Clear();
            foreach (TAG_PREMIUM premium in PreferredPremiumPrefs.Order) manager.Counts[("counts", premium)] = 0;
            var normalOnly = Card("counts", TAG_PREMIUM.NORMAL, owned);
            manager.Cards[("counts", TAG_PREMIUM.NORMAL)] = normalOnly;
            manager.Counts[("counts", TAG_PREMIUM.NORMAL)] = owned;
            PreferredQualityCollapse.BeginUpdate();
            collapsed = new List<CollectibleCard> { normalOnly };
            PreferredQualityCollapse.CollapseInPlace(collapsed, "single displayed quality", null);
            actor.Premium = TAG_PREMIUM.NORMAL;
            count.SetNative(owned, trials);
            string originalText = count.m_normalCountText.Text;
            bool originalVisible = count.gameObject.activeSelf;
            bool originalTab = count.m_normalTab.activeSelf;
            bool originalNarrow = count.m_normalBorder.activeSelf;
            bool originalWide = count.m_normalWideBorder.activeSelf;
            bool originalTrial = count.m_trialTab.activeSelf;
            CollapsedPremiumCount.Attach(visual);
            count.Show();
            Check(count.m_normalCountText.Text == originalText && count.gameObject.activeSelf == originalVisible
                && count.m_normalTab.activeSelf == originalTab && count.m_normalBorder.activeSelf == originalNarrow
                && count.m_normalWideBorder.activeSelf == originalWide && count.GetCount() == owned
                && count.GetTrialCount() == trials && count.m_trialTab.activeSelf == originalTrial,
                $"One displayed quality must keep native text, frames, visibility and trial data, including 9+ (owned={owned}, trials={trials}, text={count.m_normalCountText.Text}, original={originalText}, visible={count.gameObject.activeSelf}/{originalVisible}, borders={count.m_normalBorder.activeSelf}/{originalNarrow},{count.m_normalWideBorder.activeSelf}/{originalWide}, trial={count.m_trialTab.activeSelf}/{originalTrial})");
            Check(Near(count.m_normalCountText.Width, nativeWidth) && count.m_normalCountText.ResizeToFit
                && count.m_normalCountText.ResizeToFitAndGrow && count.m_normalCountText.WordWrap
                && count.m_normalCountText.FontSize == nativeFontSize
                && Near(count.m_normalWideBorder.transform.localScale.x, nativeScale.x),
                "One displayed quality must retain native font and frame dimensions");
            if (owned == 2 && trials == 0)
            {
                manager.Cards[("counts", TAG_PREMIUM.GOLDEN)] = Card("counts", TAG_PREMIUM.GOLDEN, 1);
                manager.Counts[("counts", TAG_PREMIUM.GOLDEN)] = 1;
                count.Show();
                Check(CountText(count) == "1+2" && count.m_normalWideBorder.activeSelf,
                    "A bound native counter must switch to a breakdown when a second quality appears");
                manager.Counts[("counts", TAG_PREMIUM.GOLDEN)] = 0;
                count.Show();
                Check(count.m_normalCountText.Text == originalText && count.m_normalBorder.activeSelf
                    && !count.m_normalWideBorder.activeSelf && Near(count.m_normalCountText.Width, nativeWidth)
                    && Near(count.m_normalWideBorder.transform.localScale.x, nativeScale.x)
                    && count.m_normalCountText.ResizeToFit && count.m_normalCountText.WordWrap,
                    "Returning from a breakdown to one quality must restore the current native presentation");
            }
            CollapsedPremiumCount.Release(visual);
            Check(count.m_normalCountText.Text == originalText,
                "Releasing a native single-quality counter must preserve its current text");
        }
        MixedPremiumCard.ResetSession();
        CollectionManager.Instance = null;
    }
}
