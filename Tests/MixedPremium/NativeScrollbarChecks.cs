using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckNativeScrollbarArt()
    {
        var resources = Resources.Objects;
        foreach (bool xy in new[] { false, true })
        foreach (bool sibling in new[] { false, true })
        foreach (bool skinned in new[] { false, true })
        {
            var source = MakeSettingsScrollbarTemplate();
            var frame = new GameObject("ScrollbarDialog");
            var bar = new ComboMemberScrollbar(frame.transform);
            try
            {
                var grip = source.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "NativeFilterGrip");
                Mesh gripMesh = grip.GetComponent<MeshFilter>().sharedMesh;
                if (sibling)
                {
                    Vector3 position = grip.transform.position;
                    grip.transform.SetParent(source.transform, true);
                    grip.transform.position = position;
                }
                if (xy)
                {
                    Vector3 Map(Vector3 v) => new Vector3(v.x, v.z, -v.y);
                    foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                    {
                        filter.sharedMesh.vertices = filter.sharedMesh.vertices.Select(Map).ToArray();
                        filter.sharedMesh.bounds = new Bounds(Map(filter.sharedMesh.bounds.center),
                            new Vector3(filter.sharedMesh.bounds.size.x, filter.sharedMesh.bounds.size.z, filter.sharedMesh.bounds.size.y));
                    }
                    foreach (Transform node in source.GetComponentsInChildren<Transform>(true))
                        if (node != source.transform) node.localPosition = Map(node.localPosition);
                    BoxCollider track = source.m_scroller.m_ScrollTrack;
                    track.center = Map(track.center); track.size = new Vector3(track.size.x, track.size.z, track.size.y);
                }
                if (skinned)
                {
                    Material material = grip.sharedMaterial;
                    GameObject owner = grip.gameObject;
                    grip.GetComponent<MeshFilter>().sharedMesh = null;
                    owner.name = "RetiredGripFixture"; owner.SetActive(false);
                    var skinObject = Child(owner.transform.parent, "NativeFilterGrip");
                    skinObject.transform.localPosition = owner.transform.localPosition;
                    skinObject.transform.localRotation = owner.transform.localRotation;
                    skinObject.transform.localScale = owner.transform.localScale;
                    var skin = skinObject.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = gripMesh; skin.sharedMaterial = material;
                }
                // Real imported/baked bounds can describe the entire prefab,
                // while only these four vertices are used by the grip.
                gripMesh.bounds = new Bounds(new Vector3(4, 0, 2), new Vector3(18, 20, 30));
                bar.Layout(CameraUtils.Camera, frame, new Rect(30, 40, 12, 240), 0, 18, 5);
                Check(bar.Root != null && bar.Root.activeInHierarchy && bar.Thumb.height > 0 && bar.Thumb.height < bar.Track.height,
                    $"Native scrollbar builds for plane={(xy ? "XY" : "XZ")}, sibling={sibling}, skinned={skinned}");
                var copies = bar.Root.GetComponentsInChildren<MeshRenderer>(true);
                Check(copies.Count(r => r.gameObject.name == "NativeFilterGrip") == 1,
                    $"Exactly one native grip for xy={xy}, sibling={sibling}, skinned={skinned}: " + string.Join(",", copies.Select(r => r.gameObject.name)));
                Check(Math.Abs(bar.Thumb.width - bar.Track.width) < .01f,
                    "Padded imported grip bounds cannot shrink its visible width");
                var copy = copies.Single(r => r.gameObject.name == "NativeFilterGrip");
                Rect bounds = ComboSceneGeometry.ProjectBounds(copy.bounds, CameraUtils.Camera);
                Check(Math.Abs(bounds.center.x - bar.Thumb.center.x) < .01f && Math.Abs(bounds.center.y - bar.Thumb.center.y) < .01f
                    && Math.Abs(bounds.width / bounds.height - .18f / .3f) < .001f,
                    "Native grip geometry remains visible, centered and proportional after plane normalization");
            }
            finally
            {
                bar.Dispose(); UnityEngine.Object.Destroy(source.gameObject); UnityEngine.Object.Destroy(frame);
            }
        }
        var batched = MakeSettingsScrollbarTemplate();
        var dialog = new GameObject("BatchedScrollbarDialog");
        var batchedBar = new ComboMemberScrollbar(dialog.transform);
        try
        {
            batched.m_scroller.m_ScrollTrack.size = new Vector3(5, .01f, 3.8f);
            var rail = batched.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "NativeFilterRail");
            var gripMesh = batched.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "NativeFilterGrip").GetComponent<MeshFilter>().sharedMesh;
            gripMesh.vertices = gripMesh.vertices.Concat(new[] { new Vector3(100, 0, 100) }).ToArray();
            gripMesh.RecalculateBounds();
            Mesh mesh = rail.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] original = mesh.vertices;
            mesh.vertices = original.Concat(new[] { new Vector3(-6, 0, -2), new Vector3(-1, 0, -2),
                new Vector3(-1, 0, 2), new Vector3(-6, 0, 2) }).ToArray();
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new System.Collections.Generic.List<int> { 4, 5, 6, 4, 6, 7 }, 1, false);
            var uv = new System.Collections.Generic.List<Vector4>(); mesh.GetUVs(0, uv);
            mesh.SetUVs(0, uv.Concat(uv).ToList());
            mesh.RecalculateBounds();
            Material nativeAtlas = rail.sharedMaterial;
            rail.sharedMaterials = new[] { nativeAtlas, new Material { name = "UnrelatedTrayFrame" } };
            var cover = Child(batched.transform, "NativeDisabledTrackCover");
            cover.transform.localPosition = rail.transform.localPosition;
            cover.AddComponent<MeshFilter>().sharedMesh = mesh;
            cover.AddComponent<MeshRenderer>().sharedMaterial = nativeAtlas;
            batched.m_scroller.m_scrollTrackCover = cover;
            batchedBar.Layout(CameraUtils.Camera, dialog, new Rect(30, 40, 12, 240), 0, 18, 5);
            Check(batchedBar.Root != null, "The actual Sets rail is available even when it shares a mesh with the tray frame");
            var copy = batchedBar.Root.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "NativeFilterRail");
            Mesh copied = copy.GetComponent<MeshFilter>().sharedMesh;
            Check(copied.GetTriangles(0).Length > 0 && copied.GetTriangles(1).Length == 0
                && copied.Uvs[0].All(value => value.x >= .1999f && value.x <= .3001f)
                && copy.sharedMaterials.Length == 2 && copy.sharedMaterials[0].name == nativeAtlas.name,
                "A broad input collider cannot admit batched tray borders; native rail UVs and material slots are preserved");
            Check(!batchedBar.Root.GetComponentsInChildren<Transform>(true).Any(t => t.gameObject.name == cover.name)
                && mesh.vertices.Length == 8 && mesh.GetTriangles(1).Length == 6,
                "A disabled-track cover is not copied, and the source's batched mesh stays unchanged");
        }
        finally { batchedBar.Dispose(); UnityEngine.Object.Destroy(batched.gameObject); UnityEngine.Object.Destroy(dialog); }
        var gripOnly = MakeSettingsScrollbarTemplate();
        var gripOnlyDialog = new GameObject("LiveSetsGripOnlyDialog");
        var gripOnlyBar = new ComboMemberScrollbar(gripOnlyDialog.transform);
        try
        {
            foreach (MeshRenderer renderer in gripOnly.GetComponentsInChildren<MeshRenderer>(true))
                if (!renderer.transform.IsChildOf(gripOnly.m_scroller.m_ScrollThumb.transform))
                    UnityEngine.Object.DestroyImmediate(renderer.GetComponent<MeshFilter>());
            gripOnly.m_scroller.m_ScrollTrack.size = new Vector3(5, .01f, 3.8f);
            gripOnlyBar.Layout(CameraUtils.Camera, gripOnlyDialog, new Rect(20, 30, 12, 200), 0, 20, 5);
            Check(gripOnlyBar.Root != null && gripOnlyBar.Root.activeInHierarchy
                && gripOnlyBar.Root.GetComponentsInChildren<MeshRenderer>(true).Length == 1,
                "The logged live Sets shape (grip artwork and collider track, no rail renderer) produces its native scrollbar");
            Check(Math.Abs(gripOnlyBar.Thumb.width - gripOnlyBar.Track.width) < .01f,
                "A broad native track hit box cannot shrink the copied grip to an invisible dot");
            Input.mousePosition = new Vector3(gripOnlyBar.Track.center.x, gripOnlyBar.Track.yMin, 0);
            Input.LeftPressed = Input.LeftHeld = true;
            int first = 0;
            Check(gripOnlyBar.Poll(ref first) && first == 15,
                "A native scrollbar without separate rail artwork still supports track clicks and direct dragging");
        }
        finally
        {
            Input.LeftPressed = Input.LeftHeld = false;
            gripOnlyBar.Dispose(); UnityEngine.Object.Destroy(gripOnly.gameObject); UnityEngine.Object.Destroy(gripOnlyDialog);
        }
        var asymmetric = MakeSettingsScrollbarTemplate();
        var alignmentDialog = new GameObject("NativeGripAlignment");
        var aligned = new ComboMemberScrollbar(alignmentDialog.transform);
        try
        {
            var grip = asymmetric.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.gameObject.name == "NativeFilterGrip");
            // An active shadow extends the grip's bounds only to the right.
            var shadow = Child(grip.transform.parent, "NativeGripShadow");
            shadow.transform.localPosition = new Vector3(.08f, 0, 0);
            shadow.AddComponent<MeshFilter>().sharedMesh = grip.GetComponent<MeshFilter>().sharedMesh;
            shadow.AddComponent<MeshRenderer>().sharedMaterial = grip.sharedMaterial;
            Resources.Objects = new UnityEngine.Object[] { asymmetric };
            aligned.Layout(CameraUtils.Camera, alignmentDialog, new Rect(30, 40, 18, 240), 0, 18, 5);
            Rect visible = ComboSceneGeometry.ProjectBounds(aligned.Root.GetComponentsInChildren<MeshRenderer>(true)
                .Single(r => r.gameObject.name == "NativeFilterGrip").bounds, CameraUtils.Camera);
            Check(Math.Abs(visible.center.x - aligned.Track.center.x) < .01f,
                "An asymmetric grip shadow cannot shift the native grip away from the rail's input axis");
        }
        finally { aligned.Dispose(); UnityEngine.Object.Destroy(asymmetric.gameObject); UnityEngine.Object.Destroy(alignmentDialog); }
        var unrelated = MakeSettingsScrollbarTemplate();
        Resources.Objects = new UnityEngine.Object[] { unrelated.m_scroller };
        var missing = new ComboMemberScrollbar(unrelated.transform);
        try
        {
            missing.Layout(CameraUtils.Camera, unrelated.gameObject, new Rect(0, 0, 12, 240), 0, 18, 5);
            Check(missing.Root != null && missing.Root.GetComponentsInChildren<MeshRenderer>(true).Length == 4,
                "Native rail and grip remain available in Settings when the source has no SetFilterTray wrapper");
        }
        finally { missing.Dispose(); UnityEngine.Object.Destroy(unrelated.gameObject); }
        var sets = MakeSettingsScrollbarTemplate();
        foreach (MeshRenderer renderer in sets.GetComponentsInChildren<MeshRenderer>(true))
            if (!renderer.transform.IsChildOf(sets.m_scroller.m_ScrollThumb.transform))
                UnityEngine.Object.DestroyImmediate(renderer.GetComponent<MeshFilter>());
        var nativeList = MakeSettingsScrollbarTemplate();
        Resources.Objects = new UnityEngine.Object[] { sets, sets.m_scroller, nativeList.m_scroller };
        var complete = new ComboMemberScrollbar(nativeList.transform);
        try
        {
            complete.Layout(CameraUtils.Camera, nativeList.gameObject, new Rect(0, 0, 12, 240), 0, 18, 5);
            Check(complete.Root.GetComponentsInChildren<MeshRenderer>(true).Length == 4,
                "A complete native rail and grip are preferred over an incomplete Sets source");
        }
        finally { complete.Dispose(); UnityEngine.Object.Destroy(sets.gameObject); UnityEngine.Object.Destroy(nativeList.gameObject); }
        Resources.Objects = resources;
    }
}
