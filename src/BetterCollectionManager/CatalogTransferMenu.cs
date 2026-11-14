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
        private const int MaxChoiceColumns = 5, ChoiceRows = 6, MaxChoicesPerPage = MaxChoiceColumns * ChoiceRows;
        private readonly List<UIBButton> m_choiceButtons = new List<UIBButton>();
        private UIBButton m_previous, m_next;
        private string[] m_choices;
        private string m_choiceTitle;
        private Action<int> m_choiceAction;
        private object m_choiceRequest;
        private int m_page, m_choiceColumns;
        private int ChoicesPerPage => m_choiceColumns * ChoiceRows;
        private bool m_artworkLayoutDirty;
        private int m_layoutWidth, m_layoutHeight;
        private Matrix4x4 m_layoutView, m_layoutProjection;
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
            if (s_instance != null && s_instance.m_choiceButtons.Count > 0) DestroyMenu();
            if (s_instance == null) s_instance = new GameObject("BCM_CatalogTransferMenu").AddComponent<CatalogTransferMenu>();
            if (!s_instance.m_ready) { DestroyMenu(); throw new InvalidOperationException("Transfer choice unavailable."); }
            s_instance.Hide();
            s_instance.SetVisibleButtons(new[] { s_instance.m_first, s_instance.m_second, s_instance.m_cancel });
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
        internal static Action OpenChoices(string title, IList<string> choices, Action<int> chosen, Action canceled)
        {
            if (choices == null || choices.Count == 0) throw new ArgumentException("No choices are available.");
            if (s_instance == null) s_instance = new GameObject("BCM_CatalogTransferMenu").AddComponent<CatalogTransferMenu>();
            if (!s_instance.m_ready) { DestroyMenu(); throw new InvalidOperationException("Artwork choice unavailable."); }
            CatalogTransferMenu menu = s_instance;
            menu.Hide();
            object request = new object(); menu.m_choiceRequest = request;
            menu.m_choices = new List<string>(choices).ToArray();
            menu.m_choiceTitle = title; menu.m_choiceAction = chosen; menu.m_canceled = canceled; menu.m_page = 0;
            menu.m_choiceColumns = ArtworkColumns();
            ConfigureArtworkButton(menu.m_cancel);
            if (menu.m_choiceButtons.Count == 0)
            {
                for (int i = 0; i < MaxChoicesPerPage; i++)
                {
                    int row = i;
                    menu.m_choiceButtons.Add(menu.ChoiceButton("BCM_ArtworkChoice" + i, () => menu.ChooseArtwork(row)));
                }
                menu.m_previous = menu.ChoiceButton("BCM_ArtworkPrevious", () => menu.ChangePage(-1));
                menu.m_previous.SetText("Previous");
                menu.m_next = menu.ChoiceButton("BCM_ArtworkNext", () => menu.ChangePage(1));
                menu.m_next.SetText("Next");
            }
            menu.RefreshChoices();
            KeywordSetTray.HideImmediately();
            try
            {
                menu.m_showAttempted = true; menu.Show(false); menu.gameObject.SetActive(true);
                // Show updates the native header slices after LayoutMenu.
                menu.m_artworkLayoutDirty = true; menu.PlaceNativeMenu();
            }
            catch { menu.Hide(); throw; }
            return () => { if (menu != null && ReferenceEquals(menu.m_choiceRequest, request)) menu.Hide(); };
        }
        private UIBButton ChoiceButton(string name, Action action)
        {
            UIBButton button = CreateMenuButton(name, "GLOBAL_BACK", _ => action());
            ConfigureArtworkButton(button);
            FilterButtonFeedback.BindPressSound(button);
            return button;
        }
        private static void ConfigureArtworkButton(UIBButton button)
        {
            // Native OnOver punches the root through 90 degrees. Measuring that
            // animated face feeds its shrinking bounds back into the layout.
            button.m_WiggleAmount = Vector3.zero; button.m_WiggleTime = 0f;
            button.m_DepressOnOver = false;
            button.m_AnimateFlip = button.m_WigglePostFlip = false;
            button.m_ButtonText.GameStringLookup = button.m_ButtonText.RichText = false;
            button.m_ButtonText.ResizeToFit = button.m_ButtonText.ResizeToFitAndGrow = false;
            button.m_ButtonText.WordWrap = false;
        }
        private static float ArtworkWidth => Math.Min(Screen.width * .88f, Screen.height * 1.45f);
        private static int ArtworkColumns() => Math.Max(1, Math.Min(MaxChoiceColumns,
            (int)(ArtworkWidth / Math.Max(1f, Screen.height * .25f))));
        private void SetVisibleButtons(IEnumerable<UIBButton> buttons)
        {
            foreach (UIBButton button in new[] { m_first, m_second, m_cancel, m_previous, m_next })
                if (button != null) button.gameObject.SetActive(false);
            foreach (UIBButton button in m_choiceButtons) button.gameObject.SetActive(false);
            m_visible.Clear();
            foreach (UIBButton button in buttons) { button.gameObject.SetActive(true); m_visible.Add(button); }
        }
        private void RefreshChoices()
        {
            m_artworkLayoutDirty = true;
            int start = m_page * ChoicesPerPage;
            var visible = new List<UIBButton>();
            for (int row = 0; row < ChoicesPerPage && start + row < m_choices.Length; row++)
            {
                m_choiceButtons[row].SetText(WrapChoice(m_choices[start + row])); visible.Add(m_choiceButtons[row]);
            }
            if (m_page > 0) visible.Add(m_previous);
            if (start + ChoicesPerPage < m_choices.Length) visible.Add(m_next);
            visible.Add(m_cancel); SetVisibleButtons(visible);
            int pages = (m_choices.Length + ChoicesPerPage - 1) / ChoicesPerPage;
            m_menu.m_headerText.Text = m_choiceTitle + (pages > 1 ? " (" + (m_page + 1) + "/" + pages + ")" : "");
        }
        private static string WrapChoice(string label)
        {
            if (label.Length <= 26) return label;
            int split = label.LastIndexOf(' ', Math.Min(26, label.Length - 1));
            if (split < 8) split = Math.Min(26, label.Length);
            return label.Substring(0, split) + "\n" + label.Substring(split).TrimStart();
        }
        private void ChangePage(int delta)
        {
            if (m_choices == null) return;
            m_page = Math.Max(0, Math.Min((m_choices.Length - 1) / ChoicesPerPage, m_page + delta));
            RefreshChoices(); LayoutMenu();
        }
        private void ChooseArtwork(int row)
        {
            if (m_choices == null) return;
            int index = m_page * ChoicesPerPage + row;
            if (index >= m_choices.Length) return;
            Action<int> chosen = m_choiceAction;
            m_canceled = null; Hide(); chosen?.Invoke(index);
        }
        private void Choose(bool first)
        {
            Action action = first ? m_firstAction : m_secondAction;
            m_canceled = null; Hide(); action?.Invoke();
        }
        protected override List<UIBButton> GetButtons() => m_visible;
        protected override void LayoutMenu()
        {
            if (m_choices == null) base.LayoutMenu();
            else
            {
                m_menu.m_buttonContainer.ClearSlices();
                foreach (UIBButton button in m_visible) button.gameObject.SetActive(true);
            }
            PlaceNativeMenu();
        }
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
            if (m_choices != null) { PlaceArtworkMenu(camera); return; }
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
        private void PlaceArtworkMenu(Camera camera)
        {
            if (!m_artworkLayoutDirty && m_layoutWidth == Screen.width && m_layoutHeight == Screen.height
                && m_layoutView.Equals(camera.worldToCameraMatrix) && m_layoutProjection.Equals(camera.projectionMatrix))
            { PlaceInputBlocker(camera); return; }
            int columns = ArtworkColumns();
            if (columns != m_choiceColumns)
            {
                int first = m_page * ChoicesPerPage;
                m_choiceColumns = columns; m_page = first / ChoicesPerPage;
                RefreshChoices();
            }
            Vector3 animatedScale = m_menu.transform.localScale;
            try
            {
                m_menu.transform.localScale = NORMAL_SCALE;
                m_menu.transform.localPosition = m_nativePosition;
                m_menu.transform.localRotation = m_nativeRotation;
                m_menuParent.localScale = Vector3.one;
                float depth = camera.nearClipPlane + (camera.farClipPlane - camera.nearClipPlane) * .5f;
                m_menuParent.position = camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, Screen.height * .5f, depth));
                ComboSceneGeometry.FaceCaption(m_menuParent, m_menu.m_headerText, camera);
                float width = ArtworkWidth;
                float rowHeight = Screen.height * .075f, gap = Screen.height * .012f;
                int count = Math.Min(ChoicesPerPage, m_choices.Length - m_page * ChoicesPerPage);
                // Keep the frame and footer stationary on a shorter last page.
                int rows = (Math.Min(ChoicesPerPage, m_choices.Length) + m_choiceColumns - 1) / m_choiceColumns;
                float height = (rows + 2) * rowHeight + gap * 4;
                float left = (Screen.width - width) * .5f, top = (Screen.height + height) * .5f;
                float columnWidth = (width - gap * (m_choiceColumns + 1)) / m_choiceColumns;
                // Native backgrounds grow from the top of the button container.
                Vector3 panelTop = camera.ScreenToWorldPoint(new Vector3(Screen.width * .5f, top - rowHeight, depth));
                m_menu.m_buttonContainer.transform.position = panelTop;
                Vector3 origin = m_menu.transform.InverseTransformPoint(panelTop);
                Vector3 corner = m_menu.transform.InverseTransformPoint(camera.ScreenToWorldPoint(
                    new Vector3(Screen.width * .5f + width, top - rowHeight - height + rowHeight, depth)));
                m_menu.m_background.SetSize(Math.Abs(corner.x - origin.x), Vector3.Distance(
                    camera.ScreenToWorldPoint(new Vector3(0, 0, depth)), camera.ScreenToWorldPoint(new Vector3(0, height - rowHeight, depth))) / m_menu.transform.lossyScale.z);
                m_menu.m_border.SetSize(Math.Abs(corner.x - origin.x), Vector3.Distance(
                    camera.ScreenToWorldPoint(new Vector3(0, 0, depth)), camera.ScreenToWorldPoint(new Vector3(0, height - rowHeight, depth))) / m_menu.transform.lossyScale.z);
                Rect body = new Rect(left, top - height, width, height - rowHeight);
                ComboSceneGeometry.Fit(m_menu.m_background.transform, camera, body);
                ComboSceneGeometry.Fit(m_menu.m_border.transform, camera, body);
                ComboSceneGeometry.Fit(m_menu.m_header.transform, camera,
                    new Rect(left, top - rowHeight, width, rowHeight));
                ComboSceneGeometry.TextAfterArtwork(m_menu.m_headerText, m_menu.m_header.gameObject);
                bool ready = FitArtworkText(m_menu.m_headerText, camera, new Rect(left + gap, top - rowHeight, width - gap * 2, rowHeight), Screen.height * .028f);
                for (int i = 0; i < count; i++)
                {
                    int column = i % m_choiceColumns, row = i / m_choiceColumns;
                    ready &= FitArtworkButton(m_choiceButtons[i], camera, new Rect(left + gap + column * (columnWidth + gap),
                        top - rowHeight * (row + 2) - gap, columnWidth, rowHeight - gap));
                }
                float footer = top - height + gap;
                float footerWidth = (width - gap * 4) / 3;
                ready &= FitArtworkButton(m_cancel, camera, new Rect(left + gap * 2 + footerWidth, footer, footerWidth, rowHeight - gap));
                if (m_page > 0) ready &= FitArtworkButton(m_previous, camera, new Rect(left + gap, footer, footerWidth, rowHeight - gap));
                if ((m_page + 1) * ChoicesPerPage < m_choices.Length)
                    ready &= FitArtworkButton(m_next, camera, new Rect(left + gap + (footerWidth + gap) * 2, footer, footerWidth, rowHeight - gap));
                m_layoutWidth = Screen.width; m_layoutHeight = Screen.height;
                m_layoutView = camera.worldToCameraMatrix; m_layoutProjection = camera.projectionMatrix;
                m_artworkLayoutDirty = !ready;
            }
            finally { m_menu.transform.localScale = animatedScale; }
            PlaceInputBlocker(camera);
        }
        private static bool FitArtworkButton(UIBButton button, Camera camera, Rect target)
        {
            // Measure the button face independently from long card names.
            bool ready = ArtworkButtonBounds(button, camera, out Rect art);
            if (ready)
            {
                button.transform.localScale *= target.height / art.height * .98f;
                if (ArtworkButtonBounds(button, camera, out art))
                {
                    Vector3 scale = button.m_RootObject.transform.localScale;
                    float stretch = target.width / art.width;
                    button.m_RootObject.transform.localScale = new Vector3(scale.x * stretch, scale.y, scale.z);
                    if (button.m_ButtonText.transform.IsChildOf(button.m_RootObject.transform))
                    {
                        Vector3 caption = button.m_ButtonText.transform.localScale;
                        button.m_ButtonText.transform.localScale = new Vector3(caption.x / stretch, caption.y, caption.z);
                    }
                }
                if (ArtworkButtonBounds(button, camera, out art))
                {
                    float depth = camera.WorldToScreenPoint(button.transform.position).z;
                    button.transform.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth))
                        - camera.ScreenToWorldPoint(new Vector3(art.center.x, art.center.y, depth));
                }
            }
            ready &= FitArtworkText(button.m_ButtonText, camera, new Rect(target.xMin + target.width * .05f, target.yMin,
                target.width * .9f, target.height), Screen.height * (button.m_ButtonText.Text.Contains("\n") ? .042f : .024f));
            ComboSceneGeometry.TextAfterArtwork(button.m_ButtonText, button.m_RootObject);
            ComboSceneGeometry.FitButtonHitArea(button);
            return ready;
        }
        private static bool ArtworkButtonBounds(UIBButton button, Camera camera, out Rect bounds)
        {
            bool found = false; bounds = default;
            UIBHighlight[] highlights = button.GetComponentsInChildren<UIBHighlight>(true);
            foreach (Renderer renderer in button.m_RootObject.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled || renderer.GetComponentInParent<UberText>() != null
                    || ComboSceneGeometry.IsGlyphRenderer(renderer)
                    || FilterButtonFeedback.OwnsVisual(null, highlights, renderer.transform)) continue;
                Rect part = ComboSceneGeometry.ProjectBounds(renderer.bounds, camera);
                bounds = found ? Rect.MinMaxRect(Math.Min(bounds.xMin, part.xMin), Math.Min(bounds.yMin, part.yMin),
                    Math.Max(bounds.xMax, part.xMax), Math.Max(bounds.yMax, part.yMax)) : part;
                found = true;
            }
            return found && bounds.width > .001f && bounds.height > .001f;
        }
        private static bool FitArtworkText(UberText text, Camera camera, Rect target, float pixels)
        {
            text.Show(); text.UpdateNow(true);
            if (!CounterTextLayout.TryScreenInkRect(text, camera, out Rect ink) || ink.height <= .001f || ink.width <= .001f) return false;
            text.transform.localScale *= Math.Min(Math.Min(pixels, target.height * .85f) / ink.height, target.width / ink.width);
            text.UpdateNow(true);
            if (!CounterTextLayout.TryScreenInkRect(text, camera, out ink)) return false;
            float depth = camera.WorldToScreenPoint(text.transform.position).z;
            text.transform.position += camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth))
                - camera.ScreenToWorldPoint(new Vector3(ink.center.x, ink.center.y, depth));
            return true;
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
                collider.enabled = button != null && m_visible.Contains(button) && button.gameObject.activeInHierarchy
                    && (m_choices == null || collider == button.GetComponent<BoxCollider>());
                if (!collider.enabled) continue;
                if (!button.IsEnabled()) button.SetEnabled(true, true);
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
            m_choices = null; m_choiceAction = null; m_choiceRequest = null;
            if (IsShown() || m_showAttempted) base.Hide();
            m_showAttempted = false;
            canceled?.Invoke();
        }
        internal static void DestroyMenu()
        {
            if (s_instance == null) return;
            if (s_instance.m_choices == null) s_instance.m_canceled = null;
            s_instance.Hide(); UnityEngine.Object.Destroy(s_instance.gameObject); s_instance = null;
        }
    }
}
