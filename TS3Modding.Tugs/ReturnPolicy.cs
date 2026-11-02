using System;

namespace TS3Modding.Tugs
{
    public sealed class ReturnFacts
    {
        public bool Enabled, SinglePlayer, HasAircraft, Deleted, HasOriginalStand, GateMatches, TugAvailable,
            StandOccupied, StandClaimedByOther, OtherAircraftClaimsStand, HasNoseWheel, RouterBusy,
            ReadyToTaxi, PushInProgress, AtStand, HasExplicitHold, RouteMatchesOrigin, Outgoing, VerifiedPushbackEnd;
        public int State, RouteCount, RoutePosition;
        public float Speed, Altitude, StandDistance, TugDistance, FirstReturnPointDistance, ReturnPathLength;
    }
    public static class ReturnPolicy
    {
        internal static bool InReturnPhase(ReturnFacts f)=>f.Outgoing&&(f.State==12||f.State==15)&&!f.AtStand&&f.ReadyToTaxi&&!f.PushInProgress;
        public static string Refusal(ReturnFacts f)
        {
            if (!f.Enabled) return "Return-to-stand is disabled in mod options.";
            if (!f.SinglePlayer) return "Return-to-stand currently supports single player only.";
            if (!f.HasAircraft || f.Deleted) return "No available aircraft associated with this tug's stand.";
            if (!InReturnPhase(f))
                return "Available after pushback is complete, before taxiing away.";
            if (!f.HasOriginalStand || !f.GateMatches || !f.HasNoseWheel) return "Original stand or compatible tug connection unavailable.";
            if (!f.TugAvailable) return "Tug is assigned to another aircraft.";
            if (f.StandOccupied || f.StandClaimedByOther || f.OtherAircraftClaimsStand) return "Original stand is occupied or assigned to another aircraft.";
            if (!Finite(f.Speed) || Math.Abs(f.Speed) > 0.01f || !Finite(f.Altitude) || Math.Abs(f.Altitude) > 0.1f)
                return "Aircraft must be stationary on the ground.";
            if (f.RouterBusy) return "A route calculation is running or pending; return is unavailable until it finishes.";
            if (f.HasExplicitHold) return "A new or unverified crossing/holding target prevents return.";
            if (!f.RouteMatchesOrigin || f.RouteCount < 3 || f.RoutePosition < 3 || f.RoutePosition > f.RouteCount)
                return "Original pushback route is no longer available.";
            if (!Within(f.FirstReturnPointDistance,15)) return "Return path does not meet the aircraft's current position.";
            if(!Finite(f.StandDistance)||!Finite(f.TugDistance)||!Finite(f.ReturnPathLength)||
                f.StandDistance<0||f.TugDistance<0||f.ReturnPathLength<0) return "Return geometry is invalid.";
            if(!f.VerifiedPushbackEnd)
            {
                if(!Within(f.StandDistance,100)) return "Aircraft is beyond the fallback stand limit; complete a new pushback to record its return path.";
                if(!Within(f.TugDistance,100)) return "Tug is beyond the fallback recall limit; complete a new pushback to record its return path.";
                if(!Within(f.ReturnPathLength,150)) return "Route exceeds the fallback length limit; complete a new pushback to record its return path.";
            }
            return null;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Within(float value, float limit) => Finite(value) && value >= 0 && value <= limit;
    }
}
