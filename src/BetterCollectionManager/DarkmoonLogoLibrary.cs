using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Blizzard.T5.AssetManager;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class DarkmoonLogoLibrary
    {
        private static readonly FieldInfo s_native = AccessTools.Field(typeof(LuckyDrawUtils), "m_assetReferenceLibrary");
        private static AssetHandle<LuckyDrawAssetReferenceLibrary> s_handle;
        private static bool s_loading;
        private static int s_revision;
        private static float s_started;
        private static readonly List<Action<Exception>> s_waiters = new List<Action<Exception>>();

        internal static LuckyDrawAssetReferenceLibrary Current
        {
            get
            {
                var native = s_native?.GetValue(null) as LuckyDrawAssetReferenceLibrary;
                return native != null ? native : s_handle?.Asset;
            }
        }

        internal static void EnsureLoaded(Action ready, Action<Exception> failed)
        {
            if (Current != null) { ready(); return; }
            if (s_loading && Time.unscaledTime - s_started > 30f)
            {
                s_revision++;
                Complete(new TimeoutException("Native DFT library load timed out"));
            }
            s_waiters.Add(error => { if (error == null) ready(); else failed(error); });
            if (s_loading) return;
            s_loading = true;
            s_started = Time.unscaledTime;
            int revision = ++s_revision;
            try
            {
                // The client owns the root-library address. Read its literal
                // asset reference rather than pinning a GUID in the mod or
                // calling the client's synchronous lazy-loading getter.
                string reference = NativeReference();
                if (!AssetLoader.Get().LoadAsset<LuckyDrawAssetReferenceLibrary>(reference, (asset, handle, data) =>
                {
                    if (revision != s_revision) { handle?.Dispose(); return; }
                    if (handle?.Asset == null)
                    {
                        handle?.Dispose();
                        Complete(new InvalidOperationException("Native DFT asset library did not load"));
                        return;
                    }
                    s_handle?.Dispose();
                    s_handle = handle;
                    Complete(null);
                })) throw new InvalidOperationException("Native DFT asset library is not available for asynchronous loading");
            }
            catch (Exception ex) { Complete(ex); }
        }

        private static string NativeReference()
        {
            MethodInfo method = AccessTools.Method(typeof(LuckyDrawUtils), "GetAssetLibrary");
            if (method == null) throw new MissingMethodException("LuckyDrawUtils.GetAssetLibrary");
            string found = null;
            foreach (var instruction in PatchProcessor.GetOriginalInstructions(method))
            {
                if (instruction.opcode != OpCodes.Ldstr || !(instruction.operand is string value)) continue;
                int colon = value.IndexOf(':');
                if (colon < 6 || !value.Substring(0, colon).EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    || !Guid.TryParseExact(value.Substring(colon + 1), "N", out _)) continue;
                if (found != null && found != value)
                    throw new InvalidOperationException("Native DFT library loader has multiple asset references");
                found = value;
            }
            return found ?? throw new InvalidOperationException("Native DFT library address could not be discovered");
        }

        private static void Complete(Exception error)
        {
            s_loading = false;
            var callbacks = s_waiters.ToArray();
            s_waiters.Clear();
            foreach (var callback in callbacks)
                try { callback(error); }
                catch (Exception ex) { DebugLog.Exception("Hub: DFT library completion failed", ex); }
        }
    }
}
