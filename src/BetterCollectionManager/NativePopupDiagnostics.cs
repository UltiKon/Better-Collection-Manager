using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>One read-only snapshot of the installed game's popup implementation.</summary>
    internal static class NativePopupDiagnostics
    {
        private static bool s_captured;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<short, OpCode> s_opcodes = CreateOpCodes();
        private static Dictionary<short, OpCode> CreateOpCodes()
        {
            var result = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is OpCode opcode) result[opcode.Value] = opcode;
            return result;
        }
        internal static IReadOnlyList<MethodBase> CalledMethods(MethodBase method)
        {
            var calls = new List<MethodBase>();
            try
            {
                byte[] code = method.GetMethodBody()?.GetILAsByteArray();
                if (code == null) return calls;
                for (int offset = 0; offset < code.Length;)
                {
                    short value = code[offset++] == 0xfe ? (short)(0xfe00 | code[offset++]) : (short)code[offset - 1];
                    if (!s_opcodes.TryGetValue(value, out OpCode opcode)) break;
                    int length;
                    switch (opcode.OperandType)
                    {
                        case OperandType.InlineNone: length = 0; break;
                        case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: length = 1; break;
                        case OperandType.InlineVar: length = 2; break;
                        case OperandType.InlineI8: case OperandType.InlineR: length = 8; break;
                        case OperandType.InlineSwitch: length = 4 + 4 * BitConverter.ToInt32(code, offset); break;
                        default: length = 4; break;
                    }
                    if (opcode.OperandType == OperandType.InlineMethod)
                        try { calls.Add(method.Module.ResolveMethod(BitConverter.ToInt32(code, offset), method.DeclaringType.GetGenericArguments(), (method as MethodInfo)?.GetGenericArguments() ?? Type.EmptyTypes)); }
                        catch { }
                    offset += length;
                }
            }
            catch { }
            return calls;
        }

        internal static IEnumerable<string> ReferencedStrings(MethodBase method)
        {
            var result = new List<string>();
            try
            {
                byte[] code = method.GetMethodBody()?.GetILAsByteArray();
                if (code == null) return result;
                for (int offset = 0; offset < code.Length;)
                {
                    short value = code[offset++] == 0xfe ? (short)(0xfe00 | code[offset++]) : (short)code[offset - 1];
                    if (!s_opcodes.TryGetValue(value, out OpCode opcode)) break;
                    int length;
                    switch (opcode.OperandType)
                    {
                        case OperandType.InlineNone: length = 0; break;
                        case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: length = 1; break;
                        case OperandType.InlineVar: length = 2; break;
                        case OperandType.InlineI8: case OperandType.InlineR: length = 8; break;
                        case OperandType.InlineSwitch: length = 4 + 4 * BitConverter.ToInt32(code, offset); break;
                        default: length = 4; break;
                    }
                    if (opcode.OperandType == OperandType.InlineString) result.Add(method.Module.ResolveString(BitConverter.ToInt32(code, offset)));
                    offset += length;
                }
            }
            catch { }
            return result;
        }

        public static void Capture(Component controller, object definition, Camera projection, params object[] surfaces)
        {
            if (s_captured) return;
            s_captured = true;
            try
            {
                var roots = new HashSet<GameObject> { controller.gameObject };
                if (definition is Component menu) roots.Add(menu.gameObject);
                DebugLog.Msg("[Popup native] definition=" + definition.GetType().FullName + " base=" + definition.GetType().BaseType);
                foreach (FieldInfo field in definition.GetType().GetFields(Members))
                {
                    object value = field.GetValue(definition);
                    DebugLog.Msg("[Popup native] field " + field.Name + " type=" + field.FieldType.FullName);
                    if (value is Component component) roots.Add(component.gameObject);
                    else if (value is GameObject go) roots.Add(go);
                }
                foreach (Camera camera in Camera.allCameras)
                    DebugLog.Msg("[Popup native] camera projection=" + ReferenceEquals(camera, projection)
                        + " enabled=" + camera.enabled + " mask=" + camera.cullingMask
                        + Properties(camera, "name", "nearClipPlane", "farClipPlane", "depth", "clearFlags", "backgroundColor", "targetTexture", "rect"));
                var renderers = new HashSet<Renderer>();
                foreach (GameObject root in roots)
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderers.Add(renderer);
                int count = 0;
                foreach (Renderer renderer in renderers)
                {
                    if (++count > 120) { DebugLog.Msg("[Popup native] renderer snapshot limit reached"); break; }
                    Bounds bounds = renderer.bounds;
                    Vector3 screen = projection == null ? Vector3.zero : projection.WorldToScreenPoint(bounds.center);
                    string materials = "";
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null) materials += " material=" + material.name + " queue=" + material.renderQueue + Properties(material, "shader");
                    DebugLog.Msg("[Popup native] renderer=" + Path(renderer.transform) + " enabled=" + renderer.enabled
                        + " active=" + renderer.gameObject.activeInHierarchy + " layer=" + renderer.gameObject.layer
                        + " sorting=" + renderer.sortingLayerID + "/" + renderer.sortingOrder
                        + " screen=" + screen + " worldBounds=" + bounds + materials);
                }
                foreach (string name in new[] { "Awake", "Show", "SetTransform", "LayoutMenu", "LayoutMenuBackground", "Hide" })
                    foreach (MethodInfo method in typeof(ButtonListMenu).GetMethods(Members | BindingFlags.DeclaredOnly))
                        if (method.Name == name) DumpMethod(method);
                var types = new HashSet<Type>();
                foreach (object surface in surfaces)
                    if (surface != null && types.Add(surface.GetType()))
                        foreach (MethodInfo method in surface.GetType().GetMethods(Members))
                            if (method.Name == "SetSize") DumpMethod(method);
            }
            catch (Exception ex) { DebugLog.Exception("Native popup snapshot unavailable", ex); }
        }
        private static string Properties(object value, params string[] names)
        {
            string result = "";
            foreach (string name in names)
            {
                try
                {
                    PropertyInfo property = value.GetType().GetProperty(name, Members);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        result += " " + name + "=" + Convert.ToString(property.GetValue(value, null), CultureInfo.InvariantCulture);
                }
                catch { result += " " + name + "=<unavailable>"; }
            }
            return result;
        }
        private static string Path(Transform node)
        {
            string result = node.gameObject.name;
            for (int i = 0; node.parent != null && i < 10; i++) { node = node.parent; result = node.gameObject.name + "/" + result; }
            return result;
        }
        private static void DumpMethod(MethodInfo method)
        {
            try
            {
                byte[] code = method.GetMethodBody()?.GetILAsByteArray();
                if (code == null) return;
                string label = method.DeclaringType.FullName + "." + method;
                // Include resolved metadata: raw IL tokens alone cannot be read
                // outside the installed game assembly.
                DebugLog.Msg("[Popup native IL] " + label + " bytes=" + Convert.ToBase64String(code));
                for (int offset = 0; offset < code.Length;)
                {
                    int start = offset;
                    short value = code[offset++] == 0xfe ? (short)(0xfe00 | code[offset++]) : (short)code[offset - 1];
                    if (!s_opcodes.TryGetValue(value, out OpCode opcode)) break;
                    int length;
                    switch (opcode.OperandType)
                    {
                        case OperandType.InlineNone: length = 0; break;
                        case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: length = 1; break;
                        case OperandType.InlineVar: length = 2; break;
                        case OperandType.InlineI8: case OperandType.InlineR: length = 8; break;
                        case OperandType.InlineSwitch: length = 4 + 4 * BitConverter.ToInt32(code, offset); break;
                        default: length = 4; break;
                    }
                    if (opcode.OperandType == OperandType.InlineField || opcode.OperandType == OperandType.InlineMethod
                        || opcode.OperandType == OperandType.InlineType || opcode.OperandType == OperandType.InlineTok)
                    {
                        int token = BitConverter.ToInt32(code, offset);
                        try
                        {
                            MemberInfo member = method.Module.ResolveMember(token);
                            DebugLog.Msg("[Popup native IL] " + method.Name + " @" + start.ToString("X4") + " " + opcode.Name
                                + " " + member.DeclaringType?.FullName + "::" + member);
                        }
                        catch { }
                    }
                    else if (opcode.OperandType == OperandType.InlineString)
                    {
                        string text = method.Module.ResolveString(BitConverter.ToInt32(code, offset)).Replace("\r", "\\r").Replace("\n", "\\n");
                        DebugLog.Msg("[Popup native IL] " + method.Name + " @" + start.ToString("X4") + " string=" + text);
                    }
                    offset += length;
                }
            }
            catch (Exception ex) { DebugLog.Warning("[Popup native IL] " + method.Name + ": " + ex.Message); }
        }
    }
}
