namespace UnityEngine
{
    public static class GUIUtility { public static string systemCopyBuffer { get; set; } }
    public static class Application { public static int targetFrameRate = -1; public static bool isFocused = true; }
    public static class QualitySettings { public static int vSyncCount; public static ColorSpace activeColorSpace; }
    // Match Hearthstone's restricted Font API: no OS-font factory.
    public sealed class Font : Object { }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
}
