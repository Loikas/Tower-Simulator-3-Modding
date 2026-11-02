using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TS3Modding.Tugs
{
    // Airport-local identifiers only. Native names/references are never renamed.
    internal sealed class TowStandAliases
    {
        private readonly Dictionary<string,string> aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        internal TowStandAliases(IEnumerable<string> names)
        {
            var ordered=names.Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal)
                .OrderBy(n=>TowVoiceIntent.Identifier(n)==null?1:0).ThenBy(n=>n,StringComparer.Ordinal).ToArray();
            var canonical=new HashSet<string>(ordered.Select(Preferred),StringComparer.OrdinalIgnoreCase);
            var reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var name in ordered)
            {
                var candidate=Preferred(name);var unique=candidate;var suffix=1;
                while(reserved.Contains(unique)||(unique!=candidate&&canonical.Contains(unique)))
                {
                    unique=char.IsDigit(candidate[candidate.Length-1])&&suffix<=26?candidate+(char)('A'+suffix-1):Regex.Match(candidate,@"^[A-Z]*").Value+string.Concat(Regex.Matches(candidate,@"\d+").Cast<Match>().Select(m=>m.Value))+"0"+suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    suffix++;
                }
                reserved.Add(unique);
                aliases.Add(name,unique);
            }
        }
        internal string Alias(string nativeName)
        {
            string alias;if(nativeName!=null&&aliases.TryGetValue(nativeName,out alias))return alias;
            throw new InvalidOperationException("Stand no longer belongs to the airport alias map. Refresh destinations.");
        }
        internal static TowStandAliases For(TowPlanningSession session,string target=null)=>new TowStandAliases(session.Roads.Where(r=>r.Type==1).Select(r=>r.Name).Concat((session.Destinations??new TowDestination[0]).Select(d=>d.Name)).Concat(new[]{target}));
        internal static string Preferred(string name)
        {
            var simple=TowVoiceIntent.Identifier(name);if(simple!=null)return simple.ToUpperInvariant();
            var body=Regex.Replace(name??"",@"^(?:gate|stand|parking)[_\s-]+","",RegexOptions.IgnoreCase);
            body=Regex.Replace(body,@"([a-z])([A-Z])","$1 $2");
            var ascii=new string(body.Normalize(NormalizationForm.FormD).Where(c=>c<128).ToArray()).ToLowerInvariant();
            var digits=string.Concat(Regex.Matches(ascii,@"\d+").Cast<Match>().Select(m=>m.Value));
            var words=Regex.Matches(ascii,@"[a-z]+").Cast<Match>().Select(m=>m.Value).ToArray();
            string prefix;
            if(words.Length>0&&words[0]=="cargo")prefix="CG";
            else if(words.Length>0&&(words[0]=="remote"||words[0]=="apron"))prefix=words[0]=="remote"?"RP":"AP";
            else if(words.Length>0&&words[0]=="hangar")prefix="HG";
            else if(words.Length>1)prefix=string.Concat(words.Take(3).Select(w=>w[0])).ToUpperInvariant();
            else if(words.Length==1)prefix=(words[0].Substring(0,1)+new string(words[0].Skip(1).Where(c=>"aeiou".IndexOf(c)<0).Take(1).ToArray())).ToUpperInvariant();
            else prefix="S";
            return prefix+(digits.Length==0?"1":digits);
        }
    }
}
