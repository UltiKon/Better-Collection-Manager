using System;
#if BEPINEX
using BepInEx.Logging;
#else
using MelonLoader;
#endif

namespace BetterCollectionManager
{
    /// <summary>
    /// Loader logging wrapper. Set <see cref="VerboseDebug"/> to false to silence
    /// per-card / per-UpdateResults sample dumps while keeping enter/leave/skip/exception logs.
    /// </summary>
    internal static class DebugLog
    {
        /// <summary>OFF — per-card / PegRay / AlignPlaque dumps stay quiet.</summary>
        public static bool VerboseDebug = false;

#if BEPINEX
        public static ManualLogSource Logger;
#else
        public static MelonLogger.Instance Logger;
#endif

        public static void Msg(string message)
        {
#if BEPINEX
            Logger?.LogInfo(message);
#else
            if (Logger != null)
                Logger.Msg(message);
            else
                MelonLogger.Msg($"[BetterCollectionManager] {message}");
#endif
        }

        public static void Warning(string message)
        {
#if BEPINEX
            Logger?.LogWarning(message);
#else
            if (Logger != null)
                Logger.Warning(message);
            else
                MelonLogger.Warning($"[BetterCollectionManager] {message}");
#endif
        }

        public static void Error(string message)
        {
#if BEPINEX
            Logger?.LogError(message);
#else
            if (Logger != null)
                Logger.Error(message);
            else
                MelonLogger.Error($"[BetterCollectionManager] {message}");
#endif
        }

        public static void Exception(string context, Exception ex)
        {
            Error($"{context}: {ex}");
            if (ex != null && !string.IsNullOrEmpty(ex.StackTrace))
                Error(ex.StackTrace);
        }

        public static void Verbose(string message)
        {
            if (!VerboseDebug)
                return;
            Msg(message);
        }
    }
}
