using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
namespace TS3Modding.RejectedTakeoffs
{
    internal static class MaintenanceSaveBoundary
    {
        internal static CodeInstruction[] Rewrite(IEnumerable<CodeInstruction> code,MethodInfo replacement,out Action<string,string> writer)
        {
            var instructions=code.ToArray();var matches=instructions.Where(i=>i.operand is MethodInfo method&&((method.DeclaringType==typeof(File)&&method.Name=="WriteAllText")||(method.DeclaringType.FullName=="TS3Modding.Tugs.NativeTowTransfer"&&method.Name=="WriteSave"))&&method.GetParameters().Length==2).ToArray();
            if(matches.Length!=1)throw new NotSupportedException("Maintenance save IO boundary changed.");
            writer=(Action<string,string>)Delegate.CreateDelegate(typeof(Action<string,string>),(MethodInfo)matches[0].operand);
            matches[0].opcode=OpCodes.Call;matches[0].operand=replacement;return instructions;
        }
    }
}
