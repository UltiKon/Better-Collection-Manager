using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace BetterCollectionManager
{
    /// <summary>Reads native IL to resolve asset references and dependent hooks.</summary>
    internal static class NativeMethodReferences
    {
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

    }
}
