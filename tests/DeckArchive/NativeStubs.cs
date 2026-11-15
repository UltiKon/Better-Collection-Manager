using BetterCollectionManager;
namespace PegasusShared
{
    public enum FormatType { FT_UNKNOWN, FT_WILD, FT_STANDARD, FT_CLASSIC, FT_TWIST }
    public enum DeckType { NORMAL_DECK, DRAFT_DECK }
    public enum DeckSourceType { DECK_SOURCE_TYPE_NORMAL }
    public enum RuneType { RT_NONE, RT_BLOOD, RT_FROST, RT_UNHOLY }
}
namespace PegasusUtil { public class DeckContents { public long DeckId; } }
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup; } }
namespace HarmonyLib
{
    public class Harmony { public void Patch(object method, HarmonyMethod prefix = null, HarmonyMethod postfix = null) { } }
    public class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo Method(Type type, string name, Type[] parameters = null) => type.GetMethod(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        public static System.Reflection.FieldInfo Field(Type type, string name) => type.GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    }
}
namespace Blizzard.GameService.SDK.Client.Integration
{
    public class Account { public ulong High = 1, Low = 2; public bool IsValid() => true; }
    public static class BattleNet { public static Account Current = new Account(); public static Account GetMyGameAccountId() => Current; }
}
public enum TAG_PREMIUM { NORMAL, GOLDEN, DIAMOND, SIGNATURE, MAX }
public class NetCache { public class DeckHeader { public long ID; } }
public class CollectionDeckSlot
{
    public string CardID;
    public int[] Counts = new int[4];
    public int Count => Counts.Sum();
    public int GetCount(TAG_PREMIUM premium) => Counts[(int)premium];
}
public class CollectionDeck
{
    private readonly SideboardManager m_sideboardManager;
    public CollectionDeck() { m_sideboardManager = new SideboardManager(this); }
    public long ID;
    public string Name, HeroCardID, UIHeroOverrideCardID = "";
    public PegasusShared.FormatType FormatType;
    public PegasusShared.DeckType Type;
    public bool HeroOverridden, RandomHeroUseFavorite = true, RandomCoinUseFavorite = true, RandomPetUseFavorite = true;
    public bool IsDeckTemplate, Saving, Deleted, Loaded = true;
    public int? CardBackID, CosmeticCoinID, PetID, PetVariantID;
    public TAG_PREMIUM UIHeroOverridePremium;
    public enum ChangeSource { SaveCurrentDeck }
    private PegasusShared.RuneType[] runes = Array.Empty<PegasusShared.RuneType>();
    public readonly List<CollectionDeckSlot> Slots = new();
    public readonly Dictionary<string, CollectionDeck> Sideboards = new();
    public bool IsBeingDeleted() => Deleted;
    public bool IsSavingChanges() => Saving;
    public bool NetworkContentsLoaded() => Loaded;
    public List<CollectionDeckSlot> GetSlots() => Slots;
    public Dictionary<string, CollectionDeck> GetAllSideboards() => Sideboards;
    public PegasusShared.RuneType[] GetRuneOrder() => runes;
    public void SetRuneOrder(params PegasusShared.RuneType[] values) => runes = values;
    public bool AddCard(string id, TAG_PREMIUM premium, bool allowInvalid)
    {
        var slot = Slots.Find(s => s.CardID == id);
        if (slot == null) { slot = new CollectionDeckSlot { CardID = id }; Slots.Add(slot); }
        slot.Counts[(int)premium]++; return true;
    }
    public void AddCardToSideboard(string id, int owner, TAG_PREMIUM premium, bool allowInvalid)
    {
        string key = GameUtils.Ids.Single(p => p.Value == owner).Key;
        if (!Sideboards.ContainsKey(key)) Sideboards[key] = new CollectionDeck();
        Sideboards[key].AddCard(id, premium, allowInvalid);
    }
    public void SendChanges(ChangeSource source) => Saving = true;
    public void ClearSideboards() => Sideboards.Clear();
    public bool CanCardBeAddedAsOwned(string id, TAG_PREMIUM premium) => false;
    public CollectionDeckSlot InsertSlotWithCard(string id, TAG_PREMIUM premium, bool owned, int count)
    {
        var slot = new CollectionDeckSlot { CardID = id }; slot.Counts[(int)premium] = count; Slots.Add(slot); return slot;
    }
    public void MarkNetworkContentsLoaded() => Loaded = true;
    public void OnNameChangeComplete() { }
    public void OnContentChangesComplete() => Saving = false;
    public void CopyFrom(CollectionDeck source)
    {
        ID = source.ID; Name = source.Name; HeroCardID = source.HeroCardID; FormatType = source.FormatType;
        UIHeroOverrideCardID = source.UIHeroOverrideCardID; UIHeroOverridePremium = source.UIHeroOverridePremium;
        HeroOverridden = source.HeroOverridden; RandomHeroUseFavorite = source.RandomHeroUseFavorite;
        CardBackID = source.CardBackID; CosmeticCoinID = source.CosmeticCoinID; RandomCoinUseFavorite = source.RandomCoinUseFavorite;
        PetID = source.PetID; PetVariantID = source.PetVariantID; RandomPetUseFavorite = source.RandomPetUseFavorite;
        SetRuneOrder(source.GetRuneOrder().ToArray()); Slots.Clear(); Sideboards.Clear();
        foreach (var slot in source.Slots) Slots.Add(new CollectionDeckSlot { CardID = slot.CardID, Counts = slot.Counts.ToArray() });
        foreach (var pair in source.Sideboards) { var copy = new CollectionDeck(); copy.CopyFrom(pair.Value); Sideboards[pair.Key] = copy; }
    }
}
public class SideboardManager
{
    private readonly CollectionDeck owner;
    public SideboardManager(CollectionDeck deck) { owner = deck; }
    public CollectionDeck GetOrCreateSideboard(string id, TAG_PREMIUM premium, bool edit)
    {
        if (!owner.Sideboards.ContainsKey(id)) owner.Sideboards[id] = new CollectionDeck();
        return owner.Sideboards[id];
    }
}
public class CollectionDeckTray { }
public class CollectionManager
{
    public static CollectionManager Instance = new();
    public object m_pendingDeckCreate;
    public bool Editing, Ready = true;
    public CollectionDeck Edited;
    public readonly Dictionary<long, CollectionDeck> Decks = new();
    public readonly Dictionary<long, CollectionDeck> Bases = new();
    public static CollectionManager Get() => Instance;
    public bool IsInEditMode() => Editing;
    public bool AreAllDeckContentsReady() => Ready;
    public CollectionDeck GetEditedDeck() => Edited;
    public CollectionDeck GetDeck(long id) => Decks.GetValueOrDefault(id);
    public CollectionDeck GetBaseDeck(long id) => Bases.GetValueOrDefault(id);
    public List<CollectionDeck> GetDecks(PegasusShared.DeckType type) => Decks.Values.Where(d => d.Type == type).ToList();
}
public static class GameUtils
{
    public static readonly Dictionary<string, int> Ids = new() { ["HERO_01"] = 1, ["CARD_A"] = 2, ["CARD_B"] = 3, ["OWNER"] = 4 };
    public static int TranslateCardIdToDbId(string id) => Ids.GetValueOrDefault(id);
}
public class Network
{
    public class DBAction
    {
        public enum ActionType { CREATE_DECK, SET_DECK }
        public enum ResultType { SUCCESS, CONSTRAINT }
        public long MetaData;
        public ActionType Action;
        public ResultType Result;
    }
    public static Network Instance = new();
    public static bool Online = true;
    public int Requests, Readbacks;
    public PegasusShared.FormatType CreatedFormat;
    public static bool IsLoggedIn() => Online;
    public static Network Get() => Instance;
    public void CreateDeck(PegasusShared.DeckType type, string name, int hero, bool overridden, PegasusShared.FormatType format,
        long sortOrder, PegasusShared.DeckSourceType source, int? back, int? coin, out int? request)
    { CreatedFormat = format; request = ++Requests; }
    public void RequestDeckContents(params long[] ids) => Readbacks++;
}
public class AlertPopup
{
    public enum Response { OK, CONFIRM, CANCEL }
    public enum ResponseDisplay { OK, CONFIRM_CANCEL }
    public class PopupInfo { public string m_id, m_okText, m_headerText, m_text; public bool m_richTextEnabled; public ResponseDisplay m_responseDisplay; }
    public PopupInfo Info = new();
    public PopupInfo GetInfo() => Info;
    public UIBButton m_okayButton = new() { Skin = "ornamental OK" }, m_confirmButton = new(), m_cancelButton = new();
    public MultiSliceElement m_buttonContainer = new();
    public NineSliceElement m_body = new();
    public Header m_header = new();
    public List<UnityEngine.GameObject> m_buttonIconsSet1 = new(), m_buttonIconsSet2 = new();
    public Response? LastResponse;
    public class Header { public MultiSliceElement m_container = new(); }
    private void ButtonPress(Response response) => LastResponse = response;
}
public class DialogManager
{
    public static readonly DialogManager Instance = new();
    public static DialogManager Get() => Instance;
    public readonly List<AlertPopup.PopupInfo> Errors = new();
    public void ShowPopup(AlertPopup.PopupInfo info) => Errors.Add(info);
}
namespace BetterCollectionManager
{
    internal static class CollectionQualitySettings { internal static string SettingsPath; internal static bool DeckArchiveEnabled = true; }
    internal static class DebugLog { internal static void Exception(string message, Exception ex) { } }
    internal static class DeckArchiveUi
    {
        internal static int Saved;
        internal static readonly List<string> RestoredKeys = new();
        internal static void Install(HarmonyLib.Harmony harmony) { }
        internal static void Invalidate() { }
        internal static void Refresh() { }
        internal static void ShowSaved() => Saved++;
        internal static void Restored(string key) => RestoredKeys.Add(key);
        internal static void Update() { }
        internal static void Shutdown() { }
        internal static bool IsLocalId(long id) => false;
        internal static void Edited(CollectionDeck deck, string key) { }
    }
}
