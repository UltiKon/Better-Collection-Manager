using System.Collections;

namespace BetterCollectionManager
{
    internal static class LoaderCoroutines
    {
#if BEPINEX
        internal static UnityEngine.MonoBehaviour Host;
        internal static object Start(IEnumerator routine) => Host.StartCoroutine(routine);
        internal static void Stop(object token)
        {
            if (Host != null && token is UnityEngine.Coroutine coroutine)
                Host.StopCoroutine(coroutine);
        }
#else
        internal static object Start(IEnumerator routine) => MelonLoader.MelonCoroutines.Start(routine);
        internal static void Stop(object token)
        {
            if (token != null) MelonLoader.MelonCoroutines.Stop(token);
        }
#endif
    }
}
