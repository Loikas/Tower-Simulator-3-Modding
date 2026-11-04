using System;
using System.Collections.Generic;
using System.Linq;
using TS3Modding.API;

namespace TS3Modding.FlightStrips
{
    public enum StripStage { Unknown, Pending, Arrival, Final, ActiveTaxi, Departure }
    public sealed class StripRecommendation
    {
        public StripRecommendation(StripSnapshot strip, AircraftId? aircraft, StripStage stage, string reason)
        { Strip = strip; Aircraft = aircraft; Stage = stage; Reason = reason; }
        public StripSnapshot Strip { get; }
        public AircraftId? Aircraft { get; }
        public StripStage Stage { get; }
        public string Reason { get; }
    }
    // Pure preview policy. Stages are conceptual, not actual airport block names or commands.
    public static class FlightStripPlanner
    {
        public static IReadOnlyList<StripRecommendation> Plan(AirportObservation observation)
        {
            var result = new List<StripRecommendation>();
            foreach (var strip in observation.Strips)
            {
                if (strip.InBin) { result.Add(new StripRecommendation(strip, null, StripStage.Unknown, "Strip is already in bin; leave unchanged")); continue; }
                if (string.IsNullOrWhiteSpace(strip.Callsign)) { result.Add(new StripRecommendation(strip, null, StripStage.Unknown, "Missing strip callsign")); continue; }
                var matches = observation.Aircraft.Where(a => a.Scene == strip.Scene &&
                    string.Equals(a.Details.Callsign, strip.Callsign, StringComparison.OrdinalIgnoreCase) &&
                    a.Details.NetworkIndex == strip.NetworkIndex).ToArray();
                if (matches.Length != 1)
                {
                    result.Add(new StripRecommendation(strip, null, StripStage.Unknown, matches.Length == 0 ? "No exact callsign/network/scene match" : "Ambiguous aircraft match"));
                    continue;
                }
                var aircraft = matches[0];
                var state = aircraft.Details.RawState;
                var stage = StageFor(aircraft);
                var action = aircraft.Details.RawAction;
                var incoming = action == "ACTION_INCOMING";
                var outgoing = action == "ACTION_OUTGOING" || action == "ACTION_TERMINALSTART";
                if ((incoming && strip.Departure) || (outgoing && !strip.Departure)) stage = StripStage.Unknown;
                result.Add(new StripRecommendation(strip, aircraft.Id, stage,
                    stage == StripStage.Unknown ? "Unreviewed state/action, inactive aircraft, deleted flag, or direction mismatch; leave unchanged" : "Preview from " + state + " / " + action));
            }
            return result.AsReadOnly();
        }
        private static StripStage StageFor(AircraftSnapshot aircraft)
        {
            if (aircraft.MarkedDeleted) return StripStage.Unknown;
            var state = aircraft.Details.RawState;
            var action = aircraft.Details.RawAction;
            if (action != "ACTION_INCOMING" && action != "ACTION_OUTGOING" && action != "ACTION_TERMINALSTART") return StripStage.Unknown;
            if (state == "STATE_WAIT_FOR_SPAWN" || state == "STATE_WAIT_FOR_RESPAWN") return StripStage.Pending;
            if (!aircraft.Details.SimulationActive || !aircraft.Active) return StripStage.Unknown;
            if (action == "ACTION_INCOMING")
            {
                if (state == "STATE_INCOMING") return StripStage.Arrival;
                if (state == "STATE_FINALRUNWAY" || state == "STATE_LAND") return StripStage.Final;
                if (state == "STATE_ESCAPE_RUNWAY" || state == "STATE_TO_TERMINAL") return StripStage.ActiveTaxi;
            }
            else
            {
                if (state == "STATE_PULL_BACK" || state == "STATE_PUSHBACK_DIR" || state == "STATE_TO_RUNWAY" || state == "STATE_TO_INTERSECTION") return StripStage.ActiveTaxi;
                if (state == "STATE_TAKEOFFUP" || state == "STATE_FLYAWAY") return StripStage.Departure;
            }
            // Waiting/absence alone never proves parking or completion. No archive recommendation.
            return StripStage.Unknown;
        }
    }
}
