using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blizzard.T5.AssetManager
{
    public sealed class AssetHandle<T> : IDisposable where T : UnityEngine.Object
    {
        public T Asset;
        public void Dispose() { }
    }
}
public static class ShopPrefabs
{
    public const string ShopSendToBamPrefab = "StoreSendToBAM.prefab:d518a59d394e8cf429cd22c56dd5540d";
}
public class CraftingButton : Component
{
    public bool Enabled = true;
    public bool IsButtonEnabled() => Enabled;
    public UberText labelText;
    public MeshRenderer buttonRenderer;
    public Material enabledMaterial;
}
public sealed class DisenchantButton : CraftingButton
{
    public int NativeReleases;
    private void OnRelease() { NativeReleases++; }
}
public sealed class CraftingUI : Component
{
    public DisenchantButton m_buttonDisenchant;
    internal static GameObject MakePrefab()
    {
        var root = new GameObject("CraftingUI asset"); root.SetActive(false);
        var ui = root.AddComponent<CraftingUI>();
        var button = new GameObject("Native disenchant button"); button.transform.SetParent(root.transform, false);
        ui.m_buttonDisenchant = button.AddComponent<DisenchantButton>();
        var art = new GameObject("Native crafting face"); art.transform.SetParent(button.transform, false);
        art.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(3f, .01f, 1.2f)) };
        ui.m_buttonDisenchant.buttonRenderer = art.AddComponent<MeshRenderer>();
        ui.m_buttonDisenchant.enabledMaterial = new Material { name = "Native enabled crafting stone", renderQueue = 3300 };
        var label = new GameObject("Native crafting label"); label.transform.SetParent(button.transform, false);
        ui.m_buttonDisenchant.labelText = label.AddComponent<UberText>();
        return root;
    }
}
namespace Hearthstone.UI
{
    public sealed class WidgetInstance : Component
    {
        public sealed class WeakReference { public string AssetString { get; set; } }
        private WeakReference m_widgetTemplate;
        public void Reference(string value) { m_widgetTemplate = new WeakReference { AssetString = value }; }
    }
}
public sealed class StoreQuantityPrompt : Component
{
    public GameObject m_root;
    public UberText m_inputTextFieldBounds, m_instructionText;
    internal const string Asset = "StoreQuantityPrompt.prefab:quantity-fixture";
    internal static GameObject MakePrefab(Transform parent = null)
    {
        var root = new GameObject("NativeQuantityPrompt"); root.SetActive(false);
        if (parent != null) root.transform.SetParent(parent, false);
        var prompt = root.AddComponent<StoreQuantityPrompt>();
        prompt.m_root = new GameObject("QuantityPanel"); prompt.m_root.transform.SetParent(root.transform, false);
        GameObject Art(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name); go.transform.SetParent(prompt.m_root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(center, size) };
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = name, renderQueue = 4300 };
            return go;
        }
        Art("Outer frame", Vector3.zero, new Vector3(7, .02f, 2));
        Art("Native dark input", new Vector3(0, 0, -.5f), new Vector3(6, .02f, .6f));
        Art("Pack icon", new Vector3(-2.5f, 0, -.5f), new Vector3(.25f, .02f, .25f));
        var slot = new GameObject("Native input bounds"); slot.transform.SetParent(prompt.m_root.transform, false);
        slot.transform.localPosition = new Vector3(0, 0, -.5f);
        prompt.m_inputTextFieldBounds = slot.AddComponent<UberText>();
        prompt.m_inputTextFieldBounds.LayoutBounds = new Bounds(Vector3.zero, new Vector3(5, .01f, .4f));
        prompt.m_inputTextFieldBounds.RenderQueue = 1400;
        var title = new GameObject("Quantity instruction"); title.transform.SetParent(prompt.m_root.transform, false);
        prompt.m_instructionText = title.AddComponent<UberText>();
        if (parent != null) root.SetActive(true);
        return root;
    }
}
public sealed class CardInfoPane : Component
{
    public UberText m_flavorText, m_setName;
    public GameObject m_standardTheming, m_wildTheming;
    public Component m_trialCardsInspectPlateWidget;
    internal static GameObject MakePrefab(Transform parent = null)
    {
        var root = new GameObject("NativeCardInfoPane"); root.SetActive(false);
        if (parent != null) root.transform.SetParent(parent, false);
        var pane = root.AddComponent<CardInfoPane>();
        GameObject Paper(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(6, .02f, 4)) };
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = name, renderQueue = 4300 };
            return go;
        }
        pane.m_standardTheming = Paper("Standard flavor parchment"); pane.m_wildTheming = Paper("Wild flavor parchment");
        var text = new GameObject("Native flavor text"); text.transform.SetParent(root.transform, false);
        pane.m_flavorText = text.AddComponent<UberText>();
        pane.m_flavorText.LayoutBounds = new Bounds(new Vector3(0, 0, .3f), new Vector3(4.8f, .01f, 2.4f));
        pane.m_flavorText.RenderQueue = 1450;
        var set = new GameObject("Category band"); set.transform.SetParent(root.transform, false);
        set.transform.localPosition = new Vector3(0, 0, -1.4f);
        pane.m_setName = set.AddComponent<UberText>(); pane.m_setName.RenderQueue = 1700;
        var artist = Paper("Artist ribbon"); artist.transform.localPosition = new Vector3(0, 0, -3);
        var gem = Paper("Rarity gem"); gem.transform.localPosition = new Vector3(0, 0, -2);
        if (parent != null) root.SetActive(true);
        return root;
    }
}
public sealed class StoreSendToBAM : Component
{
    public GameObject m_sendToBAMRoot, m_midSection;
    public Component m_miniSummary;
    public MultiSliceElement m_allSections;
    public UIBButton m_okayButton, m_cancelButton;
    public UberText m_headlineText, m_messageText;
    internal static GameObject MakePrefab(Transform parent = null)
    {
        var root = new GameObject("NativeProductInformationPrefab"); root.SetActive(false);
        if (parent != null) root.transform.SetParent(parent, false);
        var popup = root.AddComponent<StoreSendToBAM>();
        GameObject Child(string name, Transform owner)
        { var child = new GameObject(name); child.transform.SetParent(owner, false); return child; }
        void Art(GameObject obj, float width, float height)
        {
            obj.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(width, .02f, height)) };
            obj.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = "Native parchment", renderQueue = 4600 };
        }
        popup.m_sendToBAMRoot = Child("ProductInformation", root.transform);
        var frame = popup.m_sendToBAMRoot.transform;
        frame.localRotation = new Quaternion(0, 0, 1, 0);
        popup.m_midSection = Child("Parchment", frame); Art(popup.m_midSection, 6, 4);
        var header = Child("Header", frame); Art(header, 4, .8f);
        popup.m_headlineText = Child("HeaderText", header.transform).AddComponent<UberText>();
        popup.m_headlineText.transform.localRotation = new Quaternion(0, 0, 1, 0);
        popup.m_headlineText.RenderQueue = 1700;
        popup.m_headlineText.Text = "Product Information";
        popup.m_messageText = Child("Message", popup.m_midSection.transform).AddComponent<UberText>();
        popup.m_messageText.RenderQueue = 1450;
        var footer = Child("ButtonFrame", frame); Art(footer, 5, .8f);
        var divider = Child("Native footer center divider", footer.transform); Art(divider, .15f, 1.0f);
        divider.GetComponent<Renderer>().sharedMaterial = new Material { name = "Native metal footer", renderQueue = 5000 };
        UIBButton Button(string name, float x)
        {
            var go = Child(name, footer.transform); go.transform.localPosition = new Vector3(x, 0, 0);
            Art(go, 2, .5f); var button = go.AddComponent<UIBButton>();
            button.m_ButtonText = Child("Caption", go.transform).AddComponent<UberText>();
            button.m_ButtonText.RenderQueue = 1700;
            button.m_ButtonText.transform.localRotation = new Quaternion(0, 0, 1, 0);
            button.m_ButtonText.Text = name; return button;
        }
        popup.m_okayButton = Button("Save", -1.2f); popup.m_cancelButton = Button("Cancel", 1.2f);
        popup.m_allSections = popup.m_sendToBAMRoot.AddComponent<MultiSliceElement>();
        popup.m_allSections.OnUpdateSlices = () => {
            float extent = popup.m_midSection.transform.localScale.z * 2;
            header.transform.localPosition = new Vector3(0, 0, extent + .4f);
            footer.transform.localPosition = new Vector3(0, 0, -extent - .4f);
        };
        popup.m_allSections.UpdateSlices();
        if (parent != null) root.SetActive(true);
        return root;
    }
}
public struct BlurParameters
{
    public float Amount, Radius;
    public BlurParameters(float amount, float radius) { Amount = amount; Radius = radius; }
}
public struct ScreenEffectParameters
{
    public static readonly ScreenEffectParameters BlurVignettePerspective = new ScreenEffectParameters();
    public BlurParameters Blur;
}
public sealed class ScreenEffectsHandle
{
    public static readonly HashSet<ScreenEffectsHandle> Active = new HashSet<ScreenEffectsHandle>();
    public readonly object Owner;
    public int Starts, Stops;
    public ScreenEffectParameters Parameters;
    public ScreenEffectsHandle(object owner) { Owner = owner; }
    public void StartEffect(ScreenEffectParameters effect, Action callback = null)
    { Starts++; Parameters = effect; Active.Add(this); }
    public void StopEffect(Action callback = null) { Stops++; Active.Remove(this); }
}

