using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using Blizzard.T5.AssetManager;
using UnityEngine;
using Assets;

namespace BetterCollectionManager
{
    internal sealed class ComboNativeArrow : IDisposable
    {
        internal sealed class Template : IDisposable
        {
            private readonly List<AssetHandle<GameObject>> m_assets = new List<AssetHandle<GameObject>>();
            internal UIBButton Left, Right;
            private Quaternion m_leftBasis, m_rightBasis;
            internal Quaternion LeftRotation => Quaternion.Inverse(m_rightBasis) * m_leftBasis;
            private GameObject Load(string path)
            {
                var asset = AssetLoader.Get().LoadAsset<GameObject>(path); m_assets.Add(asset); return asset?.Asset;
            }
            private static object Field(object source, string name) => source?.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(source);
            private UIBButton Arrow(CardListPanel panel, bool left)
            {
                string side = left ? "left" : "right";
                var nested = Field(panel, "m_" + side + "ArrowNested") as NestedPrefab;
                UIBButton button;
                Quaternion basis;
                if (!string.IsNullOrEmpty(nested?.m_Prefab))
                {
                    button = Load(nested.m_Prefab)?.GetComponentInChildren<UIBButton>(true);
                    basis = button != null ? nested.transform.rotation * button.transform.rotation : Quaternion.identity;
                }
                else
                {
                    button = Field(panel, "m_" + side + "Arrow") as UIBButton;
                    basis = button != null ? button.transform.rotation : Quaternion.identity;
                }
                if (left) m_leftBasis = basis; else m_rightBasis = basis;
                return button;
            }
            internal Template()
            {
                try
                {
                    foreach (CardListPanel panel in Resources.FindObjectsOfTypeAll<CardListPanel>())
                    {
                        Left = Arrow(panel, true); Right = Arrow(panel, false);
                        if (Left != null && Right != null) return;
                    }
                    // Read the installed client's dialog mapping without showing
                    // the hero picker or attaching any of its native callbacks.
                    var mappings = Field(DialogManager.Get(), "m_typeMapping") as IEnumerable;
                    if (mappings != null)
                        foreach (object mapping in mappings)
                        {
                            if (Field(mapping, "m_type")?.ToString() != "CARD_LIST") continue;
                            GameObject popup = Load(Field(mapping, "m_prefabName") as string);
                            CardListPanel panel = popup?.GetComponentInChildren<CardListPanel>(true);
                            if (panel == null) continue;
                            Left = Arrow(panel, true); Right = Arrow(panel, false);
                            if (Left != null && Right != null) return;
                        }
                    throw new InvalidOperationException("Native hero-picker arrows are unavailable.");
                }
                catch { Dispose(); throw; }
            }
            public void Dispose() { foreach (var asset in m_assets) asset?.Dispose(); m_assets.Clear(); }
        }
        internal readonly GameObject Root;
        private readonly PagingArrow m_arrow;
        private readonly UIBButton m_pickerButton;
        private MeshFilter m_pickerFace;
        private Renderer m_replacedFace;
        private Bounds m_faceBounds;
        private readonly BoxCollider m_hit;
        private readonly Bounds m_nativeHitBounds;
        private readonly Quaternion m_pickerRotation;
        private Vector3 m_pickerNormal;
        private readonly List<Material> m_materials = new List<Material>();
        private readonly bool m_left;
        private readonly bool m_highlightIsArtwork;
        private float m_flashUntil;
        private readonly UIBButton m_pressButton;
        private readonly Vector3 m_pressRestPosition;
        private bool m_pressNeedsReset;
        private static float s_defaultDepressTime, s_defaultRaiseTime;
        private static readonly MethodInfo s_depress = typeof(UIBButton).GetMethod("Depress", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo s_raise = typeof(UIBButton).GetMethod("Raise", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo s_stopTween = HarmonyLib.AccessTools.TypeByName("iTween")?.GetMethod("StopByName",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject), typeof(string) }, null);
        private Rect m_hitArea;
        internal ComboNativeArrow(Transform parent, bool left, Action click, string pressSound = null, Template template = null)
        {
            m_left = left;
            if (template != null)
            {
                var staging = new GameObject("BCM_ArrowStaging"); staging.SetActive(false);
                try
                {
                    Root = UnityEngine.Object.Instantiate((left ? template.Left : template.Right).gameObject, staging.transform, false);
                    Root.SetActive(false); Root.transform.SetParent(parent, false);
                }
                finally { UnityEngine.Object.Destroy(staging); }
                m_pickerButton = Root.GetComponent<UIBButton>();
                // A detached arrow must not inherit the picker button's flip
                // or punch state when its owner is hidden and shown again.
                m_pickerButton.m_WiggleAmount = Vector3.zero; m_pickerButton.m_WiggleTime = 0f;
                m_pickerButton.m_DepressOnOver = false;
                m_pickerButton.m_AnimateFlip = false; m_pickerButton.m_WigglePostFlip = false;
                m_pickerButton.m_HoldDepressionOnRelease = false;
                m_pickerRotation = left ? template.LeftRotation : Quaternion.identity;
                foreach (PegUIElement peg in Root.GetComponentsInChildren<PegUIElement>(true)) peg.ClearEventListeners();
                FilterButtonFeedback.RestoreNativeListeners(m_pickerButton);
                // Some arrows draw on the caption object itself.
                m_pickerButton.m_ButtonText?.Show();
            }
            else Root = AssetLoader.Get().InstantiatePrefab("PagingArrow.prefab:70d4430ff418d6d42a943e77dc98d523", AssetLoadingOptions.IgnorePrefabPosition);
            Root.name = left ? "BCM_ComboPreviousArrow" : "BCM_ComboNextArrow";
            Root.transform.SetParent(parent, false);
            m_arrow = Root.GetComponent<PagingArrow>();
            m_arrow?.HideHighlight();
            // Some native book assets contain only the hover overlay: the
            // ordinary arrow is baked into the collection book itself.
            // A detached inspect arrow must keep that overlay visible.
            bool hasArtwork = false;
            foreach (Renderer renderer in Root.GetComponentsInChildren<Renderer>(false))
                hasArtwork |= renderer.enabled;
            m_highlightIsArtwork = !hasArtwork && m_arrow != null;
            if (m_highlightIsArtwork) m_arrow.ShowHighlight();
            CaptureFace();
            PegUIElement[] inputs = Root.GetComponentsInChildren<PegUIElement>(true);
            bool nativeInput = inputs.Length > 0;
            if (!nativeInput && Root.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                var collider = Root.AddComponent<BoxCollider>();
                if (Root.GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    Bounds bounds = FilterTrayGeometry.TransformBounds(Root.GetComponentsInChildren<Renderer>(true)[0].bounds, Root.transform.InverseTransformPoint);
                    collider.center = bounds.center; collider.size = bounds.size + new Vector3(0, .1f, 0);
                }
            }
            PegUIElement input = nativeInput ? inputs[0] : Root.AddComponent<PegUIElement>();
            input.SetEnabled(true, true);
            m_hit = Root.GetComponent<BoxCollider>() ?? Root.AddComponent<BoxCollider>();
            m_nativeHitBounds = new Bounds(m_hit.center, m_hit.size);
            foreach (Collider hit in Root.GetComponentsInChildren<Collider>(true))
                if (hit != m_hit) hit.enabled = false;
            m_hit.enabled = true;
            if (m_pickerFace != null && m_pickerFace.gameObject == Root)
            {
                // Some native arrows draw directly on their input object.
                // Give only that mesh a movable child; input stays on Root.
                var face = new GameObject("BCM_ArrowFace"); face.transform.SetParent(Root.transform, false);
                var source = Root.GetComponent<Renderer>();
                var mesh = face.AddComponent<MeshFilter>(); mesh.sharedMesh = m_pickerFace.sharedMesh;
                var renderer = face.AddComponent<MeshRenderer>(); renderer.sharedMaterials = source.sharedMaterials;
                renderer.sortingLayerID = source.sortingLayerID; renderer.sortingOrder = source.sortingOrder;
                source.enabled = false; m_replacedFace = source; m_pickerFace = mesh;
            }
            if (s_depress == null || s_raise == null || s_stopTween == null)
                throw new MissingMethodException("Native arrow button animation is unavailable.");
            m_pressButton = m_pickerButton ?? CreateBookPressButton();
            // Picker arrows can render through their caption/mesh without a
            // UIBButton artwork root. Supply one before enabling native motion.
            if (m_pressButton.m_RootObject == null || m_pressButton.m_RootObject == Root
                || (m_pickerFace != null && !m_pickerFace.transform.IsChildOf(m_pressButton.m_RootObject.transform)))
                m_pressButton.m_RootObject = CreatePressArtwork();
            EnsureNativePressTiming();
            m_pressRestPosition = m_pressButton.m_RootObject.transform.localPosition;
            foreach (UIBHighlight highlight in Root.GetComponentsInChildren<UIBHighlight>(true))
            {
                highlight.m_MouseOverSound = highlight.m_MouseOutSound = "";
                highlight.m_MouseDownSound = highlight.m_MouseUpSound = highlight.m_ReleaseDragSound = "";
            }
            FilterButtonFeedback.BindPressSound(input, pressSound ?? CollectionFilterMenu.ButtonPressSound);
            input.AddEventListener(UIEventType.ROLLOVER, _ => m_arrow?.ShowHighlight());
            input.AddEventListener(UIEventType.PRESS, _ => {
                // UIBButton guards its native press state, so an input button
                // which already ran OnPress does not start a second tween.
                m_pressNeedsReset = true;
                s_depress.Invoke(m_pressButton, null); m_arrow?.ShowHighlight();
            });
            input.AddEventListener(UIEventType.ROLLOUT, _ => CancelPress());
            input.AddEventListener(UIEventType.RELEASEALL, _ => CancelPress());
            input.AddEventListener(UIEventType.RELEASE, _ => {
                s_raise.Invoke(m_pressButton, null);
                m_arrow?.ShowHighlight(); m_flashUntil = Time.unscaledTime + .12f;
                click();
            });
            foreach (Renderer renderer in Root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null) { var copy = new Material(materials[i]); copy.renderQueue = Math.Max(copy.renderQueue, 3700); materials[i] = copy; m_materials.Add(copy); }
                renderer.sharedMaterials = materials;
            }
            // PegUI resolves the exact hit object, not its parents. Native
            // artwork (including newly separated meshes) must lead back to
            // this arrow's input owner for both hover and click dispatch.
            foreach (Transform part in Root.GetComponentsInChildren<Transform>(true))
                if (part != Root.transform && part.GetComponent<PegUIElement>() == null)
                {
                    var proxy = part.GetComponent<PegUIElementProxy>() ?? part.gameObject.AddComponent<PegUIElementProxy>();
                    proxy.Owner = input;
                }
            LayerUtils.SetLayer(Root.transform, GameLayer.HighPriorityUI);
            Root.SetActive(false);
        }
        internal void Layout(Camera camera, Rect area, float depth, bool visible, Quaternion? orientation = null)
        {
            Root.SetActive(visible);
            if (m_replacedFace != null) m_replacedFace.enabled = false;
            if (!visible) { ResetPress(); return; }
            Transform artwork = m_pressButton.m_RootObject.transform;
            Vector3 animatedPosition = artwork.localPosition;
            artwork.localPosition = m_pressRestPosition;
            try { LayoutCore(camera, area, depth, orientation); }
            finally { artwork.localPosition = animatedPosition; }
        }
        private void LayoutCore(Camera camera, Rect area, float depth, Quaternion? orientation)
        {
            if (m_pickerButton != null)
            {
                MeshFilter mesh = m_pickerFace;
                if (mesh != null && mesh.sharedMesh != null)
                {
                    Bounds bounds = FilterTrayGeometry.TransformBounds(m_faceBounds,
                        point => Root.transform.InverseTransformPoint(mesh.transform.TransformPoint(point)));
                    Quaternion facing = bounds.size.x < bounds.size.y && bounds.size.x < bounds.size.z
                        ? new Quaternion(0, m_pickerNormal.x < 0 ? -.70710677f : .70710677f, 0, .70710677f)
                        : bounds.size.y < bounds.size.z ? new Quaternion(m_pickerNormal.y > 0 ? -.70710677f : .70710677f, 0, 0, .70710677f)
                        : m_pickerNormal.z > 0 ? new Quaternion(1, 0, 0, 0) : Quaternion.identity;
                    Root.transform.rotation = camera.transform.rotation * facing;
                }
                else if (m_pickerButton.m_ButtonText != null) ComboSceneGeometry.FaceCaption(Root.transform, m_pickerButton.m_ButtonText, camera);
                // Left/right can share a prefab and differ only in the native
                // nested anchor's rotation. Preserve that authored direction.
                Root.transform.rotation = Root.transform.rotation * m_pickerRotation;
            }
            else if (!orientation.HasValue)
            {
                var book = CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager() as BookPageManager;
                Transform bone = (m_left ? book?.m_pageLeftArrowBone : book?.m_pageRightArrowBone)?.transform;
                Camera source = bone != null ? CameraUtils.FindProjectionCameraForObject(bone.gameObject) : null;
                if (source != null)
                    orientation = camera.transform.rotation * Quaternion.Inverse(source.transform.rotation) * bone.rotation;
            }
            if (m_pickerButton == null)
                Root.transform.rotation = orientation ?? camera.transform.rotation * new Quaternion(.70710677f, 0, 0, .70710677f);
            // Mirroring a one-sided native mesh reverses its winding and can
            // cull the entire arrow. Use the book's actual arrow orientation.
            if (m_pickerButton == null && !orientation.HasValue && m_left)
                Root.transform.rotation = Root.transform.rotation * new Quaternion(0, 1, 0, 0);
            Root.transform.localScale = Vector3.one;
            float margin = Math.Max(.1f, (camera.farClipPlane - camera.nearClipPlane) * .01f);
            depth = Mathf.Clamp(depth, camera.nearClipPlane + margin, camera.farClipPlane - margin);
            Root.transform.position = camera.ScreenToWorldPoint(new Vector3(area.center.x, area.center.y, depth));
            if (m_pickerFace == null) ComboSceneGeometry.Fit(Root.transform, camera, area);
            else
                for (int pass = 0; pass < 3; pass++)
                {
                    if (!FaceBounds(camera, out Rect bounds)) break;
                    Root.transform.localScale *= Math.Min(area.width / bounds.width, area.height / bounds.height) * .985f;
                    if (!FaceBounds(camera, out bounds)) break;
                    Root.transform.position += camera.ScreenToWorldPoint(new Vector3(area.center.x, area.center.y, depth))
                        - camera.ScreenToWorldPoint(new Vector3(bounds.center.x, bounds.center.y, depth));
                }
            if (m_pickerFace != null) FaceBounds(camera, out m_hitArea);
            else ComboSceneGeometry.ScreenBounds(Root, camera, out m_hitArea);
            if (m_pickerFace != null)
            {
                Bounds hit = FilterTrayGeometry.TransformBounds(m_faceBounds,
                    p => Root.transform.InverseTransformPoint(m_pickerFace.transform.TransformPoint(p)));
                Vector3 hitCenter = hit.center, hitSize = hit.size;
                // Fit the visible plane, retaining the native depth and its
                // center. Flattening this box to the render mesh can place
                // its entire clickable surface behind the modal blocker.
                if (hitSize.x < hitSize.y && hitSize.x < hitSize.z)
                { hitCenter.x = m_nativeHitBounds.center.x; hitSize.x = Math.Max(m_nativeHitBounds.size.x, .02f); }
                else if (hitSize.y < hitSize.z)
                { hitCenter.y = m_nativeHitBounds.center.y; hitSize.y = Math.Max(m_nativeHitBounds.size.y, .02f); }
                else
                { hitCenter.z = m_nativeHitBounds.center.z; hitSize.z = Math.Max(m_nativeHitBounds.size.z, .02f); }
                m_hit.center = hitCenter; m_hit.size = hitSize;
                m_hit.enabled = true;
            }
            // These arrows face an orthographic UI camera. The usual local-Y
            // depression can point straight into the screen and be invisible.
            // Keep the native tween, but express its offset in this UI plane.
            Transform artwork = m_pressButton.m_RootObject.transform;
            Transform parent = artwork.parent;
            Vector3 center = camera.WorldToScreenPoint(artwork.position);
            Vector3 down = new Vector3(center.x, center.y - Mathf.Clamp(m_hitArea.height * .025f, 2f, 6f), center.z);
            m_pressButton.m_ClickDownOffset = parent.InverseTransformPoint(camera.ScreenToWorldPoint(down))
                - parent.InverseTransformPoint(camera.ScreenToWorldPoint(center));
        }
        private void CaptureFace()
        {
            // Layout and input use the resting face for both book and picker
            // arrows. Hover outlines must never resize the arrow or its hit box.
            MeshFilter face = m_pickerFace = FindFace();
            if (face != null) m_faceBounds = face.sharedMesh.bounds;
            if (face != null && face.sharedMesh != null)
                try
                {
                    Mesh readable = face.sharedMesh.isReadable ? face.sharedMesh : MixedPremiumMesh.GetReadableCopy(face.sharedMesh);
                    Vector3[] vertices = readable.vertices;
                    if (vertices.Length > 0)
                    {
                        m_faceBounds = new Bounds(vertices[0], Vector3.zero);
                        foreach (Vector3 vertex in vertices) m_faceBounds.Encapsulate(vertex);
                    }
                    foreach (Vector3 normal in readable.normals)
                        if (normal.sqrMagnitude > .5f)
                        {
                            m_pickerNormal = Root.transform.InverseTransformPoint(face.transform.TransformPoint(normal))
                                - Root.transform.InverseTransformPoint(face.transform.TransformPoint(Vector3.zero));
                            break;
                        }
                }
                catch (Exception ex) { DebugLog.Warning("Native arrow face normal unavailable: " + ex.Message); }
        }
        private MeshFilter FindFace()
        {
            var highlights = Root.GetComponentsInChildren<UIBHighlight>(true);
            foreach (bool allowFeedback in new[] { false, true })
            {
                MeshFilter best = null; float largest = 0;
                foreach (MeshFilter mesh in Root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Renderer renderer = mesh.GetComponent<Renderer>();
                    bool bookHighlight = m_arrow != null && m_arrow.m_pagingArrowHighlight != null
                        && mesh.transform.IsChildOf(m_arrow.m_pagingArrowHighlight.transform);
                    if (renderer == null || !renderer.enabled || mesh.sharedMesh == null
                        || (!allowFeedback && (FilterButtonFeedback.OwnsVisual(null, highlights, mesh.transform)
                            || (bookHighlight && !m_highlightIsArtwork)))) continue;
                    bool visible = true;
                    for (Transform t = mesh.transform; t != Root.transform && t != null; t = t.parent)
                        if (!t.gameObject.activeSelf) { visible = false; break; }
                    if (!visible) continue;
                    Vector3 size = mesh.sharedMesh.bounds.size;
                    float area = Math.Max(size.x * size.y, Math.Max(size.x * size.z, size.y * size.z));
                    if (area > largest) { best = mesh; largest = area; }
                }
                if (best != null) return best;
            }
            return null;
        }
        private void EnsureNativePressTiming()
        {
            if (m_pressButton.m_DepressTime > 0 && m_pressButton.m_RaiseTime > 0) return;
            if (s_defaultDepressTime <= 0 || s_defaultRaiseTime <= 0)
            {
                var defaults = new GameObject("BCM_NativePressDefaults"); defaults.SetActive(false);
                try
                {
                    // UIBButton requires Collider, which is abstract. Supply a
                    // concrete collider before adding the native component.
                    defaults.AddComponent<BoxCollider>().enabled = false;
                    var button = defaults.AddComponent<UIBButton>();
                    s_defaultDepressTime = button.m_DepressTime; s_defaultRaiseTime = button.m_RaiseTime;
                }
                finally { UnityEngine.Object.Destroy(defaults); }
            }
            if (m_pressButton.m_DepressTime <= 0) m_pressButton.m_DepressTime = s_defaultDepressTime;
            if (m_pressButton.m_RaiseTime <= 0) m_pressButton.m_RaiseTime = s_defaultRaiseTime;
        }
        private UIBButton CreateBookPressButton()
        {
            // PagingArrow supplies artwork only. Let the game's UIBButton
            // animate it with its native offset, duration, easing and iTween.
            GameObject artwork = CreatePressArtwork();
            var driver = new GameObject("BCM_NativeArrowPress"); driver.SetActive(false); driver.transform.SetParent(Root.transform, false);
            driver.AddComponent<BoxCollider>().enabled = false;
            var button = driver.AddComponent<UIBButton>(); button.m_RootObject = artwork;
            button.m_WiggleAmount = Vector3.zero; button.m_WiggleTime = 0f;
            driver.SetActive(true);
            return button;
        }
        private GameObject CreatePressArtwork()
        {
            var artwork = new GameObject("BCM_ArrowPressArtwork"); artwork.transform.SetParent(Root.transform, false);
            if (m_pickerButton != null && m_pickerButton.m_RootObject != null)
                artwork.transform.localRotation = m_pickerButton.m_RootObject.transform.localRotation;
            foreach (Transform child in Root.GetComponentsInChildren<Transform>(true))
                if (child.parent == Root.transform && child != artwork.transform
                    && child.GetComponentsInChildren<Renderer>(true).Length > 0
                    && child.GetComponentsInChildren<PegUIElement>(true).Length == 0)
                    child.SetParent(artwork.transform, true);
            if (m_pickerFace != null && !m_pickerFace.transform.IsChildOf(artwork.transform)
                && m_pickerFace.transform != Root.transform && m_pickerFace.GetComponent<PegUIElement>() == null)
                m_pickerFace.transform.SetParent(artwork.transform, true);
            return artwork;
        }
        private void CancelPress()
        {
            s_raise.Invoke(m_pressButton, null);
            if (!m_highlightIsArtwork) m_arrow?.HideHighlight();
            m_flashUntil = 0f;
        }
        private void ResetPress()
        {
            if (m_pressNeedsReset)
            {
                float raiseTime = m_pressButton.m_RaiseTime;
                try { m_pressButton.m_RaiseTime = 0f; s_raise.Invoke(m_pressButton, null); }
                finally { m_pressButton.m_RaiseTime = raiseTime; }
                s_stopTween.Invoke(null, new object[] { m_pressButton.m_RootObject, "depress" });
                m_pressButton.m_RootObject.transform.localPosition = m_pressRestPosition;
                m_pressNeedsReset = false;
            }
            m_flashUntil = 0f;
            if (!m_highlightIsArtwork) m_arrow?.HideHighlight();
        }
        private bool FaceBounds(Camera camera, out Rect area)
        {
            // Hover/press overlays change while the click dispatches a page
            // change. Measure only the native face so they cannot move or
            // shrink the arrow and its hit target during that same release.
            area = default;
            if (m_pickerFace == null || m_pickerFace.sharedMesh == null) return false;
            Bounds bounds = m_faceBounds;
            bool first = true;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 p = camera.WorldToScreenPoint(m_pickerFace.transform.TransformPoint(bounds.center
                    + new Vector3(bounds.extents.x * x, bounds.extents.y * y, bounds.extents.z * z)));
                area = first ? new Rect(p.x, p.y, 0, 0) : Rect.MinMaxRect(Math.Min(area.xMin, p.x), Math.Min(area.yMin, p.y),
                    Math.Max(area.xMax, p.x), Math.Max(area.yMax, p.y));
                first = false;
            }
            return area.width > .001f && area.height > .001f;
        }
        internal bool Contains(Camera camera, Vector3 pointer) => Root.activeInHierarchy
            && m_hitArea.Contains(pointer);
        internal void Update()
        {
            if (m_replacedFace != null) m_replacedFace.enabled = false;
            if (!Root.activeInHierarchy) { ResetPress(); return; }
            if (m_flashUntil > 0 && Time.unscaledTime >= m_flashUntil)
            { if (!m_highlightIsArtwork) m_arrow?.HideHighlight(); m_flashUntil = 0; }
        }
        public void Dispose()
        {
            ResetPress();
            Root.SetActive(false);
            foreach (Material material in m_materials) UnityEngine.Object.Destroy(material);
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
    }
}
