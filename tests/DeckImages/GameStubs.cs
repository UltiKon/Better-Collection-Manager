public enum TAG_CLASS { MAGE = 4, PALADIN = 5 }
public enum TAG_PREMIUM { NORMAL, GOLDEN, DIAMOND, SIGNATURE }
public enum GAME_TAG { COST, CARDTYPE, RARITY }
public enum TAG_CARDTYPE { HERO, MINION, SPELL, WEAPON, LOCATION }
public enum TAG_RARITY { FREE, COMMON, RARE, EPIC, LEGENDARY }
public sealed class EntityDef
{
    public string Name;
    public string Id;
    public int Cost;
    public TAG_CARDTYPE Type = TAG_CARDTYPE.MINION;
    public TAG_RARITY Rarity = TAG_RARITY.COMMON;
    public EntityDef Clone() => new EntityDef { Id = Id, Name = Name, Cost = Cost, Type = Type, Rarity = Rarity };
    public string GetCardId() => Id;
    public string GetName() => Name;
    public int GetTag(GAME_TAG tag) => tag == GAME_TAG.COST ? Cost : tag == GAME_TAG.CARDTYPE ? (int)Type : (int)Rarity;
}
public class CollectionDeck
{
    public string Name = "Deck";
    public string FormatType = "FT_STANDARD";
    public TAG_CLASS GetClass() => TAG_CLASS.MAGE;
    public readonly List<CollectionDeckSlot> Slots = new();
    public readonly Dictionary<string, SideboardDeck> Sideboards = new();
    public List<CollectionDeckSlot> GetSlots() => Slots;
    public Dictionary<string, SideboardDeck> GetAllSideboards() => Sideboards;
}
public sealed class SideboardDeck : CollectionDeck { }
public sealed class CollectionManager
{
    public static CollectionManager Instance;
    public CollectionDeck Edited;
    public readonly Dictionary<string, CollectibleCard> Cards = new();
    public CollectibleCard GetCard(string id, TAG_PREMIUM premium) => Cards.TryGetValue(id, out var card) ? card : null;
    public static CollectionManager Get() => Instance;
    public bool IsInEditMode() => Edited != null;
    public CollectionDeck GetEditedDeck() => Edited;
}
public sealed class Actor
{
    public CardDef Definition;
    public EntityDef Entity;
    public UnityEngine.Material Material;
    public UnityEngine.Texture PortraitTexture;
    public bool RefuseMaterial;
    public bool HasCardDef => Definition != null;
    public DefLoader.DisposableCardDef ShareDisposableCardDef() => new() { CardDef = Definition };
    public EntityDef GetEntityDef() => Entity;
    public UnityEngine.Material GetPortraitMaterial() => Material;
    public UnityEngine.Texture GetPortraitTexture() => Material?.mainTexture;
    public void SetPortraitMaterial(UnityEngine.Material material) { if (!RefuseMaterial) Material = material; }
    public void UpdateAllComponents() { }
}
public sealed class CardDef
{
    public readonly Dictionary<TAG_PREMIUM, UnityEngine.Texture> Portraits = new();
    public readonly List<Blizzard.T5.AssetManager.AssetHandle<UnityEngine.Texture>> Requests = new();
    public readonly Dictionary<TAG_PREMIUM, UnityEngine.Material> Materials = new();
    public readonly List<Blizzard.T5.AssetManager.AssetHandle<UnityEngine.Material>> MaterialRequests = new();
    public string GetPortraitMaterialPath(TAG_PREMIUM premium) => Materials.ContainsKey(premium) ? premium.ToString() : null;
    public Blizzard.T5.AssetManager.AssetHandle<UnityEngine.Material> GetPortraitMaterial(TAG_PREMIUM premium)
    {
        var handle = new Blizzard.T5.AssetManager.AssetHandle<UnityEngine.Material> { Asset = Materials[premium] };
        MaterialRequests.Add(handle); return handle;
    }
    public string GetPortraitTexturePath(TAG_PREMIUM premium, bool fallback, bool disk, out TAG_PREMIUM used)
    { used = premium; return Portraits.ContainsKey(premium) ? premium.ToString() : null; }
    public Blizzard.T5.AssetManager.AssetHandle<UnityEngine.Texture> GetPortraitTexture(TAG_PREMIUM premium, bool fallback, bool disk)
    {
        var handle = new Blizzard.T5.AssetManager.AssetHandle<UnityEngine.Texture> { Asset = Portraits.TryGetValue(premium, out var portrait) ? portrait : fallback ? Portraits[TAG_PREMIUM.NORMAL] : null };
        Requests.Add(handle); return handle;
    }
}
public sealed class DefLoader
{
    public static readonly DefLoader Instance = new();
    public readonly Dictionary<string, CardDef> Definitions = new();
    public static DefLoader Get() => Instance;
    public DisposableCardDef GetCardDef(string id) => Definitions.TryGetValue(id, out var def) ? new() { CardDef = def } : null;
    public sealed class DisposableCardDef : IDisposable
    {
        public CardDef CardDef;
        public bool Disposed;
        public void Dispose() { Disposed = true; }
    }
}
namespace Blizzard.T5.AssetManager
{
    public sealed class AssetHandle<T> : IDisposable
    {
        public T Asset;
        public bool Disposed;
        public void Dispose() { Disposed = true; }
    }
}
public sealed class UberText { }
public sealed class CollectibleCard { public bool IsCraftable = true; }
public static class CraftingUI
{
    public static readonly HashSet<string> Uncraftable = new();
    public static bool IsCraftingEventForCardActive(string id, TAG_PREMIUM premium, out bool future)
    { future = false; return !Uncraftable.Contains(id); }
}
public sealed class FixedRewardsMgr
{
    public static readonly HashSet<string> Uncraftable = new();
    public static FixedRewardsMgr Get() => new();
    public bool CanCraftCard(string id, TAG_PREMIUM premium) => !Uncraftable.Contains(id);
}
public static class CraftingManager
{
    public static readonly Dictionary<string, int> Prices = new();
    public sealed class Price { public int Amount; public int GetBuyValue() => Amount; }
    public static Price GetCardValue(string id, TAG_PREMIUM premium) => Prices.TryGetValue(id, out int price) ? new Price { Amount = price } : null;
}
public sealed class UIStatus
{
    public static readonly UIStatus Instance = new();
    public readonly List<string> Messages = new();
    public static UIStatus Get() => Instance;
    public void AddInfo(string text) => Messages.Add(text);
}
public sealed class CollectionDeckSlot
{
    public string CardID;
    private readonly EntityDef m_definition;
    private readonly TAG_PREMIUM m_premium;
    public int Count;
    public bool Owned = true;
    public CollectionDeckSlot(string id, EntityDef definition, TAG_PREMIUM premium, int count)
    { CardID = id; m_definition = definition; m_premium = premium; Count = count; }
    public EntityDef GetEntityDef() => m_definition;
    public int GetCount(TAG_PREMIUM premium) => m_premium == premium ? Count : 0;
}
namespace UnityEngine
{
    public class Object { public bool Destroyed; public static void Destroy(Object obj) { obj.Destroyed = true; } }
    public sealed class Texture : Object { public Color32[] Pixels; }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float horizontal, float vertical) { x = horizontal; y = vertical; }
    }
    public static class Graphics
    {
        public static bool Fail;
        public static int Copies;
        public static Vector2 LastScale, LastOffset;
        public static void Blit(Texture source, RenderTexture target, Vector2 scale, Vector2 offset)
        {
            RenderTexture.active = target;
            if (Fail) throw new InvalidOperationException("GPU copy failed");
            LastScale = scale; LastOffset = offset;
            target.Pixels = (Color32[])source.Pixels.Clone(); Copies++;
        }
        public static void Blit(Texture source, RenderTexture target, Material material)
        { if (material == null) throw new ArgumentNullException("mat"); throw new Exception("The portrait copy must not need a material"); }
    }
    public sealed class Material : Object
    {
        public object shader = new();
        public Texture mainTexture;
        public Texture LayerTexture;
        public float AnimationTime;
        public Material() { }
        public Material(Material source) { shader = source.shader; mainTexture = source.mainTexture; }
    }
    public enum CameraClearFlags { SolidColor, Depth }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float red, float green, float blue, float alpha = 1) { r = red; g = green; b = blue; a = alpha; }
        public static Color black => new(0, 0, 0);
        public static Color white => new(1, 1, 1);
    }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 center => new(x + width / 2, y + height / 2);
    }
    public sealed class RenderTexture
    {
        public static RenderTexture active;
        public Color32[] Pixels;
        public RenderTexture(int width, int height) { Pixels = new Color32[width * height]; }
    }
    public sealed class Camera
    {
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public RenderTexture targetTexture;
        public Action<Camera> Scene;
        public readonly List<Color> Backgrounds = new();
        public void Render()
        {
            Backgrounds.Add(backgroundColor);
            // Reproduce a camera target that discards a pre-render seed. Only
            // the camera's explicit clear survives, as in the reported export.
            var color = clearFlags == CameraClearFlags.SolidColor ? backgroundColor : Color.black;
            Array.Fill(targetTexture.Pixels, new Color32((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255), 255));
            Scene?.Invoke(this);
        }
    }
    public static class Time { public static float realtimeSinceStartup; }
    public static class SystemInfo { public static int maxTextureSize = 8192; }
    public sealed class WaitForEndOfFrame { }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte red, byte green, byte blue, byte alpha) { r = red; g = green; b = blue; a = alpha; }
    }
    public sealed class Texture2D
    {
        private Color32[] m_pixels;
        public static bool FailRead;
        public Texture2D(int width, int height) { m_pixels = new Color32[width * height]; }
        public void Apply(bool mipmaps, bool release) { }
        public byte[] EncodeToPNG() => new byte[] { 137, 80, 78, 71 };
        public Color32[] GetPixels32() => (Color32[])m_pixels.Clone();
        public void SetPixels32(Color32[] pixels) { m_pixels = (Color32[])pixels.Clone(); }
        public void ReadPixels(Rect area, int x, int y, bool mipmaps) { if (FailRead) throw new InvalidOperationException("GPU readback failed"); m_pixels = (Color32[])RenderTexture.active.Pixels.Clone(); }
    }
}
namespace BetterCollectionManager
{
    internal static class CatalogTransferMenu
    {
        internal static bool Shown;
        internal static int Opened;
        internal static List<string> Choices;
        internal static Action<int> Chosen;
        internal static Action Canceled;
        internal static Action OpenChoices(string title, IList<string> choices, Action<int> chosen, Action canceled)
        {
            if (Shown) Cancel();
            Shown = true; Opened++; Choices = new List<string>(choices); Chosen = chosen; Canceled = canceled;
            return () => { if (Shown && Chosen == chosen) Cancel(); };
        }
        internal static void Select(int index)
        {
            if (!Shown) return;
            Action<int> chosen = Chosen; Shown = false; Chosen = null; Canceled = null; chosen(index);
        }
        internal static void Cancel()
        {
            if (!Shown) return;
            Action canceled = Canceled; Shown = false; Chosen = null; Canceled = null; canceled();
        }
    }
    internal static class PackageArtwork
    {
        internal static readonly Dictionary<string, EntityDef> Definitions = new();
        internal static EntityDef Resolve(string id) => Definitions.TryGetValue(id, out var definition) ? definition : null;
    }
    internal static class DebugLog
    {
        internal static readonly List<string> Warnings = new(), Messages = new();
        internal static void Exception(string message, Exception ex) { }
        internal static void Warning(string text) { Warnings.Add(text); }
        internal static void Msg(string text) { Messages.Add(text); }
    }
    internal static class CollectionQualitySettings
    {
        internal static int Saves;
        internal static void EnsureLoaded() { }
        internal static void FlushPreferences(bool force = false) { }
        internal static void QueuePreferencesSave() { Saves++; }
    }
    internal static class LoaderPreferences
    {
        internal static PreferenceCategory CreateCategory(string key) => new();
        internal static void Load() { }
    }
    internal sealed class PreferenceCategory
    {
        private static readonly Dictionary<string, object> Entries = new();
        internal PreferenceEntry<T> CreateEntry<T>(string key, T value, string description)
        {
            if (!Entries.TryGetValue(key, out var entry)) Entries[key] = entry = new PreferenceEntry<T> { Value = value };
            return (PreferenceEntry<T>)entry;
        }
    }
    internal sealed class PreferenceEntry<T> { internal T Value; }
    internal static class DeckImageClipboard
    {
        internal static bool Supported = true;
        internal static bool Occupied;
        internal static int Copies;
        internal static byte[] LastPng, LastDib;
        internal static bool TryCopy(byte[] png, byte[] dib) { if (Occupied) return false; LastPng = png; LastDib = dib; Copies++; return true; }
    }
    internal static class LoaderCoroutines
    {
        internal static System.Collections.IEnumerator Current;
        internal static object Start(System.Collections.IEnumerator routine) { Current = routine; return routine; }
        internal static void Stop(object token) { Current = null; }
        internal static void Tick()
        {
            UnityEngine.Time.realtimeSinceStartup += .1f;
            var routine = Current;
            if (routine != null && !routine.MoveNext()) Current = null;
        }
    }
    internal sealed class DeckImageRenderer : IDisposable
    {
        internal static int Created, Disposed, Cards, DustCalls, ManaCalls, ClassCalls, ShieldCalls, ArtworkCalls;
        internal static readonly List<(string Text, int X, int Y, int Width, int Height, int Font, bool Left)> TextCalls = new();
        internal static readonly List<(bool Vines, int X, int Y, int Size)> HeaderArtwork = new();
        internal static readonly List<(int X, int Y, int Width, int Height)> InfoFrames = new();
        internal static readonly List<(int X, int Y, int Width, int Height, int Edge, int Inset, int Overhang)> WildFrames = new();
        internal static readonly List<(int X, int Y, int Width, int Height)> QuantityShields = new();
        internal static readonly List<(int X, int Y, int Size)> ManaCrystals = new();
        internal static (int X, int Y, int Width, int Height, int Stride, int Count) Wells;
        internal static readonly List<(int Width, int Height)> Sizes = new();
        internal static readonly List<int> CaptureWidths = new();
        internal static readonly List<string> Pipeline = new();
        private static void Record(string stage) { lock (Pipeline) Pipeline.Add(stage); }
        internal static readonly List<(int X, int Y, int Width, int Height)> DustIcons = new();
        internal static bool Loading, Fail;
        private readonly int m_mainThread = Environment.CurrentManagedThreadId;
        private void MainThread() { if (Environment.CurrentManagedThreadId != m_mainThread) throw new Exception("Native call left the main thread"); }
        internal static int WorkerPaints, NativeShieldCalls;
        internal static System.Threading.ManualResetEventSlim PaintGate;
        internal static readonly System.Threading.ManualResetEventSlim PaintEntered = new(false);
        internal static readonly System.Threading.ManualResetEventSlim PaintFinished = new(false);
        internal static int DustInkRight = -1;
        internal UnityEngine.Texture2D Image;
        internal DeckImageCanvas Canvas;
        internal static readonly List<string> Captions = new();
        internal static readonly List<TAG_PREMIUM> Premiums = new();
        internal static readonly List<TAG_CLASS> ClassBackgrounds = new();
        internal DeckImageCanvas.Ink TextInk => DeckImageCanvas.Text;
        internal DeckImageCanvas.Ink MutedInk => DeckImageCanvas.Muted;
        internal DeckImageCanvas.Ink AccentInk => DeckImageCanvas.Gold;
        internal DeckImageCanvas.Ink SideboardInk => DeckImageCanvas.Coral;
        internal DeckImageCanvas.Ink CardShadowInk => new(0, 0, 0, 130);
        internal DeckImageCanvas.Ink SideboardPanelInk => new(125, 58, 57, 160);
        internal DeckImageRenderer(UnityEngine.Camera camera, UberText font, int width, int height, bool decorated, DeckImageBackground background = DeckImageBackground.Parchment, bool deferCanvas = false)
        { Created++; if (deferCanvas) Canvas = new DeckImageCanvas(1, 1, decorated); else { Sizes.Add((width, height)); Image = new UnityEngine.Texture2D(width, height); Canvas = new DeckImageCanvas(width, height, decorated); } }
        internal void SetCanvasSize(int width, int height)
        { Record("canvas"); Sizes.Add((width, height)); Image = new UnityEngine.Texture2D(width, height); Canvas = new DeckImageCanvas(width, height, true); }
        internal System.Threading.Tasks.Task InitializeCanvasAsync(int width, int height, TAG_CLASS? backgroundClass, UnityEngine.Texture artwork)
        {
            MainThread(); SetCanvasSize(width, height);
            if (backgroundClass.HasValue) ClassBackground(backgroundClass.Value);
            if (artwork != null) ArtworkBackground(artwork);
            return System.Threading.Tasks.Task.CompletedTask;
        }
        internal bool PrepareCard(DeckImageSnapshot.Card card) => !Loading;
        internal bool CardArtworkReady(Actor actor) => !Loading;
        internal Actor CreateCard(DeckImageSnapshot.Card card) { Premiums.Add(card.Premium); return new Actor(); }
        internal int DustIcon(int x, int y, int width, int height) { MainThread(); DustCalls++; DustIcons.Add((x, y, width, height)); return DustInkRight >= 0 ? DustInkRight : width; }
        internal void ClassIcon(TAG_CLASS deckClass, int x, int y, int size)
        { MainThread(); ClassCalls++; HeaderArtwork.Add((false, x, y, size)); }
        internal DeckImageCanvas.Ink CountShield(bool wild, int count, int x, int y, int width, int height)
        { MainThread(); NativeShieldCalls++; ShieldCalls++; QuantityShields.Add((x, y, width, height)); return TextInk; }
        internal bool RarityIcon(TAG_RARITY rarity, int x, int y, int width, int height) => false;
        internal void ManaBar(int count, int maximum, int x, int y, int width, int height) { ManaCalls++; }
        internal void ManaWells(int x, int y, int width, int height, int stride, int count) => Wells = (x, y, width, height, stride, count);
        internal void ManaCrystal(int x, int y, int size) => ManaCrystals.Add((x, y, size));
        internal void InfoFrame(int x, int y, int width, int height) => InfoFrames.Add((x, y, width, height));
        internal void WildFrame(int x, int y, int width, int height, int edge, int inset, int overhang) => WildFrames.Add((x, y, width, height, edge, inset, overhang));
        internal static readonly List<UnityEngine.Texture> ArtworkTextures = new();
        internal void ArtworkBackground(UnityEngine.Texture artwork) { ArtworkCalls++; ArtworkTextures.Add(artwork); }
        internal static readonly List<(string Text, int Width, int Height, int Font)> CaptionMeasurements = new();
        internal int MeasureCaption(string text, int width, int height, int font)
        {
            CaptionMeasurements.Add((text, width, height, font));
            return Math.Min(width - 12, (int)(text.Length * font * .55));
        }
        internal static readonly List<string> VisibleHeightCaptions = new();
        internal static readonly List<string> VerticallyCenteredCaptions = new();
        internal int Caption(string text, int x, int y, int width, int height, int font, DeckImageCanvas.Ink? ink = null, bool leftAligned = false, bool fitVisibleHeight = false, bool centerVisibleInk = false) { MainThread(); Captions.Add(text); TextCalls.Add((text, x, y, width, height, font, leftAligned)); if (fitVisibleHeight) VisibleHeightCaptions.Add(text); if (centerVisibleInk) VerticallyCenteredCaptions.Add(text); return Math.Min(width - 12, (int)(text.Length * font * .55)); }
        internal Func<DeckImageCardFrame> CaptureCardJob(Actor actor, int bodyWidth, bool sideboard)
        {
            MainThread();
            if (Fail) throw new InvalidOperationException("GPU failure");
            Cards++; CaptureWidths.Add(bodyWidth); Record("capture");
            var pixels = Enumerable.Repeat(new UnityEngine.Color32(150, 90, 50, 255), 8 * 12).ToArray();
            var pair = new DeckImageCapture.Pair(pixels, pixels);
            return () => new DeckImageCardFrame(pair, pair, 8, 12, bodyWidth, false);
        }
        internal DeckImageLayout.Cell PaintCard(DeckImageCardFrame frame, DeckImageLayout.Cell cell, Action<DeckImageLayout.Cell> beforePaint)
        {
            if (Environment.CurrentManagedThreadId == m_mainThread) throw new Exception("Card pixels block the main thread");
            WorkerPaints++; Record("paint");
            if (PaintGate != null) { PaintEntered.Set(); PaintGate.Wait(); }
            beforePaint(cell); PaintFinished.Set(); return cell;
        }
        internal bool QuantityLabel(int count, int x, int y, int width, int height)
        {
            if (!DeckImageQuantityLabel.Paint(Canvas, count, x, y, width, height)) return false;
            ShieldCalls++; QuantityShields.Add((x, y, width, height)); Captions.Add("x" + count);
            return true;
        }
        internal void ClassBackground(TAG_CLASS deckClass) => ClassBackgrounds.Add(deckClass);
        internal void Finish() { throw new Exception("Full-image Unity upload must not be used by export"); }
        internal void ReleaseCard() { }
        public void Dispose() { Disposed++; }
    }
}
