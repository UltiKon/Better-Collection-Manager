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

        private static object s_buildCoroutine;
        private static GameObject s_root;
        private static GameObject s_panel;
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
        internal static bool IsCollectionSessionActive => s_cmSessionActive;
        private static GameObject s_itemTemplate;

        // Physics input bypass — PegUI often never delivers RELEASE on BnetBar plaques.
        private static GameObject s_physicsHoverGo;

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

        public static void OnCollectionManagerLeave()
        {
            ComboCollectionCards.Reset();
            s_cmSessionActive = false;
            StopBuildCoroutine();
            KeywordFilterState.Clear();
            ExtraFilterState.ClearAll();
            ExpansionFilterState.Clear();
            KeywordCatalog.Invalidate();
            DestroyUi();
            s_buildAborted = false;
            s_itemTemplate = null;
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

        private static void ClearPhysicsHover()
        {
            SetPlaqueHoverVisual(s_physicsHoverGo, false);
            s_physicsHoverGo = null;
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

                DestroyUi();

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
                        return s_itemTemplate;
                    }
                }

                SetFilterItem sample = FindSampleItem(tray);
                if (sample != null && sample.gameObject != null)
                {
                    s_itemTemplate = sample.gameObject;
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

        private static Rect GetScreenRect(Camera cam, GameObject go, bool corkOnly)
        {
            if (cam == null || go == null)
                return default;
            Bounds b = corkOnly ? GetCorkRendererBounds(go) : GetRendererBounds(go);
            return WorldBoundsToScreenRect(cam, b);
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

        private static void OnOpenButtonReleased(UIEvent e)
        {
            OnFilterPlaqueReleased(FilterTrayKind.Keywords, "pegui");
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

        private static void DestroyUi()
        {
            CollectionFilterMenu.Detach();
            s_built = false;
            // Do not clear s_cmSessionActive here — leave/enter owns that; SoftRetry DestroyUi must not abort the session.
            s_panelShown = false;
            ClearPhysicsHover();
            ClearRows();


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
