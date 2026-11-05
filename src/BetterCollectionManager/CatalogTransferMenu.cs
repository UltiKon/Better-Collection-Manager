using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class CatalogTransferMenu : ButtonListMenu
    {
        private static CatalogTransferMenu s_instance;
        private readonly List<UIBButton> m_visible = new List<UIBButton>();
        private UIBButton m_first, m_second, m_cancel;
        private Action m_firstAction, m_secondAction, m_canceled;
        private bool m_ready, m_showAttempted;
        private Vector3 m_nativePosition;
        private Quaternion m_nativeRotation;
        protected override void Awake()
        {
            try
            {
                m_targetLayer = GameLayer.HighPriorityUI;
                base.Awake();
                m_menuParent = new GameObject("BCM_TransferChoiceBone").transform;
                m_menuParent.SetParent(transform, false); SetTransform();
                m_nativePosition = m_menu.transform.localPosition;
                m_nativeRotation = m_menu.transform.localRotation;
                m_useTextSizesInSlices = m_menu.m_useTextSizesInSlices;
                m_menu.m_headerText.GameStringLookup = false;
                m_menu.m_headerText.RichText = false;
                m_first = CreateMenuButton("BCM_TransferFirst", "GLOBAL_BACK", _ => Choose(true));
                m_second = CreateMenuButton("BCM_TransferSecond", "GLOBAL_BACK", _ => Choose(false));
                m_cancel = CreateMenuButton("BCM_TransferCancel", "GLOBAL_BACK", _ => Hide());
                foreach (UIBButton button in new[] { m_first, m_second, m_cancel })
                {
                    button.m_ButtonText.GameStringLookup = button.m_ButtonText.RichText = false;
                    FilterButtonFeedback.BindPressSound(button); m_visible.Add(button);
                }
                m_cancel.SetText("Cancel"); m_ready = true;
            }
            catch (Exception ex) { DebugLog.Exception("Transfer choice setup failed", ex); }
            gameObject.SetActive(false);
        }
        internal static void Open(string title, string first, Action firstAction, string second, Action secondAction, Action canceled = null)
        {
            if (s_instance == null) s_instance = new GameObject("BCM_CatalogTransferMenu").AddComponent<CatalogTransferMenu>();
            if (!s_instance.m_ready) { DestroyMenu(); throw new InvalidOperationException("Transfer choice unavailable."); }
            s_instance.Hide();
            s_instance.m_menu.m_headerText.Text = title;
            s_instance.m_first.SetText(first); s_instance.m_second.SetText(second);
            s_instance.m_firstAction = firstAction; s_instance.m_secondAction = secondAction; s_instance.m_canceled = canceled;
            KeywordSetTray.HideImmediately();
            try
            {
                s_instance.m_showAttempted = true;
                s_instance.Show(false); s_instance.gameObject.SetActive(true); s_instance.PlaceNativeMenu();
            }
            catch
            {
                s_instance.m_canceled = null; s_instance.Hide(); throw;
            }
        }
        private void Choose(bool first)
        {
            Action action = first ? m_firstAction : m_secondAction;
            m_canceled = null; Hide(); action?.Invoke();
        }
        protected override List<UIBButton> GetButtons() => m_visible;
        protected override void LayoutMenu() { base.LayoutMenu(); PlaceNativeMenu(); }
        private void LateUpdate()
        {
            if (!IsShown()) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }
            PlaceNativeMenu();
        }
        private void PlaceNativeMenu()
        {
            if (!m_ready || m_menu == null) return;
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
                float width = Math.Min(Screen.width * .32f, Screen.height * .42f), height = Screen.height * .45f;
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
                collider.enabled = button != null && m_visible.Contains(button) && button.gameObject.activeInHierarchy;
                if (!collider.enabled) continue;
                button.SetEnabled(true, true);
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
            Action canceled = m_canceled; m_canceled = null;
            m_firstAction = m_secondAction = null;
            if (IsShown() || m_showAttempted) base.Hide();
            m_showAttempted = false;
            canceled?.Invoke();
        }
        internal static void DestroyMenu()
        {
            if (s_instance == null) return;
            s_instance.m_canceled = null; s_instance.Hide(); UnityEngine.Object.Destroy(s_instance.gameObject); s_instance = null;
        }
    }
}
