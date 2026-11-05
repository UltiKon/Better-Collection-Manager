using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static Vector3 s_gridOffset;
        private static int s_gridColumns = 1, s_gridRows;
        private static readonly Dictionary<SetFilterItem, FilterTrayGeometry> s_wideRows = new Dictionary<SetFilterItem, FilterTrayGeometry>();
        private sealed class RowShape
        {
            public float Width, Height, ActualHeight;
            public int Column, Columns;
        }
        private static readonly Dictionary<SetFilterItem, RowShape> s_rowShapes = new Dictionary<SetFilterItem, RowShape>();
        private static float s_gridFit = 1f, s_gridAnchorX;
        private static FilterTrayGeometry s_gridGeometry;
        private static bool s_gridDirty;
        private static int s_gridScreenWidth, s_gridScreenHeight;

        public static void SettingsChanged() => s_gridDirty = true;

        private static void LayoutItemsLikeSets()
        {
            if (s_contents == null) return;
            Vector3 origin = s_contentsBone != null ? s_contentsBone.position : s_contents.position;
            int columns = s_gridColumns;
            var heights = new float[s_items.Count];
            var headers = new bool[s_items.Count];
            for (int i = 0; i < s_items.Count; i++)
            {
                heights[i] = s_items[i] != null ? s_items[i].Height : 0.45f;
                headers[i] = IsFullWidthRow(s_items[i]);
            }
            FilterGridLayout.Cell[] cells = FilterGridLayout.Arrange(heights, headers, columns);
            Bounds viewport = s_scroller?.m_ScrollBounds != null ? GetBoxWorldBounds(s_scroller.m_ScrollBounds)
                : new Bounds(origin, new Vector3(2f * columns, 1f, 4f));
            float pitch = viewport.size.x * 0.98f / columns;
            try
            {
                s_scroller?.ClearVisibleAffectObjects();
                s_scroller?.ForceVisibleAffectedObjectsShow(false);
            }
            catch { }
            float inset = 0f;
            for (int i = 0; i < s_items.Count; i++)
            {
                SetFilterItem item = s_items[i];
                if (item == null) continue;
                item.gameObject.SetActive(true);
                item.Height = cells[i].Height;
                float x = columns == 1 ? origin.x : viewport.center.x + s_gridAnchorX
                    + (cells[i].Column < 0 ? 0f : (cells[i].Column - (columns - 1) * 0.5f) * pitch);
                item.transform.position = new Vector3(x, origin.y, origin.z - cells[i].Down - inset);
                if (IsFullWidthRow(item)) StretchFullRow(item, viewport);
                else if (columns > 1)
                {
                    float center = viewport.center.x + (cells[i].Column - (columns - 1) * 0.5f) * pitch;
                    StretchRow(item, center, pitch, cells[i].Column, columns, item.Height);
                    FitCellHitArea(item);
                }
                if (i == 0 && s_backItem != null && s_backLayouts.TryGetValue(s_backItem, out PinnedBackLayout backLayout))
                {
                    item.m_uberText?.UpdateNow(true);
                    inset = backLayout.InsetFirstRow(GetPinnedBackBounds(item));
                }
                try
                {
                    UIBScrollableItem scrollItem = item.GetComponent<UIBScrollableItem>();
                    if (scrollItem != null) scrollItem.m_active = UIBScrollableItem.ActiveState.Active;
                    // Preserve the native visibility contract, including at
                    // the default single-column size.
                    s_scroller?.AddVisibleAffectedObject(item.gameObject,
                        new Vector3(item.Height, item.Height, item.Height), true, CounterTextLayout.SetRowVisible);
                }
                catch { }
            }
            float[] rowHeights = FilterGridLayout.ScrollHeights(cells);
            float extent = 0f;
            for (int i = 0; i < rowHeights.Length; i++) extent += rowHeights[i];
            extent += inset;
            UIBScrollableItem contentSize = s_contents.GetComponent<UIBScrollableItem>();
            for (int i = 0; i < s_items.Count; i++)
            {
                UIBScrollableItem scrollItem = s_items[i]?.GetComponent<UIBScrollableItem>();
                if (scrollItem == null) continue;
                Vector3 size = scrollItem.m_size;
                // Publish height exactly once: either through a pre-existing
                // content item, or through one representative per grid row.
                size.z = contentSize != null ? 0f : rowHeights[i] + (i == 0 ? inset : 0f);
                scrollItem.m_size = size;
            }
            if (contentSize != null)
            {
                // Like SetFilterItem.Height and visible-affected sizes, native
                // scroll distances are world units. Dividing by the fitted
                // transform scale makes the scrollbar's travel too long.
                contentSize.m_active = UIBScrollableItem.ActiveState.Active;
                contentSize.m_size = new Vector3(viewport.size.x, Mathf.Max(contentSize.m_size.y, 0.01f), extent);
            }
            s_scroller?.ResetScrollStartPosition();
        }

        private static void PrepareGridItems()
        {
            s_gridGeometry?.Restore();
            if (s_trayObject != null && s_live?.m_trayObject != null)
                s_trayObject.transform.localScale = s_live.m_trayObject.transform.localScale;
            ConfigureScrollerLikeSets(s_live);
            s_gridFit = 1f;
        }

        private static void StretchFullRow(SetFilterItem item, Bounds viewport)
            => StretchRow(item, viewport.center.x, viewport.size.x * 0.98f);

        private static void StretchRow(SetFilterItem item, float centerX, float width, int column = -1, int columns = 1, float height = 0f)
        {
            if (item == null) return;
            if (s_wideRows.TryGetValue(item, out FilterTrayGeometry existing) && existing.FrameRenderer != null
                && s_rowShapes.TryGetValue(item, out RowShape shape) && shape.Column == column && shape.Columns == columns
                && Mathf.Abs(shape.Width - width) < 0.0001f && Mathf.Abs(shape.Height - height) < 0.0001f
                && Mathf.Abs(existing.FrameRenderer.bounds.size.x - width) < 0.0001f
                && Mathf.Abs(existing.FrameRenderer.bounds.size.z - shape.ActualHeight) < 0.0001f)
            {
                // Visibility changes often move a row without changing its
                // slice or gradient. Retain the already resized native mesh.
                item.transform.position += new Vector3(centerX - existing.FrameRenderer.bounds.center.x, 0f, 0f);
                CenterRowText(item, existing.FrameRenderer.bounds);
                return;
            }
            if (!s_wideRows.TryGetValue(item, out FilterTrayGeometry geometry))
            {
                item.m_uberText?.UpdateNow(true);
                Renderer[] textSurfaces = item.m_uberText != null ? CounterTextLayout.VisibleRenderers(item.m_uberText) : null;
                // Even while culled, the native RTT output is text, never a
                // stretchable piece of button artwork.
                Renderer output = item.m_uberText != null && item.m_uberText.RenderToTexture ? CounterTextLayout.TextureSurface(item.m_uberText) : null;
                if (output != null) textSurfaces = new[] { output };
                geometry = new FilterTrayGeometry(item.gameObject, item.m_uberText != null ? item.m_uberText.transform : null, textSurfaces);
                s_wideRows[item] = geometry;
            }
            else geometry.Restore();
            Renderer background = geometry.FrameRenderer;
            if (background == null) return;
            Bounds local = FilterTrayGeometry.TransformBounds(background.bounds, item.transform.InverseTransformPoint);
            Bounds target = new Bounds(local.center, new Vector3(
                width / Mathf.Max(Mathf.Abs(item.transform.lossyScale.x), 0.0001f), local.size.y,
                height > 0f ? height / Mathf.Max(Mathf.Abs(item.transform.lossyScale.z), 0.0001f) : local.size.z));
            geometry.Resize(local, target, Mathf.Max(local.size.z * 0.25f, 0.01f), null, column, columns);
            s_rowShapes[item] = new RowShape { Width = width, Height = height, ActualHeight = background.bounds.size.z, Column = column, Columns = columns };
            item.transform.position += new Vector3(centerX - background.bounds.center.x, 0f, 0f);
            CenterRowText(item, background.bounds);
        }

        private static void CenterRowText(SetFilterItem item, Bounds background, bool refresh = true)
        {
            UberText label = item.m_uberText;
            if (label == null) return;
            // Offscreen RTT matrices describe the previous render. Align only
            // after the native scroller has made the current surface visible.
            Renderer surface = label.RenderToTexture ? CounterTextLayout.TextureSurface(label) : null;
            if (label.RenderToTexture && (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy)) return;
            label.Alignment = UberText.AlignmentOptions.Center;
            if (refresh) label.UpdateNow(true);
            CounterTextLayout.RemoveClonedGlyphs(label);
            Renderer[] faces = CounterTextLayout.VisibleRenderers(label);
            Camera camera = ResolveUiCamera();
            Vector3 delta;
            if (camera != null && CounterTextLayout.TryScreenInkRect(label, camera, out Rect ink))
            {
                float depth = faces.Length > 0 ? item.transform.InverseTransformPoint(faces[0].bounds.center).y : 0f;
                Rect frame = GridScreenRect(background, camera);
                if (!FilterTrayGeometry.TryProjectToPlane(item.transform, camera, ink.center, depth, out Vector3 from)
                    || !FilterTrayGeometry.TryProjectToPlane(item.transform, camera,
                        new Vector3(frame.center.x, frame.center.y, 0f), depth, out Vector3 to)) return;
                delta = item.transform.TransformPoint(to) - item.transform.TransformPoint(from);
            }
            else if (CounterTextLayout.TryGlyphRange(label, out float left, out float right))
                delta = new Vector3(background.center.x - (left + right) * 0.5f, 0f, background.center.z - label.GetTextWorldSpaceBounds().center.z);
            else return;
            bool movedAncestor = false;
            foreach (Renderer face in faces)
            {
                if (face.transform == item.transform || face.transform.IsChildOf(label.transform)) continue;
                face.transform.position += delta;
                movedAncestor |= label.transform.IsChildOf(face.transform);
            }
            if (!movedAncestor) label.transform.position += delta;
        }

        private sealed class CaptionAlignment
        {
            internal Matrix4x4 Row, Label, View, Projection;
            internal Bounds Frame, Ink;
            internal Renderer Glyph;
            internal string Text;
            internal int Width, Height;
        }
        private static readonly Dictionary<SetFilterItem, CaptionAlignment> s_captionAlignment = new Dictionary<SetFilterItem, CaptionAlignment>();
        internal static void AlignVisibleCaption(SetFilterItem item)
        {
            if (item == null || !item.gameObject.activeInHierarchy || !s_wideRows.TryGetValue(item, out FilterTrayGeometry geometry)
                || geometry.FrameRenderer == null || item.m_uberText == null) return;
            UberText label = item.m_uberText; Camera camera = ResolveUiCamera();
            if (camera == null) return;
            Bounds ink = label.GetTextWorldSpaceBounds(), frame = geometry.FrameRenderer.bounds;
            Renderer glyph = CounterTextLayout.GlyphRenderer(label);
            s_captionAlignment.TryGetValue(item, out CaptionAlignment state);
            if (state != null && state.Row.Equals(item.transform.localToWorldMatrix) && state.Label.Equals(label.transform.localToWorldMatrix)
                && state.View.Equals(camera.worldToCameraMatrix) && state.Projection.Equals(camera.projectionMatrix)
                && state.Frame.Equals(frame) && state.Ink.Equals(ink) && state.Text == label.Text && state.Glyph == glyph
                && state.Width == Screen.width && state.Height == Screen.height) return;
            CenterRowText(item, frame, false);
            if (glyph == null || ink.size.sqrMagnitude <= .0000001f) return;
            if (state == null) s_captionAlignment[item] = state = new CaptionAlignment();
            state.Row = item.transform.localToWorldMatrix; state.Label = label.transform.localToWorldMatrix;
            state.View = camera.worldToCameraMatrix; state.Projection = camera.projectionMatrix;
            state.Frame = frame; state.Ink = label.GetTextWorldSpaceBounds(); state.Text = label.Text; state.Glyph = glyph;
            state.Width = Screen.width; state.Height = Screen.height;
        }

        private static void RefreshRowTextAlignment()
        {
            foreach (KeyValuePair<SetFilterItem, FilterTrayGeometry> entry in s_wideRows)
                if (entry.Key != null && entry.Key.gameObject.activeInHierarchy && entry.Value.FrameRenderer != null)
                    CenterRowText(entry.Key, entry.Value.FrameRenderer.bounds);
        }

        private static void FitCellHitArea(SetFilterItem item)
        {
            MeshFilter face = s_wideRows[item].FrameRenderer?.GetComponent<MeshFilter>();
            if (face?.sharedMesh == null) return;
            Bounds local = FilterTrayGeometry.TransformBounds(face.sharedMesh.bounds,
                p => item.transform.InverseTransformPoint(face.transform.TransformPoint(p)));
            // PegUI keeps references to the prefab's colliders. Preserve those
            // objects, their enabled state and their native input depth; the
            // artwork plane can sit behind the tray's other input surfaces.
            BoxCollider[] hits = item.GetComponentsInChildren<BoxCollider>(true);
            if (hits.Length == 0 && item.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                BoxCollider fallback = item.gameObject.AddComponent<BoxCollider>();
                fallback.size = new Vector3(1f, 0.01f, 1f);
                hits = new[] { fallback };
            }
            foreach (BoxCollider hit in hits)
            {
                Bounds area = FilterTrayGeometry.TransformBounds(local,
                    p => hit.transform.InverseTransformPoint(item.transform.TransformPoint(p)));
                hit.center = new Vector3(area.center.x, hit.center.y, area.center.z);
                hit.size = new Vector3(area.size.x, hit.size.y, area.size.z);
            }
        }

        /// <summary>
        /// Initialize a fresh row's idle artwork. Native SetFilterItem controls
        /// hover/press afterward; SafeSetSelected controls the selection latch.
        /// </summary>
        private static void SanitizeItemVisuals(SetFilterItem item)
        {
            if (item.m_uberText != null)
            {
                // Keep the native midpoint tint uniformly across all lines;
                // wrapping must not put glyphs in a darker part of the gradient.
                Color upper = item.m_uberText.GradientUpperColor, lower = item.m_uberText.GradientLowerColor;
                Color uniform = new Color((upper.r + lower.r) * 0.5f, (upper.g + lower.g) * 0.5f,
                    (upper.b + lower.b) * 0.5f, (upper.a + lower.a) * 0.5f);
                item.m_uberText.GradientUpperColor = uniform;
                item.m_uberText.GradientLowerColor = uniform;
            }
            // Reset only when constructing a row. Later visibility refreshes
            // must preserve the native OnPress/OnOver state.
            item.m_mouseOverGlow?.SetActive(false);
            item.m_pressedShadow?.SetActive(false);
            ApplyIdleChrome(item, selected: false);
        }

        private static void ApplyIdleChrome(SetFilterItem item, bool selected)
        {
            if (item == null)
                return;
            try
            {
                // Clear trial/temp set data so hourglass icon logic stays off.
                try
                {
                    item.TrialCardPeriod = null;
                }
                catch
                {
                    // ignore
                }

                UIBHighlight[] highlights = item.GetComponentsInChildren<UIBHighlight>(true);

                // Hide unrelated prefab decorations; native feedback owns its own artwork.
                if (item.m_icon != null)
                {
                    item.m_icon.enabled = false;
                    item.m_icon.gameObject.SetActive(false);
                }
                if (item.m_iconFX != null)
                    item.m_iconFX.SetActive(false);
                // Selection chrome — only when actually selected (headers never).
                bool showSel = selected && !item.IsHeader;
                if (item.m_selectedGlow != null)
                    item.m_selectedGlow.SetActive(showSel);
                // Prefab leftovers: Glow / Highlight / Icon / Trial / Temporary / Hourglass.
                Transform[] all = item.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null || t == item.transform)
                        continue;
                    if (t.GetComponent<UberText>() != null || FilterButtonFeedback.OwnsVisual(item, highlights, t))
                        continue;
                    if (IsChromeChildName(t.name))
                        t.gameObject.SetActive(false);
                }

                if (item.m_uberText != null)
                {
                    item.m_uberText.gameObject.SetActive(true);
                    item.m_uberText.UpdateNow(true);
                    CounterTextLayout.RemoveClonedGlyphs(item.m_uberText);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsChromeChildName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string n = name;
            return n.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Hourglass", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Temporary", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Trial", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("IconFX", StringComparison.OrdinalIgnoreCase) >= 0
                || n.Equals("Icon", StringComparison.OrdinalIgnoreCase)
                || n.IndexOf("SetIcon", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("EventIcon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SafeSetSelected(SetFilterItem item, bool selected)
        {
            if (item == null)
                return;
            try
            {
                var field = HarmonyLib.AccessTools.Field(typeof(SetFilterItem), "m_isSelected");
                field?.SetValue(item, selected);
                // Like native SetSelected, change the latched press shadow
                // only when selection changes, not on every visibility update.
                item.m_pressedShadow?.SetActive(selected && !item.IsHeader);
                // Re-apply selection chrome without erasing live native feedback.
                ApplyIdleChrome(item, selected);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.SafeSetSelected failed", ex);
            }
        }

        private static void ReleaseWideRow(SetFilterItem item)
        {
            if (ReferenceEquals(item, null)) return;
            if (!s_wideRows.TryGetValue(item, out FilterTrayGeometry geometry)) return;
            geometry.Dispose();
            s_wideRows.Remove(item);
            s_rowShapes.Remove(item);
        }

        private static void ReleaseWideRows()
        {
            foreach (FilterTrayGeometry geometry in s_wideRows.Values) geometry.Dispose();
            s_wideRows.Clear(); s_captionAlignment.Clear();
            s_rowShapes.Clear();
            s_originalGlow.Clear();
        }

        private static void ApplyGridItemScale(SetFilterItem item, UIBScrollableItem scrollItem)
        {
            if (item == null || s_trayObject == null || !item.transform.IsChildOf(s_trayObject.transform)) return;
            item.Height *= s_gridFit;
            if (scrollItem != null) scrollItem.m_size *= s_gridFit;
        }

        private static void ConfigureGrid(bool hasBack = true)
        {
            if (s_trayObject == null || s_live?.m_trayObject == null) return;
            Transform tray = s_trayObject.transform;
            Vector3 restPosition = tray.localPosition - s_gridOffset;
            Vector3 shown = s_live.m_showBone != null ? s_live.m_showBone.transform.localPosition : restPosition;
            Vector3 nativeScale = s_live.m_trayObject.transform.localScale;
            s_gridGeometry?.Restore();
            tray.localScale = nativeScale;
            ConfigureScrollerLikeSets(s_live);
            s_gridOffset = Vector3.zero;
            s_gridFit = 1f;
            s_gridDirty = false;
            s_gridScreenWidth = Screen.width;
            s_gridScreenHeight = Screen.height;
            int requestedColumns = CollectionQualitySettings.FilterMenuColumns, requestedRows = CollectionQualitySettings.FilterMenuRows;
            if (s_scroller?.m_ScrollBounds == null) return;
            tray.localPosition = shown;
            try
            {
                if (s_gridGeometry == null) s_gridGeometry = new FilterTrayGeometry(s_trayObject, s_contents);
                BoxCollider box = s_scroller.m_ScrollBounds;
                CameraMask mask = s_trayObject.GetComponent<CameraMask>();
                // Resize around the visible opening, not the larger native
                // hit-test box. Otherwise art and scroll range disagree.
                FitGridScrollViewport(mask, box);
                Bounds native = GetBoxWorldBounds(box);
                Bounds local = FilterTrayGeometry.TransformBounds(new Bounds(box.center, box.size),
                    p => tray.InverseTransformPoint(box.transform.TransformPoint(p)));
                float rowHeight = s_itemPrefab.GetComponent<UIBScrollableItem>()?.m_size.z ?? 0.45f;
                if (rowHeight < 0.01f) rowHeight = 0.45f;
                float nativeHeight = native.size.z;
                Camera camera = ResolveUiCamera();
                Bounds nativeFrame = s_gridGeometry.FrameRenderer != null ? s_gridGeometry.FrameRenderer.bounds
                    : GetTrayFrameBounds(s_trayObject, s_contents, s_contents, native);
                Rect nativeScreen = camera != null ? GridScreenRect(nativeFrame, camera) : default(Rect);
                var headers = new bool[s_items.Count];
                for (int i = 0; i < s_items.Count; i++) headers[i] = IsFullWidthRow(s_items[i]);
                bool[] layoutShape = CollectionQualitySettings.HideEmptyFilters && s_unfilteredHeaders.Count > 0
                    ? s_unfilteredHeaders.ToArray() : headers;
                int initialHeading = layoutShape.Length > 0 && layoutShape[0] ? 1 : 0;
                int navigation = hasBack ? PinnedHeaderRows(requestedColumns) : 0;
                int rowCapacity = CollectionQualitySettings.MaxFilterRows;
                int columnCapacity = CollectionQualitySettings.MaxFilterColumns;
                float bottom = Screen.height * 0.08f;
                if (camera != null && CollectionFilterMenu.TryGetToggleScreenRect(camera, out Rect toggle))
                    bottom = Mathf.Max(bottom, toggle.yMax + 6f);
                float top = Screen.height * 0.92f;
                if (camera != null)
                {
                    Rect opening = GridScreenRect(native, camera);
                    // Automatic can extend beyond the original tray. Reserve
                    // its frame trim and pinned rows, then count native-size
                    // rows in the usable screen area above the opening button.
                    Vector3 rowStart = camera.WorldToScreenPoint(native.center);
                    Vector3 rowEnd = camera.WorldToScreenPoint(native.center + new Vector3(0f, 0f, rowHeight));
                    float rowPixels = Mathf.Max(1f, Mathf.Abs(rowEnd.y - rowStart.y));
                    // The collider's depth is for hit testing, not visible
                    // height; including it overestimates space in tilted UI.
                    Rect flatOpening = GridScreenRect(new Bounds(native.center,
                        new Vector3(native.size.x, 0f, native.size.z)), camera);
                    float framePixels = Mathf.Max(0f, nativeScreen.height - flatOpening.height);
                    float automaticHeight = Mathf.Min(top - bottom, Screen.height * FilterGridLayout.AutomaticHeightFraction);
                    rowCapacity = (int)(Mathf.Max(0f, automaticHeight - framePixels) / rowPixels)
                        - navigation - initialHeading;
                    float automaticWidth = Mathf.Min(Screen.width * 0.96f - Mathf.Max(0f, nativeScreen.xMin),
                        Screen.width * FilterGridLayout.AutomaticWidthFraction);
                    float frameWidth = Mathf.Max(0f, nativeScreen.width - opening.width);
                    columnCapacity = System.Math.Max(1, System.Math.Min(CollectionQualitySettings.MaxFilterColumns,
                        (int)((automaticWidth - frameWidth) / Mathf.Max(1f, opening.width))));
                }
                FilterGridLayout.Dimensions chosen = FilterGridLayout.Choose(layoutShape, requestedRows, requestedColumns, rowCapacity, columnCapacity);
                int actualNavigation = hasBack ? PinnedHeaderRows(chosen.Columns) : 0;
                if (actualNavigation != navigation)
                {
                    // Automatic columns are chosen from the single-column
                    // budget. Once expanded, use the space saved by shared
                    // header rows without changing that column choice again.
                    rowCapacity += navigation - actualNavigation;
                    navigation = actualNavigation;
                    chosen = FilterGridLayout.Choose(layoutShape, requestedRows, chosen.Columns, rowCapacity, columnCapacity);
                }
                int columns = s_gridColumns = chosen.Columns, rows = s_gridRows = chosen.Rows;
                float heightChange = ((rows + navigation + initialHeading) * rowHeight - nativeHeight)
                    / Mathf.Max(Mathf.Abs(tray.lossyScale.z), 0.0001f);
                Bounds target = new Bounds(local.center, new Vector3(local.size.x * columns,
                    local.size.y, Mathf.Max(rowHeight, local.size.z + heightChange)));
                float anchorX = (s_contentsBone != null ? s_contentsBone.position.x : native.center.x) - native.center.x;
                s_gridGeometry.Resize(local, target, rowHeight, box);
                Bounds resizedViewport = FilterTrayGeometry.TransformBounds(target,
                    p => box.transform.InverseTransformPoint(tray.TransformPoint(p)));
                box.center = resizedViewport.center;
                box.size = resizedViewport.size;
                if (s_contentsBone != null)
                    s_contentsBone.position += tray.TransformPoint(new Vector3(0f, 0f, (target.size.z - local.size.z) * 0.5f)) - tray.position;
                if (mask != null)
                {
                    mask.m_Width += target.size.x - local.size.x;
                    mask.m_Height += target.size.z - local.size.z;
                }
                // The hit collider can be larger than the native pixel mask.
                // Retaining that overscan makes a short grid appear to fit to
                // the scroller although most rows are clipped offscreen.
                FitGridScrollViewport(mask, box);
                s_scroller.m_ForceScrollAreaHitTest = true;
                s_scroller.m_UseScrollContentsInHitTest = false;
                if (camera != null)
                {
                    Bounds frame = s_gridGeometry.FrameRenderer != null ? s_gridGeometry.FrameRenderer.bounds
                        : GetTrayFrameBounds(s_trayObject, s_contents, s_contents, GetBoxWorldBounds(box));
                    Rect screen = GridScreenRect(frame, camera);
                    if (screen.width > 0f && screen.height > 0f)
                    {
                        // A large grid may shrink uniformly to fit the screen;
                        // the root is never stretched on an individual axis.
                        // Keep the outer frame at the native left seam. The
                        // inner viewport is inset and leaves cards visible if
                        // used as the panel's anchor.
                        float left = Mathf.Max(0f, nativeScreen.xMin);
                        float right = Screen.width * 0.96f;
                        if (requestedColumns == 0)
                            right = Mathf.Min(right, left + Screen.width * FilterGridLayout.AutomaticWidthFraction);
                        left = Mathf.Min(left, right - Screen.width * 0.2f);
                        s_gridFit = Mathf.Min(1f, Mathf.Min((right - left) / screen.width,
                            Mathf.Max(1f, top - bottom) / screen.height));
                        tray.localScale = nativeScale * s_gridFit;
                        frame = s_gridGeometry.FrameRenderer != null ? s_gridGeometry.FrameRenderer.bounds
                            : GetTrayFrameBounds(s_trayObject, s_contents, s_contents, GetBoxWorldBounds(box));
                        screen = GridScreenRect(frame, camera);
                        float dx = left - screen.xMin;
                        float dy = Mathf.Max(0f, bottom - screen.yMin) - Mathf.Max(0f, screen.yMax - top);
                        Vector3 point = camera.WorldToScreenPoint(tray.position);
                        if (Mathf.Abs(dx) + Mathf.Abs(dy) > 0.001f && FilterTrayGeometry.TryProjectToPlane(tray, camera,
                            point + new Vector3(dx, dy, 0f), 0f, out Vector3 move))
                            tray.position = tray.TransformPoint(move);
                    }
                }
                s_gridAnchorX = anchorX * s_gridFit;
                s_gridOffset = tray.localPosition - shown;
            }
            finally { tray.localPosition = restPosition + s_gridOffset; }
            s_scroller.ResetScrollStartPosition();
        }

        private static bool IsPointerOverVisibleTray()
        {
            if (s_trayObject == null || !s_trayObject.activeInHierarchy) return false;
            Camera camera = ResolveUiCamera();
            if (camera == null || s_scroller?.m_ScrollBounds == null) return false;
            // The native tray also owns shadows and feedback meshes extending
            // well past its border. Only the fitted panel defines its interior.
            Renderer panel = s_gridGeometry?.FrameRenderer;
            Mesh mesh = panel != null ? panel.GetComponent<MeshFilter>()?.sharedMesh : null;
            Bounds bounds = mesh != null
                ? FilterTrayGeometry.TransformBounds(mesh.bounds, panel.transform.TransformPoint)
                : GetBoxWorldBounds(s_scroller.m_ScrollBounds);
            Rect frame = GridScreenRect(bounds, camera);
            Vector3 pointer = Input.mousePosition;
            return pointer.x >= frame.xMin && pointer.x <= frame.xMax && pointer.y >= frame.yMin && pointer.y <= frame.yMax;
        }

        private static void FitGridScrollViewport(CameraMask mask, BoxCollider box)
        {
            if (mask == null || box == null) return;
            Bounds bounds = GetBoxWorldBounds(box);
            Vector3 center = mask.transform.position, scale = mask.transform.lossyScale;
            float halfX = mask.m_Width * Mathf.Abs(scale.x) * 0.5f;
            float halfZ = mask.m_Height * Mathf.Abs(scale.z) * 0.5f;
            float left = Mathf.Max(bounds.min.x, center.x - halfX), right = Mathf.Min(bounds.max.x, center.x + halfX);
            float low = Mathf.Max(bounds.min.z, center.z - halfZ), high = Mathf.Min(bounds.max.z, center.z + halfZ);
            if (right <= left || high <= low) return;
            Bounds clipped = new Bounds(new Vector3((left + right) * 0.5f, bounds.center.y, (low + high) * 0.5f),
                new Vector3(right - left, bounds.size.y, high - low));
            Bounds local = FilterTrayGeometry.TransformBounds(clipped, box.transform.InverseTransformPoint);
            box.center = local.center;
            box.size = local.size;
        }

        private static Vector3 GetHiddenGridPosition(Vector3 nativeHidden, Vector3 shown)
        {
            Camera camera = ResolveUiCamera();
            if (camera == null || s_trayObject == null || s_scroller?.m_ScrollBounds == null)
                return nativeHidden + s_gridOffset;
            Transform tray = s_trayObject.transform;
            Vector3 previous = tray.localPosition;
            try
            {
                tray.localPosition = shown;
                Bounds bounds = GetTrayFrameBounds(s_trayObject, s_contents, s_contents,
                    GetBoxWorldBounds(s_scroller.m_ScrollBounds));
                Rect frame = GridScreenRect(bounds, camera);
                Vector3 point = camera.WorldToScreenPoint(tray.position);
                Vector3 direction = nativeHidden + s_gridOffset - shown;
                Vector3 worldDirection = tray.parent != null
                    ? tray.parent.TransformPoint(direction) - tray.parent.TransformPoint(Vector3.zero) : direction;
                Vector3 delta = camera.WorldToScreenPoint(tray.position + worldDirection) - point;
                float multiplier = FilterGridLayout.HiddenSlideMultiplier(frame, delta, Screen.width, Screen.height);
                return shown + direction * multiplier;
            }
            finally { tray.localPosition = previous; }
        }

        private static Rect GridScreenRect(Bounds bounds, Camera camera)
        {
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = camera.WorldToScreenPoint(bounds.center + new Vector3(
                    bounds.extents.x * x, bounds.extents.y * y, bounds.extents.z * z));
                left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
                bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y);
            }
            return Rect.MinMaxRect(left, bottom, right, top);
        }

    }
}
