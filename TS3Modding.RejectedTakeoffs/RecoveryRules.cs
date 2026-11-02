using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
namespace TS3Modding.RejectedTakeoffs
{
    internal static class RecoveryRules
    {
        internal static int ExitApproach(int source,int exit)=>Math.Max(source,exit-2);
        internal static float ExitHandoffClearance(float runwayWidth)=>Math.Max(0,runwayWidth)/2+5;
        internal static bool BlockLineup(int command,bool occupied)=>occupied&&(command==27||command==28);
        internal static int ExitRank(bool hasVia,bool matchesFirstVia)=>hasVia&&matchesFirstVia?0:1;
        internal static float InitialDistance(float native,bool recovery)=>recovery?float.MaxValue:native;
        internal static IEnumerable<CodeInstruction> InjectInitialDistance(IEnumerable<CodeInstruction> source,MethodInfo callback)
        {
            var code=source.ToList();var sites=Enumerable.Range(1,Math.Max(0,code.Count-2)).Where(i=>code[i].opcode==OpCodes.Ldc_R4&&Equals(code[i].operand,21f)&&code[i-1].opcode==OpCodes.Call&&(code[i+1].opcode==OpCodes.Bge_Un_S||code[i+1].opcode==OpCodes.Bge_Un)).ToArray();
            if(callback==null||sites.Length!=1)throw new NotSupportedException("Native initial taxi progress scan changed.");
            for(var i=0;i<code.Count;i++)
            {
                if(i==sites[0]){var load=new CodeInstruction(OpCodes.Ldarg_0);load.labels.AddRange(code[i].labels);code[i].labels.Clear();yield return load;yield return new CodeInstruction(OpCodes.Call,callback);}
                yield return code[i];
            }
        }
        internal static bool StartConnection(double ahead,double offset,double gap,double sourceSpacing)=>ahead>=-1&&offset>=0&&offset<=15&&gap>=0&&gap<=Math.Max(30,sourceSpacing);
        internal static bool ForwardExit(double ahead,double offset,double clearance)=>ahead>=10&&!double.IsNaN(offset)&&!double.IsInfinity(offset)&&offset>=clearance;
        internal static int JoinIndex(IReadOnlyList<double> squaredDistances)
        {
            var best=225d;var index=-1;
            for(var i=0;i<Math.Min(6,squaredDistances.Count);i++)if(squaredDistances[i]>=0&&squaredDistances[i]<=best){best=squaredDistances[i];index=i;}
            return index;
        }
        internal static bool Replan(bool native,bool owned,int state,float speed,int command,bool combinedTakeoff)
            =>native||(owned&&state==14&&Math.Abs(speed)<=.1&&(command==1||command==2)&&!combinedTakeoff);
        internal static bool Vacated(double distance,double runwayWidthMeters,double length,double wingspan,float altitude)
            =>altitude>100 || (!double.IsNaN(distance)&&!double.IsInfinity(distance)&&distance>Math.Max(0,runwayWidthMeters)/2+Math.Sqrt(length*length+wingspan*wingspan)/2+5);
        internal static string RunwayKey(string name)
        {
            var words=(name??"").ToUpperInvariant().Replace("RUNWAY_","").Replace("RUNWAY ","").Split('/');
            var keys=new List<string>();
            foreach(var word in words)
            {
                var m=Regex.Match(word.Trim(),@"^0?([1-9]|[12][0-9]|3[0-6])([LRC]?)$");if(!m.Success)return null;
                var n=int.Parse(m.Groups[1].Value);var side=m.Groups[2].Value;
                var reciprocal=(n+17)%36+1;var other=side=="L"?"R":side=="R"?"L":side;
                keys.Add(string.CompareOrdinal(n.ToString("00")+side,reciprocal.ToString("00")+other)<0?n.ToString("00")+side:reciprocal.ToString("00")+other);
            }
            return keys.Distinct().Count()==1?keys[0]:null;
        }
        internal static IEnumerable<CodeInstruction> InjectReplan(IEnumerable<CodeInstruction> source,FieldInfo negative,MethodInfo callback)
        {
            var code=source.ToList();var sites=Enumerable.Range(0,Math.Max(0,code.Count-4)).Where(i=>code[i].opcode==OpCodes.Ldloc_1&&(code[i+1].opcode==OpCodes.Brfalse||code[i+1].opcode==OpCodes.Brfalse_S)&&code[i+2].opcode==OpCodes.Ldarg_0&&code[i+3].opcode==OpCodes.Ldc_I4_1&&code[i+4].opcode==OpCodes.Stfld&&Equals(code[i+4].operand,negative)).ToArray();
            if(negative==null||callback==null||sites.Length!=1)throw new NotSupportedException("Native runway taxi recalculation branch changed.");
            for(var i=0;i<code.Count;i++)
            {
                yield return code[i];
                if(i==sites[0]){yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Ldarg_1);yield return new CodeInstruction(OpCodes.Ldarg_2);yield return new CodeInstruction(OpCodes.Call,callback);}
            }
        }
    }
}
