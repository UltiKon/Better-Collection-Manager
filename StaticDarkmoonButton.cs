using System;
using System.Globalization;
using Hearthstone.UI;
using UnityEngine;

namespace BetterCollectionManager
{
    // Lives on the original controller, so hiding the replacement does not stop
    // availability refreshes and its lifetime follows the native box.
    internal sealed class StaticDarkmoonButton : MonoBehaviour
    {
        private LuckyDrawButtonController m_controller;
        private BoxMenuButton m_button;
        internal WidgetTemplate EventWidget { get; private set; }
        private bool m_requested, m_interactable = true;
        private bool m_controllerEnabled = true;
        private bool m_reportedReady;

        internal static StaticDarkmoonButton Find(LuckyDrawButtonController controller)
            => controller == null ? null : controller.GetComponent<StaticDarkmoonButton>();

        internal static StaticDarkmoonButton Show(LuckyDrawButtonController controller)
        {
            StaticDarkmoonButton replacement = Find(controller);
            if (replacement == null)
            {
                replacement = controller.gameObject.AddComponent<StaticDarkmoonButton>();
                try { replacement.Initialize(controller); }
                catch { UnityEngine.Object.Destroy(replacement); throw; }
            }
            replacement.m_requested = true;
            replacement.Refresh();
            // Hub's ready callback registers on GetWidget(); Box also queries
            // GetButton(). Supply the event widget before dispatching readiness.
            controller.OnReady?.Invoke();
            if (!replacement.m_reportedReady)
            {
                replacement.m_reportedReady = true;
                DebugLog.Msg("Hub: static Darkmoon ready callbacks completed with empty event widget; animated entry suppressed");
            }
            return replacement;
        }

        private void Initialize(LuckyDrawButtonController controller)
        {
            m_controller = controller;
            Box box = Box.Get();
            if (box == null) throw new InvalidOperationException("Box not initialized");
            Exception failure = null;
            foreach (BoxMenuButton source in new[] { box.m_CollectionButton, box.m_ArenaButton, box.m_GameModesButton })
            {
                if (source == null || NativeHubButtons.Caption(source) == null) continue;
                try
                {
                    m_button = NativeHubButtons.Clone(source, controller.transform,
                        "BCM_StaticDarkmoonButton", "Darkmoon Faire");
                    break;
                }
                catch (Exception ex) { failure = ex; }
            }
            if (m_button == null) throw new InvalidOperationException("No usable static DFT template", failure);
            var events = new GameObject("BCM_StaticDarkmoonEvents");
            events.transform.SetParent(controller.transform, false);
            // A separate, empty native template retains Hub's event contract.
            // It has no visual behaviors, nested assets, or input components.
            EventWidget = events.AddComponent<WidgetTemplate>();
            EventWidget.Initialize();
            m_button.AddEventListener(UIEventType.RELEASE, _ => Open());
            m_button.AddEventListener(UIEventType.ROLLOVER, _ => ShowTooltip());
            m_button.AddEventListener(UIEventType.ROLLOUT, _ => m_button.GetComponent<TooltipZone>()?.HideTooltip());
            Transform label = controller.transform.parent?.Find("ButtonName");
            if (label != null) label.gameObject.SetActive(false);
            DebugLog.Msg("Hub: static Darkmoon button created; animated entry prefab was not loaded");
        }

        internal void Hide()
        {
            m_requested = false;
            if (m_button != null)
            {
                m_button.GetComponent<TooltipZone>()?.HideTooltip();
                m_button.m_TextMesh.Hide();
                m_button.gameObject.SetActive(false);
            }
        }

        internal void SetControllerEnabled(bool enabled)
        {
            m_controllerEnabled = enabled;
            Refresh();
        }

        internal void Relay(string eventName)
        {
            if (eventName == "DISABLE_INTERACTION") m_interactable = false;
            if (eventName == "ENABLE_INTERACTION") m_interactable = true;
            Refresh();
        }

