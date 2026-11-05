using System;
using System.Collections.Generic;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

// The grid adapter runs against a small native-tray fixture. Scroll registration
// records every item; geometry tests do not implement the production grid math.
public sealed class SetFilterItem : Component
{
    public delegate void ItemSelectedCallback(List<TAG_CARD_SET> cardSets, List<int> specificCards,
        PegasusShared.FormatType format, SetFilterItem item, bool transitionPage);
    public float Height;
    public bool IsHeader, ShowTooltip, EditModeOnly, IsAllStandard;
    public GameObject m_pressedShadow, m_selectedGlow, m_mouseOverGlow, m_iconFX;
    public Renderer m_icon;
    public object TrialCardPeriod;
    public UberText m_uberText;
    public string Text;
    public PegasusShared.FormatType FormatType;
    public List<TAG_CARD_SET> CardSets;
    public List<int> SpecificCards { get; set; }
    public Texture IconTexture;
    public Vector2? IconOffset;
    public ItemSelectedCallback Callback;
    public bool m_isSelected;
    public bool Selected { get => m_isSelected; set => m_isSelected = value; }
    public void SetSelected(bool value)
    {
        m_isSelected = value;
        m_selectedGlow?.SetActive(value);
        m_pressedShadow?.SetActive(value);
    }
    // Native SetFilterItem overrides PegUI's press/release/hover callbacks;
    // it needs no UIBHighlight component or synthetic transform movement.
    public int NativePressCalls, NativeReleaseCalls, NativeHoverCalls;
    public void NativeFeedback(UIEventType type)
    {
        if (type == UIEventType.PRESS) { NativePressCalls++; m_pressedShadow?.SetActive(true); }
        if (type == UIEventType.RELEASE) { NativeReleaseCalls++; if (!Selected) m_pressedShadow?.SetActive(false); }
        if (type == UIEventType.ROLLOVER) { NativeHoverCalls++; m_mouseOverGlow?.SetActive(true); }
        if (type == UIEventType.ROLLOUT)
        {
            m_mouseOverGlow?.SetActive(false);
            if (!Selected) m_pressedShadow?.SetActive(false);
        }
    }
}
public enum TAG_CARD_SET { CORE, TEST_EXPANSION, SPECIAL_EXPANSION }
public sealed class UIBScrollableItem : Component
{
    public enum ActiveState { Active }
    public ActiveState m_active;
    public Vector3 m_size;
}
public sealed class UIBScrollable : Behaviour
{
    public enum ScrollDirection { X, Y, Z }
    public ScrollDirection m_ScrollPlane = ScrollDirection.Z;
    public GameObject m_scrollTrackCover;
    public BoxCollider m_ScrollBounds, m_ScrollTrack;
    public ScrollBarThumb m_ScrollThumb;
    public bool InputBlocked, Paused;
    public GameObject ScrollObject;
    public float ScrollValue, ImmediateScrollValue;
    public float m_ScrollBottomPadding;
    public void SetScrollImmediate(float value)
    {
        ScrollValue = ImmediateScrollValue = Mathf.Clamp01(value);
        UpdateScroll();
    }
    // Native UIBScrollable's drag path accepts fractions, uses its configured
    // rail collider, and drives ScrollBarThumb directly, independent of meshes.
    private void StartDragging()
    {
        if (!InputBlocked && !Paused && enabled) m_ScrollThumb.StartDragging();
    }
    private void DragThumb()
    {
        if (!FilterTrayGeometry.TryProjectToPlane(m_ScrollTrack.transform, CameraUtils.Camera,
            Input.mousePosition, m_ScrollTrack.center.y - m_ScrollTrack.size.y * 0.5f, out Vector3 point)) return;
        Bounds travel = m_ScrollTrack.bounds;
        float worldZ = m_ScrollTrack.transform.TransformPoint(point).z;
        SetScrollImmediate((travel.max.z - worldZ) / travel.size.z);
    }
    public bool EnableIfNeeded()
    {
        float height = ScrollObject.GetComponent<UIBScrollableItem>()?.m_size.z
            ?? Registered.Sum(row => row.GetComponent<UIBScrollableItem>()?.m_size.z ?? 0f);
        bool needed=height + m_ScrollBottomPadding - m_ScrollBounds.bounds.size.z > .16f;
        if (m_ScrollThumb != null) m_ScrollThumb.gameObject.SetActive(needed);
        if (m_scrollTrackCover != null) m_scrollTrackCover.SetActive(!needed);
        return needed;
    }
    public Action OnUpdateScroll;
    public void UpdateScroll() => OnUpdateScroll?.Invoke();
    public bool m_ForceScrollAreaHitTest, m_UseScrollContentsInHitTest, m_OverridePegUI;
    public delegate void VisibleAffected(GameObject obj, bool visible);
    private readonly Dictionary<GameObject, VisibleAffected> m_callbacks = new Dictionary<GameObject, VisibleAffected>();
    public readonly List<GameObject> Registered = new List<GameObject>();
    public readonly Dictionary<GameObject, Vector3> RegisteredSizes = new Dictionary<GameObject, Vector3>();
    public int Resets;
    public float m_VisibleObjectThreshold;
    public void ClearVisibleAffectObjects() { Registered.Clear(); RegisteredSizes.Clear(); m_callbacks.Clear(); }
    public void ForceVisibleAffectedObjectsShow(bool show) { }
    // Model the native visibility callback even when the scroller component
    // is disabled: an outstanding tween can invoke it during the tray slide.
    public void UpdateAndFireVisibleAffectedObjects()
    {
        Bounds viewport = m_ScrollBounds.enabled && m_ScrollBounds.gameObject.activeInHierarchy
            ? m_ScrollBounds.bounds : default;
        foreach (GameObject row in Registered)
        {
            float extent = RegisteredSizes[row].z;
            float z = row.transform.position.z;
            bool visible = z + extent >= viewport.min.z - m_VisibleObjectThreshold
                && z - extent <= viewport.max.z + m_VisibleObjectThreshold;
            if (m_callbacks[row] != null) m_callbacks[row](row, visible); else row.SetActive(visible);
        }
    }
    public void AddVisibleAffectedObject(GameObject go, Vector3 size, bool include, VisibleAffected callback)
    {
        Registered.Add(go);
        RegisteredSizes[go] = size; m_callbacks[go] = callback;
    }
    public Action OnResetScrollStartPosition;
    public void ResetScrollStartPosition() { Resets++; OnResetScrollStartPosition?.Invoke(); }
}
public sealed class SoundManager
{
    private static readonly SoundManager Instance = new SoundManager();
    public readonly List<string> Played = new List<string>();
    public readonly List<GameObject> Owners = new List<GameObject>();
    public static SoundManager Get() => Instance;
    public void Play(int assetReference)
    {
        if (!FilterButtonFeedback.ShouldPlaySound()) return;
        Played.Add("native-asset:" + assetReference); Owners.Add(null);
    }
    public void LoadAndPlay(string asset, GameObject owner = null)
    {
        if (!FilterButtonFeedback.AllowNativeSound(asset)) return;
        Played.Add(asset); Owners.Add(owner);
    }
}
public sealed class SetFilterTray : MonoBehaviour
{
    public GameObject m_trayObject, m_showBone, m_hideBone, m_contents, m_contentsBone;
    public SetFilterItem m_selected;
    public readonly List<SetFilterItem> m_items = new List<SetFilterItem>();
    public int ShowCalls;
    public bool InputCaptured, Closing;
    private bool m_shown;
    public bool IsShown() => m_shown;
    public void Show(bool shown)
    {
        m_shown = shown;
        ShowCalls++;
        if (shown) { InputCaptured = true; Closing = false; m_trayObject.SetActive(true); }
        else Closing = InputCaptured;
    }
    public void FinishClosing()
    {
        if (!Closing || !m_trayObject.activeInHierarchy) return;
        InputCaptured = Closing = false;
        m_trayObject.SetActive(false);
    }
    public UIBScrollable m_scroller;
}
public sealed class CollectionSetFilterDropdownToggle : Component
{
    public MeshRenderer m_currentIconQuad;
    public Texture CurrentIcon;
    public Vector2 CurrentOffset;
    public int IconChanges;
    public void SetToggleIcon(Texture icon, Vector2 offset)
    {
        CurrentIcon = icon; CurrentOffset = offset; IconChanges++;
    }
}
public class PegUIElement : Component
{
    public enum InteractionState { None, Out, Over, Down, Up, Disabled }
    private readonly Dictionary<UIEventType, UIEvent.Handler> m_listeners = new Dictionary<UIEventType, UIEvent.Handler>();
    public bool Enabled;
    public bool ThrowOnDisable;
    public Collider RegisteredHit;
    public bool IsEnabled() => Enabled;
    public void ClearEventListeners() { m_listeners.Clear(); FilterButtonFeedback.ListenersCleared(this); }
    public void SetEnabled(bool value, bool include)
    {
        if (!value && ThrowOnDisable)
        {
            ThrowOnDisable = false;
            throw new InvalidOperationException("Simulated native PegUI cleanup failure");
        }
        Enabled = value;
    }
    public void AddEventListener(UIEventType type, UIEvent.Handler handler)
    {
        m_listeners.TryGetValue(type, out UIEvent.Handler listeners);
        m_listeners[type] = listeners + handler;
    }
    public void Send(UIEventType type)
    {
        if (!Enabled) return;
        var item = GetComponent<SetFilterItem>();
        FilterButtonFeedback.BeginNativeFeedback(item, out bool guarded);
        try
        {
            if (item != null && type == UIEventType.PRESS)
                SoundManager.Get().LoadAndPlay(CollectionFilterMenu.ButtonPressSound);
            item?.NativeFeedback(type);
            (this as UIBButton)?.NativeFeedback(type);
            if (m_listeners.TryGetValue(type, out UIEvent.Handler listeners)) listeners?.Invoke(new UIEvent());
            if (item != null && type == UIEventType.RELEASE)
                SoundManager.Get().LoadAndPlay(CollectionFilterMenu.ButtonPressSound);
        }
        finally { FilterButtonFeedback.EndNativeFeedback(null, guarded); }
    }
    public void Release() => Send(UIEventType.RELEASE);
    public void ReleaseThrough(Collider hit)
    {
        if (hit == RegisteredHit && hit.enabled && hit.gameObject.activeInHierarchy) Release();
    }
}
public sealed class ScrollBarThumb : PegUIElement
{
    private bool m_dragging;
    public bool IsDragging() => m_dragging;
    public void StartDragging() => m_dragging = true;
    public void StopDragging() => m_dragging = false;
}
public static class GameUtils
{
    public static bool IsClassicCard(string id) => id.StartsWith("VAN_", StringComparison.Ordinal);
    public static readonly HashSet<int> CounterpartIds = new HashSet<int>();
    public static int TranslateCardIdToDbId(string id) => id.GetHashCode();
    public static List<int> GetCounterpartCards(int id) => CounterpartIds.Contains(id) ? new List<int> { id } : null;
    public static readonly Dictionary<string, List<string>> CounterpartCardIds = new Dictionary<string, List<string>>();
    public static int CounterpartCardIdCalls;
    public static List<string> GetCounterpartCardIds(string id)
    {
        CounterpartCardIdCalls++;
        return CounterpartCardIds.TryGetValue(id, out var counterparts) ? counterparts : null;
    }
    public static readonly Dictionary<string, string[]> RelatedCards = new Dictionary<string, string[]>();
    public static List<string> GetRelatedCardIds(EntityDef definition, out bool offsetCardNameForRunes)
    {
        offsetCardNameForRunes = false;
        return RelatedCards.TryGetValue(definition.GetCardId(), out var cards) ? new List<string>(cards) : new List<string>();
    }
    public static TAG_CARD_SET[] GetCardSetsInFormat(PegasusShared.FormatType format) => format == PegasusShared.FormatType.FT_STANDARD
        ? GetStandardSets() : new[] { TAG_CARD_SET.CORE, TAG_CARD_SET.TEST_EXPANSION };
    public static bool IsCardValidForFormat(PegasusShared.FormatType format, EntityDef definition)
    {
        var card = CollectionManager.Get()?.Cards.Values.FirstOrDefault(value => value.CardId == definition.GetCardId());
        return GetCardSetsInFormat(format).Contains(card?.Set ?? TAG_CARD_SET.CORE);
    }
    public static TAG_CARD_SET[] GetStandardSets() => new[] { TAG_CARD_SET.CORE };
    public static string GetCardIdFromDbfId(int id) => "dbf-" + id;
    public static void SetParent(GameObject child, GameObject parent, bool keepWorld) => child.transform.SetParent(parent.transform, keepWorld);
}
namespace BetterCollectionManager
{
    internal static class FilterAvailability
    {
        public static void InspectedCardOwnershipChanged(string id, IReadOnlyList<CollectibleCard> variants) { }
        public static void Capture(IEnumerable<CollectibleCard> cards) { }
        public static void Clear() { }
        public static Func<GAME_TAG, bool> KeywordVisible;
        public static bool Keyword(GAME_TAG tag) => KeywordVisible?.Invoke(tag) ?? true;
        public static Func<FilterTrayKind, int, bool> ExtraVisible;
        public static Func<SetFilterItem, bool> ExpansionVisible;
        public static bool Expansion(SetFilterItem item, bool selected) => selected || (ExpansionVisible?.Invoke(item) ?? true);
        public static bool Extra(FilterTrayKind kind, int value, bool selected) => selected || (ExtraVisible?.Invoke(kind, value) ?? true);
    }
    internal static class ExtraFilterState
    {
        private static readonly Dictionary<FilterTrayKind, HashSet<int>> Selected = new Dictionary<FilterTrayKind, HashSet<int>>();
        public static bool IsActive => Selected.Values.Any(values => values.Count > 0);
        public static string Describe() => "fixture extras";
        private static HashSet<int> Values(FilterTrayKind kind)
        {
            if (!Selected.TryGetValue(kind, out var values)) Selected[kind] = values = new HashSet<int>();
            return values;
        }
        private static void Toggle(FilterTrayKind kind, int value)
        {
            var values = Values(kind);
            if (!values.Remove(value)) values.Add(value);
            FilterModes.InvalidateResults();
        }
        public static int SelectedCount(FilterTrayKind kind) => Values(kind).Count;
        public static bool IsExcluded(FilterTrayKind kind, int value) => Values(kind).Contains(value) && FilterModes.IsNegative(FilterModes.Get(kind));
        public static int Clears;
        public static void ClearAll() { Clears++; Selected.Clear(); }
        public static void ClearTypes() { Values(FilterTrayKind.Type).Clear(); }
        public static void ToggleType(TAG_CARDTYPE value) => Toggle(FilterTrayKind.Type, (int)value);
        public static bool HasType(TAG_CARDTYPE value) => Values(FilterTrayKind.Type).Contains((int)value);
        public static bool IsCurrentType(TAG_CARDTYPE value) => true;
        public static void ClearRaces() { Values(FilterTrayKind.Race).Clear(); }
        public static void ToggleRace(TAG_RACE value) => Toggle(FilterTrayKind.Race, (int)value);
        public static bool HasRace(TAG_RACE value) => Values(FilterTrayKind.Race).Contains((int)value);
        public static bool IsCurrentRace(TAG_RACE value) => true;
        public static void ClearSchools() { Values(FilterTrayKind.School).Clear(); }
        public static void ToggleSchool(TAG_SPELL_SCHOOL value) => Toggle(FilterTrayKind.School, (int)value);
        public static bool HasSchool(TAG_SPELL_SCHOOL value) => Values(FilterTrayKind.School).Contains((int)value);
        public static bool IsCurrentSchool(TAG_SPELL_SCHOOL value) => true;
        public static void ClearRarities() { Values(FilterTrayKind.Rarity).Clear(); }
        public static void ToggleRarity(TAG_RARITY value) => Toggle(FilterTrayKind.Rarity, (int)value);
        public static bool HasRarity(TAG_RARITY value) => Values(FilterTrayKind.Rarity).Contains((int)value);
        public static bool IsCurrentRarity(TAG_RARITY value) => true;
        public static void ClearCategories() { Values(FilterTrayKind.Categories).Clear(); }
        public static void ToggleCategory(CuratedCategory value) { ClearPackages(); Toggle(FilterTrayKind.Categories, (int)value); }
        public static void ClearPackages() => Values(FilterTrayKind.Packages).Clear();
        public static void TogglePackage(PackageKind value) { ClearCategories(); Toggle(FilterTrayKind.Packages, (int)value); }
        public static bool HasPackage(PackageKind value) => Values(FilterTrayKind.Packages).Contains((int)value);
        public static bool IsCurrentPackage(PackageKind value) => HasPackage(value);
        public static bool IsActiveForKind(FilterTrayKind kind) => Values(kind).Count > 0;
        public static bool MatchesPackage(CategoryCatalog.Combo entry) => HasPackage(entry.PackageGroup);
        public static bool MatchesEntry(CategoryCatalog.Combo entry) => entry.IsPackage
            ? HasPackage(entry.PackageGroup) : !IsActiveForKind(FilterTrayKind.Packages) && MatchesComboCategory(entry.Category);
        public static bool HasCategory(CuratedCategory value) => Values(FilterTrayKind.Categories).Contains((int)value);
        public static bool IsCurrentCategory(CuratedCategory value) => true;
        public static void PruneCategories(IEnumerable<CuratedCategory> available)
            => Values(FilterTrayKind.Categories).IntersectWith(available.Select(value => (int)value));
        public static bool MatchesComboCategory(CuratedCategory value)
            => (HasCategory(value) || HasCategory(CuratedCategory.Combos)) && (FilterModes.Get(FilterTrayKind.Categories) != FilterMode.And || Values(FilterTrayKind.Categories).Count == 1);
        public static Func<CollectibleCard, bool> OtherFilterMatch = card => true;
        public static bool MatchesExcept(CollectibleCard card, FilterTrayKind except, bool ignorePackages = false) => OtherFilterMatch(card);
    }
    internal static partial class KeywordFilterUi
    {
        private static bool s_cmSessionActive = true, s_built = true;
        private static bool s_buildAborted;
        private static object s_buildCoroutine;
        public static int SessionBuilds;
        private static void EnsureFields() { }
        private static void StopBuildCoroutine()
        {
            if (s_buildCoroutine != null) MelonLoader.MelonCoroutines.Stop(s_buildCoroutine);
            s_buildCoroutine = null;
        }
        private static System.Collections.IEnumerator BuildWhenReady()
        {
            if (!s_buildAborted && s_cmSessionActive) SessionBuilds++;
            yield break;
        }
        public static int LabelRefreshes;
        public static void NotifyFilterChangedFromChrome() { UpdateOpenButtonLabel(); RefreshAllPlaqueLabels(); }
        private static void UpdateOpenButtonLabel() => LabelRefreshes++;
        private static void RefreshAllPlaqueLabels() => LabelRefreshes++;
        public static bool IsPointerOverOpenButton() => CollectionFilterMenu.IsPointerOverToggle();
        public static void RelayOpenButtonToggle(string reason) => CollectionFilterMenu.Toggle();
    }
}
public sealed class CameraMask : Behaviour
{
    public enum CAMERA_MASK_UP_VECTOR { Y, Z }
    public CAMERA_MASK_UP_VECTOR m_UpVector;
    public bool m_UseCameraFromLayer, m_RealtimeUpdate;
    public GameLayer m_CameraFromLayer;
    public readonly List<GameLayer> m_CullingMasks = new List<GameLayer>();
    public CustomViewEntryPoint RenderEntryPoint;
    public float m_Width, m_Height;
    public GameObject m_ClipObjects;
    public int MaskUpdates;
    public void UpdateMask() { MaskUpdates++; }
}
// Models native event registration and effect ownership, not its animation curve.
public sealed class UIBHighlight : Behaviour
{
    public string m_MouseDownSound = "Small_Click.prefab:2a1c5335bf08dc84eb6e04fc58160681", m_MouseUpSound;
    public string m_MouseOverSound, m_MouseOutSound, m_ReleaseDragSound;
    public GameObject m_MouseOverHighlight, m_MouseDownHighlight;
    public bool EnableResponse = true, AlwaysOver;
    public int PressCalls, ReleaseCalls, HoverCalls;
    public void Awake()
    {
        PegUIElement peg = GetComponent<PegUIElement>();
        peg.AddEventListener(UIEventType.PRESS, _ => { if (enabled && EnableResponse) { PressCalls++; m_MouseDownHighlight?.SetActive(true); } });
        peg.AddEventListener(UIEventType.RELEASE, _ => { if (enabled && EnableResponse) { ReleaseCalls++; m_MouseDownHighlight?.SetActive(false); } });
        peg.AddEventListener(UIEventType.ROLLOVER, _ => { if (enabled && EnableResponse) { HoverCalls++; m_MouseOverHighlight?.SetActive(true); } });
        peg.AddEventListener(UIEventType.ROLLOUT, _ => { if (enabled && EnableResponse) m_MouseOverHighlight?.SetActive(false); });
    }
}

