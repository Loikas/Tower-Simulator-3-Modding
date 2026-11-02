using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace TS3Modding.Tugs
{
    [Serializable] internal sealed class TowSpeechLexicon{public TowSpeechAircraft[] airplane=null;}
    [Serializable] internal sealed class TowSpeechAircraft{public string id=null;public string[] pronunciations=null;}
    internal static class TowSpokenAddress
    {
        private static string Normalize(string value)
        {
            var words=Regex.Replace((value??"").ToLowerInvariant(),@"\b(zero|oh|one|two|three|tree|four|five|fife|six|seven|eight|nine|niner|nyner)\b",m=>{
                switch(m.Value){case "zero":case "oh":return "0";case "one":return "1";case "two":return "2";case "three":case "tree":return "3";case "four":return "4";case "five":case "fife":return "5";case "six":return "6";case "seven":return "7";case "eight":return "8";default:return "9";}
            });
            return Regex.Replace(words,"[^a-z0-9]","");
        }
        internal static string Command(string transcript,IEnumerable<TowSpeechAircraft> aircraft)
        {
            var text=Regex.Replace(TowVoiceIntent.NormalizeSpeech(transcript).Trim(),@"^(?:(?:hello|hi|hey|dear|please)\s+)+","",RegexOptions.IgnoreCase).TrimEnd('.',',','!','?');
            var tow=Regex.Match(text,@"\b(?:(?:start|hold|continue|resume|cancel|recover|recheck)\s+)?tow(?:ing)?\b|\bhold short of (?:taxiway|runway)\b|\b(?:cleared to )?cross runway\b|\bcancel runway crossing\b",RegexOptions.IgnoreCase);
            if(!tow.Success)throw new InvalidOperationException("No explicit tow instruction was recognized.");
            var address=Normalize(text.Substring(0,tow.Index));var instruction=text.Substring(tow.Index);
            // Observed ASR confusion: stock Rainier flights become "RYANAIR".
            // Only alias the airline word at the address prefix, preserving every digit.
            // Keep exact and recovered matches together: a real Ryanair/Rainier collision
            // must remain ambiguous rather than selecting one by list order or frequency.
            var addresses=new[]{address,Regex.Replace(address,@"^ryanair(?=[0-9])","rainier")}.Distinct().ToArray();
            var matches=aircraft.Where(a=>a!=null&&!string.IsNullOrEmpty(a.id)&&(addresses.Contains(Normalize(a.id))||(a.pronunciations??new string[0]).Any(p=>addresses.Contains(Normalize(p))))).Select(a=>a.id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(address.Length==0||matches.Length!=1)throw new InvalidOperationException("Tow callsign is missing, unknown or ambiguous. Say the full aircraft callsign.");
            // Keep the whole instruction, including invalid/mixed clauses: the addressed tow handler
            // refuses it atomically rather than submitting a partial instruction to the native parser.
            var spokenTow=TowVoiceIntent.IsTow(instruction);
            var intent=TowVoiceIntent.Parse(instruction);
            if(intent?.Action=="transfer"||intent?.Action=="reroute"||intent?.Action=="holdshort"||intent?.Action=="cross"||intent?.Action=="runwayhold"||intent?.Action=="revokecross")instruction=intent.CommandText();
            // HandleCommand passes addressed text straight to Airplane.Command; scoped
            // hold/cross hooks already identify owned tows without adding a visible word.
            if(!spokenTow&&intent!=null&&instruction.StartsWith("TOW ",StringComparison.Ordinal))instruction=instruction.Substring(4);
            return matches[0].ToUpperInvariant()+" "+instruction.ToUpperInvariant();
        }
    }
}
