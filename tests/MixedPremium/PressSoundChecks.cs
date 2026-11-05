using System;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckPressSoundTiming()
    {
        int frame = Time.frameCount;
        bool pressed = Input.LeftPressed;
        var root = new GameObject("PressSoundRegression");
        try
        {
            var button = root.AddComponent<PegUIElement>(); button.SetEnabled(true, true);
            var highlight = root.AddComponent<UIBHighlight>();
            highlight.m_MouseDownSound = ""; highlight.m_MouseUpSound = "Small_Click.prefab:release-only";
            FilterButtonFeedback.BindPressSound(button); FilterButtonFeedback.BindPressSound(button);
            int sounds = SoundManager.Get().Played.Count, actions = 0;
            button.AddEventListener(UIEventType.RELEASE, _ => {
                Check(SoundManager.Get().Played.Count == sounds + 1, "The click is already audible when the action starts"); actions++;
            });
            Time.frameCount++; Input.LeftPressed = true;
            button.Send(UIEventType.PRESS); FilterButtonFeedback.PlayPressSound(button);
            Check(SoundManager.Get().Played.Count == sounds + 1 && actions == 0
                && SoundManager.Get().Played[sounds] == "Small_Click.prefab:release-only"
                && highlight.m_MouseUpSound == "", "A release-only native click moves to press and duplicate bindings/fallbacks play once");
            Input.LeftPressed = false; button.Release();
            Check(actions == 1 && SoundManager.Get().Played.Count == sounds + 1, "Release executes the action without replaying the click");
            button.ClearEventListeners(); FilterButtonFeedback.BindPressSound(button);
            sounds = SoundManager.Get().Played.Count; Time.frameCount++;
            button.Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == sounds + 1, "Rebinding after native listener cleanup restores press feedback");
            button.SetEnabled(false, true); Time.frameCount++; button.Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == sounds + 1, "Disabled controls never play a click");

            button.SetEnabled(true, true); FilterButtonFeedback.MutePressSound(button);
            sounds = SoundManager.Get().Played.Count; Time.frameCount++; button.Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == sounds, "Muted native text-field controls retain input without a press sound");

            var checkbox = CheckBox.MakeTemplate(new GameObject("PressCheckbox"));
            checkbox.transform.SetParent(root.transform, false);
            checkbox.m_checkOnSound = "Small_Click.prefab:on"; checkbox.m_checkOffSound = "Small_Click.prefab:off";
            FilterButtonFeedback.BindPressSound(checkbox);
            sounds = SoundManager.Get().Played.Count;
            checkbox.Send(UIEventType.PRESS);
            Check(!checkbox.IsChecked() && SoundManager.Get().Played[sounds] == "Small_Click.prefab:on",
                "Checkbox press selects the next state's sound before changing the setting");
            checkbox.SetChecked(true); checkbox.Send(UIEventType.RELEASE);
            Check(SoundManager.Get().Played.Count == sounds + 1 && checkbox.m_checkOnSound == "" && checkbox.m_checkOffSound == "",
                "Checkbox release cannot replay the native on/off sound");
            Time.frameCount++; checkbox.Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played[sounds + 1] == "Small_Click.prefab:off", "The next checkbox press uses the opposite native sound");

            var dropdown = DropdownControl.MakeTemplate(new GameObject("PressDropdown"));
            dropdown.transform.SetParent(root.transform, false);
            FilterButtonFeedback.BindDropdown(dropdown);
            var row = new GameObject("DynamicDropdownRow").AddComponent<DropdownMenuItem>();
            row.transform.SetParent(dropdown.m_menuItemContainer.transform, false);
            FilterButtonFeedback.BindDropdownRows(dropdown);
            sounds = SoundManager.Get().Played.Count; Time.frameCount++; row.Send(UIEventType.PRESS);
            Check(SoundManager.Get().Played.Count == sounds, "Owned dropdown presses and option rows stay silent");
            dropdown.onUserPressedButton();
            Check(SoundManager.Get().Played.Count == sounds + 1, "Opening an owned dropdown sounds once after it becomes visible");
            dropdown.getMenuShownCallback()?.Invoke(true);
            Check(SoundManager.Get().Played.Count == sounds + 1, "Repeated shown notifications do not replay the opening click");
            FilterButtonFeedback.BeginDropdownCancel(dropdown, out bool owned);
            try
            {
                dropdown.onUserCancelled();
                dropdown.onUserCancelled();
                SoundManager.Get().LoadAndPlay("Small_Click.prefab:2a1c5335bf08dc84eb6e04fc58160681");
                SoundManager.Get().LoadAndPlay("Card_Transition_In.prefab:effect");
            }
            finally { FilterButtonFeedback.EndDropdownCancel(null, owned); }
            Check(SoundManager.Get().Played.Count == sounds + 3 && SoundManager.Get().Played[sounds + 2] == "Card_Transition_In.prefab:effect",
                "Closing a dropdown sounds once, suppresses delayed native clicks and preserves animation effects");
            Check(FilterButtonFeedback.AllowNativeSound("Small_Click.prefab:2a1c5335bf08dc84eb6e04fc58160681"),
                "Native click playback outside an owned dropdown callback remains available");
        }
        finally
        {
            Input.LeftPressed = pressed; Time.frameCount = frame; UnityEngine.Object.Destroy(root);
        }
    }
}
