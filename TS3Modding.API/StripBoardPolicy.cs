using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.API
{
    public static class StripBoardPolicy
    {
        public static bool UniqueWithinBoard(StripSnapshot strip,IReadOnlyList<StripSnapshot> strips) =>
            strips.Count(s=>s.Scene==strip.Scene && s.NetworkIndex==strip.NetworkIndex &&
                string.Equals(s.Callsign,strip.Callsign,StringComparison.OrdinalIgnoreCase) &&
                (s.BoardInstanceId==strip.BoardInstanceId || s.BoardInstanceId==0 || strip.BoardInstanceId==0))==1;
        public static string OperationKey(AircraftId aircraft,StripSnapshot strip,string phase) => aircraft+":"+strip.BoardInstanceId+":"+phase;
    }
}
