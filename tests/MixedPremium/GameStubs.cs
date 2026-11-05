using System.Collections.Generic;

public enum TAG_CLASS { DRUID = 2, HUNTER = 3, MAGE = 4, PALADIN = 5, PRIEST = 6, ROGUE = 7, SHAMAN = 8, WARLOCK = 9, WARRIOR = 10, NEUTRAL = 12, DEMONHUNTER = 14, DEATHKNIGHT = 1 }
public enum TAG_PREMIUM { NORMAL, GOLDEN, DIAMOND, SIGNATURE }
public enum TAG_CARDTYPE { MINION, SPELL, WEAPON, HERO, LOCATION }
public enum TAG_RACE { INVALID, MURLOC, DEMON, MECHANICAL, ELEMENTAL, BEAST, TOTEM, PIRATE, DRAGON, ALL, QUILBOAR, NAGA, UNDEAD, DRAENEI }
public enum TAG_SPELL_SCHOOL { NONE, ARCANE, FIRE, FROST, NATURE, HOLY, SHADOW, FEL }
public enum TAG_RARITY { FREE, COMMON, RARE, EPIC, LEGENDARY }
public enum GAME_TAG { RARITY, CARDTYPE, CLASS, MULTIPLE_CLASSES, HIDE_WATERMARK, ELITE, CARDRACE, SPELL_SCHOOL, ATK, HEALTH, CARDRACE2, ARMOR, COLLECTIBLE, COST_BLOOD, COST_FROST, COST_UNHOLY }
public sealed class EntityDef
{
    public TAG_RARITY Rarity;
    public string CardId, Name;
    public string GetName() => Name;
    public string GetCardId() => CardId;
    private Dictionary<GAME_TAG,int> tags = new Dictionary<GAME_TAG,int>();
    public int GetTag(GAME_TAG tag) => tag == GAME_TAG.RARITY ? (int)Rarity : tags.TryGetValue(tag, out int value) ? value : 0;
    public void SetTag(GAME_TAG tag, int value) { if (tag == GAME_TAG.RARITY) Rarity = (TAG_RARITY)value; else tags[tag] = value; }
    public void GetClasses(IList<TAG_CLASS> classes)
    {
        classes.Clear();
        uint multiple = (uint)GetTag(GAME_TAG.MULTIPLE_CLASSES);
        if (multiple == 0) { if (GetTag(GAME_TAG.CLASS) != 0) classes.Add((TAG_CLASS)GetTag(GAME_TAG.CLASS)); return; }
        for (int value = 1; multiple != 0; value++, multiple >>= 1)
            if ((multiple & 1) != 0) classes.Add((TAG_CLASS)value);
    }
    public EntityDef Clone() => new EntityDef { Rarity = Rarity, CardId = CardId, Name = Name, tags = new Dictionary<GAME_TAG,int>(tags) };
}
public interface ICollectible { }
public sealed class CollectibleCard : ICollectible
{
    public TAG_CARD_SET Set;
    private string m_cardId;
    public string CardId {
        get {
            if (NativeComboRuntime.Active == null) return m_cardId;
            string result = null;
            NativeComboRuntime.Invoke(typeof(CollectibleCard).GetProperty(nameof(CardId)).GetGetMethod(), this, System.Array.Empty<object>(), _ => result = m_cardId);
            return result;
        }
        set => m_cardId = value;
    }
    public TAG_PREMIUM PremiumType;
    private string m_name;
    public string Name
    {
        get => NativeComboRuntime.Invoke(typeof(CollectibleCard).GetProperty(nameof(Name)).GetGetMethod(),
            this, System.Array.Empty<object>(), _ => m_name);
        set => m_name = value;
    }
    public TAG_RARITY Rarity;
    public bool IsCraftable = true;
    private System.DateTime m_LatestInsertDate;
    public System.DateTime LatestInsertDate { set { m_LatestInsertDate = value; } }
    public int SeenCount;
    public bool IsNewCard => OwnedCount > 0 && SeenCount < OwnedCount && SeenCount < (Rarity == TAG_RARITY.LEGENDARY ? 1 : 2);
    public bool IsNewCollectible => IsNewCard;
    public bool Refundable;
    public int OwnedCount;
    private int m_manaCost;
    public int ManaCost
    {
        get => NativeComboRuntime.Invoke(typeof(CollectibleCard).GetProperty(nameof(ManaCost)).GetGetMethod(),
            this, System.Array.Empty<object>(), _ => m_manaCost);
        set => m_manaCost = value;
    }
    public int TrialCount;
    public EntityDef Definition;
    public EntityDef GetEntityDef()
    {
        if (NativeComboRuntime.Active == null) return Definition ?? new EntityDef { Rarity = Rarity, CardId = CardId };
        EntityDef result = null;
        NativeComboRuntime.Invoke(typeof(CollectibleCard).GetMethod(nameof(GetEntityDef)), this, System.Array.Empty<object>(),
            _ => result = Definition ?? new EntityDef { Rarity = Rarity, CardId = CardId });
        return result;
    }
}
public sealed class CollectionManager
{
    public sealed class FindCardsResult { public List<CollectibleCard> m_cards = new List<CollectibleCard>(); }
    public struct CardModification
    {
        public enum ModificationType { Add, Remove }
        public ModificationType modificationType;
        public string cardID;
        public TAG_PREMIUM premium;
        public int count;
    }
    public void OnCardsModified(List<CardModification> changes) { }
    public static CollectionManager Instance;
    public static CollectionManager Get() => Instance;
    public bool Loaded = true;
    public bool IsFullyLoaded() => Loaded;
    public int GetAllCardsCalls;
    public List<CollectibleCard> GetAllCards() { GetAllCardsCalls++; return new List<CollectibleCard>(Cards.Values); }
    public readonly Dictionary<(string, TAG_PREMIUM), int> Counts = new Dictionary<(string, TAG_PREMIUM), int>();
    public readonly Dictionary<(string, TAG_PREMIUM), CollectibleCard> Cards = new Dictionary<(string, TAG_PREMIUM), CollectibleCard>();
    public CollectibleCard GetCard(string cardId, TAG_PREMIUM premium) => Cards.TryGetValue((cardId, premium), out CollectibleCard card) ? card : null;
    public int GetOwnedCount(string cardId, TAG_PREMIUM premium) => Counts.TryGetValue((cardId, premium), out int count) ? count : 0;
    public readonly Dictionary<(string, TAG_PREMIUM), int> Trials = new Dictionary<(string, TAG_PREMIUM), int>();
    public int GetTrialCardCount(string id, TAG_PREMIUM premium) => Trials.TryGetValue((id, premium), out int count) ? count : 0;
    public DeckRuleset Rules = new DeckRuleset();
    public DeckRuleset GetDeckRuleset() => Rules;
    public PegasusShared.FormatType Theme = PegasusShared.FormatType.FT_STANDARD;
    public PegasusShared.FormatType GetThemeShowing() => Theme;
    public bool Editing;
    public CollectionDeck EditedDeck;
    public bool IsInEditMode() => Editing;
    public CollectionDeck GetEditedDeck() => EditedDeck;
    public readonly TestDisplay Display = new TestDisplay();
    public TestDisplay GetCollectibleDisplay() => Display;
}
public class BookPageManager : CollectibleDisplay
{
    public enum PageTransitionType { NONE, SINGLE_PAGE_LEFT, SINGLE_PAGE_RIGHT }
    public bool FullyLoaded = true;
    public bool IsFullyLoaded() => FullyLoaded;
    public int m_currentPageNum = 1;
    public UnityEngine.GameObject m_pageLeftArrowBone = new UnityEngine.GameObject("Native left arrow bone");
    public UnityEngine.GameObject m_pageRightArrowBone = new UnityEngine.GameObject("Native right arrow bone");
}
public class CollectionPageManager : BookPageManager
{
    public bool Turning;
    public bool ArePagesTurning() => Turning;
    public static UnityEngine.Color ColorForClass(TAG_CLASS value) => new UnityEngine.Color((int)value / 16f, .25f, .5f, 1f);
    public string m_deckTemplatePickerPrefab;
    public DeckTemplatePicker Picker;
    public DeckTemplatePicker GetDeckTemplatePicker() => Picker;
}
public sealed class TestDisplay : CollectionPageManager
{
    public object m_cardsCollection;
    public int CurrentPageNum { get => m_currentPageNum; set => m_currentPageNum = value; }
    public int LockRefreshes, PageRefreshes;
    public void UpdateCurrentPageCardLocks() { LockRefreshes++; }
    public override TestDisplay GetPageManager() => this;
    public void RefreshCurrentPageContents() { PageRefreshes++; }
}
public class CollectibleDisplay
{
    public delegate void CollectionActorsReadyCallback(List<CollectionCardActors> actors, List<ICollectible> other, object data);
    public bool Ready = true;
    public bool IsReady() => Ready;
    public bool BookOpened = true;
    public bool IsBookOpened() => BookOpened;
    public virtual CollectionPageManager GetPageManager() => this as CollectionPageManager;
    public int SearchCalls;
    public bool IgnoreSearch;
    public string LastSearch;
    public virtual void FilterBySearchText(string text)
    {
        SearchCalls++; LastSearch = text;
        if (!IgnoreSearch && this is TestDisplay display && display.m_cardsCollection is CollectibleCardFilter filter) filter.FilterSearchText(text);
    }
    public CollectionUtils.ViewMode ViewMode;
    public CollectionUtils.ViewSubmode Submode;
    public CollectionUtils.ViewMode GetViewMode() => ViewMode;
    public CollectionUtils.ViewSubmode GetViewSubmode() => Submode;
}
public static class CollectionUtils
{
    public enum ViewMode { CARDS, HERO_SKINS, CARD_BACKS, DECK_TEMPLATE, MASS_DISENCHANT, COINS,
        BATTLEGROUNDS_GUIDE_SKINS, BATTLEGROUNDS_HERO_SKINS, HERO_PICKER, BATTLEGROUNDS_BOARD_SKINS,
        BATTLEGROUNDS_FINISHERS, BATTLEGROUNDS_EMOTES, PETS, COUNT }
    public enum ViewSubmode { DEFAULT, CARD_ZILLIAX_MODULES }
}
public enum ActorStateType { CARD_IDLE, CARD_MOUSE_OVER, CARD_RECENTLY_ACQUIRED, CARD_RECENTLY_ACQUIRED_MOUSE_OVER }
public sealed class UberText : UnityEngine.MonoBehaviour
{
    public string Text = "native";
    public float Width = 0.4f;
    public bool ResizeToFit, ResizeToFitAndGrow, WordWrap, RichText, GameStringLookup;
    public enum AlignmentOptions { Left, Center, Right }
    public enum AnchorOptions { Upper, Middle, Lower }
    public AlignmentOptions Alignment;
    public AnchorOptions Anchor;
    public float CharacterSize = 1f;
    public UnityEngine.Font TrueTypeFont;
    private readonly UnityEngine.Font m_localizedFont = new UnityEngine.Font();
    public UnityEngine.Font GetLocalizedFont() => m_localizedFont;
    public float BoldSize;
    private int m_renderQueue;
    public int RenderQueue { get => m_renderQueue; set => m_renderQueue = System.Math.Min(value, 2000); }
    private bool m_hidden;
    public void Show() { m_hidden = false; UpdateNow(true); }
    public void Hide() { m_hidden = true; UpdateNow(true); }
    public bool isHidden() => m_hidden;
    public int FontSize = 35;
    public bool RenderToTexture, TextGeometryReady = true, SimulateNativeQueue, TextureRefreshDeferred;
    public float GlyphOffset, SurfaceOffset;
    public UnityEngine.GameObject RenderOnObject;
    public UnityEngine.Color TextColor = UnityEngine.Color.white;
    public UnityEngine.Color GradientUpperColor = UnityEngine.Color.white, GradientLowerColor = UnityEngine.Color.white;
    private UberTextRenderToTexture m_UberTextRenderToTexture;
    private UnityEngine.MeshRenderer m_glyphRenderer;
    public UnityEngine.Bounds LayoutBounds;
    public UnityEngine.Bounds GetBounds() => new UnityEngine.Bounds
    {
        center = transform.TransformPoint(LayoutBounds.center),
        size = LayoutBounds.size * System.Math.Abs(transform.WorldScale)
    };
    private float InkWidth()
    {
        if (!TextGeometryReady) return 0f;
        if (ResizeToFit && Width <= 0f) return 0f;
        float width = 0, maximum = 0, factor = 1;
        string text = Text ?? "";
        for (int i = 0; i < text.Length; i++)
        {
            if (RichText && text[i] == '<')
            {
                int end = text.IndexOf('>', i);
                string tag = text.Substring(i, end - i + 1);
                if (tag.StartsWith("<size=")) factor = float.Parse(tag.Substring(6, tag.Length - 7)) / FontSize;
                else if (tag == "</size>") factor = 1;
                i = end;
            }
            else if (text[i] == '\n') { maximum = System.Math.Max(maximum, width); width = 0; }
            else width += 0.15f * factor;
        }
        return System.Math.Max(maximum, width) * CharacterSize;
    }
    public UnityEngine.Bounds GetTextWorldSpaceBounds() => new UnityEngine.Bounds
    {
        center = RenderToTexture ? new UnityEngine.Vector3(-3000f + GlyphOffset, 3000f + (Text == "+" ? 0.03f : 0.1f) * CharacterSize, 0)
            : transform.TransformPoint(new UnityEngine.Vector3(GlyphOffset, 0, (Text == "+" ? 0.03f : 0.1f) * CharacterSize)),
        size = new UnityEngine.Vector3(InkWidth() * (RenderToTexture ? 1f : System.Math.Abs(transform.WorldScale)),
            0.2f * CharacterSize, 0.2f * CharacterSize * System.Math.Abs(transform.WorldScale) * (Text ?? "").Split('\n').Length)
    };
    public UnityEngine.Bounds GetTextBounds() => GetTextWorldSpaceBounds();
    public int UpdateCalls;
    private sealed class NativeRendering
    {
        public UnityEngine.Renderer Renderer;
        public UnityEngine.Renderer GetTextMeshRenderer() => Renderer;
    }
#pragma warning disable CS0414
    private NativeRendering m_UberTextRendering;
#pragma warning restore CS0414
    public void UpdateNow(bool force)
    {
        UpdateCalls++;
        if (RenderToTexture && TextureRefreshDeferred) return;
        if (m_hidden)
        {
            if (m_glyphRenderer != null) m_glyphRenderer.enabled = false;
            m_UberTextRenderToTexture?.Hide();
            if (RenderOnObject?.GetComponent<UnityEngine.Renderer>() is UnityEngine.Renderer target) target.enabled = false;
            return;
        }
        if (!RenderToTexture)
        {
            m_UberTextRenderToTexture?.Hide();
            if (m_glyphRenderer == null)
            {
                var glyphs = new UnityEngine.GameObject("NativeFontGlyphs");
                glyphs.transform.SetParent(transform, false);
                m_glyphRenderer = glyphs.AddComponent<UnityEngine.MeshRenderer>();
            }
            m_glyphRenderer.transform.SetParent(transform, false);
            m_glyphRenderer.enabled = true;
            m_UberTextRendering = new NativeRendering { Renderer = m_glyphRenderer };
            if (SimulateNativeQueue || gameObject.name.StartsWith("BCM_Combo"))
            {
                if (m_glyphRenderer.sharedMaterial == null || m_glyphRenderer.sharedMaterial.renderQueue != 3000 + RenderQueue)
                    m_glyphRenderer.sharedMaterial = new UnityEngine.Material { name = "Native font", renderQueue = 3000 + RenderQueue };
                m_glyphRenderer.sortingOrder = RenderQueue;
            }
            return;
        }
        if (m_glyphRenderer != null) m_glyphRenderer.enabled = false;
        if (m_UberTextRenderToTexture == null) m_UberTextRenderToTexture = new UberTextRenderToTexture(this);
        m_UberTextRenderToTexture.Update(this);
        if (RenderOnObject?.GetComponent<UnityEngine.Renderer>() is UnityEngine.Renderer output) output.enabled = true;
    }
}
public static class UberTextSetup
{
    public sealed class Config { public object UberTextRenderTextureTracker = new object(); }
    public static Config GetConfig() => new Config();
}
#pragma warning disable CS0414
public sealed class UberTextRenderToTexture
{
    private UnityEngine.MeshRenderer m_planeMeshRenderer;
    private UnityEngine.Matrix4x4 viewMatrix, projectionMatrix;
    private UnityEngine.GameObject CaptureTarget;
    public static int CaptureCalls;
    public static UnityEngine.Vector2Int LastCaptureSize;
    public static UnityEngine.MeshRenderer LastCaptureSource;
    private UnityEngine.RenderTexture m_renderTexture = new UnityEngine.RenderTexture();
    public UberTextRenderToTexture(object tracker) { }
    public void InitOnRenderObject(UnityEngine.GameObject target, UnityEngine.Vector2Int size) { CaptureTarget=target; LastCaptureSize=size; }
    public void SetTargetClearColor(UnityEngine.Color color) { }
    public void SetRenderQueueIncrement(int queue) { }
    public void DoRenderToTexture(UnityEngine.MeshRenderer renderer) { CaptureCalls++; LastCaptureSource=renderer; }
    public void Destroy() { }
    public UberTextRenderToTexture(UberText label)
    {
        var plane = new UnityEngine.GameObject("NativeTextSurface");
        plane.transform.SetParent(label.transform, false);
        plane.AddComponent<UnityEngine.MeshFilter>();
        m_planeMeshRenderer = plane.AddComponent<UnityEngine.MeshRenderer>();
        m_planeMeshRenderer.UseLocalBounds = true;
    }
    public void Update(UberText label)
    {
        m_planeMeshRenderer.enabled = true;
        m_planeMeshRenderer.LocalBounds = new UnityEngine.Bounds
        {
            center = new UnityEngine.Vector3(label.SurfaceOffset, 0, 0),
            size = new UnityEngine.Vector3(label.Width, 0.2f, label.Width)
        };
        var bounds = m_planeMeshRenderer.LocalBounds;
        var mesh = new UnityEngine.Mesh { name = "NativeTextPlaneFixture", subMeshCount = 1, vertices = new[] {
            bounds.min, new UnityEngine.Vector3(bounds.max.x, bounds.min.y, bounds.min.z), bounds.max,
            new UnityEngine.Vector3(bounds.min.x, bounds.max.y, bounds.max.z) }, bounds = bounds };
        mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false);
        mesh.SetUVs(0, new List<UnityEngine.Vector4> { new UnityEngine.Vector4(0, 0, 0, 0), new UnityEngine.Vector4(1, 0, 0, 0),
            new UnityEngine.Vector4(1, 1, 0, 0), new UnityEngine.Vector4(0, 1, 0, 0) });
        m_planeMeshRenderer.GetComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
        viewMatrix = new UnityEngine.Matrix4x4 { Scale = 1, Offset = new UnityEngine.Vector3(3000, -3000, 0) };
        projectionMatrix = new UnityEngine.Matrix4x4 { Scale = 2 / label.Width };
    }
    public void Hide() => m_planeMeshRenderer.enabled = false;
}
#pragma warning restore CS0414
public sealed class ProjectedShadow : UnityEngine.MonoBehaviour { }
public sealed class GemObject : UnityEngine.Component { }
public struct RunePattern
{
    public int Blood, Frost, Unholy;
    public RunePattern(EntityDef definition)
    { Blood = definition.GetTag(GAME_TAG.COST_BLOOD); Frost = definition.GetTag(GAME_TAG.COST_FROST); Unholy = definition.GetTag(GAME_TAG.COST_UNHOLY); }
}
public sealed class CardRuneBanner : UnityEngine.Component
{
    public RunePattern Current;
    public void Show(RunePattern pattern) { Current = pattern; gameObject.SetActive(true); }
    public void Hide() { gameObject.SetActive(false); }
}
public sealed class Actor : UnityEngine.Component
{
    public TAG_PREMIUM Premium;
    public EntityDef Def;
    public ActorStateType State;
    public bool IgnoreRepeatedState;
    public System.Action<ActorStateType> StateChanged;
    public UberText RulesText;
    public UberText NameText, CostText, AttackText, HealthText, RaceText;
    public GemObject AttackGem, HealthGem;
    public UnityEngine.GameObject m_attackObject { get => AttackGem?.gameObject; set => AttackGem = value?.GetComponent<GemObject>(); }
    public UnityEngine.GameObject m_healthObject { get => HealthGem?.gameObject; set => HealthGem = value?.GetComponent<GemObject>(); }
    public UberText m_attackTextMesh { get => AttackText; set => AttackText = value; }
    public UberText m_healthTextMesh { get => HealthText; set => HealthText = value; }
    public UnityEngine.GameObject m_rarityGemMesh, m_rarityFrameMesh, m_rarityNoGemMesh, m_eliteObject, m_nameBannerMesh, m_manaObject;
    public UnityEngine.GameObject m_armorObject;
    public UberText m_armorTextMesh;
    public CardRuneBanner m_cardRuneBanner;
    public UnityEngine.MeshRenderer GetMeshRenderer() => GetComponent<UnityEngine.MeshRenderer>();
    public UnityEngine.GameObject PortraitMesh;
    public int m_portraitMatIdx;
    public UnityEngine.GameObject GetPortraitMesh() => PortraitMesh;
    public UnityEngine.Material GetPortraitMaterial() => PortraitMesh?.GetComponent<UnityEngine.Renderer>()?.sharedMaterials[m_portraitMatIdx];
    public UberText GetPowersTextObject() => RulesText;
    public UberText GetNameText() => NameText;
    public UberText GetCostText() => CostText;
    public UberText GetAttackText() => AttackText;
    public UberText GetHealthText() => HealthText;
    public GemObject GetAttackObject() => AttackGem;
    public GemObject GetHealthObject() => HealthGem;
    public void SetPremium(TAG_PREMIUM premium) { Premium = premium; }
    public TAG_PREMIUM GetPremium() => Premium;
    public void SetEntityDef(EntityDef def) { Def = def; }
    public EntityDef GetEntityDef() => Def;
    public void UpdateAllComponents()
    {
        if (m_cardRuneBanner != null && Def != null)
        {
            var pattern = new RunePattern(Def);
            if (pattern.Blood + pattern.Frost + pattern.Unholy > 0) m_cardRuneBanner.Show(pattern);
            else m_cardRuneBanner.Hide();
        }
    }
    private bool m_shown = true;
    public void Hide() { m_shown = false; if (GetMeshRenderer() != null) GetMeshRenderer().enabled = false; }
    public void Show() { if (!m_shown && GetMeshRenderer() != null) GetMeshRenderer().enabled = true; m_shown = true; }
    public void SetActorState(ActorStateType state) { if (IgnoreRepeatedState && State == state) return; State = state; StateChanged?.Invoke(state); }
    public ActorStateType GetActorStateType() => State;
    public void Destroy() { gameObject.SetActive(false); Destroyed = true; }
}
public sealed class CollectionCardActors
{
    private List<Actor> m_cardActors;
    public Actor Primary { get => m_cardActors.Count == 0 ? null : m_cardActors[0]; set { m_cardActors.Clear(); m_cardActors.Add(value); } }
    public CollectionCardActors(Actor primary) { m_cardActors = new List<Actor> { primary }; }
    public void AddCardActor(Actor actor) => m_cardActors.Add(actor);
    public Actor GetPreferredActor()
    {
        // Simulates the single Harmony postfix; production actor selection is linked.
        return BetterCollectionManager.MixedPremiumCard.TryGetSelected(this, out Actor actor) ? actor : Primary;
    }
}
public class CollectionCardVisual : UnityEngine.Component
{
    private PegUIElement.InteractionState m_interactionState;
    public PegUIElement.InteractionState GetInteractionState() => m_interactionState;
    public void SetInteractionState(PegUIElement.InteractionState state) => m_interactionState = state;
    public enum LockType { NONE, MAX_COPIES_IN_DECK, NO_MORE_INSTANCES, NOT_PLAYABLE, BANNED }
    public bool Shown = true;
    public bool m_lastClickLeft = true;
    public CollectionUtils.ViewMode ViewMode;
    private CollectionCardActors m_actors;
    public CollectionCardActors Actors { get => m_actors; set => m_actors = value; }
    public void SetActors(CollectionCardActors actors, CollectionUtils.ViewMode mode)
    {
        NativeComboRuntime.Invoke(typeof(CollectionCardVisual).GetMethod(nameof(SetActors)), this, new object[] { actors, mode }, args => {
            if (EmulateNativeActorCache && ReferenceEquals(m_actors, args[0])) return;
            m_actors = IgnoreNextSetActorsReplacement ? actors : (CollectionCardActors)args[0];
            IgnoreNextSetActorsReplacement = false;
            ViewMode = (CollectionUtils.ViewMode)args[1];
            CachedRendererActor = m_actors?.GetPreferredActor();
            if (EmulateNativeActorCache) CachedRendererActor?.gameObject.SetActive(false);
            if (FailNextSetActors) { FailNextSetActors = false; throw new System.InvalidOperationException("Native renderer binding failed"); }
            if (NativeComboRuntime.Active != null) BetterCollectionManager.ComboCollectionCards.Apply(this);
        });
    }
    public bool FailNextSetActors;
    public bool IgnoreNextSetActorsReplacement;
    public Actor CachedRendererActor;
    public bool EmulateNativeActorCache;
    public int NativeActorShows, NativeActorHides;
    public System.Action<Actor> OnNativeActorShown, OnNativeActorHidden;
    public void Hide()
    {
        NativeComboRuntime.Invoke(typeof(CollectionCardVisual).GetMethod(nameof(Hide)), this, System.Array.Empty<object>(), _ => {
            if (EmulateNativeActorCache && !Shown) return;
            Shown = false;
            if (EmulateNativeActorCache)
            {
                NativeActorHides++; CachedRendererActor?.gameObject.SetActive(false);
                OnNativeActorHidden?.Invoke(CachedRendererActor);
            }
        });
    }
    public void Show()
    {
        NativeComboRuntime.Invoke(typeof(CollectionCardVisual).GetMethod(nameof(Show)), this, System.Array.Empty<object>(), _ => {
            if (EmulateNativeActorCache && Shown) return;
            Shown = true;
            if (EmulateNativeActorCache)
            {
                NativeActorShows++; CachedRendererActor?.gameObject.SetActive(true);
                OnNativeActorShown?.Invoke(CachedRendererActor);
            }
            else GetActor()?.gameObject.SetActive(true);
        });
    }
    public CollectionCardCount m_count;
    public UnityEngine.Component m_cardLock;
    public UnityEngine.GameObject m_newCardCallout, m_favoriteBanner;
    public UnityEngine.Vector3 m_boxColliderCenter = new UnityEngine.Vector3(0, 0.14f, 0);
    public UnityEngine.Vector3 m_boxColliderSize = new UnityEngine.Vector3(2, 0.21f, 2.7f);
    public int CountRefreshes;
    public bool m_disableCraftingMode, IgnoreInput, PendingTransaction, ThrowOnInspect;
    public int NativeLeftCalls, NativeDeckActions, NativeInspectCalls;
    public Actor InspectedActor;
    public bool SawRoutingFlag;
    public void NativeLeftRelease()
    {
        NativeLeftCalls++;
        if (CollectionManager.Get().IsInEditMode() || CollectionManager.Get().GetEditedDeck() != null) NativeDeckActions++;
    }
    private bool ShouldIgnoreAllInput() => IgnoreInput || (CollectionInputMgr.Get()?.DraggingScrollbar ?? false);
    protected virtual void OnRightClick()
    {
        // Simulates the production mixed-premium right-click prefix, followed
        // by the game's native transaction guard and preview implementation.
        if (!BetterCollectionManager.CollectionClickInspect.IsRoutingLeftClick)
            BetterCollectionManager.MixedPremiumCard.Choose(this, false, false);
        if (PendingTransaction) return;
        if (ThrowOnInspect) throw new System.InvalidOperationException("Native preview failed");
        NativeInspectCalls++;
        InspectedActor = GetActor();
        SawRoutingFlag = BetterCollectionManager.CollectionClickInspect.IsRoutingLeftClick;
    }
    private void OnRelease() { NativeLeftRelease(); }
    private void OnDrag()
    {
        NativeComboRuntime.Invoke((System.Reflection.MethodInfo)System.Reflection.MethodBase.GetCurrentMethod(), this,
            System.Array.Empty<object>(), _ => { CollectionInputMgr.Get().GrabCardVisual(this); });
    }
    public LockType LastLock;
    public string LastLockReason;
    public int LockShows, LockSounds;
    public void ShowLock(LockType type, string reason, bool playSound, CollectionDeck deck)
    {
        if (playSound && type != LockType.NONE) LockSounds++;
        LastLock = type; LastLockReason = reason; LockShows++;
        if (m_cardLock == null && type != LockType.NONE)
        {
            var go = new UnityEngine.GameObject("NativeCardLock"); go.transform.SetParent(transform, false);
            m_cardLock = go.AddComponent<CollectionCardLock>();
            ((CollectionCardLock)m_cardLock).Init();
        }
        if (m_cardLock is CollectionCardLock nativeLock) nativeLock.UpdateReason(reason);
        m_cardLock?.gameObject.SetActive(type != LockType.NONE);
    }
    public TAG_PREMIUM LastCountPremium;
    public bool IsShown() => Shown;
    public CollectionUtils.ViewMode GetVisualType() => ViewMode;
    public string CardId => GetActor().Def.CardId;
    public CollectionCardActors GetCollectionCardActors() => Actors;
    public Actor GetActor() => Actors.GetPreferredActor();
    public void OnDoneCrafting()
    {
        CountRefreshes++; LastCountPremium = GetActor().GetPremium();
        if (m_count != null) m_count.SetNative(CollectionManager.Get().GetOwnedCount(CardId, LastCountPremium), m_count.Trial);
    }
    public static void ShowActorShadow(Actor actor, bool show)
    {
        foreach (UnityEngine.Transform child in actor.GetComponentsInChildren<UnityEngine.Transform>(true))
            if (child.gameObject.CompareTag("FakeShadow") || child.gameObject.CompareTag("FakeShadowUnique"))
                child.GetComponent<UnityEngine.Renderer>().enabled = show && child.gameObject.CompareTag("FakeShadow");
    }
}
public sealed class CollectionCardCount : UnityEngine.Component
{
    public UnityEngine.GameObject m_normalTab, m_normalBorder, m_normalWideBorder, m_trialTab;
    public UnityEngine.GameObject m_trialWideBorder, m_trialAndOwnedBorder;
    public UberText m_normalCountText;
    public UnityEngine.Color m_standardTextColor = new UnityEngine.Color(0.3f, 0.2f, 0.1f, 1f);
    public UnityEngine.Color m_wildTextColor = new UnityEngine.Color(0.4f, 0.25f, 0.15f, 1f);
    public UnityEngine.Color m_classicTextColor = new UnityEngine.Color(0.5f, 0.3f, 0.2f, 1f);
    public UnityEngine.Material m_standardBorderMaterial = new UnityEngine.Material();
    public UnityEngine.Material m_wildBorderMaterial = new UnityEngine.Material();
    public UnityEngine.Material m_classicBorderMaterial = new UnityEngine.Material();
    public int Owned, Trial;
    private UnityEngine.Transform m_centerBoneToUse;
    public void Init(UnityEngine.Transform center) { m_centerBoneToUse = center; }
    public int GetCount() => Owned;
    public int GetTrialCount() => Trial;
    public void SetNative(int owned, int trial) { Owned = owned; Trial = trial; Show(); }
    public void Show()
    {
        if (Owned <= 1 && Trial == 0)
        {
            Hide();
            BetterCollectionManager.CollapsedPremiumCount.Refresh(this);
            return;
        }
        gameObject.SetActive(true);
        m_normalCountText.TextColor = CollectionManager.Get().Theme == PegasusShared.FormatType.FT_WILD
            ? m_wildTextColor : CollectionManager.Get().Theme == PegasusShared.FormatType.FT_CLASSIC
            ? m_classicTextColor : m_standardTextColor;
        m_normalTab.SetActive(Owned > 1);
        m_normalBorder.SetActive(Owned < 10);
        m_normalWideBorder.SetActive(Owned >= 10);
        m_normalCountText.Text = Owned > 9 ? "9+" : "x" + Owned;
        if (m_centerBoneToUse != null) m_normalTab.transform.position = m_centerBoneToUse.position;
        m_trialTab?.SetActive(Trial > 0);
        BetterCollectionManager.CollapsedPremiumCount.Refresh(this);
    }
    public void Hide() { gameObject.SetActive(false); }
}
public static class ActorNames
{
    public static string GetHandActor(EntityDef def, TAG_PREMIUM premium) => premium.ToString();
    public static string LastTemplateIdentity;
    public static string GetHandActor(TAG_CARDTYPE type, TAG_PREMIUM premium, string cardId = null)
    { LastTemplateIdentity = cardId; return premium.ToString(); }
}
namespace Assets { public enum AssetLoadingOptions { IgnorePrefabPosition } }
public sealed class AssetLoader
{
    public static readonly AssetLoader Instance = new AssetLoader();
    public static AssetLoader Get() => Instance;
    public System.Func<UnityEngine.Mesh> MeshFactory;
    public readonly Dictionary<string, UnityEngine.GameObject> Assets = new Dictionary<string, UnityEngine.GameObject>();
    public Actor LastActor;
    public int InstantiateCalls;
    public readonly UnityEngine.GameObject ProductInfoPrefab = StoreSendToBAM.MakePrefab();
    public readonly UnityEngine.GameObject QuantityPrefab = StoreQuantityPrompt.MakePrefab();
    public readonly UnityEngine.GameObject FlavorPrefab = CardInfoPane.MakePrefab();
    public Blizzard.T5.AssetManager.AssetHandle<T> LoadAsset<T>(string asset, Assets.AssetLoadingOptions options = default) where T : UnityEngine.Object
    {
        UnityEngine.GameObject prefab;
        if (Assets.TryGetValue(asset, out prefab)) { }
        else if (asset == "NORMAL") prefab = NativeCardPartsFixture.MakePrefab();
        else if (asset == ShopPrefabs.ShopSendToBamPrefab) prefab = ProductInfoPrefab;
        else if (asset == "CardListPopup.prefab:fixture") prefab = CardListPanel.MakePrefab();
        else if (asset == "HeroPickerLeft.prefab:fixture" || asset == "HeroPickerRight.prefab:fixture") prefab = CardListPanel.MakeArrow(asset.Contains("Left"));
        else if (asset == "GameMenu.prefab:dc76cbcfb64a34d7e93755df33db2f80")
        { prefab = new UnityEngine.GameObject("GameMenu native asset"); prefab.SetActive(false); prefab.AddComponent<GameMenu>(); }
        else if (asset == "CraftingUI.prefab:ef05b5bf5ebb14a22919f0095d75f0b2") prefab = CraftingUI.MakePrefab();
        else if (asset == "OptionsMenu.prefab:a6e5621068fd7c8429475b3e1a1aa991")
        {
            prefab = new UnityEngine.GameObject("OptionsMenu"); prefab.SetActive(false); prefab.AddComponent<OptionsMenu>();
        }
        else if (asset == AdjustBuyingAmount.Asset) prefab = AdjustBuyingAmount.MakePrefab();
        else if (asset == StoreQuantityPrompt.Asset) prefab = QuantityPrefab;
        else if (asset == "CardInfoPane.prefab:b9220edd61d504be38fab162c18e56f1") prefab = FlavorPrefab;
        else if (asset == "MainShopWidget.prefab:debaa5f123c09694eaca007ce8375d8d")
        {
            prefab = new UnityEngine.GameObject("MainShopWidget");
            prefab.AddComponent<Hearthstone.UI.WidgetInstance>().Reference(AdjustBuyingAmount.Asset);
        }
        else throw new System.Exception("Unexpected native skin asset: " + asset);
        return new Blizzard.T5.AssetManager.AssetHandle<T> { Asset = (T)(UnityEngine.Object)prefab };
    }
    public bool CreateRulesText, OffsetFrames;
    public static void AttachRulesText(Actor actor)
    {
        foreach (string tag in new[] { "FakeShadow", "FakeShadowUnique", "actor_ContactShadowRenderPlane" })
        {
            var shadow = new UnityEngine.GameObject(tag) { tag = tag.StartsWith("Fake") ? tag : "Untagged" };
            shadow.transform.SetParent(actor.transform, false);
            shadow.AddComponent<UnityEngine.MeshFilter>().sharedMesh = new UnityEngine.Mesh
                { name = tag, isReadable = false, FailGpuRead = true };
            shadow.AddComponent<UnityEngine.MeshRenderer>();
        }
        actor.gameObject.AddComponent<ProjectedShadow>().enabled = true;
        actor.RulesText = AttachLabel(actor, "RulesText");
        actor.NameText = AttachLabel(actor, "Name");
        actor.CostText = AttachLabel(actor, "Mana");
        actor.m_manaObject = new UnityEngine.GameObject("ManaGem");
        actor.m_manaObject.transform.SetParent(actor.transform, false);
        actor.AttackText = AttachLabel(actor, "Attack");
        actor.HealthText = AttachLabel(actor, "Health");
        actor.RaceText = AttachLabel(actor, "Race");
        var attack = new UnityEngine.GameObject("AttackGem");
        attack.transform.SetParent(actor.transform, false);
        actor.AttackGem = attack.AddComponent<GemObject>();
        var health = new UnityEngine.GameObject("HealthGem");
        health.transform.SetParent(actor.transform, false);
        actor.HealthGem = health.AddComponent<GemObject>();
        var rarity = new UnityEngine.GameObject("RarityGem");
        rarity.transform.SetParent(actor.transform, false);
        actor.m_rarityGemMesh = rarity;
        rarity.AddComponent<UnityEngine.MeshRenderer>().LocalBounds = new UnityEngine.Bounds
        {
            center = new UnityEngine.Vector3(0, 0, -0.35f * CameraUtils.Camera.UpDirection),
            size = new UnityEngine.Vector3(0.1f, 0.1f, 0.12f)
        };
        rarity.GetComponent<UnityEngine.Renderer>().UseLocalBounds = true;
        var banner = new UnityEngine.GameObject("NameBanner");
        banner.transform.SetParent(actor.transform, false);
        actor.m_nameBannerMesh = banner;
        banner.AddComponent<UnityEngine.MeshFilter>().sharedMesh = new UnityEngine.Mesh
            { name = "NativeNameBanner", isReadable = false, FailGpuRead = true };
        var bannerRenderer = banner.AddComponent<UnityEngine.MeshRenderer>();
        bannerRenderer.UseLocalBounds = true;
        bannerRenderer.LocalBounds = new UnityEngine.Bounds
        {
            center = new UnityEngine.Vector3(0, 0, -0.1f * CameraUtils.Camera.UpDirection),
            size = new UnityEngine.Vector3(1.4f, 0.1f, 0.4f)
        };
        actor.RulesText.LayoutBounds = new UnityEngine.Bounds
        {
            center = new UnityEngine.Vector3(0, 0, -0.85f * CameraUtils.Camera.UpDirection),
            size = new UnityEngine.Vector3(1.2f, 0.1f, 0.6f)
        };
    }
    private static UberText AttachLabel(Actor actor, string name)
    {
        var text = new UnityEngine.GameObject(name);
        text.transform.SetParent(actor.transform, false);
        var label = text.AddComponent<UberText>();
        text.AddComponent<UnityEngine.MeshFilter>().sharedMesh =
            new UnityEngine.Mesh { name = "Rebuilt" + name, isReadable = false, FailGpuRead = true };
        text.AddComponent<UnityEngine.MeshRenderer>();
        return label;
    }
    public UnityEngine.GameObject InstantiatePrefab(string name, Assets.AssetLoadingOptions options)
    {
        InstantiateCalls++;
        if (name == "PagingArrow.prefab:70d4430ff418d6d42a943e77dc98d523") return PagingArrow.MakePrefab();
        var go = new UnityEngine.GameObject(name);
        LastActor = go.AddComponent<Actor>();
        UnityEngine.Mesh mesh = MeshFactory();
        if (OffsetFrames)
        {
            float offset = name == "GOLDEN" ? -0.035f : 0.015f;
            for (int i = 0; i < mesh.vertices.Length; i++) mesh.vertices[i].x += offset;
            mesh.bounds = new UnityEngine.Bounds
            {
                center = new UnityEngine.Vector3(offset, 0, 0), size = new UnityEngine.Vector3(2, 0.1f, 2.7f)
            };
        }
        go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
        var frame = go.AddComponent<UnityEngine.MeshRenderer>();
        frame.UseLocalBounds = true;
        frame.LocalBounds = mesh.bounds;
        go.AddComponent<UnityEngine.Collider>();
        if (CreateRulesText) AttachRulesText(LastActor);
        return go;
    }
}
public static class CameraUtils
{
    public static readonly UnityEngine.Camera Camera = new UnityEngine.Camera();
    public static bool Unavailable;
    public static System.Func<UnityEngine.GameObject, UnityEngine.Camera> Projection;
    public static UnityEngine.Camera FindProjectionCameraForObject(UnityEngine.GameObject obj) => Unavailable ? null : Projection?.Invoke(obj) ?? Camera;
    public static UnityEngine.Camera FindFirstByLayer(GameLayer layer) => Unavailable ? null : Camera;
}
public class CollectionInputMgr
{
    public static event System.Action<CollectionCardVisual> CollectionDraggableCardGrabbed;
    public static event System.Action CollectionDraggableCardDropped;
    public static CollectionInputMgr Instance = new CollectionInputMgr();
    public static CollectionInputMgr Get() => Instance;
    public bool HoldingCard, DraggingScrollbar;
    public bool OverDeck;
    public CollectionDraggableCardVisual Held;
    public int DropCalls;
    public bool PublishGrabBeforeHeld;
    public bool DeferHeldPublication;
    public bool LastDropCanceled;
    protected bool MouseIsOverDeck => OverDeck;
    public bool HasHeldCard() => HoldingCard;
    public CollectionDraggableCardVisual GetHeldCardVisual() => Held;
    public void NativeGrab(CollectionCardVisual source)
    {
        if (DeferHeldPublication) { CollectionDraggableCardGrabbed?.Invoke(source); return; }
        if (PublishGrabBeforeHeld) CollectionDraggableCardGrabbed?.Invoke(source);
        Held = new CollectionDraggableCardVisual { Definition = source.GetActor().GetEntityDef(), Premium = source.GetActor().GetPremium(), ViewMode = source.GetVisualType() };
        HoldingCard = true;
        if (!PublishGrabBeforeHeld) CollectionDraggableCardGrabbed?.Invoke(source);
    }
    public bool GrabCard(CollectionCardVisual source)
        => NativeComboRuntime.Invoke((System.Reflection.MethodInfo)System.Reflection.MethodBase.GetCurrentMethod(), this,
            new object[] { source }, _ => { NativeGrab(source); return true; });
    public bool GrabCardVisual(CollectionCardVisual source)
        => NativeComboRuntime.Invoke((System.Reflection.MethodInfo)System.Reflection.MethodBase.GetCurrentMethod(), this,
            new object[] { source }, _ => { NativeGrab(source); return true; });
    protected void DropCard(bool canceled)
    {
        DropCalls++;
        LastDropCanceled = canceled;
        if (!canceled && OverDeck && Held != null)
            CollectionDeckTray.Get().AddCard(Held.Definition, Held.Premium, true, null, new[] { DeckRule.RuleType.DEATHKNIGHT_RUNE_LIMIT });
        HoldingCard = false;
        Held = null;
        CollectionDraggableCardDropped?.Invoke();
    }
}
public sealed class CollectionDraggableCardVisual
{
    public EntityDef Definition;
    public TAG_PREMIUM Premium;
    public object Slot;
    public CollectionUtils.ViewMode ViewMode;
    public EntityDef GetCurrentlyShowingEntityDef() => Definition;
    public object GetSlot() => Slot;
    public CollectionUtils.ViewMode GetVisualType() => ViewMode;
}
public sealed class Box
{
    private static readonly Box s_instance = new Box();
    public static Box Get() => s_instance;
    public UnityEngine.Camera GetCamera() => CameraUtils.Camera;
}
public sealed class UniversalInputManager
{
    public static UniversalInputManager Instance = new UniversalInputManager();
    public bool Touch;
    public int DialogCount { get; private set; }
    public bool DialogActive => DialogCount > 0;
    // Native Hearthstone counts modal claims; Show/Hide must be balanced.
    public void SetGameDialogActive(bool active) => DialogCount = System.Math.Max(0, DialogCount + (active ? 1 : -1));
    public bool IsDialogActive() => DialogActive;
    public static UniversalInputManager Get() => Instance;
    public bool IsTouchMode() => Touch;
    public sealed class TextInputParams
    {
        public UnityEngine.GameObject m_owner;
        public bool m_number, m_showBackground, m_touchScreenKeyboardHideInput, m_multiLine;
        public string m_text;
        public int m_maxCharacters;
        public UnityEngine.Rect m_rect;
        public UnityEngine.Font m_font;
        public UnityEngine.TextAnchor? m_alignment;
        public GameLayer? m_gameLayer;
        public int? m_orderInLayer;
        public UnityEngine.Color? m_color;
        public System.Action<string> m_updatedCallback, m_completedCallback;
        public System.Action<bool, UnityEngine.GameObject> m_canceledCallback;
        public System.Action m_unfocusedCallback;
    }
    public TextInputParams CurrentTextInput;
    public InputFieldUI NativeTextField = new InputFieldUI();
    public bool TextInputReady = true;
    public string InputText;
    public bool IsTextInputActive() => CurrentTextInput != null;
    public void UseTextInput(TextInputParams parameters, bool force = false)
    {
        if (!TextInputReady || (!force && CurrentTextInput?.m_owner == parameters.m_owner)) return;
        if (CurrentTextInput != null && CurrentTextInput.m_owner != parameters.m_owner)
        {
            var old = CurrentTextInput; CurrentTextInput = null;
            old.m_canceledCallback?.Invoke(false, parameters.m_owner);
        }
        CurrentTextInput = parameters; InputText = parameters.m_text ?? "";
        NativeTextField.Field.lineType = parameters.m_multiLine ? HSInputField.LineType.MultiLineNewline : HSInputField.LineType.SingleLine;
        NativeTextField.Field.textComponent.resizeTextForBestFit = false;
        NativeGuiPass();
    }
    public void CancelTextInput(UnityEngine.GameObject requester, bool force = false)
    {
        if (CurrentTextInput == null || (!force && CurrentTextInput.m_owner != requester)) return;
        var previous = CurrentTextInput; CurrentTextInput = null;
        if (previous.m_owner != requester) previous.m_canceledCallback?.Invoke(false, requester);
    }
    public void UpdateNativeText(string text)
    {
        InputText = text; CurrentTextInput?.m_updatedCallback?.Invoke(text);
    }
    public void CompleteNativeText(string text)
    {
        UpdateNativeText(text);
        var previous = CurrentTextInput; CurrentTextInput = null;
        previous?.m_completedCallback?.Invoke(InputText);
        InputText = ""; // Native manager clears this after callback dispatch.
    }
    public void CancelNativeText()
    {
        var previous = CurrentTextInput; CurrentTextInput = null;
        previous?.m_canceledCallback?.Invoke(true, null);
    }
    public void UnfocusNativeText() => CurrentTextInput?.m_unfocusedCallback?.Invoke();
    public static HarmonyLib.Harmony ComboInputHooks;
    // Captured native flow: every GUI pass derives fontSize from rect.height
    // and calls InputFieldUI.SetupTextProperties. HSInputField is the game's
    // own implementation, not UnityEngine.UI.InputField.
    private int ComputeTextInputFontSize(float rectHeight)
    {
        var method = typeof(UniversalInputManager).GetMethod(nameof(ComputeTextInputFontSize), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (ComboInputHooks != null && ComboInputHooks.Hooks.TryGetValue(method, out var hook))
        {
            object[] args = { CurrentTextInput?.m_owner, rectHeight };
            hook.Prefix.Method.Invoke(null, args); rectHeight = (float)args[1];
        }
        return System.Math.Max(4, System.Math.Min(96, (int)System.Math.Ceiling(rectHeight) - 4));
    }
    private bool ProcessTextInputFinishKeys()
    {
        var method = typeof(UniversalInputManager).GetMethod(nameof(ProcessTextInputFinishKeys), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (ComboInputHooks != null && ComboInputHooks.Hooks.TryGetValue(method, out var hook))
        {
            object[] args = { CurrentTextInput?.m_owner, false };
            if (!(bool)hook.Prefix.Method.Invoke(null, args)) return (bool)args[1];
        }
        return UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Return) || UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.KeypadEnter)
            || UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape);
    }
    public void NativeGuiPass()
    {
        if (CurrentTextInput == null) return;
        NativeTextField.Field.textComponent.fontSize = ComputeTextInputFontSize(CurrentTextInput.m_rect.height * UnityEngine.Screen.height + 1.5f);
        NativeTextField.Field.textComponent.alignment = CurrentTextInput.m_alignment ?? UnityEngine.TextAnchor.MiddleLeft;
    }
    public bool NativeFinishKeys() => ProcessTextInputFinishKeys();
}
public sealed class OverlayUI
{
    private static readonly OverlayUI s_instance = new OverlayUI();
    public static OverlayUI Get() => s_instance;
    public InputFieldUI m_inputFieldUI = new InputFieldUI();
    public readonly UnityEngine.GameObject Root = new UnityEngine.GameObject("NativeOverlayUI");
    public void AddGameObject(UnityEngine.GameObject go) => go.transform.SetParent(Root.transform, false);
    public bool HasObject(UnityEngine.GameObject go) => go.transform.IsChildOf(Root.transform);
}
public sealed class InputFieldUI
{
#pragma warning disable CS0414
    private UnityEngine.UI.Image m_inputFieldBackgroundImage = new UnityEngine.UI.Image();
#pragma warning restore CS0414
    public HSInputField Field = new HSInputField();
}
public sealed class HSInputField
{
    public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }
    public LineType lineType { get; set; }
    public UnityEngine.UI.Text textComponent { get; } = new UnityEngine.UI.Text();
}
namespace UnityEngine.UI
{
    public sealed class Image
    {
        public UnityEngine.Sprite sprite = new UnityEngine.Sprite();
        public UnityEngine.Texture mainTexture => sprite?.texture;
        public UnityEngine.Color color = UnityEngine.Color.white;
        public float pixelsPerUnit = 1;
        public UnityEngine.Material material = new UnityEngine.Material { renderQueue = 4300 };
    }
    public sealed class Text
    {
        public bool resizeTextForBestFit { get; set; } = true;
        public int fontSize { get; set; } = 100;
        public UnityEngine.TextAnchor alignment { get; set; }
    }
}
public sealed class CraftingTray
{
    public static CraftingTray Instance;
    public bool Shown;
    public static CraftingTray Get() => Instance;
    public bool IsShown() => Shown;
}
public sealed class CraftingManager
{
    public static CraftingManager Instance = new CraftingManager();
    public static CraftingManager Get() => Instance;
    public static bool GetIsInCraftingMode() => false;
    public CraftingPendingTransaction Transaction = new CraftingPendingTransaction();
    public CraftingPendingTransaction ServerTransaction;
    public Actor Shown;
    public bool Cancelling;
    public int Dust, CloseCalls;
    public sealed class CardValue { public int OverrideEvent; }
    public int RefundEvent = 1;
    public static CardValue GetCardValue(string id, TAG_PREMIUM premium) => new CardValue { OverrideEvent = Get().RefundEvent };
    public string SubmittedCardId;
    public CraftingPendingTransaction Submitted;
    public Dictionary<TAG_PREMIUM, int> SellValues = new Dictionary<TAG_PREMIUM, int>
        { [TAG_PREMIUM.NORMAL] = 5, [TAG_PREMIUM.GOLDEN] = 50, [TAG_PREMIUM.DIAMOND] = 100, [TAG_PREMIUM.SIGNATURE] = 100 };
    public bool IsCancelling() => Cancelling;
    public bool IsCardShowing() => Shown != null;
    public bool GetShownCardInfo(out EntityDef def, out TAG_PREMIUM premium)
    { def = Shown?.Def; premium = Shown?.Premium ?? TAG_PREMIUM.NORMAL; return def != null; }
    public CraftingPendingTransaction GetPendingClientTransaction() => Transaction;
    public CraftingPendingTransaction GetPendingServerTransaction() => ServerTransaction;
    public bool IsPendingTransactionForCard(string id) => Transaction?.CardID == id;
    public int GetNumOwnedIncludePending(string id, TAG_PREMIUM? premium)
        => (CollectionManager.Get().GetCard(id, premium.Value)?.OwnedCount ?? 0) + Transaction.GetTransactionAmount(premium.Value);
    public bool TryGetCardSellValue(string id, TAG_PREMIUM premium, out int sell) => SellValues.TryGetValue(premium, out sell);
    public void AdjustUnCommitedArcaneDustChanges(int amount) => Dust += amount;
    public bool CancelCraftMode()
    { CloseCalls++; SubmittedCardId = Transaction.CardID; Submitted = Transaction; Transaction = null; Cancelling = true; return true; }
    public void EnterCraftMode(Actor actor, System.Action callback = null)
    {
        Shown = actor;
        Navigation.Push(() => { bool result = CancelCraftMode(); callback?.Invoke(); return result; });
    }
    public void CompleteClose() { Cancelling = false; Shown = null; }
    public void DisenchantButtonPressed() { }
    public void OnCardDisenchanted(Network.CardSaleResult result) { }
}
public static class Network { public static bool LoggedIn = true; public static bool IsLoggedIn() => LoggedIn; public sealed class CardSaleResult { public string AssetName; } }
public class CollectibleCardFilter
{
    public System.Func<string, List<CollectibleCard>> Query;
    public CollectionManager.FindCardsResult GenerateResults() => new CollectionManager.FindCardsResult { m_cards = Query?.Invoke(m_filterText) ?? new List<CollectibleCard>() };
    public void FilterSearchText(string text) => m_filterText = text;
    public int? m_filterOwnedMinimum = 1;
    public bool? m_craftableFilterValue;
    public string m_filterText;
    public List<int> m_filterMasks = new List<int> { -1 };
    public TAG_CARD_SET[] m_filterCardSets;
    public CollectionManager.FindCardsResult FindCardsResult { get; set; }
}
public sealed class CollectibleCardClassFilter : CollectibleCardFilter
{
    public Blizzard.T5.Core.Map<TAG_CLASS, List<CollectibleCard>> m_currentResultsByClass = new Blizzard.T5.Core.Map<TAG_CLASS, List<CollectibleCard>>();
    public List<CollectibleCard> m_unfilteredDeathKnightCards = new List<CollectibleCard>(), m_hiddenDeathKnightCards = new List<CollectibleCard>();
    public int m_cardsPerPage = 8;
}
namespace Blizzard.T5.Core { public sealed class Map<TKey, TValue> : Dictionary<TKey, TValue> { } }
namespace BetterCollectionManager
{
    internal static class PreferredQualityMode
    {
        public static bool IsEnabled = true;
        public enum SkipReason { None }
        public static bool ShouldCollapse(CollectibleCardClassFilter filter, out SkipReason reason, out string detail)
        { reason = SkipReason.None; detail = null; return IsEnabled && PreferredPremiumPrefs.CollapseEnabled; }
    }
    internal static class PreferredPremiumPrefs
    {
        public static bool CollapseEnabled = true;
        public static TAG_PREMIUM[] Order = { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
        public static void EnsureLoaded() { }
        public static void SetPriority(int rank, TAG_PREMIUM premium) => PreferredPremiumHelper.MoveToPriority(Order, rank, premium);
    }
    internal static class DebugLog
    {
        public static readonly System.Collections.Generic.List<string> ExpansionStarMessages = new System.Collections.Generic.List<string>();
        public static readonly System.Collections.Generic.List<string> PageTraceMessages = new System.Collections.Generic.List<string>();
        public static bool VerboseDebug = false;
        public static void Verbose(string text) { }
        public static void Msg(string text)
        {
            if (text.StartsWith("[Expansion star]", System.StringComparison.Ordinal)) ExpansionStarMessages.Add(text);
            if (text.StartsWith("[Page trace]", System.StringComparison.Ordinal)) PageTraceMessages.Add(text);
        }
        public static void Error(string text) { }
        public static void Warning(string text) { }
        public static readonly List<string> Exceptions = new List<string>();
        public static void Exception(string text, System.Exception ex) { Exceptions.Add(text + ": " + ex); }
    }
}

namespace PegasusShared
{
    public enum FormatType { FT_STANDARD, FT_WILD, FT_CLASSIC, FT_UNKNOWN }
    // The real game exposes this name alongside UnityEngine.Vector2.
    public sealed class Vector2 { }
}

// Native scene constructor stores the installed collection prefab reference.
public sealed class CollectionManagerScene
{
    public readonly string Prefab;
    public CollectionManagerScene() { Prefab = "CollectionManager.prefab:native-scrollbar-test"; }
}
public sealed class NestedPrefab : UnityEngine.Component
{
    public string m_Prefab;
}
public sealed class NestedPrefabPlatformOverride : UnityEngine.Component
{
    public string m_Prefab;
}
