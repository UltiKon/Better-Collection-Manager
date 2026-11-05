using System;
using System.Linq;
using System.Reflection;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboDismissal(CategoryCatalog.Combo combo)
    {
        int width = Screen.width, height = Screen.height, frame = Time.frameCount;
        Vector3 pointer = Input.mousePosition; bool released = Input.LeftReleased;
        var update = typeof(ComboPopup).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        var late = typeof(ComboPopup).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        var blocker = typeof(ComboPopup).GetMethod("OnBlockerRelease", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            foreach (var size in new[] { (887, 799), (1920, 1080) })
            {
                Screen.width = size.Item1; Screen.height = size.Item2; Input.LeftReleased = false;
                ComboPopup.Inspect(combo); var popup = (ComboPopup)ButtonListMenu.Visible;
                Check(popup.Buttons.All(button => button.gameObject.name != "BCM_CloseCombo")
                    && !popup.ActiveButtons.Any(button => button.gameObject.name == "BCM_CloseCombo"),
                    "Inspect has no Close button at either narrow or wide aspect ratios");
                Input.mousePosition = Vector3.zero; Input.LeftReleased = true;
                update.Invoke(popup, null); blocker.Invoke(popup, new object[] { new UIEvent() }); late.Invoke(popup, null);
                Check(popup.IsShown(), "The release opening inspect cannot immediately dismiss it");
                Time.frameCount += 3;
                foreach (Actor actor in popup.GetComponentsInChildren<Actor>(false))
                {
                    ComboSceneGeometry.CardFaceBounds(actor, CameraUtils.Camera, out Rect card);
                    Input.mousePosition = new Vector3(card.center.x, card.center.y, 0);
                    update.Invoke(popup, null); blocker.Invoke(popup, new object[] { new UIEvent() }); late.Invoke(popup, null);
                    Check(popup.IsShown(), "Clicking a visible component card keeps inspect open despite disabled preview colliders");
                }
                foreach (UIBButton button in popup.Buttons)
                {
                    ComboSceneGeometry.ScreenBounds(button.gameObject, CameraUtils.Camera, out Rect control);
                    Input.mousePosition = new Vector3(control.center.x, control.center.y, 0);
                    update.Invoke(popup, null); blocker.Invoke(popup, new object[] { new UIEvent() }); late.Invoke(popup, null);
                    Check(popup.IsShown(), "Button hit regions are protected from the inspect off-click catcher");
                }
                var flavor = popup.GetComponentsInChildren<Transform>(false).Single(part => part.gameObject.name == "BCM_ComboFlavorPane");
                Check(ComboSceneGeometry.ScreenBounds(flavor.gameObject, CameraUtils.Camera, out Rect panel)
                    && !flavor.gameObject.GetComponentsInChildren<Collider>(true).Any(hit => hit.enabled),
                    "The native flavor pane displays the description without stealing outside-click input");
                Input.mousePosition = new Vector3(panel.center.x, panel.center.y, 0);
                update.Invoke(popup, null);
                Check(popup.IsShown() && UniversalInputManager.Get().DialogActive,
                    "The fallback defers dismissal until after PegUI release dispatch so the collection cannot receive the same click");
                late.Invoke(popup, null);
                Check(!popup.IsShown() && !ScreenEffectsHandle.Active.Any(effect => effect.Owner == popup),
                    "A click on the description rather than a card or button closes inspect and releases its blur");
                Input.LeftReleased = false; ComboPopup.Inspect(combo); Time.frameCount += 3;
                Input.mousePosition = Vector3.zero;
                int sounds = SoundManager.Get().Played.Count;
                blocker.Invoke(popup, new object[] { new UIEvent() }); blocker.Invoke(popup, new object[] { new UIEvent() });
                Check(!popup.IsShown() && !UniversalInputManager.Get().DialogActive
                    && SoundManager.Get().Played.Count == sounds + 1,
                    "The native background blocker closes inspect once and plays one native close sound");
            }
        }
        finally
        {
            ComboPopup.Close(); Screen.width = width; Screen.height = height; Time.frameCount = frame;
            Input.mousePosition = pointer; Input.LeftReleased = released;
        }
    }
}