namespace BetterCollectionManager
{
    internal static partial class KeywordSetTray
    {
        private static SetFilterTray s_live;
        private static GameObject s_trayObject, s_itemPrefab;
        private static Transform s_contents, s_contentsBone;
        private static UIBScrollable s_scroller;
        private static SetFilterItem s_backItem;
        private static readonly List<SetFilterItem> s_items = new List<SetFilterItem>();
        private static readonly Dictionary<GAME_TAG, SetFilterItem> s_keywordItems = new Dictionary<GAME_TAG, SetFilterItem>();
        private static SetFilterItem AddItem(string text, PegasusShared.FormatType format, Action callback)
        {
            s_unfilteredHeaders.Add(false);
            SetFilterItem item = CreateNavigationItem(s_contents, text, format, callback);
            s_items.Add(item);
            return item;
        }
        private static readonly Dictionary<SetFilterItem, PinnedBackLayout> s_backLayouts = new Dictionary<SetFilterItem, PinnedBackLayout>();
        private static readonly Dictionary<Collider, bool> s_backBlockedColliders = new Dictionary<Collider, bool>();
        private const string BackItemName = "BCM_PinnedFilterBack";
        private static readonly FilterPointerGesture s_pointerGesture = new FilterPointerGesture();
        private static bool s_fixtureOffscreenText;
        private static bool s_fixtureKeywordMenus, s_fixtureModeRows;
        private static bool s_fixtureRowGradient;
        private static bool s_shown, s_isAnimating;
        private static object s_slideCoroutine;
        private const float SlideInSeconds = 0.35f, SlideOutSeconds = 0.25f;
        private static FilterTrayKind s_activeKind;
        private static GameObject s_hideArea;
        private static PegUIElement s_hidePeg;
        private static float s_lastScrollValue = float.NaN, s_ignoreOutsideUntil;
        private static SetFilterItem s_backPressedItem;
        private static bool s_backNavigationPending;
        private static int s_backNavigationFrame = -1, s_backReleaseFrame = -1;
        public static bool IsShown => s_shown && s_trayObject != null && s_trayObject.activeInHierarchy;
        public static bool IsBuilt => s_trayObject != null;
        public static FilterTrayKind ActiveKind => s_activeKind;
        public static void NotifyToggleClicked() { }
        public static int FixtureRebuilds;
        public static void RebuildRows() { ResetMenuInteractions(); FixtureRebuilds++; RebuildFixture(s_activeKind == FilterTrayKind.Expansions, 31); }
        private static void ResetScrollToTop() => s_scroller?.ResetScrollStartPosition();
        private static void ApplyVisibleAffectedClip()
        {
            foreach (SetFilterItem item in s_items) ApplyIdleChrome(item, item.Selected);
            RefreshRowTextAlignment();
        }
        private static void ForceCameraMaskRefresh(string reason)
        {
            if (s_trayObject != null) UpdateVisibleMenuMask(GetActiveCameraMask(s_trayObject), reinitialize: true);
            RefreshRowTextAlignment();
        }
        private static void EnsureContentsBone(SetFilterTray live) { }
        private static void ActivateTrayHierarchy() => s_trayObject.SetActive(true);
        private static void AssignHeightFromScrollItem(SetFilterItem item, UIBScrollableItem scroll) => item.Height = scroll?.m_size.z ?? 0.5f;
        private static string Format(Vector3 vector) => vector.ToString();
        private static Camera ResolveUiCamera() => CameraUtils.Camera;
        private static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        }
        private static void CopyCameraMaskSettings(CameraMask source, CameraMask target)
        { target.m_Width = source.m_Width; target.m_Height = source.m_Height; }
        private static SetFilterItem AddHeader(string text, PegasusShared.FormatType format)
        {
            s_unfilteredHeaders.Add(true);
            SetFilterItem item = CreateNavigationItem(s_contents, text, format, null);
            item.IsHeader = true;
            item.GetComponent<PegUIElement>().SetEnabled(false, true);
            s_items.Add(item);
            return item;
        }
        private static void ConfigureScrollerLikeSets(SetFilterTray live)
        {
            s_scroller.enabled = true;
            s_scroller.m_OverridePegUI = live.m_scroller.m_OverridePegUI;
            s_scroller.m_ScrollBounds.center = live.m_scroller.m_ScrollBounds.center;
            s_scroller.m_ScrollBounds.size = live.m_scroller.m_ScrollBounds.size;
            s_contentsBone.localPosition = new Vector3(0f, 0f, 1.5f);
            CameraMask mask = s_trayObject.GetComponent<CameraMask>();
            mask.m_Width = 2f; mask.m_Height = 4f;
            s_scroller.ResetScrollStartPosition();
        }

