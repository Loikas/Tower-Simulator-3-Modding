using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace TS3Modding.Tugs
{
    internal static class TowTaxiwaySpeech
    {
        internal static string Normalize(string instruction,string[] airportNames)
        {
            if(!TowVoiceIntent.IsTow(instruction))return instruction;
            // Only route/hold clauses: never rewrite a callsign or destination stand.
            return Regex.Replace(instruction??"",@"\bvia\s+(?:taxiways?\s+)?[^;.!?]+|\bhold short of taxiway\s+[^;.!?]+",clause=>{
                var text=clause.Value;
                foreach(var id in (airportNames??new string[0]).Select(TowVoiceIntent.Identifier).Where(n=>n!=null&&n.Length>=2).Distinct().OrderByDescending(n=>n.Length))
                {
                    var spoken=TowVoiceIntent.Spoken(id);
                    var letters=string.Join(@"\s+",id.Select(c=>Regex.Escape(c.ToString())));
                    // Commas are explicit taxiway boundaries, unlike whitespace from ASR.
                    text=Regex.Replace(text,@"\b(?:"+string.Join(@"\s+",spoken.Split(' ').Select(Regex.Escape))+"|"+letters+@")\b",id.ToUpperInvariant(),RegexOptions.IgnoreCase);
                }
                return text;
            },RegexOptions.IgnoreCase);
        }
    }
}
