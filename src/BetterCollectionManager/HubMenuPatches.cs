using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class HubMenuPatches
    {
        private static readonly MethodInfo s_canEnable = AccessTools.Method(typeof(Box), "CanEnableUIEvents");
        private static readonly FieldInfo s_waiting = AccessTools.Field(typeof(Box), "m_waitingForNetData");
        private static bool s_darkmoonInstalled, s_menuInstalled, s_darkmoonFallback;
        private static Box s_lastBox;
        private static bool s_lastEnabled;

        internal static bool SwapEnabled => s_menuInstalled && HubMenuSettings.TavernBrawlInMainMenu;

        internal static void DarkmoonRuntimeFailure(LuckyDrawButtonController controller, Exception ex)
        {
            if (s_darkmoonFallback) return;
            s_darkmoonFallback = true;
            StaticDarkmoonButton.Find(controller)?.Hide();
            DebugLog.Exception("Hub: static Darkmoon update failed; restoring original entry", ex);
            controller?.Show();
        }

        internal static void DisableSwap(string reason)
        {
            s_menuInstalled = false;
            Box box = Box.Get();
            if (box != null && box.m_BattleGroundsButton != null)
            {
                box.m_BattleGroundsButton.m_buttonType = Box.ButtonType.BACON;
                box.m_BattleGroundsButton.SetText(GameStrings.Get("GLUE_BACON"));
                box.UpdateUI();
            }
            DebugLog.Warning("Hub: keeping the original Battlegrounds main-menu access: " + reason);
        }

        public static void Install(HarmonyLib.Harmony harmony)
        {
            HubMenuSettings.EnsureLoaded();
            if (s_canEnable == null || s_waiting == null)
            { DebugLog.Warning("Hub: native interaction guards unavailable; keeping original menu"); return; }
            if (HubMenuSettings.StaticDarkmoonButton)
                s_darkmoonInstalled = InstallGroup(harmony, new[]
                {
                    Hook(typeof(LuckyDrawButtonController), "Show", nameof(DarkmoonShow)),
                    Hook(typeof(LuckyDrawButtonController), "Hide", nameof(DarkmoonHide)),
                    Hook(typeof(LuckyDrawButtonController), "SetEnable", nameof(DarkmoonEnable)),
                    Hook(typeof(LuckyDrawButtonController), "RelayTriggerEvent", nameof(DarkmoonRelay)),
                    Hook(typeof(LuckyDrawButtonController), "GetButton", nameof(DarkmoonGetButton)),
                    Hook(typeof(LuckyDrawButtonController), "GetWidget", nameof(DarkmoonGetWidget)),
                    Hook(typeof(Box), "OnLuckyDrawButtonPressed", nameof(DarkmoonPressed))
                });
            if (HubMenuSettings.TavernBrawlInMainMenu)
                s_menuInstalled = InstallGroup(harmony, new[]
                {
                    Hook(typeof(Box), "SetupButtonText", nameof(MainButtonText), true),
                    Hook(typeof(Box), "UpdateBoxButtons", nameof(MainButtonsUpdated), true),
                    Hook(typeof(Box), "OnButtonPressed", nameof(MainPressed)),
                    Hook(typeof(Box), "OnButtonMouseOver", nameof(MainMouseOver)),
                    Hook(typeof(BoxScrollButton), "IsFeatureActive", nameof(BrawlFeature)),
                    Hook(typeof(BoxScrollButton), "CanDownloadMode", nameof(BrawlDownload)),
                    Hook(typeof(Box), "FireButtonPressEvent", nameof(FireButton)),
                    Hook(typeof(GameModeDisplay), "InitializeGameModeSceneData", nameof(ModesInitialized), true),
                    Hook(typeof(GameModeDisplay), "NavigateToSelectedMode", nameof(NavigateMode)),
                    Hook(typeof(GameModeDisplay), "GetModuleTagForGameMode", nameof(ModuleForMode))
                });
            DebugLog.Msg($"Hub: static Darkmoon={s_darkmoonInstalled}; Tavern Brawl main slot={s_menuInstalled}");
        }

        private sealed class Patch
        {
            public Type Type;
            public string Method, Handler;
            public bool Postfix;
        }
        private static Patch Hook(Type type, string method, string handler, bool postfix = false)
            => new Patch { Type = type, Method = method, Handler = handler, Postfix = postfix };

        private static bool InstallGroup(HarmonyLib.Harmony harmony, Patch[] patches)
        {
            try
            {
                // Validate the whole group before patching. Hooks do nothing
                // until the group is fully installed, including after failure.
                foreach (Patch patch in patches)
                    if (AccessTools.Method(patch.Type, patch.Method) == null)
                        throw new MissingMethodException(patch.Type.FullName, patch.Method);
                foreach (Patch patch in patches)
                {
                    var handler = new HarmonyMethod(AccessTools.Method(typeof(HubMenuPatches), patch.Handler));
                    harmony.Patch(AccessTools.Method(patch.Type, patch.Method),
                        prefix: patch.Postfix ? null : handler, postfix: patch.Postfix ? handler : null);
                }
                return true;
            }
            catch (Exception ex) { DebugLog.Exception("Hub patch group unavailable; original behavior retained", ex); return false; }
        }

        internal static bool CanInteract(Box box)
        {
            SceneMgr scene = SceneMgr.Get();
            return box != null && scene != null && box.IsInStateWithButtons()
                && !scene.IsTransitionNowOrPending() && !box.IsTransitioningToSceneMode()
                && s_waiting != null && !(bool)s_waiting.GetValue(box)
                && s_canEnable != null && (bool)s_canEnable.Invoke(box, null)
                && !(DialogManager.Get()?.ShowingDialog() ?? true)
                && !(PopupDisplayManager.Get()?.IsShowing ?? true);
        }

        internal static bool DarkmoonShow(LuckyDrawButtonController __instance, bool ___m_isEnabled)
        {
            if (!s_darkmoonInstalled || s_darkmoonFallback || !HubMenuSettings.StaticDarkmoonButton
                || Box.Get() == null || Box.Get().m_luckyDrawButtonController != __instance) return true;
            if (!___m_isEnabled) return false;
            try { StaticDarkmoonButton.Show(__instance); return false; }
            catch (Exception ex)
            {
                // Never remove the only access if this client's plain button
                // template cannot be cloned. One warning, then native behavior.
                s_darkmoonFallback = true;
                StaticDarkmoonButton.Find(__instance)?.Hide();
                DebugLog.Exception("Hub: static Darkmoon replacement failed; using original button", ex);
                return true;
            }
        }
        internal static void DarkmoonHide(LuckyDrawButtonController __instance)
            => StaticDarkmoonButton.Find(__instance)?.Hide();
        internal static void DarkmoonEnable(LuckyDrawButtonController __instance, bool enabled)
            => StaticDarkmoonButton.Find(__instance)?.SetControllerEnabled(enabled);
        internal static void DarkmoonRelay(LuckyDrawButtonController __instance, string eventName)
            => StaticDarkmoonButton.Find(__instance)?.Relay(eventName);
        internal static bool DarkmoonGetButton(LuckyDrawButtonController __instance, ref LuckyDrawButton __result)
        {
            if (s_darkmoonFallback || StaticDarkmoonButton.Find(__instance) == null) return true;
            __result = null;
            return false;
        }
        internal static bool DarkmoonGetWidget(LuckyDrawButtonController __instance, ref Hearthstone.UI.Widget __result)
        {
            var replacement = StaticDarkmoonButton.Find(__instance);
            if (s_darkmoonFallback || replacement == null || replacement.EventWidget == null) return true;
            __result = replacement.EventWidget;
            return false;
        }
        internal static bool DarkmoonPressed(Box __instance)
        {
            var replacement = StaticDarkmoonButton.Find(__instance.m_luckyDrawButtonController);
            if (s_darkmoonFallback || replacement == null) return true;
            replacement.Open();
            return false;
        }

        private static bool IsBrawlButton(BoxScrollButton button)
            => SwapEnabled && button != null && Box.Get() != null && button == Box.Get().m_BattleGroundsButton;

        internal static bool CanEnterBrawl(out string reason)
        {
            reason = string.Empty;
            var manager = TavernBrawlManager.Get();
            return manager != null && manager.CanEnterStandardTavernBrawl(out reason);
        }

        internal static void MainButtonText(Box __instance)
        {
            if (!SwapEnabled || __instance.m_BattleGroundsButton == null) return;
            __instance.m_BattleGroundsButton.SetText(GameStrings.Get("GLOBAL_TAVERN_BRAWL"));
        }
        internal static void MainButtonsUpdated(Box __instance) { if (SwapEnabled) UpdateMainButton(__instance, true); }
        public static void OnUpdate()
        {
            if (SwapEnabled) UpdateMainButton(Box.Get(), false);
        }
        private static void UpdateMainButton(Box box, bool force)
        {
            try { ApplyMainButton(box, force); }
            catch (Exception ex)
            {
                DebugLog.Exception("Hub: main-menu replacement failed", ex);
                DisableSwap("main-menu state could not be updated");
            }
        }
        private static void ApplyMainButton(Box box, bool force)
        {
            if (box == null || box.m_BattleGroundsButton == null) return;
            bool enabled = CanInteract(box) && CanEnterBrawl(out _);
            bool first = box != s_lastBox;
            if (!force && !first && enabled == s_lastEnabled) return;
            BoxScrollButton button = box.m_BattleGroundsButton;
            button.m_buttonType = Box.ButtonType.TAVERN_BRAWL;
            MainButtonText(box);
            box.SetBoxButtonInteractions(enabled, button, true);
            if (first)
            {
                AccessTools.Method(typeof(BoxScrollButton), "UpdateDownloadButtonIcon")?.Invoke(button, new object[] { false });
                AccessTools.Method(typeof(BoxScrollButton), "UpdateDownloadProgress")?.Invoke(button, new object[] { false });
                DebugLog.Msg("Hub: Tavern Brawl replaces the Battlegrounds main-menu slot");
            }
            s_lastBox = box;
            s_lastEnabled = enabled;
        }
        internal static bool BrawlFeature(BoxScrollButton __instance, ref bool __result)
        {
            if (!IsBrawlButton(__instance)) return true;
            __result = CanEnterBrawl(out _);
            return false;
        }
        internal static bool BrawlDownload(BoxScrollButton __instance, ref bool __result)
        {
            if (!IsBrawlButton(__instance)) return true;
            __result = false;
            return false;
        }
        internal static bool MainPressed(Box __instance, UIEvent e)
        {
            if (!SwapEnabled || e.GetElement() != __instance.m_BattleGroundsButton) return true;
            if (CanInteract(__instance) && CanEnterBrawl(out _)
                && !__instance.GetRailroadManager().ShouldDisableButtonType(Box.ButtonType.TAVERN_BRAWL))
                __instance.OnTavernBrawlButtonPressed(e);
            return false;
        }
        internal static bool MainMouseOver(Box __instance, UIEvent e)
        {
            if (!SwapEnabled || e.GetElement() != __instance.m_BattleGroundsButton) return true;
            if (!CanInteract(__instance)) return false;
            bool available = CanEnterBrawl(out string reason);
            __instance.m_BattleGroundsButton.GetComponent<TooltipZone>()?.ShowBoxTooltip(
                GameStrings.Get("GLOBAL_TAVERN_BRAWL"), available ? GameStrings.Get("GLOBAL_TAVERN_BRAWL") : reason);
            return false;
        }
        internal static void ModesInitialized(GameModeDisplay __instance)
        {
            if (!SwapEnabled) return;
            var menu = __instance.GetComponent<HubModesMenu>() ?? __instance.gameObject.AddComponent<HubModesMenu>();
            menu.Initialize(__instance);
        }
        internal static bool NavigateMode(GameModeDisplay __instance)
        {
            if (!SwapEnabled) return true;
            var menu = __instance.GetComponent<HubModesMenu>();
            return menu == null || !menu.TryNavigateBattlegrounds();
        }
        internal static bool ModuleForMode(GameModeDbfRecord gameModeRecord, ref Hearthstone.Core.Streaming.DownloadTags.Content __result)
        {
            if (!SwapEnabled || gameModeRecord == null || gameModeRecord.LinkedScene != "BACON") return true;
            __result = Hearthstone.Core.Streaming.DownloadTags.Content.Bgs;
            return false;
        }
        internal static bool FireButton(Box.ButtonType buttonType, bool isShowingTutorialPreview)
        {
            // Hub removes its Box listener when leaving for Modes. Preserve the
            // native BG entry handler, but complete its normal scene request here.
            if (!SwapEnabled || buttonType != Box.ButtonType.BACON || isShowingTutorialPreview
                || SceneMgr.Get() == null || SceneMgr.Get().GetMode() != SceneMgr.Mode.GAME_MODE) return true;
            SceneMgr.Get().SetNextMode(SceneMgr.Mode.BACON);
            return false;
        }
    }
}
