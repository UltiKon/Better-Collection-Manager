using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MelonLoader
{
    public class MelonPreferences_Entry<T> { public T Value; }
    public sealed class MelonPreferences_Category
    {
        public readonly Dictionary<string, object> Entries = new Dictionary<string, object>();
        public MelonPreferences_Entry<T> CreateEntry<T>(string key, T value, string description)
        {
            if (Entries.TryGetValue(key, out object existing)) return (MelonPreferences_Entry<T>)existing;
            var entry = new MelonPreferences_Entry<T> { Value = value };
            Entries[key] = entry;
            return entry;
        }
    }
    public static class MelonPreferences
    {
        public static readonly Dictionary<string, MelonPreferences_Category> Categories = new Dictionary<string, MelonPreferences_Category>();
        public static MelonPreferences_Category CreateCategory(string name)
        {
            if (!Categories.TryGetValue(name, out var category)) Categories[name] = category = new MelonPreferences_Category();
            return category;
        }
        public static void Load() { }
        public static void Save() { }
    }
}

public sealed class DeckRule
{
    public enum RuleType { DECK_SIZE, DEATHKNIGHT_RUNE_LIMIT, TOURIST_LIMIT, PLAYER_OWNS_EACH_COPY,
        COUNT_COPIES_OF_EACH_CARD, IS_CARD_PLAYABLE, HAS_TAG_VALUE, IS_CLASS_CARD_OR_NEUTRAL, IS_DUAL_CLASS_CARD_OR_NEUTRAL }
    public RuleType Type;
}
public sealed class RuleInvalidReason { public string DisplayError; }
public sealed class DeckRuleset
{
    public bool Banned, NotPlayable;
    public Func<EntityDef, bool> ClassAllowed;
    public Func<DeckRule.RuleType, string> ErrorText;
    public int Capacity = 30, MaxCopies = 2;
    public bool CanAddToDeck(EntityDef def, TAG_PREMIUM premium, CollectionDeck deck,
        out List<RuleInvalidReason> reasons, out List<DeckRule> broken, params DeckRule.RuleType[] ignore)
    {
        reasons = new List<RuleInvalidReason>(); broken = new List<DeckRule>();
        var failures = new List<DeckRule.RuleType>();
        if (deck.LocalCount(def.CardId) >= (def.Rarity == TAG_RARITY.LEGENDARY ? 1 : MaxCopies)) failures.Add(DeckRule.RuleType.COUNT_COPIES_OF_EACH_CARD);
        if (deck.Slots.Values.Sum() >= Capacity) failures.Add(DeckRule.RuleType.DECK_SIZE);
        CollectionManager manager = CollectionManager.Get();
        if (manager.GetEditedDeck().GetCardCountAllMatchingSlots(def.CardId, premium)
            >= manager.GetOwnedCount(def.CardId, premium) + manager.GetTrialCardCount(def.CardId, premium)) failures.Add(DeckRule.RuleType.PLAYER_OWNS_EACH_COPY);
        if (ClassAllowed != null && !ClassAllowed(def)) failures.Add(DeckRule.RuleType.IS_CLASS_CARD_OR_NEUTRAL);
        if (NotPlayable) failures.Add(DeckRule.RuleType.IS_CARD_PLAYABLE);
        if (Banned) failures.Add(DeckRule.RuleType.HAS_TAG_VALUE);
        foreach (var failure in failures.Where(f => !ignore.Contains(f)))
        {
            broken.Add(new DeckRule { Type = failure });
            reasons.Add(new RuleInvalidReason { DisplayError = ErrorText?.Invoke(failure) ?? failure.ToString() });
        }
        return broken.Count == 0;
    }
}
public sealed class CollectionDeck
{
    public PegasusShared.FormatType FormatType = PegasusShared.FormatType.FT_STANDARD;
    public string Name;
    public List<TAG_CLASS> Classes;
    public List<TAG_CLASS> TouristClasses = new List<TAG_CLASS>();
    public List<TAG_CLASS> GetClasses() => Classes ?? Enum.GetValues(typeof(TAG_CLASS)).Cast<TAG_CLASS>().ToList();
    public List<TAG_CLASS> GetTouristClasses() => TouristClasses;
    public TAG_CLASS GetClass() => GetClasses()[0];
    public readonly Dictionary<(string, TAG_PREMIUM), int> Slots = new Dictionary<(string, TAG_PREMIUM), int>();
    public readonly List<CollectionDeck> Sideboards = new List<CollectionDeck>();
    public int GetCardCountAllMatchingSlots(string id, TAG_PREMIUM premium) =>
        (Slots.TryGetValue((id, premium), out int count) ? count : 0) + Sideboards.Sum(d => d.GetCardCountAllMatchingSlots(id, premium));
    public int LocalCount(string id) => Slots.Where(p => p.Key.Item1 == id).Sum(p => p.Value);
    public void Add(string id, TAG_PREMIUM premium) => Slots[(id, premium)] = GetLocal(id, premium) + 1;
    private int GetLocal(string id, TAG_PREMIUM premium) => Slots.TryGetValue((id, premium), out int value) ? value : 0;
}
public sealed class CollectionDeckTray
{
    public static CollectionDeckTray Instance;
    public static CollectionDeckTray Get() => Instance;
    public CollectionDeck Context;
    public readonly List<TAG_PREMIUM> Added = new List<TAG_PREMIUM>();
    public readonly List<Actor> AnimationSources = new List<Actor>();
    public readonly List<Actor> Animations = new List<Actor>();
    public readonly List<int> AnimationDeckCounts = new List<int>();
    public DeckTrayCardListContent GetCurrentCardListContext(bool includeSideboard = true) => new DeckTrayCardListContent(this);
    public int Suggestions;
    public Action AfterAdd;
    public CollectionDeck GetCurrentDeckContext() => Context;
    public bool MouseIsOver(Camera camera) => CollectionInputMgr.Get().OverDeck;
    public bool AddCard(EntityDef def, TAG_PREMIUM premium, bool sound, Actor actor, params DeckRule.RuleType[] ignore)
    {
        // Native AddCard commits the slot synchronously, before its animation.
        if (!CollectionManager.Get().Rules.CanAddToDeck(def, premium, Context, out _, out _, ignore)) return false;
        Context.Add(def.CardId, premium);
        Added.Add(premium);
        if (actor != null) GetCurrentCardListContext().UpdateCardList(def, true, actor);
        AfterAdd?.Invoke();
        return true;
    }
    public void OnCardManuallyAddedByUser_CheckSuggestions(EntityDef def) { Suggestions++; }
}

