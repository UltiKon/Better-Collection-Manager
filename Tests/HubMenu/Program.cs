using System;
using System.Linq;
using System.IO;
using BetterCollectionManager;
using Hearthstone.DataModels;
using Hearthstone.UI;
using UnityEngine;

internal static class Program
{
    private static int s_checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); s_checks++; }
    private static GameObject Child(string name, Transform parent)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
    private static T Template<T>(string name, Transform parent) where T : BoxMenuButton, new()
    {
        T button = Child(name, parent).AddComponent<T>();
        button.m_TextMesh = Child(name + " caption", button.transform).AddComponent<UberText>();
        button.m_ButtonMeshRenderer = button.gameObject.AddComponent<MeshRenderer>();
        button.gameObject.AddComponent<Collider>();
        button.gameObject.AddComponent<TooltipZone>();
        button.SetText(name);
        return button;
    }
    private static Box NewBox()
    {
        var box = new GameObject("NativeBox").AddComponent<Box>();
        Box.Instance = box;
        box.m_CollectionButton = Template<BoxMenuButton>("Collection", box.transform);
        box.m_BattleGroundsButton = Template<BoxScrollButton>("Battlegrounds", box.transform);
        var anchor = Child("DFTAnchor", box.transform);
        box.m_luckyDrawButtonController = Child("DFTController", anchor.transform).AddComponent<LuckyDrawButtonController>();
        Child("ButtonName", anchor.transform);
        return box;
    }
    private static void Main()
    {
        HubMenuSettings.EnsureLoaded();
        var darkmoon = (MelonLoader.MelonPreferences_Entry<bool>)MelonLoader.MelonPreferences.Entries["StaticDarkmoonButton"];
        var brawl = (MelonLoader.MelonPreferences_Entry<bool>)MelonLoader.MelonPreferences.Entries["TavernBrawlInMainMenu"];
        darkmoon.Value = brawl.Value = false;
        var disabled = new HarmonyLib.Harmony();
        HubMenuPatches.Install(disabled);
        Box original = NewBox();
        original.m_luckyDrawButtonController.Show();
        HubMenuPatches.OnUpdate();
        Check(disabled.Patched.Count == 0 && !HubMenuPatches.SwapEnabled, "Disabled preferences must install no replacement hooks");
        Check(original.m_luckyDrawButtonController.AssetLoads == 1 && original.m_luckyDrawButtonController.GetButton() != null,
            "Disabling the DFT experiment must retain the original entry load");
        Check(original.m_BattleGroundsButton.m_TextMesh.Text == "Battlegrounds", "Disabled swap must preserve the main slot");
        darkmoon.Value = brawl.Value = true;
        var harmony = new HarmonyLib.Harmony();
        HubMenuPatches.Install(harmony);
        Check(harmony.Patched.Count == 17 && HubMenuPatches.SwapEnabled, "Both complete patch groups must install");
        CheckDarkmoon();
        CheckAutomaticLogo();
        CheckLogoCache();
        CheckMainSlot();
        CheckLegacyTile();
        CheckMissingAssets();
        CheckFallbackFailures();
        Console.WriteLine($"Passed {s_checks} hub menu, startup suppression, navigation and lifecycle checks.");
    }

    private static void CheckDarkmoon()
    {
        Box box = NewBox();
        var controller = box.m_luckyDrawButtonController;
        int ready = 0;
        int opened = 0;
        Widget.EventListenerDelegate listener = name => { if (name == "LUCKY_DRAW_OPENED") opened++; };
        controller.OnReady += () =>
        {
            // This is the callback that threw in the supplied live-client log.
            controller.GetWidget().RegisterEventListener(listener);
            Check(controller.GetButton() == null, "Ready callbacks must have an event widget without animated button art");
            ready++;
        };
        LuckyDrawManager.Instance.Dirty = true;
        controller.Show();
        var replacement = StaticDarkmoonButton.Find(controller);
        BoxMenuButton button = controller.GetComponentsInChildren<BoxMenuButton>(true).Single();
        Check(replacement != null && controller.AssetLoads == 0, "Startup must bypass native animated prefab loading");
        Widget widget = controller.GetWidget();
        Check(widget == replacement.EventWidget && replacement.EventWidget.Initialized,
            "The ready callback must receive the initialized, empty event widget");
        Check(widget.GetComponentsInChildren<Renderer>(true).Length == 0
            && widget.GetComponentsInChildren<Collider>(true).Length == 0,
            "The event widget must not contain presentation or input components");
        Check(!button.gameObject.activeSelf, "Data that is still refreshing must not expose an invalid offer");
        LuckyDrawManager.Instance.Dirty = false;
        replacement.Refresh();
        Check(button.gameObject.activeSelf && button.IsEnabled(), "Offer data becoming ready must reveal the static entry");
        Check(box.m_CollectionButton.m_TextMesh.Text == "Collection", "Cloning must not relabel Collection");
        Check(button.GetComponent<TooltipZone>().targetObject == button.gameObject
            && button.GetComponent<TooltipZone>().tooltipDisplayLocation == button.transform,
            "Cloned tooltip anchors must not point back to Collection");
        Check(controller.transform.parent.Find("ButtonName").gameObject.activeSelf == false, "Inherited native caption must not overlap the static caption");
        button.Release();
        Check(DeepLinkManager.Opened.Last()[1] == "42", "Static entry must reach the current offer");
        Check(opened == 1, "Static entry must notify the Hub to dismiss its native tooltip notification");
        LuckyDrawManager.Instance.Preferred = 99;
        button.Release();
        Check(DeepLinkManager.Opened.Last()[1] == "99", "Click must re-evaluate the offer ID");
        int clicks = DeepLinkManager.Opened.Count;
        foreach (Action block in new Action[]
        {
            () => box.Waiting = true, () => box.Effects = true, () => box.Transitioning = true,
            () => SceneMgr.Instance.Transition = true, () => DialogManager.Instance.Showing = true,
            () => PopupDisplayManager.Instance.IsShowing = true, () => box.Railroad.Disable = true,
            () => box.Railroad.Hide = true, () => LuckyDrawManager.Instance.Enabled = false,
            () => LuckyDrawManager.Instance.Dirty = true
        })
        {
            block(); replacement.Open();
            Check(DeepLinkManager.Opened.Count == clicks, "Blocked or expired entry must not navigate");
            box.Waiting = box.Effects = box.Transitioning = SceneMgr.Instance.Transition = DialogManager.Instance.Showing
                = PopupDisplayManager.Instance.IsShowing = box.Railroad.Disable = box.Railroad.Hide = LuckyDrawManager.Instance.Dirty = false;
            LuckyDrawManager.Instance.Enabled = true;
        }
        controller.RelayTriggerEvent("DISABLE_INTERACTION"); button.Release();
        Check(DeepLinkManager.Opened.Count == clicks && !button.IsEnabled(), "Native interaction disabling must reach the static button");
        controller.RelayTriggerEvent("ENABLE_INTERACTION");
        Network.LoggedIn = false;
        button.Release();
        Check(DialogManager.Instance.Reconnect != null && DeepLinkManager.Opened.Count == clicks, "Offline entry must use the reconnect dialog");
        Network.LoggedIn = true;
        LuckyDrawManager.Instance.Preferred = 101;
        DialogManager.Instance.Reconnect();
        Check(DeepLinkManager.Opened.Last()[1] == "101", "Reconnect must use fresh offer data");
        controller.Hide(); replacement.Refresh();
        Check(!button.gameObject.activeSelf && button.m_TextMesh.Hidden, "Hidden controller must also hide separately rendered caption surfaces");
        controller.Show(); controller.Show();
        Check(ready == 3 && controller.AssetLoads == 0 && controller.GetComponentsInChildren<BoxMenuButton>(true).Length == 1,
            "Repeated show must reuse one button and preserve ready callbacks without loading art");
        int before = opened;
        button.Release();
        Check(opened == before + 1 && controller.GetWidget() == widget
            && controller.GetComponentsInChildren<WidgetTemplate>(true).Length == 1,
            "Repeated ready callbacks must reuse one widget and not duplicate the native listener");
        controller.GetWidget().RemoveEventListener(listener);
        button.Release();
        Check(opened == before + 1, "Hub teardown must be able to remove its widget listener");
        controller.SetEnable(false); controller.Show();
        Check(!button.gameObject.activeSelf, "Disabled controller must not reveal the replacement");
        controller.SetEnable(true); controller.Show();
        SceneMgr.Instance.Current = SceneMgr.Mode.COLLECTIONMANAGER; replacement.Refresh();
        Check(!button.gameObject.activeSelf, "Entry must be hidden outside the hub");
        SceneMgr.Instance.Current = SceneMgr.Mode.HUB;

        // Serialized captions can be siblings instead of children of the source.
        UberText external = Child("External caption", box.transform).AddComponent<UberText>(); external.Text = "Source";
        box.m_CollectionButton.m_TextMesh = external;
        var clone = NativeHubButtons.Clone(box.m_CollectionButton, controller.transform, "SiblingLabelFixture", "Private caption");
        Check(FilterButtonFeedback.PressBound.Contains(clone), "Static cloned hub buttons bind click feedback before their release actions");
        Check(external.Text == "Source" && clone.m_TextMesh != external && clone.m_TextMesh.transform.IsChildOf(clone.transform),
            "External source captions need an owned copy");

        Box siblingBox = NewBox();
        UberText siblingText = siblingBox.m_CollectionButton.m_TextMesh;
        siblingText.gameObject.name = "ButtonName";
        siblingText.transform.SetParent(siblingBox.m_CollectionButton.transform.parent, true);
        siblingText.TextColor = new Color { a = 0f };
        siblingText.gameObject.SetActive(false);
        siblingBox.m_CollectionButton.m_TextMesh = null;
        siblingBox.m_luckyDrawButtonController.Show();
        var siblingButton = siblingBox.m_luckyDrawButtonController.GetComponentsInChildren<BoxMenuButton>(true).Single();
        Check(siblingBox.m_luckyDrawButtonController.AssetLoads == 0 && siblingButton.m_TextMesh.Text == "Darkmoon Faire",
            "The live client's unwired sibling ButtonName must supply a static DFT caption without loading the animation");
        Check(siblingButton.m_TextMesh.TextColor.a == 1f && siblingText.TextColor.a == 0f && !siblingText.gameObject.activeSelf,
            "A loading/faded source caption must yield a visible private caption without changing its source");
        Check(siblingButton.GetComponentsInChildren<UberText>(true).Length == 1,
            "Sibling-caption cloning must leave exactly one owned caption");

        Box cached = NewBox();
        UberText cachedText = cached.m_CollectionButton.m_TextMesh;
        cachedText.RenderToTexture = true;
        cachedText.RenderOnObject = Child("Source text texture surface", cached.transform);
        cachedText.LocalizedFont = new Font();
        cachedText.FontSize = 31;
        cachedText.CharacterSize = 2f;
        cachedText.gameObject.AddComponent<SourceCaptionReveal>();
        Child("Cached source glyphs", cachedText.transform).AddComponent<MeshRenderer>();
        cachedText.Hide();
        cached.m_luckyDrawButtonController.Show();
        var cachedButton = cached.m_luckyDrawButtonController.GetComponentsInChildren<BoxMenuButton>(true).Single();
        UberText privateText = cachedButton.m_TextMesh;
        Check(!privateText.RenderToTexture && privateText.RenderOnObject == null
            && privateText.GetComponent<SourceCaptionReveal>() == null
            && privateText.transform.childCount == 0,
            "Static captions must not inherit cached texture surfaces, glyphs or source reveal scripts");
        Check(privateText.TrueTypeFont == cachedText.LocalizedFont && privateText.FontSize == 31
            && privateText.CharacterSize == 2f,
            "Fresh captions must retain the native localized typography");
        Check(cachedText.isHidden() && cachedText.RenderToTexture && cachedText.RenderOnObject.activeSelf,
            "Creating the static caption must preserve source rendering and visibility");
        privateText.Hide();
        privateText.gameObject.SetActive(false);
        StaticDarkmoonButton.Find(cached.m_luckyDrawButtonController).Refresh();
        Check(!privateText.isHidden() && privateText.gameObject.activeSelf,
            "A delayed caption hide must recover without toggling the entire entry");

        Box unwired = NewBox();
        unwired.m_CollectionButton.m_TextMesh = null;
        unwired.m_luckyDrawButtonController.Show();
        Check(unwired.m_luckyDrawButtonController.AssetLoads == 0
            && unwired.m_luckyDrawButtonController.GetComponentsInChildren<UberText>(true).Length == 1,
            "An unwired child caption must be reused on the clone instead of duplicated");

        GameObject events = replacement.EventWidget.gameObject;
        UnityEngine.Object.Destroy(controller.gameObject);
        Check(events == null && button == null, "Destroying the native controller must release the replacement and event widget");

        Box alternate = NewBox();
        UnityEngine.Object.Destroy(alternate.m_CollectionButton.m_TextMesh.gameObject);
        alternate.m_CollectionButton.m_TextMesh = null;
        alternate.m_ArenaButton = Template<BoxMenuButton>("Arena", alternate.transform);
        alternate.m_luckyDrawButtonController.Show();
        Check(alternate.m_luckyDrawButtonController.AssetLoads == 0 && alternate.m_ArenaButton.m_TextMesh.Text == "Arena",
            "An unavailable Collection caption must try another plain native template");
    }

    private static void CheckAutomaticLogo()
    {
        var model = new LuckyDrawButtonDataModel
        {
            ButtonItemType = LuckyDrawFeaturedType.Custom,
            ButtonCustomPrefabId = "CURRENT_ENTRY"
        };
        var library = LuckyDrawUtils.AssetReferenceLibrary;
        library.Assets.Clear();
        library.Assets["CURRENT_ENTRY"] = new ResponsiveLibraryAsset
        {
            m_desktopAsset = new PlatformAssetID { m_platformAssetID = "CurrentDesktop.prefab:guid-v1" },
            m_mobileAsset = new PlatformAssetID { m_platformAssetID = "CurrentMobile.prefab:guid-phone" }
        };
        AssetReferenceLibrary.Desktop = true;
        var first = DarkmoonLogoSource.ReadSelection(41, model);
        string resolved = null; int errors = 0;
        DarkmoonLogoSource.Resolve(first, path => resolved = path, _ => errors++);
        Check(resolved == "CurrentDesktop.prefab:guid-v1" && errors == 0,
            "Custom artwork must use the native entry library's current desktop asset address");
        AssetReferenceLibrary.Desktop = false;
        var phone = DarkmoonLogoSource.ReadSelection(41, model);
        DarkmoonLogoSource.Resolve(phone, path => resolved = path, _ => errors++);
        Check(resolved == "CurrentMobile.prefab:guid-phone" && phone.Identity != first.Identity,
            "Device-specific asset selection must produce a distinct source identity");
        AssetReferenceLibrary.Desktop = true;
        library.Assets["CURRENT_ENTRY"].m_desktopAsset.m_platformAssetID = "CurrentDesktop.prefab:guid-v2";
        var redirected = DarkmoonLogoSource.ReadSelection(41, model);
        Check(redirected.Identity != first.Identity && redirected.AssetPath.EndsWith("guid-v2"),
            "A changed asset reference must refresh even when the offer and custom ID remain equal");
        DarkmoonLogoLibrary.Loaded = false;
        var cold = DarkmoonLogoSource.ReadSelection(41, model);
        resolved = null;
        DarkmoonLogoSource.Resolve(cold, path => resolved = path, _ => errors++);
        Check(resolved == null && DarkmoonLogoLibrary.Pending.Count == 1,
            "An unloaded native artwork library must be awaited rather than synchronously loaded by source polling");
        DarkmoonLogoLibrary.Loaded = true;
        DarkmoonLogoLibrary.Pending.Dequeue()();
        Check(resolved == "CurrentDesktop.prefab:guid-v2"
            && DarkmoonLogoSource.ReadSelection(41, model).Identity != cold.Identity,
            "After library warmup, the current device address must be rediscovered automatically");
        string key = DarkmoonLogoSelection.CacheKey("build-one", first.Identity, first.AssetPath);
        Check(key.Length == 64 && key == DarkmoonLogoSelection.CacheKey("build-one", first.Identity, first.AssetPath),
            "Cache filenames must be stable across runs for the same client and source");
        Check(key != DarkmoonLogoSelection.CacheKey("build-two", first.Identity, first.AssetPath)
            && key != DarkmoonLogoSelection.CacheKey("build-one", redirected.Identity, redirected.AssetPath)
            && key != DarkmoonLogoSelection.CacheKey("build-one", DarkmoonLogoSource.ReadSelection(42, model).Identity, first.AssetPath),
            "Client updates, changed asset addresses and new offers must invalidate stale image caches");
        Check(DarkmoonLogoSelection.CacheKey("ab", "c", "d") != DarkmoonLogoSelection.CacheKey("a", "bc", "d"),
            "Cache identity fields must not collide at string boundaries");
        model.ButtonCustomPrefabId = "NEXT_ENTRY";
        Check(first.CustomId == "CURRENT_ENTRY" && first.AssetPath.EndsWith("guid-v1"),
            "Async selection snapshots must not mutate with the live offer model or library mapping");
        DarkmoonLogoSource.Resolve(DarkmoonLogoSource.ReadSelection(42, model), _ => resolved = "WRONG", _ => errors++);
        Check(errors == 1 && resolved != "WRONG", "Missing current artwork must not silently resolve another offer's fallback art");

        model.ButtonItemType = LuckyDrawFeaturedType.Pet; model.ButtonPetVariantID = 73;
        GameDbf.PetVariant.Values[73] = new PetVariantDbfRecord { PetIcon = "NativePetIcon.asset:pet-guid" };
        var pet = DarkmoonLogoSource.ReadSelection(42, model);
        DarkmoonLogoSource.Resolve(pet, path => resolved = path, _ => errors++);
        Check(resolved == "NativePetIcon.asset:pet-guid", "Pet artwork must resolve from the current variant's native icon reference");
        GameDbf.PetVariant.Values[73].PetIcon = "NativePetIcon.asset:pet-guid-2";
        Check(DarkmoonLogoSource.ReadSelection(42, model).Identity != pet.Identity,
            "Pet icon redirection must invalidate its previous source identity");

        model.ButtonItemType = LuckyDrawFeaturedType.HeroSkin; model.ButtonHeroSkinID = "LIVE_HERO";
        var hero = DarkmoonLogoSource.ReadSelection(43, model);
        var request = new DarkmoonLogoRequestState();
        Check(request.Select(hero.Identity), "New featured artwork must schedule a request");
        int oldRevision = request.Revision;
        Check(!request.Select(hero.Identity) && request.Revision == oldRevision,
            "An unchanged offer must not repeatedly schedule the same load");
        int applied = 0;
        DarkmoonLogoSource.Resolve(hero, path => { if (request.IsCurrent(oldRevision)) applied++; resolved = path; }, _ => errors++);
        Check(DefLoader.Instance.Pending.Count == 1 && applied == 0,
            "Hero artwork resolution must use the asynchronous native definition API");
        request.Select(pet.Identity);
        DefLoader.Instance.Complete("Portrait.texture:hero-guid");
        Check(resolved == "Portrait.texture:hero-guid" && applied == 0 && DefLoader.Instance.Disposals == 1,
            "A late hero result must not replace a newer offer, and its native definition must be released");
        int current = request.Revision;
        Check(!request.IsCurrent(oldRevision) && request.IsCurrent(current),
            "Only the current asset request may publish a thumbnail");
        request.Invalidate();
        Check(!request.IsCurrent(current) && request.Select(pet.Identity),
            "Scene teardown must reject late results and allow the same offer to load on return");
        model.ButtonItemType = LuckyDrawFeaturedType.None;
        DarkmoonLogoSource.Resolve(DarkmoonLogoSource.ReadSelection(43, model), _ => resolved = "WRONG", _ => errors++);
        Check(errors == 2 && resolved != "WRONG", "Unsupported future artwork types must preserve static access without wrong art");
    }

    private static void CheckLogoCache()
    {
        byte[] pixels = { 255, 0, 19, 0, 40, 80, 120, 255 };
        byte[] encoded = DarkmoonLogoCache.Encode(2, 1, pixels);
        var decoded = DarkmoonLogoCache.Decode(encoded);
        Check(decoded.Width == 2 && decoded.Height == 1 && decoded.Rgba.SequenceEqual(pixels),
            "Logo cache must preserve RGBA32 dimensions, color and transparency exactly");
        byte[] unknown = (byte[])encoded.Clone(); unknown[0] = 0;
        byte[] oversized = (byte[])encoded.Clone(); BitConverter.GetBytes(int.MaxValue).CopyTo(oversized, 4);
        foreach (byte[] malformed in new[] { Array.Empty<byte>(), unknown, oversized, encoded.Take(encoded.Length - 1).ToArray(), encoded.Concat(new byte[] { 0 }).ToArray() })
        {
            bool rejected = false;
            try { DarkmoonLogoCache.Decode(malformed); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed logo data must be rejected before allocating a native texture");
        }
        foreach (Action invalid in new Action[]
        {
            () => DarkmoonLogoCache.Encode(0, 1, pixels),
            () => DarkmoonLogoCache.Encode(2049, 1, pixels),
            () => DarkmoonLogoCache.Encode(2, 1, new byte[7]),
            () => DarkmoonLogoCache.Encode(2, 1, null)
        })
        {
            bool rejected = false;
            try { invalid(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid logo dimensions or pixel counts must not be saved");
        }
        string directory = Path.Combine(Path.GetTempPath(), "bcm-logo-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "logo.rgba");
            DarkmoonLogoCache.WriteAtomic(path, encoded);
            byte[] changed = (byte[])pixels.Clone(); changed[7] = 127;
            DarkmoonLogoCache.WriteAtomic(path, DarkmoonLogoCache.Encode(2, 1, changed));
            Check(DarkmoonLogoCache.Read(path).Rgba.SequenceEqual(changed) && !File.Exists(path + ".tmp"),
                "Saving over an existing logo must leave a complete cache and remove temporary output");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void CheckMainSlot()
    {
        Box box = NewBox();
        HubMenuPatches.MainButtonsUpdated(box);
        Check(box.m_BattleGroundsButton.m_TextMesh.Text == "Tavern Brawl" && box.m_BattleGroundsButton.m_buttonType == Box.ButtonType.TAVERN_BRAWL,
            "Main slot must have Brawl caption and railroad identity");
        bool feature = false;
        Check(!HubMenuPatches.BrawlFeature(box.m_BattleGroundsButton, ref feature) && feature, "Slot availability must follow Brawl");
        bool download = true;
        Check(!HubMenuPatches.BrawlDownload(box.m_BattleGroundsButton, ref download) && !download, "Brawl must not require a BG module download");
        var click = new UIEvent(UIEventType.RELEASE, box.m_BattleGroundsButton);
        Check(!HubMenuPatches.MainPressed(box, click) && box.BrawlClicks == 1 && box.BaconClicks == 0, "Main slot must route exclusively to Brawl");
        TavernBrawlManager.Instance.Available = false;
        HubMenuPatches.OnUpdate(); HubMenuPatches.MainPressed(box, click);
        Check(!box.m_BattleGroundsButton.IsEnabled() && box.BrawlClicks == 1, "Closed Brawl must disable and block the slot");
        TavernBrawlManager.Instance.Available = true;
        box.Waiting = true; HubMenuPatches.OnUpdate(); HubMenuPatches.MainPressed(box, click);
        Check(!box.m_BattleGroundsButton.IsEnabled() && box.BrawlClicks == 1, "Native data and transition guards must remain in force");
        box.Waiting = false; HubMenuPatches.OnUpdate();
        Check(box.m_BattleGroundsButton.IsEnabled(), "Returning to a ready hub must re-enable Brawl");
        Check(HubMenuPatches.MainPressed(box, new UIEvent(UIEventType.RELEASE, box.m_CollectionButton)), "Other native button dispatch must pass through");
        var unrelated = Template<BoxScrollButton>("Other BG button", box.transform);
        Check(HubMenuPatches.BrawlFeature(unrelated, ref feature), "Feature overrides must be limited to the replaced slot");
    }

    private static GameModeDisplay Modes(bool retainedArt, bool legacyRecord, bool backButton = true)
    {
        SceneMgr.Instance.Current = SceneMgr.Mode.GAME_MODE;
        SceneMgr.Instance.Next = null;
        GameDbf.GameMode.Values.Clear();
        var adventure = new GameModeDbfRecord { ID = 1, LinkedScene = "ADVENTURE", Name = "Adventure" };
        var brawl = new GameModeDbfRecord { ID = 2, LinkedScene = "TAVERN_BRAWL", Name = "Brawl" };
        GameDbf.GameMode.Values.Add(adventure); GameDbf.GameMode.Values.Add(brawl);
        if (legacyRecord) GameDbf.GameMode.Values.Add(new GameModeDbfRecord
        { ID = 7, LinkedScene = "BACON", GameModeButtonState = "BATTLEGROUNDS_ART", Name = "Battlegrounds", EventActive = false });
        var display = new GameObject("NativeModes").AddComponent<GameModeDisplay>();
        display.Active(adventure); display.Active(brawl);
        display.Data.GameModeButtons.Add(new GameModeButtonDataModel { GameModeRecordId = 1 });
        display.Data.GameModeButtons.Add(new GameModeButtonDataModel { GameModeRecordId = 2 });
        display.Data.LastSelectedGameModeRecordId = 2;
        display.Select(display.Data.GameModeButtons[1]);
        if (retainedArt)
            Child("Native tile visuals", display.transform).AddComponent<VisualController>().States.Add("BATTLEGROUNDS_ART");
        if (backButton)
        {
            var back = Child("Back", display.transform).AddComponent<UIBButton>();
            back.gameObject.AddComponent<Collider>();
            back.m_ButtonText = Child("Caption", back.transform).AddComponent<UberText>(); back.m_ButtonText.Text = "Back";
            back.m_RootObject = Child("Plate", back.transform);
            back.m_RootObject.AddComponent<MeshRenderer>();
            display.Back(back);
        }
        HubMenuPatches.ModesInitialized(display);
        display.GetComponent<HubModesMenu>().Refresh();
        return display;
    }

    private static void CheckLegacyTile()
    {
        var display = Modes(true, true);
        Check(display.Data.GameModeButtons.Select(b => b.GameModeRecordId).SequenceEqual(new[] { 1, 7 }), "A retained native BG visual state must restore its old tile and replace Brawl");
        Check(display.ActiveRecords.Any(r => r.ID == 7) && !GameDbf.GameMode.GetRecord(7).EventActive,
            "Legacy row must enter only the local scene list, with no event/DBF mutation");
        Check(display.Data.LastSelectedGameModeRecordId == 1, "Removing the saved Brawl entry must select a surviving mode");
        Check(display.GetComponentsInChildren<UIBButton>(true).Length == 1, "Retained tile must not create a redundant fallback");
        var tag = Hearthstone.Core.Streaming.DownloadTags.Content.Unknown;
        Check(!HubMenuPatches.ModuleForMode(GameDbf.GameMode.GetRecord(7), ref tag) && tag == Hearthstone.Core.Streaming.DownloadTags.Content.Bgs,
            "Restored tile must use the BG module, including native download handling");
        display.Select(display.Data.GameModeButtons[1]); display.Allowed = false;
        Check(HubMenuPatches.NavigateMode(display), "Native locked-mode explanations must remain available");
        display.Allowed = true;
        Check(!HubMenuPatches.NavigateMode(display) && SceneMgr.Instance.Next == SceneMgr.Mode.BACON,
            "Modes entry must run the BG handler and transition even without Hub's event listener");
        SceneMgr.Instance.Next = null;
        Box.Get().BaconNeedsDownload = true;
        HubMenuPatches.NavigateMode(display);
        Check(SceneMgr.Instance.Next == null, "Native download requirement must stop the transition");
        Box.Get().BaconNeedsDownload = false; Box.Get().BaconTutorial = true;
        HubMenuPatches.NavigateMode(display);
        Check(SceneMgr.Instance.Next == null, "Tutorial preview events must not be mistaken for a ready BG scene request");
        Box.Get().BaconTutorial = false;
    }

    private static void CheckMissingAssets()
    {
        foreach (bool legacyRecord in new[] { true, false })
        {
            var display = Modes(false, legacyRecord);
            UIBButton fallback = display.GetComponentsInChildren<UIBButton>(true).Single(b => b.gameObject.name == "BCM_BattlegroundsModes");
            UIBButton back = display.GetComponentsInChildren<UIBButton>(true).Single(b => b.gameObject.name == "Back");
            Check(FilterButtonFeedback.PressBound.Contains(fallback), "The Modes fallback binds press-time button feedback");
            Check(fallback.m_ButtonText.Text == "Battlegrounds" && back.m_ButtonText.Text == "Back", "Missing legacy assets need a private native text entry");
            Check(fallback.transform.position.x + fallback.GetComponent<Collider>().bounds.size.x / 2
                < back.transform.position.x - back.GetComponent<Collider>().bounds.size.x / 2, "Fallback hit area must not overlap Back");
            display.GetComponent<HubModesMenu>().Refresh();
            Check(display.GetComponentsInChildren<UIBButton>(true).Length == 2, "Refreshing Modes must not duplicate fallback buttons");
            back.transform.position = new Vector3(-50, 0, 0);
            back.m_RootObject.SetActive(false);
            back.m_ButtonText.Hide();
            display.GetComponent<HubModesMenu>().Refresh();
            Check(!fallback.gameObject.activeSelf && !fallback.IsEnabled(),
                "A native Back button still outside the viewport must not leave an offscreen active fallback");
            back.transform.position = new Vector3(5, 0, 0);
            back.m_RootObject.SetActive(true);
            back.m_ButtonText.Show();
            back.m_RootObject.GetComponent<MeshRenderer>().Properties.SetFloat("Fade", 1f);
            back.m_RootObject.layer = 12;
            display.GetComponent<HubModesMenu>().Refresh();
            var plate = fallback.transform.Find("Plate");
            Check(fallback.gameObject.activeInHierarchy && plate.gameObject.activeSelf
                && plate.GetComponent<MeshRenderer>().Properties.GetFloat("Fade") == 1f && !fallback.m_ButtonText.Hidden && plate.gameObject.layer == 12,
                "The fallback must follow the completed native reveal, including artwork fades and caption visibility");
            float oldX = fallback.transform.position.x;
            back.transform.position += new Vector3(1, 0, 0);
            display.GetComponent<HubModesMenu>().Refresh();
            Check(Math.Abs(fallback.transform.position.x - oldX - 1f) < .0001f,
                "Fallback layout must follow Back after its loading position changes");
            SceneMgr.Instance.Next = null; fallback.Release();
            Check(SceneMgr.Instance.Next == SceneMgr.Mode.BACON, "Fallback must reach BG without historical art");
            NetCache.Instance.Features.Games.Enabled = false;
            display.GetComponent<HubModesMenu>().Refresh();
            Check(!fallback.IsEnabled(), "Server-disabled BG must remain inaccessible");
            NetCache.Instance.Features.Games.Enabled = true;
            UnityEngine.Object.Destroy(display.gameObject);
            Check(fallback == null, "Modes teardown must destroy its fallback");
        }
    }

    private static void CheckFallbackFailures()
    {
        var display = Modes(false, false, false);
        Check(!HubMenuPatches.SwapEnabled && Box.Get().m_BattleGroundsButton.m_TextMesh.Text == "Battlegrounds"
            && Box.Get().m_BattleGroundsButton.m_buttonType == Box.ButtonType.BACON,
            "If even native fallback cloning fails, restore original BG access");
        Check(display.Data.GameModeButtons.Any(b => b.GameModeRecordId == 2), "A failed fallback must leave the native Modes list intact");
        SceneMgr.Instance.Current = SceneMgr.Mode.HUB;
        Box box = NewBox();
        UnityEngine.Object.Destroy(box.m_CollectionButton.m_TextMesh.gameObject);
        box.m_CollectionButton.m_TextMesh = null;
        box.m_luckyDrawButtonController.Show();
        Check(box.m_luckyDrawButtonController.AssetLoads == 1 && box.m_luckyDrawButtonController.GetButton() != null,
            "Failed static entry must fall back to a working native DFT button");
        Check(box.m_luckyDrawButtonController.GetWidget() != null,
            "Fallback must expose the original native widget as well as its button");
    }
}