public sealed class CardListPanel : Component
{
    public static bool FailNextCreate;
    public static bool IncludeHoverGlow;
    public static bool ArrowHasPressRoot;
    public static bool ArrowFaceOnRoot;
    public NestedPrefab m_leftArrowNested, m_rightArrowNested;
    internal static GameObject MakePrefab()
    {
        var root = new GameObject("Native hero picker card list"); root.SetActive(false);
        var panel = root.AddComponent<CardListPanel>();
        var left = new GameObject("Left nested"); left.transform.SetParent(root.transform, false);
        left.transform.localRotation = new Quaternion(0, 1, 0, 0);
        panel.m_leftArrowNested = left.AddComponent<NestedPrefab>(); panel.m_leftArrowNested.m_Prefab = "HeroPickerLeft.prefab:fixture";
        var right = new GameObject("Right nested"); right.transform.SetParent(root.transform, false);
        panel.m_rightArrowNested = right.AddComponent<NestedPrefab>(); panel.m_rightArrowNested.m_Prefab = "HeroPickerRight.prefab:fixture";
        return root;
    }
    internal static GameObject MakeArrow(bool left)
    {
        if (FailNextCreate) { FailNextCreate = false; throw new InvalidOperationException("Native arrow unavailable"); }
        var root = new GameObject("Hero picker triangle"); root.SetActive(false);
        var button = root.AddComponent<UIBButton>();
        button.m_ClickDownOffset = ArrowHasPressRoot ? new Vector3(0, -.075f, 0) : Vector3.zero;
        button.m_DepressTime = ArrowHasPressRoot ? .08f : 0f; button.m_RaiseTime = ArrowHasPressRoot ? .13f : 0f;
        root.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero,
            left ? new Vector3(1.5f, .02f, 2f) : new Vector3(.7f, .02f, 2.5f)) };
        root.AddComponent<MeshRenderer>().enabled = false;
        var art = new GameObject("Native gold triangle"); art.transform.SetParent(root.transform, false);
        if (ArrowHasPressRoot) button.m_RootObject = art;
        button.m_ButtonText = art.AddComponent<UberText>(); button.m_ButtonText.Text = "";
        var face = ArrowFaceOnRoot ? root : art;
        var mesh = face.GetComponent<MeshFilter>() ?? face.AddComponent<MeshFilter>();
        mesh.sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(.7f, .02f, 1)), normals = new[] { new Vector3(0, 1, 0) } };
        var renderer = face.GetComponent<MeshRenderer>() ?? face.AddComponent<MeshRenderer>(); renderer.enabled = true;
        renderer.sharedMaterial = new Material { name = left ? "Native hero left triangle" : "Native hero right triangle", renderQueue = 3200 };
        art.AddComponent<BoxCollider>().size = new Vector3(.7f, .1f, 1);
        var hit = root.AddComponent<BoxCollider>(); hit.size = new Vector3(.7f, .1f, 1); hit.center = new Vector3(0, .03f, 0);
        var highlight = root.AddComponent<UIBHighlight>();
        if (IncludeHoverGlow)
        {
            var lit = new GameObject("Native arrow lit face"); lit.transform.SetParent(root.transform, false);
            lit.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            lit.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = "Native arrow light", renderQueue = 3201 };
            lit.SetActive(false); highlight.m_MouseOverHighlight = lit;
        }
        highlight.m_MouseOverSound = "Metal_Hover.prefab:fixture"; highlight.m_MouseOutSound = "Metal_Out.prefab:fixture";
        highlight.m_MouseDownSound = "Metal_Down.prefab:fixture"; highlight.m_MouseUpSound = "Metal_Up.prefab:fixture";
        highlight.m_ReleaseDragSound = "Metal_Drag.prefab:fixture";
        return root;
    }
}
public sealed class DialogManager
{
    public sealed class DialogTypeMapping { public string m_type = "CARD_LIST", m_prefabName = "CardListPopup.prefab:fixture"; }
    private readonly System.Collections.Generic.List<DialogTypeMapping> m_typeMapping = new System.Collections.Generic.List<DialogTypeMapping> { new DialogTypeMapping() };
    private static readonly DialogManager Instance = new DialogManager();
    public static DialogManager Get() => Instance;
    public AlertPopup.PopupInfo Popup;
    public void ShowPopup(AlertPopup.PopupInfo info) => Popup = info;
}
public sealed class PagingArrow : Component
{
    public static bool HighlightOnlyArt;
    public GameObject m_pagingArrowHighlight;
    public void ShowHighlight() => m_pagingArrowHighlight.SetActive(true);
    public void HideHighlight() => m_pagingArrowHighlight.SetActive(false);
    internal static GameObject MakePrefab()
    {
        var root = new GameObject("PagingArrow"); var arrow = root.AddComponent<PagingArrow>();
        arrow.m_pagingArrowHighlight = new GameObject("Native arrow flash"); arrow.m_pagingArrowHighlight.transform.SetParent(root.transform, false);
        var art = HighlightOnlyArt ? arrow.m_pagingArrowHighlight : new GameObject("Native book arrow face");
        if (!HighlightOnlyArt) art.transform.SetParent(root.transform, false);
        art.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(.6f, .02f, 1)) };
        art.AddComponent<MeshRenderer>().sharedMaterial = new Material();
        arrow.m_pagingArrowHighlight.SetActive(false); return root;
    }
}
internal static class NativeCardPartsFixture
{
    internal static GameObject MakePrefab()
    {
        var root = new GameObject("Native minion template"); var actor = root.AddComponent<Actor>();
        var runes = new GameObject("Native rune banner"); runes.transform.SetParent(root.transform, false);
        actor.m_cardRuneBanner = runes.AddComponent<CardRuneBanner>();
        GameObject Part(string name, float x)
        {
            var part = new GameObject(name); part.transform.SetParent(root.transform, false);
            part.transform.localPosition = new Vector3(x, 0, -1);
            part.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(.4f, .02f, .4f)) };
            part.AddComponent<MeshRenderer>().sharedMaterial = new Material(); return part;
        }
        actor.AttackGem = Part("Native attack gem", -1).AddComponent<GemObject>();
        actor.HealthGem = Part("Native health gem", 1).AddComponent<GemObject>();
        // Set the object properties again after adding GemObject.
        actor.m_attackTextMesh = actor.AttackGem.gameObject.AddComponent<UberText>();
        actor.m_healthTextMesh = actor.HealthGem.gameObject.AddComponent<UberText>();
        actor.m_eliteObject = Part("Native dragon ornament", 0);
        return root;
    }
}

