using System;
using System.Collections;
using System.Collections.Generic;
using PegasusShared;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>
    /// Keyword filter panel: clones SetFilterTray.m_trayObject only (never the live Select Set
    /// toggle). Populates native SetFilterItems like Sets, with one keyword list
    /// divided by mechanic-group headings.
    /// Slides along the native hideBone→showBone direction, extending the hidden
    /// endpoint to accommodate the resized frame.
    /// </summary>
    internal static partial class KeywordSetTray
    {
        private const string TrayObjectName = "BCM_KeywordTrayObject";
        private const string BackItemName = "BCM_PinnedFilterBack";
        private const float SlideInSeconds = 0.35f;
        private const float SlideOutSeconds = 0.25f;

        private static SetFilterTray s_live;
        private static GameObject s_trayObject;
        private static Transform s_contents;
        private static Transform s_contentsBone;
        private static GameObject s_itemPrefab;
        private static GameObject s_headerPrefab;
        private static UIBScrollable s_scroller;
        private static SetFilterItem s_backItem;
        private static readonly Dictionary<SetFilterItem, PinnedBackLayout> s_backLayouts =
            new Dictionary<SetFilterItem, PinnedBackLayout>();
        private static readonly Dictionary<Collider, bool> s_backBlockedColliders =
            new Dictionary<Collider, bool>();
        private static SetFilterItem s_backPressedItem;
        private static bool s_backNavigationPending;
        private static int s_backNavigationFrame = -1;
        private static int s_backReleaseFrame = -1;
        private static readonly List<SetFilterItem> s_items = new List<SetFilterItem>();
        private static readonly Dictionary<GAME_TAG, SetFilterItem> s_keywordItems =
            new Dictionary<GAME_TAG, SetFilterItem>();
        private static bool s_shown;
        private static bool s_isAnimating;
        private static object s_slideCoroutine;
        private static float s_ignoreOutsideUntil;
        private static GameObject s_hideArea;
        private static PegUIElement s_hidePeg;
        private static float s_lastScrollValue = float.NaN;
        private static readonly FilterPointerGesture s_pointerGesture = new FilterPointerGesture();

        private static FilterTrayKind s_activeKind = FilterTrayKind.Keywords;

        public static bool IsShown =>
            s_shown && s_trayObject != null && s_trayObject.activeInHierarchy;

        public static FilterTrayKind ActiveKind => s_activeKind;
        public static bool IsBuilt => s_trayObject != null && s_contents != null;

        public static void EnsureBuilt(SetFilterTray liveTray)
        {
            if (s_trayObject != null)
                return;
            if (liveTray == null || liveTray.m_trayObject == null)
            {
                DebugLog.Error("KeywordSetTray.EnsureBuilt: live tray / m_trayObject missing");
                return;
            }

            try
            {
                s_live = liveTray;
                s_itemPrefab = liveTray.m_itemPrefab;
                s_headerPrefab = liveTray.m_headerPrefab;
                if (s_itemPrefab == null)
                {
                    DebugLog.Error("KeywordSetTray: m_itemPrefab null");
                    return;
                }

                // Clone ONLY the sliding tray — never SetFilterTray root / live toggle.
                GameObject go = UnityEngine.Object.Instantiate(liveTray.m_trayObject);
                go.name = TrayObjectName;
                Transform trayParent = liveTray.m_trayObject.transform.parent;
                if (trayParent != null)
                    GameUtils.SetParent(go, trayParent.gameObject, true);

                go.transform.localRotation = liveTray.m_trayObject.transform.localRotation;
                go.transform.localScale = liveTray.m_trayObject.transform.localScale;
                if (liveTray.m_hideBone != null)
                    go.transform.localPosition = liveTray.m_hideBone.transform.localPosition;
                else
                    go.transform.localPosition = liveTray.m_trayObject.transform.localPosition;

                s_trayObject = go;
                s_contents = ResolveContentsTransform(liveTray, go);
                s_contentsBone = ResolveNamedChild(liveTray, go, liveTray.m_contentsBone);
                s_scroller = go.GetComponentInChildren<UIBScrollable>(true);
                // Prefer live scroller's twin if GetComponentInChildren grabbed a nested leftover.
                if (s_scroller == null && liveTray.m_scroller != null)
                {
                    Transform twin = ResolveRelative(liveTray.m_trayObject.transform, go.transform,
                        liveTray.m_scroller.transform);
                    if (twin != null)
                        s_scroller = twin.GetComponent<UIBScrollable>();
                }

                ConfigureScrollerLikeSets(liveTray);

                ActivateChain(s_contents, go.transform);

                ClearContents();
                EnsureContentsBone(liveTray);
                go.SetActive(false);
                s_shown = false;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.EnsureBuilt failed", ex);
                Destroy();
            }
        }

        /// <summary>Called when live Sets tray opens so Keywords closes (mutual exclusivity).</summary>
        public static void OnLiveSetsShown()
        {
            if (!s_shown)
                return;
            SetShown(false, "Sets opened");
        }

        /// <summary>K cork was clicked — ignore outside-close for this release frame.</summary>
        public static void NotifyToggleClicked()
        {
            s_ignoreOutsideUntil = Time.unscaledTime + 0.3f;
        }

        public static void Toggle()
        {
            SetShown(!IsShown, FilterTrayKind.Keywords, "Toggle");
        }

        public static void ToggleKind(FilterTrayKind kind)
        {
            if (IsShown && s_activeKind == kind)
                SetShown(false, kind, $"Toggle close {kind}");
            else
                SetShown(true, kind, $"Toggle open {kind}");
        }

        private static Camera ResolveUiCamera()
        {
            try
            {
                if (s_gridGeometry?.FrameRenderer != null)
                {
                    Camera frameCamera = CameraUtils.FindProjectionCameraForObject(s_gridGeometry.FrameRenderer.gameObject);
                    if (frameCamera != null) return frameCamera;
                }
                if (s_trayObject != null)
                {
                    foreach (Renderer renderer in s_trayObject.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!renderer.enabled || (s_contents != null && renderer.transform.IsChildOf(s_contents))) continue;
                        Camera projection = CameraUtils.FindProjectionCameraForObject(renderer.gameObject);
                        if (projection != null) return projection;
                    }
                }
                if (s_scroller != null)
                {
                    var getCam = HarmonyLib.AccessTools.Method(typeof(UIBScrollable), "GetScrollCamera");
                    if (getCam != null)
                    {
                        Camera c = getCam.Invoke(s_scroller, null) as Camera;
                        if (c != null)
                            return c;
                    }
                }
                if (s_trayObject != null)
                {
                    int layer = s_trayObject.layer;
                    int mask = 1 << layer;
                    Camera[] cams = Camera.allCameras;
                    if (cams != null)
                    {
                        for (int i = 0; i < cams.Length; i++)
                        {
                            Camera c = cams[i];
                            if (c != null && c.enabled && (c.cullingMask & mask) != 0)
                                return c;
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            return Camera.main;
        }

        public static void RebuildRows()
        {
            ResetMenuInteractions();
            if (s_contents == null || s_itemPrefab == null)
                return;

            float previousScroll = s_shown && s_scroller != null ? s_scroller.ScrollValue : 0f;
            try
            {
                ActivateTrayHierarchy();
                DestroyPinnedBackItem();
                ClearContents();
                PrepareGridItems();
                ActivateTrayHierarchy();

                FormatType format = FormatType.FT_UNKNOWN;
                try
                {
                    if (CollectionManager.Get()?.GetCollectibleDisplay() is CollectionManagerDisplay cmd)
                        format = cmd.CurrentSetFilterFormatType;
                }
                catch
                {
                    // ignore
                }

                switch (s_activeKind)
                {
                    case FilterTrayKind.Expansions:
                        PopulateExpansionRows();
                        break;
                    case FilterTrayKind.Menu:
                        PopulateMenuRows(format);
                        break;
                    case FilterTrayKind.Type:
                        PopulateTypeRows(format);
                        break;
                    case FilterTrayKind.Race:
                        PopulateRaceRows(format);
                        break;
                    case FilterTrayKind.School:
                        PopulateSchoolRows(format);
                        break;
                    case FilterTrayKind.Rarity:
                        PopulateRarityRows(format);
                        break;
                    case FilterTrayKind.Packages:
                        PopulatePackageRows(format); break;
                    case FilterTrayKind.Categories:
                        PopulateCategoryRows(format);
                        break;
                    case FilterTrayKind.Keywords:
                    default:
                        PopulateKeywordRows(format);
                        break;
                }

                ConfigureGrid(s_activeKind != FilterTrayKind.Menu || ShowRootClearAll);
                foreach (SetFilterItem item in s_items)
                    ApplyGridItemScale(item, item != null ? item.GetComponent<UIBScrollableItem>() : null);
                if (s_activeKind != FilterTrayKind.Menu)
                    s_backItem = CreatePinnedBackItem(s_trayObject, s_contentsBone, s_scroller, format, controls: true, columns: s_gridColumns);
                else if (ShowRootClearAll)
                    s_backItem = CreatePinnedBackItem(s_trayObject, s_contentsBone, s_scroller, format, clearAll: true);
                LayoutItemsLikeSets();
                // Back has its own fixed header and pixel mask. The scroll
                // range must use the remaining list viewport, not that header.
                FitGridScrollViewport(GetActiveCameraMask(s_trayObject), s_scroller?.m_ScrollBounds);
                s_scroller?.ResetScrollStartPosition();
                ResetScrollToTop();
                ApplyMenuScroll(previousScroll);
                ApplyVisibleAffectedClip();
                ForceCameraMaskRefresh("rebuild-rows");
                RememberBuiltRows();
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.RebuildRows failed", ex);
            }
        }

        private static void PopulateMenuRows(FormatType format)
        {
            AddHeader("Filters", format);
            AddItem("Expansions", format, CollectionFilterMenu.ShowExpansions);
            AddFilterMenuItem("Keywords", FilterTrayKind.Keywords, format,
                KeywordFilterState.IsActive);
            AddFilterMenuItem("Categories", FilterTrayKind.Categories, format,
                ExtraFilterState.IsActiveForKind(FilterTrayKind.Categories));
            AddFilterMenuItem("Packages", FilterTrayKind.Packages, format, PackageViewState.IsActive);
            AddFilterMenuItem("Card Type", FilterTrayKind.Type, format,
                ExtraFilterState.IsActiveForKind(FilterTrayKind.Type));
            AddFilterMenuItem("Minion Type", FilterTrayKind.Race, format,
                ExtraFilterState.IsActiveForKind(FilterTrayKind.Race));
            AddFilterMenuItem("Spell School", FilterTrayKind.School, format,
                ExtraFilterState.IsActiveForKind(FilterTrayKind.School));
            AddFilterMenuItem("Rarity", FilterTrayKind.Rarity, format,
                ExtraFilterState.IsActiveForKind(FilterTrayKind.Rarity));
        }

        public static void Destroy()
        {
            ReleaseMenuInput();
            ResetPinnedBackInput();
            StopSlideCoroutine();
            s_shown = false;
            s_isAnimating = false;
            DestroyPinnedBackItem();
            ClearContents();
            s_gridGeometry?.Dispose();
            s_gridGeometry = null;
            s_expansionRows.Clear();
            s_pointerGesture.Reset();
            ResetPinnedPress();
            ResetMenuSession();
            s_closedColliders.Clear();
            s_closedPegs.Clear();
            s_contents = null;
            s_contentsBone = null;
            s_itemPrefab = null;
            s_headerPrefab = null;
            s_scroller = null;
            s_live = null;
            s_gridOffset = Vector3.zero;
            s_gridFit = 1f;
            s_gridColumns = 1;
            s_gridDirty = false;
            s_hidePeg = null;
            if (s_hideArea != null)
            {
                UnityEngine.Object.Destroy(s_hideArea);
                s_hideArea = null;
            }
            if (s_trayObject != null)
            {
                UnityEngine.Object.Destroy(s_trayObject);
                s_trayObject = null;
            }
        }

        private static void ConfigureScrollerLikeSets(SetFilterTray liveTray)
        {
            if (s_scroller == null)
                return;
            try
            {
                s_scroller.enabled = true;
                s_scroller.ForceVisibleAffectedObjectsShow(false);

                if (liveTray?.m_scroller == null)
                    return;

                UIBScrollable live = liveTray.m_scroller;

                // ScrollObject must be our Contents (same relative role as Sets).
                if (s_contents != null)
                    s_scroller.ScrollObject = s_contents.gameObject;

                // Copy public scroll/clip fields identically from live Sets scroller.
                s_scroller.m_UseCameraFromLayer = live.m_UseCameraFromLayer;
                s_scroller.m_ScrollWheelAmount = live.m_ScrollWheelAmount;
                s_scroller.m_ScrollWheelMode = live.m_ScrollWheelMode;
                s_scroller.m_ScrollBottomPadding = live.m_ScrollBottomPadding;
                s_scroller.m_ScrollTweenTime = live.m_ScrollTweenTime;
                s_scroller.m_ScrollPlane = live.m_ScrollPlane;
                s_scroller.m_ScrollDirectionReverse = live.m_ScrollDirectionReverse;
                s_scroller.m_OverridePegUI = live.m_OverridePegUI;
                s_scroller.m_ForceScrollAreaHitTest = live.m_ForceScrollAreaHitTest;
                s_scroller.m_ScrollOnMouseDrag = live.m_ScrollOnMouseDrag;
                // Exact Sets values — no threshold bump / bounds inset.
                s_scroller.m_VisibleObjectThreshold = live.m_VisibleObjectThreshold;
                s_scroller.m_UseScrollContentsInHitTest = live.m_UseScrollContentsInHitTest;
                s_scroller.m_ScrollBoundsSpringK = live.m_ScrollBoundsSpringK;
                s_scroller.m_MinOutOfBoundsScrollValue = live.m_MinOutOfBoundsScrollValue;
                s_scroller.m_ScrollDeltaMultiplier = live.m_ScrollDeltaMultiplier;
                s_scroller.m_HeightMode = live.m_HeightMode;

                AlignScrollHierarchyFromLive(liveTray, live);
                SyncCameraMaskFromLive(liveTray);

                s_scroller.ResetScrollStartPosition();
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.ConfigureScrollerLikeSets failed", ex);
            }
        }

        /// <summary>
        /// Copy Scroller/ScrollBounds/Contents/ContentsBone local TRS + BoxCollider size from live
        /// Sets so visible-affected uses the same Bounds↔Contents relationship.
        /// </summary>
        private static void AlignScrollHierarchyFromLive(SetFilterTray liveTray, UIBScrollable live)
        {
            if (liveTray?.m_trayObject == null || s_trayObject == null || live == null)
                return;

            Transform liveRoot = liveTray.m_trayObject.transform;
            Transform ourRoot = s_trayObject.transform;

            // Bounds GameObject: match live m_ScrollBounds twin under our tray.
            BoxCollider liveBounds = live.m_ScrollBounds;
            if (liveBounds != null)
            {
                Transform liveBoundsTf = liveBounds.transform;
                Transform ourBoundsTf = ResolveRelative(liveRoot, ourRoot, liveBoundsTf);
                if (ourBoundsTf != null)
                {
                    ourBoundsTf.localPosition = liveBoundsTf.localPosition;
                    ourBoundsTf.localRotation = liveBoundsTf.localRotation;
                    ourBoundsTf.localScale = liveBoundsTf.localScale;

                    BoxCollider ourBox = ourBoundsTf.GetComponent<BoxCollider>();
                    if (ourBox == null)
                        ourBox = s_scroller.m_ScrollBounds;
                    if (ourBox != null)
                    {
                        ourBox.center = liveBounds.center;
                        ourBox.size = liveBounds.size;
                        ourBox.enabled = true;
                        s_scroller.m_ScrollBounds = ourBox;
                    }
                }
                else if (s_scroller.m_ScrollBounds != null)
                {
                    s_scroller.m_ScrollBounds.center = liveBounds.center;
                    s_scroller.m_ScrollBounds.size = liveBounds.size;
                    s_scroller.m_ScrollBounds.enabled = true;
                }
            }

            // Contents rest pose (scroll object) — match live locals so bone↔bounds stays Sets-like.
            if (liveTray.m_contents != null && s_contents != null)
            {
                Transform liveContents = liveTray.m_contents.transform;
                s_contents.localPosition = liveContents.localPosition;
                s_contents.localRotation = liveContents.localRotation;
                s_contents.localScale = liveContents.localScale;
            }

            if (liveTray.m_contentsBone != null && s_contentsBone != null)
            {
                Transform liveBone = liveTray.m_contentsBone.transform;
                s_contentsBone.localPosition = liveBone.localPosition;
                s_contentsBone.localRotation = liveBone.localRotation;
                s_contentsBone.localScale = liveBone.localScale;
            }

            // Scroller GO local TRS (parent of Bounds).
            if (live.transform != null && s_scroller.transform != null)
            {
                Transform liveScrollTf = live.transform;
                Transform ourScrollTf = ResolveRelative(liveRoot, ourRoot, liveScrollTf);
                if (ourScrollTf != null)
                {
                    ourScrollTf.localPosition = liveScrollTf.localPosition;
                    ourScrollTf.localRotation = liveScrollTf.localRotation;
                    ourScrollTf.localScale = liveScrollTf.localScale;
                }
            }
        }

        private static void SyncCameraMaskFromLive(SetFilterTray liveTray)
        {
            if (liveTray?.m_trayObject == null || s_trayObject == null)
                return;
            try
            {
                CameraMask liveMask = liveTray.m_trayObject.GetComponent<CameraMask>();
                CameraMask ourMask = s_trayObject.GetComponent<CameraMask>();
                if (liveMask == null || ourMask == null)
                    return;

                CopyCameraMaskSettings(liveMask, ourMask);

                // Clip objects must be OUR Contents (Sets uses FilterTray Contents under the mask).
                if (s_contents != null)
                    ourMask.m_ClipObjects = s_contents.gameObject;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.SyncCameraMaskFromLive failed", ex);
            }
        }

        private static void CopyCameraMaskSettings(CameraMask source, CameraMask target)
        {
            target.m_Width = source.m_Width;
            target.m_Height = source.m_Height;
            target.m_UpVector = source.m_UpVector;
            target.m_RealtimeUpdate = true;
            target.m_UseCameraFromLayer = source.m_UseCameraFromLayer;
            target.RenderEntryPoint = source.RenderEntryPoint;
            // m_CameraFromLayer / m_CullingMasks need Blizzard.T5.Game — copy via reflection.
            try
            {
                var camLayerField = HarmonyLib.AccessTools.Field(typeof(CameraMask), "m_CameraFromLayer");
                camLayerField?.SetValue(target, camLayerField.GetValue(source));
                var cullField = HarmonyLib.AccessTools.Field(typeof(CameraMask), "m_CullingMasks");
                object liveCull = cullField?.GetValue(source);
                if (liveCull != null && cullField != null)
                {
                    // Shallow-copy list contents
                    var listType = liveCull.GetType();
                    var copy = Activator.CreateInstance(listType);
                    var add = listType.GetMethod("Add");
                    var countProp = listType.GetProperty("Count");
                    var itemProp = listType.GetProperty("Item");
                    int n = (int)countProp.GetValue(liveCull, null);
                    for (int i = 0; i < n; i++)
                        add.Invoke(copy, new[] { itemProp.GetValue(liveCull, new object[] { i }) });
                    cullField.SetValue(target, copy);
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Re-run CameraMask OnEnable path so Contents + all FilterItem renderers get CameraMask
        /// layer and the scissor pass is scheduled at the current showBone pose.
        /// </summary>
        private static void ForceCameraMaskRefresh(string reason)
        {
            if (s_trayObject == null)
                return;
            try
            {
                CameraMask mask = GetActiveCameraMask(s_trayObject);
                if (mask == null)
                {
                    DebugLog.Warning($"KeywordSetTray ForceCameraMaskRefresh({reason}): no CameraMask");
                    return;
                }

                if (s_contents != null)
                    mask.m_ClipObjects = s_contents.gameObject;

                // Keep realtime scissor updates while tray is shown (pose/scroll).
                mask.m_RealtimeUpdate = true;

                // New FilterItems from RebuildRows can sit on Default until we force CameraMask layer.
                EnsureTrayContentsOnCameraMaskLayer();

                UpdateVisibleMenuMask(mask, reinitialize: true);
                RefreshRowTextAlignment();


            }
            catch (Exception ex)
            {
                DebugLog.Exception($"KeywordSetTray.ForceCameraMaskRefresh({reason}) failed", ex);
            }
        }

        /// <summary>
        /// Force Contents + every FilterItem onto CameraMask layer (Unity layer 31).
        /// Avoids referencing Blizzard.T5.Game GameLayer enum from this assembly.
        /// </summary>
        private static void EnsureTrayContentsOnCameraMaskLayer()
        {
            const int cameraMaskLayer = 31; // GameLayer.CameraMask
            // Prefer live Sets contents layer if available (same mask).
            int maskLayer = cameraMaskLayer;
            try
            {
                if (s_live != null && s_live.m_trayObject != null)
                {
                    CameraMask liveMask = s_live.m_trayObject.GetComponent<CameraMask>();
                    if (liveMask != null && liveMask.m_ClipObjects != null)
                        maskLayer = liveMask.m_ClipObjects.layer;
                }
            }
            catch { /* keep 31 */ }

            if (s_contents != null)
                SetLayerRecursive(s_contents.gameObject, maskLayer);
            for (int i = 0; i < s_items.Count; i++)
            {
                SetFilterItem item = s_items[i];
                if (item == null || item.gameObject == null)
                    continue;
                SetLayerRecursive(item.gameObject, maskLayer);
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null)
                return;
            go.layer = layer;
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    all[i].gameObject.layer = layer;
            }
        }

        /// <summary>
        /// Keep / recreate ContentsBone under Contents (ClearContents used to Destroy it).
        /// </summary>
        private static void EnsureContentsBone(SetFilterTray liveTray)
        {
            if (s_contents == null)
                return;

            // Re-find if destroyed or missing.
            if (s_contentsBone == null || s_contentsBone.gameObject == null)
            {
                s_contentsBone = null;
                Transform existing = s_contents.Find("ContentsBone");
                if (existing == null)
                {
                    Transform[] kids = s_contents.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < kids.Length; i++)
                    {
                        if (kids[i] != null && kids[i].name == "ContentsBone")
                        {
                            existing = kids[i];
                            break;
                        }
                    }
                }
                if (existing != null)
                    s_contentsBone = existing;
            }

            if (s_contentsBone == null)
            {
                GameObject boneGo = new GameObject("ContentsBone");
                GameUtils.SetParent(boneGo, s_contents.gameObject, false);
                s_contentsBone = boneGo.transform;
            }

            if (liveTray?.m_contentsBone != null && s_contentsBone != null)
            {
                Transform liveBone = liveTray.m_contentsBone.transform;
                s_contentsBone.localPosition = liveBone.localPosition;
                s_contentsBone.localRotation = liveBone.localRotation;
                s_contentsBone.localScale = liveBone.localScale;
            }
        }

        private static void ResetScrollToTop()
        {
            if (s_scroller == null)
                return;
            try
            {
                s_scroller.SetScrollImmediate(0f);
                s_scroller.ScrollValue = 0f;
                s_scroller.ImmediateScrollValue = 0f;
                s_scroller.UpdateScroll();
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.ResetScrollToTop failed", ex);
            }
        }

        private static void ApplyVisibleAffectedClip()
        {
            if (s_scroller == null)
            {
                DebugLog.Warning("KeywordSetTray: no UIBScrollable — cannot clip overflow");
                return;
            }
            try
            {
                // Exact Sets Arrange path — UIBScrollable alone decides active. No fullyInside /
                // inset / extra SetActive layer (that caused ~4 tiles + empty wood + skipped rows).
                s_scroller.UpdateAndFireVisibleAffectedObjects();

                for (int i = 0; i < s_items.Count; i++)
                {
                    SetFilterItem item = s_items[i];
                    if (item == null || item.gameObject == null || !item.gameObject.activeInHierarchy)
                        continue;

                    bool selected = false;
                    try
                    {
                        var field = HarmonyLib.AccessTools.Field(typeof(SetFilterItem), "m_isSelected");
                        if (field != null)
                            selected = (bool)field.GetValue(item);
                    }
                    catch
                    {
                        // ignore
                    }
                    ApplyIdleChrome(item, selected);


                }

                RefreshRowTextAlignment();
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordSetTray.ApplyVisibleAffectedClip failed", ex);
            }
        }

        private static void ActivateTrayHierarchy()
        {
            if (s_trayObject != null)
                s_trayObject.SetActive(true);
            ActivateChain(s_contents, s_trayObject != null ? s_trayObject.transform : null);
            if (s_contents != null)
                s_contents.gameObject.SetActive(true);
        }

        private static void ActivateChain(Transform leaf, Transform stopAt)
        {
            if (leaf == null)
                return;
            Transform t = leaf;
            int guard = 0;
            while (t != null && guard++ < 24)
            {
                if (!t.gameObject.activeSelf)
                    t.gameObject.SetActive(true);
                if (stopAt != null && t == stopAt)
                    break;
                t = t.parent;
            }
        }

        private static Transform ResolveContentsTransform(SetFilterTray live, GameObject trayClone)
        {
            if (live.m_contents == null)
                return trayClone.transform;

            Transform liveTray = live.m_trayObject.transform;
            Transform liveContents = live.m_contents.transform;
            if (liveContents == liveTray || liveContents.IsChildOf(liveTray))
            {
                string rel = GetRelativePath(liveTray, liveContents);
                if (!string.IsNullOrEmpty(rel))
                {
                    Transform found = trayClone.transform.Find(rel);
                    if (found != null)
                        return found;
                }
            }

            Transform named = trayClone.transform.Find(live.m_contents.name);
            if (named != null)
                return named;

            Transform[] all = trayClone.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == live.m_contents.name)
                    return all[i];
            }
            return trayClone.transform;
        }

        private static string GetRelativePath(Transform root, Transform child)
        {
            if (root == null || child == null)
                return null;
            if (child == root)
                return "";
            var parts = new List<string>();
            Transform cur = child;
            int guard = 0;
            while (cur != null && cur != root && guard++ < 16)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            if (cur != root)
                return null;
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static void ClearContents()
        {
            ClearExpansionItems();
            ReleaseWideRows();
            for (int i = 0; i < s_items.Count; i++)
            {
                if (s_items[i] != null)
                {
                    CounterTextLayout.SetRowVisible(s_items[i].gameObject, false);
                    UnityEngine.Object.Destroy(s_items[i].gameObject);
                }
            }
            s_items.Clear();
            s_keywordItems.Clear();
            ResetKeywordRowCache();
            ResetExtraRowCache();
            s_extraSelectionRefresh.Clear();
            s_modeItem = null;
            s_notItem = null;
            s_unfilteredHeaders.Clear();

            if (s_contents == null)
                return;
            for (int i = s_contents.childCount - 1; i >= 0; i--)
            {
                Transform child = s_contents.GetChild(i);
                if (child == null)
                    continue;
                // Never destroy ContentsBone — layout origin for Sets-style Arrange.
                if (s_contentsBone != null && child == s_contentsBone)
                    continue;
                if (child.name == "ContentsBone")
                {
                    s_contentsBone = child;
                    continue;
                }
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static SetFilterItem AddHeader(string text, FormatType format)
        {
            s_unfilteredHeaders.Add(true);
            GameObject prefab = s_headerPrefab != null ? s_headerPrefab : s_itemPrefab;
            GameObject go = UnityEngine.Object.Instantiate(prefab);
            GameUtils.SetParent(go, s_contents.gameObject, false);
            // Sets keeps inactive until Arrange; we activate after Height is assigned.
            go.SetActive(false);

            SetFilterItem item = go.GetComponent<SetFilterItem>();
            UIBScrollableItem scrollItem = go.GetComponent<UIBScrollableItem>();
            if (item != null)
            {
                item.IsHeader = true;
                CounterTextLayout.UseDirectCaption(item);
                item.Text = text;
                item.FormatType = format;
                item.Callback = null;
                item.ShowTooltip = false;
                item.EditModeOnly = false;
                AssignHeightFromScrollItem(item, scrollItem);
                ApplyGridItemScale(item, scrollItem);
                SafeSetSelected(item, false);
                SanitizeItemVisuals(item);
                PegUIElement peg = go.GetComponent<PegUIElement>();
                peg?.SetEnabled(false, true);
            }
            s_items.Add(item);
            return item;
        }

        private static SetFilterItem AddItem(string text, FormatType format, Action onClick)
        {
            s_unfilteredHeaders.Add(false);
            SetFilterItem item = CreateNavigationItem(s_contents, text, format, onClick);
            s_items.Add(item);
            return item;
        }

        private static void AssignHeightFromScrollItem(SetFilterItem item, UIBScrollableItem scrollItem)
        {
            if (item == null)
                return;
            float h = 0f;
            if (scrollItem != null)
                h = scrollItem.m_size.z;
            if (h < 0.01f)
                h = item.Height;
            if (h < 0.01f)
                h = 0.45f;
            item.Height = h;
        }

        private static Transform ResolveNamedChild(SetFilterTray live, GameObject trayClone, GameObject liveNamed)
        {
            if (liveNamed == null || live?.m_trayObject == null || trayClone == null)
                return null;
            return ResolveRelative(live.m_trayObject.transform, trayClone.transform, liveNamed.transform);
        }

        private static Transform ResolveRelative(Transform liveRoot, Transform cloneRoot, Transform liveChild)
        {
            if (liveRoot == null || cloneRoot == null || liveChild == null)
                return null;
            if (liveChild == liveRoot)
                return cloneRoot;
            if (!liveChild.IsChildOf(liveRoot) && liveChild != liveRoot)
            {
                // Not under tray — try by name under clone.
                Transform[] all = cloneRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].name == liveChild.name)
                        return all[i];
                }
                return null;
            }
            string rel = GetRelativePath(liveRoot, liveChild);
            if (string.IsNullOrEmpty(rel))
                return cloneRoot;
            Transform found = cloneRoot.Find(rel);
            if (found != null)
                return found;
            Transform[] deep = cloneRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < deep.Length; i++)
            {
                if (deep[i] != null && deep[i].name == liveChild.name)
                    return deep[i];
            }
            return null;
        }

        private static string Format(Vector3 v)
        {
            return $"({v.x:F2},{v.y:F2},{v.z:F2})";
        }
    }
}
