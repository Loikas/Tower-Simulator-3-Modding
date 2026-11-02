using System.Text.RegularExpressions;
namespace TS3Modding.Tugs
{
    internal static class TowRadioText
    {
        internal static string ForPilot(string message)=>(message??"").Length>230?"Unable, tow instruction could not be completed":Regex.IsMatch(message??"",@"TS3 planner:|cnt:\s*\d|route calculation error|\r|\n|\bSystem\.[A-Z]",RegexOptions.IgnoreCase)?"Unable, tow route unavailable, holding position":message;
        internal static string Display(string speech)
        {
            var result=Regex.Replace(speech??"",@"\b(stand|taxiway) ([a-z0-9 -]+?)(?=,|[.!?]?$)",m=>{
                var id=TowVoiceIntent.Identifier(m.Groups[2].Value.Trim());
                return id==null?m.Value:m.Groups[1].Value+" "+id.ToUpperInvariant();
            },RegexOptions.IgnoreCase);
            return Regex.Replace(result,@"\bvia taxiways? ([a-z0-9 ,\-]+?)(?=[.!?]?$)",m=>{
                // Pilot replies delimit taxiways with commas; words within a group form one identifier.
                var value=m.Groups[1].Value;
                var ids=value.Contains(",")?System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(value.Split(','),part=>TowVoiceIntent.Identifier(part.Trim()))):TowVoiceIntent.Taxiways(value);
                return ids==null||System.Array.Exists(ids,id=>id==null)?m.Value:"via taxiways "+string.Join(" ",ids).ToUpperInvariant();
            },RegexOptions.IgnoreCase);
        }
    }
}
