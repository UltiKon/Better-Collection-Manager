using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboInspectPaging(CategoryCatalog.Combo original)
    {
        int width = Screen.width, height = Screen.height;
        var book = CollectionManager.Get().Display;
        Quaternion previousLeft = book.m_pageLeftArrowBone.transform.rotation, previousRight = book.m_pageRightArrowBone.transform.rotation;
        try
        {
            Screen.width = 1280; Screen.height = 720;
            book.m_pageRightArrowBone.transform.rotation = new Quaternion(0, .70710677f, .70710677f, 0);
            book.m_pageLeftArrowBone.transform.rotation = new Quaternion(.70710677f, 0, 0, .70710677f);
            var detachedRoot = new GameObject("Detached native arrow regression");
            PagingArrow.HighlightOnlyArt = true;
            GameObject.RequireConcreteButtonCollider = true;
            try
            {
                using (var detached = new ComboNativeArrow(detachedRoot.transform, false, () => { }))
                {
                    detached.Layout(CameraUtils.Camera, new Rect(100, 100, 50, 50), 10f, true);
                    CheckArrowPressMotion(detached, CameraUtils.Camera);
                    Check(detached.Root.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.enabled),
                        "A detached native arrow stays visible when its prefab contains only the book hover overlay");
                    detached.Root.GetComponent<PegUIElement>().Send(UIEventType.RELEASE);
                    Time.unscaledTime += .2f; detached.Update();
                    Check(detached.Root.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.enabled),
                        "Finishing the arrow press feedback never hides its only visible artwork");
                }
            }
            finally { PagingArrow.HighlightOnlyArt = false; GameObject.RequireConcreteButtonCollider = false; UnityEngine.Object.Destroy(detachedRoot); }
            var pressRoot = new GameObject("Shared native arrow press regression");
            try
            {
                using (var arrow = new ComboNativeArrow(pressRoot.transform, true, () => { }))
                {
                    arrow.Layout(CameraUtils.Camera, new Rect(100, 100, 100, 100), 10f, true);
                    CheckArrowPressMotion(arrow, CameraUtils.Camera);
                    CheckBookArrowHoverStability(arrow, CameraUtils.Camera);
                }
                foreach (bool faceOnRoot in new[] { false, true })
                foreach (bool hasPressRoot in new[] { false, true })
                {
                    CardListPanel.ArrowHasPressRoot = hasPressRoot;
                    CardListPanel.ArrowFaceOnRoot = faceOnRoot;
                    using (var template = new ComboNativeArrow.Template())
                    {
                        Rect? pairSize = null;
                        foreach (bool left in new[] { false, true })
                            using (var arrow = new ComboNativeArrow(pressRoot.transform, left, () => { }, template: template))
                            {
                                if (faceOnRoot) arrow.Root.GetComponent<Renderer>().enabled = true;
                                arrow.Layout(CameraUtils.Camera, new Rect(100, 100, 100, 100), 10f, true);
                                Renderer body = NativeArrowFace(arrow.Root);
                                Check(body != null, "Native arrow artwork survives either an authored or a missing press root");
                                Rect size = ComboSceneGeometry.ProjectBounds(body.bounds, CameraUtils.Camera);
                                if (pairSize.HasValue)
                                    Check(Math.Abs(size.width - pairSize.Value.width) < .1f && Math.Abs(size.height - pairSize.Value.height) < .1f,
                                        "Both visible arrow faces have the same size despite different inactive glow meshes");
                                pairSize = size;
                                CheckArrowPressMotion(arrow, CameraUtils.Camera);
                            }
                    }
                }
            }
            finally { CardListPanel.ArrowHasPressRoot = CardListPanel.ArrowFaceOnRoot = false; UnityEngine.Object.Destroy(pressRoot); }
            var cards = new List<CategoryCatalog.CardRequirement>();
            for (int i = 0; i < 9; i++) cards.Add(original.Cards[i % original.Cards.Count]);
            var combo = new CategoryCatalog.Combo { Id = "Paging regression", Name = "Many cards", Description = "", Category = original.Category,
                Cards = cards, ExactVersions = original.ExactVersions };
            ButtonListMenu.FailNextShowLayout = true;
            bool failed = false;
            try { ComboPopup.Inspect(combo); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && !UniversalInputManager.Get().IsDialogActive() && ButtonListMenu.Visible == null,
                "A failed native layout releases modal input even before IsShown becomes true");
            ComboPopup.Inspect(combo);
            var popup = (ComboPopup)ButtonListMenu.Visible;
            Check(popup.IsShown() && popup.GetComponentsInChildren<Actor>(false).Length > 0,
                "Inspect opens with complete previews after failure recovery and arrows without a native press root");
            var edit = popup.Buttons.Single(button => button.gameObject.name == "BCM_EditCombo");
            var delete = popup.Buttons.Single(button => button.gameObject.name == "BCM_DeleteCombo");
            Check(edit.Template.gameObject.name == "Native menu button template"
                && edit.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.sharedMaterial?.name == "Native dark menu button"),
                "Inspect Edit uses the ordinary native menu button, without the shop confirmation checkmark");
            Check(delete.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.sharedMaterial?.name == "Native Concede red")
                && delete.m_ButtonText.TextColor.r == 1f && GameMenu.Get().m_redButtonMaterial.renderQueue == 3100,
                "Delete uses the native Concede material and white caption without changing the game's shared material");
            var flavor = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboFlavorPane").gameObject;
            var pageField = typeof(ComboPopup).GetField("m_page", BindingFlags.NonPublic | BindingFlags.Instance);
            int pages = ComboInspectLayout.Arrange(Screen.width, Screen.height, cards.Count, 0, true).TotalPages;
            Check(pages > 1, "Many combo components reserve a flavor pane and paginate at a readable card size");
            var stableNext = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboNextArrow");
            var face = NativeArrowFace(stableNext.gameObject);
            Rect beforePageChange = ComboSceneGeometry.ProjectBounds(face.bounds, CameraUtils.Camera);
            var hover = new GameObject("Oversized native arrow hover glow"); hover.transform.SetParent(stableNext, false);
            hover.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(new Vector3(.2f, 0, 0), new Vector3(1.2f, .02f, 1.4f)) };
            hover.AddComponent<MeshRenderer>(); hover.SetActive(false);
            stableNext.GetComponent<UIBHighlight>().m_MouseOverHighlight = hover;
            stableNext.GetComponent<PegUIElement>().Send(UIEventType.ROLLOVER);
            stableNext.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
            stableNext.GetComponent<PegUIElement>().Send(UIEventType.RELEASE);
            Rect afterPageChange = ComboSceneGeometry.ProjectBounds(face.bounds, CameraUtils.Camera);
            Check(Math.Abs(beforePageChange.xMin - afterPageChange.xMin) < .1f && Math.Abs(beforePageChange.yMin - afterPageChange.yMin) < .1f
                && Math.Abs(beforePageChange.width - afterPageChange.width) < .1f && Math.Abs(beforePageChange.height - afterPageChange.height) < .1f,
                "The arrow face stays fixed when hovering and switching from one visible arrow to two");
            hover.SetActive(false); UnityEngine.Object.Destroy(hover);
            popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboPreviousArrow")
                .GetComponent<PegUIElement>().Send(UIEventType.RELEASE);
            for (int page = 0; page < pages; page++)
            {
                var layout = ComboInspectLayout.Arrange(Screen.width, Screen.height, cards.Count, page, true);
                Check((int)pageField.GetValue(popup) == page && flavor.activeInHierarchy,
                    "The same flavor pane remains visible on every inspect page, including with empty text");
                var actions = popup.Buttons.Select(button => { ComboSceneGeometry.ScreenBounds(button.gameObject, CameraUtils.Camera, out Rect rect); return rect; }).ToArray();
                Check(Math.Abs((actions[0].center.x + actions[actions.Length - 1].center.x) * .5f - layout.ActionCenter) < 1f,
                    "Actions center under the middle item or middle pair, counting the flavor pane");
                var next = popup.GetComponentsInChildren<Transform>(true).Single(part => part.gameObject.name == "BCM_ComboNextArrow");
                Check(next.gameObject.activeInHierarchy == (page + 1 < pages), "Next is visible exactly while another combo page exists");
                if (page + 1 < pages)
                {
                    var triangle = next.GetComponent<UIBButton>();
                    var feedback = triangle.GetComponent<UIBHighlight>();
                    Check(feedback.m_MouseOverSound == "" && feedback.m_MouseOutSound == "" && feedback.m_MouseDownSound == ""
                        && feedback.m_MouseUpSound == "" && feedback.m_ReleaseDragSound == "",
                        "Detached arrows retain visual feedback without native metallic hover, press, release or drag sounds");
                    Check(triangle.m_ButtonText.gameObject.activeInHierarchy && triangle.m_RootObject.activeSelf && !triangle.m_ButtonText.isHidden(),
                        "Native triangle artwork stays visible when caption and press root are shared or separate");
                    Check(next.GetComponent<UIBButton>() != null && next.GetComponent<PagingArrow>() == null
                        && next.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.sharedMaterials.Any(material => material.name == "Native hero right triangle"))
                        && next.localScale.x > 0 && next.localScale.y > 0 && next.localScale.z > 0,
                        "Inspect clones the hero-picker triangle without mirroring or book-arrow artwork");
                    Check(ComboSceneGeometry.ScreenBounds(next.gameObject, CameraUtils.Camera, out Rect arrowArea) && arrowArea.width > 10f && arrowArea.height > 10f,
                        "The native arrow presents a visible face at a usable size");
                    Check(Math.Abs(arrowArea.center.y - layout.Cards[0].center.y) < 1f && arrowArea.xMin >= layout.Panel.xMax
                        && arrowArea.xMax <= Screen.width, "Triangle arrows flank the card row at its vertical center, inside the viewport");
                    Check(arrowArea.width > layout.Font * 9f,
                        "Desktop triangle arrows use the requested threefold width rather than the previous 3.5-font box");
                    next.GetComponent<PegUIElement>().Send(UIEventType.RELEASE);
                }
            }
            var previous = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboPreviousArrow");
            Check(previous.GetComponentsInChildren<Renderer>(false).Any(renderer => renderer.sharedMaterials.Any(material => material.name == "Native hero left triangle")),
                "The previous arrow uses the installed left triangle rather than reversing the right one");
            Check(previous.rotation.Equals(CameraUtils.Camera.transform.rotation * new Quaternion(-.70710677f, 0, 0, .70710677f) * new Quaternion(0, 1, 0, 0)),
                "The left triangle retains its nested-anchor rotation even when its font and mesh use different planes");
            previous.GetComponent<PegUIElement>().Send(UIEventType.RELEASE);
            Check((int)pageField.GetValue(popup) == pages - 2 && flavor.activeInHierarchy, "The native previous arrow pages back without hiding flavor text");
            for (int reopen = 0; reopen < 4; reopen++)
            {
                var oldArrow = popup.GetComponentsInChildren<Transform>(true).Single(part => part.gameObject.name == "BCM_ComboNextArrow");
                oldArrow.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
                ComboPopup.Close();
                Check(!oldArrow.gameObject.activeInHierarchy, "Closing inspect immediately removes old arrow hit targets, even during a press");
                ComboPopup.Inspect(combo); popup = (ComboPopup)ButtonListMenu.Visible;
                var arrow = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboNextArrow");
                var button = arrow.GetComponent<UIBButton>();
                Check(arrow != oldArrow && button.IsEnabled() && button.GetComponent<BoxCollider>().enabled,
                    "Reopening inspect creates an enabled arrow and hitbox with fresh native input state");
                Quaternion beforeHover = button.m_RootObject.transform.localRotation;
                button.Hover();
                Check(button.m_RootObject.transform.localRotation.Equals(beforeHover), "Arrow hover cannot punch the native face into a blocked orientation");
                int sounds = SoundManager.Get().Played.Count;
                button.Send(UIEventType.PRESS); button.Send(UIEventType.RELEASE);
                Check((int)pageField.GetValue(popup) == 1 && SoundManager.Get().Played.Count == sounds + 1 && popup.IsShown(),
                    "Paging after repeated close-and-reopen advances once with a single click");
            }
            ComboPopup.Close(); Screen.width = 2560; Screen.height = 1440;
            combo.Cards = cards.Take(5).ToArray();
            ComboPopup.Inspect(combo); popup = (ComboPopup)ButtonListMenu.Visible;
            var firstField = typeof(ComboPopup).GetField("m_firstCard", BindingFlags.Instance | BindingFlags.NonPublic);
            var actorsField = typeof(ComboPopup).GetField("m_actors", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var step in new[] { ("Next", 2), ("Next", 3), ("Previous", 1), ("Next", 3), ("Previous", 1), ("Previous", 0) })
            {
                var arrow = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_Combo" + step.Item1 + "Arrow");
                arrow.GetComponent<PegUIElement>().Send(UIEventType.PRESS);
                arrow.GetComponent<PegUIElement>().Send(UIEventType.RELEASE);
                var actors = (List<Actor>)actorsField.GetValue(popup);
                Check((int)firstField.GetValue(popup) == step.Item2 && actors.Count == 2
                    && actors.Select(actor => actor.GetEntityDef().GetCardId()).SequenceEqual(combo.Cards.Skip(step.Item2).Take(2).Select(card => card.Id)),
                    "Paging moves a full pair in either direction and clamps only at the first or last card: " + step);
            }
        }
        finally
        {
            ComboPopup.Close(); Screen.width = width; Screen.height = height;
            book.m_pageLeftArrowBone.transform.rotation = previousLeft; book.m_pageRightArrowBone.transform.rotation = previousRight;
        }
    }
    private static void CheckBookArrowHoverStability(ComboNativeArrow arrow, Camera camera)
    {
        var native = arrow.Root.GetComponent<PagingArrow>();
        var outline = native.m_pagingArrowHighlight;
        var outlineMesh = outline.AddComponent<MeshFilter>();
        outlineMesh.sharedMesh = new Mesh { bounds = new Bounds(new Vector3(.18f, 0, .1f), new Vector3(1.2f, .02f, 1.6f)) };
        var outlineRenderer = outline.AddComponent<MeshRenderer>();
        var input = arrow.Root.GetComponent<PegUIElement>();
        var hit = arrow.Root.GetComponent<BoxCollider>();
        var face = arrow.Root.GetComponentsInChildren<Renderer>(false).Single(renderer => renderer.enabled);
        Rect target = new Rect(100, 100, 100, 100);
        try
        {
            arrow.Layout(camera, target, 10f, true);
            Rect resting = ComboSceneGeometry.ProjectBounds(face.bounds, camera);
            Vector3 center = hit.center, size = hit.size, scale = arrow.Root.transform.localScale;
            Vector3 inside = new Vector3(resting.xMax - .25f, resting.center.y, 0);
            Vector3 outside = new Vector3(resting.xMax + .25f, resting.center.y, 0);
            for (int frame = 0; frame < 12; frame++)
            {
                bool hover = frame % 2 == 0;
                input.Send(hover ? UIEventType.ROLLOVER : UIEventType.ROLLOUT);
                arrow.Layout(camera, target, 10f, true);
                Rect visible = ComboSceneGeometry.ProjectBounds(face.bounds, camera);
                Check(outline.activeInHierarchy == hover && Math.Abs(visible.xMin - resting.xMin) < .01f
                    && Math.Abs(visible.yMin - resting.yMin) < .01f && Math.Abs(visible.width - resting.width) < .01f
                    && Math.Abs(visible.height - resting.height) < .01f && arrow.Root.transform.localScale.Equals(scale),
                    "The book Back arrow keeps its resting position and size when its offset hover outline toggles");
                Check(hit.center.Equals(center) && hit.size.Equals(size) && arrow.Contains(camera, inside)
                    && !arrow.Contains(camera, outside) && !float.IsInfinity(ArrowHitDistance(hit, camera.ScreenPointToRay(inside))),
                    "Hovering at the arrow edge preserves both the native collider and screen-space input area");
            }
        }
        finally
        {
            input.Send(UIEventType.ROLLOUT);
            UnityEngine.Object.Destroy(outlineRenderer); UnityEngine.Object.Destroy(outlineMesh.sharedMesh);
            UnityEngine.Object.Destroy(outlineMesh);
        }
    }

    private static Renderer NativeArrowFace(GameObject root) => root.GetComponentsInChildren<Renderer>(false)
        .Single(renderer => renderer.enabled && renderer.sharedMaterials.Any(material => material != null && material.name.StartsWith("Native hero ")));
    // Native PegUI.TryGetPegUIElementFromHit checks only the actual hit object
    // and then its proxy. It does not walk up the transform hierarchy.
    private static PegUIElement RouteArrowHit(GameObject target, UIEventType type)
    {
        var owner = target.GetComponent<PegUIElement>() ?? target.GetComponent<PegUIElementProxy>()?.Owner;
        if (owner != null && owner.IsEnabled()) owner.Send(type);
        return owner;
    }
    private static float ArrowHitDistance(BoxCollider box, Ray ray)
    {
        Vector3 origin = box.transform.InverseTransformPoint(ray.origin);
        Vector3 direction = box.transform.InverseTransformPoint(ray.origin + ray.direction) - origin;
        float[] o = { origin.x, origin.y, origin.z }, d = { direction.x, direction.y, direction.z };
        Vector3 low = box.center - box.size * .5f, high = box.center + box.size * .5f;
        float[] min = { low.x, low.y, low.z }, max = { high.x, high.y, high.z };
        float near = 0f, far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(d[axis]) < .000001f)
            { if (o[axis] < min[axis] || o[axis] > max[axis]) return float.PositiveInfinity; continue; }
            float a = (min[axis] - o[axis]) / d[axis], b = (max[axis] - o[axis]) / d[axis];
            near = Math.Max(near, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b));
            if (near > far) return float.PositiveInfinity;
        }
        return near;
    }
    private static void CheckArrowPressMotion(ComboNativeArrow arrow, Camera camera)
    {
        var input = arrow.Root.GetComponent<PegUIElement>();
        var button = arrow.Root.GetComponentsInChildren<UIBButton>(true).Single();
        Transform artwork = button.m_RootObject.transform;
        Vector3 rest = artwork.localPosition, artworkScale = artwork.localScale;
        if (button.Template != null)
            Check(button.m_DepressTime > 0 && button.m_RaiseTime > 0
                && (button.Template.m_DepressTime <= 0 || button.m_DepressTime == button.Template.m_DepressTime)
                && (button.Template.m_RaiseTime <= 0 || button.m_RaiseTime == button.Template.m_RaiseTime),
                "Configured native tween durations are preserved and unused zero durations get native button defaults");
        var hit = arrow.Root.GetComponent<BoxCollider>();
        Vector3 hitCenter = hit.center, hitSize = hit.size, rootScale = arrow.Root.transform.localScale;
        ComboSceneGeometry.ScreenBounds(arrow.Root, camera, out Rect before);
        Vector3 edge = new Vector3(before.xMin + 1f, before.center.y, 0);
        int sounds = SoundManager.Get().Played.Count, depresses = button.NativeDepressCalls, raises = button.NativeRaiseCalls; Time.frameCount++;
        var blocker = new GameObject("Native modal blocker behind the arrow hit surface");
        try
        {
            blocker.transform.SetParent(arrow.Root.transform, false);
            var blockedHit = blocker.AddComponent<BoxCollider>();
            blockedHit.center = new Vector3(0, .025f, 0); blockedHit.size = new Vector3(3, .002f, 3);
            blocker.AddComponent<PegUIElement>().SetEnabled(true, true);
            Ray ray = camera.ScreenPointToRay(before.center);
            var firstHit = ArrowHitDistance(hit, ray) < ArrowHitDistance(blockedHit, ray) ? hit : blockedHit;
            var feedback = input.GetComponent<UIBHighlight>(); int hovers = feedback?.HoverCalls ?? 0;
            Check(RouteArrowHit(firstHit.gameObject, UIEventType.ROLLOVER) == input
                && (feedback == null || feedback.HoverCalls == hovers + 1),
                "The actual nearest hit reaches the arrow's native hover before the modal blocker");
            Check(RouteArrowHit(firstHit.gameObject, UIEventType.PRESS) == input,
                "The native hit path delivers the arrow press rather than testing its callback directly");
            var graphic = arrow.Root.GetComponentsInChildren<Renderer>(false).First(renderer => renderer.enabled).gameObject;
            Check((graphic.GetComponent<PegUIElement>() ?? graphic.GetComponent<PegUIElementProxy>()?.Owner) == input,
                "Hits on separated arrow graphics resolve to the same input owner without parent lookup");
        }
        finally { UnityEngine.Object.Destroy(blocker); }
        Vector3 target = rest + button.m_ClickDownOffset;
        Check(button.NativeDepressCalls == depresses + 1 && iTween.LastMove.Object == button.m_RootObject
            && iTween.LastMove.Target.Equals(target) && iTween.LastMove.Time == button.m_DepressTime,
            "Each arrow starts the real native button press path once, using its local-position tween parameters");
        Time.unscaledTime += button.m_DepressTime * .5f; iTween.Advance();
        Check(!artwork.localPosition.Equals(rest) && !artwork.localPosition.Equals(target) && artwork.localScale.Equals(artworkScale),
            "The native tween moves the artwork between its resting and pressed positions without a mod scale animation or Update");
        Time.unscaledTime += button.m_DepressTime * .6f; iTween.Advance();
        Check((artwork.localPosition - target).sqrMagnitude < .000001f, "Native press reaches the configured offset in the UI plane");
        ComboSceneGeometry.ScreenBounds(arrow.Root, camera, out Rect pressed);
        Check(before.center.y - pressed.center.y >= 1.5f && Math.Abs(before.center.x - pressed.center.x) < .1f
            && Math.Abs(before.width - pressed.width) < .1f && Math.Abs(before.height - pressed.height) < .1f,
            "The native press is visibly downward on an orthographic UI camera, with no scaling or sideways jump");
        Check(arrow.Root.GetComponentsInChildren<Collider>(true).All(collider => collider == hit || !collider.enabled),
            "Only the fixed root hitbox receives input while collider-bearing artwork animates");
        Check(hit.center.Equals(hitCenter) && hit.size.Equals(hitSize) && arrow.Root.transform.localScale.Equals(rootScale)
            && arrow.Contains(camera, edge), "Arrow press feedback leaves the full-size hitbox and its edge clickable");
        arrow.Layout(camera, new Rect(100, 100, before.width / .985f, before.height / .985f), 10f, true);
        Check((artwork.localPosition - target).sqrMagnitude < .000001f && artwork.localScale.Equals(artworkScale),
            "Layout measures the resting arrow and preserves the current native animation position");
        Check(RouteArrowHit(hit.gameObject, UIEventType.RELEASE) == input, "The release hit resolves to the same arrow owner as the press");
        input.Send(UIEventType.RELEASEALL);
        Check(button.NativeRaiseCalls == raises + 1 && iTween.LastMove.Target.Equals(rest) && iTween.LastMove.Time == button.m_RaiseTime,
            "Release uses the native return tween exactly once");
        Time.unscaledTime += button.m_RaiseTime + .01f; iTween.Advance(); arrow.Update();
        ComboSceneGeometry.ScreenBounds(arrow.Root, camera, out Rect after);
        Check((artwork.localPosition - rest).sqrMagnitude < .000001f && Math.Abs(after.width - before.width) < .1f && SoundManager.Get().Played.Count == sounds + 1,
            "Release restores the arrow without replaying its click sound");
        Time.frameCount++; input.Send(UIEventType.PRESS); Time.unscaledTime += button.m_DepressTime + .01f; iTween.Advance();
        input.Send(UIEventType.ROLLOUT); Time.unscaledTime += button.m_RaiseTime + .01f; iTween.Advance(); arrow.Update();
        Check((artwork.localPosition - rest).sqrMagnitude < .000001f, "Dragging off a pressed arrow lets the native return animation finish without a click");
        Time.frameCount++; input.Send(UIEventType.PRESS); Time.unscaledTime += button.m_DepressTime + .01f; iTween.Advance();
        arrow.Root.SetActive(false); arrow.Update(); arrow.Layout(camera, new Rect(100, 100, before.width / .985f, before.height / .985f), 10f, true);
        Check((artwork.localPosition - rest).sqrMagnitude < .000001f && !iTween.IsAnimating(button.m_RootObject)
            && button.m_RaiseTime > 0, "Hiding a pressed arrow clears its native press and tween while preserving its authored return duration");
    }
}