        internal sealed class GridFixture
        {
            public bool UseKeywordMenus { get => s_fixtureKeywordMenus; set => s_fixtureKeywordMenus = value; }
            public bool UseModeRows { set => s_fixtureModeRows = value; }
            public bool UsePinnedControls { get => s_pinAllControls; set => s_pinAllControls = value; }
            public bool GradientArtwork { get => s_fixtureRowGradient; set => s_fixtureRowGradient = value; }
            public void OpenLogicControls() { s_openControls[s_activeKind] = ControlGroup.Logic; RebuildRows(); }
            public void CloseControls() { s_openControls.Remove(s_activeKind); RebuildRows(); }
            public SetFilterItem[] PinnedItems => s_backLayouts.Keys.Where(item => item.transform.IsChildOf(Root.transform)).ToArray();
            public void ClearPinnedFixtures()
            {
                foreach (SetFilterItem item in s_backLayouts.Keys.ToArray()) ReleasePinnedBackItem(item);
                s_backItem = null;
            }
            public void ShiftContents(float distance) => s_contents.position += new Vector3(0f, 0f, distance);
            public GameObject Root, NativeRoot;
            public BoxCollider NativeViewport;
            public MeshFilter Frame, Track, Thumb, Shadow;
            public Mesh SourceFrame;
            public Transform TrackTop, TrackBottom;
            public Vector3 ShownPosition => s_live.m_showBone.transform.localPosition + s_gridOffset;
            public Vector3 HiddenPosition => GetHiddenGridPosition(s_live.m_hideBone.transform.localPosition, ShownPosition);
            public Rect HiddenRect()
            {
                Vector3 shown = s_live.m_showBone.transform.localPosition + s_gridOffset;
                Vector3 hidden = GetHiddenGridPosition(new Vector3(-2f, 0f, 0f), shown);
                Vector3 previous = Root.transform.localPosition;
                Root.transform.localPosition = hidden;
                Rect result = GridScreenRect(GetTrayFrameBounds(Root, s_contents, s_contents, GetBoxWorldBounds(s_scroller.m_ScrollBounds)), CameraUtils.Camera);
                Root.transform.localPosition = previous;
                return result;
            }
            public IReadOnlyList<SetFilterItem> Items => s_items;
            public UIBScrollable Scroller => s_scroller;
            public float Fit => s_gridFit;
            public UIBScrollableItem ContentSize => s_contents.GetComponent<UIBScrollableItem>();
            public float PublishedHeight => s_contents.GetComponentsInChildren<UIBScrollableItem>(true).Sum(item => item.m_size.z);
            public float NativeLeft;
            public float FrameLeft => GridScreenRect(Frame.GetComponent<Renderer>().bounds, CameraUtils.Camera).xMin;
            public bool UsesVisibleFrame => s_gridGeometry.FrameRenderer == Frame.GetComponent<Renderer>();
            public SetFilterItem MakeBack(bool textureText = false, bool hiddenSurface = false)
            {
                var go = new GameObject("Fixture Back");
                go.transform.SetParent(Root.transform, false);
                var item = go.AddComponent<SetFilterItem>();
                item.Height = 0.5f;
                AddRowArtwork(item);
                if (textureText)
                {
                    item.m_uberText.GlyphOffset = 0.18f;
                    MeshFilter surface = FixtureArt("Text surface", Vector3.zero, 0.8f, 0.15f);
                    surface.transform.SetParent(item.transform, false);
                    item.m_uberText.RenderToTexture = true;
                    item.m_uberText.RenderOnObject = surface.gameObject;
                    surface.GetComponent<Renderer>().enabled = !hiddenSurface;
                    item.m_uberText.TextureRefreshDeferred = hiddenSurface;
                }
                StretchFullRow(item, GetBoxWorldBounds(s_scroller.m_ScrollBounds));
                return item;
            }
            public void RemoveBack(SetFilterItem item) { ReleaseWideRow(item); UnityEngine.Object.Destroy(item.gameObject); }
            public SetFilterItem MakePinnedBack()
            {
                s_rowsKind = FilterTrayKind.None; // This injected header differs from the built menu.
                foreach (SetFilterItem heading in s_items.Where(row => row.IsHeader))
                {
                    heading.m_uberText.UpdateNow(true);
                    Renderer ink = CounterTextLayout.VisibleRenderers(heading.m_uberText).First();
                    ink.UseLocalBounds = false;
                    ink.WorldBounds = new Bounds(new Vector3(-5000f, 0f, -5000f), new Vector3(2000f, 1f, 2000f));
                }
                s_fixtureOffscreenText = true;
                try { s_backItem = CreatePinnedBackItem(Root, s_contentsBone, s_scroller, PegasusShared.FormatType.FT_STANDARD); }
                finally { s_fixtureOffscreenText = false; }
                LayoutItemsLikeSets();
                FitGridScrollViewport(s_backLayouts[s_backItem].Mask, s_scroller.m_ScrollBounds);
                return s_backItem;
            }
            public Bounds BackHitBounds => s_backLayouts[s_backItem].HitBounds;
            public Bounds BackBounds => s_backLayouts[s_backItem].ButtonBounds;
            public CameraMask BackMask => s_backLayouts[s_backItem].Mask;
            public CameraMask ActiveMask => GetActiveCameraMask(Root);
            public bool PointerIsOverBack => FindPinnedBackUnderPointer() == s_backItem;
            public void RemovePinnedBack() { DestroyPinnedBackItem(); }
            public void BlockBehindBack() => BlockCollidersBehindBack(s_backItem);
            public void RestoreBlocked() => RestoreBackBlockedColliders();
            public bool HasInputCapture => s_backBlockedColliders.Count > 0 || s_backPressedItem != null
                || s_backNavigationPending || s_backNavigationFrame != -1 || s_backReleaseFrame != -1;
            public bool HideAreaActive => s_hideArea != null && s_hideArea.activeInHierarchy;
            public bool OwnInputDisabled => Root.GetComponentsInChildren<Collider>(true)
                .All(c => (s_hideArea != null && c.transform.IsChildOf(s_hideArea.transform))
                    || (s_isAnimating && c == s_scroller.m_ScrollBounds) || !c.enabled)
                && Root.GetComponentsInChildren<PegUIElement>(true)
                .All(p => (s_hideArea != null && p.transform.IsChildOf(s_hideArea.transform)) || !p.IsEnabled())
                && !s_scroller.enabled && !s_scroller.m_OverridePegUI;
            public void ReleaseOutside() => s_hidePeg.Release();
            public void BeginScrollRelease()
            {
                s_pointerGesture.Reset();
                ResetPinnedPress();
                Input.mousePosition = ScreenRect.center;
                Input.LeftPressed = true; Input.LeftHeld = true; Input.LeftReleased = false;
                Time.frameCount++; ObservePointerGesture();
                Input.mousePosition += new Vector3(1000f, 0f, 0f);
                Input.LeftPressed = false; Input.LeftHeld = false; Input.LeftReleased = true;
                Time.frameCount++; ObservePointerGesture();
            }
            public void ClearGesture()
            {
                Input.LeftPressed = Input.LeftHeld = Input.LeftReleased = false;
                s_pointerGesture.Reset();
                ResetPinnedPress();
            }
            public bool Animating => s_isAnimating;
            public void StopAnimation() => MelonLoader.MelonCoroutines.Stop(s_slideCoroutine);
            public void CaptureBackInput()
            {
                UpdateCollectionInputBlocker();
                s_backPressedItem = s_backItem;
                s_backNavigationPending = true;
                s_backNavigationFrame = Time.frameCount;
                s_backReleaseFrame = Time.frameCount;
                BlockCollidersBehindBack(s_backItem);
            }
            public bool Dirty => s_gridDirty;
            public void Relayout() => LayoutItemsLikeSets();
            public void RefreshText() => RefreshRowTextAlignment();
            public void RefreshVisible() => ApplyVisibleAffectedClip();
            public void PollInteraction() => PollMenuInteractions();
            public bool ScrollbarGesture => IsScrollbarGesture;
            public bool ScrollbarRects(out Rect track, out Rect thumb) => TryGetScrollbarRects(CameraUtils.Camera, out track, out thumb);
            public float ScrollRange => Mathf.Max(0f, PublishedHeight - GetBoxWorldBounds(s_scroller.m_ScrollBounds).size.z + s_scroller.m_ScrollBottomPadding);
            public void Rebuild() => RebuildFixture(false, 31);
            public void RebuildCount(int count) => RebuildFixture(false, count);
            public int Columns => s_gridColumns;
            public int Rows => s_gridRows;
            public SetFilterTray Native => s_live;
            public Rect ScreenRect => GridScreenRect(Frame.GetComponent<Renderer>().bounds, CameraUtils.Camera);
        }

