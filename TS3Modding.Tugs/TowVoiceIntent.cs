using System;
using System.Linq;
using System.Text.RegularExpressions;
namespace TS3Modding.Tugs
{
    internal sealed class TowVoiceIntent
    {
        public string Action,Destination,HoldShort,CrossRunway;public string[] Via=new string[0];
        internal static bool IsTow(string text)=>Regex.IsMatch(text??"",@"\btow(?:ing)?\b",RegexOptions.IgnoreCase);
        // The live recognizer emitted "tau two stand" and "two two stand" for the user's tow-to-stand request.
        // Only normalize these homophones with an explicit destination noun; ordinary taxi/go stays native.
        internal static string NormalizeSpeech(string text)=>Regex.Replace(Regex.Replace(Regex.Replace(text??"",@"\b(hold|continue|resume|cancel|recover)\s+(?:tau|toe|tol|toll|low|two)\b(?=\s*[,.!?]?$)","$1 tow",RegexOptions.IgnoreCase),@"\b(?:tow|tau|toe|tol|toll|told|toad|towing|two)\s+(?:to|two)\s+(stands?|gate|parking)\b",m=>"tow to "+(m.Groups[1].Value.Equals("stands",StringComparison.OrdinalIgnoreCase)?"stand":m.Groups[1].Value),RegexOptions.IgnoreCase),@"\b(?:tow|towing)\s+(stands?|gate|parking)\b","tow to $1",RegexOptions.IgnoreCase);
        // Recovery without a recognized tow verb is permitted only after matching an
        // owned tow's full callsign. A native 'taxi to stand' retains 'taxi' in the
        // address and fails that exact match, so it cannot become a tow instruction.
        internal static string RecoverOwnedSpeech(string text)=>Regex.Replace(text??"",@"\b(?:to|two|towards)\s+(stand|gate|parking)\b","tow to $1",RegexOptions.IgnoreCase);
        // Recovery of misheard speech/decoded instructions is gated by exact tow ownership/address.
        // Never reinterpret taxi-to-runway, bare taxi, or mixed taxi clearances.
        internal static string RecoverTaxiSpeech(string text)=>Regex.Replace(
            Regex.Replace(text??"",@"\btaxi(?:ing)?\s+(?:to|two)\s+(stands?|gate|parking)\b",m=>"tow to "+(m.Groups[1].Value.Equals("stands",StringComparison.OrdinalIgnoreCase)?"stand":m.Groups[1].Value),RegexOptions.IgnoreCase),
            @"\b(hold|continue|resume|cancel)\s+taxi(?:ing)?\b(?=\s*[,.!?]?$)","$1 tow",RegexOptions.IgnoreCase);
        internal static TowVoiceIntent Parse(string text)
        {
            if(Regex.IsMatch(text??"",@"^\s*(?:recover|recheck) tow\s*$",RegexOptions.IgnoreCase))return new TowVoiceIntent{Action="recover"};
            var words=Regex.Replace((text??"").Trim().ToLowerInvariant(),@"[,\s]+"," ");
            string cross=null;
            if(IsTow(words)&&Regex.IsMatch(words,@"\bcross(?:ing)? runway\b"))
            {
                var clause=Regex.Match(words,@" (?:and )?(?:cleared to )?cross(?:ing)? runway ([a-z0-9 /-]+)$");
                if(clause.Success && Regex.IsMatch(words.Substring(0,clause.Index),@"^(?:start )?tow(?:ing)? (?:to |via )"))
                {cross=TowRunwayCrossings.Designator(clause.Groups[1].Value);if(cross==null)return null;words=words.Substring(0,clause.Index);}
            }
            if(words=="tow hold"||words=="hold tow")return new TowVoiceIntent{Action="hold"};
            if(words=="tow continue"||words=="continue tow"||words=="resume tow")return new TowVoiceIntent{Action="continue"};
            if(words=="tow back to stand"||words=="tow back to gate"||words=="tow back to original stand")return new TowVoiceIntent{Action="return"};
            if(words=="tow cancel"||words=="cancel tow")return new TowVoiceIntent{Action="cancel"};
            if(words=="cancel runway crossing"||words=="tow cancel runway crossing")return new TowVoiceIntent{Action="revokecross"};
            var crossing=Regex.Match(words,@"^(?:tow )?(?:(?:cleared to )?cross|hold short of) runway ([a-z0-9 /-]+)$");
            if(crossing.Success){var runway=TowRunwayCrossings.Designator(crossing.Groups[1].Value);return runway==null?null:new TowVoiceIntent{Action=words.Contains("hold short")?"runwayhold":"cross",Destination=runway};}
            var hold=Regex.Match(words,@"^(?:tow )?hold short of taxiway ([a-z0-9 -]+)$");
            if(hold.Success){var point=Identifier(hold.Groups[1].Value);return point==null?null:new TowVoiceIntent{Action="holdshort",HoldShort=point};}
            var viaOnly=Regex.Match(words,@"^tow(?:ing)? via (?:taxiways? )?([a-z0-9 -]+?)(?: hold short of taxiway ([a-z0-9 -]+))?$");
            if(viaOnly.Success)
            {
                var route=Taxiways(viaOnly.Groups[1].Value);var stopAt=viaOnly.Groups[2].Success?Identifier(viaOnly.Groups[2].Value):null;
                return route==null||viaOnly.Groups[2].Success&&stopAt==null?null:new TowVoiceIntent{Action="reroute",Via=route,HoldShort=stopAt,CrossRunway=cross};
            }
            var match=Regex.Match(words,@"^(?:start )?tow(?:ing)? to (?:stand|gate|parking) ([a-z0-9 -]+?)(?: via (?:taxiways? )?([a-z0-9 -]+?))?(?: hold short of taxiway ([a-z0-9 -]+))?$");
            if(!match.Success)return null;
            var id=Identifier(match.Groups[1].Value);if(id==null)return null;
            var via=match.Groups[2].Success?Taxiways(match.Groups[2].Value):new string[0];
            var stop=match.Groups[3].Success?Identifier(match.Groups[3].Value):null;
            if(via==null||match.Groups[3].Success&&stop==null)return null;
            return new TowVoiceIntent{Action="transfer",Destination=id,Via=via,HoldShort=stop,CrossRunway=cross};
        }
        internal static bool IsScopedInstruction(string text)=>Regex.IsMatch(text??"",@"\bhold short of (?:taxiway|runway)\b|\b(?:cleared to )?cross runway\b|\bcancel runway crossing\b",RegexOptions.IgnoreCase);
        internal string CommandText()=>Action=="cross"?"TOW CROSS RUNWAY "+Destination:Action=="runwayhold"?"TOW HOLD SHORT OF RUNWAY "+Destination:Action=="revokecross"?"TOW CANCEL RUNWAY CROSSING":Action=="holdshort"?"TOW HOLD SHORT OF TAXIWAY "+HoldShort.ToUpperInvariant():(Action=="reroute"?"TOW VIA TAXIWAY "+string.Join(" ",Via).ToUpperInvariant():"TOW TO STAND "+Destination.ToUpperInvariant()+(Via.Length==0?"":" VIA TAXIWAY "+string.Join(" ",Via).ToUpperInvariant()))+(HoldShort==null?"":" HOLD SHORT OF TAXIWAY "+HoldShort.ToUpperInvariant())+(CrossRunway==null?"":" CROSS RUNWAY "+CrossRunway);
        internal static string[] Taxiways(string text)
        {
            var result=new System.Collections.Generic.List<string>();
            foreach(var token in text.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries))
            {
                var id=Identifier(token);if(id==null)return null;
                if(id.Length==1&&char.IsLetter(id[0])&&result.Count>0&&result[result.Count-1]==id){result[result.Count-1]+=id;continue;}
                if(id.All(char.IsDigit)&&result.Count>0)result[result.Count-1]+=id;else result.Add(id);
            }
            return result.Count==0||result.Count>8||result.Distinct().Count()!=result.Count?null:result.ToArray();
        }
        internal static string Identifier(string value)
        {
            var letters="alfa bravo charlie delta echo foxtrot golf hotel india juliett kilo lima mike november oscar papa quebec romeo sierra tango uniform victor whiskey xray yankee zulu".Split(' ');
            var digits="zero one two three four five six seven eight niner".Split(' ');
            var input=Regex.Replace((value??"").ToLowerInvariant(),@"^(?:gate_|stand_|parking_|taxiway_)","").Replace("alpha","alfa").Replace("juliet ","juliett ").Replace("x-ray","xray");
            var result="";
            foreach(var token in input.Split(new[]{' ','-','_'},StringSplitOptions.RemoveEmptyEntries))
            {
                var letter=Array.IndexOf(letters,token);var digit=Array.IndexOf(digits,token);
                if(letter>=0)result+=(char)('a'+letter);
                else if(digit>=0)result+=(char)('0'+digit);
                else if(token=="nine")result+="9";
                else if(token=="tree")result+="3";
                else if(token=="fife")result+="5";
                else if(token=="oh")result+="0";
                else if(token=="juliet")result+="j";
                else if(Regex.IsMatch(token,@"^[a-z]{0,4}\d+[a-z]?$|^[a-z]{1,2}$"))result+=token;
                else return null;
            }
            return result.Length==0?null:result;
        }
        internal static string Spoken(string identifier)
        {
            var letters="Alfa Bravo Charlie Delta Echo Foxtrot Golf Hotel India Juliett Kilo Lima Mike November Oscar Papa Quebec Romeo Sierra Tango Uniform Victor Whiskey X-ray Yankee Zulu".Split(' ');
            var digits="zero one two three four five six seven eight niner".Split(' ');
            return string.Join(" ",(Identifier(identifier)??identifier.ToLowerInvariant()).Select(c=>c>='a'&&c<='z'?letters[c-'a']:c>='0'&&c<='9'?digits[c-'0']:c.ToString()));
        }
    }
}
