using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private enum NativeFps { Low, Medium, High }
    private static void CheckCustomFps()
    {
        string path = CollectionQualitySettings.SettingsPath;
        string originalJson = File.Exists(path) ? File.ReadAllText(path) : null;
        int originalTarget = Application.targetFrameRate, originalVsync = QualitySettings.vSyncCount;
        var loaded = typeof(CollectionQualitySettings).GetField("s_loaded", BindingFlags.NonPublic | BindingFlags.Static);
        void Reload()
        {
            MelonLoader.MelonPreferences.Categories.Clear(); loaded.SetValue(null, false);
            CollectionQualitySettings.EnsureLoaded();
        }
        CollectionQualitySettings.UseCustomFrameRate = false; CustomFrameRate.Restore();
        var owner = new GameObject("Original Options with FPS").AddComponent<OptionsMenu>();
        var control = owner.m_graphicsFps = DropdownControl.MakeTemplate();
        control.transform.SetParent(owner.transform, false);
        foreach (NativeFps option in Enum.GetValues(typeof(NativeFps))) control.addItem(option);
        control.setItemTextCallback(value => "Native " + value);
        int choices = 0; object previousValue = null;
        control.setItemChosenCallback((value, previous) => {
            choices++; previousValue = previous;
            Application.targetFrameRate = value.Equals(NativeFps.Low) ? 30 : value.Equals(NativeFps.Medium) ? 60 : 120;
            QualitySettings.vSyncCount = value.Equals(NativeFps.High) ? 1 : 0;
        });
        control.m_selectedItem.m_text.GameStringLookup = true;
        control.setSelection(NativeFps.High); Application.targetFrameRate = 120; QualitySettings.vSyncCount = 1;
        owner.Show();
        var ui = CustomFpsControl.Attach(owner);
        Check(ui != null && control.Items.Count == 4 && control.m_selectedItem.m_text.Text == "Native High",
            "Original FPS choices and captions survive adding a Custom choice");
        object custom = control.Items.Single(value => !(value is NativeFps));
        CustomFpsControl.Attach(owner);
        Check(control.Items.Count == 4, "Reopening Options cannot duplicate Custom FPS or callback wrappers");
        control.SelectOption(custom);
        Check(!CollectionQualitySettings.UseCustomFrameRate && choices == 0,
            "Opening the numeric editor neither persists a choice nor sends Custom to the native enum callback");
        var input = UniversalInputManager.Get();
        var label = control.m_selectedItem.m_text;
        var nativeInput = input.CurrentTextInput;
        Check(nativeInput != null && nativeInput.m_owner == control.gameObject && nativeInput.m_number
            && nativeInput.m_maxCharacters == 4 && nativeInput.m_font == label.GetLocalizedFont()
            && nativeInput.m_gameLayer == GameLayer.HighPriorityUI && !nativeInput.m_showBackground
            && nativeInput.m_alignment == TextAnchor.MiddleLeft
            && nativeInput.m_rect.width > 0f && nativeInput.m_rect.width <= 1f
            && nativeInput.m_rect.height > 0f && nativeInput.m_rect.height <= 1f && label.isHidden(),
            "Custom FPS keeps the parchment visible and uses left-aligned native numeric input with the original font");
        var inputCamera = CameraUtils.FindProjectionCameraForObject(label.gameObject);
        Check(CounterTextLayout.TryScreenInkRect(label, inputCamera, out Rect captionInk)
            && Math.Abs(nativeInput.m_rect.xMin * Screen.width - captionInk.xMin) < 0.01f
            && Math.Abs(nativeInput.m_rect.height * Screen.height - captionInk.height * 1.2f) < 0.01f
            && Math.Abs(nativeInput.m_rect.center.y * Screen.height - (Screen.height - captionInk.center.y)) < 0.01f,
            "FPS editing follows the native caption's inset and text height instead of the full button frame");
        var update = typeof(CustomFpsControl).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        input.CompleteNativeText("165");
        Check(CollectionQualitySettings.UseCustomFrameRate && CollectionQualitySettings.CustomFrameRate == 165
            && Application.targetFrameRate == 165 && QualitySettings.vSyncCount == 0 && owner.IsShown()
            && Equals(control.getSelection(), custom) && !label.GameStringLookup && !label.isHidden()
            && !input.IsTextInputActive(),
            "Native input completion applies, persists and labels the custom target and releases the editor");
        Check(File.ReadAllText(path).Contains("\"customFrameRate\":165")
            && File.ReadAllText(path).Contains("\"useCustomFrameRate\":true"),
            "Custom FPS and whether it is selected are saved durably in JSON");
        CustomFrameRate.Restore(); Reload(); CustomFrameRate.Update();
        Check(CollectionQualitySettings.UseCustomFrameRate && Application.targetFrameRate == 165,
            "A fresh preference load automatically restores the custom FPS target before opening Options");
        Application.targetFrameRate = 20; Application.isFocused = false; CustomFrameRate.Update();
        Check(Application.targetFrameRate == 20, "Custom FPS preserves native background throttling");
        Application.isFocused = true; QualitySettings.vSyncCount = 2; CustomFrameRate.Update();
        Check(Application.targetFrameRate == 165 && QualitySettings.vSyncCount == 0,
            "Focus and native graphics resets cannot silently override the custom target");
        control.SelectOption(custom);
        foreach (string invalid in new[] { "0", "-1", "1001", "2147483648", "2.5", "abc", "" })
            Check(!ui.Commit(invalid) && CollectionQualitySettings.CustomFrameRate == 165,
                "Invalid or unsupported FPS input does not alter the saved target: " + invalid);
        input.CompleteNativeText("0");
        Check(!input.IsTextInputActive(), "Invalid native completion waits until the manager finishes clearing its text");
        update.Invoke(ui, null);
        Check(input.CurrentTextInput?.m_text == "0" && input.InputText == "0"
            && input.CurrentTextInput.m_color.Value.Equals(new Color(1f, 0.3f, 0.3f, 1f)),
            "Invalid input reopens on the next frame with its text intact and validation feedback");
        Check(CustomFpsControl.CancelEditing() && Equals(control.getSelection(), custom) && !label.isHidden(),
            "Invalid input remains editable and Cancel restores the previous custom selection");
        control.SelectOption(custom);
        input.UpdateNativeText("240");
        int width = Screen.width;
        Screen.width += 100;
        update.Invoke(ui, null);
        Check(input.CurrentTextInput?.m_text == "240" && input.InputText == "240",
            "Native input keeps the edited number when its screen bounds are refreshed");
        Screen.width = width;
        input.CancelNativeText();
        Check(!input.IsTextInputActive() && CollectionQualitySettings.CustomFrameRate == 165 && !label.isHidden(),
            "Native Escape cancellation restores the caption without saving typed changes");
        control.SelectOption(NativeFps.Low); CustomFrameRate.Update();
        Check(choices == 1 && Equals(previousValue, NativeFps.High) && Application.targetFrameRate == 30
            && !CollectionQualitySettings.UseCustomFrameRate && CollectionQualitySettings.CustomFrameRate == 165
            && control.m_selectedItem.m_text.GameStringLookup,
            "Choosing a native FPS preset disables the override, forwards native data once and remembers the custom number");
        control.SelectOption(custom); input.UpdateNativeText("240"); input.CancelNativeText();
        Check(!CollectionQualitySettings.UseCustomFrameRate && Equals(control.getSelection(), NativeFps.Low)
            && CollectionQualitySettings.CustomFrameRate == 165, "Escape cancels an edited number without saving it");
        input.TextInputReady = false;
        control.SelectOption(custom);
        Check(!input.IsTextInputActive() && !label.isHidden(), "Editor waits for the native input asset to finish loading");
        input.TextInputReady = true; update.Invoke(ui, null);
        Check(input.IsTextInputActive() && label.isHidden(), "A ready native asset opens on the next update");
        input.UpdateNativeText("180"); input.UnfocusNativeText();
        Check(CollectionQualitySettings.CustomFrameRate == 180 && !input.IsTextInputActive(),
            "Leaving the native field commits a valid number");
        CollectionQualitySettings.SetCustomFrameRate(165);
        control.SelectOption(custom);
        var obsolete = input.CurrentTextInput;
        var other = new GameObject("Native chat input owner");
        input.UseTextInput(new UniversalInputManager.TextInputParams { m_owner = other, m_text = "chat" });
        obsolete.m_completedCallback("250");
        Check(input.CurrentTextInput?.m_owner == other && !CustomFpsControl.CancelEditing()
            && CollectionQualitySettings.CustomFrameRate == 165 && !label.isHidden(),
            "Changing native input ownership cancels FPS editing and late callbacks preserve chat and saved FPS");
        input.CancelTextInput(other); UnityEngine.Object.Destroy(other);
        control.SelectOption(custom); control.SelectOption(NativeFps.Low);
        Check(!input.IsTextInputActive() && !label.isHidden() && choices == 2,
            "A native FPS preset closes an owned numeric editor and invokes its callback once");
        control.clearItems();
        foreach (NativeFps option in Enum.GetValues(typeof(NativeFps))) control.addItem(option);
        control.setSelection(NativeFps.Medium);
        CustomFpsControl.Attach(owner);
        Check(control.Items.Count == 4 && Equals(control.getSelection(), NativeFps.Medium),
            "A native dropdown rebuild restores exactly one Custom item and keeps the selected native preset");
        control.SelectOption(NativeFps.High);
        Check(choices == 3, "Rebuilt dropdown callbacks still invoke the native handler exactly once");
        CollectionQualitySettings.SetCustomFrameRate(165); CustomFrameRate.Update();
        control.setItemChosenCallback((value, previous) => throw new InvalidOperationException("native FPS failure"));
        CustomFpsControl.Attach(owner); control.SelectOption(NativeFps.Low);
        Check(CollectionQualitySettings.UseCustomFrameRate && Application.targetFrameRate == 165
            && Equals(control.getSelection(), custom), "Failed native selection restores the working custom setting and dropdown");
        File.WriteAllText(path, "{\"customFrameRate\":0,\"useCustomFrameRate\":true}"); Reload();
        Check(!CollectionQualitySettings.UseCustomFrameRate, "A malformed FPS configuration cannot enable an invalid target");
        CustomFrameRate.Restore(); CustomFpsControl.CancelEditing();
        UnityEngine.Object.Destroy(owner.gameObject);
        if (originalJson == null) File.Delete(path); else File.WriteAllText(path, originalJson);
        Reload();
        Application.targetFrameRate = originalTarget; QualitySettings.vSyncCount = originalVsync; Application.isFocused = true;
    }
}
