using System;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class PackageNavigation : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly ComboNativeArrow Back;
        private readonly ComboNativeArrow.Template m_template;
        internal PackageNavigation(Action back)
        {
            Root = new GameObject("BCM_PackageNavigation"); Root.SetActive(false);
            try
            {
                OverlayUI.Get().AddGameObject(Root);
                // Use the complete native button and its authored hover glow,
                // as in Inspect, rather than the book's separate highlight art.
                m_template = new ComboNativeArrow.Template();
                Back = new ComboNativeArrow(Root.transform, true, back,
                    pressSound: ComboCollectionCards.PackageClickSound, template: m_template);
            }
            catch { Dispose(); throw; }
        }
        internal void Layout(Rect anchor, bool visible)
        {
            Root.SetActive(visible); Back.Root.SetActive(visible);
            if (!visible) { Back.Update(); return; }
            Camera camera = CameraUtils.FindProjectionCameraForObject(Back.Root);
            if (camera == null) return;
            float height = Math.Max(36f, Math.Min(anchor.height * .25f, Screen.height * .065f)) * 1.25f;
            float left = Mathf.Clamp(anchor.xMin, height * .5f, Math.Max(height * .5f, Screen.width - height * 1.5f));
            float bottom = Mathf.Clamp(anchor.yMax + height * .35f, height * .5f, Math.Max(height * .5f, Screen.height - height * 1.5f));
            float margin = Math.Max(.1f, (camera.farClipPlane - camera.nearClipPlane) * .01f);
            float depth = Mathf.Clamp(camera.WorldToScreenPoint(Root.transform.position).z,
                camera.nearClipPlane + margin, camera.farClipPlane - margin);
            Back.Layout(camera, new Rect(left, bottom, height, height), depth, true);
            Back.Update();
        }
        public void Dispose()
        {
            Back?.Dispose();
            m_template?.Dispose();
            if (Root != null) { Root.SetActive(false); UnityEngine.Object.Destroy(Root); }
        }
    }
}
