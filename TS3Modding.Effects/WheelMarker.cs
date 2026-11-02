using System.Text.RegularExpressions;
namespace TS3Modding.Effects
{
    internal static class WheelMarker
    {
        internal static string Size(string name)
        {
            var match=Regex.Match(name??"",@"^wheel_(small|medium|large)(?: \([0-9]+\))?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
            return match.Success?match.Groups[1].Value.ToLowerInvariant():null;
        }
    }
}
