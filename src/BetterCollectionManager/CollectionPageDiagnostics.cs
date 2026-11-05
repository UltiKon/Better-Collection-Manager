using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Opt-in, bounded native call capture; does not invoke page operations.</summary>
    internal static class CollectionPageDiagnostics
    {
        private static HarmonyLib.Harmony s_harmony;
        private static MethodInfo s_keyDown;
        private static object[] s_keyArguments;
        private static bool s_hooksInstalled, s_recording, s_installed;
        private static float s_started;
        private static int s_events;
        private static readonly List<MethodInfo> s_methods = new List<MethodInfo>();
        private const int MaxEvents = 200;
        private const float MaxSeconds = 20f;
        internal static bool Recording => s_recording;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            if (s_installed) return;
            s_installed = true; s_harmony = harmony;
            try
            {
                s_keyDown = ResolveKeyDown(typeof(Input), out s_keyArguments);
                var types = new HashSet<Type>();
                Assembly game = typeof(CollectionManager).Assembly;
                foreach (string name in new[] { "CollectiblePageManager", "CollectibleDisplay", "CollectionManagerDisplay" })
                {
                    Type type = game.GetType(name, false);
                    if (type == null)
                        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                        { type = assembly.GetType(name, false); if (type != null) break; }
                    if (type == null) continue;
                    types.Add(type);
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        Type related = field.FieldType;
                        if (related.Assembly == game && (related.Name.IndexOf("Book", StringComparison.OrdinalIgnoreCase) >= 0
                            || related.Name.IndexOf("Page", StringComparison.OrdinalIgnoreCase) >= 0)) types.Add(related);
                    }
                }
                foreach (Type type in types) s_methods.AddRange(CandidateMethods(type, false));
                s_methods.AddRange(CandidateMethods(typeof(SoundManager), true));
                DebugLog.Msg("[Page trace] Ready. Press F8 in the collection to capture native page/filter/sound calls for 20 seconds.");
                if (s_keyDown == null) DebugLog.Warning("[Page trace] F8 binding unavailable; capture will start on collection entry instead.");
            }
            catch (Exception ex) { DebugLog.Warning("[Page trace] Optional diagnostics setup failed: " + ex.Message); }
        }

        internal static MethodInfo ResolveKeyDown(Type input, out object[] arguments)
        {
            foreach (string name in new[] { "GetKeyDown", "GetKeyDownInt", "GetKeyDownString" })
            foreach (MethodInfo method in input.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != name || method.ReturnType != typeof(bool) || parameters.Length != 1) continue;
                Type key = parameters[0].ParameterType;
                if (key == typeof(string)) arguments = new object[] { "f8" };
                else if (key.IsEnum) arguments = new[] { Enum.ToObject(key, 289) };
                else if (key == typeof(int)) arguments = new object[] { 289 };
                else continue;
                return method;
            }
            arguments = null; return null;
        }

        internal static IEnumerable<MethodInfo> CandidateMethods(Type type, bool sound)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                string name = method.Name;
                // Status probes run every frame and otherwise exhaust the event budget
                // before a user can turn a page (IsPlaying / CanUserTurnPages).
                if (method.IsAbstract || method.ContainsGenericParameters || method.IsSpecialName
                    || name.StartsWith("Get", StringComparison.Ordinal)
                    || name.StartsWith("Is", StringComparison.Ordinal)
                    || name.StartsWith("Can", StringComparison.Ordinal)
                    || name.StartsWith("Has", StringComparison.Ordinal)) continue;
                bool candidate = sound ? name.StartsWith("Play", StringComparison.Ordinal)
                        || name.StartsWith("LoadAndPlay", StringComparison.Ordinal)
                        || name.StartsWith("OnLoadAndPlay", StringComparison.Ordinal)
                    : name.IndexOf("Page", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Flip", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Animation", StringComparison.OrdinalIgnoreCase) >= 0
                        || name == "UpdateFilteredCards" || name == "SetFilterCallback";
                if (candidate) yield return method;
            }
        }

        public static void OnUpdate()
        {
            if (s_keyDown != null)
            {
                try
                {
                    if ((bool)s_keyDown.Invoke(null, s_keyArguments))
                    { if (s_recording) Stop("F8"); else Arm(); }
                }
                catch (Exception ex)
                { s_keyDown = null; DebugLog.Warning("[Page trace] Hotkey unavailable: " + ex.Message); }
            }
            if (s_recording && Time.unscaledTime - s_started >= MaxSeconds) Stop("time limit");
        }

        public static void CollectionEntered()
        {
            if (s_installed && s_keyDown == null) Arm();
        }

        internal static void Arm()
        {
            if (!s_hooksInstalled && s_harmony != null)
            {
                var prefix = new HarmonyMethod(typeof(CollectionPageDiagnostics).GetMethod(nameof(Trace)));
                foreach (MethodInfo method in s_methods)
                {
                    try { s_harmony.Patch(method, prefix: prefix); }
                    catch (Exception ex) { DebugLog.Warning("[Page trace] Hook unavailable: " + method + ": " + ex.Message); }
                }
                s_hooksInstalled = true;
            }
            s_started = Time.unscaledTime; s_events = 0; s_recording = true;
            DebugLog.Msg("[Page trace] START: turn a native page, then apply a filter. F8 stops capture; automatic stop after 20 seconds/200 events.");
            foreach (MethodInfo method in s_methods)
                DebugLog.Msg("[Page trace] API " + method.DeclaringType.FullName + ": " + method);
            // Capture the installed game's transition names and numeric values so
            // a later fix can select an observed animation without guessing APIs.
            var enums = new HashSet<Type>();
            foreach (MethodInfo method in s_methods)
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Type type = parameter.ParameterType;
                if (!type.IsEnum || !enums.Add(type)) continue;
                foreach (object value in Enum.GetValues(type))
                    DebugLog.Msg("[Page trace] ENUM " + type.FullName + "." + value + "="
                        + Enum.Format(type, value, "D"));
            }
        }

        public static void Stop(string reason)
        {
            if (!s_recording) return;
            s_recording = false;
            DebugLog.Msg("[Page trace] STOP: " + reason + "; events=" + s_events);
        }

        public static void Mark(string stage)
        {
            if (!s_recording) return;
            Emit("custom-filter: " + stage);
        }

        public static void Trace(MethodBase __originalMethod, object[] __args)
        {
            if (!s_recording) return;
            try
            {
                var text = new StringBuilder();
                text.Append(__originalMethod.DeclaringType.Name).Append('.').Append(__originalMethod.Name).Append('(');
                ParameterInfo[] parameters = __originalMethod.GetParameters();
                for (int i = 0; i < __args.Length; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append(i < parameters.Length ? parameters[i].Name : "arg").Append('=');
                    object value = __args[i];
                    if (value == null) text.Append("null");
                    else if (value is string str) text.Append('"').Append(str.Length > 160 ? str.Substring(0, 160) : str).Append('"');
                    else if (value.GetType().IsPrimitive || value.GetType().IsEnum) text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    else text.Append('<').Append(value.GetType().Name).Append('>');
                }
                text.Append(") soundAllowed=").Append(FilterButtonFeedback.ShouldPlaySound()).Append(PageState());
                if (__originalMethod.DeclaringType == typeof(SoundManager))
                {
                    StackFrame[] frames = new StackTrace(1, false).GetFrames();
                    text.Append(" callers=");
                    for (int i = 0; frames != null && i < Math.Min(6, frames.Length); i++)
                    {
                        MethodBase caller = frames[i].GetMethod();
                        if (i > 0) text.Append(" <- ");
                        text.Append(caller?.DeclaringType?.Name).Append('.').Append(caller?.Name);
                    }
                }
                Emit(text.ToString());
            }
            catch (Exception ex) { Stop("diagnostic error: " + ex.Message); }
        }

        private static string PageState()
        {
            try
            {
                object manager = CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager();
                if (manager == null) return " page=<unavailable>";
                PropertyInfo page = manager.GetType().GetProperty("CurrentPageNum",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return page == null ? "" : " currentPage=" + Convert.ToString(page.GetValue(manager, null), CultureInfo.InvariantCulture);
            }
            catch { return " page=<unavailable>"; }
        }

        private static void Emit(string text)
        {
            if (Time.unscaledTime - s_started >= MaxSeconds) { Stop("time limit"); return; }
            DebugLog.Msg("[Page trace] #" + (++s_events) + " +"
                + (Time.unscaledTime - s_started).ToString("F3", CultureInfo.InvariantCulture) + "s " + text);
            if (s_events >= MaxEvents) Stop("event limit");
        }
    }
}
