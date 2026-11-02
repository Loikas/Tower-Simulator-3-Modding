using System;
using System.Text.RegularExpressions;
namespace TS3Modding.RejectedTakeoffs
{
    internal static class StandReturnPolicy
    {
        private static string Identifier(string value)=>Regex.Replace(value??"",@"^(gate|stand|parking)[ _-]*","",RegexOptions.IgnoreCase).Replace("_","").Replace(" ","").ToUpperInvariant();
        internal static bool Matches(string target,string stand)
        {
            if(string.IsNullOrWhiteSpace(target)||string.IsNullOrWhiteSpace(stand))return false;
            var generic=target.Trim().ToUpperInvariant();
            return generic=="TERMINAL"||generic=="RAMP"||generic=="APRON"||Identifier(target)==Identifier(stand);
        }
    }
}