        private void LateUpdate()
        {
            try { Refresh(); }
            catch (Exception ex) { HubMenuPatches.DarkmoonRuntimeFailure(m_controller, ex); }
        }

        internal void Refresh()
        {
            if (m_button == null) return;
            bool visible = TryGetOffer(out int offerId);
            if (m_button.gameObject.activeSelf != visible)
            {
                if (visible)
                {
                    m_button.gameObject.SetActive(true);
                    m_button.m_TextMesh.Show();
                }
                else
                {
                    m_button.GetComponent<TooltipZone>()?.HideTooltip();
                    m_button.m_TextMesh.Hide();
                    m_button.gameObject.SetActive(false);
                }
            }
            // Text can receive a delayed native reveal/hide after initial
            // creation. Restore the private caption while its entry is visible.
            if (visible)
            {
                if (!m_button.m_TextMesh.gameObject.activeSelf) m_button.m_TextMesh.gameObject.SetActive(true);
                if (m_button.m_TextMesh.isHidden()) m_button.m_TextMesh.Show();
                DarkmoonLogo.Refresh(m_button, offerId);
            }
            bool enabled = visible && m_interactable && HubMenuPatches.CanInteract(Box.Get())
                && !Box.Get().GetRailroadManager().ShouldDisableButtonType(Box.ButtonType.LUCKY_DRAW);
            if (m_button.IsEnabled() != enabled) m_button.SetEnabled(enabled);
        }

        private bool TryGetOffer(out int id)
        {
            id = 0;
            if (!m_requested || !m_controllerEnabled || m_controller == null) return false;
            Box box = Box.Get();
            SceneMgr scene = SceneMgr.Get();
            if (box == null || scene == null || scene.GetMode() != SceneMgr.Mode.HUB
                || !box.ShouldBeShowingState(Box.State.HUB_WITH_DRAWER)) return false;
            var railroad = box.GetRailroadManager();
            if (railroad == null || railroad.ShouldHideButtonType(Box.ButtonType.LUCKY_DRAW)) return false;
            LuckyDrawManager manager = LuckyDrawManager.Get();
            return manager != null && !manager.IsDataDirty()
                && manager.GetPreferredLuckyDrawID(out id) && manager.LuckyDrawEnabled(id);
        }

        internal void Open()
        {
            // Re-evaluate at the click, since offers and scene state can change
            // after the last frame, including while a reconnect dialog is open.
            if (!m_interactable || !TryGetOffer(out int id) || !HubMenuPatches.CanInteract(Box.Get())) return;
            var railroad = Box.Get().GetRailroadManager();
            if (railroad.ShouldDisableButtonType(Box.ButtonType.LUCKY_DRAW)) return;
            if (!Network.IsLoggedIn())
            {
                DialogManager.Get()?.ShowReconnectHelperDialog(Open);
                return;
            }
            DeepLinkManager.ExecuteDeepLink(new[] { "luckydraw", id.ToString(CultureInfo.InvariantCulture) },
                DeepLinkManager.DeepLinkSource.IN_GAME_MESSAGE, 0);
            EventWidget.EventReceived("LUCKY_DRAW_OPENED", default(TriggerEventParameters));
        }

        private void ShowTooltip()
        {
            if (m_button == null || !m_button.IsEnabled()) return;
            m_button.GetComponent<TooltipZone>()?.ShowBoxTooltip(
                GameStrings.Get("GLUE_TOOLTIP_BUTTON_LUCKY_DRAW_HEADLINE"),
                GameStrings.Get("GLUE_TOOLTIP_BUTTON_LUCKY_DRAW_DESC"));
        }

        private void OnDestroy()
        {
            if (m_button != null) UnityEngine.Object.Destroy(m_button.gameObject);
            if (EventWidget != null) UnityEngine.Object.Destroy(EventWidget.gameObject);
        }
    }
}
