using System;
using System.Linq;
using TS3Modding.API;

namespace TS3Modding.FlightStrips
{
    public static class CompletionPolicy
    {
        public static FlightCompletion Match(StripSnapshot strip, AirportObservation observation)
        {
            if (strip.InBin || string.IsNullOrWhiteSpace(strip.Callsign)) return null;
            var matches = observation.Completions.Where(c => c.Scene == strip.Scene && c.Callsign == strip.Callsign && c.NetworkIndex == strip.NetworkIndex &&
                (strip.Departure ? CompletionEvidenceRules.IsDeparture(c.Kind) : c.Kind == FlightCompletionKind.ParkedArrival) &&
                c.ConfirmedUtc <= observation.CapturedUtc && observation.CapturedUtc - c.ConfirmedUtc <= TimeSpan.FromMinutes(5)).ToArray();
            if (matches.Length != 1) return null;
            var proof = matches[0];
            if (observation.Aircraft.Any(a => a.Scene == strip.Scene && a.Details.Callsign == strip.Callsign && a.Details.NetworkIndex == strip.NetworkIndex && !a.Id.Equals(proof.Aircraft))) return null;
            return proof;
        }
    }
}
