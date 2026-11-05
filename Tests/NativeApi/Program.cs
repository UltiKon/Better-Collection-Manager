using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Read metadata only: these checks need the client's Assembly-CSharp.dll but
// do not load Unity, execute game code, or replace a native build/game test.
string path = args.Length > 0 ? args[0] : "Assembly-CSharp.dll";
using var stream = File.OpenRead(path);
using var pe = new PEReader(stream);
MetadataReader metadata = pe.GetMetadataReader();
var names = new SignatureNames();
var types = metadata.TypeDefinitions.ToDictionary(handle => names.GetTypeFromDefinition(metadata, handle, 0));
int checks = 0;
TypeDefinition Type(string name) => metadata.GetTypeDefinition(types[name]);
void Field(string owner, string name, string expected, bool requirePublic = true)
{
    FieldDefinition field = Type(owner).GetFields().Select(metadata.GetFieldDefinition)
        .Single(value => metadata.GetString(value.Name) == name);
    if ((requirePublic && (field.Attributes & FieldAttributes.Public) == 0) || field.DecodeSignature(names, (object?)null) != expected)
        throw new InvalidOperationException($"Incompatible native field {owner}.{name}");
    checks++;
}
void Method(string owner, string name, string result, params string[] parameters)
{
    bool found = Type(owner).GetMethods().Select(metadata.GetMethodDefinition).Any(method =>
    {
        if (metadata.GetString(method.Name) != name || (method.Attributes & MethodAttributes.Public) == 0) return false;
        var signature = method.DecodeSignature(names, (object?)null);
        return signature.ReturnType == result && signature.ParameterTypes.SequenceEqual(parameters);
    });
    if (!found) throw new InvalidOperationException($"Incompatible native method {owner}.{name}");
    checks++;
}
void InternalMethod(string owner, string name, string result, params string[] parameters)
{
    bool found = Type(owner).GetMethods().Select(metadata.GetMethodDefinition).Any(method =>
    {
        if (metadata.GetString(method.Name) != name) return false;
        var signature = method.DecodeSignature(names, (object?)null);
        return signature.ReturnType == result && signature.ParameterTypes.SequenceEqual(parameters);
    });
    if (!found) throw new InvalidOperationException($"Incompatible native hub method {owner}.{name}");
    checks++;
}
Field("BookPageManager", "m_pageLeftArrowBone", "UnityEngine.GameObject");
Field("BookPageManager", "m_pageRightArrowBone", "UnityEngine.GameObject");
Field("CardListPanel", "m_leftArrowNested", "NestedPrefab", false);
Field("CardListPanel", "m_rightArrowNested", "NestedPrefab", false);
Field("DialogManager", "m_typeMapping", "System.Collections.Generic.List`1<DialogManager/DialogTypeMapping>", false);
Field("DialogManager/DialogTypeMapping", "m_type", "DialogManager/DialogType", false);
Field("DialogManager/DialogTypeMapping", "m_prefabName", "String", false);
Field("GameMenu", "m_redButtonMaterial", "UnityEngine.Material");
InternalMethod("ButtonListMenu", "MakeButtonRed", "Void", "UIBButton", "UnityEngine.Material");
InternalMethod("BookPageManager", "TransitionPageWhenReady", "Void", "BookPageManager/PageTransitionType", "Boolean", "BookPageManager/DelOnPageTransitionComplete", "Object");
InternalMethod("CollectionManagerDisplay", "Start", "Void");
Method("SplashScreen", "Get", "SplashScreen");
Method("Actor", "Hide", "Void");
Method("Actor", "Show", "Void");
Method("CraftingManager", "EnterCraftMode", "Void", "Actor", "System.Action");
Method("CraftingManager", "OnCardDisenchanted", "Void", "Network/CardSaleResult");
InternalMethod("DisenchantButton", "OnRelease", "Void");
Method("CraftingManager", "DisenchantButtonPressed", "Void");
Method("CraftingButton", "IsButtonEnabled", "Boolean");
Method("CraftingManager", "IsCardShowing", "Boolean");
Method("CraftingManager", "IsCancelling", "Boolean");
Method("CraftingManager", "GetShownCardInfo", "Boolean", "EntityDef&", "TAG_PREMIUM&");
Method("CraftingManager", "IsPendingTransactionForCard", "Boolean", "String");
Method("CraftingManager", "GetNumOwnedIncludePending", "Int32", "String", "System.Nullable`1<TAG_PREMIUM>");
Method("CraftingManager", "GetPendingClientTransaction", "CraftingPendingTransaction");
Method("CraftingManager", "GetPendingServerTransaction", "CraftingPendingTransaction");
Method("CraftingManager", "TryGetCardSellValue", "Boolean", "String", "TAG_PREMIUM", "Int32&");
Method("CraftingManager", "AdjustUnCommitedArcaneDustChanges", "Void", "Int32");
Method("CraftingManager", "CancelCraftMode", "Boolean");
Method("Navigation", "get_CanGoBack", "Boolean");
Method("Navigation", "GoBack", "Boolean");
Field("CraftingPendingTransaction", "CardID", "String");
Method("CraftingPendingTransaction", "Add", "Void", "CraftingPendingTransaction/Operation");
Method("CraftingPendingTransaction", "GetTransactionAmount", "Int32", "TAG_PREMIUM");
Method("CraftingPendingTransaction", "GetLastTransactionWasCrafting", "Boolean");
Method("CollectibleCard", "get_IsCraftable", "Boolean");
Method("CollectibleCard", "get_IsNewCollectible", "Boolean");
Field("CollectibleCard", "m_LatestInsertDate", "System.DateTime", false);
Method("CollectibleCardFilter", "GenerateResults", "CollectionManager/FindCardsResult");
Method("CollectibleCardFilter", "FilterSearchText", "Void", "String");
Method("CollectionManagerDisplay", "FilterBySearchText", "Void", "String");
Method("CollectibleDisplay", "IsBookOpened", "Boolean");
Method("CollectibleDisplay", "IsReady", "Boolean");
Method("CollectionManager", "IsFullyLoaded", "Boolean");
Method("BookPageManager", "IsFullyLoaded", "Boolean");
Method("BookPageManager", "ArePagesTurning", "Boolean");
Method("CollectiblePageManager", "HideCraftingModeCards", "Void", "BookPageManager/PageTransitionType", "Boolean");
Method("CollectionPageManager", "ShowCraftingModeCards", "Void", "BookPageManager/DelOnPageTransitionComplete", "Object", "Boolean", "Boolean", "Boolean", "Boolean", "Boolean", "Boolean", "Boolean");
Field("BookPageManager", "m_currentPageNum", "Int32", false);
Field("CollectiblePageManager", "m_cardsCollection", "CollectibleCardFilter", false);
Method("CraftingManager", "GetCardValue", "NetCache/CardValue", "String", "TAG_PREMIUM");
Method("NetCache/CardValue", "get_OverrideEvent", "EventTimingType");
Method("CollectionManager", "OnCardsModified", "Void", "System.Collections.Generic.List`1<CollectionManager/CardModification>");
Field("CollectionManager/CardModification", "cardID", "String");
Field("CollectionManager/CardModification", "premium", "TAG_PREMIUM");
Field("CollectionManager/CardModification", "count", "Int32");
Field("CollectibleCardFilter", "m_filterOwnedMinimum", "System.Nullable`1<Int32>", false);
Field("CollectibleCardFilter", "m_filterText", "String", false);
Method("CollectionDeck", "GetClass", "TAG_CLASS");
Method("CollectionDeck", "GetClasses", "System.Collections.Generic.List`1<TAG_CLASS>");
Method("CollectionDeck", "GetTouristClasses", "System.Collections.Generic.List`1<TAG_CLASS>");