// The client captures ID/position when starting the flight, then clones the
// corresponding deck row after it becomes available. The caller owns its source.
public sealed class DeckTrayCardListContent
{
    private readonly CollectionDeckTray m_tray;
    public DeckTrayCardListContent(CollectionDeckTray tray) { m_tray = tray; }
    public void UpdateCardList(EntityDef definition, bool updateHighlight = true, Actor animateFromActor = null, Action onCompleteCallback = null)
    {
        if (animateFromActor == null) { onCompleteCallback?.Invoke(); return; }
        string id = animateFromActor.GetEntityDef().GetCardId();
        if (m_tray.Context.LocalCount(id) == 0) throw new InvalidOperationException("Flight has no matching deck row.");
        m_tray.AnimationSources.Add(animateFromActor);
        m_tray.AnimationDeckCounts.Add(m_tray.Context.Slots.Values.Sum());
        var flight = new GameObject("Native deck flight").AddComponent<Actor>();
        flight.SetPremium(animateFromActor.GetPremium()); flight.SetEntityDef(animateFromActor.GetEntityDef().Clone());
        flight.transform.SetParent(animateFromActor.transform.parent, false);
        flight.transform.localPosition = animateFromActor.transform.localPosition;
        flight.transform.localRotation = animateFromActor.transform.localRotation;
        flight.transform.localScale = animateFromActor.transform.localScale;
        m_tray.Animations.Add(flight); onCompleteCallback?.Invoke();
    }
}

