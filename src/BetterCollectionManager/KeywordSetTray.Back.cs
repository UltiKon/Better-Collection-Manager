using System;
using System.Collections.Generic;
using PegasusShared;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static void ResetPinnedBackInput()
        {
            RestoreBackBlockedColliders();
            s_backPressedItem = null;
            s_backNavigationPending = false;
            s_pendingPinnedAction = null;
            s_backNavigationFrame = -1;
            s_backReleaseFrame = -1;
        }

        private static SetFilterItem FindPinnedBackUnderPointer()
        {
            if (s_backLayouts.Count == 0)
                return null;
            Camera camera = ResolveUiCamera();
            if (camera == null || !camera.enabled)
                return null;
            foreach (KeyValuePair<SetFilterItem, PinnedBackLayout> entry in s_backLayouts)
            {
                SetFilterItem item = entry.Key;
                if (item == null || !item.gameObject.activeInHierarchy)
                    continue;
                if (entry.Value.ContainsPointer(camera, Input.mousePosition))
                    return item;
            }
            return null;
        }

        private static void BlockCollidersBehindBack(SetFilterItem back)
        {
            // Other UI cameras can see buttons behind the tray at the same
            // screen position. Only hits at this pointer position yield to
            // Back; hovering its header must not disable the entire tray.
            foreach (RaycastHit hit in PointerHits()) BlockBackCollider(hit.collider, back.transform);
        }

        private static void BlockBackCollider(Collider collider, Transform back)
        {
            if (collider == null || !collider.enabled || collider.transform == back
                || collider.transform.IsChildOf(back) || back.IsChildOf(collider.transform)
                || CollectionFilterMenu.IsToggleCollider(collider)
                || (s_hideArea != null && collider.transform.IsChildOf(s_hideArea.transform)))
                return;
            // ScrollBounds is also a collider, but disabling it makes the
            // native scroller see an empty viewport and hide its rows.
            foreach (PinnedBackLayout layout in s_backLayouts.Values)
            {
                if (collider == layout.Viewport)
                    return;
            }
            PegUIElement owner = collider.GetComponentInParent<PegUIElement>();
            if (owner == null || back.IsChildOf(owner.transform))
                return;
            if (!s_backBlockedColliders.ContainsKey(collider))
                s_backBlockedColliders[collider] = collider.enabled;
            collider.enabled = false;
        }

        private static void RestoreBackBlockedColliders()
        {
            foreach (KeyValuePair<Collider, bool> collider in s_backBlockedColliders)
            {
                if (collider.Key != null)
                    collider.Key.enabled = collider.Value;
            }
            s_backBlockedColliders.Clear();
        }

        /// <summary>
        /// Reserve a fixed header inside the viewport, with Back outside the
        /// scrolling contents. Native and mod trays share the same layout.
        /// </summary>
        public static SetFilterItem CreatePinnedBackItem(
            GameObject tray, Transform contentsBone, UIBScrollable scroller, FormatType format, bool controls = false, bool clearAll = false,
            int columns = 1)
        {
            Transform contents = scroller != null && scroller.ScrollObject != null
                ? scroller.ScrollObject.transform
                : contentsBone != null ? contentsBone.parent : null;
            BoxCollider viewport = scroller != null ? scroller.m_ScrollBounds : null;
            if (contents == null || viewport == null)
                throw new InvalidOperationException("Cannot position Back without the filter viewport");

            scroller.SetScrollImmediate(0f);
            scroller.ScrollValue = 0f;
            scroller.ImmediateScrollValue = 0f;
            scroller.UpdateScroll();

            // Inherit the same world scale/rotation as a regular row before
            // reparenting. Instantiating under tray directly loses Contents' scale.
            SetFilterItem item = null;
            item = CreateNavigationItem(contents, clearAll ? "Clear All" : "Back", format,
                () => { if (clearAll) RequestPinnedControl(item); else RequestPinnedBackNavigation(); });
            if (item == null)
                throw new InvalidOperationException("Could not create pinned Back button");

            item.gameObject.name = clearAll ? "BCM_PinnedClearAllFilters" : BackItemName;
            if (clearAll) s_pinnedActions[item] = ClearFilters;
            item.transform.SetParent(tray.transform, true);
            item.transform.position = contentsBone != null ? contentsBone.position : contents.position;
            SetLayerRecursive(item.gameObject, tray.layer);
            item.gameObject.SetActive(true);
            ConfigurePinnedBackChrome(item);

            try
            {
                PinnedBackLayout layout = new PinnedBackLayout(tray, scroller, contents, viewport);
                s_backLayouts[item] = layout;
                Bounds available = layout.Available;
                float rowHeight = item.Height > 0.01f ? item.Height : 0.45f;
                float padding = Mathf.Min(rowHeight * 0.12f, available.size.z * 0.02f);
                Bounds frame = GetTrayFrameBounds(tray, contents, item.transform, available);
                float headerTop = Mathf.Lerp(available.max.z, frame.max.z, 0.5f);
                List<PinnedControl> tools = PinnedControls(controls, clearAll);
                int topCount = tools.FindAll(control => !control.Detail).Count;
                columns = Math.Max(1, columns);
                int firstRowCount = (topCount - 1) % columns + 1;
                int topRows = (topCount + columns - 1) / columns;
                int Row(int index) => index >= topCount ? topRows + (index - topCount) / columns
                    : index < firstRowCount ? 0 : 1 + (index - firstRowCount) / columns;
                int Column(int index) => index >= topCount ? (index - topCount) % columns
                    : index < firstRowCount ? index : (index - firstRowCount) % columns;
                int Columns(int index) => index < firstRowCount ? firstRowCount : columns;
                Bounds Slot(int index)
                {
                    int slots = Columns(index), column = Column(index);
                    float width = available.size.x * 0.98f / slots;
                    return new Bounds(new Vector3(available.center.x - available.size.x * 0.49f + (column + .5f) * width,
                        available.center.y, available.center.z), new Vector3(width, available.size.y, available.size.z));
                }
                int GradientColumn(int index) => Columns(index) == 1 ? -1 : Column(index);
                Bounds button = PositionPinnedControl(item, layout, Slot(0), headerTop, padding, rowHeight, GradientColumn(0), Columns(0));
                float rowTop = headerTop;
                for (int next = 1; next < tools.Count; next++)
                {
                    if (Row(next) != Row(next - 1)) rowTop = button.min.z;
                    PinnedControl tool = tools[next];
                    SetFilterItem created = AddPinnedControl(tray, contents, layout, Slot(next), tool.Text, tool.Name, format,
                        tool.Action, rowTop, padding, rowHeight, GradientColumn(next), Columns(next));
                    SafeSetSelected(created, tool.Selected);
                    TrackPinnedCategoryAction(created);
                    button.Encapsulate(GetPinnedBackBounds(created));
                }

                // Reserve only the part that overlaps the list, so moving Back
                // into the frame's header does not leave a second empty row.
                float reservedHeight = Mathf.Max(0f, available.max.z - (button.min.z - padding));
                layout.Reserve(reservedHeight);
                // Native rows are already arranged; custom rows are aligned
                // when LayoutItemsLikeSets places their first heading.
                SetFilterItem first = null;
                foreach (SetFilterItem row in contents.GetComponentsInChildren<SetFilterItem>(false))
                {
                    if (tray == s_trayObject) break;
                    if (first == null || row.transform.position.z > first.transform.position.z)
                        first = row;
                }
                if (first != null)
                    layout.InsetFirstRow(GetPinnedBackBounds(first));
                return item;
            }
            catch
            {
                ReleasePinnedBackItem(item);
                throw;
            }
        }

        private static Bounds PositionPinnedControl(SetFilterItem item, PinnedBackLayout layout,
            Bounds available, float headerTop, float padding, float rowHeight, int column = -1, int columns = 1)
        {
            StretchRow(item, available.center.x, available.size.x, column, columns);
            Bounds button = GetPinnedBackBounds(item);
            // Keep the visible button on the native UI plane. Moving it
            // along world Y changes its screen height on an angled camera.
            Vector3 offset = new Vector3(
                available.center.x - button.center.x, 0f,
                headerTop - padding - button.max.z);
            item.transform.position += offset;
            button.center += offset;
            // Measure directly from the artwork into the button's space.
            // Reboxing a rotated world AABB makes the inherited hit area
            // wider than the actual texture, especially on angled UI.
            Renderer background = s_wideRows[item].FrameRenderer;
            MeshFilter face = background != null ? background.GetComponent<MeshFilter>() : null;
            Bounds hit = face != null && face.sharedMesh != null
                ? FilterTrayGeometry.TransformBounds(face.sharedMesh.bounds,
                    p => item.transform.InverseTransformPoint(face.transform.TransformPoint(p)))
                : FilterTrayGeometry.TransformBounds(button, item.transform.InverseTransformPoint);
            hit.size = new Vector3(hit.size.x * (0.952f / 0.98f),
                Mathf.Max(0.001f, rowHeight * 0.02f / Mathf.Max(Mathf.Abs(item.transform.lossyScale.y), 0.0001f)), hit.size.z);
            layout.SetButtonBounds(button, item.transform, hit);

            // Advance only the hit area along the camera ray, keeping its
            // screen position aligned with the visible button.
            Vector3 hitOffset = Vector3.zero;
            Camera camera = ResolveUiCamera();
            if (camera != null)
            {
                Vector3 towardCamera = camera.orthographic ? -camera.transform.forward
                    : (camera.transform.position - button.center).normalized;
                hitOffset = towardCamera * (rowHeight * 0.02f);
            }
            foreach (Collider inherited in item.GetComponentsInChildren<Collider>(true))
                inherited.enabled = false;
            BoxCollider hitArea = item.GetComponent<BoxCollider>() ?? item.gameObject.AddComponent<BoxCollider>();
            hitArea.center = hit.center + item.transform.InverseTransformPoint(item.transform.position + hitOffset);
            hitArea.size = hit.size;
            hitArea.enabled = true;

            return button;
        }

        private static SetFilterItem AddPinnedControl(GameObject tray, Transform contents, PinnedBackLayout owner, Bounds available,
            string label, string name, FormatType format, Action action, float top, float padding, float rowHeight, int column, int columns)
        {
            SetFilterItem item = null;
            item = CreateNavigationItem(contents, label, format, () => RequestPinnedControl(item));
            item.gameObject.name = name;
            item.transform.SetParent(tray.transform, true);
            SetLayerRecursive(item.gameObject, tray.layer);
            item.gameObject.SetActive(true);
            ConfigurePinnedBackChrome(item);
            var layout = new PinnedBackLayout(owner);
            s_backLayouts[item] = layout;
            s_pinnedActions[item] = action;
            if (name == "BCM_PinnedFilterNot") SafeSetSelected(item, NotEnabled);
            PositionPinnedControl(item, layout, available, top, padding, rowHeight, column, columns);
            return item;
        }

        private static Bounds GetPinnedBackBounds(SetFilterItem item)
        {
            // UberText can render glyphs far outside the UI before copying
            // them onto a texture. Those renderers must never determine row
            // placement, clipping or a screen-space input capture rectangle.
            if (s_wideRows.TryGetValue(item, out FilterTrayGeometry wide) && wide.FrameRenderer != null)
                return wide.FrameRenderer.bounds;
            item.m_uberText?.UpdateNow(true);
            Renderer[] text = item.m_uberText != null ? CounterTextLayout.VisibleRenderers(item.m_uberText) : null;
            using (var geometry = new FilterTrayGeometry(item.gameObject,
                item.m_uberText != null ? item.m_uberText.transform : null, text))
            {
                if (geometry.FrameRenderer != null) return geometry.FrameRenderer.bounds;
            }
            // A text-only row still occupies one ordinary row. Never fall
            // back to an unbounded font mesh or inherited oversized collider.
            return new Bounds(item.transform.position, new Vector3(0f, 0f, Mathf.Max(item.Height, 0.01f)));
        }

        private static void ConfigurePinnedBackChrome(SetFilterItem item)
        {
            // Keep the native hover/press response installed by the shared
            // factory. Only unrelated, oversized shadow artwork is suppressed.
            UIBHighlight[] highlights = item.GetComponentsInChildren<UIBHighlight>(true);
            foreach (Transform child in item.GetComponentsInChildren<Transform>(true))
            {
                if (child == item.transform || child.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0
                    || FilterButtonFeedback.OwnsVisual(item, highlights, child)) continue;
                DisablePinnedBackEffect(item, child.gameObject);
            }
        }

        private static void DisablePinnedBackEffect(SetFilterItem item, GameObject effect)
        {
            if (effect == null || effect == item.gameObject || !effect.transform.IsChildOf(item.transform))
                return;
            foreach (Renderer renderer in effect.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Collider collider in effect.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            effect.SetActive(false);
        }

        private static Bounds GetTrayFrameBounds(
            GameObject tray, Transform contents, Transform back, Bounds fallback)
        {
            Bounds bounds = fallback;
            bool found = false;
            foreach (Renderer renderer in tray.GetComponentsInChildren<Renderer>(false))
            {
                Transform t = renderer.transform;
                if (!renderer.enabled || t == contents || t.IsChildOf(contents)
                    || t == back || t.IsChildOf(back) || renderer.GetComponent<UberText>() != null)
                    continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }
            return bounds;
        }

        // Use the current transform directly: Collider.bounds can lag behind a
        // reparent/scale until physics updates. No physics synchronization API
        // is needed to measure the rectangular UI hit areas and viewport.
        private static Bounds GetBoxWorldBounds(BoxCollider box)
        {
            Bounds bounds = new Bounds(box.transform.TransformPoint(box.center), Vector3.zero);
            Vector3 halfSize = box.size * 0.5f;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = box.center + new Vector3(
                            halfSize.x * x, halfSize.y * y, halfSize.z * z);
                        bounds.Encapsulate(box.transform.TransformPoint(corner));
                    }
                }
            }
            return bounds;
        }

        // Restore the native clipping pass and row anchors before the next menu.
        public static void ReleasePinnedBackItem(SetFilterItem item)
        {
            ReleaseWideRow(item);
            if (ReferenceEquals(item, null))
                return;
            if (s_backLayouts.TryGetValue(item, out PinnedBackLayout layout))
            {
                ResetPinnedBackInput();
                var children = new List<SetFilterItem>();
                foreach (var entry in s_backLayouts) if (entry.Value.Owner == layout) children.Add(entry.Key);
                foreach (SetFilterItem child in children) ReleasePinnedBackItem(child);
                s_pinnedActions.Remove(item);
                s_backLayouts.Remove(item);
                layout.Restore();
            }
            if (item != null)
            {
                item.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(item.gameObject);
            }
        }

        private sealed class PinnedBackLayout
        {
            private readonly UIBScrollable m_scroller;
            private readonly Transform m_contents;
            private readonly CameraMask m_originalMask;
            private readonly bool m_maskWasEnabled;
            private readonly Bounds m_nativeClip;
            private readonly Dictionary<Transform, Vector3> m_childPositions =
                new Dictionary<Transform, Vector3>();
            private GameObject m_maskObject;
            private float m_clipTop;
            private Bounds m_buttonLocal;
            private Transform m_hitRoot;
            private Bounds m_hitLocal;

            public GameObject Tray { get; }
            public BoxCollider Viewport { get; }
            public PinnedBackLayout Owner { get; }
            private CameraMask m_mask;
            public CameraMask Mask => Owner != null ? Owner.Mask : m_mask;
            public Bounds Available { get; }
            public Bounds ButtonBounds => FilterTrayGeometry.TransformBounds(m_buttonLocal, Tray.transform.TransformPoint);

            public Bounds HitBounds => FilterTrayGeometry.TransformBounds(m_hitLocal, m_hitRoot.TransformPoint);

            public void SetButtonBounds(Bounds button, Transform hitRoot, Bounds hitLocal)
            {
                m_buttonLocal = FilterTrayGeometry.TransformBounds(button, Tray.transform.InverseTransformPoint);
                m_hitRoot = hitRoot;
                m_hitLocal = hitLocal;
            }

            public bool ContainsPointer(Camera camera, Vector3 screen)
            {
                Bounds hit = m_hitLocal;
                if (m_hitRoot == null || !FilterTrayGeometry.TryProjectToPlane(m_hitRoot, camera,
                    screen, hit.center.y, out Vector3 local)) return false;
                return local.x >= hit.min.x && local.x <= hit.max.x
                    && local.z >= hit.min.z && local.z <= hit.max.z;
            }

            public PinnedBackLayout(PinnedBackLayout owner)
            {
                Owner = owner; Tray = owner.Tray; Viewport = owner.Viewport; Available = owner.Available;
            }

            public PinnedBackLayout(
                GameObject tray, UIBScrollable scroller, Transform contents, BoxCollider viewport)
            {
                Tray = tray;
                Viewport = viewport;
                m_scroller = scroller;
                m_contents = contents;
                m_originalMask = tray.GetComponent<CameraMask>();
                if (m_originalMask == null)
                    throw new InvalidOperationException("Filter tray has no clipping mask");
                m_maskWasEnabled = m_originalMask.enabled;

                // The native filter rows advance along world -Z. Intersect the
                // hit-test viewport with the actual XZ camera clip rectangle.
                Bounds available = GetBoxWorldBounds(viewport);
                Vector3 center = m_originalMask.transform.position;
                Vector3 scale = m_originalMask.transform.lossyScale;
                float halfWidth = m_originalMask.m_Width * Mathf.Abs(scale.x) * 0.5f;
                float halfHeight = m_originalMask.m_Height * Mathf.Abs(scale.z) * 0.5f;
                m_nativeClip = new Bounds(center, new Vector3(halfWidth * 2f, 0f, halfHeight * 2f));
                float left = Mathf.Max(available.min.x, center.x - halfWidth);
                float right = Mathf.Min(available.max.x, center.x + halfWidth);
                float bottom = Mathf.Max(available.min.z, center.z - halfHeight);
                float top = Mathf.Min(available.max.z, center.z + halfHeight);
                if (right <= left || top <= bottom)
                    throw new InvalidOperationException("Filter viewport and clipping mask do not intersect");
                Available = new Bounds(new Vector3((left + right) * 0.5f, available.center.y,
                    (bottom + top) * 0.5f), new Vector3(right - left, available.size.y, top - bottom));
            }

            public void Reserve(float height)
            {
                if (height < 0f || height >= Available.size.z)
                    throw new InvalidOperationException("Filter viewport is too small for the Back header");
                m_clipTop = Available.max.z - height;

                // Move row anchors, not ScrollObject or its cached start position.
                // Keep vanilla visibility bounds so partial edge rows stay alive;
                // the camera mask clips their pixels behind the fixed header.
                for (int i = 0; i < m_contents.childCount; i++)
                {
                    Transform child = m_contents.GetChild(i);
                    m_childPositions[child] = child.localPosition;
                    child.position -= new Vector3(0f, 0f, height);
                }

                m_maskObject = new GameObject("BCM_FilterListMask");
                m_maskObject.SetActive(false);
                GameUtils.SetParent(m_maskObject, Tray, false);
                m_maskObject.layer = Tray.layer;
                m_maskObject.transform.localRotation = Quaternion.identity;
                m_maskObject.transform.localScale = Vector3.one;
                Vector3 sourcePosition = m_originalMask.transform.position;
                // The scroll collider governs hit tests/row lifetime, not the
                // visible tray edges. Only trim the native pixel mask at the
                // top; intersecting its bottom with that collider cuts off the
                // bottom row behind a dark band in every Back submenu.
                m_maskObject.transform.position = new Vector3(m_nativeClip.center.x, sourcePosition.y,
                    (m_clipTop + m_nativeClip.min.z) * 0.5f);
                m_mask = m_maskObject.AddComponent<CameraMask>();
                CopyCameraMaskSettings(m_originalMask, Mask);
                Vector3 scale = m_maskObject.transform.lossyScale;
                Mask.m_Width = m_nativeClip.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
                Mask.m_Height = (m_clipTop - m_nativeClip.min.z) / Mathf.Max(Mathf.Abs(scale.z), 0.0001f);
                Mask.m_ClipObjects = m_contents.gameObject;

                m_originalMask.enabled = false;
                m_maskObject.SetActive(true);
                if (Tray == s_trayObject) UpdateVisibleMenuMask(Mask);
                else Mask.UpdateMask();
            }

            public float InsetFirstRow(Bounds row)
            {
                // A header's artwork extends above its anchor. Reserving only
                // Back's overlap leaves that artwork behind Back or the mask.
                // Move the list start independently of the clipping rectangle.
                float gap = Mathf.Min(row.size.z * 0.1f, Available.size.z * 0.01f);
                float inset = Mathf.Max(0f, row.max.z + gap - m_clipTop);
                if (inset <= 0f)
                    return 0f;
                for (int i = 0; i < m_contents.childCount; i++)
                {
                    Transform child = m_contents.GetChild(i);
                    if (!m_childPositions.ContainsKey(child))
                        m_childPositions[child] = child.localPosition;
                    child.position -= new Vector3(0f, 0f, inset);
                }
                return inset;
            }

            public void Restore()
            {
                if (Owner != null) return;
                // Disable the temporary pass before restoring the native one;
                // two enabled masks must never share the same Contents subtree.
                if (Mask != null)
                    Mask.enabled = false;
                if (m_maskObject != null)
                {
                    m_maskObject.SetActive(false);
                    UnityEngine.Object.Destroy(m_maskObject);
                    m_maskObject = null;
                }
                m_mask = null;
                if (m_scroller != null && m_contents != null)
                {
                    m_scroller.SetScrollImmediate(0f);
                    m_scroller.ScrollValue = 0f;
                    m_scroller.ImmediateScrollValue = 0f;
                    m_scroller.UpdateScroll();
                }
                foreach (KeyValuePair<Transform, Vector3> child in m_childPositions)
                {
                    if (child.Key != null)
                        child.Key.localPosition = child.Value;
                }
                m_childPositions.Clear();
                if (m_originalMask != null)
                {
                    if (Tray == s_trayObject) UpdateVisibleMenuMask(m_originalMask);
                    else m_originalMask.enabled = m_maskWasEnabled;
                }
            }
        }

        private static void DestroyPinnedBackItem()
        {
            ReleasePinnedBackItem(s_backItem);
            s_backItem = null;
        }

        // Navigation controls are owned by their caller, outside s_items unless
        // explicitly added by AddItem.
        public static SetFilterItem CreateNavigationItem(
            Transform parent, string text, FormatType format, Action onClick)
        {
            GameObject go = UnityEngine.Object.Instantiate(s_itemPrefab);
            GameUtils.SetParent(go, parent.gameObject, false);
            go.SetActive(false);

            SetFilterItem item = go.GetComponent<SetFilterItem>();
            UIBScrollableItem scrollItem = go.GetComponent<UIBScrollableItem>();
            if (item != null)
            {
                item.IsHeader = false;
                CounterTextLayout.UseDirectCaption(item);
                item.Text = text;
                item.FormatType = format;
                item.ShowTooltip = false;
                item.Callback = null;
                item.EditModeOnly = false;
                AssignHeightFromScrollItem(item, scrollItem);
                ApplyGridItemScale(item, scrollItem);
                SafeSetSelected(item, false);
                SanitizeItemVisuals(item);

                PegUIElement peg = go.GetComponent<PegUIElement>();
                if (peg != null)
                {
                    peg.ClearEventListeners();
                    peg.SetEnabled(true, true);
                    FilterButtonFeedback.BindPressSound(peg);
                    FilterButtonFeedback.RestoreNativeListeners(peg);
                    peg.AddEventListener(UIEventType.PRESS, _ => RecordPinnedPress(item));
                    peg.AddEventListener(
                        UIEventType.RELEASE,
                        (UIEvent e) =>
                        {
                            ObservePointerGesture();
                            if (IsScrollbarGesture) return;
                            if (!s_backLayouts.ContainsKey(item) && ShouldSuppressFilterSelectionForBack())
                                return;
                            FilterButtonFeedback.RunFilterAction(onClick);
                        });
                }

                Collider col = go.GetComponent<Collider>();
                if (col != null)
                    col.enabled = true;
            }
            return item;
        }

    }
}
