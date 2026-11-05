using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class DisenchantChoiceMenu : ButtonListMenu
    {
        private static DisenchantChoiceMenu s_instance;
        private readonly List<UIBButton> m_visible = new List<UIBButton>();
        private UIBButton m_first, m_second, m_one, m_cancel;
        private DisenchantChoices.Request m_request;
        private bool m_ready, m_showAttempted;
        private Vector3 m_nativePosition;
        private Quaternion m_nativeRotation;
        protected override void Awake()
        {
            try
            {
                m_targetLayer = GameLayer.HighPriorityUI;
                base.Awake();
                m_menuParent = new GameObject("BCM_DisenchantChoiceBone").transform;
                m_menuParent.SetParent(transform, false); SetTransform();
                m_nativePosition = m_menu.transform.localPosition;
                m_nativeRotation = m_menu.transform.localRotation;
                m_useTextSizesInSlices = m_menu.m_useTextSizesInSlices;
                m_menu.m_headerText.GameStringLookup = false;
                m_menu.m_headerText.RichText = false;
                m_first = CreateMenuButton("BCM_DisenchantFirst", "GLOBAL_BACK", _ => Choose(DisenchantMode.All));
                m_one = CreateMenuButton("BCM_DisenchantOne", "GLOBAL_BACK", _ => Choose(DisenchantMode.One));
                m_second = CreateMenuButton("BCM_DisenchantSecond", "GLOBAL_BACK", _ => Choose(DisenchantMode.KeepTwo));
                m_cancel = CreateMenuButton("BCM_DisenchantCancel", "GLOBAL_BACK", _ => Hide());
                foreach (UIBButton button in new[] { m_first, m_second, m_one, m_cancel })
                {
                    button.m_ButtonText.GameStringLookup = button.m_ButtonText.RichText = false;
                    FilterButtonFeedback.BindPressSound(button);
                }
                m_cancel.SetText("Cancel"); m_ready = true;
            }
            catch (Exception ex) { DebugLog.Exception("Disenchant choice setup failed", ex); }
            gameObject.SetActive(false);
        }
        internal static void Open(DisenchantChoices.Request request)
        {
            s_instance?.Hide();
            if (request == null || !request.IsCurrent()) return;
            List<DisenchantMode> choices = request.Choices();
            if (choices.Count < 2)
            {
                if (choices.Count == 1) request.Choose(choices[0]);
                return;
            }
            if (s_instance == null) s_instance = new GameObject("BCM_DisenchantChoiceMenu").AddComponent<DisenchantChoiceMenu>();
            if (!s_instance.m_ready) { DestroyMenu(); throw new InvalidOperationException("Disenchant choice unavailable."); }
            s_instance.Hide();
            s_instance.m_request = request;
            s_instance.m_menu.m_headerText.Text = "Disenchant this card";
            s_instance.SetCaption(s_instance.m_first, "All copies", DisenchantMode.All);
            s_instance.SetCaption(s_instance.m_second, "Keep highest 2", DisenchantMode.KeepTwo);
            s_instance.SetCaption(s_instance.m_one, "Only 1 (" + PreferredPremiumHelper.PremiumName(request.Selected) + ")", DisenchantMode.One);
            s_instance.m_visible.Clear();
            foreach (var mode in choices)
                s_instance.m_visible.Add(mode == DisenchantMode.All ? s_instance.m_first : mode == DisenchantMode.KeepTwo ? s_instance.m_second : s_instance.m_one);
            s_instance.m_visible.Add(s_instance.m_cancel);
            KeywordSetTray.HideImmediately();
            try
            {
                s_instance.m_showAttempted = true;
                s_instance.Show(false); s_instance.gameObject.SetActive(true); s_instance.PlaceNativeMenu();
            }
            catch { s_instance.Hide(); throw; }
        }
        private void SetCaption(UIBButton button, string caption, DisenchantMode mode)
        {
            DisenchantPlan plan = m_request.Plan(mode);
            button.SetText(caption + " (" + plan.Count + ", +" + plan.Dust + " Dust)");
        }
        private bool CanChoose(UIBButton button)
        {
            if (button == m_cancel) return true;
            if (m_request == null) return false;
            return m_request.Plan(button == m_first ? DisenchantMode.All : button == m_second ? DisenchantMode.KeepTwo : DisenchantMode.One).Count > 0;
        }
        private void Choose(DisenchantMode mode)
        {
            DisenchantChoices.Request request = m_request;
            Hide(); request?.Choose(mode);
        }
        protected override List<UIBButton> GetButtons() => m_visible;
        protected override void LayoutMenu() { base.LayoutMenu(); PlaceNativeMenu(); }
        private void LateUpdate()
        {
            if (!IsShown()) return;
            if (m_request == null || !m_request.IsCurrent() || Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }
            PlaceNativeMenu();
        }
        private void PlaceNativeMenu()
        {
            if (!m_ready || m_menu == null) return;
            foreach (UIBButton button in m_visible) button.SetEnabled(CanChoose(button), true);
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_menu.gameObject);
            if (camera == null) return;
            // Fit the anchor using the menu's resting pose. Restore the rig's
            // animated scale before returning so the native punch still runs.
            Vector3 animatedScale = m_menu.transform.localScale;
            try
            {
                m_menu.transform.localScale = NORMAL_SCALE;
                m_menu.transform.localPosition = m_nativePosition;
                m_menu.transform.localRotation = m_nativeRotation;
                m_menuParent.localScale = Vector3.one;
                float depth = camera.nearClipPlane + (camera.farClipPlane - camera.nearClipPlane) * .5f;
                m_menuParent.position = camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, Screen.height * .55f, depth));
                ComboSceneGeometry.FaceCaption(m_menuParent, m_menu.m_headerText, camera);
                m_menuParent.position += camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, Screen.height * .55f, depth))
                    - m_menu.transform.position;
                float width = Math.Min(Screen.width * .48f, Screen.height * .7f), height = Screen.height * .45f;
                ComboSceneGeometry.Fit(m_menuParent, camera, new Rect(Screen.width * .5f - width * .5f,
                    Screen.height * .55f - height * .5f, width, height));
            }
            finally { m_menu.transform.localScale = animatedScale; }
            PlaceInputBlocker(camera);
        }
        private void PlaceInputBlocker(Camera camera)
        {
            // Native SetTransform leaves the screen-sized blocker on the
            // controller's old plane. Move it behind the fitted button hits.
            LayerUtils.SetLayer(m_menu, m_targetLayer);
            float farthest = camera.WorldToScreenPoint(m_menu.transform.position).z;
            foreach (Collider collider in m_menu.GetComponentsInChildren<Collider>(true))
            {
                UIBButton button = collider.GetComponentInParent<UIBButton>();
                collider.enabled = button != null && m_visible.Contains(button) && button.gameObject.activeInHierarchy && CanChoose(button);
                if (!collider.enabled) continue;
                Bounds bounds = collider.bounds;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    farthest = Math.Max(farthest, camera.WorldToScreenPoint(new Vector3(
                        x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y,
                        z == 0 ? bounds.min.z : bounds.max.z)).z);
            }
            if (m_blocker == null) return;
            LayerUtils.SetLayer(m_blocker, m_targetLayer);
            m_blocker.SetEnabled(true, true);
            BoxCollider hit = m_blocker.GetComponent<BoxCollider>();
            if (hit == null) return;
            float padding = Math.Max(.1f, (camera.farClipPlane - camera.nearClipPlane) * .001f);
            float depth = Math.Min(camera.farClipPlane - padding, farthest + padding);
            Transform blocker = m_blocker.transform;
            blocker.rotation = camera.transform.rotation;
            blocker.position = camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, Screen.height * .5f, depth));
            Vector3 bottom = blocker.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(0, 0, depth)));
            Vector3 top = blocker.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, depth)));
            hit.center = (bottom + top) * .5f;
            hit.size = new Vector3(Math.Max(.01f, Math.Abs(top.x - bottom.x)),
                Math.Max(.01f, Math.Abs(top.y - bottom.y)), Math.Max(.01f, Math.Abs(top.z - bottom.z)));
            hit.enabled = true;
        }

        public override void Hide()
        {
            m_request = null;
            if (IsShown() || m_showAttempted) base.Hide();
            m_showAttempted = false;
        }
        internal static void DestroyMenu()
        {
            if (s_instance == null) return;
            s_instance.Hide(); UnityEngine.Object.Destroy(s_instance.gameObject); s_instance = null;
        }
    }
}