        private static void RebuildFixture(bool expansions, int count)
        {
            ResetMenuInteractions();
            DestroyPinnedBackItem();
            ClearExpansionItems();
            ReleaseWideRows();
            foreach (SetFilterItem item in s_items) UnityEngine.Object.Destroy(item.gameObject);
            s_items.Clear();
            foreach (SetFilterItem pooled in s_contents.GetComponentsInChildren<SetFilterItem>(true))
                UnityEngine.Object.Destroy(pooled.gameObject);
            s_keywordItems.Clear();
            ResetKeywordRowCache();
            ResetExtraRowCache();
            s_extraSelectionRefresh.Clear();
            s_modeItem = null;
            s_notItem = null;
            s_unfilteredHeaders.Clear();
            PrepareGridItems();
            if (s_fixtureModeRows) AddModeRow(PegasusShared.FormatType.FT_STANDARD);
            if (expansions) PopulateExpansionRows();
            else if (s_fixtureKeywordMenus && s_activeKind == FilterTrayKind.Keywords) PopulateKeywordRows(PegasusShared.FormatType.FT_STANDARD);
            else if (s_fixtureKeywordMenus && s_activeKind == FilterTrayKind.Type) PopulateTypeRows(PegasusShared.FormatType.FT_STANDARD);
            else if (s_fixtureKeywordMenus && s_activeKind == FilterTrayKind.Race) PopulateRaceRows(PegasusShared.FormatType.FT_STANDARD);
            else if (s_fixtureKeywordMenus && s_activeKind == FilterTrayKind.School) PopulateSchoolRows(PegasusShared.FormatType.FT_STANDARD);
            else if (s_fixtureKeywordMenus && s_activeKind == FilterTrayKind.Rarity) PopulateRarityRows(PegasusShared.FormatType.FT_STANDARD);
            else if (s_fixtureKeywordMenus && s_activeKind == FilterTrayKind.Packages) PopulatePackageRows(PegasusShared.FormatType.FT_STANDARD);
            else if (s_fixtureKeywordMenus && s_activeKind == FilterTrayKind.Categories) PopulateCategoryRows(PegasusShared.FormatType.FT_STANDARD);
            else
            for (int i = 0; i < count; i++)
            {
                SetFilterItem item = CreateNavigationItem(s_contents, "Grid item " + i,
                    PegasusShared.FormatType.FT_STANDARD, null);
                item.IsHeader = i == 0 || i == 17;
                item.GetComponent<PegUIElement>().SetEnabled(!item.IsHeader, true);
                s_items.Add(item);
            }
            ConfigureGrid();
            foreach (SetFilterItem item in s_items) ApplyGridItemScale(item, item.GetComponent<UIBScrollableItem>());
            if (s_pinAllControls && s_activeKind != FilterTrayKind.Menu)
                s_backItem = CreatePinnedBackItem(s_trayObject, s_contentsBone, s_scroller, PegasusShared.FormatType.FT_STANDARD, controls: true, columns: s_gridColumns);
            else if (s_pinAllControls && ShowRootClearAll)
                s_backItem = CreatePinnedBackItem(s_trayObject, s_contentsBone, s_scroller, PegasusShared.FormatType.FT_STANDARD, clearAll: true);
            LayoutItemsLikeSets();
            if (s_backItem != null) FitGridScrollViewport(s_backLayouts[s_backItem].Mask, s_scroller.m_ScrollBounds);
            RememberBuiltRows();
        }