public sealed class AdjustBuyingAmount : MonoBehaviour
{
    private PegUIElement m_incrementClickable, m_decrementClickable;
    private UberText m_buyingAmountText;
    internal const string Asset = "AdjustBuyingAmount.prefab:shop-quantity-fixture";
    internal static GameObject MakePrefab(Transform parent = null)
    {
        var root = new GameObject("NativeShopQuantity"); root.SetActive(false);
        root.transform.SetParent(parent, false);
        var control = root.AddComponent<AdjustBuyingAmount>();
        PegUIElement Button(string name, float x)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(x, 0, 0);
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh { bounds = new Bounds(Vector3.zero, new Vector3(.8f, .02f, 1f)) };
            go.AddComponent<MeshRenderer>().sharedMaterial = new Material { name = "NativeShopQuantityGold", renderQueue = 3700 };
            go.AddComponent<BoxCollider>().size = new Vector3(.8f, .02f, 1f);
            return go.AddComponent<PegUIElement>();
        }
        control.m_decrementClickable = Button("ShopMinus", -1f);
        control.m_incrementClickable = Button("ShopPlus", 1f);
        var label = new GameObject("ShopAmount"); label.transform.SetParent(root.transform, false);
        control.m_buyingAmountText = label.AddComponent<UberText>();
        control.m_buyingAmountText.Text = "2"; control.m_buyingAmountText.UpdateNow(true);
        if (parent != null) root.SetActive(true);
        return root;
    }
}
