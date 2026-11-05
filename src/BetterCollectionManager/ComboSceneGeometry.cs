using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class ComboSceneGeometry
    {
        private sealed class ArtworkSnapshot
        {
            internal GameObject Root;
            internal Matrix4x4 Model;
            internal Camera Camera;
            internal readonly List<Renderer> Renderers = new List<Renderer>();
            internal int Order, Queue;
            internal float Near, Far;
            internal void Capture(GameObject root, Camera camera)
            {
                Root = root; Camera = camera; Model = root.transform.localToWorldMatrix;
                Renderers.Clear(); Order = Queue = 0; Near = float.PositiveInfinity; Far = float.NegativeInfinity;
                foreach (Renderer renderer in VisibleRenderers(root))
                {
                    Renderers.Add(renderer);
                    if (!IsGlyphRenderer(renderer))
                    {
                        Order = Math.Max(Order, renderer.sortingOrder);
                        foreach (Material material in renderer.sharedMaterials) if (material != null) Queue = Math.Max(Queue, material.renderQueue);
                    }
                    if (RendererDepthRange(renderer, camera, out float near, out float far)) { Near = Math.Min(Near, near); Far = Math.Max(Far, far); }
                }
            }
        }
        private static readonly ArtworkSnapshot s_paperSnapshot = new ArtworkSnapshot(), s_frameSnapshot = new ArtworkSnapshot();
        private static bool s_artworkMeasurements;
        internal static void BeginArtworkMeasurements(Camera camera, GameObject paper, GameObject frame)
        {
            s_artworkMeasurements = false;
            s_paperSnapshot.Capture(paper, camera); s_frameSnapshot.Capture(frame, camera);
            s_artworkMeasurements = true;
        }
        internal static void EndArtworkMeasurements()
        {
            s_artworkMeasurements = false;
            s_paperSnapshot.Root = s_frameSnapshot.Root = null;
            s_paperSnapshot.Renderers.Clear(); s_frameSnapshot.Renderers.Clear();
        }
        private static ArtworkSnapshot Snapshot(GameObject root)
        {
            if (!s_artworkMeasurements) return null;
            ArtworkSnapshot value = root == s_paperSnapshot.Root ? s_paperSnapshot : root == s_frameSnapshot.Root ? s_frameSnapshot : null;
            // Only the two stable background hierarchies are cached, during
            // this synchronous ordering pass. Labels and actor meshes stay live.
            return value != null && value.Model.Equals(root.transform.localToWorldMatrix) ? value : null;
        }

        internal static Rect ProjectBounds(Bounds bounds, Camera camera)
        {
            Rect result = default; bool first = true;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                Vector3 p = camera.WorldToScreenPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z));
                result = first ? new Rect(p.x, p.y, 0, 0) : Rect.MinMaxRect(Math.Min(result.xMin, p.x), Math.Min(result.yMin, p.y),
                    Math.Max(result.xMax, p.x), Math.Max(result.yMax, p.y)); first = false;
            }
            return result;
        }
        internal static Rect CardSlot(CollectionCardVisual visual, Camera camera)
        {
            // The collection's input slot covers the complete card, independently
            // of an Actor's mesh choice and first-frame animation scale.
            Vector3 half = visual.m_boxColliderSize * .5f, center = visual.m_boxColliderCenter;
            Rect result = default; bool first = true;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = camera.WorldToScreenPoint(visual.transform.TransformPoint(center + new Vector3(half.x * x, half.y * y, half.z * z)));
                result = first ? new Rect(point.x, point.y, 0, 0) : Rect.MinMaxRect(Math.Min(result.xMin, point.x), Math.Min(result.yMin, point.y),
                    Math.Max(result.xMax, point.x), Math.Max(result.yMax, point.y)); first = false;
            }
            return result;
        }
        internal static bool CardFaceBounds(Actor actor, Camera camera, out Rect area)
        {
            area = default;
            Renderer face = actor != null ? actor.GetMeshRenderer() : null;
            if (face == null || !face.enabled || !face.gameObject.activeInHierarchy) return false;
            area = ProjectBounds(face.bounds, camera);
            foreach (UberText text in actor.GetComponentsInChildren<UberText>(false))
                if (!text.isHidden() && CounterTextLayout.TryScreenInkRect(text, camera, out Rect ink))
                    area = Rect.MinMaxRect(Math.Min(area.xMin, ink.xMin), Math.Min(area.yMin, ink.yMin),
                        Math.Max(area.xMax, ink.xMax), Math.Max(area.yMax, ink.yMax));
            return area.width > .001f && area.height > .001f;
        }
        internal static void FitCardFace(Actor actor, Camera camera, Rect target)
        {
            // Particle effects, cached font planes and glows can have bounds
            // far outside a hand card. They must not shrink its actual face.
            for (int pass = 0; pass < 3; pass++)
            {
                if (!CardFaceBounds(actor, camera, out Rect area)) return;
                float scale = Math.Min(target.width / area.width, target.height / area.height) * .985f;
                actor.transform.localScale *= scale;
                if (!CardFaceBounds(actor, camera, out area)) return;
                Vector3 point = camera.WorldToScreenPoint(actor.transform.position);
                actor.transform.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, point.z))
                    - camera.ScreenToWorldPoint(new Vector3(area.center.x, area.center.y, point.z));
            }
        }
        internal static void FaceCaption(Transform root, UberText caption, Camera camera)
        {
            // Align the complete button, including its icon and frame. Shop
            // templates have a different parent orientation from menu buttons.
            root.rotation = camera.transform.rotation * Quaternion.Inverse(caption.transform.rotation) * root.rotation;
        }
        internal static bool IsGlyphRenderer(Renderer renderer) => renderer.GetComponent<TextMesh>() != null
            || (renderer.GetComponentInParent<UberText>() != null && renderer.GetComponent<MeshFilter>() == null);
        internal static void TextAfterArtwork(UberText text, GameObject artwork)
        {
            ArtworkSnapshot snapshot = Snapshot(artwork);
            int order = snapshot?.Order ?? 0, queue = snapshot?.Queue ?? 0, glyphQueue = int.MaxValue;
            if (snapshot == null)
            foreach (Renderer renderer in VisibleRenderers(artwork))
            {
                if (IsGlyphRenderer(renderer)) continue;
                order = Math.Max(order, renderer.sortingOrder);
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) queue = Math.Max(queue, material.renderQueue);
            }
            foreach (Renderer renderer in CounterTextLayout.VisibleRenderers(text))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && material.renderQueue > 0) glyphQueue = Math.Min(glyphQueue, material.renderQueue);
            int increment = Math.Max(text.RenderQueue, order + 1);
            if (glyphQueue != int.MaxValue && glyphQueue <= queue)
                increment = Math.Max(increment, text.RenderQueue + queue - glyphQueue + 1);
            // Keep the native material service responsible for shared fonts.
            if (text.RenderQueue != increment)
            { text.RenderQueue = increment; text.UpdateNow(true); }
        }
        internal static IEnumerable<Renderer> VisibleRenderers(GameObject root)
        {
            ArtworkSnapshot snapshot = Snapshot(root);
            if (snapshot != null) { foreach (Renderer renderer in snapshot.Renderers) yield return renderer; yield break; }
            var found = new HashSet<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                UberText text = renderer.GetComponentInParent<UberText>();
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && (text == null || !text.RenderToTexture)
                    && found.Add(renderer)) yield return renderer;
            }
            foreach (UberText text in root.GetComponentsInChildren<UberText>(false))
                if (!text.isHidden())
                    foreach (Renderer renderer in CounterTextLayout.VisibleRenderers(text))
                        if (found.Add(renderer)) yield return renderer;
        }
        internal static bool DepthRange(GameObject root, Camera camera, out float near, out float far)
        {
            ArtworkSnapshot snapshot = Snapshot(root);
            if (snapshot != null && snapshot.Camera == camera)
            { near = snapshot.Near; far = snapshot.Far; return !float.IsInfinity(near) && !float.IsInfinity(far); }
            near = float.PositiveInfinity; far = float.NegativeInfinity;
            foreach (Renderer renderer in VisibleRenderers(root))
            {
                if (!RendererDepthRange(renderer, camera, out float partNear, out float partFar)) continue;
                near = Math.Min(near, partNear); far = Math.Max(far, partFar);
            }
            return !float.IsInfinity(near) && !float.IsInfinity(far);
        }
        private static bool RendererDepthRange(Renderer renderer, Camera camera, out float near, out float far)
        {
            near = float.PositiveInfinity; far = float.NegativeInfinity;
            Bounds bounds = renderer.bounds;
            if (bounds.size.sqrMagnitude <= .00000001f) return false;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                float depth = camera.WorldToScreenPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z)).z;
                near = Math.Min(near, depth); far = Math.Max(far, depth);
            }
            return true;
        }
        internal static void InFrontOf(GameObject foreground, GameObject background, Camera camera)
        {
            if (foreground.transform.IsChildOf(background.transform) || background.transform.IsChildOf(foreground.transform)) return;
            if (DepthRange(background, camera, out float backgroundNear, out _)
                && DepthRange(foreground, camera, out _, out float foregroundFar) && foregroundFar >= backgroundNear - .01f)
            {
                Vector3 position = camera.WorldToScreenPoint(foreground.transform.position);
                position.z -= foregroundFar - backgroundNear + .01f;
                foreground.transform.position = camera.ScreenToWorldPoint(position);
            }
            int order = int.MinValue, layer = 0;
            foreach (Renderer renderer in VisibleRenderers(background))
                if (renderer.sortingOrder > order) { order = renderer.sortingOrder; layer = renderer.sortingLayerID; }
            if (order == int.MinValue) return;
            foreach (Renderer renderer in VisibleRenderers(foreground))
            {
                renderer.sortingLayerID = layer;
                renderer.sortingOrder = Math.Max(renderer.sortingOrder, order + 1);
            }
        }
        internal static void InFrontOf(GameObject foreground, Renderer background, Camera camera)
        {
            if (foreground == null || background == null || camera == null) return;
            // Inspect only this renderer, including when it sits on a common
            // parent of the controls. Descendant controls are not background.
            if (foreground == background.gameObject || background.transform.IsChildOf(foreground.transform)) return;
            if (RendererDepthRange(background, camera, out float near, out _)
                && DepthRange(foreground, camera, out _, out float far) && far >= near - .01f)
            {
                Vector3 position = camera.WorldToScreenPoint(foreground.transform.position);
                position.z -= far - near + .01f;
                foreground.transform.position = camera.ScreenToWorldPoint(position);
            }
            foreach (Renderer renderer in VisibleRenderers(foreground))
            {
                renderer.sortingLayerID = background.sortingLayerID;
                renderer.sortingOrder = Math.Max(renderer.sortingOrder, background.sortingOrder + 1);
            }
        }
        internal static void TextInFront(GameObject root, Camera camera)
        {
            int order = 0, layer = 0;
            float near = float.PositiveInfinity;
            bool hasArtwork = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled || renderer.GetComponentInParent<UberText>() != null) continue;
                hasArtwork = true;
                if (renderer.sortingOrder >= order) { order = renderer.sortingOrder; layer = renderer.sortingLayerID; }
                if (RendererDepthRange(renderer, camera, out float depth, out _)) near = Math.Min(near, depth);
            }
            foreach (UberText text in root.GetComponentsInChildren<UberText>(false))
            {
                if (text.isHidden()) continue;
                if (!float.IsInfinity(near))
                {
                    Vector3 position = camera.WorldToScreenPoint(text.transform.position);
                    position.z = Math.Min(position.z, near - .01f);
                    text.transform.position = camera.ScreenToWorldPoint(position);
                }
                // UberText expects a native sorting increment, not a material
                // render-queue number such as 3000 or 3600.
                int increment = Math.Max(text.RenderQueue, order + 1);
                if (text.RenderQueue != increment) { text.RenderQueue = increment; text.UpdateNow(true); }
                foreach (Renderer output in CounterTextLayout.VisibleRenderers(text))
                {
                    if (hasArtwork) output.sortingLayerID = layer;
                    output.sortingOrder = Math.Max(output.sortingOrder, order + 1);
                    output.gameObject.layer = root.layer;
                }
            }
        }
        internal static bool ScreenBounds(GameObject root, Camera camera, out Rect rect)
        {
            bool found = false; rect = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                UberText text = renderer.GetComponentInParent<UberText>();
                if (text != null && text.RenderToTexture) continue; // Raw glyphs can be in an offscreen render scene.
                Bounds bounds = renderer.bounds;
                if (bounds.size.x <= .00001f && bounds.size.y <= .00001f && bounds.size.z <= .00001f) continue;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                {
                    Vector3 p = camera.WorldToScreenPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                        y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z));
                    if (!found) { rect = new Rect(p.x, p.y, 0, 0); found = true; }
                    else rect = Rect.MinMaxRect(Math.Min(rect.xMin, p.x), Math.Min(rect.yMin, p.y), Math.Max(rect.xMax, p.x), Math.Max(rect.yMax, p.y));
                }
            }
            foreach (UberText text in root.GetComponentsInChildren<UberText>(false))
                if (text.gameObject.activeInHierarchy && !text.isHidden() && CounterTextLayout.TryScreenInkRect(text, camera, out Rect ink))
                {
                    rect = found ? Rect.MinMaxRect(Math.Min(rect.xMin, ink.xMin), Math.Min(rect.yMin, ink.yMin),
                        Math.Max(rect.xMax, ink.xMax), Math.Max(rect.yMax, ink.yMax)) : ink;
                    found = true;
                }
            return found && rect.width > .001f && rect.height > .001f;
        }
        internal static void Fit(Transform root, Camera camera, Rect target)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                if (!ScreenBounds(root.gameObject, camera, out Rect bounds)) return;
                float scale = Math.Min(target.width / bounds.width, target.height / bounds.height) * .985f;
                if (Math.Abs(scale - 1f) < .0001f && Math.Abs(bounds.center.x - target.center.x) < .01f
                    && Math.Abs(bounds.center.y - target.center.y) < .01f) return;
                root.localScale *= scale;
                if (!ScreenBounds(root.gameObject, camera, out bounds)) return;
                float depth = camera.WorldToScreenPoint(root.position).z;
                root.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth))
                    - camera.ScreenToWorldPoint(new Vector3(bounds.center.x, bounds.center.y, depth));
            }
        }

        internal static void FitButtonHitArea(UIBButton button)
        {
            // Shop buttons can carry a hit box from their original menu. Fit
            // that box to the clone's artwork after layout, without moving art
            // or measuring captions, hover glows and world-space bounding boxes.
            Bounds bounds = default; bool found = false;
            UIBHighlight[] highlights = button.GetComponentsInChildren<UIBHighlight>(true);
            foreach (Renderer renderer in button.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled || renderer.GetComponentInParent<UberText>() != null
                    || IsGlyphRenderer(renderer)
                    || FilterButtonFeedback.OwnsVisual(null, highlights, renderer.transform)) continue;
                MeshFilter mesh = renderer.GetComponent<MeshFilter>();
                if (mesh == null || mesh.sharedMesh == null) continue;
                Bounds part = FilterTrayGeometry.TransformBounds(mesh.sharedMesh.bounds,
                    p => button.transform.InverseTransformPoint(renderer.transform.TransformPoint(p)));
                if (found) bounds.Encapsulate(part); else { bounds = part; found = true; }
            }
            if (!found) return;
            BoxCollider hit = button.GetComponent<BoxCollider>() ?? button.gameObject.AddComponent<BoxCollider>();
            foreach (Collider inherited in button.GetComponentsInChildren<Collider>(true))
                if (inherited != hit) inherited.enabled = false;
            hit.center = bounds.center;
            hit.size = new Vector3(Math.Max(bounds.size.x, .001f), Math.Max(bounds.size.y, .001f), Math.Max(bounds.size.z, .001f));
            hit.enabled = true;
        }
    }
}
