using System;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class DeckArchiveSwitchPose
    {
        internal static void Open(CollectionDeckTrayButton button)
        {
            button.gameObject.SetActive(true);
            button.StopAllCoroutines();
            Animation animation = button.GetComponent<Animation>();
            AnimationClip clip = animation != null ? animation.GetClip("NewDeck_PopUp") : null;
            if (clip == null) throw new InvalidOperationException("Native New Deck opening animation is unavailable.");
            animation.Stop();
            // Clipping can deactivate a clone before a fast Play() advances its
            // folded pose. Sample the visible end pose synchronously instead.
            clip.SampleAnimation(button.gameObject, clip.length);
            animation.enabled = false;
            AccessTools.Field(typeof(CollectionDeckTrayButton), "m_isPoppedUp").SetValue(button, true);
            button.SetEnabled(true);
        }
    }
}
