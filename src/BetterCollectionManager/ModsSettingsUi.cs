using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Adds an entry to the native options without changing its settings.</summary>
    internal sealed class ModsSettingsUi : MonoBehaviour
    {
        // Initial 390 -> 350 correction, refined against the measured metal
        // frames (Signature 354 px, Mods 351 px). Keep both adjustments relative
        // to the UI size, including the small optical shift to the right.
        private const float SignatureWidthScale = (350f / 390f) * (354f / 351f);
        private const float SignatureCenterOffset = 1.5f / 354f;

        private OptionsMenu m_owner;
        private UIBButton m_button;
        private ModsSettingsMenu m_menu;
        private Vector3 m_buttonScale, m_captionScale, m_captionPosition;
        private bool m_layoutPending;
        private int m_screenWidth, m_screenHeight;

        public static void Attach(OptionsMenu owner)
        {
            if (owner == null || owner.m_signatureTextDisplayDropdown == null) return;
            try
            {
                ModsSettingsUi ui = owner.GetComponent<ModsSettingsUi>();
                if (ui == null) ui = owner.gameObject.AddComponent<ModsSettingsUi>();
                ui.m_owner = owner;
                if (ui.m_button == null) ui.CreateButton();
                ui.UpdateLayout();
            }
            catch (Exception ex) { DebugLog.Exception("Mods options button setup failed", ex); }
        }

        private void CreateButton()
        {
            UIBButton template = m_owner.m_privacyButton ?? m_owner.m_miscellaneousButton;
            if (template == null) throw new InvalidOperationException("Options button template is missing");
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
            go.name = "BCM_ModsButton";
            // Preserve the native button's dimensions even if its original
            // container has a different transform or is hidden in this scene.
            go.transform.SetParent(m_owner.transform, true);
            m_button = go.GetComponent<UIBButton>();
            // Awake on the cloned UIBHighlight registers its own highlight and
            // sound listeners. Runtime delegates from Options are not serialized
            // into the clone; clearing listeners here removes native feedback.
            m_button.BubbleUpEvent = string.Empty;
            if (m_button.m_ButtonText != null) m_button.m_ButtonText.GameStringLookup = false;
            m_button.SetText("Mods");
            m_button.SetEnabled(true, true);
            FilterButtonFeedback.BindPressSound(m_button);
            m_button.AddEventListener(UIEventType.RELEASE, _ => Open());
            go.SetActive(true);
            m_buttonScale = m_button.transform.localScale;
            if (m_button.m_ButtonText != null)
            {
                m_captionScale = m_button.m_ButtonText.transform.localScale;
                m_captionPosition = m_button.m_ButtonText.transform.localPosition;
            }
            ModsButtonTrim.Attach(m_owner, m_button);
        }

        private void LateUpdate()
        {
            if (m_owner != null && m_owner.IsShown() && m_button != null
                && (m_layoutPending || m_screenWidth != Screen.width || m_screenHeight != Screen.height))
                UpdateLayout();
        }

        private void UpdateLayout()
        {
            m_layoutPending = !PlaceButton();
            m_screenWidth = Screen.width;
            m_screenHeight = Screen.height;
        }

        private bool PlaceButton()
        {
            if (m_button == null) return false;
            DropdownControl signature = m_owner.m_signatureTextDisplayDropdown;
            GameObject anchor = signature.m_selectedItem != null
                ? signature.m_selectedItem.gameObject : signature.gameObject;
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_owner.gameObject);
            if (camera == null || !TryHitRect(anchor, camera, out Rect signatureRect)) return false;
            // Include the dropdown arrow when locating the Signature field.
            if (signature.m_button != null && TryHitRect(signature.m_button.gameObject, camera, out Rect arrowRect))
                signatureRect = Rect.MinMaxRect(Mathf.Min(signatureRect.xMin, arrowRect.xMin),
                    Mathf.Min(signatureRect.yMin, arrowRect.yMin), Mathf.Max(signatureRect.xMax, arrowRect.xMax),
                    Mathf.Max(signatureRect.yMax, arrowRect.yMax));
            UIBButton privacy = m_owner.m_privacyButton;
            if (privacy == null || !TryHitRect(privacy.gameObject, camera, out Rect privacyRect)) return false;
            m_button.transform.localScale = m_buttonScale;
            if (!TryHitRect(m_button.gameObject, camera, out Rect buttonRect)) return false;
            // Only stable hit geometry participates in layout, keeping native
            // hover/press artwork changes out of the width calculation.
            float width = signatureRect.width * SignatureWidthScale;
            if (!(width > 0f)) return false;
            float widthScale = width / buttonRect.width;
            Vector3 scale = m_buttonScale;
            scale.x *= widthScale;
            m_button.transform.localScale = scale;
            UberText caption = m_button.m_ButtonText;
            if (caption != null)
            {
                // Resize the frame/hit area while retaining the native glyph
                // proportions; recenter the caption by its visible ink.
                Vector3 textScale = m_captionScale;
                textScale.x /= widthScale;
                caption.transform.localScale = textScale;
                caption.transform.localPosition = m_captionPosition;
                caption.Alignment = UberText.AlignmentOptions.Center;
                caption.Anchor = UberText.AnchorOptions.Middle;
                caption.UpdateNow(true);
            }
            if (!TryHitRect(m_button.gameObject, camera, out buttonRect)) return false;
            Vector3 screen = camera.WorldToScreenPoint(m_button.transform.position);
            Vector3 target = screen;
            target.x += signatureRect.center.x - buttonRect.center.x + width * SignatureCenterOffset;
            target.y += privacyRect.center.y - buttonRect.center.y;
            m_button.transform.position += camera.ScreenToWorldPoint(target) - camera.ScreenToWorldPoint(screen);
            if (caption != null && TryHitRect(m_button.gameObject, camera, out buttonRect)
                && CounterTextLayout.TryScreenInkRect(caption, camera, out Rect ink))
            {
                Vector3 from = camera.WorldToScreenPoint(caption.transform.position);
                Vector3 to = from;
                to.x += buttonRect.center.x - ink.center.x;
                to.y += buttonRect.center.y - ink.center.y;
                caption.transform.position += camera.ScreenToWorldPoint(to) - camera.ScreenToWorldPoint(from);
            }
            return true;
        }

        private static bool TryHitRect(GameObject go, Camera camera, out Rect rect)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider == null || !collider.enabled || collider.bounds.size.sqrMagnitude <= 0f)
            {
                rect = default(Rect);
                return false;
            }
            Bounds bounds = collider.bounds;
            Vector3 min = bounds.min, max = bounds.max;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 p = camera.WorldToScreenPoint(new Vector3(
                            x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z));
                        left = Mathf.Min(left, p.x); right = Mathf.Max(right, p.x);
                        bottom = Mathf.Min(bottom, p.y); top = Mathf.Max(top, p.y);
                    }
            rect = Rect.MinMaxRect(left, bottom, right, top);
            return rect.width > 0f && rect.height > 0f;
        }

        private void Open()
        {
            if (m_owner == null || !m_owner.IsShown()) return;
            try
            {
                if (m_menu == null)
                {
                    GameObject root = new GameObject("BCM_ModsMenu");
                    m_menu = root.AddComponent<ModsSettingsMenu>();
                    if (!m_menu.Ready) throw new InvalidOperationException("Mods menu could not be initialized");
                }
                m_menu.Owner = m_owner;
                m_owner.Hide(false); // Preserve the native return-to-game-menu callback.
                m_menu.Show(false);
            }
            catch (Exception ex)
            {
                if (m_menu != null) UnityEngine.Object.Destroy(m_menu.gameObject);
                m_menu = null;
                if (!m_owner.IsShown()) m_owner.Show();
                DebugLog.Exception("Mods menu open failed", ex);
            }
        }

        private void OnDestroy()
        {
            if (m_menu != null) UnityEngine.Object.Destroy(m_menu.gameObject);
        }
    }

    /// <summary>Copy the Graphics field's static metal surround, without its controls.</summary>
    internal static class ModsButtonTrim
    {
        public static void Attach(OptionsMenu owner, UIBButton button)
        {
            GameObject trim = null;
            try
            {
                if (button.m_RootObject == null)
                    throw new InvalidOperationException("The native animated button face is missing");
                var original = new List<MeshRenderer>();
                foreach (MeshRenderer renderer in button.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!IsArtwork(renderer) || Inside(renderer.transform, button.m_RootObject?.transform)
                        || IsFeedback(renderer.transform, button.transform)) continue;
                    original.Add(renderer);
                }
                if (!TryBounds(original, button.transform, out Bounds destination))
                    throw new InvalidOperationException("The button's separate metal surround was not found");

                List<MeshRenderer> source = FindGraphicsTrim(owner);
                if (!TryBounds(source, button.transform, out Bounds sourceBounds))
                    throw new InvalidOperationException("The Graphics field's separate metal surround was not found");

                int vertical = destination.size.z >= destination.size.y ? 2 : 1;
                if (sourceBounds.size.x <= 0f || sourceBounds.size[vertical] <= 0f)
                    throw new InvalidOperationException("The Graphics metal surround has no usable bounds");

                trim = new GameObject("BCM_ModsMetalTrim");
                trim.transform.SetParent(button.transform, false);
                foreach (MeshRenderer renderer in source)
                {
                    // Copy only render data, not GameObjects with native scripts,
                    // input colliders, dropdown arrows, labels or callbacks.
                    GameObject part = new GameObject(renderer.gameObject.name);
                    part.layer = button.gameObject.layer;
                    part.transform.SetParent(renderer.transform, false);
                    part.transform.SetParent(trim.transform, true);
                    part.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    MeshRenderer copy = part.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = renderer.sharedMaterials;
                    copy.sortingLayerID = renderer.sortingLayerID;
                    copy.sortingOrder = renderer.sortingOrder;
                    copy.shadowCastingMode = renderer.shadowCastingMode;
                    copy.receiveShadows = renderer.receiveShadows;
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    copy.SetPropertyBlock(properties);
                }
                // Fit to the original surround in button space. The accepted
                // button width, caption, collider and animated gold face stay
                // independent of the decoration and of the source field.
                Vector3 scale = Vector3.one;
                scale.x = destination.size.x / sourceBounds.size.x;
                scale[vertical] = destination.size[vertical] / sourceBounds.size[vertical];
                trim.transform.localScale = scale;
                trim.transform.localPosition = destination.center - Vector3.Scale(sourceBounds.center, scale);
                foreach (MeshRenderer renderer in original) renderer.enabled = false;
            }
            catch (Exception ex)
            {
                if (trim != null) UnityEngine.Object.Destroy(trim);
                DebugLog.Exception("Mods metal trim unavailable; keeping native button", ex);
            }
        }

        private static List<MeshRenderer> FindGraphicsTrim(OptionsMenu owner)
        {
            foreach (DropdownControl control in new[] { owner.m_graphicsQuality, owner.m_graphicsFps, owner.m_graphicsRes })
            {
                if (control == null || !control.gameObject.activeInHierarchy) continue;
                Transform root = control.transform;
                while (root != null && root != owner.transform)
                {
                    var renderers = new List<MeshRenderer>();
                    foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(false))
                    {
                        Transform part = renderer.transform;
                        if (!IsArtwork(renderer) || Inside(part, control.m_selectedItem?.transform)
                            || Inside(part, control.m_button?.transform) || Inside(part, control.m_menu?.transform)
                            || Inside(part, control.m_menuItemTemplate?.transform)
                            || Inside(part, control.m_menuItemContainer?.transform)
                            || Inside(part, control.m_cancelCatcher?.transform) || IsFeedback(part, root)) continue;
                        renderers.Add(renderer);
                    }
                    if (renderers.Count > 0) return renderers;
                    // Some prefabs put the surround beside the dropdown. Only
                    // inspect a wrapper belonging to this one Graphics field.
                    Transform parent = root.parent;
                    if (parent == null || parent == owner.transform
                        || parent.GetComponentsInChildren<DropdownControl>(true).Length != 1) break;
                    root = parent;
                }
            }
            return new List<MeshRenderer>();
        }

        private static bool Inside(Transform part, Transform root) => root != null && (part == root || part.IsChildOf(root));

        private static bool IsArtwork(MeshRenderer renderer) => renderer.enabled
            && renderer.GetComponentInParent<UberText>() == null
            && renderer.GetComponent<MeshFilter>()?.sharedMesh != null;

        private static bool IsFeedback(Transform part, Transform root)
        {
            foreach (UIBHighlight highlight in root.GetComponentsInChildren<UIBHighlight>(true))
                if (Inside(part, highlight.m_MouseOverHighlight?.transform)
                    || Inside(part, highlight.m_MouseDownHighlight?.transform)
                    || Inside(part, highlight.m_SelectedHighlight?.transform)
                    || Inside(part, highlight.m_MouseOverSelectedHighlight?.transform)
                    || Inside(part, highlight.m_DraggingHighlight?.transform)
                    || Inside(part, highlight.m_HoldHighlight?.transform)) return true;
            return false;
        }

        private static bool TryBounds(List<MeshRenderer> renderers, Transform reference, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool found = false;
            foreach (MeshRenderer renderer in renderers)
            {
                Bounds mesh = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 point = reference.InverseTransformPoint(renderer.transform.TransformPoint(new Vector3(
                                x == 0 ? mesh.min.x : mesh.max.x, y == 0 ? mesh.min.y : mesh.max.y,
                                z == 0 ? mesh.min.z : mesh.max.z)));
                            if (!found) bounds = new Bounds(point, Vector3.zero);
                            else bounds.Encapsulate(point);
                            found = true;
                        }
            }
            return found && bounds.size.x > 0f;
        }
    }

    internal static class ModsSettingsPatches
    {
        public static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.PatchAll(typeof(OptionsShowPatch));
                harmony.PatchAll(typeof(GameMenuShowPatch));
                harmony.PatchAll(typeof(FpsItemsClearedPatch));
            }
            catch (Exception ex) { DebugLog.Exception("Mods settings menu patches unavailable", ex); }
        }

        [HarmonyPatch(typeof(OptionsMenu), "Show")]
        private static class OptionsShowPatch
        {
            [HarmonyPostfix] public static void Postfix(OptionsMenu __instance)
            {
                CustomFpsControl.Attach(__instance);
                ModsSettingsUi.Attach(__instance);
            }
        }

        [HarmonyPatch(typeof(DropdownControl), "clearItems")]
        private static class FpsItemsClearedPatch
        {
            [HarmonyPostfix] public static void Postfix(DropdownControl __instance) => CustomFpsControl.ItemsCleared(__instance);
        }

        // The game's global Back/Escape route opens GameMenu when Options is
        // hidden. Consume that route while our submenu returns to Options.
        [HarmonyPatch(typeof(GameMenu), "Show")]
        private static class GameMenuShowPatch
        {
            [HarmonyPrefix] public static bool Prefix() => !CustomFpsControl.CancelEditing() && !QualityDeckInput.CancelChoice() && !ModsSettingsMenu.ReturnToOptions();
        }
    }
}
