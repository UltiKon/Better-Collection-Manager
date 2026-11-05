using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Adds an editable choice to the original FPS dropdown.</summary>
    internal sealed class CustomFpsControl : MonoBehaviour
    {
        private enum Choice { Custom = int.MinValue }
        private static readonly object Custom = Choice.Custom;
        private static readonly ConditionalWeakTable<DropdownControl, CustomFpsControl> s_controls = new ConditionalWeakTable<DropdownControl, CustomFpsControl>();
        private static CustomFpsControl s_editing;
        private OptionsMenu m_owner;
        private DropdownControl m_control;
        private Delegate m_nativeChosen, m_nativeText, m_ourChosen, m_ourText;
        private object m_nativeSelection, m_beforeEdit;
        private bool m_hasCustom, m_nativeLookup, m_retryInput, m_invalidInput;
        private int m_inputScreenWidth, m_inputScreenHeight;
        private string m_input;
        internal static bool OwnsRow(DropdownMenuItem row)
        {
            DropdownControl control = row?.GetComponentInParent<DropdownControl>();
            return control != null && s_controls.TryGetValue(control, out _);
        }

        internal static CustomFpsControl Attach(OptionsMenu owner)
        {
            if (owner == null || owner.m_graphicsFps == null) return null;
            try
            {
                CustomFpsControl ui = owner.GetComponent<CustomFpsControl>();
                if (ui == null) ui = owner.gameObject.AddComponent<CustomFpsControl>();
                ui.m_owner = owner;
                if (ui.m_control != owner.m_graphicsFps)
                {
                    if (ui.m_control != null) s_controls.Remove(ui.m_control);
                    ui.m_control = owner.m_graphicsFps; ui.m_hasCustom = false;
                    ui.m_nativeLookup = ui.m_control.m_selectedItem.m_text.GameStringLookup;
                    s_controls.Add(ui.m_control, ui);
                }
                DropdownScrollbar.Attach(ui.m_control);
                ui.Configure();
                return ui;
            }
            catch (Exception ex) { DebugLog.Exception("Custom FPS dropdown setup failed", ex); return null; }
        }

        internal static void ItemsCleared(DropdownControl control)
        {
            if (s_controls.TryGetValue(control, out CustomFpsControl ui)) ui.m_hasCustom = false;
        }
        private static Delegate Callback(DropdownControl control, string setter)
        {
            Type callbackType = typeof(DropdownControl).GetMethod(setter).GetParameters()[0].ParameterType;
            for (Type type = control.GetType(); type != null; type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (field.FieldType == callbackType) return field.GetValue(control) as Delegate;
            throw new MissingFieldException("Native dropdown callback is unavailable: " + setter);
        }
        private void Configure()
        {
            object current = m_control.getSelection();
            if (current != null && !Equals(current, Custom)) m_nativeSelection = current;
            Delegate chosen = Callback(m_control, "setItemChosenCallback");
            Delegate text = Callback(m_control, "setItemTextCallback");
            if (!Equals(chosen, m_ourChosen))
            {
                m_nativeChosen = chosen;
                m_control.setItemChosenCallback(Choose);
                m_ourChosen = Callback(m_control, "setItemChosenCallback");
            }
            if (!Equals(text, m_ourText))
            {
                m_nativeText = text;
                m_control.setItemTextCallback(value => Equals(value, Custom)
                    ? CustomLabel()
                    : m_nativeText?.DynamicInvoke(value) as string ?? value?.ToString() ?? "");
                m_ourText = Callback(m_control, "setItemTextCallback");
            }
            if (!m_hasCustom) { m_control.addItem(Custom); m_hasCustom = true; }
            if (CollectionQualitySettings.UseCustomFrameRate) SelectCustom();
        }
        private void Choose(object value, object previous)
        {
            if (Equals(value, Custom))
            {
                CancelEditing();
                m_beforeEdit = previous;
                m_input = CollectionQualitySettings.CustomFrameRate.ToString(CultureInfo.InvariantCulture);
                m_invalidInput = false;
                s_editing = this;
                try { ShowNativeInput(); }
                catch (Exception ex)
                {
                    CancelEditing();
                    DebugLog.Exception("Native FPS input could not be opened", ex);
                }
                return;
            }
            if (s_editing == this) { s_editing = null; StopNativeInput(); }
            m_control.m_selectedItem.m_text.GameStringLookup = m_nativeLookup;
            bool customWasActive = CollectionQualitySettings.UseCustomFrameRate;
            CollectionQualitySettings.UseCustomFrameRate = false;
            CustomFrameRate.Restore();
            try
            {
                if (m_nativeChosen == null) throw new InvalidOperationException("Native FPS selection callback is unavailable");
                m_nativeChosen.DynamicInvoke(value, Equals(previous, Custom) ? m_nativeSelection : previous);
                m_nativeSelection = value;
                m_control.setSelection(value);
            }
            catch (Exception ex)
            {
                CollectionQualitySettings.UseCustomFrameRate = customWasActive;
                CustomFrameRate.Update();
                m_control.setSelection(previous);
                if (customWasActive) SelectCustom();
                DebugLog.Exception("Native FPS selection failed", ex);
            }
        }
        private static string CustomLabel() => "Custom (" + CollectionQualitySettings.CustomFrameRate.ToString(CultureInfo.InvariantCulture) + " FPS)…";
        private void SelectCustom()
        {
            m_control.m_selectedItem.m_text.GameStringLookup = false;
            m_control.setSelection(Custom);
            m_control.m_selectedItem.m_text.Text = CustomLabel();
            m_control.m_selectedItem.m_text.UpdateNow(true);
        }
        internal static void PrepareLabel(DropdownMenuItem item)
        {
            UberText text = item?.m_text;
            if (text != null && text.GameStringLookup && text.Text != null
                && text.Text.StartsWith("Custom (", StringComparison.Ordinal) && text.Text.EndsWith(" FPS)…", StringComparison.Ordinal))
            { text.GameStringLookup = false; text.UpdateNow(true); }
        }
        internal bool Commit(string text)
        {
            if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int fps)
                || !CollectionQualitySettings.SetCustomFrameRate(fps)) return false;
            if (s_editing == this) { s_editing = null; StopNativeInput(); }
            CustomFrameRate.Update();
            SelectCustom();
            return true;
        }
        internal static bool CancelEditing()
        {
            CustomFpsControl ui = s_editing;
            if (ui == null) return false;
            s_editing = null;
            if (ui.m_control == null) return true;
            ui.StopNativeInput();
            object previous = ui.m_beforeEdit ?? ui.m_nativeSelection;
            if (Equals(previous, Custom)) ui.SelectCustom();
            else { ui.m_control.m_selectedItem.m_text.GameStringLookup = ui.m_nativeLookup; ui.m_control.setSelection(previous); }
            return true;
        }
        private void OnDisable() { if (s_editing == this) CancelEditing(); }
        private void OnDestroy() { OnDisable(); if (m_control != null) s_controls.Remove(m_control); }

        private bool TryInputRect(out Rect rect)
        {
            rect = default(Rect);
            GameObject field = m_control.m_selectedItem?.gameObject;
            Camera camera = field == null ? null : CameraUtils.FindProjectionCameraForObject(field);
            if (camera == null) return false;
            bool found = false; Bounds bounds = default(Bounds);
            foreach (Collider collider in field.GetComponentsInChildren<Collider>(true))
            {
                Bounds projected = FilterTrayGeometry.TransformBounds(collider.bounds, camera.WorldToScreenPoint);
                if (found) bounds.Encapsulate(projected); else { bounds = projected; found = true; }
            }
            if (!found) return false;
            rect = new Rect(bounds.min.x, Screen.height - bounds.max.y, bounds.size.x, bounds.size.y);
            // The collider covers the whole button. Using it as the text line
            // makes InputFieldUI scale the number to the height of the frame.
            // Match the existing caption's ink and left inset instead.
            UberText label = m_control.m_selectedItem.m_text;
            if (CounterTextLayout.TryScreenInkRect(label, camera, out Rect ink))
            {
                float height = Mathf.Min(rect.height, ink.height * 1.2f);
                float left = Mathf.Max(rect.xMin, ink.xMin);
                float top = Screen.height - ink.center.y - height * 0.5f;
                rect = new Rect(left, Mathf.Max(rect.yMin, Mathf.Min(top, rect.yMax - height)),
                    rect.xMax - left, height);
            }
            return rect.width > 0f && rect.height > 0f;
        }
        private void ShowNativeInput()
        {
            if (!TryInputRect(out Rect pixels)) throw new InvalidOperationException("Native FPS field has no input bounds");
            UniversalInputManager input = UniversalInputManager.Get();
            if (input == null) { m_retryInput = true; return; }
            UberText label = m_control.m_selectedItem.m_text;
            // This is the same InputFieldUI path used by StoreQuantityPrompt.
            var parameters = new UniversalInputManager.TextInputParams {
                m_owner = m_control.gameObject,
                m_number = true,
                m_text = m_input,
                m_maxCharacters = 4,
                m_rect = new Rect(pixels.xMin / Screen.width, pixels.yMin / Screen.height,
                    pixels.width / Screen.width, pixels.height / Screen.height),
                m_font = label.GetLocalizedFont(),
                m_alignment = TextAnchor.MiddleLeft,
                m_gameLayer = GameLayer.HighPriorityUI,
                m_color = m_invalidInput ? new Color(1f, 0.3f, 0.3f, 1f) : label.TextColor,
                m_showBackground = false,
                m_touchScreenKeyboardHideInput = true,
                m_updatedCallback = text => { if (s_editing == this) m_input = text; },
                m_completedCallback = text => {
                    if (s_editing != this) return;
                    m_input = text;
                    if (!Commit(text)) { m_invalidInput = true; m_retryInput = true; }
                },
                m_canceledCallback = (userRequested, requester) => { if (s_editing == this) CancelEditing(); },
                m_unfocusedCallback = () => { if (s_editing == this && !Commit(m_input)) CancelEditing(); }
            };
            input.UseTextInput(parameters, force: true);
            m_retryInput = !input.IsTextInputActive();
            if (!m_retryInput) label.Hide(); // The native input field renders the editable text.
            m_inputScreenWidth = Screen.width; m_inputScreenHeight = Screen.height;
        }
        private void StopNativeInput()
        {
            m_retryInput = false;
            // Native ownership checks keep other text fields (e.g. chat) intact.
            UniversalInputManager.Get()?.CancelTextInput(m_control.gameObject);
            m_control.m_selectedItem.m_text.Show();
        }
        private void Update()
        {
            if (s_editing != this) return;
            if (m_owner == null || !m_owner.IsShown()) { CancelEditing(); return; }
            // Reopen invalid submissions on the next frame: the manager clears
            // its text state after invoking a completion callback.
            if (m_retryInput || m_inputScreenWidth != Screen.width || m_inputScreenHeight != Screen.height)
            {
                try { ShowNativeInput(); }
                catch (Exception ex) { CancelEditing(); DebugLog.Exception("Native FPS input refresh failed", ex); }
            }
        }
    }
}
