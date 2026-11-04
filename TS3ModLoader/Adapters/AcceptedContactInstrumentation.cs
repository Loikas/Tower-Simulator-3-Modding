using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TS3Modding.Adapters;

internal static class AcceptedContactInstrumentation
{
    internal static IEnumerable<CodeInstruction> InjectMissedDeparture(IEnumerable<CodeInstruction> instructions, FieldInfo owner, FieldInfo flyAway, MethodInfo callback)
    {
        var code = instructions.ToList();
        var sites = Enumerable.Range(2, Math.Max(0, code.Count - 2)).Where(i =>
            code[i].opcode == OpCodes.Stfld && Equals(code[i].operand, owner) &&
            code[i - 2].opcode == OpCodes.Ldstr && Equals(code[i - 2].operand, "departure") &&
            code.Skip(Math.Max(0, i - 24)).Take(Math.Min(i, 24)).Any(c => c.opcode == OpCodes.Ldstr && Equals(c.operand, "MSG_FORGOT_DEPARTURE")) &&
            code.Skip(Math.Max(0, i - 12)).Take(Math.Min(i, 12)).Any(c => c.opcode == OpCodes.Stfld && Equals(c.operand, flyAway))).ToArray();
        if (owner == null || flyAway == null || callback == null || sites.Length != 1) throw new NotSupportedException("Missed-departure automatic handoff branch changed or is ambiguous.");
        for (var i = 0; i < code.Count; i++) { yield return code[i]; if (i == sites[0]) { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, callback); } }
    }
    internal static IEnumerable<CodeInstruction> InjectDeferred(IEnumerable<CodeInstruction> instructions, FieldInfo owner, FieldInfo altitude, MethodInfo callback)
    {
        var code = instructions.ToList();
        // Only the scheduled handoff clears _departureautoalt immediately after assigning departure.
        // Automatic and penalty ownership changes elsewhere must not count as controller contact.
        var sites = Enumerable.Range(2, Math.Max(0, code.Count - 5)).Where(i =>
            code[i].opcode == OpCodes.Stfld && Equals(code[i].operand, owner) &&
            code[i + 1].opcode == OpCodes.Ldarg_0 && code[i + 2].opcode == OpCodes.Ldc_I4_0 &&
            code[i + 3].opcode == OpCodes.Stfld && Equals(code[i + 3].operand, altitude) &&
            code[i - 2].opcode == OpCodes.Ldstr && Equals(code[i - 2].operand, "departure")).ToArray();
        if (owner == null || altitude == null || callback == null || sites.Length != 1)
            throw new NotSupportedException("Scheduled altitude departure assignment changed or is ambiguous.");
        for (var i = 0; i < code.Count; i++)
        {
            yield return code[i];
            if (i == sites[0])
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, callback);
            }
        }
    }
    internal static IEnumerable<CodeInstruction> InjectRadio(IEnumerable<CodeInstruction> instructions, MethodInfo historyMethod, MethodInfo callback)
    {
        var code = instructions.ToList();
        Func<CodeInstruction, bool> history = i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && Equals(i.operand, historyMethod);
        if (historyMethod == null || callback == null || code.Count(history) != 1) throw new NotSupportedException("Radio history posting site changed.");
        foreach (var instruction in code)
        {
            yield return instruction;
            if (history(instruction))
            {
                yield return new CodeInstruction(OpCodes.Ldarg, (short)4);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, callback);
            }
        }
    }
    internal static IEnumerable<CodeInstruction> Inject(IEnumerable<CodeInstruction> instructions, FieldInfo ownerField, MethodInfo callback)
    {
        var code = instructions.ToList();
        if (ownerField == null || callback == null || code.Count(i => i.opcode == OpCodes.Stfld && Equals(i.operand, ownerField)) != 1)
            throw new NotSupportedException("RunContact accepted-owner assignment is not unique.");
        foreach (var instruction in code)
        {
            yield return instruction;
            if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, ownerField))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, callback);
            }
        }
    }
}