Field("AdjustBuyingAmount", "m_incrementClickable", "Hearthstone.UI.Clickable", false);
Field("AdjustBuyingAmount", "m_decrementClickable", "Hearthstone.UI.Clickable", false);
Field("AdjustBuyingAmount", "m_buyingAmountText", "UberText", false);
Field("Hearthstone.UI.WidgetInstance", "m_widgetTemplate", "Hearthstone.UI.WeakAssetReference", false);
Field("Hearthstone.UI.WeakAssetReference", "AssetString", "String");
Method("Actor", "GetMeshRenderer", "UnityEngine.MeshRenderer", "Boolean");
Method("SplashScreen", "add_SplashScreenShown", "Void", "System.Action`1<SplashScreen>");
Method("SplashScreen", "remove_SplashScreenShown", "Void", "System.Action`1<SplashScreen>");
Method("DropdownControl", "getMenuShownCallback", "DropdownControl/menuShownCallback");
Field("CheckBox", "m_checkOnSound", "String");
Field("CheckBox", "m_checkOffSound", "String");
Field("DropdownControl", "m_button", "UIBButton");
Field("DropdownControl", "m_selectedItem", "DropdownMenuItem");
Field("DropdownControl", "m_cancelCatcher", "PegUIElement");
Method("DropdownControl", "addItem", "Void", "Object");
Method("DropdownControl", "onUserCancelled", "Void");
Field("ButtonListMenu", "NORMAL_SCALE", "UnityEngine.Vector3", false);
Field("ButtonListMenu", "m_blocker", "PegUIElement", false);
Field("Actor", "m_cardRuneBanner", "CardRuneBanner");
Field("CollectionCardLock", "m_lockPlate", "UnityEngine.GameObject", false);
Field("CollectionCardLock", "m_bannedRibbon", "UnityEngine.GameObject", false);
Field("CollectionCardLock", "m_lockText", "UberText", false);
Method("CardRuneBanner", "Show", "Void", "RunePattern");
Method("CollectionInputMgr", "GrabCardVisual", "Boolean", "CollectionCardVisual");
Field("CollectionCardVisual", "m_boxColliderCenter", "UnityEngine.Vector3");
Field("CollectionCardVisual", "m_boxColliderSize", "UnityEngine.Vector3");
Field("ButtonListMenuDef", "m_templateButton", "UIBButton");
Field("SetFilterTray", "m_trayObject", "UnityEngine.GameObject");
Field("SetFilterTray", "m_scroller", "UIBScrollable");
Field("CameraMask", "m_ClipObjects", "UnityEngine.GameObject");
Field("CameraMask", "m_Width", "Single");
Field("CameraMask", "m_Height", "Single");
Field("CameraMask", "m_UpVector", "CameraMask/CAMERA_MASK_UP_VECTOR");
Field("CameraMask", "m_UseCameraFromLayer", "Boolean");
Field("CameraMask", "m_CameraFromLayer", "GameLayer");
Field("CameraMask", "m_CullingMasks", "System.Collections.Generic.List`1<GameLayer>");
Field("CameraMask", "m_RealtimeUpdate", "Boolean");
Field("CameraMask", "RenderEntryPoint", "CustomViewEntryPoint");
Method("CameraMask", "UpdateMask", "Void");
Method("CameraUtils", "FindFirstByLayer", "UnityEngine.Camera", "GameLayer");
Field("NestedPrefab", "m_Prefab", "String");
Field("NestedPrefabPlatformOverride", "m_Prefab", "String_MobileOverride");
Method("CollectionManagerScene", ".ctor", "Void");
Field("UIBScrollable", "m_ScrollTrack", "UnityEngine.BoxCollider");
Field("UIBScrollable", "m_ScrollThumb", "ScrollBarThumb");
Field("UIBScrollable", "m_scrollTrackCover", "UnityEngine.GameObject");
Method("UIBScrollable", "EnableIfNeeded", "Boolean");
Field("UIBHighlight", "m_MouseDownSound", "String");
Field("UIBHighlight", "m_MouseUpSound", "String");
Field("UIBScrollable", "m_ScrollPlane", "UIBScrollable/ScrollDirection");
Method("CollectionManagerDisplay", "CollectionPageContentsChanged", "Void", "System.Collections.Generic.ICollection`1<!!0>",
    "CollectibleDisplay/CollectionActorsReadyCallback", "Object");
