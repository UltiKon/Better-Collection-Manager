using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class DeckImageMenu
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo Copy = typeof(DeckOptionsMenu).GetField("m_copyPasteDeckButton", Fields);
        private static readonly FieldInfo Positions = typeof(DeckOptionsMenu).GetField("m_buttonPositions", Fields);
        private static readonly FieldInfo Bottoms = typeof(DeckOptionsMenu).GetField("m_bottomPositions", Fields);
        private static readonly FieldInfo Scales = typeof(DeckOptionsMenu).GetField("m_topScales", Fields);
        private static readonly List<State> States = new List<State>();
        private static bool s_installed;
        private static bool s_wasBusy;
        private sealed class State
        {
            internal DeckOptionsMenu Menu;
            internal DeckCopyPasteButton Button;
            internal Transform ExtraBottom;
            internal Transform[] OriginalBottoms;
            internal float[] OriginalScales;
        }
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            try
            {
                if (Copy == null || Positions == null || Bottoms == null || Scales == null)
                    throw new MissingFieldException("Native deck options layout has changed.");
                harmony.Patch(AccessTools.Method(typeof(DeckOptionsMenu), "UpdateLayout"), prefix: new HarmonyMethod(typeof(DeckImageMenu), nameof(Prepare)));
                harmony.Patch(AccessTools.Method(typeof(DeckOptionsMenu), "GetButtonCount"), postfix: new HarmonyMethod(typeof(DeckImageMenu), nameof(Count)));
                harmony.Patch(AccessTools.Method(typeof(DeckOptionsMenu), "UpdateButtons"), postfix: new HarmonyMethod(typeof(DeckImageMenu), nameof(Layout)));
                s_installed = true;
            }
            catch (Exception ex) { DebugLog.Exception("Deck image menu unavailable", ex); }
        }
        private static State Find(DeckOptionsMenu menu) => States.Find(state => state.Menu == menu);
        private static bool Eligible(DeckOptionsMenu menu)
        {
            CollectionManager manager = CollectionManager.Get();
            return s_installed && DeckImageClipboard.Supported && menu != null && manager != null && manager.IsInEditMode()
                && manager.GetEditedDeck()?.GetTotalCardCount() > 0 && Find(menu)?.Button != null;
        }
        public static void Prepare(DeckOptionsMenu __instance)
        {
            if (!s_installed || !DeckImageClipboard.Supported || Find(__instance) != null) return;
            State state = null;
            try
            {
                for (int i = States.Count - 1; i >= 0; i--) if (States[i].Menu == null) States.RemoveAt(i);
                var original = Copy.GetValue(__instance) as DeckCopyPasteButton;
                var positions = Positions.GetValue(__instance) as Transform[];
                var bottoms = Bottoms.GetValue(__instance) as Transform[];
                var scales = Scales.GetValue(__instance) as float[];
                if (original == null || positions == null || positions.Length < 2 || bottoms == null || bottoms.Length < 2 || scales == null || scales.Length < 2)
                    throw new InvalidOperationException("Native deck options anchors are unavailable.");
                state = new State { Menu = __instance, OriginalBottoms = bottoms, OriginalScales = scales };
                state.Button = UnityEngine.Object.Instantiate(original, original.transform.parent, false);
                state.Button.name = "BCM_CopyDeckAsPng";
                // Show prepares its rows under an active root. Force the clone's
                // first Awake now, even if the native Copy row was hidden, so it
                // cannot register a second set of handlers after our rebinding.
                state.Button.gameObject.SetActive(true);
                state.Button.gameObject.SetActive(false);
                state.Button.ClearEventListeners();
                // Clearing the copied action also removes UIBHighlight's native
                // hover, press and release handlers. Rebind those to this clone.
                FilterButtonFeedback.RestoreNativeListeners(state.Button);
                state.Button.ButtonText.GameStringLookup = false;
                state.Button.ButtonText.Text = "Copy as PNG";
                state.Button.ButtonText.ResizeToFit = true;
                state.Button.ButtonText.ResizeToFitAndGrow = false;
                state.Button.TooltipHeaderString = "Copy as PNG";
                state.Button.TooltipMessage = "Copy an image of all cards, including sideboards.";
                DeckCopyPasteButton button = state.Button;
                button.AddEventListener(UIEventType.RELEASE, _ => DeckImageExport.Start(CameraUtils.FindProjectionCameraForObject(button.gameObject), button.ButtonText));
                var extra = new GameObject("BCM_DeckImageBottomAnchor");
                extra.transform.SetParent(bottoms[bottoms.Length - 1].parent, false);
                extra.transform.position = bottoms[bottoms.Length - 1].position * 2f - bottoms[bottoms.Length - 2].position;
                state.ExtraBottom = extra.transform;
                var extendedBottoms = new Transform[bottoms.Length + 1];
                Array.Copy(bottoms, extendedBottoms, bottoms.Length); extendedBottoms[bottoms.Length] = extra.transform;
                var extendedScales = new float[scales.Length + 1];
                Array.Copy(scales, extendedScales, scales.Length); extendedScales[scales.Length] = scales[scales.Length - 1] * 2f - scales[scales.Length - 2];
                Bottoms.SetValue(__instance, extendedBottoms); Scales.SetValue(__instance, extendedScales);
                States.Add(state);
            }
            catch (Exception ex)
            {
                if (state != null) Release(state);
                DebugLog.Exception("Could not add Copy as PNG", ex);
            }
        }
        public static void Count(DeckOptionsMenu __instance, ref int __result)
        {
            if (Eligible(__instance)) __result++;
        }
        public static void Layout(DeckOptionsMenu __instance)
        {
            State state = Find(__instance);
            if (state?.Button == null) return;
            bool visible = Eligible(__instance);
            state.Button.gameObject.SetActive(visible);
            if (!visible) return;
            var original = (DeckCopyPasteButton)Copy.GetValue(__instance);
            var positions = (Transform[])Positions.GetValue(__instance);
            Vector3 step = positions[1].position - positions[0].position;
            // Native UpdateButtons resets every row before this postfix. Insert
            // the PNG row immediately after Copy and shift later native rows.
            foreach (string name in new[] { "m_deckHelperButton", "m_deleteButton", "m_retireButton" })
                if (typeof(DeckOptionsMenu).GetField(name, Fields)?.GetValue(__instance) is Component button && button.gameObject.activeSelf)
                    button.transform.position += step;
            state.Button.transform.position = original.transform.position + step;
            state.Button.EnableButton(!DeckImageExport.Busy);
        }
        private static void Release(State state)
        {
            if (state.Menu != null)
            {
                Bottoms.SetValue(state.Menu, state.OriginalBottoms);
                Scales.SetValue(state.Menu, state.OriginalScales);
            }
            if (state.Button != null) { state.Button.gameObject.SetActive(false); UnityEngine.Object.Destroy(state.Button.gameObject); }
            if (state.ExtraBottom != null) UnityEngine.Object.Destroy(state.ExtraBottom.gameObject);
        }
        internal static void OnUpdate()
        {
            bool busy = DeckImageExport.Busy;
            if (s_wasBusy == busy) return;
            s_wasBusy = busy;
            foreach (State state in States)
                if (state.Button != null) state.Button.EnableButton(!busy);
        }
        internal static void Shutdown()
        {
            s_installed = false;
            DeckImageExport.Shutdown();
            foreach (State state in States) Release(state);
            States.Clear();
        }
    }
}