public enum GameLayer { HighPriorityUI, CameraMask = 31 }
public enum CustomViewEntryPoint { PerspectivePostFullscreenFX, OrthographicsPostFullscreenFX }
public enum UIEventType { RELEASE, PRESS, ROLLOVER, ROLLOUT, RELEASEALL }
public sealed class UIEvent { public delegate void Handler(UIEvent e); }
public sealed class PegUIElementProxy : Component
{
    public PegUIElement Owner;
}
public sealed class CheckBox : PegUIElement
{
    public CheckBox() { Enabled = true; }
    public UberText m_uberText;
    public string m_checkOnSound = "Small_Click.prefab:on", m_checkOffSound = "Small_Click.prefab:off";
    private bool m_checked;
    public void SetButtonText(string text) => m_uberText.Text = text;
    public void SetChecked(bool value) => m_checked = value;
    public bool IsChecked() => m_checked;
    public void Click()
    {
        Send(UIEventType.PRESS); m_checked = !m_checked;
        string sound = m_checked ? m_checkOnSound : m_checkOffSound;
        if (!string.IsNullOrEmpty(sound)) SoundManager.Get().LoadAndPlay(sound);
        Send(UIEventType.RELEASE);
    }
    public static CheckBox MakeTemplate(GameObject root = null)
    {
        root = root ?? new GameObject("NativeCheckbox");
        var checkbox = root.AddComponent<CheckBox>();
        var text = new GameObject("CheckboxText");
        text.transform.SetParent(root.transform, false);
        text.transform.localPosition = new Vector3(0.25f, 0f, 0f);
        checkbox.m_uberText = text.AddComponent<UberText>();
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(-1.75f, 0f, 0.1f);
        box.size = new Vector3(0.35f, 0.1f, 0.35f);
        return checkbox;
    }
}
public sealed class UIBButton : PegUIElement
{
    public UIBButton() { Enabled = true; }
    public UIBButton Template;
    public GameObject m_RootObject;
    public Vector3 m_WiggleAmount = new Vector3(90f, 0f, 0f), m_ClickDownOffset = new Vector3(0f, -.05f, 0f);
    public float m_WiggleTime = .5f, m_DepressTime = .1f, m_RaiseTime = .1f;
    public bool m_DepressOnOver, m_AnimateFlip, m_WigglePostFlip, m_HoldDepressionOnRelease;
    public int NativeDepressCalls, NativeRaiseCalls;
    private bool m_depressed;
    private Vector3? m_originalPosition;
    // Captured native UIBButton behavior: the component owns the press state
    // and submits local-position tweens; the mod does not animate in Update.
    private void Depress()
    {
        if (m_RootObject == null || m_depressed) return;
        m_originalPosition = m_originalPosition ?? m_RootObject.transform.localPosition;
        m_depressed = true; NativeDepressCalls++;
        iTween.StopByName(m_RootObject, "depress");
        iTween.MoveTo(m_RootObject, m_originalPosition.Value + m_ClickDownOffset, m_DepressTime);
    }
    private void Raise()
    {
        if (m_RootObject == null || !m_depressed) return;
        m_depressed = false; NativeRaiseCalls++;
        iTween.StopByName(m_RootObject, "depress");
        iTween.MoveTo(m_RootObject, m_originalPosition.Value, m_RaiseTime);
    }
    public void NativeFeedback(UIEventType type)
    {
        if (type == UIEventType.PRESS && !m_DepressOnOver) Depress();
        else if (type == UIEventType.RELEASE && !m_DepressOnOver && !m_HoldDepressionOnRelease) Raise();
        else if (type == UIEventType.ROLLOUT) Raise();
    }
    public void Hover()
    {
        // Captured UIBButton.OnOver calls Wiggle, whose default amount is a
        // 90-degree punch rotation of the artwork root. No click is required.
        if (m_RootObject != null && m_WiggleTime > 0f && m_WiggleAmount.sqrMagnitude > 0f)
            m_RootObject.transform.localRotation = new Quaternion(.7071068f, 0f, 0f, .7071068f);
        if (m_DepressOnOver && m_RootObject != null) m_RootObject.transform.localPosition += m_ClickDownOffset;
    }
    private bool m_enabled = true;
    public new void SetEnabled(bool enabled, bool isInternal = false) { m_enabled = enabled; base.SetEnabled(enabled, isInternal); }
    public new bool IsEnabled() => m_enabled;
    public UberText m_ButtonText;
    public new UIEvent.Handler Release;
    public new void AddEventListener(UIEventType type, UIEvent.Handler callback) { base.AddEventListener(type, callback); if (type == UIEventType.RELEASE) Release += callback; }
    public void SetText(string text) { m_ButtonText.Text = text; }
    public void Click() { Send(UIEventType.PRESS); Release(new UIEvent()); }
}
public static class iTween
{
    public sealed class Move
    {
        public GameObject Object;
        public Vector3 Start, Target;
        public float Time, Started;
    }
    private static readonly List<Move> s_moves = new List<Move>();
    public static Move LastMove;
    public static void StopByName(GameObject target, string name) => s_moves.RemoveAll(move => move.Object == target);
    public static void MoveTo(GameObject target, Vector3 position, float time)
    {
        LastMove = new Move { Object = target, Start = target.transform.localPosition, Target = position, Time = time, Started = UnityEngine.Time.unscaledTime };
        if (time <= 0) target.transform.localPosition = position;
        else s_moves.Add(LastMove);
    }
    public static bool IsAnimating(GameObject target) => s_moves.Any(move => move.Object == target);
    public static void Advance()
    {
        foreach (Move move in s_moves.ToArray())
        {
            if (move.Object.Destroyed) { s_moves.Remove(move); continue; }
            float t = Mathf.Clamp01((UnityEngine.Time.unscaledTime - move.Started) / move.Time);
            move.Object.transform.localPosition = move.Start + (move.Target - move.Start) * t;
            if (t >= 1f) s_moves.Remove(move);
        }
    }
}
public sealed class ButtonListMenuDef : MonoBehaviour
{
    public UIBButton m_templateButton;
    public readonly SettingsFrame m_background = new SettingsFrame(), m_border = new SettingsFrame();
    public UberText m_headerText;
    public MultiSliceElement m_buttonContainer;
    public Transform m_headerMiddle = new GameObject("HeaderMiddle").transform;
    public MultiSliceElement m_header;
    public bool m_useTextSizesInSlices;
}
public sealed class SettingsFrame : Component
{
    public float Width, Height;
    public SettingsFrame()
    {
        gameObject = new GameObject("NativeFrame");
        gameObject.AddComponent<MeshFilter>().sharedMesh = new Mesh();
        gameObject.AddComponent<MeshRenderer>();
    }
    public void UseChildRenderer()
    {
        var renderer = GetComponent<MeshRenderer>();
        var slice = new GameObject("Native background slice"); slice.transform.SetParent(transform, false);
        slice.AddComponent<MeshFilter>().sharedMesh = GetComponent<MeshFilter>().sharedMesh;
        var copy = slice.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
        copy.sortingLayerID = renderer.sortingLayerID; copy.sortingOrder = renderer.sortingOrder;
        UnityEngine.Object.DestroyImmediate(renderer);
    }
    public void SetSize(float width, float height)
    {
        Width = width; Height = height;
        // Native NineSliceElement.SetSize uses WorldDimensionIndex values.
        float localWidth = width / Math.Abs(transform.lossyScale.x);
        float localHeight = height / Math.Abs(transform.lossyScale.z);
        GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(new Vector3(0, 0, -localHeight * .5f), new Vector3(localWidth, .05f, localHeight));
    }
}
public sealed class MultiSliceElement : Component
{
    public int Updates;
    public Action OnUpdateSlices;
    public readonly List<GameObject> Slices = new List<GameObject>();
    public void ClearSlices() => Slices.Clear();
    public void AddSlice(GameObject go, Vector3 minLocalPadding = default, bool useTextSize = false) => Slices.Add(go);
    public void UpdateSlices() { Updates++; OnUpdateSlices?.Invoke(); }
}
public static class LayerUtils
{
    public static void SetLayer(Component component, GameLayer layer)
    { foreach (Transform part in component.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = (int)layer; }
}
public sealed class DropdownMenuItem : PegUIElement
{
    public DropdownMenuItem() { Enabled = true; }
    public UberText m_text;
    public GameObject m_selected;
    public void UpdateTextColor()
    {
        m_text.TextColor = m_selected != null && !m_selected.activeInHierarchy
            ? new Color(0.33f, 0.227f, 0.105f, 1f) : new Color(0.3883f, 0.3386f, 0.2731f, 1f);
    }
}
public sealed class DropdownControl : Component
{
    private bool m_enabled = true;
    public void SetEnabled(bool enabled, bool isInternal = false) => m_enabled = enabled;
    public bool IsEnabled() => m_enabled;
    public delegate void itemChosenCallback(object selection, object previous);
    public delegate string itemTextCallback(object value);
    public delegate void menuShownCallback(bool shown);
    public DropdownMenuItem m_selectedItem, m_menuItemTemplate;
    public UIBButton m_button;
    public GameObject m_menuMiddle;
    public MultiSliceElement m_menu, m_menuItemContainer;
    public Component m_cancelCatcher;
    public readonly List<object> Items = new List<object>();
    private object m_selection;
    private itemChosenCallback m_chosen;
    private itemTextCallback m_text = value => value.ToString();
    private menuShownCallback m_shown;
    public void setItemChosenCallback(itemChosenCallback callback) => m_chosen = callback;
    public void setItemTextCallback(itemTextCallback callback) => m_text = callback;
    public void setMenuShownCallback(menuShownCallback callback) => m_shown = callback;
    public menuShownCallback getMenuShownCallback() => m_shown;
    public void clearItems() { Items.Clear(); BetterCollectionManager.CustomFpsControl.ItemsCleared(this); }
    public void addItem(object value) => Items.Add(value);
    public int SelectionWrites;
    public void setSelection(object value)
    {
        SelectionWrites++;
        m_selection = Items.Contains(value) ? value : null;
        m_selectedItem.m_text.Text = m_selection == null ? "" : m_text(m_selection);
    }
    public object getSelection() => m_selection;
    public bool isMenuShown() => m_menu.gameObject.activeInHierarchy;
    public void onUserPressedButton() { if (m_button.IsEnabled()) { m_menu.gameObject.SetActive(true); m_shown?.Invoke(true); } }
    public void onUserCancelled() { m_menu.gameObject.SetActive(false); m_shown?.Invoke(false); }
    public void SelectOption(object value)
    {
        if (!Items.Contains(value)) throw new Exception("Selecting a hidden dropdown item");
        object previous = m_selection;
        onUserCancelled();
        setSelection(value);
        m_chosen?.Invoke(value, previous);
    }
    public static DropdownControl MakeTemplate(GameObject root = null)
    {
        root = root ?? new GameObject("NativeDropdown");
        var control = root.AddComponent<DropdownControl>();
        GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name); child.transform.SetParent(parent, false); return child;
        }
        control.m_selectedItem = Child("Selected", root.transform).AddComponent<DropdownMenuItem>();
        var fieldMesh = new Mesh { name = "NativeDropdownFieldFixture", subMeshCount = 1,
            bounds = new Bounds { size = new Vector3(4f, 0.1f, 0.7f) },
            vertices = new[] { new Vector3(-2, 0, -.35f), new Vector3(2, 0, -.35f), new Vector3(2, 0, .35f), new Vector3(-2, 0, .35f) } };
        fieldMesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false);
        control.m_selectedItem.gameObject.AddComponent<MeshFilter>().sharedMesh = fieldMesh;
        control.m_selectedItem.gameObject.AddComponent<MeshRenderer>().sharedMaterial = new Material { renderQueue = 4300 };
        control.m_selectedItem.gameObject.AddComponent<BoxCollider>().size = new Vector3(4f, 0.1f, 0.7f);
        control.m_selectedItem.m_text = Child("Text", control.m_selectedItem.transform).AddComponent<UberText>();
        control.m_button = Child("Arrow", root.transform).AddComponent<UIBButton>();
        control.m_menu = Child("Menu", root.transform).AddComponent<MultiSliceElement>();
        control.m_menuMiddle = Child("MenuMiddle", control.m_menu.transform);
        control.m_menuItemContainer = Child("Items", control.m_menu.transform).AddComponent<MultiSliceElement>();
        control.m_menuItemTemplate = Child("Template", control.m_menuItemContainer.transform).AddComponent<DropdownMenuItem>();
        control.m_menuItemTemplate.m_text = Child("Text", control.m_menuItemTemplate.transform).AddComponent<UberText>();
        control.m_cancelCatcher = Child("CancelCatcher", root.transform).AddComponent<MonoBehaviour>();
        control.m_menu.gameObject.SetActive(false);
        return control;
    }
}
public struct WorldDimensionIndex
{
    public WorldDimensionIndex(float dimension, int index) { }
}
public static class TransformUtil
{
    public static void SetLocalScaleToWorldDimension(Transform target, WorldDimensionIndex dimension) { }
    public static void SetLocalScaleToWorldDimension(GameObject target, params WorldDimensionIndex[] dimensions) { }
}
public abstract class ButtonListMenu : MonoBehaviour
{
    protected ButtonListMenuDef m_menu;
    protected Transform m_menuParent;
    protected PegUIElement m_blocker;
    protected bool m_useTextSizesInSlices;
    protected GameLayer m_targetLayer;
    protected bool m_showAnimation = true;
    protected Vector3 NORMAL_SCALE = Vector3.one;
    private bool m_shown;
    private readonly List<UIBButton> m_allButtons = new List<UIBButton>();
    public static ButtonListMenu Visible;
    public static bool NativeShowActivatesController = true;
    public static bool FailNextShowLayout;
    public static bool SimulateNativeOpeningAnimation;
    public bool NativeOpeningAnimationPending { get; private set; }
    public Transform NativeRig => m_menu.transform;
    public List<UIBButton> Buttons => GetButtons();
    public List<UIBButton> ActiveButtons => m_allButtons.Where(b => b.gameObject.activeSelf).ToList();
    public string Header => m_menu.m_headerText.Text;
    public Vector3 AnchorPosition => m_menuParent.position;
    public float BodyHeight => m_menu.m_background.Height;
    public float BodyWidth => m_menu.m_background.Width;
    public SettingsFrame NativeBackground => m_menu.m_background;
    public SettingsFrame NativeBorder => m_menu.m_border;
    protected virtual void Awake()
    {
        OverlayUI.Get().AddGameObject(gameObject);
        var rig = new GameObject("ButtonListMenuDef(Clone)"); rig.transform.SetParent(transform, false);
        m_menu = rig.AddComponent<ButtonListMenuDef>();
        var template = new GameObject("Native menu button template"); template.transform.SetParent(rig.transform, false);
        m_menu.m_templateButton = template.AddComponent<UIBButton>();
        var menuArtwork = new GameObject("Native menu button frame"); menuArtwork.transform.SetParent(template.transform, false);
        menuArtwork.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(3f, .02f, .8f)) };
        menuArtwork.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = "Native dark menu button" };
        m_menu.m_templateButton.m_RootObject = menuArtwork;
        var menuText = new GameObject("Native menu button caption"); menuText.transform.SetParent(template.transform, false);
        m_menu.m_templateButton.m_ButtonText = menuText.AddComponent<UberText>();
        m_menu.m_templateButton.m_ButtonText.RichText = true;
        template.SetActive(false);
        var container = new GameObject("Buttons"); container.transform.SetParent(rig.transform, false);
        var header = new GameObject("MenuHeader"); header.transform.SetParent(rig.transform, false);
        header.AddComponent<MeshFilter>().sharedMesh = new Mesh
            { bounds = new Bounds(new Vector3(0, 0, 0.4f), new Vector3(5f, 0.05f, 0.8f)) };
        header.AddComponent<MeshRenderer>();
        var caption = new GameObject("Header"); caption.transform.SetParent(header.transform, false);
        caption.transform.localPosition = new Vector3(0, 0, 0.4f);
        m_menu.m_headerText = caption.AddComponent<UberText>();
        m_menu.m_header = header.AddComponent<MultiSliceElement>();
        m_menu.m_buttonContainer = container.AddComponent<MultiSliceElement>();
        m_menu.m_background.transform.SetParent(rig.transform, false);
        m_menu.m_border.transform.SetParent(rig.transform, false);
        var blocker = new GameObject("GameMenuInputBlocker"); blocker.transform.SetParent(transform, false);
        blocker.AddComponent<BoxCollider>().size = new Vector3(Screen.width, .01f, Screen.height);
        m_blocker = blocker.AddComponent<PegUIElement>(); m_blocker.SetEnabled(true, true);
        m_blocker.AddEventListener(UIEventType.RELEASE, OnBlockerRelease);
    }
    protected void SetTransform()
    {
        if (m_menuParent == null) m_menuParent = transform;
        m_menu.transform.SetParent(m_menuParent, false);
        m_menu.transform.localScale = Vector3.one;
        if (m_blocker != null)
            m_blocker.transform.position = CameraUtils.Camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, Screen.height * .5f, 1f));
    }
    public UIBButton CreateMenuButton(string name, string key, UIEvent.Handler handler, UIBButton template)
    {
        var button = CreateMenuButton(name, key, handler);
        button.Template = template;
        if (template.GetComponent<MeshFilter>() is MeshFilter sourceMesh)
        {
            button.m_RootObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh.sharedMesh;
            button.m_RootObject.AddComponent<MeshRenderer>().sharedMaterials = template.GetComponent<MeshRenderer>().sharedMaterials;
        }
        button.transform.localRotation = template.transform.localRotation;
        button.m_ButtonText.transform.localRotation = template.m_ButtonText.transform.localRotation;
        return button;
    }
    public UIBButton CreateMenuButton(string name, string key, UIEvent.Handler handler)
    {
        var go = new GameObject(name);
        go.transform.SetParent(m_menu.m_buttonContainer.transform, false);
        var button = go.AddComponent<UIBButton>();
        var artwork = new GameObject("NativeButtonArtworkRoot"); artwork.transform.SetParent(go.transform, false);
        button.m_RootObject = artwork;
        var text = new GameObject("ButtonText"); text.transform.SetParent(go.transform, false);
        button.m_ButtonText = text.AddComponent<UberText>();
        if (name == "BCM_EditCombo" || name == "BCM_DeleteCombo" || name == "BCM_DeletePackage"
            || name == "BCM_EditComboCards" || name == "BCM_ExportEntry" || name == "BCM_AddNewFolder")
        {
            button.Template = m_menu.m_templateButton;
            artwork.AddComponent<MeshFilter>().sharedMesh = m_menu.m_templateButton.m_RootObject.GetComponent<MeshFilter>().sharedMesh;
            artwork.AddComponent<MeshRenderer>().sharedMaterials = m_menu.m_templateButton.m_RootObject.GetComponent<MeshRenderer>().sharedMaterials;
        }
        if (name.StartsWith("BCM_ComboMemberMinus") || name.StartsWith("BCM_ComboMemberPlus"))
        {
            var mesh = new Mesh { name = "NativeWideMenuButton", subMeshCount = 1,
                vertices = new[] { new Vector3(-2, 0, -.4f), new Vector3(2, 0, -.4f), new Vector3(2, 0, .4f), new Vector3(-2, 0, .4f) } };
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false); mesh.RecalculateBounds();
            artwork.AddComponent<MeshFilter>().sharedMesh = mesh; artwork.AddComponent<MeshRenderer>();
        }
        if (name.StartsWith("BCM_Choose") || name == "BCM_QualityCancel" || name.StartsWith("BCM_Transfer") || name.StartsWith("BCM_Disenchant"))
        {
            go.AddComponent<BoxCollider>().size = new Vector3(3f, .05f, .8f);
            artwork.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(3f, .05f, .8f)) };
            artwork.AddComponent<MeshRenderer>();
        }
        if (name == "BCM_ComboName" || name == "BCM_ComboDescription")
        {
            var frame = new GameObject("Native field frame"); frame.transform.SetParent(artwork.transform, false);
            frame.AddComponent<MeshFilter>().sharedMesh = new Mesh {
                bounds = new Bounds(Vector3.zero, new Vector3(4f, .02f, .6f)) };
            frame.AddComponent<MeshRenderer>();
            go.AddComponent<BoxCollider>().size = new Vector3(4f, .02f, .6f);
            var output = new GameObject("Native button caption texture"); output.transform.SetParent(go.transform, false);
            output.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(1f, .02f, .2f)) };
            output.AddComponent<MeshRenderer>();
            button.m_ButtonText.RenderToTexture = true; button.m_ButtonText.RenderOnObject = output;
        }
        button.Release = handler;
        m_allButtons.Add(button);
        return button;
    }
    protected static void MakeButtonRed(UIBButton button, Material material)
    {
        foreach (Renderer renderer in button.m_RootObject.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
        button.m_ButtonText.TextColor = Color.white;
    }
    protected abstract List<UIBButton> GetButtons();
    protected virtual void OnDestroy() { }
    protected virtual void OnBlockerRelease(UIEvent e) => Hide();
    protected void LayoutMenuBackground() { }
    protected virtual void LayoutMenu()
    {
        int index = 0;
        foreach (var button in GetButtons())
        {
            button.gameObject.SetActive(true);
            if (this is BetterCollectionManager.QualityChoiceMenu) button.transform.localPosition = new Vector3(0, 0, -1.2f * index++);
        }
    }
    public virtual void Show(bool sound = true)
    {
        SetTransform();
        if (NativeShowActivatesController) gameObject.SetActive(true);
        Visible = this;
        UniversalInputManager.Get().SetGameDialogActive(true);
        foreach (var button in m_allButtons) button.gameObject.SetActive(false);
        if (FailNextShowLayout) { FailNextShowLayout = false; throw new InvalidOperationException("Native layout failure before IsShown"); }
        LayoutMenu();
        m_shown = true;
        // Captured native Show calls ShowWithPunch after virtual LayoutMenu.
        // The next frame still has HIDDEN_SCALE; the tween later restores 1.
        NativeOpeningAnimationPending = SimulateNativeOpeningAnimation && m_showAnimation;
        if (NativeOpeningAnimationPending) m_menu.transform.localScale = Vector3.one * .001f;
    }
    public void CompleteNativeOpeningAnimation()
    {
        if (NativeOpeningAnimationPending) m_menu.transform.localScale = Vector3.one;
        NativeOpeningAnimationPending = false;
    }
    public virtual void Hide()
    {
        m_shown = false; gameObject.SetActive(false);
        if (Visible == this) Visible = null;
        UniversalInputManager.Get().SetGameDialogActive(false);
    }
    public bool IsShown() => m_shown;
}
public sealed class GameMenu : Component
{
    private static readonly GameMenu s_instance = new GameMenu();
    public Transform m_menuBone = new GameObject("MenuBone").transform;
    public Material m_redButtonMaterial = new Material { name = "Native Concede red", renderQueue = 3100 };
    public static GameMenu Get() => s_instance;
}
public sealed class OptionsMenu : MonoBehaviour
{
    public DropdownControl m_signatureTextDisplayDropdown = DropdownControl.MakeTemplate(), m_graphicsQuality, m_graphicsFps;
    public CheckBox m_screenShakeCheckbox = CheckBox.MakeTemplate(), m_fullScreenCheckbox;
    public int Shows;
    private bool m_shown;
    public bool IsShown() => m_shown;
    public void Show() { Shows++; m_shown = true; UniversalInputManager.Get().SetGameDialogActive(true); }
    public void Hide(bool callHideHandler = true) { m_shown = false; UniversalInputManager.Get().SetGameDialogActive(false); }
}
public sealed class SceneMgr
{
    public enum Mode { COLLECTION, FATAL_ERROR }
    private static readonly SceneMgr s_instance = new SceneMgr();
    public static SceneMgr Get() => s_instance;
    public Mode GetNextMode() => Mode.COLLECTION;
}
namespace BetterCollectionManager
{
    internal static class KeywordFilterApplier
    {
        public static int ApplyInPlace(List<CollectibleCard> cards)
            => cards.RemoveAll(card => !KeywordFilterState.Matches(card) || !ExtraFilterState.MatchesExcept(card, FilterTrayKind.None) || !ExpansionFilterState.Matches(card));
        public static CollectionPageAnchor CapturePageAnchor()
        {
            var page = CollectionManager.Get()?.Display;
            return page == null ? null : CollectionPageAnchor.Capture(page.m_cardsCollection, page.CurrentPageNum);
        }
        public static int Refreshes;
        public static System.Action OnRefresh;
        public static CollectionPageAnchor LastAnchor;
        public static CollectionPageAnchor LastTransitionFrom;
        public static int? LastRequestedPage;
        public static void RefreshCollectionPages(CollectionPageAnchor anchor = null, int? page = null, CollectionPageAnchor transitionFrom = null)
        { Refreshes++; LastAnchor = anchor; LastTransitionFrom = transitionFrom; LastRequestedPage = page; OnRefresh?.Invoke(); }
    }
}