Field("CollectionPageManager", "m_deckTemplatePickerPrefab", "String");
Field("DeckTemplatePicker", "m_pickerButtonTpl", "DeckTemplatePickerButton");
Field("DeckTemplatePicker", "m_deckArtMaterial", "UnityEngine.Material");
Field("DeckTemplatePickerButton", "m_deckTexture", "UnityEngine.MeshRenderer");
Field("DeckTemplatePickerButton", "m_packRibbon", "UnityEngine.MeshRenderer");
Method("CollectionPageManager", "ColorForClass", "UnityEngine.Color", "TAG_CLASS");
Method("EntityBase", "GetClasses", "Void", "System.Collections.Generic.IList`1<TAG_CLASS>");
Field("DeckTemplatePickerButton", "m_title", "UberText");
Field("DeckTemplatePickerButton", "m_cardCountTexts", "System.Collections.Generic.List`1<UberText>");
foreach (string field in new[] { "m_selectGlow", "m_runes", "m_incompleteTextRibbon", "m_completeTextRibbon" })
    Field("DeckTemplatePickerButton", field, "UnityEngine.GameObject");
foreach (string field in new[] { "m_packFlap", "m_packInner" }) Field("DeckTemplatePickerButton", field, "UnityEngine.MeshRenderer");
Field("DeckTemplatePickerButton", "m_packCustomMaterial", "UnityEngine.Material");
Method("CollectionPageManager", "GetDeckTemplatePicker", "DeckTemplatePicker");
Method("GameUtils", "GetRelatedCardIds", "System.Collections.Generic.List`1<String>", "EntityDef", "Boolean&");
Method("GameUtils", "GetCounterpartCardIds", "System.Collections.Generic.List`1<String>", "String");
Method("GameUtils", "IsCardValidForFormat", "Boolean", "PegasusShared.FormatType", "EntityDef");
Method("CollectionDeck", "get_FormatType", "PegasusShared.FormatType");
InternalMethod("CollectionManagerDisplay", "ShowDeckHelper", "Void", "CollectionDeck", "System.Collections.Generic.List`1<TAG_CLASS>", "Boolean", "Boolean", "System.Nullable`1<CollectionUtils/ViewMode>");
Method("CollectionManagerDisplay", "OnDoneEditingDeck", "Void");
Method("DefLoader", "GetEntityDef", "EntityDef", "String");
Method("DefLoader", "GetCardDef", "DefLoader/DisposableCardDef", "String");
Method("CardDef", "GetPortraitTexture", "Blizzard.T5.AssetManager.AssetHandle`1<UnityEngine.Texture>", "TAG_PREMIUM", "Boolean", "Boolean");
Method("CollectionDeckTray", "GetCurrentCardListContext", "DeckTrayCardListContent", "Boolean");
Method("DeckTrayCardListContent", "UpdateCardList", "Void", "EntityDef", "Boolean", "Actor", "System.Action");
Method("OverlayUI", "Get", "OverlayUI");
Method("OverlayUI", "AddGameObject", "Void", "UnityEngine.GameObject", "CanvasAnchor", "Boolean", "CanvasScaleMode");
Method("OverlayUI", "HasObject", "Boolean", "UnityEngine.GameObject");
Method("TooltipPanelManager", "UpdateKeywordHelpForCollectionManager", "Void", "EntityDef", "Actor", "TooltipPanelManager/Orientation");
Method("TooltipPanelManager", "HideKeywordHelp", "Void");
Field("Box", "m_BattleGroundsButton", "BoxScrollButton");
Field("Box", "m_CollectionButton", "BoxMenuButton");
Field("Box", "m_ArenaButton", "BoxMenuButton");
Field("Box", "m_GameModesButton", "BoxMenuButton");
Field("Box", "m_luckyDrawButtonController", "LuckyDrawButtonController");
Field("Box", "m_waitingForNetData", "Boolean", false);
Field("LuckyDrawButtonController", "m_isEnabled", "Boolean", false);
Field("LuckyDrawButtonController", "OnReady", "System.Action");
Field("GameModeDisplay", "m_activeGameModeRecords", "System.Collections.Generic.List`1<GameModeDbfRecord>", false);
Field("GameModeDisplay", "m_selectedGameModeButtonDataModel", "Hearthstone.DataModels.GameModeButtonDataModel", false);
Field("GameModeDisplay", "m_backButton", "UIBButton", false);
InternalMethod("Box", "CanEnableUIEvents", "Boolean");
InternalMethod("Box", "SetupButtonText", "Void");
InternalMethod("Box", "UpdateBoxButtons", "Void");
InternalMethod("Box", "OnButtonPressed", "Void", "UIEvent");
InternalMethod("Box", "OnButtonMouseOver", "Void", "UIEvent");
InternalMethod("BoxScrollButton", "UpdateDownloadButtonIcon", "Void", "Boolean");
InternalMethod("BoxScrollButton", "UpdateDownloadProgress", "Void", "Boolean");
InternalMethod("GameModeDisplay", "InitializeGameModeSceneData", "Void");
InternalMethod("GameModeDisplay", "NavigateToSelectedMode", "Void");
InternalMethod("GameModeDisplay", "CanEnterMode", "Boolean", "String&", "GameModeDisplay/ModeLockedStatus&");
InternalMethod("GameModeDisplay", "GetModuleTagForGameMode", "Hearthstone.Core.Streaming.DownloadTags/Content", "GameModeDbfRecord");
Method("Box", "SetBoxButtonInteractions", "Void", "Boolean", "BoxMenuButton", "Boolean");
Method("Box", "OnTavernBrawlButtonPressed", "Void", "UIEvent");
Method("Box", "OnBattleGroundsButtonPressed", "Void", "UIEvent");
Method("Box", "OnLuckyDrawButtonPressed", "Void", "UIEvent");
Method("Box", "FireButtonPressEvent", "Void", "Box/ButtonType", "Boolean");
Method("LuckyDrawButtonController", "Show", "Void");
Method("LuckyDrawButtonController", "Hide", "Void");
Method("LuckyDrawButtonController", "SetEnable", "Void", "Boolean");
Method("LuckyDrawButtonController", "RelayTriggerEvent", "Void", "String");
Method("LuckyDrawButtonController", "GetButton", "LuckyDrawButton");
Method("LuckyDrawButtonController", "GetWidget", "Hearthstone.UI.Widget");
Method("Hearthstone.UI.Widget", "get_IsInitialized", "Boolean");
Method("Hearthstone.UI.Widget", "RegisterEventListener", "Void", "Hearthstone.UI.Widget/EventListenerDelegate");
Method("Hearthstone.UI.Widget", "RemoveEventListener", "Void", "Hearthstone.UI.Widget/EventListenerDelegate");
Method("Hearthstone.UI.WidgetTemplate", "Initialize", "Void", "Boolean");
Method("Hearthstone.UI.WidgetTemplate", "EventReceived", "Hearthstone.UI.WidgetEventListenerResponse", "String", "Hearthstone.UI.TriggerEventParameters");
Method("LuckyDrawManager", "GetPreferredLuckyDrawID", "Boolean", "Int32&");
Method("LuckyDrawManager", "LuckyDrawEnabled", "Boolean", "Int32", "Boolean");
Method("LuckyDrawManager", "IsDataDirty", "Boolean");
Method("LuckyDrawManager", "TryGetButtonDataModelForBoxID", "Boolean", "Int32", "Hearthstone.DataModels.LuckyDrawButtonDataModel&");
Method("Hearthstone.DataModels.LuckyDrawButtonDataModel", "get_ButtonItemType", "LuckyDrawFeaturedType");
Method("Hearthstone.DataModels.LuckyDrawButtonDataModel", "get_ButtonPetVariantID", "Int32");
Method("Hearthstone.DataModels.LuckyDrawButtonDataModel", "get_ButtonHeroSkinID", "String");
Method("Hearthstone.DataModels.LuckyDrawButtonDataModel", "get_ButtonCustomPrefabId", "String");
Method("LuckyDrawUtils", "get_AssetReferenceLibrary", "LuckyDrawAssetReferenceLibrary");
Field("LuckyDrawUtils", "m_assetReferenceLibrary", "LuckyDrawAssetReferenceLibrary", false);
InternalMethod("LuckyDrawUtils", "GetAssetLibrary", "LuckyDrawAssetReferenceLibrary");
Method("LuckyDrawAssetReferenceLibrary", "TryGetAsset", "Boolean", "!!0", "String", "LibraryAssetBase&", "Boolean");
InternalMethod("AssetReferenceLibrary", "GetDeviceAssetPath", "String", "LibraryAssetBase");
Method("PetVariantDbfRecord", "get_PetIcon", "String");
Method("DefLoader", "LoadCardDefAsync", "Void", "String", "DefLoader/LoadDefCallback`1<DefLoader/DisposableCardDef>", "Object");
Method("DefLoader/DisposableCardDef", "get_CardDef", "CardDef");
Method("CardDef", "GetPortraitTexturePath", "String", "TAG_PREMIUM", "Boolean", "TAG_PREMIUM&");
Method("Hearthstone.UI.VisualController", "HasState", "Boolean", "String");
Method("UIEvent", ".ctor", "Void", "UIEventType", "PegUIElement");
Field("UIEventType", "RELEASEALL", "UIEventType");
Method("PegUIElementProxy", "get_Owner", "PegUIElement");
Method("PegUIElementProxy", "set_Owner", "Void", "PegUIElement");
Method("Box", "IsInStateWithButtons", "Boolean");
Method("Box", "IsTransitioningToSceneMode", "Boolean");
Method("Box", "ShouldBeShowingState", "Boolean", "Box/State");
Method("Box", "GetRailroadManager", "BoxRailroadManager");
Method("BoxRailroadManager", "ShouldHideButtonType", "Boolean", "Box/ButtonType");
Method("BoxRailroadManager", "ShouldDisableButtonType", "Boolean", "Box/ButtonType");
Method("BoxMenuButton", "SetText", "Void", "String");
Method("PegUIElement", "ClearEventListeners", "Void");
Method("TooltipZone", "ShowBoxTooltip", "Void", "String", "String", "Int32");
Method("GameModeDisplay", "get_IsFinishedLoading", "Boolean");
Method("GameModeDisplay", "GetGameModeSceneDataModel", "Hearthstone.DataModels.GameModeSceneDataModel");
Method("CameraUtils", "FindProjectionCameraForObject", "UnityEngine.Camera", "UnityEngine.GameObject");
Field("UIBButton", "m_WiggleTime", "Single");
Field("UIBButton", "m_DepressTime", "Single");
Field("UIBButton", "m_RaiseTime", "Single");
Field("UIBButton", "m_ClickDownOffset", "UnityEngine.Vector3");
InternalMethod("UIBButton", "Depress", "Void");
InternalMethod("UIBButton", "Raise", "Void");
using var tweenStream = File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!, "Blizzard.T5.iTween.dll"));
using var tweenPe = new PEReader(tweenStream);
MetadataReader tweenMetadata = tweenPe.GetMetadataReader();
var tweenType = tweenMetadata.TypeDefinitions.Select(tweenMetadata.GetTypeDefinition)
    .Single(type => tweenMetadata.GetString(type.Name) == "iTween");
