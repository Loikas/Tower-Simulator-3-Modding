using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TS3Modding.API;

namespace TS3Modding.FlightStrips
{
    public static class StripMovementPolicy
    {
        public static string RunwayTarget(StripRecommendation recommendation, AircraftSnapshot aircraft)
        {
            if (recommendation == null || aircraft == null || !recommendation.Aircraft.HasValue ||
                !recommendation.Aircraft.Value.Equals(aircraft.Id) ||
                recommendation.Strip.InBin || aircraft.MarkedDeleted || !aircraft.Active || !aircraft.Details.SimulationActive) return null;
            var strip = recommendation.Strip;
            var state = aircraft.Details.RawState;
            var direction = strip.Departure
                ? (aircraft.Details.RawAction == "ACTION_OUTGOING" || aircraft.Details.RawAction == "ACTION_TERMINALSTART") &&
                    new[] { "STATE_WAITING", "STATE_PUSHBACK_DIR", "STATE_PULL_BACK", "STATE_TO_RUNWAY", "STATE_TO_INTERSECTION" }.Contains(state)
                : aircraft.Details.RawAction == "ACTION_INCOMING" && new[] { "STATE_INCOMING", "STATE_FINALRUNWAY", "STATE_LAND" }.Contains(state);
            if (!direction) return null;
            var runway = NormalizeRunway(aircraft.Details.AssignedRunway);
            var reported = NormalizeRunway(strip.AssignedRunway);
            if ((!string.IsNullOrWhiteSpace(aircraft.Details.AssignedRunway) && runway == null) ||
                (!string.IsNullOrWhiteSpace(strip.AssignedRunway) && reported == null)) return null;
            if (strip.Departure && runway != null && reported != null && runway != reported) return null;
            runway = runway ?? reported;
            if (runway == null) return null;
            if (!strip.Departure && NormalizeRunway(aircraft.Details.FinalReportedRunway) == null) return null;
            var matches = strip.NextRoutes.Where(r => r.Header != strip.Block && !string.Equals(r.Header, "BIN", StringComparison.OrdinalIgnoreCase) &&
                r.Runways.Any(value => NormalizeRunway(value) == runway)).ToArray();
            return matches.Length == 1 ? matches[0].Header : null;
        }
        public static string NormalizeRunway(string value)
        {
            var token = (value ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(token, "^([1-9]|0[1-9]|[12][0-9]|3[0-6])[LCR]?$")) return null;
            return token.Length == 1 || token.Length == 2 && char.IsLetter(token[1]) ? "0" + token : token;
        }
        public static string Target(StripRecommendation recommendation, AircraftSnapshot aircraft, string arrivalGround, string departureAirborne)
        {
            if (recommendation == null || aircraft == null || !recommendation.Aircraft.HasValue ||
                !recommendation.Aircraft.Value.Equals(aircraft.Id) || recommendation.Strip.InBin ||
                aircraft.MarkedDeleted || !aircraft.Active || !aircraft.Details.SimulationActive) return null;
            string target = null;
            if (!recommendation.Strip.Departure && aircraft.Details.RawAction == "ACTION_INCOMING" &&
                recommendation.Stage == StripStage.ActiveTaxi &&
                (aircraft.Details.RawState == "STATE_ESCAPE_RUNWAY" || aircraft.Details.RawState == "STATE_TO_TERMINAL")) target = arrivalGround;
            if (recommendation.Strip.Departure && recommendation.Stage == StripStage.Departure &&
                (aircraft.Details.RawAction == "ACTION_OUTGOING" || aircraft.Details.RawAction == "ACTION_TERMINALSTART") &&
                (aircraft.Details.RawState == "STATE_TAKEOFFUP" || aircraft.Details.RawState == "STATE_FLYAWAY")) target = departureAirborne;
            if (string.IsNullOrWhiteSpace(target) || string.Equals(target, "BIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, recommendation.Strip.Block, StringComparison.Ordinal)) return null;
            return target;
        }
    }
    // Two identical consecutive observations, one successful move per aircraft/direction lifetime.
    public sealed class StripMoveTracker
    {
        private readonly Dictionary<string, Tuple<string,long>> assignments=new Dictionary<string, Tuple<string,long>>();
        public long AssignmentRevision(string key,string runway)
        {
            runway=StripMovementPolicy.NormalizeRunway(runway);
            Tuple<string,long> previous;
            if(!assignments.TryGetValue(key,out previous))previous=Tuple.Create(runway,0L);
            else if(previous.Item1!=runway)previous=Tuple.Create(runway,previous.Item2+1);
            assignments[key]=previous;return previous.Item2;
        }
        private readonly Dictionary<string, string> pending = new Dictionary<string, string>();
        private readonly HashSet<string> completed = new HashSet<string>();
        private readonly HashSet<string> seen = new HashSet<string>();
        public void BeginScan() { seen.Clear(); }
        public bool Ready(string key, string fingerprint)
        {
            seen.Add(key);
            if (completed.Contains(key)) return false;
            string previous;
            var ready = pending.TryGetValue(key, out previous) && previous == fingerprint;
            pending[key] = fingerprint;
            return ready;
        }
        public void Complete(string key) { completed.Add(key); pending.Remove(key); }
        public void EndScan()
        {
            var removed = new List<string>();
            foreach (var key in pending.Keys) if (!seen.Contains(key)) removed.Add(key);
            foreach (var key in removed) pending.Remove(key);
        }
    }
}
