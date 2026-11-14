using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckArtworkPickerResize()
    {
        int width = Screen.width, height = Screen.height, selected = -1;
        bool animation = ButtonListMenu.SimulateNativeOpeningAnimation;
        var late = typeof(CatalogTransferMenu).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            ButtonListMenu.SimulateNativeOpeningAnimation = true;
            CatalogTransferMenu.OpenChoices("Choose background artwork",
                Enumerable.Range(0, 65).Select(i => "Artwork " + i).ToArray(), i => selected = i, () => { });
            var popup = ButtonListMenu.Visible;
            Check(popup.NativeOpeningAnimationPending, "Artwork picker retains the native opening animation.");
            for (int frame = 0; frame < 10; frame++) late.Invoke(popup, null);
            popup.CompleteNativeOpeningAnimation(); late.Invoke(popup, null);
            popup.Buttons.Single(b => b.gameObject.name == "BCM_ArtworkNext").Click();
            Screen.width = 800; Screen.height = 1280;
            late.Invoke(popup, null);
            Check(popup.Buttons.Count(b => b.gameObject.name.StartsWith("BCM_ArtworkChoice")) == 12
                && popup.Buttons[0].m_ButtonText.Text == "Artwork 24",
                "Portrait resizing uses two columns and preserves the page containing the former first choice.");
            foreach (UIBButton button in popup.Buttons)
            {
                button.Hover(); late.Invoke(popup, null);
                Check(ComboSceneGeometry.ScreenBounds(button.m_RootObject, CameraUtils.Camera, out Rect face)
                    && face.xMin >= 0 && face.xMax <= Screen.width && face.yMin >= 0 && face.yMax <= Screen.height
                    && CounterTextLayout.TryScreenInkRect(button.m_ButtonText, CameraUtils.Camera, out Rect ink)
                    && ink.width > 1 && ink.height > 1 && ink.xMin >= face.xMin && ink.xMax <= face.xMax
                    && ink.yMin >= face.yMin && ink.yMax <= face.yMax,
                    "Resized artwork buttons and their hovered captions remain visible within their faces and the window.");
            }
            Screen.width = width; Screen.height = height;
            late.Invoke(popup, null);
            Check(popup.Buttons.Count(b => b.gameObject.name.StartsWith("BCM_ArtworkChoice")) == 30,
                "Returning to landscape restores five columns.");
            var first = popup.Buttons[0];
            // Native templates can keep the caption under the animated artwork
            // root. Long text must not become part of the face measurement.
            first.m_ButtonText.transform.SetParent(first.m_RootObject.transform, true);
            first.m_ButtonText.Text = "Automatic (A long localized\nbackground artwork name)";
            Screen.width -= 1; late.Invoke(popup, null);
            ComboSceneGeometry.ScreenBounds(first.m_RootObject, CameraUtils.Camera, out Rect before);
            for (int frame = 0; frame < 60; frame++) { first.Hover(); late.Invoke(popup, null); }
            Check(ComboSceneGeometry.ScreenBounds(first.m_RootObject, CameraUtils.Camera, out Rect after)
                && Math.Abs(before.width - after.width) < .1f && Math.Abs(before.height - after.height) < .1f
                && CounterTextLayout.TryScreenInkRect(first.m_ButtonText, CameraUtils.Camera, out Rect caption)
                && caption.height > Screen.height * .02f,
                "A long caption nested under the face stays legible through repeated hover.");
            first.Click();
            Check(selected == 0 && !popup.IsShown(), "Selection still resolves to the correct artwork after resizing.");
            CatalogTransferMenu.OpenChoices("Choose background artwork", new[] { "Delayed text" }, i => { }, () => { });
            popup = ButtonListMenu.Visible; first = popup.Buttons[0];
            popup.CompleteNativeOpeningAnimation();
            first.m_ButtonText.TextGeometryReady = false;
            first.m_ButtonText.transform.localScale = Vector3.one * .001f;
            Screen.width += 1; late.Invoke(popup, null);
            first.m_ButtonText.TextGeometryReady = true;
            late.Invoke(popup, null);
            Check(CounterTextLayout.TryScreenInkRect(first.m_ButtonText, CameraUtils.Camera, out caption)
                && caption.height >= Screen.height * .02f,
                "A caption that loads after layout is retried before the picker caches its geometry.");
        }
        finally
        {
            CatalogTransferMenu.DestroyMenu();
            Screen.width = width; Screen.height = height;
            ButtonListMenu.SimulateNativeOpeningAnimation = animation;
        }
    }
}
