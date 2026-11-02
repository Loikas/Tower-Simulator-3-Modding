using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace TS3Modding.Tugs
{
    // Shared by both fast/strict bridge modes. Native grammar uses | clauses and numbered # keys.
    internal static class TowStrictGrammar
    {
        internal static string[] Clauses()
        {
            var result=new List<string>{"tow hold","hold tow","tow continue","continue tow","resume tow","tow cancel","cancel tow","recover tow","recheck tow","tow back to stand","tow back to gate","tow back to original stand","cancel runway crossing","tow cancel runway crossing"};
            // Native grammar already composes VIA, HOLD SHORT and CROSS clauses. Do not
            // enumerate their Cartesian product: it biases strict decoding toward tow fragments.
            foreach(var instruction in new[]{"cross runway #runway1","cleared to cross runway #runway1","hold short of runway #runway1","hold short of taxiway #taxiway1"})result.Add("tow "+instruction);
            foreach(var noun in new[]{"stand","gate","parking"})result.Add("tow to "+noun+" #target1");
            for(var n=1;n<=8;n++)foreach(var via in new[]{"tow via ","tow via taxiway "})
                result.Add(via+string.Join(" ",Enumerable.Range(1,n).Select(i=>"#taxiway"+i)));
            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(c=>c.Length).ToArray();
        }
        internal static string ExtendCommands(string original)
        {
            var existing=new HashSet<string>((original??"").Split('|').Select(s=>s.Trim()),StringComparer.OrdinalIgnoreCase);
            var extra=Clauses().Where(c=>!existing.Contains(c)).ToArray();
            if(extra.Length==0)return original;
            return (original??"")+((original??"").EndsWith("|")?"":"|")+string.Join("|",extra)+"|";
        }
        internal static string ExtendTargets(string original,KeyValuePair<string,string>[] aliases)
        {
            var existing=new HashSet<string>((original??"").Split('|'),StringComparer.OrdinalIgnoreCase);
            var extra=aliases.Select(p=>p.Key.ToUpperInvariant()+";"+TowVoiceIntent.Spoken(p.Value)).Where(p=>!existing.Contains(p)).ToArray();
            if(extra.Length==0)return original;
            return (original??"")+((original??"").EndsWith("|")?"":"|")+string.Join("|",extra);
        }
        internal static string NormalizeTargets(string command,KeyValuePair<string,string>[] aliases)
        {
            if(!TowVoiceIntent.IsTow(command))return command;
            foreach(var pair in aliases.OrderByDescending(p=>p.Key.Length))
                command=Regex.Replace(command,@"(?<=\bstand )"+Regex.Escape(pair.Key)+@"(?=\s|$)|(?<=\bgate )"+Regex.Escape(pair.Key)+@"(?=\s|$)|(?<=\bparking )"+Regex.Escape(pair.Key)+@"(?=\s|$)",pair.Value,RegexOptions.IgnoreCase);
            return command;
        }
    }
}
