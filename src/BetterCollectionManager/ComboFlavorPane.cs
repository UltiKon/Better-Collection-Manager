using System;
using Blizzard.T5.AssetManager;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class ComboFlavorPane : IDisposable
    {
        private readonly AssetHandle<GameObject> m_asset;
        internal readonly GameObject Root;
        internal readonly UberText Template;
        private readonly UberText m_category;
        private readonly GameObject m_standard, m_wild;
        private readonly Vector3 m_scale;
        internal Rect ContentArea;
        internal ComboFlavorPane(Transform parent)
        {
            m_asset = AssetLoader.Get().LoadAsset<GameObject>("CardInfoPane.prefab:b9220edd61d504be38fab162c18e56f1");
            try
            {
                if (m_asset?.Asset?.GetComponent<CardInfoPane>()?.m_flavorText == null)
                    throw new InvalidOperationException("Native flavor-text artwork is unavailable.");
                Root = new GameObject("BCM_ComboFlavorPane"); Root.SetActive(false); Root.transform.SetParent(parent, false);
                GameObject shell = UnityEngine.Object.Instantiate(m_asset.Asset, Root.transform, false);
                CardInfoPane pane = shell.GetComponent<CardInfoPane>();
                Template = pane.m_flavorText; m_category = pane.m_setName;
                m_standard = pane.m_standardTheming; m_wild = pane.m_wildTheming;
                foreach (UberText text in shell.GetComponentsInChildren<UberText>(true)) text.Hide();
                foreach (Collider hit in shell.GetComponentsInChildren<Collider>(true)) hit.enabled = false;
                if (pane.m_trialCardsInspectPlateWidget != null) pane.m_trialCardsInspectPlateWidget.gameObject.SetActive(false);
                foreach (Transform part in shell.GetComponentsInChildren<Transform>(true))
                    if (!Template.transform.IsChildOf(part) && (part.gameObject.name.IndexOf("artist", StringComparison.OrdinalIgnoreCase) >= 0
                        || part.gameObject.name.IndexOf("rarity", StringComparison.OrdinalIgnoreCase) >= 0
                        || part.gameObject.name.IndexOf("gem", StringComparison.OrdinalIgnoreCase) >= 0)) part.gameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(pane);
                shell.SetActive(true); m_scale = shell.transform.localScale;
                LayerUtils.SetLayer(shell.transform, GameLayer.HighPriorityUI);
            }
            catch { if (Root != null) UnityEngine.Object.Destroy(Root); m_asset?.Dispose(); throw; }
        }
        internal void Layout(Camera camera, Rect target, float depth, string category, string description = null)
        {
            Root.SetActive(true);
            bool wild = CollectionManager.Get().GetThemeShowing() == PegasusShared.FormatType.FT_WILD;
            if (m_standard != null) m_standard.SetActive(!wild);
            if (m_wild != null) m_wild.SetActive(wild);
            Transform shell = Template.transform;
            while (shell.parent != Root.transform) shell = shell.parent;
            shell.localScale = m_scale;
            shell.position = camera.ScreenToWorldPoint(new Vector3(target.center.x, target.center.y, depth));
            ComboSceneGeometry.FaceCaption(shell, Template, camera);
            if (m_category != null)
            {
                m_category.GameStringLookup = m_category.RichText = false;
                m_category.Text = category ?? ""; m_category.Show(); m_category.UpdateNow(true);
            }
            ComboSceneGeometry.Fit(shell, camera, target);
            ContentArea = ComboSceneGeometry.ProjectBounds(Template.GetBounds(), camera);
            if (description != null)
            {
                Template.GameStringLookup = Template.RichText = false;
                Template.TextColor = new Color(0, 0, 0, 1);
                Template.Text = description; Template.Show(); Template.UpdateNow(true);
                ComboSceneGeometry.TextInFront(Root, camera);
                ComboSceneGeometry.TextAfterArtwork(Template, Root);
            }
        }
        public void Dispose() { UnityEngine.Object.Destroy(Root); m_asset?.Dispose(); }
    }
}
