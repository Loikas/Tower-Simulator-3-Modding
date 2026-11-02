using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TS3Modding.Tugs
{
    internal static class TugHomeInstrumentation
    {
        // Apply after the native home decision and before it changes home/sleep or dispatches movement.
        public static IEnumerable<CodeInstruction> Inject(IEnumerable<CodeInstruction> source, FieldInfo home, MethodInfo decide)
        {
            var code = source.ToList();
            var matches = Enumerable.Range(0, Math.Max(0, code.Count - 3)).Where(i =>
                code[i].opcode == OpCodes.Ldarg_0 && code[i + 1].LoadsField(home) &&
                code[i + 2].opcode == OpCodes.Ldloc_1 &&
                (code[i + 3].opcode == OpCodes.Beq || code[i + 3].opcode == OpCodes.Beq_S)).ToArray();
            if (matches.Length != 1) throw new NotSupportedException("Native tug home decision is ambiguous.");
            var index = matches[0];
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[index].labels); code[index].labels.Clear();
            first.blocks.AddRange(code[index].blocks); code[index].blocks.Clear();
            code.InsertRange(index, new[] { first, new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Call, decide), new CodeInstruction(OpCodes.Stloc_1) });
            return code;
        }
    }
}
