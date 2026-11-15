using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    // Extend AlertPopup itself: its native body, title, modal handling, button
    // responses and Escape action remain responsible for the confirmation.
    internal static class DeckArchivePopup
    {
        internal const string Id = "BCM_DeckArchiveConfirmation";
        private sealed class State
        {
            internal AlertPopup Popup;
            internal UIBButton Extra;
            internal List<MultiSliceElement.Slice> OriginalSlices, ExtendedSlices;
            internal Vector3 OriginalSpacing, ExtendedSpacing;
            internal DeckArchiveActionIcon Icon;
        }
        private static readonly List<State> States = new List<State>();
        private static readonly System.Reflection.MethodInfo Press = AccessTools.Method(typeof(AlertPopup), "ButtonPress");
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(AlertPopup), "UpdateButtons"),
                postfix: new HarmonyMethod(typeof(DeckArchivePopup), nameof(Buttons)));
            harmony.Patch(AccessTools.Method(typeof(AlertPopup), "UpdateLayout"),
                postfix: new HarmonyMethod(typeof(DeckArchivePopup), nameof(Layout)));
        }
        public static void Buttons(AlertPopup __instance)
        {
            State state = States.Find(item => item.Popup == __instance);
            if (__instance.GetInfo()?.m_id != Id || !CollectionQualitySettings.DeckArchiveEnabled)
            {
                if (state != null) Restore(state);
                return;
            }
            if (state == null)
            {
                foreach (State expired in States.Where(item => item.Popup == null)) expired.Icon?.Dispose();
                States.RemoveAll(item => item.Popup == null);
                state = new State { Popup = __instance, OriginalSlices = __instance.m_buttonContainer.m_slices,
                    OriginalSpacing = __instance.m_buttonContainer.m_localSliceSpacing };
                try
                {
                    // The native OK button has different central ornamentation.
                    // Clone Confirm so all three choices use the same wide face.
                    UIBButton source = __instance.m_confirmButton;
                    state.Extra = UnityEngine.Object.Instantiate(source, source.transform.parent, false);
                    state.Extra.name = "BCM_DeckArchiveAction";
                    state.Extra.gameObject.SetActive(true);
                    state.Extra.ClearEventListeners();
                    FilterButtonFeedback.RestoreNativeListeners(state.Extra);
                    state.Extra.m_ButtonText.GameStringLookup = state.Extra.m_ButtonText.RichText = false;
                    state.Extra.m_ButtonText.ResizeToFit = true; state.Extra.m_ButtonText.ResizeToFitAndGrow = false;
                    // The new action is neither the green confirmation check nor
                    // the cancel cross. Leave native Confirm/Cancel icons intact.
                    foreach (GameObject icon in __instance.m_buttonIconsSet1.Concat(__instance.m_buttonIconsSet2))
                    {
                        if (icon == null || !icon.transform.IsChildOf(source.transform)) continue;
                        var path = new List<string>(); Transform node = icon.transform;
                        while (node != source.transform) { path.Insert(0, node.name); node = node.parent; }
                        Transform copy = state.Extra.transform.Find(string.Join("/", path));
                        if (copy != null)
                        {
                            if (state.Icon == null)
                            {
                                try { state.Icon = new DeckArchiveActionIcon(copy.gameObject); }
                                catch (Exception ex) { DebugLog.Exception("Deck archive icon unavailable", ex); }
                            }
                            copy.gameObject.SetActive(false);
                        }
                    }
                    State captured = state;
                    state.Extra.AddEventListener(UIEventType.RELEASE, _ => Press.Invoke(captured.Popup, new object[] { AlertPopup.Response.OK }));
                    __instance.m_buttonContainer.m_slices = new List<MultiSliceElement.Slice>();
                    __instance.m_buttonContainer.AddSlice(__instance.m_confirmButton.gameObject);
                    __instance.m_buttonContainer.AddSlice(state.Extra.gameObject);
                    __instance.m_buttonContainer.AddSlice(__instance.m_cancelButton.gameObject);
                    state.ExtendedSlices = __instance.m_buttonContainer.m_slices;
                    Vector3 spacing = state.OriginalSpacing;
                    int axis = (int)__instance.m_buttonContainer.m_direction;
                    float scale = Math.Abs(__instance.m_buttonContainer.transform.lossyScale[axis]);
                    spacing[axis] = Math.Max(spacing[axis], TransformUtil.GetBoundsOfChildren(source).size.x * .05f / Math.Max(.0001f, scale));
                    state.ExtendedSpacing = spacing;
                    States.Add(state);
                }
                catch
                {
                    Restore(state);
                    if (state.Extra != null) UnityEngine.Object.Destroy(state.Extra.gameObject);
                    throw;
                }
            }
            __instance.m_okayButton.gameObject.SetActive(false);
            state.Extra.gameObject.SetActive(true);
            state.Extra.SetText(__instance.GetInfo().m_okText);
            state.Icon?.SetRestore(__instance.GetInfo().m_okText == "Restore instead");
            state.Extra.SetEnabled(true, true);
            __instance.m_buttonContainer.m_slices = state.ExtendedSlices;
            __instance.m_buttonContainer.m_localSliceSpacing = state.ExtendedSpacing;
            __instance.m_buttonContainer.UpdateSlices();
        }
        public static void Layout(AlertPopup __instance)
        {
            if (__instance.GetInfo()?.m_id != Id || !CollectionQualitySettings.DeckArchiveEnabled) return;
            State state = States.Find(item => item.Popup == __instance);
            if (state?.Extra == null) return;
            // Native layout reserves only two buttons. Widen the native body
            // for three full-size faces, with equal spacing and side margins.
            float width = 0f;
            foreach (UIBButton button in new[] { __instance.m_confirmButton, state.Extra, __instance.m_cancelButton })
                width += TransformUtil.GetBoundsOfChildren(button).size.x;
            float spacing = Math.Max(__instance.m_buttonContainer.m_localSliceSpacing.magnitude
                * __instance.m_buttonContainer.transform.lossyScale.x, width * .025f);
            Vector2 size = __instance.m_body.GetWorldDimensions();
            __instance.m_body.SetWidth(Math.Max(size.x, width + spacing * 4f));
            __instance.m_header.m_container.transform.position = __instance.m_body.m_top.m_slice.transform.position;
            __instance.m_buttonContainer.transform.position = __instance.m_body.m_bottom.m_slice.transform.position;
            __instance.m_buttonContainer.UpdateSlices();
        }
        private static void Restore(State state)
        {
            if (state.Popup != null)
            {
                state.Popup.m_buttonContainer.m_slices = state.OriginalSlices;
                state.Popup.m_buttonContainer.m_localSliceSpacing = state.OriginalSpacing;
                if (state.Extra != null) state.Extra.gameObject.SetActive(false);
                state.Popup.m_buttonContainer.UpdateSlices();
            }
        }
        internal static void Shutdown()
        {
            foreach (State state in States)
            {
                Restore(state);
                state.Icon?.Dispose();
                if (state.Extra != null) UnityEngine.Object.Destroy(state.Extra.gameObject);
            }
            States.Clear();
        }
    }
}
