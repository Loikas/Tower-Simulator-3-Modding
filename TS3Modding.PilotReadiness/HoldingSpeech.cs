using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TS3Modding.PilotReadiness
{
    internal static class HoldingSpeech
    {
        private static readonly string[] Digits={"zero","one","two","three","four","five","six","seven","eight","niner"};
        private static readonly string[] Letters={"Alfa","Bravo","Charlie","Delta","Echo","Foxtrot","Golf","Hotel","India","Juliett","Kilo","Lima","Mike","November","Oscar","Papa","Quebec","Romeo","Sierra","Tango","Uniform","Victor","Whiskey","X-ray","Yankee","Zulu"};
        private static string Identifier(string value) => string.Join(" ",value.ToUpperInvariant().Select(c=>char.IsDigit(c)?Digits[c-'0']:c>='A'&&c<='Z'?Letters[c-'A']:c=='/'?",":c.ToString()));
        public static string WithCallsign(string message,string callsign)=>Format(message).TrimEnd().TrimEnd('.',',','!','?')+", "+callsign;
        public static string Format(string message)
        {
            var spoken=Regex.Replace(message,@"\brunway\s+(\d{1,2}[LRC]?(?:/\d{1,2}[LRC]?)?)(?![A-Za-z0-9])",m=>"runway "+string.Join(", ",m.Groups[1].Value.ToUpperInvariant().Split('/').Select(r=>
            {
                var suffix=r.Last();
                var number=char.IsLetter(suffix)?r.Substring(0,r.Length-1):r;
                return Identifier(number)+(char.IsLetter(suffix)?suffix=='L'?" left":suffix=='R'?" right":" center":"");
            })),RegexOptions.IgnoreCase);
            return Regex.Replace(spoken,@"\b(taxiway|at)\s+([A-Z]{1,3}\d*|\d+[A-Z]?)(?![A-Za-z0-9])",m=>m.Groups[1].Value+" "+Identifier(m.Groups[2].Value),RegexOptions.IgnoreCase);
        }
    }
}

