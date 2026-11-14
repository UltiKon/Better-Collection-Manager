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
        internal static void CaptureDropdown(DropdownControl control, Camera cachedCamera, int sequence, string phase)
        {
            const string prefix = "[Mods dropdown render] ";
            try
            {
                Camera camera = CameraUtils.FindProjectionCameraForObject(control.gameObject);
                string tag = prefix + sequence + "/" + phase + " ";
                Type quality = typeof(Camera).Assembly.GetType("UnityEngine.QualitySettings");
                object qualityLevel = quality?.GetMethod("GetQualityLevel", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
                object shaderLod = typeof(Shader).GetProperty("globalMaximumLOD", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
                object pipeline = quality?.GetProperty("renderPipeline", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
                DebugLog.Msg(tag + "control=" + control.gameObject.name + " frame=" + Time.frameCount
                    + " nativeQuality=" + qualityLevel + " shaderLOD=" + shaderLod + " pipeline=" + ObjectId(pipeline) + " open=" + control.isMenuShown()
                    + " selection=" + control.getSelection() + " camera=" + ObjectId(camera) + " cachedCamera=" + ObjectId(cachedCamera));
                int cameras = 0;
                foreach (Camera candidate in Camera.allCameras)
                {
                    if (++cameras > 8) break;
                    DebugLog.Msg(tag + "camera=" + ObjectId(candidate) + " enabled=" + candidate.enabled
                        + " mask=" + candidate.cullingMask + Properties(candidate, "depth", "nearClipPlane", "farClipPlane", "targetTexture", "clearFlags", "pixelRect"));
                }
                void RendererState(string label, Renderer renderer)
                {
                    if (renderer == null) { DebugLog.Msg(tag + label + " renderer=<null>"); return; }
                    Vector3 screen = camera == null ? Vector3.zero : camera.WorldToScreenPoint(renderer.bounds.center);
                    DebugLog.Msg(tag + label + " renderer=" + ObjectId(renderer) + " path=" + Path(renderer.transform)
                        + " enabled=" + renderer.enabled + " active=" + renderer.gameObject.activeInHierarchy
                        + " forceOff=" + renderer.forceRenderingOff + " layer=" + renderer.gameObject.layer
                        + " renderingMask=" + renderer.renderingLayerMask + " sorting=" + renderer.sortingLayerID + "/" + renderer.sortingOrder
                        + " screen=" + screen + " bounds=" + renderer.bounds + Properties(renderer, "isVisible"));
                    foreach (Component component in renderer.GetComponentsInChildren<Component>(true))
                        if (component.GetType().Name == "PopupRenderer")
                        {
                            object root = component.GetType().GetProperty("PopupRoot", Members)?.GetValue(component, null);
                            DebugLog.Msg(tag + label + " popupRenderer root=" + ObjectId(root)
                                + Properties(root, "IsEnabled", "RenderingLayerMask", "IsBeingDisabledOrDestroyed")
                                + Properties(component, "IsDesiredHidden", "IsDesiredHiddenInHierarchy"));
                        }
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null)
                        {
                            Texture texture = material.mainTexture;
                            var keywords = material.GetType().GetProperty("shaderKeywords", Members)?.GetValue(material, null) as string[];
                            DebugLog.Msg(tag + label + " material=" + ObjectId(material) + " queue=" + material.renderQueue
                                + " shader=" + ObjectId(material.shader) + Properties(material.shader, "maximumLOD", "isSupported", "renderQueue")
                                + " texture=" + ObjectId(texture) + Properties(texture, "width", "height", "updateCount")
                                + " color=" + (material.HasProperty("_Color") ? material.color.ToString() : "n/a")
                                + " keywords=" + string.Join(",", keywords ?? Array.Empty<string>()) + Properties(material, "passCount"));
                        }
                }
                void Caption(string label, DropdownMenuItem row)
                {
                    UberText text = row?.m_text;
                    if (text == null) { DebugLog.Msg(tag + label + " text=<null>"); return; }
                    DebugLog.Msg(tag + label + " value=" + row.GetValue() + " text=" + text.Text.Replace("\n", " ")
                        + " owner=" + ObjectId(text) + " active=" + text.gameObject.activeInHierarchy + " enabled=" + text.enabled
                        + " hidden=" + text.isHidden() + " RTT=" + text.RenderToTexture + " queueIncrement=" + text.RenderQueue
                        + " font=" + ObjectId(text.TrueTypeFont) + " fontSize=" + text.FontSize + " charSize=" + text.CharacterSize
                        + " color=" + text.TextColor + " gradient=" + text.GradientUpperColor + "/" + text.GradientLowerColor
                        + " pose=" + text.transform.position + " scale=" + text.transform.lossyScale);
                    string runtime = "";
                    foreach (string field in new[] { "m_updated", "m_CurrentLayer", "m_HasRenderingLayerMaskOverride", "m_RenderingLayerMaskOverride", "m_popupRoot", "m_popupRenderingEnabled" })
                        runtime += " " + field + "=" + typeof(UberText).GetField(field, Members)?.GetValue(text);
                    DebugLog.Msg(tag + label + " runtime" + runtime);
                    RendererState(label + "/glyph", CounterTextLayout.GlyphRenderer(text));
                    if (text.RenderToTexture) RendererState(label + "/surface", CounterTextLayout.TextureSurface(text));
                }
                Caption("selected", control.m_selectedItem);
                Caption("template", control.m_menuItemTemplate);
                int rows = 0;
                foreach (DropdownMenuItem row in control.m_menuItemContainer.GetComponentsInChildren<DropdownMenuItem>(true))
                    if (row != control.m_menuItemTemplate && row != control.m_selectedItem)
                    {
                        if (rows < 3) Caption("row" + rows, row);
                        rows++;
                    }
                DebugLog.Msg(tag + "popupRows=" + rows);
                int art = 0;
                foreach (Renderer renderer in control.m_menu.GetComponentsInChildren<Renderer>(true))
                    if (renderer.GetComponentInParent<UberText>() == null && renderer.gameObject.activeInHierarchy)
                    {
                        RendererState("popupArt" + art, renderer);
                        if (++art == 4) break;
                    }
            }
            catch (Exception ex) { DebugLog.Exception("Mods dropdown render trace unavailable", ex); }
        }
        private static string ObjectId(object value)
        {
            if (value == null) return "<null>";
            if (value is UnityEngine.Object native)
                return native == null ? "<destroyed>" : native.name + "#" + native.GetHashCode();
            return value.GetType().Name + "#" + value.GetHashCode();
        }

        private static string Properties(object value, params string[] names)
        {
            if (value == null) return "";
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