        internal static void AddRowArtwork(SetFilterItem item)
        {
            MeshFilter face = FixtureArt("Row face", new Vector3(0f, -0.2f, 0f), 1f, 0.5f);
            face.transform.SetParent(item.transform, false);
            // Real selection effects can be larger than the button face and
            // use generic child/mesh/material names beneath an effect parent.
            var glow = new GameObject("Native selected effect");
            glow.transform.SetParent(item.transform, false);
            MeshFilter glowMesh = FixtureArt("Effect plane", new Vector3(0f, -0.19f, 0f), 1.6f, 0.8f);
            glowMesh.transform.SetParent(glow.transform, false);
            glow.SetActive(false);
            item.m_selectedGlow = glow;
            var pressed = new GameObject("Native press effect");
            pressed.transform.SetParent(item.transform, false);
            MeshFilter pressMesh = FixtureArt("Inner Shadow", new Vector3(0f, -0.18f, 0f), 1f, 0.5f);
            pressMesh.transform.SetParent(pressed.transform, false);
            pressed.SetActive(false);
            item.m_pressedShadow = pressed;
            var hover = new GameObject("Native hover effect");
            hover.transform.SetParent(item.transform, false);
            MeshFilter hoverMesh = FixtureArt("Mouseover Glow", new Vector3(0f, -0.17f, 0f), 1f, 0.5f);
            hoverMesh.transform.SetParent(hover.transform, false);
            hover.SetActive(false);
            item.m_mouseOverGlow = hover;
            if (s_fixtureRowGradient)
                foreach (MeshFilter layer in new[] { face, glowMesh, pressMesh, hoverMesh })
                    layer.sharedMesh.colors = new[] { Color.white, new Color(.4f, .5f, .6f, 1f), new Color(.4f, .5f, .6f, 1f), Color.white };
            var label = new GameObject("Row caption");
            label.transform.SetParent(item.transform, false);
            item.m_uberText = label.AddComponent<UberText>();
            // Native caption tint can vary vertically even with one TextColor.
            item.m_uberText.GradientUpperColor = new Color(0.9f, 0.8f, 0.7f, 1f);
            item.m_uberText.GradientLowerColor = new Color(0.5f, 0.4f, 0.3f, 0.8f);
            item.m_uberText.TextColor = new Color(0.32f, 0.21f, 0.12f, 1f);
            var ink = label.AddComponent<MeshRenderer>();
            ink.UseLocalBounds = true;
            ink.LocalBounds = new Bounds(Vector3.zero, new Vector3(0.6f, 0.01f, 0.1f));
            if (s_fixtureOffscreenText)
            {
                ink.UseLocalBounds = false;
                ink.WorldBounds = new Bounds(new Vector3(-5000f, 0f, -5000f), new Vector3(2000f, 1f, 2000f));
                MeshFilter surface = FixtureArt("Text output", Vector3.zero, 0.8f, 0.15f);
                surface.transform.SetParent(item.transform, false);
                item.m_uberText.RenderToTexture = true;
                item.m_uberText.RenderOnObject = surface.gameObject;
                item.gameObject.AddComponent<BoxCollider>().size = new Vector3(50f, 10f, 50f);
                var inheritedHit = new GameObject("Inherited child hit area");
                inheritedHit.transform.SetParent(item.transform, false);
                inheritedHit.AddComponent<BoxCollider>().size = new Vector3(100f, 10f, 100f);
            }
        }

