using System;
using HarmonyLib;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BetterCollectionManager
{
    // SetFilterItem owns its press/hover artwork through native overrides.
    // UIBHighlight is optional additional feedback, not the row's press handler.
    internal static class FilterButtonFeedback
    {
        [ThreadStatic] private static int s_actionDepth, s_nativeDepth, s_pressSoundDepth, s_pageRefreshDepth, s_dropdownCancelDepth;
        private static readonly ConditionalWeakTable<SetFilterItem, object> s_owned = new ConditionalWeakTable<SetFilterItem, object>();
        public static void InstallSoundGuard(HarmonyLib.Harmony harmony)
        {
            var soundPrefix = new HarmonyMethod(typeof(FilterButtonFeedback).GetMethod(nameof(ShouldPlaySound)));
            var feedbackPrefix = new HarmonyMethod(typeof(FilterButtonFeedback).GetMethod(nameof(BeginNativeFeedback)));
            var feedbackFinalizer = new HarmonyMethod(typeof(FilterButtonFeedback).GetMethod(nameof(EndNativeFeedback)));
            int sounds = 0, events = 0;
            foreach (MethodInfo method in typeof(SoundManager).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                // Native feedback also uses asset-reference overloads and sounds
                // whose asset names contain no "click". Guard every playback entry.
                if (method.Name.IndexOf("Play", StringComparison.OrdinalIgnoreCase) < 0 || method.IsAbstract
                    || method.ContainsGenericParameters) continue;
                try { harmony.Patch(method, prefix: soundPrefix); sounds++; }
                catch (Exception ex) { DebugLog.Exception("Filter sound hook unavailable: " + method, ex); }
            }
            // Native OnRelease can play its sound after dispatching our callback.
            // Own the entire native event, including inherited PegUI dispatch.
            for (Type type = typeof(SetFilterItem); type != null && type != typeof(MonoBehaviour)
                && type != typeof(Behaviour) && type != typeof(Component); type = type.BaseType)
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!IsFeedbackMethod(method.Name) || method.IsAbstract || method.ContainsGenericParameters) continue;
                try { harmony.Patch(method, prefix: feedbackPrefix, finalizer: feedbackFinalizer); events++; }
                catch (Exception ex) { DebugLog.Exception("Filter feedback hook unavailable: " + method, ex); }
            }
            MethodInfo clear = AccessTools.Method(typeof(PegUIElement), "ClearEventListeners");
            if (clear != null) harmony.Patch(clear, postfix: new HarmonyMethod(typeof(FilterButtonFeedback).GetMethod(nameof(ListenersCleared))));
            MethodInfo addItem = AccessTools.Method(typeof(DropdownControl), "addItem");
            if (addItem != null) harmony.Patch(addItem, postfix: new HarmonyMethod(typeof(FilterButtonFeedback).GetMethod(nameof(BindDropdownRows))));
            foreach (string name in new[] { "onUserPressedButton", "onUserPressedSelection", "onUserItemClicked", "onUserCancelled" })
            {
                MethodInfo callback = AccessTools.Method(typeof(DropdownControl), name);
                if (callback != null) harmony.Patch(callback,
                    prefix: new HarmonyMethod(typeof(FilterButtonFeedback).GetMethod(nameof(BeginDropdownCancel))),
                    finalizer: new HarmonyMethod(typeof(FilterButtonFeedback).GetMethod(nameof(EndDropdownCancel))));
            }
            DebugLog.Msg($"Filter sound hooks: audio={sounds}, native feedback={events}");
        }
        private static bool IsFeedbackMethod(string name)
            => name == "SetSelected" || name == "set_Selected" || name == "OnUIEvent" || name == "HandleUIEvent"
                || (name.StartsWith("On", StringComparison.Ordinal) && (name.IndexOf("Press", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Click", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("MouseDown", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("MouseUp", StringComparison.OrdinalIgnoreCase) >= 0));
        public static void BeginNativeFeedback(Component __instance, out bool __state)
        {
            SetFilterItem item = __instance as SetFilterItem ?? __instance?.GetComponent<SetFilterItem>();
            __state = item != null && s_owned.TryGetValue(item, out _);
            if (__state) s_nativeDepth++;
        }
        public static Exception EndNativeFeedback(Exception __exception, bool __state)
        {
            if (__state) s_nativeDepth--;
            return __exception;
        }
        public static bool ShouldPlaySound(object[] __args = null)
            => s_pressSoundDepth > 0 || ((s_pageRefreshDepth > 0 || (s_actionDepth == 0 && s_nativeDepth == 0))
                && !(s_dropdownCancelDepth > 0 && __args != null && Array.Exists(__args,
                    value => value is string asset && asset.StartsWith("Small_Click.prefab:", StringComparison.Ordinal))));
        internal static bool IsFilterAction => s_actionDepth > 0;
        public static bool AllowNativeSound(string asset) => ShouldPlaySound(new object[] { asset });
        public static void RunFilterAction(Action action)
        {
            s_actionDepth++;
            try { action?.Invoke(); }
            finally { s_actionDepth--; }
        }

        public static void RunPageRefresh(Action action)
        {
            // Page/card effects are part of the result refresh, independent of
            // the row's click. Keep suppressing duplicate native row sounds.
            s_pageRefreshDepth++;
            try { action?.Invoke(); }
            finally { s_pageRefreshDepth--; }
        }

        private sealed class PressSound { internal string Asset; internal Func<string> ChooseAsset; internal int Frame = -1; internal bool Listening, Silent; }
        private static readonly ConditionalWeakTable<PegUIElement, PressSound> s_pressSounds = new ConditionalWeakTable<PegUIElement, PressSound>();
        public static void BindPressSound(PegUIElement peg, string asset = null)
        {
            if (peg == null) return;
            SetFilterItem item = peg.GetComponent<SetFilterItem>();
            if (item != null) s_owned.GetValue(item, _ => new object());
            UIBHighlight highlight = peg.GetComponent<UIBHighlight>();
            bool bound = s_pressSounds.TryGetValue(peg, out PressSound binding);
            if (!bound)
            {
                binding = new PressSound { Asset = asset ?? (!string.IsNullOrEmpty(highlight?.m_MouseDownSound)
                    ? highlight.m_MouseDownSound : !string.IsNullOrEmpty(highlight?.m_MouseUpSound)
                        ? highlight.m_MouseUpSound : CollectionFilterMenu.ButtonPressSound) };
                s_pressSounds.Add(peg, binding);
            }
            binding.Silent = false;
            if (asset != null) binding.Asset = asset;
            if (!binding.Listening)
            {
                peg.AddEventListener(UIEventType.PRESS, _ => PlayPressSound(peg));
                binding.Listening = true;
            }
            if (peg is CheckBox checkbox && binding.ChooseAsset == null)
            {
                string on = checkbox.m_checkOnSound, off = checkbox.m_checkOffSound;
                binding.ChooseAsset = () => { string sound = checkbox.IsChecked() ? off : on; return string.IsNullOrEmpty(sound) ? binding.Asset : sound; };
                checkbox.m_checkOnSound = checkbox.m_checkOffSound = "";
            }
            if (highlight != null) { highlight.m_MouseDownSound = ""; highlight.m_MouseUpSound = ""; }
        }
        internal static void PlayPressSound(PegUIElement peg)
        {
            if (peg == null || !peg.IsEnabled() || !s_pressSounds.TryGetValue(peg, out PressSound binding) || binding.Silent) return;
            // Physics fallback and PegUI can both observe this same press.
            if (Input.GetMouseButtonDown(0) && binding.Frame == Time.frameCount) return;
            binding.Frame = Time.frameCount;
            PlayClickSound(binding.ChooseAsset?.Invoke() ?? binding.Asset);
        }

        internal static void PlayClickSound(string asset = null)
            => PlayOwnedSound(asset ?? CollectionFilterMenu.ButtonPressSound);

        // SetFilterTray.Show(false) uses this sound for the panel animation,
        // independently of any press feedback on the toggle itself.
        internal static void PlayCloseSound()
            => PlayOwnedSound("choose_opponent_panel_slide_off.prefab:3139d09eb94899d41b9bf612649f47bf");

        private static void PlayOwnedSound(string asset)
        {
            s_pressSoundDepth++;
            try { SoundManager.Get()?.LoadAndPlay(asset); }
            catch (Exception ex) { DebugLog.Exception("Filter feedback sound unavailable", ex); }
            finally { s_pressSoundDepth--; }
        }

        public static void ListenersCleared(PegUIElement __instance)
        {
            if (s_pressSounds.TryGetValue(__instance, out PressSound binding)) binding.Listening = false;
        }
        internal static void MutePressSound(PegUIElement peg)
        {
            BindPressSound(peg);
            if (peg != null && s_pressSounds.TryGetValue(peg, out PressSound binding)) binding.Silent = true;
        }
        private sealed class DropdownSound { internal bool Open; }
        private static readonly ConditionalWeakTable<DropdownControl, DropdownSound> s_dropdowns = new ConditionalWeakTable<DropdownControl, DropdownSound>();
        internal static void BindDropdown(DropdownControl control)
        {
            if (control == null) return;
            if (!s_dropdowns.TryGetValue(control, out DropdownSound binding))
            {
                binding = new DropdownSound { Open = control.isMenuShown() }; s_dropdowns.Add(control, binding);
                var previous = control.getMenuShownCallback();
                control.setMenuShownCallback(shown => {
                    bool changed = binding.Open != shown; binding.Open = shown;
                    previous?.Invoke(shown);
                    if (changed) PlayClickSound();
                });
            }
            MutePressSound(control.m_button); MutePressSound(control.m_selectedItem);
            MutePressSound(control.m_cancelCatcher?.GetComponent<PegUIElement>());
            BindDropdownRows(control);
        }
        public static void BindDropdownRows(DropdownControl __instance)
        {
            if (!s_dropdowns.TryGetValue(__instance, out _)) return;
            foreach (DropdownMenuItem row in __instance.m_menuItemContainer.GetComponentsInChildren<DropdownMenuItem>(true))
                if (row != __instance.m_menuItemTemplate) MutePressSound(row);
        }
        public static void BeginDropdownCancel(DropdownControl __instance, out bool __state)
        {
            __state = s_dropdowns.TryGetValue(__instance, out _);
            if (__state) s_dropdownCancelDepth++;
        }
        public static Exception EndDropdownCancel(Exception __exception, bool __state)
        {
            if (__state) s_dropdownCancelDepth--;
            return __exception;
        }

        public static void RestoreNativeListeners(PegUIElement peg)
        {
            UIBHighlight highlight = peg.GetComponent<UIBHighlight>();
            if (highlight == null) return;
            highlight.enabled = true;
            highlight.EnableResponse = true;
            highlight.AlwaysOver = false;
            try
            {
                AccessTools.Method(typeof(UIBHighlight), "Awake")?.Invoke(highlight, null);
            }
            catch (Exception ex) { DebugLog.Exception("Filter button native feedback setup failed", ex); }
        }

        // Visibility/selection updates must not turn off artwork currently
        // controlled by the native hover/press handler, including its parents.
        public static bool OwnsVisual(SetFilterItem item, UIBHighlight[] highlights, Transform visual)
        {
            if (visual == null) return false;
            if (item != null && (Contains(item.m_selectedGlow, visual)
                || Contains(item.m_mouseOverGlow, visual) || Contains(item.m_pressedShadow, visual))) return true;
            foreach (UIBHighlight highlight in highlights)
                if (Contains(highlight.m_MouseOverHighlight, visual)
                    || Contains(highlight.m_MouseDownHighlight, visual)) return true;
            return false;
        }

        private static bool Contains(GameObject effect, Transform visual)
            => effect != null && (effect.transform == visual || visual.IsChildOf(effect.transform)
                || effect.transform.IsChildOf(visual));
    }
}
