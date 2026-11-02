using System;
namespace TS3Modding.PilotReadiness
{
    internal static class HoldDashPolicy
    {
        // Native taxi movement consumes a previous hold by its source-road name.
        internal static bool PendingCross(bool positionHeld,string previousRoad,string candidateRoad)=>positionHeld||string.IsNullOrEmpty(previousRoad)||string.IsNullOrEmpty(candidateRoad)||!string.Equals(previousRoad,candidateRoad,StringComparison.Ordinal);
    }
}