        public static GridFixture MakeGridFixture(float nativeHitHeight = 4f, bool rootExtent = false)
        {
            HideImmediately();
            s_pinAllControls = false;
            ResetMenuSession();
            ReleaseWideRows();
            s_gridGeometry?.Dispose();
            s_gridGeometry = null;
            s_gridOffset = Vector3.zero;
            s_gridFit = 1f;
            s_gridDirty = false;
            s_items.Clear();
            s_backItem = null;
            var native = new GameObject("Native tray");
            var nativeScroll = native.AddComponent<UIBScrollable>();
            nativeScroll.m_OverridePegUI = true;
            nativeScroll.m_ScrollBounds = native.AddComponent<BoxCollider>();
            nativeScroll.m_ScrollBounds.size = new Vector3(2f, 1f, nativeHitHeight);
            var nativeContents = new GameObject("Native Contents");
            nativeContents.transform.SetParent(native.transform, false);
            s_live = new GameObject("Native filter controller").AddComponent<SetFilterTray>();
            s_live.m_trayObject = native;
            s_live.m_showBone = new GameObject("Shown");
            s_live.m_scroller = nativeScroll;
            s_live.m_hideBone = new GameObject("Hidden");
            s_live.m_contents = nativeContents;
            s_live.m_contentsBone = new GameObject("Native contents bone");
            s_live.m_hideBone.transform.localPosition = new Vector3(-20f, 0f, 0f);
            s_trayObject = new GameObject("Custom tray");
            s_trayObject.AddComponent<PegUIElement>().SetEnabled(true, true);
            s_scroller = s_trayObject.AddComponent<UIBScrollable>();
            s_scroller.m_ScrollBounds = s_trayObject.AddComponent<BoxCollider>();
            var mask = s_trayObject.AddComponent<CameraMask>();
            mask.m_Width = 2f; mask.m_Height = 4f;
            s_contents = new GameObject("Contents").transform;
            s_contents.SetParent(s_trayObject.transform, false);
            s_scroller.ScrollObject = s_contents.gameObject;
            if (rootExtent) s_contents.gameObject.AddComponent<UIBScrollableItem>().m_size = new Vector3(2f, 0.1f, 100f);
            s_contentsBone = new GameObject("ContentsBone").transform;
            s_contentsBone.SetParent(s_contents, false);
            s_contentsBone.localPosition = new Vector3(0f, 0f, 1.5f);
            s_itemPrefab = new GameObject("Native item");
            s_itemPrefab.AddComponent<SetFilterItem>();
            s_itemPrefab.AddComponent<UIBScrollableItem>().m_size = new Vector3(1f, 0.1f, 0.5f);
            BoxCollider templateHit = s_itemPrefab.AddComponent<BoxCollider>();
            templateHit.center = new Vector3(0f, 0.12f, 0f);
            templateHit.size = new Vector3(1f, 0.06f, 0.5f);
            MeshFilter frame = FixtureArt("Frame", new Vector3(0f, 0.1f, 0f), 2.5f, 4.5f);
            frame.gameObject.layer = 12;
            // Native hover planes can be larger than the actual frame. They
            // must not become the projection/placement reference while hidden.
            MeshFilter glow = FixtureArt("Inactive hover glow", Vector3.zero, 10f, 12f);
            glow.gameObject.SetActive(false);
            MeshFilter track = FixtureArt("ScrollTrack", new Vector3(1.15f, 0.01f, 0f), 0.12f, 3.8f);
            MeshFilter thumb = FixtureArt("ScrollThumb", new Vector3(1.15f, 0.02f, 1.5f), 0.18f, 0.3f);
            s_scroller.m_ScrollTrack = track.gameObject.AddComponent<BoxCollider>();
            s_scroller.m_ScrollTrack.center = new Vector3(0f, 0.01f, 0f);
            s_scroller.m_ScrollTrack.size = new Vector3(0.12f, 0.0001f, 3.8f);
            s_scroller.m_ScrollThumb = thumb.gameObject.AddComponent<ScrollBarThumb>();
            s_scroller.m_ScrollThumb.SetEnabled(true, true);
            thumb.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.18f, 0.0001f, 0.3f);
            s_scroller.OnUpdateScroll = () => {
                Bounds rail = GetBoxWorldBounds(s_scroller.m_ScrollTrack);
                float center = Mathf.Lerp(rail.max.z, rail.min.z, Mathf.Clamp01(s_scroller.ScrollValue));
                thumb.transform.position += new Vector3(rail.center.x - thumb.transform.position.x, 0f,
                    center - thumb.transform.position.z);
            };
            MeshFilter shadow = FixtureArt("InnerShadow", new Vector3(0f, 0.03f, 0f), 2f, 4f);
            Transform top = new GameObject("TrackTop").transform, bottom = new GameObject("TrackBottom").transform;
            top.SetParent(s_trayObject.transform, false); bottom.SetParent(s_trayObject.transform, false);
            top.localPosition = new Vector3(1.15f, 0f, 1.8f); bottom.localPosition = new Vector3(1.15f, 0f, -1.8f);
            return new GridFixture { Root = s_trayObject, NativeRoot = native, NativeViewport = nativeScroll.m_ScrollBounds,
                Frame = frame, SourceFrame = frame.sharedMesh, Track = track, Thumb = thumb, Shadow = shadow, TrackTop = top, TrackBottom = bottom,
                NativeLeft = GridScreenRect(frame.GetComponent<Renderer>().bounds, CameraUtils.Camera).xMin };
        }

        private static MeshFilter FixtureArt(string name, Vector3 position, float width, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(s_trayObject.transform, false);
            go.transform.localPosition = position;
            var mesh = new Mesh { name = name, subMeshCount = 1,
                vertices = new[] { new Vector3(-width / 2f, 0f, -height / 2f), new Vector3(width / 2f, 0f, -height / 2f),
                    new Vector3(width / 2f, 0f, height / 2f), new Vector3(-width / 2f, 0f, height / 2f) },
                bounds = new Bounds(Vector3.zero, new Vector3(width, 0.01f, height)) };
            mesh.SetUVs(0, new List<Vector4> { new Vector4(0f, 0f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f),
                new Vector4(1f, 1f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f) });
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false);
            MeshFilter filter = go.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return filter;
        }
    }
}

