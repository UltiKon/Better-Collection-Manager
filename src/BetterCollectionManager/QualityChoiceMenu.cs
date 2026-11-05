using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Native modal picker shown after a collection card was dropped.</summary>
    internal sealed class QualityChoiceMenu : ButtonListMenu
    {
        private static QualityChoiceMenu s_instance;
        private readonly Dictionary<TAG_PREMIUM, UIBButton> m_choices = new Dictionary<TAG_PREMIUM, UIBButton>();
        private readonly List<UIBButton> m_visible = new List<UIBButton>();
        private QualityAddRequest m_request;
        private UIBButton m_cancel;
        private bool m_ready;
        private Vector3 m_nativePosition;
        private Quaternion m_nativeRotation;

        protected override void Awake()
        {
            try
            {
                m_targetLayer = GameLayer.HighPriorityUI;
                base.Awake();
                // The Escape menu's bone can be hidden, collapsed or facing
                // another camera. Keep this menu on its own overlay anchor.
                m_menuParent = new GameObject("BCM_QualityChoiceBone").transform;
                m_menuParent.SetParent(transform, false);
                SetTransform();
                m_nativePosition = m_menu.transform.localPosition;
                m_nativeRotation = m_menu.transform.localRotation;
                m_useTextSizesInSlices = m_menu.m_useTextSizesInSlices;
                m_menu.m_headerText.GameStringLookup = false;
                m_menu.m_headerText.Text = "Choose quality";
                foreach (TAG_PREMIUM value in new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL })
                {
                    TAG_PREMIUM premium = value;
                    UIBButton button = CreateMenuButton("BCM_Choose" + premium, "GLOBAL_BACK", _ => Choose(premium));
                    button.m_ButtonText.GameStringLookup = false;
                    m_choices[premium] = button;
                }
                m_cancel = CreateMenuButton("BCM_QualityCancel", "GLOBAL_BACK", _ => Hide());
                m_cancel.m_ButtonText.GameStringLookup = false;
                m_cancel.SetText("Cancel");
                m_ready = true;
            }
            catch (Exception ex) { DebugLog.Exception("Quality choice menu initialization failed", ex); }
            gameObject.SetActive(false);
        }

        public static void Open(QualityAddRequest request)
        {
            if (!QualityDeckInput.IsValid(request)) return;
            Open(request, QualityDeckInput.Choices(request, request.IgnoreRules));
        }
        internal static void Open(QualityAddRequest request, List<TAG_PREMIUM> choices, Func<TAG_PREMIUM, string> caption = null)
        {
            if (!QualityDeckInput.IsValid(request)) return;
            if (choices.Count == 0) return;
            if (choices.Count == 1) { QualityDeckInput.Confirm(request, choices[0]); return; }
            if (s_instance == null)
            {
                s_instance = new GameObject("BCM_QualityChoiceMenu").AddComponent<QualityChoiceMenu>();
                if (!s_instance.m_ready) { DestroyMenu(); throw new InvalidOperationException("Quality choice menu is unavailable"); }
            }
            s_instance.m_request = request;
            s_instance.m_menu.m_headerText.Text = request.Title ?? "Choose quality";
            s_instance.m_visible.Clear();
            foreach (TAG_PREMIUM premium in choices)
            {
                UIBButton button = s_instance.m_choices[premium];
                button.SetText(caption != null ? caption(premium) : PreferredPremiumHelper.PremiumName(premium) + " ("
                    + QualityDeckInput.Remaining(request, premium) + " available)");
                s_instance.m_visible.Add(button);
            }
            s_instance.m_visible.Add(s_instance.m_cancel);
            s_instance.Show(false);
            s_instance.gameObject.SetActive(true);
            s_instance.PlaceNativeMenu();
        }

        private void Choose(TAG_PREMIUM premium)
        {
            QualityAddRequest request = m_request;
            m_request = null; // A choice is a confirmation, not a cancellation.
            Hide();
            QualityDeckInput.Confirm(request, premium);
        }

        protected override List<UIBButton> GetButtons() => m_visible;

        protected override void LayoutMenu()
        {
            base.LayoutMenu();
            PlaceNativeMenu();
        }
        private void LateUpdate()
        {
            if (IsShown()) PlaceNativeMenu();
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
            QualityAddRequest request = m_request;
            m_request = null;
            if (IsShown()) base.Hide();
            request?.Canceled?.Invoke();
        }

        public static bool Cancel()
        {
            if (s_instance == null || !s_instance.IsShown()) return false;
            s_instance.Hide();
            return true;
        }

        public static void ValidateContext()
        {
            if (s_instance != null && s_instance.IsShown() && !QualityDeckInput.IsValid(s_instance.m_request)) Cancel();
        }

        public static void DestroyMenu()
        {
            if (s_instance == null) return;
            Cancel();
            UnityEngine.Object.Destroy(s_instance.gameObject);
            s_instance = null;
        }
    }
}
