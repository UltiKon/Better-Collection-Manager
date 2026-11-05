using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>
    /// Compact "K"/"K*" chip: CM Sets cork clone, reparented to BnetBar with
    /// worldPositionStays so it composites into the Friends HUD (layer 18).
    /// </summary>
    internal static partial class KeywordFilterUi
    {
        private const string RootName = "BCM_KeywordFilterRoot";
        private const string OpenButtonName = "BCM_KeywordFilterOpenButton";
        private const string PanelName = "BCM_KeywordFilterPanel";
        private const string CorkPivotName = "BCM_KeywordCorkPivot";
        private const string PlaqueDebugHomeName = "BCM_KeywordPlaqueDebugHome";
        private const float RowHeight = 0.32f;
        private const float VisibleRows = 10f;
        private const float MaxBoundsScreenWidthFraction = 0.35f;

        private static object s_buildCoroutine;
        private static GameObject s_root;
        private static GameObject s_panel;
        private static Transform s_scrollContent;
        private static PegUIElement s_openButton;
        private static GameObject s_corkPivot;
        private static UberText s_openLabelText;
        private static bool s_openUsesKeywordsLabel;
        private static readonly List<GameObject> s_extraPlaques = new List<GameObject>();
        private static readonly List<GameObject> s_rowObjects = new List<GameObject>();
        private static bool s_panelShown;
        private static bool s_built;
        private static bool s_buildAborted;
        /// <summary>True only between DoEnterCollectionManagerEvents and Exit/Unload.</summary>
        private static bool s_cmSessionActive;
        private static float s_scrollY;
        private static float s_scrollMinY;
        private static GameObject s_itemTemplate;

        // Physics input bypass — PegUI often never delivers RELEASE on BnetBar plaques.
        private static FilterTrayKind s_physicsHoverKind = FilterTrayKind.None;
        private static GameObject s_physicsHoverGo;
        private static FilterTrayKind s_physicsPressKind = FilterTrayKind.None;

        /// <summary>Cached idle UberText color (cream plaque brown); active uses dark red.</summary>
        private static Color? s_plaqueLabelIdleColor;
        private static readonly Color PlaqueLabelActiveColor = new Color(0.55f, 0.05f, 0.05f, 1f);
        private static readonly Color PlaqueLabelIdleFallback = new Color(0.35f, 0.22f, 0.12f, 1f);
        /// <summary>Max collider screen height so stacked plaques (~73px centers) do not overlap.</summary>
        private const float MaxColliderScreenHeightPx = 58f;
        private const float ColliderScreenShrink = 0.90f;

        private static AccessTools.FieldRef<CollectibleDisplay, SetFilterTray> s_setFilterTray;
        private static AccessTools.FieldRef<SetFilterTray, GameObject> s_itemPrefabField;
        private static AccessTools.FieldRef<SetFilterTray, List<SetFilterItem>> s_items;
        private static AccessTools.FieldRef<SetFilterTray, CollectionSetFilterDropdownToggle> s_toggleButton;
        private static bool s_fieldsReady;

        // Bottom-bar layout: persist vanilla local poses so Exit/Unload restores them.
        private static Transform s_setFilterContainer;
        private static Transform s_manaTabManager;
        private static Transform s_searchBar;
        private static Vector3 s_origSetFilterContainerLocal;
        private static Vector3 s_origManaTabLocal;
        private static Vector3 s_origSearchLocal;
        private static bool s_bottomBarOffsetsApplied;

        public static void OnCollectionManagerLeave()
        {
            ComboCollectionCards.Reset();
            CollectionPageDiagnostics.Stop("collection exit");
            s_cmSessionActive = false;
            StopBuildCoroutine();
            KeywordFilterState.Clear();
            ExtraFilterState.ClearAll();
            ExpansionFilterState.Clear();
            KeywordCatalog.Invalidate();
            RestoreBottomBarLayout("CM leave");
            DestroyUi();
            s_buildAborted = false;
            s_itemTemplate = null;
            DebugLog.Msg("KeywordFilterUi: CM session ended — plaques torn down");
        }

        public static void OnUpdate()
        {
            KeywordSetTray.PollOutsideClick();
        }

        /// <summary>Called from Friends-chrome Has/Ref/Clear so the K chip shows K*.</summary>
        public static void NotifyFilterChangedFromChrome()
        {
            UpdateOpenButtonLabel();
            RefreshAllPlaqueLabels();
        }

        /// <summary>True if cursor is over any filter plaque (Keywords/Type/Race/School/Quality).</summary>
        public static bool IsPointerOverOpenButton()
        {
            return CollectionFilterMenu.IsAttached
                ? CollectionFilterMenu.IsPointerOverToggle()
                : IsPointerOverAnyFilterPlaque();
        }

        public static bool IsPointerOverAnyFilterPlaque()
        {
            try
            {
                Camera cam = FindBnetCamera(FindFriendsButton());
                if (cam == null && s_openButton != null)
                    cam = FindCameraForLayer(s_openButton.gameObject.layer);
                if (cam == null)
                    return false;
                return TryGetPlaqueUnderMouse(cam, out _, out _);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// BnetCamera Physics.RaycastAll — first hit must be our FilterPlaqueTag (else Friends/Crafting win).
        /// </summary>
        private static bool TryGetPlaqueUnderMouse(
            Camera bnetCam,
            out GameObject plaqueGo,
            out FilterTrayKind kind)
        {
            plaqueGo = null;
            kind = FilterTrayKind.None;
            if (bnetCam == null || !s_built)
                return false;

            Ray ray = bnetCam.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 800f);
            if (hits == null || hits.Length == 0)
                return false;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Transform ht = hits[0].transform;
            if (ht == null)
                return false;

            FilterPlaqueTag tag = ht.GetComponent<FilterPlaqueTag>();
            if (tag == null)
                tag = ht.GetComponentInParent<FilterPlaqueTag>();
            if (tag == null || tag.gameObject == null || !tag.gameObject.activeInHierarchy)
                return false;

            plaqueGo = tag.gameObject;
            kind = tag.Kind;
            return kind != FilterTrayKind.None;
        }

        private static void PollPlaquePhysicsInput()
        {
            if (!s_built || s_openButton == null || s_openButton.gameObject == null)
            {
                ClearPhysicsHover();
                return;
            }

            Camera cam = FindBnetCamera(FindFriendsButton());
            if (cam == null)
                cam = FindCameraForLayer(s_openButton.gameObject.layer);
            if (cam == null)
                return;

            GameObject overGo;
            FilterTrayKind overKind;
            bool overUs = TryGetPlaqueUnderMouse(cam, out overGo, out overKind);

            if (!overUs)
            {
                overGo = null;
                overKind = FilterTrayKind.None;
            }

            if (overKind != s_physicsHoverKind || overGo != s_physicsHoverGo)
            {
                SetPlaqueHoverVisual(s_physicsHoverGo, false);
                s_physicsHoverKind = overKind;
                s_physicsHoverGo = overGo;
                if (overKind != FilterTrayKind.None)
                    SetPlaqueHoverVisual(s_physicsHoverGo, true);
            }

            if (Input.GetMouseButtonDown(0))
            {
                s_physicsPressKind = overKind;
                if (overGo != null) FilterButtonFeedback.PlayPressSound(overGo.GetComponent<PegUIElement>());
            }

            if (Input.GetMouseButtonUp(0))
            {
                FilterTrayKind press = s_physicsPressKind;
                s_physicsPressKind = FilterTrayKind.None;
                // Press and release on same plaque — do not steal Friends/Crafting (first hit wasn't us).
                if (press != FilterTrayKind.None && press == overKind && overUs)
                    OnFilterPlaqueReleased(press, "physics");
            }
        }

        private static void ClearPhysicsHover()
        {
            SetPlaqueHoverVisual(s_physicsHoverGo, false);
            s_physicsHoverGo = null;
            s_physicsHoverKind = FilterTrayKind.None;
            s_physicsPressKind = FilterTrayKind.None;
        }

        private static void SetPlaqueHoverVisual(GameObject go, bool on)
        {
            if (go == null)
                return;
            try
            {
                UIBHighlight hl = go.GetComponent<UIBHighlight>();
                GameObject glow = hl != null ? hl.m_MouseOverHighlight : null;
                if (glow == null)
                    glow = FindChildGlowObject(go.transform);
                if (glow == null)
                    return;
                glow.SetActive(on);
                if (on)
                    EnsureRenderersEnabled(glow);
                if (!on && hl != null && hl.m_MouseDownHighlight != null)
                    hl.m_MouseDownHighlight.SetActive(false);
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Hide-area stole K's RELEASE — run the same toggle path as PegUI RELEASE on K.
        /// </summary>
        public static void RelayOpenButtonToggle(string reason)
        {
            DebugLog.Msg($"KeywordFilterUi: RelayOpenButtonToggle reason={reason}");
            if (CollectionFilterMenu.IsAttached)
                CollectionFilterMenu.Toggle();
            else
                OnOpenButtonReleased(null);
        }

        private static void EnsureFields()
        {
            if (s_fieldsReady)
                return;

            try
            {
                s_setFilterTray = AccessTools.FieldRefAccess<CollectibleDisplay, SetFilterTray>("m_setFilterTray");
                s_itemPrefabField = AccessTools.FieldRefAccess<SetFilterTray, GameObject>("m_itemPrefab");
                s_items = AccessTools.FieldRefAccess<SetFilterTray, List<SetFilterItem>>("m_items");
                s_toggleButton = AccessTools.FieldRefAccess<SetFilterTray, CollectionSetFilterDropdownToggle>("m_toggleButton");
                s_fieldsReady = true;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.EnsureFields failed", ex);
            }
        }

        private static void StopBuildCoroutine()
        {
            if (s_buildCoroutine != null)
            {
                LoaderCoroutines.Stop(s_buildCoroutine);
                s_buildCoroutine = null;
            }
        }

        private static IEnumerator BuildWhenReady()
        {
            // Let CM finish first paint / CraftingModeButton spawn before first TryBuild.
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;

            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (s_buildAborted || !s_cmSessionActive)
                {
                    s_buildCoroutine = null;
                    yield break;
                }

                if (!PreferredQualityMode.IsEnabled)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                BuildResult result = TryBuild();
                if (result == BuildResult.Success)
                {
                    s_buildCoroutine = null;
                    yield break;
                }
                if (result == BuildResult.HardFail)
                {
                    s_buildAborted = true;
                    s_buildCoroutine = null;
                    yield break;
                }

                yield return new WaitForSeconds(attempt < 10 ? 0.25f : 0.5f);
            }

            DebugLog.Warning("KeywordFilterUi: timed out waiting for SetFilterTray / Select Set toggle");
            s_buildCoroutine = null;
        }

        private enum BuildResult
        {
            Success,
            SoftRetry,
            HardFail,
        }

        private static BuildResult TryBuild()
        {
            if (!s_cmSessionActive || !PreferredQualityMode.IsEnabled)
                return BuildResult.SoftRetry;

            if (s_built && CollectionFilterMenu.IsAttached)
                return BuildResult.Success;

            EnsureFields();
            if (!s_fieldsReady)
                return BuildResult.SoftRetry;

            CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
            if (display == null || !(display is CollectionManagerDisplay))
                return BuildResult.SoftRetry;
            if (!display.SetFilterTrayInitialized())
                return BuildResult.SoftRetry;

            // Crafting button / deck tray must exist — real CM UI, not pre-CM BnetBar-only.
            try
            {
                if (display.m_craftingModeButton == null || !display.m_craftingModeButton.gameObject.activeInHierarchy)
                    return BuildResult.SoftRetry;
            }
            catch
            {
                return BuildResult.SoftRetry;
            }

            SetFilterTray tray = s_setFilterTray != null ? s_setFilterTray(display) : null;
            if (tray == null || tray.m_trayObject == null)
                return BuildResult.SoftRetry;

            CollectionSetFilterDropdownToggle toggle = ResolveSelectSetToggle(tray);
            if (toggle == null)
                return BuildResult.SoftRetry;

            try
            {
                GameObject template = ResolveItemTemplate(tray);
                if (template == null)
                {
                    DebugLog.Error("KeywordFilterUi: no SetFilterItem template. Aborting.");
                    return BuildResult.HardFail;
                }

                DestroyUi(); // tears down stretched leftovers from prior BnetBar attempts

                s_itemTemplate = ResolveItemTemplate(tray);
                if (s_itemTemplate == null)
                {
                    DebugLog.Error("KeywordFilterUi: item template null after DestroyUi. Aborting.");
                    return BuildResult.HardFail;
                }

                // Reuse the expansion button; the cloned tray holds the hub and mod submenus.
                KeywordSetTray.EnsureBuilt(tray);
                if (!CollectionFilterMenu.Attach(tray, toggle))
                {
                    DestroyUi();
                    return BuildResult.HardFail;
                }

                s_built = true;
                s_panelShown = false;
                DebugLog.Msg("KeywordFilterUi: shared filter menu attached to expansion button");
                return BuildResult.Success;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.TryBuild failed", ex);
                DestroyUi();
                return BuildResult.HardFail;
            }
        }

        private static CollectionSetFilterDropdownToggle ResolveSelectSetToggle(SetFilterTray tray)
        {
            try
            {
                if (tray != null && s_toggleButton != null)
                {
                    CollectionSetFilterDropdownToggle t = s_toggleButton(tray);
                    if (t != null)
                        return t;
                }
                return UnityEngine.Object.FindObjectOfType<CollectionSetFilterDropdownToggle>();
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.ResolveSelectSetToggle failed", ex);
                return null;
            }
        }

        private static BnetBarFriendButton FindFriendsButton()
        {
            try
            {
                BnetBarFriendButton btn = BnetBarFriendButton.Get();
                if (btn != null)
                    return btn;

                BnetBar bar = BnetBar.Get();
                if (bar != null && bar.m_friendButton != null)
                    return bar.m_friendButton;
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.FindFriendsButton failed", ex);
            }
            return null;
        }

        private static GameObject ResolveItemTemplate(SetFilterTray tray)
        {
            if (s_itemTemplate != null)
                return s_itemTemplate;

            try
            {
                if (tray != null && s_itemPrefabField != null)
                {
                    GameObject prefab = s_itemPrefabField(tray);
                    if (prefab != null)
                    {
                        s_itemTemplate = prefab;
                        DebugLog.Msg($"KeywordFilterUi template from m_itemPrefab '{prefab.name}'");
                        return s_itemTemplate;
                    }
                }

                SetFilterItem sample = FindSampleItem(tray);
                if (sample != null && sample.gameObject != null)
                {
                    s_itemTemplate = sample.gameObject;
                    DebugLog.Msg($"KeywordFilterUi template from live SetFilterItem '{sample.Text}'");
                    return s_itemTemplate;
                }

                SetFilterItem[] found = UnityEngine.Object.FindObjectsOfType<SetFilterItem>();
                if (found != null)
                {
                    for (int i = 0; i < found.Length; i++)
                    {
                        SetFilterItem it = found[i];
                        if (it == null || it.IsHeader || it.gameObject == null)
                            continue;
                        if (it.gameObject.name.StartsWith("BCM_", StringComparison.Ordinal)
                            || it.gameObject.name == OpenButtonName)
                            continue;
                        s_itemTemplate = it.gameObject;
                        DebugLog.Msg($"KeywordFilterUi template from scene SetFilterItem '{it.Text}'");
                        return s_itemTemplate;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.ResolveItemTemplate failed", ex);
            }

            return null;
        }

        private static void BuildOpenButton(CollectionSetFilterDropdownToggle toggle)
        {
            // Clone Select Set cork. Do NOT rewrite BoxCollider (meshColΔ blow-up).
            // Prefer SetFilterContainer parent so tray hierarchy doesn't cull the extra cork.
            Vector3 liveToggleWorld = toggle.transform.position;
            Quaternion liveWorldRot = toggle.transform.rotation;
            Vector3 liveLossy = toggle.transform.lossyScale;

            Transform trayRoot = toggle.transform.parent; // SetFilterTray(Clone)
            Transform container = trayRoot != null ? trayRoot.parent : null; // SetFilterContainer
            Transform host = container != null ? container : (trayRoot != null ? trayRoot : toggle.transform);

            // Snapshot original collider locals BEFORE instantiate/strip.
            BoxCollider srcBox = toggle.GetComponent<BoxCollider>();
            Vector3 srcBoxCenter = srcBox != null ? srcBox.center : Vector3.zero;
            Vector3 srcBoxSize = srcBox != null ? srcBox.size : Vector3.one;
            bool srcBoxTrigger = srcBox != null && srcBox.isTrigger;

            GameObject go = UnityEngine.Object.Instantiate(toggle.gameObject);
            go.name = OpenButtonName;
            go.SetActive(false);

            GameUtils.SetParent(go, host.gameObject, true);
            go.transform.position = liveToggleWorld;
            go.transform.rotation = liveWorldRot;
            ApplyWorldScale(go.transform, liveLossy);
            SetLayerRecursive(go, toggle.gameObject.layer);

            StripSelectSetToggleBehavior(go);

            // Restore exact world pose after strip; leave BoxCollider as clone original.
            go.transform.position = liveToggleWorld;
            go.transform.rotation = liveWorldRot;
            ApplyWorldScale(go.transform, liveLossy);
            PreserveOriginalBoxCollider(go, srcBoxCenter, srcBoxSize, srcBoxTrigger);

            // Match Select Set child activeSelf exactly (glow/highlight OFF like Sets).
            SyncChildActiveFromSource(toggle.gameObject, go);
            DumpMeshRenderers("src", toggle.gameObject);
            DumpMeshRenderers("K-samePose", go);

            go.SetActive(true);
            go.transform.SetAsLastSibling();

            Camera uiCam = FindCmUiCamera(go);
            // Step 2 — visibility proof at identical pose as Select Set.
            LogMeshVisibility("same-pose proof", go, toggle.gameObject, uiCam);
            float samePoseMeshColK = MeshColDelta(go);
            float samePoseMeshColSrc = MeshColDelta(toggle.gameObject);
            bool kVis = AnyRendererVisible(go);
            bool srcVis = AnyRendererVisible(toggle.gameObject);
            DebugLog.Msg(
                $"KeywordFilterUi same-pose: parent={host.name} " +
                $"isVisibleK={kVis} isVisibleSrc={srcVis} " +
                $"meshColΔK={samePoseMeshColK:F3} meshColΔSrc={samePoseMeshColSrc:F3} " +
                $"world={FormatVec(go.transform.position)} srcWorld={FormatVec(liveToggleWorld)}");

            if (!kVis && srcVis)
            {
                DebugLog.Warning(
                    "KeywordFilterUi: K invisible at same pose — enabling cork meshes only (not glows)");
                EnsureCorkMeshesLikeSource(go, toggle);
                SyncChildActiveFromSource(toggle.gameObject, go);
                SetLayerRecursive(go, toggle.gameObject.layer);
                LogMeshVisibility("same-pose after force", go, toggle.gameObject, uiCam);
            }

            // Temporary same-pose; PlaceAboveFriendsList() reparents to BnetBar (worldPositionStays).
            go.transform.position = liveToggleWorld;
            go.transform.rotation = liveWorldRot;
            ApplyWorldScale(go.transform, liveLossy);

            // Label after place so UberText bounds don't skew same-pose meshColΔ.
            s_openLabelText = EnsureKLabel(go);
            SetOpenChipLabel(OpenLabel());
            ForceOpenChipLabelVisible(go);
            // Collider must still be the original Select Set box — never AlignColliderToMesh.
            PreserveOriginalBoxCollider(go, srcBoxCenter, srcBoxSize, srcBoxTrigger);

            // Idle chrome like Sets (glow off until hover). Final PegUI wire after friends place.
            SyncChildActiveFromSource(toggle.gameObject, go);
            s_openButton = go.GetComponent<PegUIElement>();
            if (s_openButton == null)
                s_openButton = go.AddComponent<PegUIElement>();

            go.transform.SetAsLastSibling();
            LogMeshVisibility("clone-ready", go, toggle.gameObject, uiCam);
            DumpMeshRenderers("K-clone", go);
            DebugLog.Msg(
                $"KeywordFilterUi K cork clone ready parent={host.name} " +
                $"local={FormatVec(go.transform.localPosition)} world={FormatVec(go.transform.position)} " +
                $"meshColΔK={MeshColDelta(go):F3} meshColΔSrc={MeshColDelta(toggle.gameObject):F3}");
        }

        /// <summary>
        /// Build cork under SetFilterContainer (correct CM lossy), set world pose above Friends,
        /// then SetParent(BnetBar, worldPositionStays:true) so it composites into HUD layer 18.
        /// Never manually copy Friends localScale after reparent.
        /// </summary>
        private static bool PlaceAboveFriendsList(GameObject go, CollectionSetFilterDropdownToggle styleSrc)
        {
            if (go == null)
                return false;

            BnetBarFriendButton friends = FindFriendsButton();
            if (friends == null)
                return false;

            Transform bnetBar = ResolveBnetBarParent(friends);
            if (bnetBar == null)
            {
                DebugLog.Error("KeywordFilterUi PlaceAboveFriends: BnetBar parent missing");
                return false;
            }

            Camera bnetCam = FindBnetCamera(friends);
            if (bnetCam == null)
            {
                DebugLog.Error("KeywordFilterUi PlaceAboveFriends: no BnetCamera");
                return false;
            }

            Camera boxCam = FindCmUiCamera(styleSrc != null ? styleSrc.gameObject : go);
            const float gapPx = 12f;

            // Gold Crafting/Back plaque on BnetBar (layer 18) — never Friends pill; never BoxCam-only soft-pass.
            string plaquePath;
            GameObject plaque = ReplaceOpenButtonWithBackStyleClone(
                go, friends, bnetBar, bnetCam, gapPx, out plaquePath);
            if (plaque != null)
            {
                go = plaque;
                bool plaqueUnderBnet = go.transform.IsChildOf(bnetBar);
                Camera plaqueCam = plaqueUnderBnet ? bnetCam : (FindCmUiCamera(go) ?? boxCam ?? bnetCam);

                Rect frScr = GetFriendsScreenRect(friends, bnetCam);
                Rect kScr = GetPlaqueScreenRect(plaqueCam, go);
                float plaqueGap = kScr.yMin - frScr.yMax;
                bool plaqueOverlap = ScreenRectsOverlap(kScr, frScr, 0f);
                bool nearFriendsBand = kScr.width >= 1f
                    && Mathf.Abs(kScr.center.x - frScr.center.x) <= 80f
                    && kScr.yMin >= frScr.yMin - 20f
                    && kScr.yMin <= frScr.yMax + 100f;
                bool vis = AnyRendererVisible(go);
                if (!vis)
                    LogPlaqueVisibilityFail(go, plaqueCam);
                float plaqueLossyRatio = LossyScaleAxisRatio(go.transform.lossyScale);
                bool plaqueSizeOk = kScr.width >= 100f && kScr.height >= 45f;
                bool plaqueGapOk = plaqueGap >= 8f && plaqueGap <= 28f;
                // HARD: isVisible required for final assert (EOF re-check in BuildWhenReady).
                // Same-frame isVisible is often false — size/gap/layer must pass; visibility deferred.
                bool plaqueAssert = plaqueUnderBnet && plaqueSizeOk && plaqueGapOk && !plaqueOverlap
                    && go.activeInHierarchy && AnyRendererEnabled(go) && plaqueLossyRatio < 3f
                    && go.layer == friends.gameObject.layer;

                DebugLog.Verbose(
                    $"KeywordFilterUi PlaceAboveFriends: visual={plaquePath} " +
                    $"K={kScr.width:F0}x{kScr.height:F0} gap={plaqueGap:F1} " +
                    $"isVisible={vis} assert={plaqueAssert}");
                DebugLog.Msg(
                    $"KeywordFilterUi: Keywords plaque ready visual={plaquePath} " +
                    $"K={kScr.width:F0}x{kScr.height:F0}");

                if (!plaqueAssert)
                {
                    DebugLog.Error("KeywordFilterUi PlaceAboveFriends: ASSERT FAILED (plaque BnetBar)");
                    return false;
                }
                // Re-wire PegUI after any final sibling/layer tweaks.
                WirePlaqueInteraction(go, FilterTrayKind.Keywords, OpenLabel());
                ForceOpenChipLabelVisible(go);
                SetOpenChipLabel(OpenLabel());
                BuildExtraPlaqueStack(go, friends, bnetBar, bnetCam, gapPx);
                // Provisional success — BuildWhenReady EOF must see isVisible.
                return true;
            }

            // 1) Correct CM cork lossy while still under SetFilterContainer.
            Vector3 desiredLossy = go.transform.lossyScale;
            if (styleSrc != null)
            {
                go.transform.rotation = styleSrc.transform.rotation;
                ApplyWorldScale(go.transform, styleSrc.transform.lossyScale);
                desiredLossy = go.transform.lossyScale;
            }
            if (LossyScaleAxisRatio(desiredLossy) >= 2.5f || AverageAbs(desiredLossy) < 0.5f)
            {
                float u = Mathf.Max(6.5f, AverageAbs(desiredLossy));
                desiredLossy = new Vector3(u, u, u);
                ApplyWorldScale(go.transform, desiredLossy);
                desiredLossy = go.transform.lossyScale;
            }

            Rect setsFullBox = default;
            if (boxCam != null && styleSrc != null)
                setsFullBox = WorldBoundsToScreenRect(boxCam, GetFullCorkMeshBounds(styleSrc.gameObject));

            LogFriendsAnchor(friends, bnetCam);
            DebugLog.Msg(
                $"KeywordFilterUi PlaceAboveFriends: HUD reparent plan; " +
                $"cmParent={GetHierarchyPath(go.transform.parent)} bnetBar={GetHierarchyPath(bnetBar)} " +
                $"desiredLossy={FormatVec(desiredLossy)} friendsLayer={friends.gameObject.layer}");

            // 2) World pose above Friends (still under CM). Rotation finalized after reparent via BnetCamera.
            go.SetActive(true);
            float localUp = EstimateFriendsLocalUpOffset(friends, bnetCam);
            Vector3 abovePos = friends.transform.position
                + friends.transform.TransformVector(Vector3.up * localUp);
            go.transform.position = abovePos;
            go.transform.rotation = friends.transform.rotation;
            ApplyWorldScale(go.transform, desiredLossy);
            Vector3 worldPos = go.transform.position;
            Quaternion worldRot = go.transform.rotation;
            Vector3 lossyBeforeReparent = go.transform.lossyScale;

            // 3) Reparent with worldPositionStays.
            DestroyCorkPivot();
            go.transform.SetParent(bnetBar, true);
            SetLayerRecursive(go, friends.gameObject.layer);

            float lossyRatio = LossyScaleAxisRatio(go.transform.lossyScale);
            string parentMode = "BnetBar/worldPositionStays";

            if (lossyRatio >= 2.5f || AverageAbs(go.transform.lossyScale) < 0.5f)
            {
                DebugLog.Warning(
                    $"KeywordFilterUi PlaceAboveFriends: lossy crushed after reparent " +
                    $"{FormatVec(go.transform.lossyScale)} ratio={lossyRatio:F2} — identity pivot");

                go.transform.SetParent(null, true);
                go.transform.position = worldPos;
                go.transform.rotation = worldRot;
                go.transform.localScale = SanitizeLocalScale(desiredLossy);

                GameObject pivot = new GameObject(CorkPivotName);
                s_corkPivot = pivot;
                pivot.transform.SetParent(bnetBar, false);
                pivot.transform.position = worldPos;
                pivot.transform.rotation = Quaternion.identity;
                Vector3 pLossy = pivot.transform.parent != null
                    ? pivot.transform.parent.lossyScale
                    : Vector3.one;
                float px = Mathf.Abs(pLossy.x) < 1e-4f ? 1f : pLossy.x;
                float py = Mathf.Abs(pLossy.y) < 1e-4f ? 1f : pLossy.y;
                float pz = Mathf.Abs(pLossy.z) < 1e-4f ? 1f : pLossy.z;
                pivot.transform.localScale = SanitizeLocalScale(new Vector3(1f / px, 1f / py, 1f / pz));
                SetLayerRecursive(pivot, friends.gameObject.layer);

                go.transform.SetParent(pivot.transform, true);
                SetLayerRecursive(go, friends.gameObject.layer);
                lossyRatio = LossyScaleAxisRatio(go.transform.lossyScale);
                parentMode = "BnetBar/identityPivot";
            }

            // Plaque path already attempted above. Cork FaceAndSize last resort — never Friends pill.
            string visualKind = "cork";
            Vector3 chosenEuler = go.transform.eulerAngles;
            DebugLog.Warning(
                "KeywordFilterUi PlaceAboveFriends: plaque failed — try cork FaceAndSize");
            bool corkFaced = FaceAndSizeCorkOnHud(go, friends, bnetCam, gapPx, out chosenEuler);
            if (!corkFaced)
            {
                DebugLog.Error("KeywordFilterUi PlaceAboveFriends: no usable HUD open visual");
                return false;
            }

            if (go == null)
                return false;

            go.transform.SetAsLastSibling();
            EnsureRenderersEnabled(go);
            EnsureClickCollider(go);
            // Plaque path already wired RELEASE→tray; cork fallback needs WireKeywordCorkInteraction.
            bool isPlaque = visualKind != null && visualKind != "cork"
                && (visualKind.IndexOf("Done", StringComparison.OrdinalIgnoreCase) >= 0
                    || visualKind.IndexOf("Craft", StringComparison.OrdinalIgnoreCase) >= 0
                    || visualKind.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isPlaque)
                WireKeywordCorkInteraction(go);
            ForceOpenChipLabelVisible(go);
            SetOpenChipLabel(OpenLabel());

            Rect friendsScr = GetFriendsScreenRect(friends, bnetCam);
            // Strategy A plaque is BoxCamera-native — measure with its camera, not Bnet.
            Camera measureCam = bnetCam;
            Camera boxCamMeasure = FindCmUiCamera(go);
            bool underBnet = go.transform.IsChildOf(bnetBar);
            if (!underBnet && boxCamMeasure != null)
                measureCam = boxCamMeasure;

            Rect kFinal = GetScreenRect(measureCam, go, corkOnly: false);
            if (kFinal.width < 2f || kFinal.height < 2f)
                kFinal = WorldBoundsToScreenRect(measureCam, GetFullCorkMeshBounds(go));

            float screenGap = kFinal.yMin - friendsScr.yMax;
            bool overlapFriends = ScreenRectsOverlap(kFinal, friendsScr, 0f);
            lossyRatio = LossyScaleAxisRatio(go.transform.lossyScale);
            bool sizeOk = isPlaque
                ? (kFinal.width >= 80f && kFinal.height >= 40f)
                : (kFinal.width > 30f && kFinal.height > 30f);
            bool gapOk = screenGap >= 10f;
            // Strategy A stays under CM; Strategy B under BnetBar — both OK.
            bool parentOk = underBnet || isPlaque;
            bool layerOk = underBnet
                ? go.layer == friends.gameObject.layer
                : true;
            bool activeOk = go.activeInHierarchy;
            bool rendsOk = AnyRendererEnabled(go);
            bool assertOk = lossyRatio < 3f && sizeOk && gapOk && !overlapFriends
                && parentOk && layerOk && activeOk && rendsOk;

            string camTag = measureCam == bnetCam ? "bnet" : "box";
            DebugLog.Msg(
                $"KeywordFilterUi PlaceAboveFriends SCALE: mode={parentMode} visual={visualKind} " +
                $"beforeReparent={FormatVec(lossyBeforeReparent)} after={FormatVec(go.transform.lossyScale)} " +
                $"localScale={FormatVec(go.transform.localScale)} euler={FormatVec(chosenEuler)} " +
                $"lossyRatio={lossyRatio:F3} layer={go.layer}");
            DebugLog.Msg(
                $"KeywordFilterUi PlaceAboveFriends SCREEN: " +
                $"Friends(bnet)=[{friendsScr.xMin:F0}..{friendsScr.xMax:F0}] y=[{friendsScr.yMin:F0}..{friendsScr.yMax:F0}] h={friendsScr.height:F0} " +
                $"Sets(box)={setsFullBox.width:F0}x{setsFullBox.height:F0} " +
                $"K({camTag})={kFinal.width:F0}x{kFinal.height:F0} " +
                $"K y=[{kFinal.yMin:F0}..{kFinal.yMax:F0}] gapPx={screenGap:F1} overlapFriends={overlapFriends}");
            DebugLog.Msg(
                $"KeywordFilterUi PlaceAboveFriends ASSERT: ok={assertOk} visual={visualKind} " +
                $"sizeOk={sizeOk} lossyRatio<3={lossyRatio < 3f} gap≥10={gapOk} " +
                $"overlap=False={!overlapFriends} parentOk={parentOk} underBnet={underBnet} layer={layerOk}");
            DebugLog.Msg(
                $"KeywordFilterUi PlaceAboveFriends DONE: path={GetHierarchyPath(go.transform)} " +
                $"world={FormatVec(go.transform.position)} chosenEuler={FormatVec(chosenEuler)} visual={visualKind}");

            if (!assertOk)
                DebugLog.Error("KeywordFilterUi PlaceAboveFriends: ASSERT FAILED");

            return true;
        }

        private static Transform ResolveBnetBarParent(BnetBarFriendButton friends)
        {
            if (friends != null && friends.transform.parent != null)
                return friends.transform.parent;
            try
            {
                BnetBar bar = BnetBar.Get();
                if (bar != null)
                    return bar.transform;
            }
            catch
            {
                // ignore
            }
            return friends != null ? friends.transform : null;
        }

        private static float EstimateFriendsLocalUpOffset(BnetBarFriendButton friends, Camera bnetCam)
        {
            // Friends local +Y that clears the pill + cork half + ~12px gap (approx in local units).
            float friendsH = EstimateFriendsScreenHeight(friends, bnetCam);
            if (bnetCam != null
                && TryGetScreenUpWorldBasis(bnetCam, friends.transform.position, out Vector3 screenUp, out float worldPerPx))
            {
                float worldGap = (friendsH * 0.55f + 36f + 12f) * worldPerPx;
                // Convert world screen-up offset into friends-local up magnitude.
                Vector3 friendsUp = friends.transform.TransformVector(Vector3.up);
                float friendsUpMag = friendsUp.magnitude;
                if (friendsUpMag > 1e-4f)
                {
                    float along = Vector3.Dot(screenUp * worldGap, friendsUp.normalized);
                    if (along > 0.05f)
                        return along / friendsUpMag;
                }
            }
            return 2.2f;
        }

        /// <summary>
        /// After BnetBar reparent: pick rotation maximizing BnetCamera AABB area with height&gt;20,
        /// then uniform-scale localScale so K height ≈ 0.9× Friends, then gap above Friends.
        /// Returns false if no candidate yields height&gt;20 (no Friends visual fallback).
        /// </summary>
        private static bool FaceAndSizeCorkOnHud(
            GameObject go,
            BnetBarFriendButton friends,
            Camera bnetCam,
            float gapPx,
            out Vector3 chosenEuler)
        {
            chosenEuler = go != null ? go.transform.eulerAngles : Vector3.zero;
            if (go == null || friends == null || bnetCam == null)
                return false;

            Vector3 worldPos = go.transform.position;
            Quaternion baseRot = friends.transform.rotation;

            var candidates = new List<Quaternion>();
            Vector3[] extras =
            {
                Vector3.zero,
                new Vector3(90f, 0f, 0f),
                new Vector3(-90f, 0f, 0f),
                new Vector3(0f, 90f, 0f),
                new Vector3(0f, -90f, 0f),
                new Vector3(0f, 0f, 90f),
                new Vector3(0f, 0f, -90f),
                new Vector3(180f, 0f, 0f),
                new Vector3(0f, 180f, 0f),
                new Vector3(90f, 0f, 90f),
                new Vector3(-90f, 0f, 90f),
                new Vector3(90f, 180f, 0f),
                new Vector3(-90f, 180f, 0f),
            };
            for (int i = 0; i < extras.Length; i++)
                candidates.Add(baseRot * Quaternion.Euler(extras[i]));

            // Explicitly face / anti-face BnetCamera.
            try
            {
                Vector3 fwd = bnetCam.transform.forward;
                Vector3 up = bnetCam.transform.up;
                candidates.Add(Quaternion.LookRotation(fwd, up));
                candidates.Add(Quaternion.LookRotation(-fwd, up));
                candidates.Add(Quaternion.LookRotation(fwd, -up));
                candidates.Add(Quaternion.LookRotation(-fwd, -up));
            }
            catch
            {
                // ignore
            }

            Quaternion best = go.transform.rotation;
            float bestScore = float.NegativeInfinity;
            float bestH = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                go.transform.rotation = candidates[i];
                go.transform.position = worldPos;
                Rect r = WorldBoundsToScreenRect(bnetCam, GetFullCorkMeshBounds(go));
                float area = r.width * r.height;
                float score = area;
                if (r.height < 20f)
                    score *= 0.01f; // heavily penalize edge-on / paper
                if (r.width < 20f)
                    score *= 0.01f;
                // Prefer roughly button-like aspect.
                float aspect = r.height > 1f ? r.width / r.height : 0f;
                if (aspect >= 0.4f && aspect <= 2.5f)
                    score *= 1.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidates[i];
                    bestH = r.height;
                }
            }

            go.transform.rotation = best;
            go.transform.position = worldPos;
            chosenEuler = go.transform.eulerAngles;

            Rect bestRect = WorldBoundsToScreenRect(bnetCam, GetFullCorkMeshBounds(go));
            DebugLog.Msg(
                $"KeywordFilterUi FaceAndSize: chosenEuler={FormatVec(chosenEuler)} " +
                $"probe={bestRect.width:F0}x{bestRect.height:F0} bestH={bestH:F0}");

            if (bestRect.height < 20f || bestRect.width < 20f)
                return false;

            // Uniform scale so K screen height ≈ 0.85–1.0× Friends height (~65px).
            Rect friendsScr = GetFriendsScreenRect(friends, bnetCam);
            float targetH = Mathf.Clamp(friendsScr.height * 0.92f, 48f, 90f);
            for (int i = 0; i < 10; i++)
            {
                Rect kScr = WorldBoundsToScreenRect(bnetCam, GetFullCorkMeshBounds(go));
                if (kScr.height < 2f)
                    break;
                float ratio = targetH / kScr.height;
                if (Mathf.Abs(ratio - 1f) < 0.06f)
                    break;
                ratio = Mathf.Clamp(ratio, 0.4f, 4f);
                go.transform.localScale = SanitizeLocalScale(go.transform.localScale * ratio);
                go.transform.rotation = best;
                go.transform.position = worldPos;
            }

            // Also ensure long side in ~50–90px and not a line.
            {
                Rect kScr = WorldBoundsToScreenRect(bnetCam, GetFullCorkMeshBounds(go));
                float longSide = Mathf.Max(kScr.width, kScr.height);
                if (longSide > 5f && (longSide < 50f || longSide > 95f))
                {
                    float targetLong = Mathf.Clamp(longSide, 50f, 90f);
                    // If too small bump up; if too large shrink toward 80.
                    float want = longSide < 50f ? 65f : 80f;
                    float ratio = Mathf.Clamp(want / longSide, 0.5f, 3f);
                    go.transform.localScale = SanitizeLocalScale(go.transform.localScale * ratio);
                    go.transform.rotation = best;
                    go.transform.position = worldPos;
                }
            }

            NudgeAboveFriendsOnBnet(go, friends, bnetCam, gapPx);

            Rect finalR = WorldBoundsToScreenRect(bnetCam, GetFullCorkMeshBounds(go));
            chosenEuler = go.transform.eulerAngles;
            return finalR.height > 30f && finalR.width > 30f;
        }

        private static void NudgeAboveFriendsOnBnet(
            GameObject go,
            BnetBarFriendButton friends,
            Camera bnetCam,
            float gapPx)
        {
            if (go == null || friends == null || bnetCam == null)
                return;

            Rect friendsScr = GetFriendsScreenRect(friends, bnetCam);
            Quaternion keepRot = go.transform.rotation;
            for (int i = 0; i < 24; i++)
            {
                Rect kScr = GetPlaqueScreenRect(bnetCam, go);
                if (kScr.width < 1f)
                {
                    kScr = WorldBoundsToScreenRect(bnetCam, GetFullCorkMeshBounds(go));
                    if (kScr.width < 1f)
                        kScr = GetScreenRect(bnetCam, go, corkOnly: false);
                }

                float err = (friendsScr.yMax + gapPx) - kScr.yMin;
                bool overlap = ScreenRectsOverlap(kScr, friendsScr, 0f);
                // Left-align (not center) — wide plaque centered clips off the left edge.
                float wantMinX = Mathf.Max(friendsScr.xMin, 0f);
                float xErr = wantMinX - kScr.xMin;
                if (err <= 1.5f && !overlap && Mathf.Abs(xErr) <= 4f)
                    break;

                Vector3 sp = bnetCam.WorldToScreenPoint(go.transform.position);
                float depth = sp.z;
                if (err > 1.5f || overlap)
                    sp.y += Mathf.Max(err, overlap ? 8f : 2f);
                if (Mathf.Abs(xErr) > 4f)
                    sp.x += xErr;
                sp.z = depth;
                go.transform.position = bnetCam.ScreenToWorldPoint(sp);
                go.transform.rotation = keepRot;
            }
        }

        /// <summary>
        /// Gold Crafting/Back plaque on BnetBar (layer 18). Face BnetCamera; W≥100∧H≥45.
        /// BoxCam sibling placement never draws above Friends — BnetBar first. NEVER FriendListButton.
        /// </summary>
        private static GameObject ReplaceOpenButtonWithBackStyleClone(
            GameObject corkGo,
            BnetBarFriendButton friends,
            Transform bnetBar,
            Camera bnetCam,
            float gapPx,
            out string usedPrefabPath)
        {
            usedPrefabPath = null;
            if (friends == null || bnetBar == null || bnetCam == null)
                return null;

            UIBButton srcBtn = FindPreferredPlaqueButton(out usedPrefabPath);
            if (srcBtn == null || srcBtn.gameObject == null)
            {
                DebugLog.Warning("KeywordFilterUi BackStyle: no Back/Crafting UIBButton found");
                return null;
            }

            Rect friendsScr = GetFriendsScreenRect(friends, bnetCam);
            Vector3 srcLossy = srcBtn.transform.lossyScale;
            Vector3 srcLocalScale = srcBtn.transform.localScale;
            Quaternion srcWorldRot = srcBtn.transform.rotation;
            int hudLayer = friends.gameObject.layer;

            DebugLog.Verbose(
                $"KeywordFilterUi BackStyle: cloning '{usedPrefabPath}' → BnetBar layer={hudLayer} " +
                $"srcLossy={FormatVec(srcLossy)} FriendsScr={friendsScr.width:F0}x{friendsScr.height:F0}");

            // Never spawn debugHome on Crafting — it ate PegUI hits on the vanilla button.
            DestroyNamedLeftovers(PlaqueDebugHomeName);

            DestroyCorkPivot();
            GameObject pivot = new GameObject(CorkPivotName);
            s_corkPivot = pivot;
            pivot.transform.SetParent(bnetBar, false);
            pivot.transform.position = friends.transform.position
                + friends.transform.TransformVector(Vector3.up * EstimateFriendsLocalUpOffset(friends, bnetCam));
            pivot.transform.rotation = friends.transform.rotation;
            Vector3 pLossy = bnetBar.lossyScale;
            float px = Mathf.Abs(pLossy.x) < 1e-4f ? 1f : pLossy.x;
            float py = Mathf.Abs(pLossy.y) < 1e-4f ? 1f : pLossy.y;
            float pz = Mathf.Abs(pLossy.z) < 1e-4f ? 1f : pLossy.z;
            pivot.transform.localScale = new Vector3(1f / px, 1f / py, 1f / pz);
            SetLayerRecursive(pivot, hudLayer);

            GameObject clone = InstantiatePlaqueClone(srcBtn);
            clone.transform.SetParent(pivot.transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            float srcMag = Mathf.Max(1f, AverageAbs(srcLocalScale));
            if (AverageAbs(srcLossy) > 1f && AverageAbs(srcLossy) < 40f)
                srcMag = AverageAbs(srcLossy);
            clone.transform.localScale = new Vector3(srcMag, srcMag, srcMag);
            SetLayerRecursive(clone, hudLayer);

            ApplyKeywordsLabel(clone);
            EnsureRenderersEnabled(clone);

            Vector3 chosenEuler;
            bool sized = FaceAndSizePlaqueOnBnet(
                clone, friends, bnetCam, gapPx, srcMag, srcWorldRot, out chosenEuler);
            ApplyKeywordsLabel(clone);
            AlignPlaqueToFriendsScreen(clone, friends, bnetCam, gapPx);
            // PegUI Wire happens once in PlaceAboveFriends (avoid double-wire).
            EnsureRenderersEnabled(clone);

            Rect kScr = GetPlaqueScreenRect(bnetCam, clone);
            float gap = kScr.height > 1f ? kScr.yMin - friendsScr.yMax : -999f;
            bool vis = AnyRendererVisible(clone);
            bool ok = sized && kScr.width >= 100f && kScr.height >= 45f
                && gap >= 8f && gap <= 28f
                && clone.layer == hudLayer
                && kScr.xMin >= -2f;

            DebugLog.Verbose(
                $"KeywordFilterUi BackStyle BnetBar: K={kScr.width:F0}x{kScr.height:F0} gap={gap:F1} " +
                $"KX=[{kScr.xMin:F0}..{kScr.xMax:F0}] isVisible={vis} ok={ok}");

            if (!vis)
            {
                LogPlaqueVisibilityFail(clone, bnetCam);
                DumpPlaqueVsSourceVisibility(srcBtn.gameObject, clone);
            }

            if (!ok)
            {
                DebugLog.Error(
                    "KeywordFilterUi BackStyle: plaque edge-on/tiny/misplaced — NOT falling back to Friends");
                UnityEngine.Object.Destroy(clone);
                DestroyCorkPivot();
                return null;
            }

            if (corkGo != null)
                UnityEngine.Object.Destroy(corkGo);
            return clone;
        }

        private static bool VerifyPlaqueMeshesVisible(string tag)
        {
            if (s_openButton == null || s_openButton.gameObject == null)
            {
                DebugLog.Warning($"KeywordFilterUi VerifyVisible[{tag}]: no open button");
                return false;
            }
            GameObject go = s_openButton.gameObject;
            bool vis = AnyRendererVisible(go);
            Rect k = default;
            Camera bnet = FindBnetCamera(FindFriendsButton());
            if (bnet != null)
                k = GetPlaqueScreenRect(bnet, go);
            DebugLog.Verbose(
                $"KeywordFilterUi VerifyVisible[{tag}]: isVisible={vis} " +
                $"K={k.width:F0}x{k.height:F0} layer={go.layer}");
            return vis;
        }

        private static void DumpPlaqueVsSourceVisibility(GameObject src = null, GameObject clone = null)
        {
            try
            {
                if (clone == null && s_openButton != null)
                    clone = s_openButton.gameObject;
                if (src == null)
                {
                    string path;
                    UIBButton btn = FindPreferredPlaqueButton(out path);
                    if (btn != null)
                        src = btn.gameObject;
                }
                bool srcVis = src != null && AnyRendererVisible(src);
                bool cloneVis = clone != null && AnyRendererVisible(clone);
                DebugLog.Warning(
                    $"KeywordFilterUi plaque vs src SAME FRAME: srcIsVisible={srcVis} cloneIsVisible={cloneVis} " +
                    $"srcPath={(src != null ? GetHierarchyPath(src.transform) : "null")} " +
                    $"clonePath={(clone != null ? GetHierarchyPath(clone.transform) : "null")} " +
                    $"srcEuler={(src != null ? FormatVec(src.transform.eulerAngles) : "-")} " +
                    $"cloneEuler={(clone != null ? FormatVec(clone.transform.eulerAngles) : "-")} " +
                    $"srcBounds={(src != null ? FormatVec(GetPlaqueMeshBounds(src).size) : "-")} " +
                    $"cloneBounds={(clone != null ? FormatVec(GetPlaqueMeshBounds(clone).size) : "-")}");
                Camera bnet = FindBnetCamera(FindFriendsButton());
                if (clone != null)
                    LogPlaqueVisibilityFail(clone, bnet);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.DumpPlaqueVsSourceVisibility failed", ex);
            }
        }

        private static Bounds GetPlaqueMeshBounds(GameObject go)
        {
            // Prefer face-only bounds (excludes CraftingButtonBackground_shadow etc.).
            Bounds face = GetPlaqueFaceBounds(go);
            if (face.size.sqrMagnitude > 1e-6f)
                return face;
            if (go == null)
                return default;
            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            bool any = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            for (int i = 0; mrs != null && i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || !mr.enabled || mr.GetComponentInParent<UberText>() != null)
                    continue;
                string n = mr.gameObject.name ?? string.Empty;
                if (IsExcludedPlaqueHitMeshName(n))
                    continue;
                Bounds wb = mr.bounds;
                if (wb.size.x > 25f || wb.size.y > 25f || wb.size.z > 25f)
                    continue;
                if (!any) { b = wb; any = true; }
                else b.Encapsulate(wb);
            }
            return b;
        }

        private static bool ReprobeOpenPlaqueFacing()
        {
            if (s_openButton == null || s_openButton.gameObject == null)
                return false;
            BnetBarFriendButton friends = FindFriendsButton();
            Camera bnetCam = FindBnetCamera(friends);
            if (friends == null || bnetCam == null)
                return false;

            GameObject go = s_openButton.gameObject;
            string path;
            UIBButton src = FindPreferredPlaqueButton(out path);
            Quaternion srcRot = src != null ? src.transform.rotation : go.transform.rotation;
            float mag = AverageAbs(go.transform.localScale);
            if (mag < 0.5f)
                mag = 7f;
            Vector3 euler;
            bool ok = FaceAndSizePlaqueOnBnet(go, friends, bnetCam, 12f, mag, srcRot, out euler);
            ApplyKeywordsLabel(go);
            EnsureRenderersEnabled(go);
            DebugLog.Msg(
                $"KeywordFilterUi ReprobeFacing: ok={ok} euler={FormatVec(euler)} " +
                $"K={GetPlaqueScreenRect(bnetCam, go).width:F0}x{GetPlaqueScreenRect(bnetCam, go).height:F0}");
            return ok;
        }

        private static GameObject InstantiatePlaqueClone(UIBButton srcBtn)
        {
            // Instantiate only — never DestroyImmediate / mutate src CraftingModeButton.
            GameObject clone = UnityEngine.Object.Instantiate(srcBtn.gameObject);
            clone.name = OpenButtonName;
            clone.SetActive(true);
            if (!ReferenceEquals(clone, srcBtn.gameObject))
                StripHostileHudBehaviours(clone);
            else
                DebugLog.Error("KeywordFilterUi: Instantiate returned src — abort strip");
            return clone;
        }

        private static void WirePlaqueInteraction(GameObject clone)
        {
            WirePlaqueInteraction(clone, FilterTrayKind.Keywords, OpenLabel());
        }

        private static void WirePlaqueInteraction(GameObject clone, FilterTrayKind kind, string label)
        {
            if (clone == null)
                return;

            UIBHighlight hlSrc = clone.GetComponent<UIBHighlight>();
            GameObject mouseOver = hlSrc != null ? hlSrc.m_MouseOverHighlight : null;
            GameObject mouseDown = hlSrc != null ? hlSrc.m_MouseDownHighlight : null;
            if (mouseOver == null)
                mouseOver = FindChildGlowObject(clone.transform);
            if (mouseDown == null && mouseOver != null)
                mouseDown = mouseOver;

            StripHostileHudBehaviours(clone);
            StripAllPegAndColliders(clone);

            // Collider must match CURRENT mesh pose (after any Y-stack offset).
            EnsurePlaqueClickCollider(clone);

            FilterPlaqueTag tag = clone.GetComponent<FilterPlaqueTag>();
            if (tag == null)
                tag = clone.AddComponent<FilterPlaqueTag>();
            tag.Kind = kind;

            PegUIElement peg = clone.AddComponent<PegUIElement>();
            peg.ClearEventListeners();
            peg.SetEnabled(true, true);

            // PegUI.TryGetPegUIElementFromHit only checks the HIT transform (no parent walk).
            // Any leftover child collider/proxy must route to THIS peg, never Keywords.
            InstallPegProxiesForPlaque(clone, peg);

            UIBHighlight hl = clone.GetComponent<UIBHighlight>();
            if (hl == null)
                hl = clone.AddComponent<UIBHighlight>();
            hl.m_MouseOverHighlight = mouseOver;
            hl.m_MouseDownHighlight = mouseDown;
            hl.EnableResponse = true;
            hl.AlwaysOver = false;
            try
            {
                var awake = HarmonyLib.AccessTools.Method(typeof(UIBHighlight), "Awake");
                awake?.Invoke(hl, null);
            }
            catch { /* ignore */ }
            if (mouseOver != null)
            {
                mouseOver.SetActive(false);
                EnsureRenderersEnabled(mouseOver);
            }
            if (mouseDown != null && mouseDown != mouseOver)
            {
                mouseDown.SetActive(false);
                EnsureRenderersEnabled(mouseDown);
            }

            FilterButtonFeedback.BindPressSound(peg);
            peg.AddEventListener(UIEventType.RELEASE, OnAnyFilterPlaqueReleased);
            peg.AddEventListener(UIEventType.ROLLOVER, OnAnyFilterPlaqueRollover);
            peg.AddEventListener(UIEventType.ROLLOUT, OnAnyFilterPlaqueRollout);

            if (kind == FilterTrayKind.Keywords)
            {
                s_openButton = peg;
                s_openUsesKeywordsLabel = true;
                s_openLabelText = clone.GetComponentInChildren<UberText>(true);
            }

            ApplyPlaqueLabel(clone, label, IsPlaqueFilterActive(kind));

            BoxCollider box = clone.GetComponent<BoxCollider>();
            int childCol = CountEnabledChildColliders(clone);
            int proxyN = clone.GetComponentsInChildren<PegUIElementProxy>(true).Length;
            DebugLog.Msg(
                $"KeywordFilterUi Wire stack plaque kind={kind} peg.enabled={peg.IsEnabled()} " +
                $"collider={(box != null && box.enabled)} size={(box != null ? FormatVec(box.size) : "-")} " +
                $"center={(box != null ? FormatVec(box.center) : "-")} layer={clone.layer} " +
                $"childCols={childCol} proxies={proxyN} RELEASE→{kind} " +
                $"parent={GetHierarchyPath(clone.transform.parent)} label={label}");
        }

        /// <summary>Destroy PegUI / proxies / colliders so Instantiate(Keywords) can't steal hits.</summary>
        private static void StripAllPegAndColliders(GameObject go)
        {
            if (go == null)
                return;
            PegUIElement[] allPeg = go.GetComponentsInChildren<PegUIElement>(true);
            for (int i = 0; i < allPeg.Length; i++)
            {
                if (allPeg[i] != null)
                    UnityEngine.Object.DestroyImmediate(allPeg[i]);
            }
            PegUIElementProxy[] proxies = go.GetComponentsInChildren<PegUIElementProxy>(true);
            for (int i = 0; i < proxies.Length; i++)
            {
                if (proxies[i] != null)
                    UnityEngine.Object.DestroyImmediate(proxies[i]);
            }
            FilterPlaqueTag[] tags = go.GetComponentsInChildren<FilterPlaqueTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] != null)
                    UnityEngine.Object.DestroyImmediate(tags[i]);
            }
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    UnityEngine.Object.DestroyImmediate(cols[i]);
            }
        }

        /// <summary>
        /// PegUI hit test does NOT walk parents — child hits need PegUIElementProxy.Owner = plaque peg.
        /// </summary>
        private static void InstallPegProxiesForPlaque(GameObject plaque, PegUIElement peg)
        {
            if (plaque == null || peg == null)
                return;
            Transform[] all = plaque.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t.gameObject == plaque)
                    continue;
                // No live PegUI on children; proxy → this plaque.
                PegUIElement childPeg = t.GetComponent<PegUIElement>();
                if (childPeg != null)
                    UnityEngine.Object.DestroyImmediate(childPeg);
                PegUIElementProxy proxy = t.GetComponent<PegUIElementProxy>();
                if (proxy == null)
                    proxy = t.gameObject.AddComponent<PegUIElementProxy>();
                proxy.Owner = peg;
            }
        }

        private static int CountEnabledChildColliders(GameObject root)
        {
            if (root == null)
                return 0;
            int n = 0;
            Collider[] cols = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i].gameObject == root)
                    continue;
                if (cols[i].enabled)
                    n++;
            }
            return n;
        }

        private static FilterTrayKind KindFromEvent(UIEvent e)
        {
            FilterTrayKind kind = FilterTrayKind.Keywords;
            try
            {
                PegUIElement el = e != null ? e.GetElement() : null;
                FilterPlaqueTag tag = null;
                if (el != null)
                    tag = el.GetComponent<FilterPlaqueTag>();
                if (tag == null && el != null)
                    tag = el.GetComponentInParent<FilterPlaqueTag>();
                if (tag != null)
                    kind = tag.Kind;
            }
            catch { /* fall back Keywords */ }
            return kind;
        }

        private static void OnAnyFilterPlaqueReleased(UIEvent e)
        {
            OnFilterPlaqueReleased(KindFromEvent(e), "pegui");
        }

        private static void OnAnyFilterPlaqueRollover(UIEvent e)
        {
            FilterTrayKind kind = KindFromEvent(e);
            DebugLog.Verbose($"KeywordFilterUi: plaque hover enter kind={kind} (pegui)");
            try
            {
                PegUIElement el = e != null ? e.GetElement() : null;
                if (el == null)
                    return;
                UIBHighlight hl = el.GetComponent<UIBHighlight>();
                if (hl != null && hl.m_MouseOverHighlight != null)
                {
                    hl.m_MouseOverHighlight.SetActive(true);
                    EnsureRenderersEnabled(hl.m_MouseOverHighlight);
                }
            }
            catch { /* ignore */ }
        }

        private static void OnAnyFilterPlaqueRollout(UIEvent e)
        {
            FilterTrayKind kind = KindFromEvent(e);
            DebugLog.Verbose($"KeywordFilterUi: plaque hover exit kind={kind} (pegui)");
            try
            {
                PegUIElement el = e != null ? e.GetElement() : null;
                if (el == null)
                    return;
                UIBHighlight hl = el.GetComponent<UIBHighlight>();
                if (hl == null)
                    return;
                if (hl.m_MouseOverHighlight != null)
                    hl.m_MouseOverHighlight.SetActive(false);
                if (hl.m_MouseDownHighlight != null)
                    hl.m_MouseDownHighlight.SetActive(false);
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Type occupies Keywords' Friends-adjacent slot; Keyword is lifted above Rarity.
        /// </summary>
        private static void BuildExtraPlaqueStack(
            GameObject keywordsPlaque,
            BnetBarFriendButton friends,
            Transform bnetBar,
            Camera bnetCam,
            float gapPx)
        {
            DestroyExtraPlaques();
            if (keywordsPlaque == null || friends == null || bnetBar == null || bnetCam == null)
                return;

            PreferredPremiumPrefs.EnsureLoaded();

            // Type at Keywords' current pose (above Friends); Keyword still overlaps until lifted.
            GameObject type = CreateStackedPlaque(
                PlaqueLabelForKind(FilterTrayKind.Type, "Type"),
                FilterTrayKind.Type,
                keywordsPlaque,
                keywordsPlaque,
                friends,
                bnetBar,
                bnetCam,
                gapPx,
                placeAtReferencePose: true);
            if (type == null)
            {
                DebugLog.Warning("KeywordFilterUi: failed to create Type plaque");
                return;
            }
            s_extraPlaques.Add(type);

            GameObject rarity = CreateStackedPlaque(
                PlaqueLabelForKind(FilterTrayKind.Rarity, "Rarity"),
                FilterTrayKind.Rarity,
                type,
                keywordsPlaque,
                friends,
                bnetBar,
                bnetCam,
                gapPx);
            if (rarity != null)
                s_extraPlaques.Add(rarity);
            GameObject below = rarity != null ? rarity : type;

            OffsetPlaqueAboveReference(keywordsPlaque, below, bnetCam, gapPx);
            below = keywordsPlaque;

            GameObject race = CreateStackedPlaque(
                PlaqueLabelForKind(FilterTrayKind.Race, "Race"),
                FilterTrayKind.Race,
                below,
                keywordsPlaque,
                friends,
                bnetBar,
                bnetCam,
                gapPx);
            if (race != null)
            {
                s_extraPlaques.Add(race);
                below = race;
            }

            GameObject school = CreateStackedPlaque(
                PlaqueLabelForKind(FilterTrayKind.School, "School"),
                FilterTrayKind.School,
                below,
                keywordsPlaque,
                friends,
                bnetBar,
                bnetCam,
                gapPx);
            if (school != null)
            {
                s_extraPlaques.Add(school);
                below = school;
            }

            EnsurePlaqueClickCollider(keywordsPlaque, bnetCam);
            InstallPegProxiesForPlaque(keywordsPlaque, keywordsPlaque.GetComponent<PegUIElement>());
            for (int i = 0; i < s_extraPlaques.Count; i++)
                EnsurePlaqueClickCollider(s_extraPlaques[i], bnetCam);

            LogPlaqueColliderScreenAabbs(keywordsPlaque, bnetCam);
            LogPegRaycastAtPlaqueCenters(keywordsPlaque, bnetCam);
            RefreshAllPlaqueLabels();
        }

        private static void OffsetPlaqueAboveReference(
            GameObject go,
            GameObject below,
            Camera bnetCam,
            float gapPx)
        {
            if (go == null || below == null || bnetCam == null)
                return;
            GameObject pivotGo = go.transform.parent != null
                ? go.transform.parent.gameObject
                : go;
            OffsetPivotAboveReference(pivotGo, go, below, bnetCam, gapPx);
        }

        /// <summary>
        /// Park Quality plaque near the top of the BnetCamera view (settings), X-aligned with the filter stack.
        /// </summary>
        private static void PlaceQualityNearScreenTop(
            GameObject quality,
            Camera bnetCam,
            GameObject alignXWith)
        {
            if (quality == null || bnetCam == null)
                return;
            Transform pivot = quality.transform.parent != null
                ? quality.transform.parent
                : quality.transform;
            Quaternion keepRot = pivot.rotation;

            for (int i = 0; i < 12; i++)
            {
                Rect qScr = GetPlaqueScreenRect(bnetCam, quality);
                if (qScr.width < 2f)
                    break;

                // Target: plaque top ~40px below screen top (well above School / filter column).
                float wantMaxY = Screen.height - 40f;
                float yErr = wantMaxY - qScr.yMax;

                float xErr = 0f;
                if (alignXWith != null)
                {
                    Rect refScr = GetPlaqueScreenRect(bnetCam, alignXWith);
                    if (refScr.width > 2f)
                        xErr = refScr.xMin - qScr.xMin;
                }

                if (Mathf.Abs(yErr) <= 2f && Mathf.Abs(xErr) <= 2f)
                    break;

                Vector3 sp = bnetCam.WorldToScreenPoint(pivot.position);
                sp.y += yErr;
                sp.x += xErr;
                // Keep depth stable for ScreenToWorldPoint.
                if (sp.z < 0.05f)
                    sp.z = bnetCam.nearClipPlane + 0.5f;
                pivot.position = bnetCam.ScreenToWorldPoint(sp);
                pivot.rotation = keepRot;
            }

            Rect finalScr = GetPlaqueScreenRect(bnetCam, quality);
            DebugLog.Msg(
                $"KeywordFilterUi Quality top park: scrY=[{finalScr.yMin:F0}..{finalScr.yMax:F0}] " +
                $"screenH={Screen.height}");
        }

        private static bool IsPlaqueFilterActive(FilterTrayKind kind)
        {
            switch (kind)
            {
                case FilterTrayKind.Keywords: return KeywordFilterState.IsActive;
                case FilterTrayKind.Type:
                case FilterTrayKind.Race:
                case FilterTrayKind.School:
                case FilterTrayKind.Rarity:
                    return ExtraFilterState.IsActiveForKind(kind);
                case FilterTrayKind.Quality:
                    // Preference / settings — never "filter-active" red.
                    return false;
                default: return false;
            }
        }

        private static string PlaqueLabelForKind(FilterTrayKind kind, string baseLabel)
        {
            return IsPlaqueFilterActive(kind) ? baseLabel + "*" : baseLabel;
        }

        /// <summary>
        /// Crafting/Back clone as BnetBar sibling of Keywords (never child of Keywords GO).
        /// Copy Keywords world TRS, then screen-Y stack above <paramref name="below"/>.
        /// </summary>
        private static GameObject CreateStackedPlaque(
            string label,
            FilterTrayKind kind,
            GameObject below,
            GameObject keywordsPlaque,
            BnetBarFriendButton friends,
            Transform bnetBar,
            Camera bnetCam,
            float gapPx,
            bool placeAtReferencePose = false)
        {
            if (below == null || keywordsPlaque == null || bnetBar == null || bnetCam == null)
                return null;

            // Pose reference = Keywords plaque; parent = BnetBar (sibling of Keywords cork pivot).
            Transform keywordsPivot = keywordsPlaque.transform.parent;
            Transform parent = bnetBar;
            // Prefer Keywords pivot's parent if that is BnetBar (or under it).
            if (keywordsPivot != null && keywordsPivot.parent != null)
                parent = keywordsPivot.parent;

            // Guard: never parent under the Keywords button itself.
            if (parent == keywordsPlaque.transform
                || (parent != null && parent.IsChildOf(keywordsPlaque.transform)))
                parent = bnetBar;

            int layer = keywordsPlaque.layer;
            string craftPath;
            UIBButton srcBtn = FindPreferredPlaqueButton(out craftPath);
            if (srcBtn == null)
            {
                DebugLog.Error($"KeywordFilterUi stack {kind}: no Crafting/Back src");
                return null;
            }

            GameObject pivot = new GameObject(CorkPivotName + "_" + kind);
            pivot.transform.SetParent(parent, false);
            if (keywordsPivot != null)
            {
                pivot.transform.localPosition = keywordsPivot.localPosition;
                pivot.transform.localRotation = keywordsPivot.localRotation;
                pivot.transform.localScale = keywordsPivot.localScale;
            }
            else
            {
                pivot.transform.position = keywordsPlaque.transform.position;
                pivot.transform.rotation = keywordsPlaque.transform.rotation;
                pivot.transform.localScale = Vector3.one;
            }
            SetLayerRecursive(pivot, layer);

            // Fresh Crafting clone — do NOT Instantiate(Keywords) (inherits PegUI/proxy parentage).
            GameObject clone = InstantiatePlaqueClone(srcBtn);
            clone.name = OpenButtonName + "_" + kind;
            clone.SetActive(true);
            clone.transform.SetParent(pivot.transform, false);
            // Match Keywords local pose under its pivot (visual parity).
            clone.transform.localPosition = keywordsPlaque.transform.localPosition;
            clone.transform.localRotation = keywordsPlaque.transform.localRotation;
            clone.transform.localScale = keywordsPlaque.transform.localScale;
            SetLayerRecursive(clone, layer);

            StripHostileHudBehaviours(clone);
            StripAllPegAndColliders(clone);

            // Same pose as Keywords (Type takes Friends-adjacent slot) OR stack above `below`.
            if (!placeAtReferencePose)
                OffsetPivotAboveReference(pivot, clone, below, bnetCam, gapPx);

            // If Crafting clone mesh size exploded vs Keywords, snap lossy to Keywords.
            Rect kwScr = GetPlaqueScreenRect(bnetCam, keywordsPlaque);
            Rect preScr = GetPlaqueScreenRect(bnetCam, clone);
            if (kwScr.width > 10f && (preScr.width > kwScr.width * 1.35f
                || preScr.height > kwScr.height * 1.35f
                || preScr.width < kwScr.width * 0.65f
                || preScr.height < kwScr.height * 0.65f))
            {
                ApplyWorldScale(clone.transform, keywordsPlaque.transform.lossyScale);
                clone.transform.rotation = keywordsPlaque.transform.rotation;
                if (!placeAtReferencePose)
                    OffsetPivotAboveReference(pivot, clone, below, bnetCam, gapPx);
            }

            WirePlaqueInteraction(clone, kind, label);
            EnsurePlaqueClickCollider(clone, bnetCam);
            InstallPegProxiesForPlaque(clone, clone.GetComponent<PegUIElement>());

            // Sibling order: later siblings drawn/hit-tested after — stack above Keywords.
            pivot.transform.SetAsLastSibling();
            clone.transform.SetAsLastSibling();
            EnsureRenderersEnabled(clone);

            // Hard assert: not under Keywords button.
            if (clone.transform.IsChildOf(keywordsPlaque.transform))
            {
                DebugLog.Error(
                    $"KeywordFilterUi stack {kind}: STILL under Keywords — reparent to BnetBar");
                pivot.transform.SetParent(bnetBar, true);
            }

            Rect kScr = GetPlaqueScreenRect(bnetCam, clone);
            BoxCollider box = clone.GetComponent<BoxCollider>();
            Vector3 meshWorld = GetPlaqueMeshBounds(clone).center;
            Vector3 colWorld = box != null
                ? clone.transform.TransformPoint(box.center)
                : Vector3.zero;
            Vector3 meshScr = bnetCam.WorldToScreenPoint(meshWorld);
            Vector3 colScr = bnetCam.WorldToScreenPoint(colWorld);
            PegUIElement peg = clone.GetComponent<PegUIElement>();
            DebugLog.Msg(
                $"KeywordFilterUi stack plaque {kind}: K(bnet)={kScr.width:F0}x{kScr.height:F0} " +
                $"src={craftPath} peg={(peg != null && peg.IsEnabled())} " +
                $"meshScr=({meshScr.x:F0},{meshScr.y:F0}) colScr=({colScr.x:F0},{colScr.y:F0}) " +
                $"Δscr=({meshScr.x - colScr.x:F0},{meshScr.y - colScr.y:F0}) " +
                $"path={GetHierarchyPath(clone.transform)} label={label}");
            if (Mathf.Abs(meshScr.x - colScr.x) > 25f || Mathf.Abs(meshScr.y - colScr.y) > 25f)
                DebugLog.Error(
                    $"KeywordFilterUi stack plaque {kind}: collider≠mesh screen (hitbox likely wrong)");
            if (kScr.width > 400f || kScr.height > 120f || kScr.width < 80f || kScr.height < 40f)
                DebugLog.Error(
                    $"KeywordFilterUi stack plaque {kind}: BAD SIZE {kScr.width:F0}x{kScr.height:F0}");

            return clone;
        }

        /// <summary>Shift entire stack pivot so clone sits ~gapPx above <paramref name="below"/>.</summary>
        private static void OffsetPivotAboveReference(
            GameObject pivot,
            GameObject clone,
            GameObject below,
            Camera bnetCam,
            float gapPx)
        {
            if (pivot == null || clone == null || below == null || bnetCam == null)
                return;

            Quaternion keepPivotRot = pivot.transform.rotation;
            for (int i = 0; i < 16; i++)
            {
                Rect belowScr = GetPlaqueScreenRect(bnetCam, below);
                Rect kScr = GetPlaqueScreenRect(bnetCam, clone);
                if (kScr.width < 2f)
                    break;
                float yErr = (belowScr.yMax + gapPx) - kScr.yMin;
                float xErr = belowScr.xMin - kScr.xMin;
                if (Mathf.Abs(yErr) <= 1.5f && Mathf.Abs(xErr) <= 2f)
                    break;

                Vector3 sp = bnetCam.WorldToScreenPoint(pivot.transform.position);
                sp.y += yErr;
                sp.x += xErr;
                pivot.transform.position = bnetCam.ScreenToWorldPoint(sp);
                pivot.transform.rotation = keepPivotRot;
            }
        }

        /// <summary>
        /// Ray through each plaque mesh center (BnetCamera) — log which collider PegUI would resolve.
        /// </summary>
        private static void LogPegRaycastAtPlaqueCenters(GameObject keywordsPlaque, Camera bnetCam)
        {
            if (bnetCam == null)
                return;
            var plaques = new List<GameObject>();
            if (keywordsPlaque != null)
                plaques.Add(keywordsPlaque);
            for (int i = 0; i < s_extraPlaques.Count; i++)
            {
                if (s_extraPlaques[i] != null)
                    plaques.Add(s_extraPlaques[i]);
            }

            for (int p = 0; p < plaques.Count; p++)
            {
                GameObject go = plaques[p];
                FilterPlaqueTag tag = go.GetComponent<FilterPlaqueTag>();
                FilterTrayKind expect = tag != null ? tag.Kind : FilterTrayKind.Keywords;
                Vector3 center = GetPlaqueMeshBounds(go).center;
                Vector3 scr = bnetCam.WorldToScreenPoint(center);
                Ray ray = bnetCam.ScreenPointToRay(scr);
                RaycastHit[] hits = Physics.RaycastAll(ray, 800f);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                string resolved = "(none)";
                if (hits != null && hits.Length > 0)
                {
                    for (int h = 0; h < hits.Length && h < 6; h++)
                    {
                        Transform ht = hits[h].transform;
                        if (ht == null)
                            continue;
                        PegUIElement peg = ht.GetComponent<PegUIElement>();
                        PegUIElementProxy proxy = ht.GetComponent<PegUIElementProxy>();
                        PegUIElement owner = peg != null ? peg : (proxy != null ? proxy.Owner : null);
                        FilterPlaqueTag hitTag = owner != null
                            ? owner.GetComponent<FilterPlaqueTag>()
                            : null;
                        string kindStr = hitTag != null ? hitTag.Kind.ToString() : "-";
                        string line =
                            $"  hit[{h}] '{ht.name}' dist={hits[h].distance:F3} " +
                            $"peg={(peg != null)} proxy={(proxy != null)} →kind={kindStr}";
                        if (h == 0)
                        {
                            resolved = kindStr;
                            DebugLog.Verbose(
                                $"KeywordFilterUi PegRay expect={expect} scr=({scr.x:F0},{scr.y:F0}) " +
                                $"first→{resolved}");
                        }
                        DebugLog.Verbose(line);
                    }
                }
                else
                {
                    DebugLog.Verbose(
                        $"KeywordFilterUi PegRay expect={expect} scr=({scr.x:F0},{scr.y:F0}) NO HITS");
                }

                if (resolved != expect.ToString() && resolved != "-")
                    DebugLog.Verbose(
                        $"KeywordFilterUi PegRay MISMATCH expect={expect} got={resolved}");
            }
        }

        private static void ApplyPlaqueLabel(GameObject go, string text, bool active = false)
        {
            try
            {
                UberText[] all = go.GetComponentsInChildren<UberText>(true);
                Color idle = s_plaqueLabelIdleColor ?? PlaqueLabelIdleFallback;
                Color col = active ? PlaqueLabelActiveColor : idle;
                for (int i = 0; i < all.Length; i++)
                {
                    UberText ut = all[i];
                    if (ut == null) continue;
                    ut.gameObject.SetActive(true);
                    if (!s_plaqueLabelIdleColor.HasValue && !active)
                    {
                        try
                        {
                            Color cur = ut.TextColor;
                            if (cur.a > 0.01f
                                && (Mathf.Abs(cur.r - PlaqueLabelActiveColor.r) > 0.05f
                                    || Mathf.Abs(cur.g - PlaqueLabelActiveColor.g) > 0.05f))
                                s_plaqueLabelIdleColor = cur;
                        }
                        catch { /* ignore */ }
                        idle = s_plaqueLabelIdleColor ?? PlaqueLabelIdleFallback;
                        col = idle;
                    }
                    ut.Text = text;
                    try { ut.TextColor = col; } catch { /* ignore */ }
                    ut.UpdateNow(true);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.ApplyPlaqueLabel failed", ex);
            }
        }

        private static void DestroyExtraPlaques()
        {
            for (int i = 0; i < s_extraPlaques.Count; i++)
            {
                GameObject go = s_extraPlaques[i];
                if (go == null)
                    continue;
                Transform pivot = go.transform.parent;
                UnityEngine.Object.Destroy(go);
                if (pivot != null && pivot.name != null && pivot.name.StartsWith(CorkPivotName, StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(pivot.gameObject);
            }
            s_extraPlaques.Clear();
        }

        private static void OnFilterPlaqueReleased(FilterTrayKind kind, string via = "pegui")
        {
            CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
            SetFilterTray live = display != null && s_setFilterTray != null
                ? s_setFilterTray(display)
                : null;
            KeywordSetTray.EnsureBuilt(live);
            KeywordSetTray.NotifyToggleClicked();
            KeywordSetTray.ToggleKind(kind);
            s_panelShown = KeywordSetTray.IsShown;
            RefreshAllPlaqueLabels();
            DebugLog.Msg(
                $"KeywordFilterUi: plaque clicked kind={kind} ({via}) trayShown={KeywordSetTray.IsShown} " +
                $"activeKind={KeywordSetTray.ActiveKind}");
        }

        private static void RefreshAllPlaqueLabels()
        {
            bool kwActive = IsPlaqueFilterActive(FilterTrayKind.Keywords);
            SetOpenChipLabel(OpenLabel());
            if (s_openButton != null && s_openButton.gameObject != null)
                ApplyPlaqueLabel(s_openButton.gameObject, OpenLabel(), kwActive);

            for (int i = 0; i < s_extraPlaques.Count; i++)
            {
                GameObject go = s_extraPlaques[i];
                if (go == null) continue;
                string n = go.name ?? string.Empty;
                FilterTrayKind kind = FilterTrayKind.None;
                if (n.EndsWith("_Type", StringComparison.Ordinal)) kind = FilterTrayKind.Type;
                else if (n.EndsWith("_Rarity", StringComparison.Ordinal)) kind = FilterTrayKind.Rarity;
                else if (n.EndsWith("_Race", StringComparison.Ordinal)) kind = FilterTrayKind.Race;
                else if (n.EndsWith("_School", StringComparison.Ordinal)) kind = FilterTrayKind.School;
                else if (n.EndsWith("_Quality", StringComparison.Ordinal)) kind = FilterTrayKind.Quality;
                if (kind == FilterTrayKind.None) continue;
                string baseLabel = kind == FilterTrayKind.Type ? "Type"
                    : kind == FilterTrayKind.Rarity ? "Rarity"
                    : kind == FilterTrayKind.Race ? "Race"
                    : kind == FilterTrayKind.School ? "School" : "Quality";
                bool active = IsPlaqueFilterActive(kind);
                ApplyPlaqueLabel(go, PlaqueLabelForKind(kind, baseLabel), active);
            }
        }

        private static GameObject FindChildGlowObject(Transform root)
        {
            if (root == null)
                return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            GameObject best = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i] == root)
                    continue;
                string n = all[i].name;
                if (n.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("MouseOver", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // Prefer ButtonBack_Glow / explicit glow meshes over unrelated highlights.
                if (n.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0)
                    return all[i].gameObject;
                if (best == null)
                    best = all[i].gameObject;
            }
            return best;
        }

        /// <summary>
        /// Root BoxCollider fitted to plaque face / visual screen rect (~200×61), not shadow meshes.
        /// Vertical screen extent capped so stack siblings (~73px centers) do not overlap.
        /// </summary>
        private static void EnsurePlaqueClickCollider(GameObject go, Camera bnetCam = null)
        {
            if (go == null)
                return;
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            box.enabled = true;
            box.isTrigger = false;

            if (bnetCam == null)
                bnetCam = FindBnetCamera(FindFriendsButton());

            // Prefer fitting collider to visual screen AABB (already excludes giant shadow meshes).
            if (bnetCam != null && TryFitColliderToVisualScreenRect(go, box, bnetCam))
                return;

            Bounds b = GetPlaqueFaceBounds(go);
            if (b.size.sqrMagnitude < 1e-6f)
                b = GetPlaqueMeshBounds(go);
            if (b.size.sqrMagnitude < 1e-6f)
            {
                EnsureClickCollider(go);
                return;
            }

            SetBoxColliderFromWorldBounds(go, box, b);
            if (bnetCam != null)
                ShrinkColliderScreenHeight(go, box, bnetCam, MaxColliderScreenHeightPx);
        }

        /// <summary>Plaque face meshes only — exclude shadow / glow / giant brackets.</summary>
        private static Bounds GetPlaqueFaceBounds(GameObject go)
        {
            if (go == null)
                return default;

            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            bool any = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            Bounds preferred = default;
            bool anyPreferred = false;

            for (int i = 0; mrs != null && i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;
                if (mr.GetComponentInParent<UberText>() != null)
                    continue;
                string n = mr.gameObject.name ?? string.Empty;
                if (IsExcludedPlaqueHitMeshName(n))
                    continue;
                Bounds wb = mr.bounds;
                if (wb.size.sqrMagnitude < 1e-6f)
                    continue;
                // Shadow / outer chrome can be 60+ units — never use for hitboxes.
                if (wb.size.x > 25f || wb.size.y > 25f || wb.size.z > 25f)
                    continue;

                if (!any) { b = wb; any = true; }
                else b.Encapsulate(wb);

                if (IsPreferredPlaqueFaceMeshName(n))
                {
                    if (!anyPreferred) { preferred = wb; anyPreferred = true; }
                    else preferred.Encapsulate(wb);
                }
            }

            if (anyPreferred)
                return preferred;
            return any ? b : default;
        }

        private static bool IsExcludedPlaqueHitMeshName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return true;
            return n.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("MouseOver", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("MouseDown", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("dust", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("bottle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPreferredPlaqueFaceMeshName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            return n.IndexOf("Bracket_Center", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ButtonBack_Bracket", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("cream", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("plate", StringComparison.OrdinalIgnoreCase) >= 0
                || (n.IndexOf("ButtonBack", StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static void SetBoxColliderFromWorldBounds(GameObject go, BoxCollider box, Bounds worldBounds)
        {
            Vector3 localMin = go.transform.InverseTransformPoint(worldBounds.min);
            Vector3 localMax = go.transform.InverseTransformPoint(worldBounds.max);
            Vector3 c = worldBounds.center;
            Vector3 e = worldBounds.extents;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iy = -1; iy <= 1; iy += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                Vector3 lp = go.transform.InverseTransformPoint(
                    c + new Vector3(e.x * ix, e.y * iy, e.z * iz));
                localMin = Vector3.Min(localMin, lp);
                localMax = Vector3.Max(localMax, lp);
            }
            Vector3 localSize = localMax - localMin;
            box.center = (localMin + localMax) * 0.5f;
            box.size = new Vector3(
                Mathf.Max(Mathf.Abs(localSize.x), 0.05f),
                Mathf.Max(Mathf.Abs(localSize.y), 0.05f),
                Mathf.Max(Mathf.Abs(localSize.z), 0.05f));
        }

        /// <summary>Fit BoxCollider to ~90% of visual plaque screen rect; clamp height &lt; stack gap.</summary>
        private static bool TryFitColliderToVisualScreenRect(GameObject go, BoxCollider box, Camera cam)
        {
            Rect visual = GetPlaqueScreenRect(cam, go);
            if (visual.width < 40f || visual.height < 20f)
                return false;

            float cx = visual.center.x;
            float cy = visual.center.y;
            float halfW = visual.width * 0.5f * ColliderScreenShrink;
            float halfH = visual.height * 0.5f * ColliderScreenShrink;
            float maxHalfH = MaxColliderScreenHeightPx * 0.5f;
            if (halfH > maxHalfH)
                halfH = maxHalfH;

            float depth = cam.WorldToScreenPoint(go.transform.position).z;
            if (depth < 0.05f)
                depth = cam.nearClipPlane + 0.5f;

            Vector3[] corners =
            {
                cam.ScreenToWorldPoint(new Vector3(cx - halfW, cy - halfH, depth)),
                cam.ScreenToWorldPoint(new Vector3(cx + halfW, cy - halfH, depth)),
                cam.ScreenToWorldPoint(new Vector3(cx - halfW, cy + halfH, depth)),
                cam.ScreenToWorldPoint(new Vector3(cx + halfW, cy + halfH, depth)),
            };

            Vector3 localMin = go.transform.InverseTransformPoint(corners[0]);
            Vector3 localMax = localMin;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 lp = go.transform.InverseTransformPoint(corners[i]);
                localMin = Vector3.Min(localMin, lp);
                localMax = Vector3.Max(localMax, lp);
            }

            // Keep a thin Z so Physics still hits the plaque plane.
            Vector3 size = localMax - localMin;
            float z = Mathf.Max(Mathf.Abs(size.z), 0.08f);
            if (z > 0.35f)
                z = 0.35f;
            box.center = (localMin + localMax) * 0.5f;
            box.size = new Vector3(
                Mathf.Max(Mathf.Abs(size.x), 0.05f),
                Mathf.Max(Mathf.Abs(size.y), 0.05f),
                z);
            return true;
        }

        private static void ShrinkColliderScreenHeight(
            GameObject go, BoxCollider box, Camera cam, float maxH)
        {
            Rect colScr = GetColliderScreenRect(cam, go, box);
            if (colScr.height <= maxH || colScr.height < 1f)
                return;
            float scale = maxH / colScr.height;
            Vector3 s = box.size;
            // Shrink the two local axes that contribute most to screen Y — uniform XY shrink is safe.
            box.size = new Vector3(s.x * scale, s.y * scale, s.z);
        }

        private static Rect GetColliderScreenRect(Camera cam, GameObject go, BoxCollider box)
        {
            if (cam == null || go == null || box == null)
                return default;
            Vector3 c = go.transform.TransformPoint(box.center);
            Vector3 axisX = go.transform.TransformVector(new Vector3(box.size.x * 0.5f, 0f, 0f));
            Vector3 axisY = go.transform.TransformVector(new Vector3(0f, box.size.y * 0.5f, 0f));
            Vector3 axisZ = go.transform.TransformVector(new Vector3(0f, 0f, box.size.z * 0.5f));
            float xMin = float.PositiveInfinity, xMax = float.NegativeInfinity;
            float yMin = float.PositiveInfinity, yMax = float.NegativeInfinity;
            bool any = false;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iy = -1; iy <= 1; iy += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                Vector3 w = c + axisX * ix + axisY * iy + axisZ * iz;
                Vector3 sp = cam.WorldToScreenPoint(w);
                if (sp.z <= 0.01f)
                    continue;
                any = true;
                xMin = Mathf.Min(xMin, sp.x);
                xMax = Mathf.Max(xMax, sp.x);
                yMin = Mathf.Min(yMin, sp.y);
                yMax = Mathf.Max(yMax, sp.y);
            }
            if (!any)
                return default;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void LogPlaqueColliderScreenAabbs(GameObject keywordsPlaque, Camera bnetCam)
        {
            if (bnetCam == null)
                return;
            var list = new List<GameObject>();
            if (keywordsPlaque != null)
                list.Add(keywordsPlaque);
            for (int i = 0; i < s_extraPlaques.Count; i++)
            {
                if (s_extraPlaques[i] != null)
                    list.Add(s_extraPlaques[i]);
            }

            var rects = new List<Rect>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                GameObject go = list[i];
                BoxCollider box = go.GetComponent<BoxCollider>();
                FilterPlaqueTag tag = go.GetComponent<FilterPlaqueTag>();
                string kind = tag != null ? tag.Kind.ToString() : go.name;
                Rect r = GetColliderScreenRect(bnetCam, go, box);
                rects.Add(r);
                DebugLog.Msg(
                    $"KeywordFilterUi colScr kind={kind} W×H={r.width:F0}x{r.height:F0} " +
                    $"y=[{r.yMin:F0}..{r.yMax:F0}] " +
                    $"box.size={(box != null ? FormatVec(box.size) : "-")}");
                if (r.height > MaxColliderScreenHeightPx + 2f)
                    DebugLog.Error(
                        $"KeywordFilterUi colScr kind={kind}: height {r.height:F0} > max {MaxColliderScreenHeightPx}");
            }

            for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                if (!ScreenRectsOverlap(rects[i], rects[j], 0f))
                    continue;
                FilterPlaqueTag a = list[i].GetComponent<FilterPlaqueTag>();
                FilterPlaqueTag b = list[j].GetComponent<FilterPlaqueTag>();
                DebugLog.Error(
                    $"KeywordFilterUi colScr OVERLAP {a?.Kind}↔{b?.Kind} " +
                    $"({rects[i].width:F0}x{rects[i].height:F0} vs {rects[j].width:F0}x{rects[j].height:F0})");
            }
        }

        /// <summary>
        /// Prefer CraftingModeButton if its BoxCamera screen width ≥ DoneButton (room for Keywords).
        /// </summary>
        private static UIBButton FindPreferredPlaqueButton(out string path)
        {
            path = null;
            UIBButton done = null;
            UIBButton craft = null;
            string donePath = null;
            string craftPath = null;

            try
            {
                CollectionDeckTray tray = CollectionDeckTray.Get();
                if (tray != null && tray.m_doneButton != null)
                {
                    done = tray.m_doneButton;
                    donePath = GetHierarchyPath(done.transform) + " (m_doneButton/Back)";
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.FindPreferredPlaqueButton done failed", ex);
            }

            try
            {
                CollectibleDisplay display = CollectionManager.Get()?.GetCollectibleDisplay();
                if (display != null && display.m_craftingModeButton != null)
                {
                    craft = display.m_craftingModeButton;
                    craftPath = GetHierarchyPath(craft.transform) + " (m_craftingModeButton)";
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.FindPreferredPlaqueButton craft failed", ex);
            }

            Camera boxCam = null;
            if (done != null)
                boxCam = FindCmUiCamera(done.gameObject);
            else if (craft != null)
                boxCam = FindCmUiCamera(craft.gameObject);

            if (done != null && craft != null && boxCam != null)
            {
                float dw = GetScreenRect(boxCam, done.gameObject, corkOnly: false).width;
                float cw = GetScreenRect(boxCam, craft.gameObject, corkOnly: false).width;
                DebugLog.Msg(
                    $"KeywordFilterUi plaque pick: DoneW={dw:F0} CraftW={cw:F0} → prefer wider for Keywords");
                if (cw >= dw)
                {
                    path = craftPath;
                    return craft;
                }
                path = donePath;
                return done;
            }

            if (craft != null)
            {
                path = craftPath;
                return craft;
            }
            if (done != null)
            {
                path = donePath;
                return done;
            }

            try
            {
                UIBButton[] all = UnityEngine.Object.FindObjectsOfType<UIBButton>();
                for (int i = 0; i < all.Length; i++)
                {
                    UIBButton b = all[i];
                    if (b == null) continue;
                    string n = b.gameObject.name;
                    string t = null;
                    try { t = b.GetText(); } catch { /* ignore */ }
                    bool hit = n.IndexOf("Craft", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Done", StringComparison.OrdinalIgnoreCase) >= 0
                        || (!string.IsNullOrEmpty(t) && (
                            t.IndexOf("Craft", StringComparison.OrdinalIgnoreCase) >= 0
                            || t.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0));
                    if (hit)
                    {
                        path = GetHierarchyPath(b.transform) + " (scene UIBButton)";
                        return b;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.FindPreferredPlaqueButton scan failed", ex);
            }

            return null;
        }

        /// <summary>
        /// Screen-align plaque with BoxCamera onto the source Crafting/Back plane.
        /// Friends screen from BnetCam; unproject with BoxCam ray → source face plane.
        /// </summary>
        private static bool PlacePlaqueAboveFriendsBoxCam(
            GameObject go,
            BnetBarFriendButton friends,
            Camera bnetCam,
            Camera boxCam,
            float gapPx,
            UIBButton srcBtn)
        {
            if (go == null || friends == null || boxCam == null || bnetCam == null || srcBtn == null)
                return false;

            Rect friendsScr = GetFriendsScreenRect(friends, bnetCam);
            Rect srcScr = GetPlaqueScreenRect(boxCam, srcBtn.gameObject);
            float targetH = Mathf.Clamp(
                friendsScr.height > 20f ? friendsScr.height * 0.95f : 60f, 50f, 70f);
            // Start from source native screen height; scale later if needed.
            float halfH = srcScr.height > 8f
                ? Mathf.Min(srcScr.height, targetH) * 0.5f
                : targetH * 0.5f;

            Quaternion keepRot = srcBtn.transform.rotation;
            Vector3 keepLocalScale = srcBtn.transform.localScale;
            go.transform.rotation = keepRot;
            go.transform.localScale = keepLocalScale;

            // Face plane of the source plaque (reject edge-on normals vs BoxCam).
            Vector3 planeN = srcBtn.transform.forward;
            if (Mathf.Abs(Vector3.Dot(planeN, boxCam.transform.forward)) < 0.15f)
                planeN = -srcBtn.transform.up;
            if (Mathf.Abs(Vector3.Dot(planeN, boxCam.transform.forward)) < 0.15f)
                planeN = srcBtn.transform.up;
            Plane plaquePlane = new Plane(planeN, srcBtn.transform.position);

            float targetGap = Mathf.Clamp(gapPx, 10f, 16f);

            for (int i = 0; i < 24; i++)
            {
                Rect kNow = GetPlaqueScreenRect(boxCam, go);
                float useHalf = kNow.height > 8f ? kNow.height * 0.5f : halfH;
                float targetSx = friendsScr.center.x;
                float targetSy = friendsScr.yMax + targetGap + useHalf;

                Ray ray = boxCam.ScreenPointToRay(new Vector3(targetSx, targetSy, 0f));
                float enter;
                if (!plaquePlane.Raycast(ray, out enter) || enter < 0.05f)
                {
                    // Fallback: source depth ScreenToWorldPoint (same camera).
                    float depth = boxCam.WorldToScreenPoint(srcBtn.transform.position).z;
                    if (depth < 0.05f)
                        return false;
                    go.transform.position = boxCam.ScreenToWorldPoint(new Vector3(targetSx, targetSy, depth));
                }
                else
                {
                    go.transform.position = ray.GetPoint(enter);
                }
                go.transform.rotation = keepRot;

                Rect kScr = GetPlaqueScreenRect(boxCam, go);
                if (kScr.height < 2f)
                    continue;

                float gap = kScr.yMin - friendsScr.yMax;
                float xErr = friendsScr.center.x - kScr.center.x;
                bool overlap = ScreenRectsOverlap(kScr, friendsScr, 0f);

                // Uniform scale toward 50–70px height (from src localScale only).
                float hRatio = targetH / Mathf.Max(kScr.height, 0.5f);
                if (Mathf.Abs(hRatio - 1f) > 0.08f)
                {
                    hRatio = Mathf.Clamp(hRatio, 0.55f, 1.8f);
                    go.transform.localScale = keepLocalScale * hRatio;
                    keepLocalScale = go.transform.localScale;
                    go.transform.rotation = keepRot;
                    continue;
                }

                if (!overlap && Mathf.Abs(gap - targetGap) <= 2.5f && Mathf.Abs(xErr) <= 5f)
                    break;

                // Next loop recomputes target from updated half-height.
            }

            // Final nudge on plane using measured gap/x.
            for (int i = 0; i < 10; i++)
            {
                Rect kScr = GetPlaqueScreenRect(boxCam, go);
                float gap = kScr.yMin - friendsScr.yMax;
                float xErr = friendsScr.center.x - kScr.center.x;
                bool overlap = ScreenRectsOverlap(kScr, friendsScr, 0f);
                if (!overlap && Mathf.Abs(gap - targetGap) <= 1.5f && Mathf.Abs(xErr) <= 3f)
                    break;

                float sx = kScr.center.x + xErr;
                float sy = kScr.center.y + ((friendsScr.yMax + targetGap) - kScr.yMin);
                if (overlap)
                    sy += 10f;
                Ray ray = boxCam.ScreenPointToRay(new Vector3(sx, sy, 0f));
                float enter;
                if (plaquePlane.Raycast(ray, out enter) && enter > 0.05f)
                    go.transform.position = ray.GetPoint(enter);
                go.transform.rotation = keepRot;
            }

            Rect finalR = GetPlaqueScreenRect(boxCam, go);
            float finalGap = finalR.yMin - friendsScr.yMax;
            DebugLog.Msg(
                $"KeywordFilterUi PlacePlaqueBoxCam: srcScr={srcScr.width:F0}x{srcScr.height:F0} " +
                $"K={finalR.width:F0}x{finalR.height:F0} gap={finalGap:F1} " +
                $"lossy={FormatVec(go.transform.lossyScale)} local={FormatVec(go.transform.localScale)} " +
                $"world={FormatVec(go.transform.position)}");
            return finalR.width >= 80f && finalR.height >= 40f
                && finalR.width <= 420f && finalR.height <= 120f
                && finalGap >= 6f && finalGap <= 32f;
        }

        /// <summary>Mesh-only plaque screen rect — skip UberText / giant decorative bounds.</summary>
        private static Rect GetPlaqueScreenRect(Camera cam, GameObject go)
        {
            if (cam == null || go == null)
                return default;

            bool any = false;
            float xMin = float.PositiveInfinity;
            float xMax = float.NegativeInfinity;
            float yMin = float.PositiveInfinity;
            float yMax = float.NegativeInfinity;

            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; mrs != null && i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;
                if (mr.GetComponentInParent<UberText>() != null)
                    continue;
                Bounds wb = mr.bounds;
                if (wb.size.sqrMagnitude < 1e-6f)
                    continue;
                // Skip giant / degenerate meshes that blow up screen AABB.
                if (wb.size.x > 25f || wb.size.y > 25f || wb.size.z > 25f)
                    continue;
                Rect r = WorldBoundsToScreenRect(cam, wb);
                if (r.width < 1f && r.height < 1f)
                    continue;
                if (r.width > Screen.width * 0.55f || r.height > Screen.height * 0.45f)
                    continue;
                if (!any)
                {
                    xMin = r.xMin; xMax = r.xMax; yMin = r.yMin; yMax = r.yMax;
                    any = true;
                }
                else
                {
                    xMin = Mathf.Min(xMin, r.xMin);
                    xMax = Mathf.Max(xMax, r.xMax);
                    yMin = Mathf.Min(yMin, r.yMin);
                    yMax = Mathf.Max(yMax, r.yMax);
                }
            }

            if (!any)
                return GetScreenRect(cam, go, corkOnly: false);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void LogPlaqueVisibilityFail(GameObject go, Camera cam)
        {
            if (go == null || !DebugLog.VerboseDebug)
                return;
            int mask = cam != null ? cam.cullingMask : 0;
            bool layerInMask = cam != null && ((mask & (1 << go.layer)) != 0);
            DebugLog.Verbose(
                $"KeywordFilterUi plaque NOT isVisible: path={GetHierarchyPath(go.transform)} " +
                $"layer={go.layer} cam={(cam != null ? cam.name : "null")} " +
                $"layerInCullingMask={layerInMask} cullingMask=0x{mask:X8} " +
                $"pos={FormatVec(go.transform.position)} lossy={FormatVec(go.transform.lossyScale)}");
            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            int n = 0;
            for (int i = 0; mrs != null && i < mrs.Length && n < 6; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null) continue;
                DebugLog.Verbose(
                    $"  MR[{n}] '{mr.name}' enabled={mr.enabled} active={mr.gameObject.activeInHierarchy} " +
                    $"isVisible={mr.isVisible} layer={mr.gameObject.layer} bounds={FormatVec(mr.bounds.size)}");
                n++;
            }
        }

        private static void StripHostileHudBehaviours(GameObject go)
        {
            if (go == null)
                return;
            try
            {
                // CraftingModeButton : UIBButton : PegUIElement — destroy subclasses first.
                CraftingModeButton[] crafts = go.GetComponentsInChildren<CraftingModeButton>(true);
                for (int i = 0; i < crafts.Length; i++)
                {
                    if (crafts[i] != null)
                        UnityEngine.Object.DestroyImmediate(crafts[i]);
                }
                UIBButton[] uibs = go.GetComponentsInChildren<UIBButton>(true);
                for (int i = 0; i < uibs.Length; i++)
                {
                    if (uibs[i] != null)
                        UnityEngine.Object.DestroyImmediate(uibs[i]);
                }
                TooltipZone[] tips = go.GetComponentsInChildren<TooltipZone>(true);
                for (int i = 0; i < tips.Length; i++)
                {
                    if (tips[i] != null)
                        UnityEngine.Object.DestroyImmediate(tips[i]);
                }
                Transform[] all = go.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null)
                        continue;
                    string n = all[i].name;
                    // Keep Glow/highlight meshes for PegUI hover — only strip dust/bottle chrome.
                    if (n.IndexOf("dust", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("bottle", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        all[i].gameObject.SetActive(false);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.StripHostileHudBehaviours failed", ex);
            }
        }

        /// <summary>
        /// BnetBar FaceAndSize: Friends euler + Crafting src euler + probes; W≥100∧H≥45.
        /// </summary>
        private static bool FaceAndSizePlaqueOnBnet(
            GameObject go,
            BnetBarFriendButton friends,
            Camera bnetCam,
            float gapPx,
            float srcMag,
            Quaternion srcWorldRot,
            out Vector3 chosenEuler)
        {
            chosenEuler = go != null ? go.transform.eulerAngles : Vector3.zero;
            if (go == null || friends == null || bnetCam == null)
                return false;

            Vector3 pos = go.transform.position;
            float seed = Mathf.Max(srcMag, 1f);
            go.transform.localScale = new Vector3(seed, seed, seed);

            // World rotations: Friends HUD facing (historically visible) + Crafting src + camera-facing.
            Quaternion[] worldCands =
            {
                friends.transform.rotation,
                Quaternion.Euler(90f, 0f, 0f),
                Quaternion.Euler(90f, friends.transform.eulerAngles.y, 0f),
                srcWorldRot,
                Quaternion.Euler(srcWorldRot.eulerAngles.x, friends.transform.eulerAngles.y, srcWorldRot.eulerAngles.z),
                Quaternion.LookRotation(bnetCam.transform.forward, bnetCam.transform.up),
                Quaternion.LookRotation(-bnetCam.transform.forward, bnetCam.transform.up),
                Quaternion.LookRotation(bnetCam.transform.forward, friends.transform.up),
                Quaternion.LookRotation(-bnetCam.transform.forward, friends.transform.up),
                Quaternion.LookRotation(bnetCam.transform.forward, Vector3.up),
                Quaternion.LookRotation(-bnetCam.transform.forward, Vector3.up),
            };

            Vector3[] localExtras =
            {
                Vector3.zero,
                new Vector3(90f, 0f, 0f),
                new Vector3(-90f, 0f, 0f),
                new Vector3(0f, 90f, 0f),
                new Vector3(0f, -90f, 0f),
                new Vector3(0f, 0f, 90f),
                new Vector3(0f, 0f, -90f),
                new Vector3(0f, 180f, 0f),
                new Vector3(180f, 0f, 0f),
                new Vector3(0f, 180f, 180f),
                new Vector3(90f, 0f, 90f),
                new Vector3(-90f, 180f, 0f),
                new Vector3(90f, 180f, 0f),
                new Vector3(0f, 90f, 90f),
                new Vector3(90f, 270f, 0f),
                new Vector3(-90f, 270f, 0f),
            };

            Quaternion bestLocal = Quaternion.identity;
            Quaternion bestWorld = friends.transform.rotation;
            float bestScore = -1f;
            bool found = false;
            bool useWorld = true;

            for (int i = 0; i < worldCands.Length; i++)
            {
                go.transform.rotation = worldCands[i];
                go.transform.position = pos;
                go.transform.localScale = new Vector3(seed, seed, seed);
                Rect r = GetPlaqueScreenRect(bnetCam, go);
                if (r.width < 100f || r.height < 45f)
                    continue;
                float score = Mathf.Min(r.width, r.height);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWorld = worldCands[i];
                    useWorld = true;
                    found = true;
                }
            }

            for (int i = 0; i < localExtras.Length; i++)
            {
                go.transform.localRotation = Quaternion.Euler(localExtras[i]);
                go.transform.position = pos;
                go.transform.localScale = new Vector3(seed, seed, seed);
                Rect r = GetPlaqueScreenRect(bnetCam, go);
                if (r.width < 100f || r.height < 45f)
                    continue;
                float score = Mathf.Min(r.width, r.height);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLocal = Quaternion.Euler(localExtras[i]);
                    useWorld = false;
                    found = true;
                }
            }

            if (!found)
            {
                bestScore = -1f;
                for (int i = 0; i < worldCands.Length; i++)
                {
                    go.transform.rotation = worldCands[i];
                    go.transform.localScale = new Vector3(seed, seed, seed);
                    go.transform.position = pos;
                    Rect r = GetPlaqueScreenRect(bnetCam, go);
                    float score = Mathf.Min(r.width, r.height);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWorld = worldCands[i];
                        useWorld = true;
                    }
                }
                for (int i = 0; i < localExtras.Length; i++)
                {
                    go.transform.localRotation = Quaternion.Euler(localExtras[i]);
                    go.transform.localScale = new Vector3(seed, seed, seed);
                    go.transform.position = pos;
                    Rect r = GetPlaqueScreenRect(bnetCam, go);
                    float score = Mathf.Min(r.width, r.height);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestLocal = Quaternion.Euler(localExtras[i]);
                        useWorld = false;
                    }
                }
            }

            if (useWorld)
                go.transform.rotation = bestWorld;
            else
                go.transform.localRotation = bestLocal;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(seed, seed, seed);
            chosenEuler = go.transform.eulerAngles;

            Rect friendsScr = GetFriendsScreenRect(friends, bnetCam);
            float targetH = Mathf.Clamp(friendsScr.height > 20f ? friendsScr.height * 0.95f : 65f, 50f, 80f);
            const float targetW = 200f;

            for (int i = 0; i < 18; i++)
            {
                Rect kScr = GetPlaqueScreenRect(bnetCam, go);
                float h = Mathf.Max(kScr.height, 0.25f);
                float w = Mathf.Max(kScr.width, 0.25f);

                if (w >= 100f && h >= 45f && h >= 50f && h <= 85f && w >= 140f && w <= 300f)
                    break;

                float ratioH = targetH / h;
                float ratioW = targetW / w;
                float ratio = Mathf.Min(ratioH, ratioW);
                if (w < 100f || h < 45f)
                    ratio = Mathf.Max(ratioH, ratioW);

                float maxStep = i < 5 ? 10f : (i < 10 ? 3f : 1.4f);
                ratio = Mathf.Clamp(ratio, 1f / maxStep, maxStep);
                go.transform.localScale = go.transform.localScale * ratio;

                if (useWorld)
                    go.transform.rotation = bestWorld;
                else
                    go.transform.localRotation = bestLocal;
                go.transform.position = pos;
            }

            NudgeAboveFriendsOnBnet(go, friends, bnetCam, gapPx);
            AlignPlaqueToFriendsScreen(go, friends, bnetCam, gapPx);
            chosenEuler = go.transform.eulerAngles;

            Rect finalR = GetPlaqueScreenRect(bnetCam, go);
            DebugLog.Verbose(
                $"KeywordFilterUi FaceAndSizePlaque: euler={FormatVec(chosenEuler)} useWorld={useWorld} " +
                $"lossy={FormatVec(go.transform.lossyScale)} " +
                $"K(bnet)={finalR.width:F0}x{finalR.height:F0} " +
                $"pass={finalR.width >= 100f && finalR.height >= 45f}");
            return finalR.width >= 100f && finalR.height >= 45f;
        }

        /// <summary>
        /// Wide plaque centered on Friends overhangs left off-screen. Shift so
        /// K.xMin ≥ max(Friends.xMin, 0) while keeping ~gapPx above Friends.
        /// </summary>
        private static void AlignPlaqueToFriendsScreen(
            GameObject go,
            BnetBarFriendButton friends,
            Camera bnetCam,
            float gapPx)
        {
            if (go == null || friends == null || bnetCam == null)
                return;

            Rect friendsScr = GetFriendsScreenRect(friends, bnetCam);
            Rect before = GetPlaqueScreenRect(bnetCam, go);
            DebugLog.Verbose(
                $"KeywordFilterUi AlignPlaque BEFORE: " +
                $"FriendsX=[{friendsScr.xMin:F0}..{friendsScr.xMax:F0}] " +
                $"KX=[{before.xMin:F0}..{before.xMax:F0}]");

            Quaternion keepRot = go.transform.rotation;
            float wantMinX = Mathf.Max(friendsScr.xMin, 0f);
            float targetGap = Mathf.Clamp(gapPx, 10f, 16f);

            for (int i = 0; i < 16; i++)
            {
                Rect kScr = GetPlaqueScreenRect(bnetCam, go);
                if (kScr.width < 2f)
                    break;

                float xErr = wantMinX - kScr.xMin;
                float gapErr = (friendsScr.yMax + targetGap) - kScr.yMin;
                bool overlap = ScreenRectsOverlap(kScr, friendsScr, 0f);
                if (Mathf.Abs(xErr) <= 1.5f && Mathf.Abs(gapErr) <= 1.5f && !overlap)
                    break;

                Vector3 sp = bnetCam.WorldToScreenPoint(go.transform.position);
                sp.x += xErr;
                if (gapErr > 1.5f || overlap)
                    sp.y += Mathf.Max(gapErr, overlap ? 8f : 2f);
                else if (gapErr < -1.5f)
                    sp.y += gapErr;
                go.transform.position = bnetCam.ScreenToWorldPoint(sp);
                go.transform.rotation = keepRot;
            }

            // UX: nudge ~10% of plaque width further right (min ~20px).
            Rect mid = GetPlaqueScreenRect(bnetCam, go);
            if (mid.width > 2f)
            {
                float nudgePx = Mathf.Max(20f, mid.width * 0.10f);
                Vector3 sp = bnetCam.WorldToScreenPoint(go.transform.position);
                sp.x += nudgePx;
                go.transform.position = bnetCam.ScreenToWorldPoint(sp);
                go.transform.rotation = keepRot;
            }

            Rect after = GetPlaqueScreenRect(bnetCam, go);
            DebugLog.Verbose(
                $"KeywordFilterUi AlignPlaque AFTER: " +
                $"FriendsX=[{friendsScr.xMin:F0}..{friendsScr.xMax:F0}] " +
                $"KX=[{after.xMin:F0}..{after.xMax:F0}] gap={(after.yMin - friendsScr.yMax):F1}");
        }

        private static void ApplyKeywordsLabel(GameObject go)
        {
            try
            {
                string text = OpenLabel();
                if (s_openLabelText == null && go != null)
                    s_openLabelText = go.GetComponentInChildren<UberText>(true);
                if (s_openLabelText != null)
                {
                    s_openLabelText.gameObject.SetActive(true);
                    s_openLabelText.Text = text;
                    s_openLabelText.UpdateNow(true);
                }
                // Stamp plaque/cork leftovers: Back, Crafting, friend counts, K/K*.
                if (go != null)
                {
                    UberText[] all = go.GetComponentsInChildren<UberText>(true);
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null)
                            continue;
                        string cur = all[i].Text ?? string.Empty;
                        bool replace = string.IsNullOrEmpty(cur)
                            || cur == "K" || cur == "K*" || IsMostlyDigits(cur)
                            || cur.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0
                            || cur.IndexOf("Done", StringComparison.OrdinalIgnoreCase) >= 0
                            || cur.IndexOf("Craft", StringComparison.OrdinalIgnoreCase) >= 0
                            || all[i] == s_openLabelText;
                        if (!replace)
                            continue;
                        all[i].Text = text;
                        all[i].UpdateNow(true);
                    }
                }
                DebugLog.Msg($"KeywordFilterUi ApplyKeywordsLabel: text='{text}'");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.ApplyKeywordsLabel failed", ex);
            }
        }

        private static bool IsMostlyDigits(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            int digits = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsDigit(s[i]))
                    digits++;
            }
            return digits > 0 && digits >= s.Length - 1;
        }

        private static void DestroyCorkPivot()
        {
            if (s_corkPivot != null)
            {
                UnityEngine.Object.Destroy(s_corkPivot);
                s_corkPivot = null;
            }
            DestroyNamedLeftovers(CorkPivotName);
        }

        private static Camera FindBnetCamera(BnetBarFriendButton friends)
        {
            try
            {
                BaseUI ui = BaseUI.Get();
                if (ui != null)
                {
                    Camera c = ui.GetBnetCamera();
                    if (c != null)
                        return c;
                }
            }
            catch
            {
                // ignore
            }

            if (friends != null)
            {
                Camera layerCam = FindCameraForLayer(friends.gameObject.layer);
                if (layerCam != null)
                    return layerCam;
            }
            return null;
        }

        private static Rect GetFriendsScreenRect(BnetBarFriendButton friends, Camera bnetCam)
        {
            if (friends == null || bnetCam == null)
                return default;
            Rect r = GetScreenRect(bnetCam, friends.gameObject, corkOnly: false);
            if (r.width >= 1f && r.height >= 1f)
                return r;
            Vector3 fs = bnetCam.WorldToScreenPoint(friends.transform.position);
            return new Rect(fs.x - 20f, fs.y - 12f, 40f, 24f);
        }

        /// <summary>
        /// Full cork visual AABB for aspect/size asserts — prefer SortButton_mesh / cork face,
        /// else union of all non-UberText MeshRenderers (not CurrentIcon-only tight bounds).
        /// </summary>
        private static Bounds GetFullCorkMeshBounds(GameObject go)
        {
            if (go == null)
                return default;

            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                string n = all[i].name;
                if (n.IndexOf("SortButton_mesh", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("SortButtonMesh", StringComparison.OrdinalIgnoreCase) >= 0
                    || (n.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0
                        && n.IndexOf("mesh", StringComparison.OrdinalIgnoreCase) >= 0
                        && n.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    MeshRenderer mr = all[i].GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled && mr.gameObject.activeInHierarchy)
                        return mr.bounds;
                }
            }

            return GetCorkRendererBounds(go);
        }

        private static float AverageAbs(Vector3 v)
        {
            return (Mathf.Abs(v.x) + Mathf.Abs(v.y) + Mathf.Abs(v.z)) / 3f;
        }

        /// <summary>max(|axis|) / min(|axis|) — &lt;2 means near-uniform.</summary>
        private static float LossyScaleAxisRatio(Vector3 lossy)
        {
            float ax = Mathf.Abs(lossy.x);
            float ay = Mathf.Abs(lossy.y);
            float az = Mathf.Abs(lossy.z);
            float max = Mathf.Max(ax, Mathf.Max(ay, az));
            float min = Mathf.Min(ax, Mathf.Min(ay, az));
            if (min < 1e-4f)
                return max < 1e-4f ? 1f : 999f;
            return max / min;
        }

        private static bool AnyRendererEnabled(GameObject go)
        {
            if (go == null)
                return false;
            Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null && rends[i].enabled)
                    return true;
            }
            return false;
        }

        private static bool TryGetScreenUpWorldBasis(
            Camera cam,
            Vector3 planePoint,
            out Vector3 screenUpWorld,
            out float worldPerPx)
        {
            screenUpWorld = Vector3.up;
            worldPerPx = 0.05f;
            if (cam == null)
                return false;

            Vector3 sp = cam.WorldToScreenPoint(planePoint);
            if (sp.z <= 0.01f)
                return false;

            Plane plane = new Plane(-cam.transform.forward, planePoint);
            Ray r0 = cam.ScreenPointToRay(sp);
            Ray r1 = cam.ScreenPointToRay(sp + new Vector3(0f, 100f, 0f));
            if (!plane.Raycast(r0, out float d0) || !plane.Raycast(r1, out float d1))
                return false;

            Vector3 w0 = r0.GetPoint(d0);
            Vector3 w1 = r1.GetPoint(d1);
            Vector3 delta = w1 - w0;
            float mag = delta.magnitude;
            if (mag < 1e-4f)
                return false;

            screenUpWorld = delta / mag;
            worldPerPx = mag / 100f;
            return worldPerPx > 1e-5f;
        }

        /// <summary>
        /// Copy each child's activeSelf from Select Set so glow/highlight stay off when Sets has them off.
        /// CurrentIcon / button meshes follow source (book icon OK).
        /// </summary>
        private static void SyncChildActiveFromSource(GameObject srcRoot, GameObject dstRoot)
        {
            if (srcRoot == null || dstRoot == null)
                return;
            Transform[] srcAll = srcRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < srcAll.Length; i++)
            {
                Transform s = srcAll[i];
                if (s == null || s == srcRoot.transform)
                    continue;
                string rel = GetRelativePath(srcRoot.transform, s);
                if (string.IsNullOrEmpty(rel))
                    continue;
                Transform d = dstRoot.transform.Find(rel);
                if (d == null)
                    continue;
                // Don't deactivate our BCM_KLabel if we added one.
                if (d.name.StartsWith("BCM_", StringComparison.Ordinal))
                    continue;
                if (d.gameObject.activeSelf != s.gameObject.activeSelf)
                    d.gameObject.SetActive(s.gameObject.activeSelf);
                MeshRenderer smr = s.GetComponent<MeshRenderer>();
                MeshRenderer dmr = d.GetComponent<MeshRenderer>();
                if (smr != null && dmr != null)
                    dmr.enabled = smr.enabled;
            }
        }

        private static void ForceOffNamedChrome(GameObject go, params string[] nameParts)
        {
            if (go == null || nameParts == null)
                return;
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == go.transform)
                    continue;
                if (t.name.StartsWith("BCM_", StringComparison.Ordinal))
                    continue;
                string n = t.name;
                for (int p = 0; p < nameParts.Length; p++)
                {
                    if (n.IndexOf(nameParts[p], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        t.gameObject.SetActive(false);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Bottom bar: Mana/Search/Crafting/Sets stay vanilla. Keywords cork sits snug
        /// immediately left of Sets. Only if K is clipped under left chrome, nudge Sets+K
        /// together by a tiny amount — never move mana/search/crafting.
        /// </summary>
        private static void ApplyBottomBarLayout(CollectionSetFilterDropdownToggle setsToggle)
        {
            if (setsToggle == null || s_openButton == null)
                return;

            try
            {
                // Undo any prior layout (mana/search/container back to vanilla).
                RestoreBottomBarLayout("before-reapply");

                Camera uiCam = FindCmUiCamera(setsToggle.gameObject);
                if (uiCam == null)
                {
                    DebugLog.Error("KeywordFilterUi bottom-bar: no UI camera — abort layout");
                    return;
                }

                Transform trayRoot = setsToggle.transform.parent;
                s_setFilterContainer = trayRoot != null ? trayRoot.parent : null;
                ResolveManaAndSearch(out s_manaTabManager, out s_searchBar);
                Transform crafting = ResolveCraftingTransform();

                if (s_setFilterContainer == null)
                {
                    DebugLog.Warning("KeywordFilterUi ApplyBottomBarLayout: SetFilterContainer missing");
                    return;
                }

                // Snapshot vanilla locals (mana/search never written — only restored if we nudge Sets).
                s_origSetFilterContainerLocal = s_setFilterContainer.localPosition;
                if (s_manaTabManager != null)
                    s_origManaTabLocal = s_manaTabManager.localPosition;
                if (s_searchBar != null)
                    s_origSearchLocal = s_searchBar.localPosition;

                Transform kTf = s_openButton.transform;
                Quaternion setsRot = setsToggle.transform.rotation;
                Vector3 setsLossy = setsToggle.transform.lossyScale;

                Vector3 sets0 = setsToggle.transform.position;
                Bounds setsCorkWorld = GetTightCorkBounds(setsToggle.gameObject);
                if (!TryGetScreenRightWorldBasis(
                        uiCam, setsCorkWorld.center, out Vector3 screenRightWorld, out float worldPerPx))
                {
                    DebugLog.Error("KeywordFilterUi bottom-bar: screen↔world basis failed — abort");
                    return;
                }

                Rect setsScr0 = WorldBoundsToScreenRect(uiCam, setsCorkWorld);
                float corkScreenW = Mathf.Max(8f, setsScr0.width);
                float pitch = corkScreenW * worldPerPx; // visual cork width, not inflated AABB*1.12
                if (pitch < 0.5f)
                    pitch = 3.5f;

                Vector3 manaWorld0 = s_manaTabManager != null ? s_manaTabManager.position : Vector3.zero;
                Vector3 searchWorld0 = s_searchBar != null ? s_searchBar.position : Vector3.zero;
                Rect manaScr0 = GetManaGemsScreenRect(uiCam, s_manaTabManager);
                Rect searchScr0 = s_searchBar != null
                    ? GetScreenRect(uiCam, s_searchBar.gameObject, corkOnly: false)
                    : default;
                Rect craftScr0 = crafting != null
                    ? GetScreenRect(uiCam, crafting.gameObject, corkOnly: false)
                    : default;

                DebugLog.Msg(
                    $"KeywordFilterUi bottom-bar SCREEN BEFORE: " +
                    $"Sets=[{setsScr0.xMin:F0}..{setsScr0.xMax:F0}] cx={setsScr0.center.x:F0} " +
                    $"Mana=[{manaScr0.xMin:F0}..{manaScr0.xMax:F0}] " +
                    $"Search=[{searchScr0.xMin:F0}..{searchScr0.xMax:F0}] " +
                    $"Craft=[{craftScr0.xMin:F0}..{craftScr0.xMax:F0}] " +
                    $"corkW={corkScreenW:F0}px pitch={pitch:F3} worldPerPx={worldPerPx:F4}");

                // Place K at Sets pose, then slide left until K.maxX ≈ Sets.minX − gapPx.
                // ~16px (~0.15–0.25×corkW) so corks look like two distinct buttons, not overlapping.
                float gapPx = Mathf.Clamp(corkScreenW * 0.2f, 12f, 20f);
                kTf.position = sets0;
                kTf.rotation = setsRot;
                ApplyWorldScale(kTf, setsLossy);

                for (int i = 0; i < 8; i++)
                {
                    Rect kR = WorldBoundsToScreenRect(uiCam, GetTightCorkBounds(kTf.gameObject));
                    Rect sR = WorldBoundsToScreenRect(uiCam, GetTightCorkBounds(setsToggle.gameObject));
                    float err = (sR.xMin - gapPx) - kR.xMax; // positive => K too far left; move right
                    if (Mathf.Abs(err) < 1.5f)
                        break;
                    kTf.position += screenRightWorld * (err * worldPerPx);
                    kTf.rotation = setsRot;
                    ApplyWorldScale(kTf, setsLossy);
                }

                float setsDelta = 0f;
                float manaDelta = 0f;
                float searchDelta = 0f;

                // Only if K is clipped under left chrome: nudge Sets+K together (cap ≤ 0.5×cork).
                // Prefer leaving Sets vanilla if K is mostly visible.
                Rect kScr = WorldBoundsToScreenRect(uiCam, GetTightCorkBounds(kTf.gameObject));
                const float marginPx = 4f;
                // "Mostly visible" = at least ~55% of cork width on-screen.
                float visibleFrac = kScr.width > 1f
                    ? Mathf.Clamp01((kScr.xMax - Mathf.Max(0f, kScr.xMin)) / kScr.width)
                    : 0f;
                if (kScr.xMin < marginPx && visibleFrac < 0.55f)
                {
                    float needPx = marginPx - kScr.xMin;
                    float nudgeWorld = Mathf.Min(needPx * worldPerPx, pitch * 0.5f);
                    if (nudgeWorld > 0.05f)
                    {
                        s_setFilterContainer.position += screenRightWorld * nudgeWorld;
                        kTf.position += screenRightWorld * nudgeWorld;
                        kTf.rotation = setsRot;
                        ApplyWorldScale(kTf, setsLossy);
                        setsDelta = nudgeWorld;
                        s_bottomBarOffsetsApplied = true;
                        DebugLog.Msg(
                            $"KeywordFilterUi bottom-bar: Sets+K nudged +{nudgeWorld:F3} " +
                            $"(K was clipped, visibleFrac={visibleFrac:F2})");
                    }
                }

                // Mana/Search/Crafting never moved — re-assert unchanged.
                if (s_manaTabManager != null
                    && Vector3.Distance(s_manaTabManager.position, manaWorld0) > 0.001f)
                {
                    s_manaTabManager.position = manaWorld0;
                    DebugLog.Warning("KeywordFilterUi bottom-bar: mana drifted — forced vanilla");
                }
                if (s_searchBar != null
                    && Vector3.Distance(s_searchBar.position, searchWorld0) > 0.001f)
                {
                    s_searchBar.position = searchWorld0;
                    DebugLog.Warning("KeywordFilterUi bottom-bar: search drifted — forced vanilla");
                }

                // Mark offsets only if Sets container moved; else nothing to restore for mana/search.
                if (setsDelta > 0.01f)
                    s_bottomBarOffsetsApplied = true;

                kScr = WorldBoundsToScreenRect(uiCam, GetTightCorkBounds(kTf.gameObject));
                Rect setsScr = WorldBoundsToScreenRect(uiCam, GetTightCorkBounds(setsToggle.gameObject));
                Rect manaScr = GetManaGemsScreenRect(uiCam, s_manaTabManager);
                Rect searchScr = s_searchBar != null
                    ? GetScreenRect(uiCam, s_searchBar.gameObject, corkOnly: false)
                    : default;
                Rect craftScr = crafting != null
                    ? GetScreenRect(uiCam, crafting.gameObject, corkOnly: false)
                    : default;

                float gapKSets = setsScr.xMin - kScr.xMax;
                bool adjacent = gapKSets >= 10f && gapKSets <= 24f;
                bool overlapMesh = gapKSets < -1f;
                bool searchCraftOk = searchScr.width < 1f || craftScr.width < 1f
                    || searchScr.xMax <= craftScr.xMin + 2f;
                bool manaUntouched = s_manaTabManager == null
                    || Vector3.Distance(s_manaTabManager.position, manaWorld0) < 0.01f;
                bool searchUntouched = s_searchBar == null
                    || Vector3.Distance(s_searchBar.position, searchWorld0) < 0.01f;

                // Re-bind PegUI / hover / click after world move (collider pose + listeners).
                WireKeywordCorkInteraction(kTf.gameObject);

                DebugLog.Msg(
                    $"KeywordFilterUi bottom-bar SCREEN AFTER: " +
                    $"K=[{kScr.xMin:F0}..{kScr.xMax:F0}] cx={kScr.center.x:F0} " +
                    $"Sets=[{setsScr.xMin:F0}..{setsScr.xMax:F0}] cx={setsScr.center.x:F0} " +
                    $"gapKSets={gapKSets:F1}px targetGap={gapPx:F1}px adjacent={adjacent} overlap={overlapMesh} " +
                    $"Mana=[{manaScr.xMin:F0}..{manaScr.xMax:F0}] " +
                    $"Search=[{searchScr.xMin:F0}..{searchScr.xMax:F0}] " +
                    $"Craft=[{craftScr.xMin:F0}..{craftScr.xMax:F0}] " +
                    $"searchCraftOk={searchCraftOk}");
                DebugLog.Msg(
                    $"KeywordFilterUi bottom-bar deltas: setsDelta={setsDelta:F3} " +
                    $"manaDelta={manaDelta:F3} searchDelta={searchDelta:F3} " +
                    $"manaUntouched={manaUntouched} searchUntouched={searchUntouched}");
                DebugLog.Msg(
                    $"KeywordFilterUi bottom-bar WORLD: " +
                    $"sets={FormatVec(setsToggle.transform.position)} K={FormatVec(kTf.position)} " +
                    $"mana={FormatVec(manaWorld0)} search={FormatVec(searchWorld0)}");

                if (!manaUntouched || !searchUntouched)
                    DebugLog.Error("KeywordFilterUi bottom-bar: mana/search were moved — should be vanilla");
                if (overlapMesh)
                    DebugLog.Warning($"KeywordFilterUi bottom-bar: K|Sets mesh overlap (gap={gapKSets:F1}px)");
                else if (!adjacent)
                    DebugLog.Warning($"KeywordFilterUi bottom-bar: K|Sets gap={gapKSets:F1}px (want ~{gapPx:F0}px)");
                if (!searchCraftOk)
                    DebugLog.Warning("KeywordFilterUi bottom-bar: Search overlaps Crafting");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.ApplyBottomBarLayout failed", ex);
            }
        }

        /// <summary>
        /// Tight cork visual bounds — prefer CurrentIcon / small mesh renderers over inflated
        /// full-toggle AABB that made center-to-center spacing look like a full cork gap.
        /// </summary>
        private static Bounds GetTightCorkBounds(GameObject go)
        {
            if (go == null)
                return default;

            Transform icon = null;
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                string n = all[i].name;
                if (n.IndexOf("CurrentIcon", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.Equals("Icon", StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf("ButtonMesh", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Cork", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MeshRenderer mr = all[i].GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                    {
                        icon = all[i];
                        break;
                    }
                }
            }

            if (icon != null)
            {
                MeshRenderer mr = icon.GetComponent<MeshRenderer>();
                if (mr != null)
                    return mr.bounds;
            }

            // Fallback: smallest enabled MeshRenderer (skip UberText / giant bounds).
            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            MeshRenderer best = null;
            float bestVol = float.PositiveInfinity;
            for (int i = 0; mrs != null && i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;
                if (mr.GetComponentInParent<UberText>() != null)
                    continue;
                Bounds b = mr.bounds;
                float vol = b.size.x * b.size.y * b.size.z;
                if (vol < 1e-6f || vol > 500f)
                    continue;
                if (vol < bestVol)
                {
                    bestVol = vol;
                    best = mr;
                }
            }
            if (best != null)
                return best.bounds;

            return GetCorkRendererBounds(go);
        }

        private static void RestoreBottomBarLayout(string reason)
        {
            if (!s_bottomBarOffsetsApplied)
                return;
            try
            {
                if (s_setFilterContainer != null)
                    s_setFilterContainer.localPosition = s_origSetFilterContainerLocal;
                if (s_manaTabManager != null)
                    s_manaTabManager.localPosition = s_origManaTabLocal;
                if (s_searchBar != null)
                    s_searchBar.localPosition = s_origSearchLocal;

                DebugLog.Msg(
                    $"KeywordFilterUi bottom-bar RESTORED ({reason}): " +
                    $"containerLocal={FormatVec(s_origSetFilterContainerLocal)} " +
                    $"manaLocal={FormatVec(s_origManaTabLocal)} " +
                    $"searchLocal={FormatVec(s_origSearchLocal)}");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.RestoreBottomBarLayout failed", ex);
            }
            finally
            {
                s_bottomBarOffsetsApplied = false;
            }
        }

        private static void ResolveManaAndSearch(out Transform mana, out Transform search)
        {
            mana = null;
            search = null;
            try
            {
                if (CollectionManager.Get()?.GetCollectibleDisplay() is CollectionManagerDisplay cmd)
                {
                    if (cmd.m_manaTabManager != null)
                        mana = cmd.m_manaTabManager.transform;
                    if (cmd.m_search != null)
                        search = cmd.m_search.transform;
                }
            }
            catch
            {
                // ignore
            }

            if (mana == null || search == null)
            {
                Transform cm = s_setFilterContainer != null ? s_setFilterContainer.parent : null;
                if (cm != null)
                {
                    if (mana == null)
                    {
                        Transform t = FindChildRecursive(cm, "ManaTabManager");
                        if (t != null)
                            mana = t;
                    }
                    if (search == null)
                    {
                        Transform t = FindChildRecursive(cm, "Search");
                        if (t != null)
                            search = t;
                    }
                }
            }
        }

        private static Transform ResolveCraftingTransform()
        {
            Transform cm = s_setFilterContainer != null ? s_setFilterContainer.parent : null;
            if (cm == null)
                return null;
            Transform t = FindChildRecursive(cm, "CraftingTray");
            if (t != null)
                return t;
            t = FindChildRecursive(cm, "Crafting");
            return t;
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            if (root.name == name)
                return root;
            Transform direct = root.Find(name);
            if (direct != null)
                return direct;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                    return all[i];
            }
            return null;
        }

        /// <summary>
        /// World direction that increases screen-X at planePoint, plus world units per screen pixel.
        /// </summary>
        private static bool TryGetScreenRightWorldBasis(
            Camera cam,
            Vector3 planePoint,
            out Vector3 screenRightWorld,
            out float worldPerPx)
        {
            screenRightWorld = Vector3.right;
            worldPerPx = 0.05f;
            if (cam == null)
                return false;

            Vector3 sp = cam.WorldToScreenPoint(planePoint);
            if (sp.z <= 0.01f)
                return false;

            Plane plane = new Plane(-cam.transform.forward, planePoint);
            Ray r0 = cam.ScreenPointToRay(sp);
            Ray r1 = cam.ScreenPointToRay(sp + new Vector3(100f, 0f, 0f));
            if (!plane.Raycast(r0, out float d0) || !plane.Raycast(r1, out float d1))
                return false;

            Vector3 w0 = r0.GetPoint(d0);
            Vector3 w1 = r1.GetPoint(d1);
            Vector3 delta = w1 - w0;
            float mag = delta.magnitude;
            if (mag < 1e-4f)
                return false;

            screenRightWorld = delta / mag;
            worldPerPx = mag / 100f;
            return worldPerPx > 1e-5f;
        }

        private static void NudgeToScreenGap(
            Camera cam,
            Transform kTf,
            Transform setsTf,
            Vector3 screenRightWorld,
            float worldPerPx,
            float padPx)
        {
            if (cam == null || kTf == null || setsTf == null)
                return;
            for (int i = 0; i < 4; i++)
            {
                Rect k = GetScreenRect(cam, kTf.gameObject, corkOnly: true);
                Rect s = GetScreenRect(cam, setsTf.gameObject, corkOnly: true);
                float gap = s.xMin - k.xMax;
                float err = gap - padPx;
                if (Mathf.Abs(err) < 2f)
                    break;
                kTf.position += screenRightWorld * (err * worldPerPx);
            }
        }

        private static Rect GetScreenRect(Camera cam, GameObject go, bool corkOnly)
        {
            if (cam == null || go == null)
                return default;
            Bounds b = corkOnly ? GetCorkRendererBounds(go) : GetRendererBounds(go);
            return WorldBoundsToScreenRect(cam, b);
        }

        /// <summary>Union screen rect of mana gem meshes — never the ManaTabManager root alone.</summary>
        private static Rect GetManaGemsScreenRect(Camera cam, Transform manaTab)
        {
            if (cam == null || manaTab == null)
                return default;

            Transform container = manaTab.Find("ManaCrystalContainer");
            if (container == null)
                container = FindChildRecursive(manaTab, "ManaCrystalContainer");
            Transform root = container != null ? container : manaTab;

            bool any = false;
            float xMin = float.PositiveInfinity;
            float xMax = float.NegativeInfinity;
            float yMin = float.PositiveInfinity;
            float yMax = float.NegativeInfinity;

            MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; mrs != null && i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;
                // Skip giant decorative meshes.
                Bounds wb = mr.bounds;
                if (wb.size.sqrMagnitude < 0.0001f || wb.size.x > 40f || wb.size.z > 40f)
                    continue;
                Rect r = WorldBoundsToScreenRect(cam, wb);
                if (r.width < 1f && r.height < 1f)
                    continue;
                if (!any)
                {
                    xMin = r.xMin;
                    xMax = r.xMax;
                    yMin = r.yMin;
                    yMax = r.yMax;
                    any = true;
                }
                else
                {
                    xMin = Mathf.Min(xMin, r.xMin);
                    xMax = Mathf.Max(xMax, r.xMax);
                    yMin = Mathf.Min(yMin, r.yMin);
                    yMax = Mathf.Max(yMax, r.yMax);
                }
            }

            if (!any)
                return default;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect WorldBoundsToScreenRect(Camera cam, Bounds b)
        {
            if (cam == null)
                return default;
            Vector3 c = b.center;
            Vector3 e = b.extents;
            float xMin = float.PositiveInfinity;
            float xMax = float.NegativeInfinity;
            float yMin = float.PositiveInfinity;
            float yMax = float.NegativeInfinity;
            bool any = false;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iy = -1; iy <= 1; iy += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                Vector3 sp = cam.WorldToScreenPoint(c + new Vector3(e.x * ix, e.y * iy, e.z * iz));
                if (sp.z <= 0.01f)
                    continue;
                any = true;
                xMin = Mathf.Min(xMin, sp.x);
                xMax = Mathf.Max(xMax, sp.x);
                yMin = Mathf.Min(yMin, sp.y);
                yMax = Mathf.Max(yMax, sp.y);
            }
            if (!any)
                return default;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool ScreenRectsOverlap(Rect a, Rect b, float padPx)
        {
            if (a.width < 1f || b.width < 1f)
                return false;
            return a.xMin < b.xMax + padPx && b.xMin < a.xMax + padPx
                && a.yMin < b.yMax + padPx && b.yMin < a.yMax + padPx;
        }

        private static void PreserveOriginalBoxCollider(
            GameObject go,
            Vector3 localCenter,
            Vector3 localSize,
            bool isTrigger)
        {
            if (go == null)
                return;
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            // Restore Select Set local collider — never rewrite from world mesh bounds.
            box.center = localCenter;
            box.size = localSize;
            box.isTrigger = isTrigger;
            box.enabled = true;
        }

        private static float MeshColDelta(GameObject go)
        {
            if (go == null)
                return 0f;
            Bounds cork = GetCorkRendererBounds(go);
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                return 0f;
            Vector3 colCenter = go.transform.TransformPoint(box.center);
            return Vector3.Distance(cork.center, colCenter);
        }

        private static bool AnyRendererVisible(GameObject go)
        {
            if (go == null)
                return false;
            Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null && rends[i].enabled && rends[i].isVisible)
                    return true;
            }
            return false;
        }

        /// <summary>Bounds from cork MeshRenderers only (exclude UberText — those skew meshColΔ).</summary>
        private static Bounds GetCorkRendererBounds(GameObject go)
        {
            Bounds bounds = new Bounds(go != null ? go.transform.position : Vector3.zero, Vector3.zero);
            bool any = false;
            if (go == null)
                return bounds;
            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || !mr.enabled)
                    continue;
                if (mr.GetComponentInParent<UberText>() != null)
                    continue;
                if (!any)
                {
                    bounds = mr.bounds;
                    any = true;
                }
                else
                    bounds.Encapsulate(mr.bounds);
            }
            if (!any)
                return GetRendererBounds(go);
            return bounds;
        }

        private static void EnsureCorkMeshesLikeSource(GameObject go, CollectionSetFilterDropdownToggle src)
        {
            if (go == null)
                return;
            // Re-enable every MeshRenderer that isn't UberText.
            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || mr.GetComponentInParent<UberText>() != null)
                    continue;
                mr.gameObject.SetActive(true);
                mr.enabled = true;
            }
            // If we still have toggle refs on a leftover, force those.
            try
            {
                var t = go.GetComponent<CollectionSetFilterDropdownToggle>();
                if (t == null && src != null)
                {
                    // already stripped — find by name match from src meshes
                    if (src.m_buttonMesh != null)
                    {
                        Transform found = FindChildByName(go.transform, src.m_buttonMesh.name);
                        if (found != null)
                        {
                            var mr = found.GetComponent<MeshRenderer>();
                            if (mr != null)
                            {
                                mr.enabled = true;
                                found.gameObject.SetActive(true);
                            }
                        }
                    }
                    if (src.m_buttonMeshBackground != null)
                    {
                        Transform found = FindChildByName(go.transform, src.m_buttonMeshBackground.name);
                        if (found != null)
                        {
                            var mr = found.GetComponent<MeshRenderer>();
                            if (mr != null)
                            {
                                mr.enabled = true;
                                found.gameObject.SetActive(true);
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                    return all[i];
            }
            return null;
        }

        private static void DumpMeshRenderers(string label, GameObject go)
        {
            if (go == null || !DebugLog.VerboseDebug)
                return;
            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length && i < 12; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null)
                    continue;
                Material mat = mr.sharedMaterial;
                string matName = mat != null ? mat.name : "(null)";
                string shader = mat != null && mat.shader != null ? mat.shader.name : "(null)";
                Color col = Color.clear;
                try
                {
                    if (mat != null && mat.HasProperty("_Color"))
                        col = mat.color;
                }
                catch
                {
                    // ignore
                }
                DebugLog.Msg(
                    $"KeywordFilterUi MR[{label}] '{mr.name}' enabled={mr.enabled} " +
                    $"active={mr.gameObject.activeInHierarchy} isVisible={mr.isVisible} " +
                    $"layer={mr.gameObject.layer} mat={matName} shader={shader} " +
                    $"rgba=({col.r:F2},{col.g:F2},{col.b:F2},{col.a:F2}) " +
                    $"boundsCenter={FormatVec(mr.bounds.center)}");
            }
        }

        private static void SyncChildLocalTransforms(Transform srcRoot, Transform dstRoot)
        {
            if (srcRoot == null || dstRoot == null)
                return;
            Transform[] srcAll = srcRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < srcAll.Length; i++)
            {
                Transform s = srcAll[i];
                if (s == null || s == srcRoot)
                    continue;
                string rel = GetRelativePath(srcRoot, s);
                if (string.IsNullOrEmpty(rel))
                    continue;
                Transform d = dstRoot.Find(rel);
                if (d == null)
                    continue;
                d.localPosition = s.localPosition;
                d.localRotation = s.localRotation;
                d.localScale = s.localScale;
            }
        }

        private static void LogChildLocalRots(string label, Transform root)
        {
            if (root == null)
                return;
            var sb = new System.Text.StringBuilder();
            sb.Append($"KeywordFilterUi childRots[{label}]");
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            int n = 0;
            for (int i = 0; i < all.Length && n < 8; i++)
            {
                Transform t = all[i];
                if (t == null || t == root)
                    continue;
                sb.Append($" | {t.name} euler={FormatVec(t.localEulerAngles)}");
                n++;
            }
            DebugLog.Msg(sb.ToString());
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

        private static void StripSelectSetToggleBehavior(GameObject go)
        {
            // Remove CollectionSetFilterDropdownToggle (PegUI subclass) so it cannot open Sets.
            // Preserve UIBHighlight mouseover targets (MeshOver/Glow) — destroying UIBHighlight
            // was why K had no hover. PegUI is recreated; WireKeywordCorkInteraction re-binds.
            UIBHighlight hl = go.GetComponent<UIBHighlight>();
            GameObject mouseOver = hl != null ? hl.m_MouseOverHighlight : null;
            GameObject mouseDown = hl != null ? hl.m_MouseDownHighlight : null;
            GameObject mouseUp = hl != null ? hl.m_MouseUpHighlight : null;

            CollectionSetFilterDropdownToggle toggle = go.GetComponent<CollectionSetFilterDropdownToggle>();
            if (toggle != null)
                UnityEngine.Object.DestroyImmediate(toggle);

            // Destroying the PegUI subclass may have removed UIBHighlight (RequireComponent).
            PegUIElement existing = go.GetComponent<PegUIElement>();
            if (existing != null && !(existing is CollectionSetFilterDropdownToggle))
            {
                // Already a plain PegUIElement — keep it.
            }
            else if (go.GetComponent<PegUIElement>() == null)
            {
                go.AddComponent<PegUIElement>();
            }

            UIBHighlight hlNow = go.GetComponent<UIBHighlight>();
            if (hlNow == null)
            {
                hlNow = go.AddComponent<UIBHighlight>();
                hlNow.m_MouseOverHighlight = mouseOver;
                hlNow.m_MouseDownHighlight = mouseDown;
                hlNow.m_MouseUpHighlight = mouseUp;
            }
            hlNow.EnableResponse = true;
            hlNow.AlwaysOver = false;
            // Idle: hide highlight objects (Sets-like). Hover will re-enable via UIBHighlight.
            if (hlNow.m_MouseOverHighlight != null)
                hlNow.m_MouseOverHighlight.SetActive(false);
            if (hlNow.m_MouseDownHighlight != null)
                hlNow.m_MouseDownHighlight.SetActive(false);
        }

        /// <summary>
        /// PegUI click (toggle keyword tray) + UIBHighlight hover. Call after positioning
        /// so collider world pose and listeners are valid.
        /// </summary>
        private static void WireKeywordCorkInteraction(GameObject go)
        {
            if (go == null)
                return;

            try
            {
                // Never keep a dying BnetBarFriendButton PegUI — it clears listeners on destroy.
                BnetBarFriendButton strayFriends = go.GetComponent<BnetBarFriendButton>();
                if (strayFriends != null)
                {
                    UnityEngine.Object.DestroyImmediate(strayFriends);
                    DebugLog.Warning("KeywordFilterUi Wire: DestroyImmediate leftover BnetBarFriendButton");
                }

                Collider[] cols = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                        cols[i].enabled = true;
                }

                BoxCollider box = go.GetComponent<BoxCollider>();
                if (box == null)
                    box = go.AddComponent<BoxCollider>();
                box.enabled = true;

                PegUIElement peg = go.GetComponent<PegUIElement>();
                if (peg == null)
                    peg = go.AddComponent<PegUIElement>();

                peg.ClearEventListeners();
                peg.SetEnabled(true, true);

                // Re-bind UIBHighlight to this PegUI (Awake already ran on clone / may be stale).
                UIBHighlight hl = go.GetComponent<UIBHighlight>();
                if (hl != null)
                {
                    hl.EnableResponse = true;
                    hl.AlwaysOver = false;
                    var awake = HarmonyLib.AccessTools.Method(typeof(UIBHighlight), "Awake");
                    awake?.Invoke(hl, null);
                    if (hl.m_MouseOverHighlight != null && hl.m_MouseOverHighlight.activeSelf)
                        hl.m_MouseOverHighlight.SetActive(false);
                }

                peg.AddEventListener(UIEventType.ROLLOVER, OnKeywordCorkRollover);
                peg.AddEventListener(UIEventType.ROLLOUT, OnKeywordCorkRollout);
                FilterButtonFeedback.BindPressSound(peg);
                peg.AddEventListener(UIEventType.RELEASE, OnOpenButtonReleased);
                s_openButton = peg;

                if (s_openUsesKeywordsLabel)
                    ApplyKeywordsLabel(go);

                DebugLog.Msg(
                    $"KeywordFilterUi WireKeywordCorkInteraction: peg=ok enabled={peg.IsEnabled()} " +
                    $"collider={(box != null && box.enabled)} " +
                    $"uibHighlight={(hl != null)} mouseOver={(hl != null && hl.m_MouseOverHighlight != null ? hl.m_MouseOverHighlight.name : "null")} " +
                    $"RELEASE→OnOpenButtonReleased label={OpenLabel()}");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.WireKeywordCorkInteraction failed", ex);
            }
        }

        private static void OnKeywordCorkRollover(UIEvent e)
        {
            DebugLog.Verbose("KeywordFilterUi: K hover enter");
            try
            {
                if (s_openButton == null)
                    return;
                UIBHighlight hl = s_openButton.GetComponent<UIBHighlight>();
                if (hl != null && hl.m_MouseOverHighlight != null)
                {
                    hl.m_MouseOverHighlight.SetActive(true);
                    EnsureRenderersEnabled(hl.m_MouseOverHighlight);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void OnKeywordCorkRollout(UIEvent e)
        {
            DebugLog.Verbose("KeywordFilterUi: K hover exit");
            try
            {
                if (s_openButton == null)
                    return;
                UIBHighlight hl = s_openButton.GetComponent<UIBHighlight>();
                if (hl != null)
                {
                    if (hl.m_MouseOverHighlight != null)
                        hl.m_MouseOverHighlight.SetActive(false);
                    if (hl.m_MouseDownHighlight != null)
                        hl.m_MouseDownHighlight.SetActive(false);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void AlignColliderWithSource(
            GameObject go,
            Vector3 srcLocalCenter,
            Vector3 srcLocalSize,
            bool isTrigger)
        {
            DisableChildColliders(go);
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            box.enabled = true;
            box.isTrigger = isTrigger;
            box.center = srcLocalCenter;
            Vector3 size = srcLocalSize;
            if (Mathf.Abs(size.x) < 0.05f)
                size.x = 0.5f;
            if (Mathf.Abs(size.y) < 0.05f)
                size.y = 0.5f;
            if (Mathf.Abs(size.z) < 0.05f)
                size.z = 0.5f;
            box.size = size;
        }

        /// <summary>Fit root BoxCollider to renderer world bounds in local space (shrink meshColΔ).</summary>
        private static void AlignColliderToMesh(GameObject go, bool isTrigger)
        {
            DisableChildColliders(go);
            Bounds b = GetRendererBounds(go);
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            box.enabled = true;
            box.isTrigger = isTrigger;

            Vector3 localCenter = go.transform.InverseTransformPoint(b.center);
            Vector3 localMin = go.transform.InverseTransformPoint(b.min);
            Vector3 localMax = go.transform.InverseTransformPoint(b.max);
            Vector3 localSize = localMax - localMin;
            box.center = localCenter;
            box.size = new Vector3(
                Mathf.Max(Mathf.Abs(localSize.x), 0.2f),
                Mathf.Max(Mathf.Abs(localSize.y), 0.2f),
                Mathf.Max(Mathf.Abs(localSize.z), 0.2f));
        }

        private static void DisableChildColliders(GameObject go)
        {
            Collider[] all = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject != go)
                    all[i].enabled = false;
            }
        }

        private static UberText EnsureKLabel(GameObject go)
        {
            UberText existing = go.GetComponentInChildren<UberText>(true);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return existing;
            }

            // Prefer a SetFilterItem UberText (CM-native) over Friends HUD text.
            if (s_itemTemplate != null)
            {
                UberText src = s_itemTemplate.GetComponentInChildren<UberText>(true);
                if (src != null)
                {
                    GameObject labelGo = UnityEngine.Object.Instantiate(src.gameObject, go.transform);
                    labelGo.name = "BCM_KLabel";
                    labelGo.SetActive(true);
                    labelGo.transform.localPosition = Vector3.zero;
                    labelGo.transform.localRotation = Quaternion.identity;
                    labelGo.transform.localScale = Vector3.one;
                    return labelGo.GetComponent<UberText>();
                }
            }
            return null;
        }

        /// <summary>
        /// Visible cork-row recipe: match Select Set world pose, bump world Z (+~3.5 / local Z
        /// +~0.93) so K sits in front of tray wood, then slide left using MESH screen X
        /// (not transform screen — mesh is ~26u from transform origin).
        /// </summary>
        private static void PlaceAboveSelectSet(GameObject go, CollectionSetFilterDropdownToggle sourceToggle)
        {
            Transform src = sourceToggle.transform;
            Vector3 srcWorld = src.position;
            Quaternion srcWorldRot = src.rotation;
            Vector3 srcLossy = src.lossyScale;
            Bounds srcBounds = GetRendererBounds(sourceToggle.gameObject);
            Vector3 srcMesh = srcBounds.center;

            Camera uiCam = FindCmUiCamera(go);

            // 1) Exact Select Set world pose (known visible when Z-bumped).
            go.transform.position = srcWorld;
            go.transform.rotation = srcWorldRot;
            ApplyWorldScale(go.transform, srcLossy);
            go.SetActive(true);

            // 2) Front-of-tray bump — same as when K was visible (world Z +3.5).
            const float visibleWorldZBump = 3.5f;
            go.transform.position = new Vector3(srcWorld.x, srcWorld.y, srcWorld.z + visibleWorldZBump);
            go.transform.rotation = srcWorldRot;
            ApplyWorldScale(go.transform, srcLossy);

            Bounds afterBump = GetRendererBounds(go);
            if (afterBump.size.y < srcBounds.size.y * 0.7f)
            {
                // Fallback: local Z bump under current parent if world Z flattened.
                go.transform.position = srcWorld;
                go.transform.rotation = srcWorldRot;
                ApplyWorldScale(go.transform, srcLossy);
                Vector3 lp = go.transform.localPosition;
                go.transform.localPosition = new Vector3(lp.x, lp.y, lp.z + 0.93f);
                go.transform.rotation = srcWorldRot;
                ApplyWorldScale(go.transform, srcLossy);
                DebugLog.Warning("KeywordFilterUi: world Z bump flattened; used local Z +0.93 fallback");
            }

            Vector3 frontPos = go.transform.position;
            string placeNote = "worldZ+3.5";

            // 3) LEFT on cork strip — score by MESH screen X (40–80px left of Select Set mesh).
            float bestScore = float.NegativeInfinity;
            Vector3 bestPos = frontPos;
            bool foundLeft = false;

            if (uiCam != null)
            {
                float srcMeshSx = uiCam.WorldToScreenPoint(srcMesh).x;
                Vector3 camRight = uiCam.transform.right;
                float[] gaps = { 2f, 3.5f, 5f, 7f, 9f, 12f, -2f, -3.5f, -5f };

                for (int i = 0; i < gaps.Length; i++)
                {
                    // Camera-left is -right; also try +right if that yields mesh-left on screen.
                    Vector3 candidate = frontPos - camRight * gaps[i];
                    go.transform.position = candidate;
                    go.transform.rotation = srcWorldRot;
                    ApplyWorldScale(go.transform, srcLossy);

                    Bounds kb = GetRendererBounds(go);
                    if (kb.size.y < srcBounds.size.y * 0.7f)
                        continue;

                    Vector3 kMesh = kb.center;
                    Vector3 kMeshScreen = uiCam.WorldToScreenPoint(kMesh);
                    if (kMeshScreen.z <= 0f)
                        continue;

                    float leftPx = srcMeshSx - kMeshScreen.x;
                    // Prefer 40–80px left; still accept 25–120.
                    float score = leftPx;
                    if (leftPx >= 40f && leftPx <= 80f)
                        score += 1000f;
                    else if (leftPx >= 25f && leftPx <= 120f)
                        score += 500f;
                    else if (leftPx < 15f)
                        continue;

                    bool anyVisible = false;
                    Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < rends.Length; r++)
                    {
                        if (rends[r] != null && rends[r].enabled && rends[r].isVisible)
                        {
                            anyVisible = true;
                            break;
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPos = candidate;
                        foundLeft = true;
                        placeNote = $"worldZ+3.5 camRight*-{gaps[i]:F1} meshLeftPx={leftPx:F0} vis={anyVisible}";
                    }
                }
            }

            if (foundLeft)
                go.transform.position = bestPos;
            else
            {
                // Last resort: stay on visible Z-bump pose (coincident front of Sets).
                go.transform.position = frontPos;
                placeNote = "worldZ+3.5 (no left candidate; coincident front)";
            }

            go.transform.rotation = srcWorldRot;
            ApplyWorldScale(go.transform, srcLossy);
            go.transform.SetAsLastSibling();

            LogMeshVisibility("after place", go, sourceToggle.gameObject, uiCam);
            DebugLog.Msg(
                $"KeywordFilterUi place {placeNote} " +
                $"src world={FormatVec(srcWorld)} K world={FormatVec(go.transform.position)} " +
                $"K local={FormatVec(go.transform.localPosition)} " +
                $"path={GetHierarchyPath(go.transform)}");
        }

        private static void LogMeshVisibility(
            string context,
            GameObject k,
            GameObject src,
            Camera uiCam)
        {
            if (!DebugLog.VerboseDebug)
                return;
            Bounds kb = GetCorkRendererBounds(k);
            Bounds sb = src != null ? GetCorkRendererBounds(src) : new Bounds();
            Vector3 kMesh = kb.center;
            Vector3 sMesh = sb.center;
            BoxCollider box = k != null ? k.GetComponent<BoxCollider>() : null;
            Vector3 colCenter = box != null && k != null
                ? k.transform.TransformPoint(box.center)
                : Vector3.zero;
            float meshColK = MeshColDelta(k);
            float meshColSrc = MeshColDelta(src);

            bool anyEnabled = false;
            bool anyVisible = AnyRendererVisible(k);
            if (k != null)
            {
                Renderer[] rends = k.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    if (rends[i] != null && rends[i].enabled)
                        anyEnabled = true;
                }
            }

            DebugLog.Msg(
                $"KeywordFilterUi meshVis ({context}): " +
                $"transformScreen={FormatScreen(uiCam, k != null ? k.transform.position : Vector3.zero)} " +
                $"meshScreen={FormatScreen(uiCam, kMesh)} " +
                $"srcMeshScreen={FormatScreen(uiCam, sMesh)} " +
                $"boundsK={FormatVec(kb.size)} boundsSrc={FormatVec(sb.size)} " +
                $"meshCenter={FormatVec(kMesh)} colCenter={FormatVec(colCenter)} " +
                $"meshColΔK={meshColK:F3} meshColΔSrc={meshColSrc:F3} " +
                $"rendererEnabled={anyEnabled} isVisible={anyVisible}");
        }

        /// <summary>
        /// If K bounds are &gt;~2× source Select Set, pull localScale back toward source — never abort to nothing.
        /// </summary>
        private static void ClampScaleTowardSource(GameObject go, CollectionSetFilterDropdownToggle sourceToggle)
        {
            if (go == null || sourceToggle == null)
                return;

            Bounds srcB = GetRendererBounds(sourceToggle.gameObject);
            float srcMax = Mathf.Max(srcB.size.x, Mathf.Max(srcB.size.y, srcB.size.z));
            if (srcMax < 0.01f)
                return;

            Vector3 targetScale = SanitizeLocalScale(sourceToggle.transform.localScale);
            for (int i = 0; i < 6; i++)
            {
                Bounds kb = GetRendererBounds(go);
                float kMax = Mathf.Max(kb.size.x, Mathf.Max(kb.size.y, kb.size.z));
                if (kMax <= srcMax * 2.2f)
                    break;

                go.transform.localScale = Vector3.Lerp(go.transform.localScale, targetScale, 0.55f);
                go.transform.localScale = SanitizeLocalScale(go.transform.localScale);
                DebugLog.Warning(
                    $"KeywordFilterUi: K bounds {FormatVec(kb.size)} > 2× src {FormatVec(srcB.size)}; " +
                    $"scaling toward src → localScale={FormatVec(go.transform.localScale)}");
            }
        }

        private static Camera FindCmUiCamera(GameObject go)
        {
            try
            {
                if (go != null)
                {
                    Camera proj = CameraUtils.FindProjectionCameraForObject(go);
                    if (proj != null)
                        return proj;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                Box box = Box.Get();
                if (box != null)
                {
                    Camera c = box.GetCamera();
                    if (c != null)
                        return c;
                }
            }
            catch
            {
                // ignore
            }

            if (go != null)
            {
                Camera layerCam = FindCameraForLayer(go.layer);
                if (layerCam != null)
                    return layerCam;
            }

            return CameraUtils.GetMainCamera() ?? Camera.main;
        }

        private static Bounds GetRendererBounds(GameObject go)
        {
            Renderer[] rends = go != null ? go.GetComponentsInChildren<Renderer>(true) : null;
            if (rends == null || rends.Length == 0)
                return new Bounds(go != null ? go.transform.position : Vector3.zero, Vector3.zero);

            bool any = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null || !rends[i].enabled)
                    continue;
                if (!any)
                {
                    b = rends[i].bounds;
                    any = true;
                }
                else
                {
                    b.Encapsulate(rends[i].bounds);
                }
            }
            return b;
        }

        private static bool IsBoundsScreenWide(GameObject go, Camera cam)
        {
            if (go == null || cam == null || Screen.width <= 0)
                return false;
            Bounds b = GetRendererBounds(go);
            Vector3 min = cam.WorldToScreenPoint(b.min);
            Vector3 max = cam.WorldToScreenPoint(b.max);
            float screenW = Mathf.Abs(max.x - min.x);
            float fraction = screenW / Screen.width;
            if (fraction > MaxBoundsScreenWidthFraction)
            {
                DebugLog.Warning(
                    $"KeywordFilterUi: bounds screen width fraction={fraction:F2} " +
                    $"(px={screenW:F0}/{Screen.width}) boundsSize={FormatVec(b.size)} — too wide");
                return true;
            }
            return false;
        }

        private static void LogBoundsCompare(GameObject k, GameObject sourceToggle, Camera cam)
        {
            Bounds kb = GetRendererBounds(k);
            string src = "(none)";
            if (sourceToggle != null)
            {
                Bounds sb = GetRendererBounds(sourceToggle);
                src = FormatVec(sb.size);
            }
            float frac = 0f;
            if (cam != null && Screen.width > 0)
            {
                Vector3 min = cam.WorldToScreenPoint(kb.min);
                Vector3 max = cam.WorldToScreenPoint(kb.max);
                frac = Mathf.Abs(max.x - min.x) / Screen.width;
            }
            DebugLog.Msg(
                $"KeywordFilterUi bounds K={FormatVec(kb.size)} srcToggle={src} " +
                $"screenWidthFraction={frac:F3} (must be << 0.5)");
        }

        private static Vector3 SanitizeLocalScale(Vector3 scale)
        {
            float x = Mathf.Abs(scale.x) < 0.01f ? 1f : scale.x;
            float y = Mathf.Abs(scale.y) < 0.01f ? 1f : scale.y;
            float z = Mathf.Abs(scale.z) < 0.01f ? 1f : scale.z;
            return new Vector3(x, y, z);
        }

        private static float EstimateFriendsScreenHeight(BnetBarFriendButton friends, Camera cam)
        {
            if (cam == null)
                return 25f;
            Renderer rend = friends.GetComponentInChildren<Renderer>();
            if (rend == null)
                return 25f;
            Bounds b = rend.bounds;
            Vector3 s0 = cam.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, b.min.z));
            Vector3 s1 = cam.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, b.max.z));
            float h = Mathf.Abs(s1.y - s0.y);
            return Mathf.Clamp(h, 12f, 80f);
        }

        private static float MeasureScreenYDelta(Camera cam, Vector3 friendsWorld, Vector3 kWorld)
        {
            if (cam == null)
                return kWorld.y - friendsWorld.y;
            float fy = cam.WorldToScreenPoint(friendsWorld).y;
            float ky = cam.WorldToScreenPoint(kWorld).y;
            return ky - fy;
        }

        private static void EnsureClickCollider(GameObject go)
        {
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null)
                box = go.AddComponent<BoxCollider>();
            box.enabled = true;

            Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends != null && rends.Length > 0)
            {
                bool any = false;
                Bounds b = new Bounds(go.transform.position, Vector3.zero);
                for (int i = 0; i < rends.Length; i++)
                {
                    if (rends[i] == null || !rends[i].enabled || !rends[i].gameObject.activeInHierarchy)
                        continue;
                    if (!any)
                    {
                        b = rends[i].bounds;
                        any = true;
                    }
                    else
                    {
                        b.Encapsulate(rends[i].bounds);
                    }
                }

                if (any)
                {
                    Vector3 localCenter = go.transform.InverseTransformPoint(b.center);
                    Vector3 worldSize = b.size;
                    Vector3 localSize = go.transform.InverseTransformVector(worldSize);
                    box.center = localCenter;
                    box.size = new Vector3(
                        Mathf.Max(Mathf.Abs(localSize.x), 0.35f),
                        Mathf.Max(Mathf.Abs(localSize.y), 0.35f),
                        Mathf.Max(Mathf.Abs(localSize.z), 0.2f));
                }
            }

            // Enable all colliders under the chip.
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = true;
            }
        }

        private static void BuildPanel(CollectionSetFilterDropdownToggle toggle)
        {
            s_panel = new GameObject(PanelName);
            // Compact panel in CM Select Set space (same host as K) — not BnetBar.
            Transform host = s_root != null
                ? s_root.transform
                : (toggle != null && toggle.transform.parent != null
                    ? toggle.transform.parent
                    : toggle.transform);
            GameUtils.SetParent(s_panel, host.gameObject, true);
            s_panel.transform.localScale = Vector3.one;
            s_panel.transform.localRotation = Quaternion.identity;
            if (toggle != null)
                SetLayerRecursive(s_panel, toggle.gameObject.layer);

            Vector3 anchor = s_openButton != null
                ? s_openButton.transform.position
                : toggle.transform.position;
            // Near K: slight +X and +world Z — compact list, not a screen-wide backdrop.
            s_panel.transform.position = new Vector3(anchor.x + 0.8f, anchor.y, anchor.z + 1.2f);

            GameObject title = InstantiateFilterItem(s_itemTemplate, s_panel.transform, "Title");
            PlaceLocal(title, new Vector3(0.2f, 0.2f, 0f), 1f);
            SetFilterItem titleItem = title.GetComponent<SetFilterItem>();
            if (titleItem != null)
            {
                titleItem.IsHeader = true;
                titleItem.Text = "Keyword";
                titleItem.SetSelected(false);
            }
            DisablePeg(title);
            RefreshUberText(title);

            GameObject hasHdr = InstantiateFilterItem(s_itemTemplate, s_panel.transform, "HasHdr");
            PlaceLocal(hasHdr, new Vector3(1.7f, -0.1f, 0f), 0.55f);
            SetFilterItem hasHdrItem = hasHdr.GetComponent<SetFilterItem>();
            if (hasHdrItem != null)
            {
                hasHdrItem.IsHeader = true;
                hasHdrItem.Text = "Has";
            }
            DisablePeg(hasHdr);

            GameObject refHdr = InstantiateFilterItem(s_itemTemplate, s_panel.transform, "RefHdr");
            PlaceLocal(refHdr, new Vector3(2.5f, -0.1f, 0f), 0.55f);
            SetFilterItem refHdrItem = refHdr.GetComponent<SetFilterItem>();
            if (refHdrItem != null)
            {
                refHdrItem.IsHeader = true;
                refHdrItem.Text = "Ref";
            }
            DisablePeg(refHdr);

            GameObject viewport = new GameObject("BCM_Viewport");
            viewport.transform.SetParent(s_panel.transform, false);
            viewport.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            viewport.transform.localRotation = Quaternion.identity;
            viewport.transform.localScale = Vector3.one;

            GameObject content = new GameObject("BCM_ScrollContent");
            content.transform.SetParent(viewport.transform, false);
            content.transform.localPosition = Vector3.zero;
            content.transform.localRotation = Quaternion.identity;
            content.transform.localScale = Vector3.one;
            s_scrollContent = content.transform;
            s_scrollY = 0f;

            GameObject clearGo = InstantiateFilterItem(s_itemTemplate, s_panel.transform, "Clear");
            PlaceLocal(clearGo, new Vector3(0.1f, -3.7f, 0f), 0.75f);
            SetFilterItem clearItem = clearGo.GetComponent<SetFilterItem>();
            if (clearItem != null)
                clearItem.Text = "Clear";
            EnableClickable(clearGo);
            clearGo.GetComponent<PegUIElement>()?.AddEventListener(UIEventType.RELEASE, OnClearReleased);

            GameObject doneGo = InstantiateFilterItem(s_itemTemplate, s_panel.transform, "Done");
            PlaceLocal(doneGo, new Vector3(1.6f, -3.7f, 0f), 0.75f);
            SetFilterItem doneItem = doneGo.GetComponent<SetFilterItem>();
            if (doneItem != null)
                doneItem.Text = "Done";
            EnableClickable(doneGo);
            doneGo.GetComponent<PegUIElement>()?.AddEventListener(UIEventType.RELEASE, OnDoneReleased);

            FillKeywordRows();
            SetPanelShown(false);

            DebugLog.Msg($"KeywordFilterUi panel local={FormatVec(s_panel.transform.localPosition)} world={FormatVec(s_panel.transform.position)}");
        }

        private static void RebuildPanelContents()
        {
            if (s_panel == null || s_scrollContent == null)
                return;
            FillKeywordRows();
        }

        private static void FillKeywordRows()
        {
            ClearRows();
            IReadOnlyList<KeywordCatalog.Entry> entries = KeywordCatalog.GetEntries();
            KeywordFilterState.PruneToAvailable(entries);

            for (int i = 0; i < entries.Count; i++)
                CreateKeywordRow(entries[i], -i * RowHeight);

            float contentHeight = entries.Count * RowHeight;
            float viewHeight = VisibleRows * RowHeight;
            s_scrollMinY = Mathf.Min(0f, viewHeight - contentHeight);
            s_scrollY = 0f;
            if (s_scrollContent != null)
                s_scrollContent.localPosition = Vector3.zero;

            DebugLog.Msg($"KeywordFilterUi list rows={entries.Count}; {KeywordFilterState.Describe()}");
        }

        private static void CreateKeywordRow(KeywordCatalog.Entry entry, float y)
        {
            GameObject row = new GameObject("KW_" + ((int)entry.Tag));
            row.transform.SetParent(s_scrollContent, false);
            row.transform.localPosition = new Vector3(0f, y, 0f);
            row.transform.localRotation = Quaternion.identity;
            row.transform.localScale = Vector3.one;
            SetLayerRecursive(row, s_scrollContent.gameObject.layer);
            row.SetActive(true);
            s_rowObjects.Add(row);

            GameObject nameGo = InstantiateFilterItem(s_itemTemplate, row.transform, entry.Name);
            PlaceLocal(nameGo, new Vector3(0.15f, 0f, 0f), 0.9f);
            SetFilterItem nameItem = nameGo.GetComponent<SetFilterItem>();
            if (nameItem != null)
            {
                nameItem.Text = entry.Name;
                nameItem.SetSelected(KeywordFilterState.IsSelected(entry.Tag));
            }
            RefreshUberText(nameGo);
            EnableClickable(nameGo);
            GAME_TAG tag = entry.Tag;
            nameGo.GetComponent<PegUIElement>()?.AddEventListener(
                UIEventType.RELEASE, (UIEvent e) => OnKeywordToggle(tag, nameItem));
        }

        private static GameObject InstantiateFilterItem(GameObject prefab, Transform parent, string name)
        {
            if (prefab == null)
                throw new ArgumentException("KeywordFilterUi.InstantiateFilterItem: template is null (name=" + name + ")");

            GameObject go = UnityEngine.Object.Instantiate(prefab);
            go.name = name;
            Vector3 savedScale = go.transform.localScale;
            Quaternion savedRot = go.transform.localRotation;
            if (parent != null)
                go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = savedScale.sqrMagnitude > 0.0001f ? savedScale : Vector3.one;
            go.transform.localRotation = savedRot;
            if (parent != null)
                SetLayerRecursive(go, parent.gameObject.layer);
            go.SetActive(true);

            SetFilterItem item = go.GetComponent<SetFilterItem>();
            if (item != null)
            {
                item.IsHeader = false;
                item.Callback = null;
                item.ShowTooltip = false;
                StripExtras(item);
            }
            return go;
        }

        private static SetFilterItem FindSampleItem(SetFilterTray tray)
        {
            try
            {
                List<SetFilterItem> items = s_items != null ? s_items(tray) : null;
                if (items == null)
                    return null;
                for (int i = 0; i < items.Count; i++)
                {
                    SetFilterItem it = items[i];
                    if (it != null && !it.IsHeader)
                        return it;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static void ApplyWorldScale(Transform t, Vector3 worldScale)
        {
            if (t == null)
                return;
            // Avoid axis collapse under rotated parents (previously produced localScale.x≈0).
            Vector3 safeWorld = SanitizeLocalScale(worldScale);
            if (t.parent == null)
            {
                t.localScale = safeWorld;
                return;
            }
            Vector3 p = t.parent.lossyScale;
            float px = Mathf.Abs(p.x) < 0.0001f ? 1f : p.x;
            float py = Mathf.Abs(p.y) < 0.0001f ? 1f : p.y;
            float pz = Mathf.Abs(p.z) < 0.0001f ? 1f : p.z;
            t.localScale = SanitizeLocalScale(new Vector3(safeWorld.x / px, safeWorld.y / py, safeWorld.z / pz));
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null)
                return;
            Transform[] transforms = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null)
                    transforms[i].gameObject.layer = layer;
            }
        }

        private static void StripExtras(SetFilterItem item)
        {
            if (item == null)
                return;
            try
            {
                item.Callback = null;
                item.ShowTooltip = false;
            }
            catch
            {
                // ignore
            }
        }

        private static void PlaceLocal(GameObject go, Vector3 localPos, float scale)
        {
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
        }

        private static void DisablePeg(GameObject go)
        {
            PegUIElement peg = go.GetComponent<PegUIElement>();
            peg?.SetEnabled(false, true);
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                col.enabled = false;
        }

        private static void EnableClickable(GameObject go)
        {
            PegUIElement peg = go.GetComponent<PegUIElement>();
            peg?.SetEnabled(true, true);
            FilterButtonFeedback.BindPressSound(peg);
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                col.enabled = true;
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box != null && box.size.sqrMagnitude < 0.01f)
                box.size = new Vector3(1.2f, 0.4f, 0.25f);
        }

        private static void RefreshUberText(GameObject go)
        {
            try
            {
                UberText uber = go.GetComponentInChildren<UberText>(true);
                uber?.UpdateNow(true);
            }
            catch
            {
                // ignore
            }
        }

        private static void EnsureRenderersEnabled(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                renderers[i].enabled = true;
                MeshRenderer mr = renderers[i] as MeshRenderer;
                if (mr != null)
                    mr.enabled = true;
            }
        }

        private static Camera FindCameraForLayer(int layer)
        {
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
            return Camera.main;
        }

        private static string OpenLabel()
        {
            // Display "Keyword" (singular); kind id remains Keywords internally.
            if (s_openUsesKeywordsLabel)
                return KeywordFilterState.IsActive ? "Keyword*" : "Keyword";
            return KeywordFilterState.IsActive ? "K*" : "K";
        }

        private static void ClearRows()
        {
            for (int i = 0; i < s_rowObjects.Count; i++)
            {
                if (s_rowObjects[i] != null)
                    UnityEngine.Object.Destroy(s_rowObjects[i]);
            }
            s_rowObjects.Clear();
        }

        private static void LogFriendsAnchor(BnetBarFriendButton friends, Camera bnetCam = null)
        {
            Transform t = friends.transform;
            Camera cam = bnetCam ?? FindBnetCamera(friends);
            DebugLog.Msg(
                $"KeywordFilterUi friends anchor world={FormatVec(t.position)} local={FormatVec(t.localPosition)} " +
                $"lossyScale={FormatVec(t.lossyScale)} euler={FormatVec(t.eulerAngles)} layer={friends.gameObject.layer} " +
                $"bnetScreen={FormatScreen(cam, t.position)} path={GetHierarchyPath(t)}");
        }

        /// <summary>
        /// Log K vs Friends with correct cameras — never WorldToScreen Friends through BoxCamera.
        /// </summary>
        private static void LogPlacementAboveFriends(
            BnetBarFriendButton friends,
            GameObject go,
            Camera boxCam,
            Camera bnetCam,
            Rect friendsScr,
            Rect kFullScr,
            Rect setsFullScr)
        {
            Transform ft = friends.transform;
            Transform kt = go.transform;
            bool scaleOk =
                Mathf.Abs(kt.localScale.x) > 0.01f
                && Mathf.Abs(kt.localScale.y) > 0.01f
                && Mathf.Abs(kt.localScale.z) > 0.01f;
            float gapPx = kFullScr.yMin - friendsScr.yMax;
            DebugLog.Msg(
                $"KeywordFilterUi ABOVE via boxCamScreenToWorld " +
                $"scaleOk={scaleOk} prefab=CollectionSetFilterDropdownToggle " +
                $"friends world={FormatVec(ft.position)} layer={friends.gameObject.layer} " +
                $"K world={FormatVec(kt.position)} layer={go.layer} parent={kt.parent?.name} " +
                $"euler={FormatVec(kt.eulerAngles)} localScale={FormatVec(kt.localScale)} " +
                $"lossyScale={FormatVec(kt.lossyScale)} label={OpenLabel()} " +
                $"activeInHierarchy={go.activeInHierarchy}");
            DebugLog.Msg(
                $"KeywordFilterUi screenPoints friends(bnet)={FormatScreen(bnetCam, ft.position)} " +
                $"FriendsAABB=[{friendsScr.xMin:F0}..{friendsScr.xMax:F0}],y=[{friendsScr.yMin:F0}..{friendsScr.yMax:F0}] " +
                $"K(box)={FormatScreen(boxCam, kt.position)} " +
                $"Kfull={kFullScr.width:F0}x{kFullScr.height:F0} SetsFull={setsFullScr.width:F0}x{setsFullScr.height:F0} " +
                $"gapPx={gapPx:F1} (no cross-camera screenΔy)");
        }

        private static string FormatScreen(Camera cam, Vector3 world)
        {
            if (cam == null)
                return "(no-cam)";
            Vector3 s = cam.WorldToScreenPoint(world);
            bool onScreen = s.z > 0f && s.x >= 0f && s.x <= Screen.width && s.y >= 0f && s.y <= Screen.height;
            return $"({s.x:F0},{s.y:F0},z={s.z:F1},onScreen={onScreen})";
        }

        private static void OnOpenButtonReleased(UIEvent e)
        {
            OnFilterPlaqueReleased(FilterTrayKind.Keywords, "pegui");
        }

        private static void OnDoneReleased(UIEvent e)
        {
            KeywordSetTray.SetShown(false);
            s_panelShown = false;
        }

        private static void OnClearReleased(UIEvent e)
        {
            KeywordFilterState.Clear();
            SyncToggleVisuals();
            UpdateOpenButtonLabel();
            KeywordFilterApplier.RefreshCollectionPages();
        }

        private static void OnKeywordToggle(GAME_TAG tag, SetFilterItem item)
        {
            bool next = !KeywordFilterState.IsSelected(tag);
            KeywordFilterState.SetSelected(tag, next);
            item?.SetSelected(next);
            UpdateOpenButtonLabel();
            DebugLog.Msg($"Keyword toggle {tag}={next}; {KeywordFilterState.Describe()}");
            KeywordFilterApplier.RefreshCollectionPages();
        }

        private static void SyncToggleVisuals()
        {
            for (int i = 0; i < s_rowObjects.Count; i++)
            {
                GameObject row = s_rowObjects[i];
                if (row == null || !row.name.StartsWith("KW_", StringComparison.Ordinal))
                    continue;
                if (!int.TryParse(row.name.Substring(3), out int tagInt))
                    continue;
                GAME_TAG tag = (GAME_TAG)tagInt;

                SetFilterItem[] items = row.GetComponentsInChildren<SetFilterItem>(true);
                for (int j = 0; j < items.Length; j++)
                {
                    SetFilterItem it = items[j];
                    if (it == null)
                        continue;
                    it.SetSelected(KeywordFilterState.IsSelected(tag));
                }
            }
        }

        private static void UpdateOpenButtonLabel()
        {
            SetOpenChipLabel(OpenLabel());
        }

        private static void SetOpenChipLabel(string text)
        {
            try
            {
                if (s_openLabelText == null && s_openButton != null)
                    s_openLabelText = s_openButton.GetComponentInChildren<UberText>(true);
                if (s_openLabelText == null)
                    return;
                s_openLabelText.Text = text;
                s_openLabelText.UpdateNow(true);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.SetOpenChipLabel failed", ex);
            }
        }

        private static void SetPanelShown(bool shown)
        {
            KeywordSetTray.SetShown(shown);
            s_panelShown = KeywordSetTray.IsShown;
            if (s_openButton != null && s_openButton.gameObject != null)
                s_openButton.gameObject.SetActive(true);
            if (s_root != null)
                s_root.SetActive(true);
        }

        private static void ForceOpenChipLabelVisible(GameObject go)
        {
            try
            {
                if (s_openLabelText == null)
                    s_openLabelText = go.GetComponentInChildren<UberText>(true);
                if (s_openLabelText == null && s_itemTemplate != null)
                {
                    UberText src = s_itemTemplate.GetComponentInChildren<UberText>(true);
                    if (src != null)
                    {
                        GameObject labelGo = UnityEngine.Object.Instantiate(src.gameObject, go.transform);
                        labelGo.name = "BCM_KLabel";
                        labelGo.SetActive(true);
                        labelGo.transform.localPosition = Vector3.zero;
                        labelGo.transform.localRotation = Quaternion.identity;
                        labelGo.transform.localScale = Vector3.one * 1.2f;
                        s_openLabelText = labelGo.GetComponent<UberText>();
                    }
                }
                if (s_openLabelText == null)
                    return;
                s_openLabelText.gameObject.SetActive(true);
                s_openLabelText.Text = OpenLabel();
                s_openLabelText.UpdateNow(true);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.ForceOpenChipLabelVisible failed", ex);
            }
        }

        private static void LogVisibility(string context)
        {
            try
            {
                string buttonPath = s_openButton != null ? GetHierarchyPath(s_openButton.transform) : "(null)";
                string buttonPos = s_openButton != null
                    ? $"world={FormatVec(s_openButton.transform.position)} local={FormatVec(s_openButton.transform.localPosition)} lossyScale={FormatVec(s_openButton.transform.lossyScale)}"
                    : "n/a";
                DebugLog.Msg(
                    $"KeywordFilterUi visibility ({context}): button[{buttonPos}] path={buttonPath} " +
                    $"panelActive={(s_panel != null && s_panel.activeSelf)} rows={s_rowObjects.Count}");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.LogVisibility failed", ex);
            }
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null)
                return "(null)";
            var parts = new List<string>();
            Transform cur = t;
            int guard = 0;
            while (cur != null && guard++ < 32)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string FormatVec(Vector3 v)
        {
            return $"({v.x:F2},{v.y:F2},{v.z:F2})";
        }

        private static void DestroyUi()
        {
            CollectionFilterMenu.Detach();
            s_built = false;
            // Do not clear s_cmSessionActive here — leave/enter owns that; SoftRetry DestroyUi must not abort the session.
            s_panelShown = false;
            s_scrollContent = null;
            ClearPhysicsHover();
            ClearRows();

            // Restore mana/search/Sets container before tearing down K (leave may also restore).
            RestoreBottomBarLayout("DestroyUi");

            DestroyExtraPlaques();
            if (s_openButton != null && s_openButton.gameObject != null)
                UnityEngine.Object.Destroy(s_openButton.gameObject);
            s_openButton = null;
            s_openLabelText = null;
            s_openUsesKeywordsLabel = false;
            DestroyCorkPivot();

            if (s_panel != null)
            {
                UnityEngine.Object.Destroy(s_panel);
                s_panel = null;
            }

            KeywordSetTray.Destroy();

            if (s_root != null)
            {
                UnityEngine.Object.Destroy(s_root);
                s_root = null;
            }

            s_setFilterContainer = null;
            s_manaTabManager = null;
            s_searchBar = null;

            // Sweep leftovers from earlier attempts.
            DestroyNamedLeftovers(OpenButtonName);
            DestroyNamedLeftovers(PanelName);
            DestroyNamedLeftovers(RootName);
            DestroyNamedLeftovers(CorkPivotName);
            DestroyNamedLeftovers(PlaqueDebugHomeName);
            DestroyNamedLeftovers("BCM_KeywordFriendsFrame");
            DestroyNamedLeftovers("BCM_KeywordSetTray");
            DestroyNamedLeftovers("BCM_KeywordTrayObject");
        }

        private static void DestroyNamedLeftovers(string name)
        {
            try
            {
                // Include inactive — prior broken chips may be disabled.
                Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null || t.name != name)
                        continue;
                    // Skip assets / prefabs not in a scene.
                    if (t.gameObject.scene.name == null || t.gameObject.scene.name == "")
                        continue;
                    UnityEngine.Object.Destroy(t.gameObject);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("KeywordFilterUi.DestroyNamedLeftovers failed for " + name, ex);
            }
        }
    }
}