internal static partial class Program
{
    private static void CheckNativeFilterGrid()
    {
        int oldHeight = Screen.height, oldWidth = Screen.width;
        float oldScale = CameraUtils.Camera.PixelScale, oldSkewX = CameraUtils.Camera.DepthSkewX, oldSkewZ = CameraUtils.Camera.DepthSkewZ;
        Vector3 oldOffset = CameraUtils.Camera.Offset, oldEye = CameraUtils.Camera.transform.position;
        Screen.width = 1280; Screen.height = 720;
        CameraUtils.Camera.PixelScale = 100f;
        CameraUtils.Camera.Offset = new Vector3(640f, 360f, 0f);
        foreach (int rows in new[] { 0, 1, 2, 7, 16 })
        foreach (int columns in new[] { 1, 2, 4 })
        {
            CameraUtils.Camera.transform.position = new Vector3(0f, columns == 2 ? -20f : 20f, 0f);
            CameraUtils.Camera.DepthSkewX = columns == 2 ? 0.6f : -0.35f;
            CameraUtils.Camera.DepthSkewZ = rows % 2 == 0 ? 0.4f : -0.7f;
            CollectionQualitySettings.FilterMenuRows = rows;
            CollectionQualitySettings.FilterMenuColumns = columns;
            var fixture = KeywordSetTray.MakeGridFixture();
            KeywordSetTray.SettingsChanged();
            Check(fixture.Dirty, "Settings changes must queue a filter grid rebuild");
            fixture.Rebuild();
            Check(!fixture.Dirty, "A completed grid rebuild must consume the queued layout change");
            Check(fixture.Items.Count == 31 && fixture.Scroller.Registered.Distinct().Count() == 31,
                "The production tray adapter must register all entries so overflow remains scrollable");
            Check(fixture.NativeRoot.transform.localScale.Equals(Vector3.one)
                && fixture.NativeViewport.size.Equals(new Vector3(2f, 1f, 4f)),
                "Custom grid changes must not resize the game's native expansion tray");
            Vector3 rootScale = fixture.Root.transform.localScale;
            Check(Near(rootScale.x, rootScale.y) && Near(rootScale.x, rootScale.z),
                "The tray root must never stretch the scrollbar, frame or shadows on one axis");
            Bounds track = fixture.Track.GetComponent<Renderer>().bounds, thumb = fixture.Thumb.GetComponent<Renderer>().bounds;
            Check(Near(track.size.x, 0.12f * fixture.Fit) && Near(thumb.size.x, 0.18f * fixture.Fit)
                && Near(thumb.size.z, 0.3f * fixture.Fit), "Scrollbar track width and thumb dimensions must remain native");
            Check(Near(fixture.TrackTop.position.x, fixture.TrackBottom.position.x)
                && fixture.TrackTop.position.z > fixture.TrackBottom.position.z,
                "Native scrollbar travel bones must follow the extended track");
            Check(!fixture.Shadow.GetComponent<Renderer>().enabled, "Broad inner shadow must not obscure the extended menu");
            Check(fixture.SourceFrame.vertices.Length == 4 && Near(fixture.SourceFrame.bounds.size.x, 2.5f),
                "Resizing must leave the imported native frame mesh intact");
            Check(fixture.Root.GetComponentsInChildren<MeshFilter>(true).Length == 5 + fixture.Items.Count * 4,
                "The tray must reuse only its native frame, background and item artwork, without an additional brown plane");
            Bounds viewport = fixture.Scroller.m_ScrollBounds.bounds;
            Check(Near(track.size.z, viewport.size.z - 0.2f * fixture.Fit)
                && Near(track.center.x, viewport.max.x + 0.15f * fixture.Fit),
                "Scrollbar track must extend to the viewport height and translate to its right edge");
            Check(Near(fixture.Root.transform.position.y, 0f), "Screen fitting must move along the UI plane without changing depth");
            Check(fixture.Scroller.m_ForceScrollAreaHitTest && !fixture.Scroller.m_UseScrollContentsInHitTest,
                "The native scroller must hit-test the visible viewport, including gaps between items");
            var cells = FilterGridLayout.Arrange(Enumerable.Repeat(0.5f * fixture.Fit, 31).ToArray(),
                fixture.Items.Select(item => item.IsHeader).ToArray(), columns);
            float publishedHeight = fixture.PublishedHeight;
            Check(Near(publishedHeight, cells.Max(c => c.Down + c.Height)),
                "Native scroll sizes must total exactly one height per grid row, in world units, even when the fitted scale is below one");
            Check(Math.Abs(fixture.FrameLeft - fixture.NativeLeft) < 0.01f,
                $"The outer frame must stay at its native left seam (rows={rows}, columns={columns}, actual={fixture.FrameLeft}, native={fixture.NativeLeft}), without a gap exposing collection cards");
            Check(fixture.UsesVisibleFrame && fixture.Root.layer != fixture.Frame.gameObject.layer,
                "Frame placement must use visible artwork, ignoring larger hidden hover planes and the container's different layer");
            Check(fixture.Items.Count(item => item.GetComponent<UIBScrollableItem>().m_size.z > 0f)
                == cells.Select(cell => cell.Down).Distinct().Count(),
                "Exactly one item per actual row must contribute to the native scroll height");
            float range = Math.Max(0f, publishedHeight - viewport.size.z);
            float required = Math.Max(0f, cells.Max(c => c.Down + c.Height) - viewport.size.z);
            Check(Near(range, required) && (required < 0.01f || range * 0.5f < required - 0.001f),
                "The last grid row must coincide with the end of the scrollbar, not its midpoint");
            if (rows == 1)
                Check(publishedHeight > viewport.size.z, "A short viewport must report overflow so the native scroll thumb can appear");
            Check(fixture.HiddenRect().xMax < 0f, "The complete resized frame must start offscreen, including the scrollbar");
            foreach (SetFilterItem item in fixture.Items)
            {
                Vector3 scale = item.transform.lossyScale;
                Check(Near(scale.x, fixture.Fit) && Near(scale.y, fixture.Fit) && Near(scale.z, fixture.Fit),
                    "Resizing the frame must preserve the proportions of text, artwork and hit targets");
                Check(Near(item.Height, 0.5f * fixture.Fit),
                    "Registered row heights must follow the fitted item geometry");
                Check(Near(fixture.Scroller.RegisteredSizes[item.gameObject].z, item.Height),
                    "All columns must retain full visibility bounds even when only one item contributes their row's scroll height");
            }
            SetFilterItem first = fixture.Items[1], next = fixture.Items[2];
            Check(columns == 1 ? next.transform.position.z < first.transform.position.z
                : Near(next.transform.position.z, first.transform.position.z) && next.transform.position.x > first.transform.position.x,
                "The actual tray must fill columns left to right before starting the next row");
            CheckFrameSlices(fixture.Frame.sharedMesh, fixture.SourceFrame);
            float contentHeight = fixture.PublishedHeight;
            Vector3 frameSize = fixture.Frame.sharedMesh.bounds.size, maskSize = new Vector3(fixture.Root.GetComponent<CameraMask>().m_Width, 0f,
                fixture.Root.GetComponent<CameraMask>().m_Height);
            Vector3 size = fixture.Root.transform.localScale, position = fixture.Root.transform.position;
            fixture.Rebuild();
            Check(Near(contentHeight, fixture.PublishedHeight) && fixture.Root.transform.localScale.Equals(size) && fixture.Root.transform.position.Equals(position)
                && fixture.Frame.sharedMesh.bounds.size.Equals(frameSize)
                && maskSize.Equals(new Vector3(fixture.Root.GetComponent<CameraMask>().m_Width, 0f, fixture.Root.GetComponent<CameraMask>().m_Height)),
                "Repeated rebuilds must not accumulate scale or shift the tray");
            CollectionQualitySettings.FilterMenuRows = 0;
            CollectionQualitySettings.FilterMenuColumns = 1;
            fixture.Rebuild();
            Check(fixture.Rows == 8 && fixture.Root.transform.localScale.Equals(Vector3.one)
                && fixture.ScreenRect.yMin >= Screen.height * 0.08f - 0.01f
                && fixture.ScreenRect.yMax <= Screen.height * 0.92f + 0.01f
                && fixture.Items.All(item => item.transform.lossyScale.Equals(Vector3.one) && Near(item.Height, 0.5f)),
                $"Returning to Automatic must restore native item size and use screen-height rows, including with a tilted camera (previous {rows}x{columns}, now {fixture.Rows}x{fixture.Columns}, fit={fixture.Fit})");
        }
        CollectionQualitySettings.FilterMenuRows = 1;
        CollectionQualitySettings.FilterMenuColumns = 3;
        var overscan = KeywordSetTray.MakeGridFixture(8f);
        overscan.Rebuild();
        float visibleHeight = overscan.Root.GetComponent<CameraMask>().m_Height * overscan.Root.transform.lossyScale.z;
        Check(Near(overscan.Scroller.m_ScrollBounds.size.z * overscan.Scroller.m_ScrollBounds.transform.lossyScale.z, visibleHeight),
            "An oversized native hit-test box must shrink to the actual pixel viewport, not leave hidden rows counted as visible");
        Check(overscan.PublishedHeight > visibleHeight,
            "Overflow must remain scrollable when the original collider was much taller than its mask");
        CollectionQualitySettings.FilterMenuRows = 16;
        CollectionQualitySettings.FilterMenuColumns = 4;
        var rootExtent = KeywordSetTray.MakeGridFixture(4f, true);
        rootExtent.Rebuild();
        var rootCells = FilterGridLayout.Arrange(rootExtent.Items.Select(item => item.Height).ToArray(),
            rootExtent.Items.Select(item => item.IsHeader).ToArray(), 4);
        Check(rootExtent.Fit < 1f && Near(rootExtent.PublishedHeight, rootCells.Max(c => c.Down + c.Height))
            && Near(rootExtent.ContentSize.m_size.z, rootExtent.PublishedHeight),
            "An existing content-size component must contribute the world-space extent once, without inverse-scale inflation or duplicate child heights");
        var varied = FilterGridLayout.Arrange(new[] { 0.4f, 0.8f, 0.5f, 0.3f, 0.6f, 0.7f, 0.2f },
            new[] { false, false, true, false, false, false, true }, 2);
        float[] contributions = FilterGridLayout.ScrollHeights(varied);
        Check(Near(contributions.Sum(), 2.8f) && Near(contributions[0], 0.8f) && Near(contributions[1], 0f)
            && Near(contributions[3], 0.6f) && Near(contributions[4], 0f),
            "Mixed-height and incomplete rows must contribute their maximum height once, including separate headings");
        CollectionQualitySettings.FilterMenuRows = 0;
        CollectionQualitySettings.FilterMenuColumns = 1;
        Screen.height = oldHeight; Screen.width = oldWidth;
        foreach (Vector3 direction in new[] { new Vector3(-20f, 0f, 0f), new Vector3(20f, 0f, 0f), new Vector3(0f, -20f, 0f), new Vector3(0f, 20f, 0f) })
        {
            Rect frame = Rect.MinMaxRect(10f, 10f, 90f, 70f);
            float travel = FilterGridLayout.HiddenSlideMultiplier(frame, direction, 100f, 80f);
            Check(travel >= 1f && (direction.x < 0f ? frame.xMax + direction.x * travel < 0f
                : direction.x > 0f ? frame.xMin + direction.x * travel > 100f
                : direction.y < 0f ? frame.yMax + direction.y * travel < 0f
                : frame.yMin + direction.y * travel > 80f),
                "Slide travel must hide the complete rectangle in any native slide direction");
        }
        CameraUtils.Camera.PixelScale = oldScale; CameraUtils.Camera.Offset = oldOffset;
        CameraUtils.Camera.transform.position = oldEye;
        CameraUtils.Camera.DepthSkewX = oldSkewX; CameraUtils.Camera.DepthSkewZ = oldSkewZ;
    }
    private static void CheckFrameSlices(Mesh resized, Mesh source)
    {
        var uv = new List<Vector4>(); resized.GetUVs(0, uv);
        Vector3[] points = resized.vertices;
        Check(points.Length > 4 && uv.Count == points.Length, "Four-vertex native planes must be sliced before resizing");
        // Along the left edge strip, original UV distances must still correspond
        // to native world distances, even after the middle becomes much wider.
        float left = resized.bounds.min.x;
        float stripU = uv.Where(u => u.x > 0.001f && u.x < 0.5f).Min(u => u.x);
        bool sampledBorder = false;
        for (int i = 0; i < points.Length; i++)
            if (Near(uv[i].x, stripU))
            {
                sampledBorder = true;
                Check(Near(points[i].x - left, uv[i].x * source.bounds.size.x),
                    "Border texture and shadow falloff must keep their original thickness");
            }
        Check(sampledBorder, "The border thickness check must inspect actual sliced geometry");
        double area = 0;
        int[] triangles = resized.GetTriangles(0);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
            area += Math.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) * 0.5;
        }
        Check(Math.Abs(area - resized.bounds.size.x * resized.bounds.size.z) < 0.001,
            "Nine-slicing must cover the extended frame exactly, without holes or overlapping triangles");
    }

}
