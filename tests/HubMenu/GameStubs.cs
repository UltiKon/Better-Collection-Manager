using System;
using System.Collections.Generic;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

// The installed client/loader also exposes a legacy Harmony namespace.
// Keep that naming collision present when compiling the production hooks.
namespace Harmony { internal sealed class LegacyNamespaceFixture { } }

namespace HarmonyLib
{
    public sealed class Harmony
    {
        public readonly List<MethodBase> Patched = new List<MethodBase>();
        public void Patch(MethodBase original, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        { if (original == null) throw new Exception("Missing native hook"); Patched.Add(original); }
    }
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method) { if (method == null) throw new Exception("Missing hook handler"); } }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
namespace MelonLoader
{
    public sealed class MelonPreferences_Entry<T> { public T Value; }
    public sealed class MelonPreferences_Category
    {
        public MelonPreferences_Entry<T> CreateEntry<T>(string name, T value, string description)
        { var entry = new MelonPreferences_Entry<T> { Value = value }; MelonPreferences.Entries[name] = entry; return entry; }
    }
    public static class MelonPreferences
    {
        public static readonly Dictionary<string, object> Entries = new Dictionary<string, object>();
        public static MelonPreferences_Category CreateCategory(string name) => new MelonPreferences_Category();
        public static void Load() { }
        public static void Save() { }
    }
}
namespace BetterCollectionManager
{
    internal static class DebugLog
    {
        public static readonly List<string> Lines = new List<string>();
        public static void Msg(string message) { Lines.Add(message); }
        public static void Warning(string message) { Lines.Add(message); }
        public static void Exception(string message, Exception ex) { Lines.Add(message + ": " + ex.Message); }
    }
    internal static class CounterTextLayout
    {
        internal static int RemoveClonedGlyphs(UberText label)
        {
            if (!label.Updated) throw new Exception("Cloned glyph cleanup needs the new caption renderer first");
            return 0;
        }
    }
    // Native source resolution and request/cache identity are tested below.
    // The actual GPU readback/renderer still requires the game.
    internal static class DarkmoonLogo
    {
        internal static void Refresh(BoxMenuButton button, int offerId) { }
    }
    internal static class DarkmoonLogoLibrary
    {
        internal static bool Loaded = true;
        internal static readonly Queue<Action> Pending = new Queue<Action>();
        internal static LuckyDrawAssetReferenceLibrary Current => Loaded ? LuckyDrawUtils.AssetReferenceLibrary : null;
        internal static void EnsureLoaded(Action ready, Action<Exception> failed)
        { if (Loaded) ready(); else Pending.Enqueue(ready); }
    }
}
public enum UIEventType { RELEASE, PRESS, ROLLOVER, ROLLOUT }
public sealed class UIEvent
{
    public delegate void Handler(UIEvent e);
    private readonly PegUIElement m_element;
    public UIEvent(UIEventType type, PegUIElement element) { m_element = element; }
    public PegUIElement GetElement() => m_element;
}
public class PegUIElement : MonoBehaviour
{
    private readonly Dictionary<UIEventType, UIEvent.Handler> m_listeners = new Dictionary<UIEventType, UIEvent.Handler>();
    private bool m_enabled = true;
    public void AddEventListener(UIEventType type, UIEvent.Handler handler)
    { m_listeners.TryGetValue(type, out var existing); m_listeners[type] = existing + handler; }
    public void ClearEventListeners() => m_listeners.Clear();
    public void SetEnabled(bool value, bool isInternal = false) { m_enabled = value; }
    public bool IsEnabled() => m_enabled;
    public void Release() { if (m_enabled && m_listeners.TryGetValue(UIEventType.RELEASE, out var handler)) handler(new UIEvent(UIEventType.RELEASE, this)); }
}
public sealed class UberText : Component
{
    public string Text;
    public bool GameStringLookup = true;
    public bool Updated, Hidden;
    public Color TextColor = new Color { a = 1f };
    public Font TrueTypeFont, LocalizedFont;
    public int FontSize = 24, RenderQueue;
    public float CharacterSize = 1f, BoldSize, Width = 5f, Height = 1f, LineSpacing = 1f, OutlineSize;
    public int Alignment, Anchor;
    public Color GradientUpperColor, GradientLowerColor, OutlineColor;
    public bool WordWrap, ResizeToFit, ResizeToFitAndGrow, RichText, Outline, RenderToTexture;
    public GameObject RenderOnObject;
    public Font GetLocalizedFont() => LocalizedFont;
    public void UpdateNow(bool value) { Updated = true; }
    public void Show() { Hidden = false; }
    public void Hide() { Hidden = true; }
    public bool isHidden() => Hidden;
}
public sealed class SourceCaptionReveal : MonoBehaviour { }
public class Spell : Behaviour { }
public class HighlightState : Component { }
public class BoxMenuButton : PegUIElement
{
    public UberText m_TextMesh;
    public MeshRenderer m_ButtonMeshRenderer;
    public Spell m_Spell;
    public HighlightState m_HighlightState;
    public bool m_triggerHighlightStates;
    public Box.ButtonType m_buttonType;
    public void SetText(string value) { m_TextMesh.Text = value; }
}
public sealed class BoxScrollButton : BoxMenuButton
{
    public int m_feature = 10;
    private void UpdateDownloadButtonIcon(bool value) { }
    private void UpdateDownloadProgress(bool value) { }
    public bool IsFeatureActive() => true;
    public bool CanDownloadMode() => true;
}
public sealed class UIBButton : PegUIElement
{
    public UberText m_ButtonText;
    public GameObject m_RootObject;
    public string BubbleUpEvent;
    public bool m_AnimateFlip;
    public float m_WiggleTime, m_DepressTime, m_RaiseTime;
    public void SetText(string text) { m_ButtonText.Text = text; }
}
public sealed class TooltipZone : Component
{
    public GameObject targetObject;
    public Transform tooltipDisplayLocation, touchTooltipLocation;
    public int Hides;
    public void HideTooltip() { Hides++; }
    public void ShowBoxTooltip(string headline, string body) { }
}
public static class GameStrings
{
    public static string Get(string key) => key == "GLOBAL_TAVERN_BRAWL" ? "Tavern Brawl" : key == "GLUE_BACON" ? "Battlegrounds" : key;
}
public sealed class BoxRailroadManager
{
    public bool Hide, Disable;
    public bool ShouldHideButtonType(Box.ButtonType type) => Hide;
    public bool ShouldDisableButtonType(Box.ButtonType type) => Disable;
}
public sealed class Box : MonoBehaviour
{
    public enum ButtonType { BACON, TAVERN_BRAWL, LUCKY_DRAW }
    public enum State { HUB_WITH_DRAWER }
    public static Box Instance;
    public static Box Get() => Instance;
    public BoxScrollButton m_BattleGroundsButton;
    public BoxMenuButton m_CollectionButton;
    public BoxMenuButton m_ArenaButton, m_GameModesButton;
    public LuckyDrawButtonController m_luckyDrawButtonController;
    private bool m_waitingForNetData;
    public bool Waiting { set => m_waitingForNetData = value; }
    public bool InState = true, Transitioning, Drawer = true, Effects;
    public int BrawlClicks, BaconClicks;
    public readonly BoxRailroadManager Railroad = new BoxRailroadManager();
    public BoxRailroadManager GetRailroadManager() => Railroad;
    public bool IsInStateWithButtons() => InState;
    public bool IsTransitioningToSceneMode() => Transitioning;
    public bool ShouldBeShowingState(State state) => Drawer;
    private bool CanEnableUIEvents() => !Effects;
    public void SetBoxButtonInteractions(bool enabled, BoxMenuButton button, bool updateText = false) => button.SetEnabled(enabled && !Railroad.Disable);
    public void UpdateUI() { }
    private void SetupButtonText() { }
    private void UpdateBoxButtons() { }
    private void OnButtonPressed(UIEvent e) { }
    private void OnButtonMouseOver(UIEvent e) { }
    public void OnTavernBrawlButtonPressed(UIEvent e) { BrawlClicks++; }
    public void OnBattleGroundsButtonPressed(UIEvent e)
    {
        BaconClicks++;
        if (BaconNeedsDownload || !Network.IsLoggedIn()) return;
        FireButtonPressEvent(ButtonType.BACON, BaconTutorial);
    }
    public bool BaconNeedsDownload, BaconTutorial;
    public void OnLuckyDrawButtonPressed(UIEvent e) { }
    public void FireButtonPressEvent(ButtonType buttonType, bool isShowingTutorialPreview = false)
        => HubMenuPatches.FireButton(buttonType, isShowingTutorialPreview);
}
public sealed class LuckyDrawButton : Component { }
public sealed class LuckyDrawButtonController : MonoBehaviour
{
    public Action OnReady;
    private bool m_isEnabled = true;
    private LuckyDrawButton m_nativeButton;
    private Hearthstone.UI.Widget m_nativeWidget;
    public int AssetLoads;
    public void Show()
    {
        if (!HubMenuPatches.DarkmoonShow(this, m_isEnabled)) return;
        if (!m_isEnabled) return;
        AssetLoads++;
        m_nativeButton = new GameObject("Heavy animated Darkmoon prefab").AddComponent<LuckyDrawButton>();
        m_nativeWidget = m_nativeButton.gameObject.AddComponent<Hearthstone.UI.WidgetTemplate>();
        OnReady?.Invoke();
    }
    public void Hide()
    {
        HubMenuPatches.DarkmoonHide(this);
        if (m_nativeWidget != null) UnityEngine.Object.Destroy(m_nativeWidget.gameObject);
        m_nativeWidget = null;
        m_nativeButton = null;
    }
    public void SetEnable(bool enabled) { HubMenuPatches.DarkmoonEnable(this, enabled); m_isEnabled = enabled; }
    public void RelayTriggerEvent(string eventName) { HubMenuPatches.DarkmoonRelay(this, eventName); }
    public LuckyDrawButton GetButton()
    {
        LuckyDrawButton result = null;
        return HubMenuPatches.DarkmoonGetButton(this, ref result) ? m_nativeButton : result;
    }
    public Hearthstone.UI.Widget GetWidget()
    {
        Hearthstone.UI.Widget result = null;
        return HubMenuPatches.DarkmoonGetWidget(this, ref result) ? m_nativeWidget : result;
    }
}
public sealed class LuckyDrawManager
{
    public static LuckyDrawManager Instance = new LuckyDrawManager();
    public static LuckyDrawManager Get() => Instance;
    public bool Dirty, Enabled = true;
    public int Preferred = 42;
    public bool IsDataDirty() => Dirty;
    public bool GetPreferredLuckyDrawID(out int id) { id = Preferred; return id > 0; }
    public bool LuckyDrawEnabled(int id, bool enabledInGracePeriod = false) => Enabled;
}
public sealed class TavernBrawlManager
{
    public static TavernBrawlManager Instance = new TavernBrawlManager();
    public static TavernBrawlManager Get() => Instance;
    public bool Available = true;
    public bool CanEnterStandardTavernBrawl(out string reason) { reason = Available ? "" : "Closed"; return Available; }
}
public sealed class SceneMgr
{
    public enum Mode { HUB, GAME_MODE, BACON, COLLECTIONMANAGER }
    public static readonly SceneMgr Instance = new SceneMgr();
    public static SceneMgr Get() => Instance;
    public Mode Current = Mode.HUB;
    public Mode? Next;
    public bool Transition;
    public Mode GetMode() => Current;
    public bool IsTransitionNowOrPending() => Transition;
    public void SetNextMode(Mode mode) { Next = mode; }
}
public sealed class DialogManager
{
    public static readonly DialogManager Instance = new DialogManager();
    public static DialogManager Get() => Instance;
    public bool Showing;
    public Action Reconnect;
    public bool ShowingDialog() => Showing;
    public void ShowReconnectHelperDialog(Action callback) { Reconnect = callback; }
}
public sealed class PopupDisplayManager
{
    public static readonly PopupDisplayManager Instance = new PopupDisplayManager();
    public static PopupDisplayManager Get() => Instance;
    public bool IsShowing;
}
public static class Network { public static bool LoggedIn = true; public static bool IsLoggedIn() => LoggedIn; }
public static class DeepLinkManager
{
    public enum DeepLinkSource { IN_GAME_MESSAGE }
    public static readonly List<string[]> Opened = new List<string[]>();
    public static void ExecuteDeepLink(string[] link, DeepLinkSource source, int index) { Opened.Add(link); }
}
public sealed class GameModeDbfRecord
{
    public int ID;
    public string LinkedScene, GameModeButtonState, Name, Description;
    public bool EventActive;
}
public static class GameDbf
{
    public static readonly Records GameMode = new Records();
    public static readonly PetRecords PetVariant = new PetRecords();
    public sealed class PetRecords
    {
        public readonly Dictionary<int, PetVariantDbfRecord> Values = new Dictionary<int, PetVariantDbfRecord>();
        public PetVariantDbfRecord GetRecord(int id) => Values.TryGetValue(id, out var value) ? value : null;
    }
    public sealed class Records
    {
        public readonly List<GameModeDbfRecord> Values = new List<GameModeDbfRecord>();
        public IEnumerable<GameModeDbfRecord> GetRecords() => Values;
        public GameModeDbfRecord GetRecord(int id) => Values.Find(r => r.ID == id);
    }
}
namespace Hearthstone.Core.Streaming
{
    public static class DownloadTags
    {
        public enum Content { Unknown, Bgs }
        public static string GetTagString(Content tag) => tag.ToString();
    }
}
namespace Hearthstone.UI
{
    public struct TriggerEventParameters { }
    public struct WidgetEventListenerResponse { }
    public abstract class Widget : MonoBehaviour
    {
        public delegate void EventListenerDelegate(string eventName);
        public abstract void RegisterEventListener(EventListenerDelegate listener);
        public abstract void RemoveEventListener(EventListenerDelegate listener);
    }
    public sealed class WidgetTemplate : Widget
    {
        private EventListenerDelegate m_listeners;
        public bool Initialized;
        public void Initialize(bool shouldPreload = false) { Initialized = true; }
        public override void RegisterEventListener(EventListenerDelegate listener)
        { m_listeners -= listener; m_listeners += listener; }
        public override void RemoveEventListener(EventListenerDelegate listener) { m_listeners -= listener; }
        public WidgetEventListenerResponse EventReceived(string eventName, TriggerEventParameters parameters)
        { m_listeners?.Invoke(eventName); return default; }
    }
    public sealed class DataModelList<T> : List<T> { }
    public sealed class VisualController : Component
    {
        public readonly HashSet<string> States = new HashSet<string>();
        public bool HasState(string name) => States.Contains(name);
    }
}
namespace Hearthstone.DataModels
{
    public sealed class LuckyDrawButtonDataModel
    {
        public LuckyDrawFeaturedType ButtonItemType;
        public int ButtonPetVariantID;
        public string ButtonHeroSkinID, ButtonCustomPrefabId;
    }
    public sealed class GameModeButtonDataModel
    {
        public int GameModeRecordId;
        public string Name, Description, ButtonState, ModuleTag;
    }
    public sealed class GameModeSceneDataModel
    {
        public Hearthstone.UI.DataModelList<GameModeButtonDataModel> GameModeButtons = new Hearthstone.UI.DataModelList<GameModeButtonDataModel>();
        public int LastSelectedGameModeRecordId;
    }
}
public sealed class GameModeDisplay : MonoBehaviour
{
    public enum ModeLockedStatus { Locked, Unlocked }
    public bool IsFinishedLoading = true, Allowed = true;
    private List<GameModeDbfRecord> m_activeGameModeRecords = new List<GameModeDbfRecord>();
    private Hearthstone.DataModels.GameModeButtonDataModel m_selectedGameModeButtonDataModel;
    private UIBButton m_backButton;
    public readonly Hearthstone.DataModels.GameModeSceneDataModel Data = new Hearthstone.DataModels.GameModeSceneDataModel();
    public Hearthstone.DataModels.GameModeSceneDataModel GetGameModeSceneDataModel() => Data;
    public void Back(UIBButton button) { m_backButton = button; }
    public void Active(GameModeDbfRecord record) { m_activeGameModeRecords.Add(record); }
    public List<GameModeDbfRecord> ActiveRecords => m_activeGameModeRecords;
    public void Select(Hearthstone.DataModels.GameModeButtonDataModel button) { m_selectedGameModeButtonDataModel = button; }
    private void InitializeGameModeSceneData() { }
    private void NavigateToSelectedMode() { }
    private bool CanEnterMode(out string reason, out ModeLockedStatus lockedStatus)
    { reason = ""; lockedStatus = ModeLockedStatus.Locked; return Allowed; }
    private Hearthstone.Core.Streaming.DownloadTags.Content GetModuleTagForGameMode(GameModeDbfRecord gameModeRecord)
        => Hearthstone.Core.Streaming.DownloadTags.Content.Unknown;
}
namespace Assets { public static class Global { public enum UnlockableGameMode { BATTLEGROUNDS } } }
public static class GameModeUtils
{
    public static bool Unlocked = true;
    public static bool HasUnlockedMode(Assets.Global.UnlockableGameMode mode) => Unlocked;
}
public sealed class NetCache
{
    public sealed class NetCacheFeatures
    {
        public sealed class CacheGames { public bool Enabled = true; public bool GetFeatureFlag(int flag) => Enabled; }
        public readonly CacheGames Games = new CacheGames();
    }
    public static readonly NetCache Instance = new NetCache();
    public readonly NetCacheFeatures Features = new NetCacheFeatures();
    public static NetCache Get() => Instance;
    public T GetNetObject<T>() where T : class => Features as T;
}

