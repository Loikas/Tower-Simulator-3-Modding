using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.PilotReadiness
{
    internal static class RunwayNames
    {
        public static string Label(IEnumerable<string> names)
        {
            var values=names.Where(n=>!string.IsNullOrWhiteSpace(n)).Select(n=>n.Trim().ToUpperInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(values.Length==1) return values[0];
            if(values.Length!=2) return null;
            var parsed=values.Select(n=>System.Text.RegularExpressions.Regex.Match(n,@"^(0?[1-9]|[12][0-9]|3[0-6])([LRC]?)$")).ToArray();
            if(parsed.Any(m=>!m.Success)) return null;
            var a=int.Parse(parsed[0].Groups[1].Value); var b=int.Parse(parsed[1].Groups[1].Value);
            var sa=parsed[0].Groups[2].Value; var sb=parsed[1].Groups[2].Value;
            if(Math.Abs(a-b)!=18||!((sa==""&&sb=="")||(sa=="C"&&sb=="C")||(sa=="L"&&sb=="R")||(sa=="R"&&sb=="L"))) return null;
            return a<b?a.ToString("D2")+sa+"/"+b.ToString("D2")+sb:b.ToString("D2")+sb+"/"+a.ToString("D2")+sa;
        }
    }
}

