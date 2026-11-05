using UnityEngine;

namespace BetterCollectionManager
{
    internal static class CustomFrameRate
    {
        private static bool s_overridden;
        private static int s_nativeTarget, s_nativeVSync;
        public static void Update()
        {
            if (!CollectionQualitySettings.UseCustomFrameRate) { Restore(); return; }
            if (!Application.isFocused) return; // Keep native background throttling.
            if (!s_overridden)
            {
                s_nativeTarget = Application.targetFrameRate;
                s_nativeVSync = QualitySettings.vSyncCount;
                s_overridden = true;
            }
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate != CollectionQualitySettings.CustomFrameRate)
                Application.targetFrameRate = CollectionQualitySettings.CustomFrameRate;
        }
        public static void Restore()
        {
            if (!s_overridden) return;
            Application.targetFrameRate = s_nativeTarget;
            QualitySettings.vSyncCount = s_nativeVSync;
            s_overridden = false;
        }
    }
}