public enum LuckyDrawFeaturedType { None, Pet, HeroSkin, Custom, Generic }
public enum LuckyDrawAssetCategory { ENTRY_BUTTON }
public enum TAG_PREMIUM { NORMAL }
public sealed class PlatformAssetID { public string m_platformAssetID; }
public abstract class LibraryAssetBase { }
public sealed class ResponsiveLibraryAsset : LibraryAssetBase
{
    public PlatformAssetID m_desktopAsset, m_mobileAsset;
}
public class AssetReferenceLibrary
{
    public static bool Desktop = true;
    protected string GetDeviceAssetPath(LibraryAssetBase asset)
    {
        var responsive = (ResponsiveLibraryAsset)asset;
        return (Desktop ? responsive.m_desktopAsset : responsive.m_mobileAsset).m_platformAssetID;
    }
}
public sealed class LuckyDrawAssetReferenceLibrary : AssetReferenceLibrary
{
    public readonly Dictionary<string, ResponsiveLibraryAsset> Assets = new Dictionary<string, ResponsiveLibraryAsset>();
    public bool TryGetAsset<T>(T category, string id, out LibraryAssetBase asset, bool validateAsset = false) where T : Enum
    { Assets.TryGetValue(id, out var value); asset = value; return asset != null; }
}
public static class LuckyDrawUtils
{
    public static readonly LuckyDrawAssetReferenceLibrary AssetReferenceLibrary = new LuckyDrawAssetReferenceLibrary();
}
public sealed class PetVariantDbfRecord { public string PetIcon; }
public sealed class CardDef
{
    public string Portrait;
    public string GetPortraitTexturePath(TAG_PREMIUM premium, bool fallback, out TAG_PREMIUM used)
    { used = premium; return Portrait; }
}
public sealed class DefLoader
{
    public sealed class DisposableCardDef : IDisposable
    {
        public CardDef CardDef;
        public void Dispose() { Instance.Disposals++; }
    }
    public static readonly DefLoader Instance = new DefLoader();
    public static DefLoader Get() => Instance;
    public readonly Queue<Action<DisposableCardDef>> Pending = new Queue<Action<DisposableCardDef>>();
    public int Disposals;
    public void LoadCardDefAsync(string cardId, Action<string, DisposableCardDef, object> callback, object userData = null)
    { Pending.Enqueue(definition => callback(cardId, definition, userData)); }
    public void Complete(string portrait)
    { Pending.Dequeue()(new DisposableCardDef { CardDef = new CardDef { Portrait = portrait } }); }
}

namespace BetterCollectionManager
{
    internal static class FilterButtonFeedback
    {
        internal static readonly HashSet<PegUIElement> PressBound = new HashSet<PegUIElement>();
        internal static void BindPressSound(PegUIElement element) => PressBound.Add(element);
    }
}