bool stopTweenAvailable = tweenType.GetMethods().Select(tweenMetadata.GetMethodDefinition).Any(method =>
{
    var signature = method.DecodeSignature(names, (object?)null);
    return tweenMetadata.GetString(method.Name) == "StopByName" && (method.Attributes & MethodAttributes.Public) != 0
        && (method.Attributes & MethodAttributes.Static) != 0 && signature.ReturnType == "Void"
        && signature.ParameterTypes.SequenceEqual(new[] { "UnityEngine.GameObject", "String" });
});
if (!stopTweenAvailable) throw new InvalidOperationException("Native arrow tween cancellation API is unavailable");
checks++;
using var textStream = File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!, "Blizzard.T5.UberText.Runtime.dll"));
using var textPe = new PEReader(textStream);
metadata = textPe.GetMetadataReader();
types = metadata.TypeDefinitions.ToDictionary(handle => names.GetTypeFromDefinition(metadata, handle, 0));
Field("UberTextRenderToTexture", "viewMatrix", "UnityEngine.Matrix4x4", false);
Field("UberTextRenderToTexture", "projectionMatrix", "UnityEngine.Matrix4x4", false);
Field("UberTextRenderToTexture", "m_renderTexture", "UnityEngine.RenderTexture", false);
Method("UberTextRenderToTexture", "InitOnRenderObject", "Void", "UnityEngine.GameObject", "UnityEngine.Vector2Int");
Method("UberTextRenderToTexture", "DoRenderToTexture", "Void", "UnityEngine.MeshRenderer");
Method("UberTextRenderToTexture", "Destroy", "Void");
Console.WriteLine($"Passed {checks} native API checks against {Path.GetFileName(path)} (metadata only).");

sealed class SignatureNames : ISignatureTypeProvider<string, object?>
{
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        if (type.IsNested) return GetTypeFromDefinition(reader, type.GetDeclaringType(), 0) + "/" + name;
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name), ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
    public string GetGenericInstantiation(string type, ImmutableArray<string> args) => type + "<" + string.Join(",", args) + ">";
    public string GetByReferenceType(string type) => type + "&";
    public string GetSZArrayType(string type) => type + "[]";
    public string GetArrayType(string type, ArrayShape shape) => type + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetPointerType(string type) => type + "*";
    public string GetPinnedType(string type) => type;
    public string GetModifiedType(string modifier, string type, bool required) => type;
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
}
