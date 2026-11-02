using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.Tugs
{
    public sealed class TugObservation
    {
        public int TugId { get; set; }
        public int AircraftId { get; set; }
        public string Gate { get; set; }
        public string Callsign { get; set; }
        public string AircraftState { get; set; }
        public string OutgoingCallsign { get; set; }
        public bool Linked { get; set; }
        public bool Home { get; set; }
        public bool Sleeping { get; set; }
        public bool AircraftDeleted { get; set; }
        public bool WaitingForTug { get; set; }
        public bool AtStand { get; set; }
        public bool StandAssociation { get; set; }
        public bool CanReturn { get; set; }
        public bool ReturnActive { get; set; }
        public string ReturnReason { get; set; }
        public string ReturnCallsign { get; set; }

        // These labels describe native flags, not a clearance or permission to tow.
        public string Phase => AircraftDeleted ? "Aircraft unavailable" : StandAssociation&&AtStand ? "Aircraft at stand / tug released" : Linked && !Home ? "Coupled" :
            AircraftId == 0 ? (Home && Sleeping ? "Idle" : Home ? "Returning" : "Unassigned") :
            Home ? (Sleeping ? "Assigned / waiting" : "Returning") : "Approaching / following";
        public string Fingerprint => string.Join("|", new[] { AircraftId.ToString(), Gate, Callsign, AircraftState,
            OutgoingCallsign, Linked.ToString(), Home.ToString(), Sleeping.ToString(), AircraftDeleted.ToString(),
            WaitingForTug.ToString(), AtStand.ToString(), StandAssociation.ToString() });
        public string Summary => "tug=" + TugId + " gate=" + Gate + " aircraft=" + AircraftId +
            " callsign=" + Callsign + " phase=" + Phase + " state=" + AircraftState +
            " waitingForTug=" + WaitingForTug + " atStand=" + AtStand + " outgoing=" + OutgoingCallsign;
    }

    public sealed class TugObservationTracker
    {
        private readonly Dictionary<int, string> previous = new Dictionary<int, string>();
        public string[] Observe(IEnumerable<TugObservation> observations)
        {
            // Validate the complete scan before committing it; do not report removals after a failed scan.
            var fresh = observations.ToDictionary(row => row.TugId);
            var changes = new List<string>();
            foreach (var row in fresh.Values.OrderBy(row => row.TugId))
            {
                string old;
                if (!previous.TryGetValue(row.TugId, out old) || old != row.Fingerprint)
                    changes.Add("Observed " + row.Summary);
            }
            foreach (var missing in previous.Keys.Except(fresh.Keys).OrderBy(id => id))
                changes.Add("No longer observed tug=" + missing + " (not proof of destruction)");
            previous.Clear();
            foreach (var row in fresh.Values) previous.Add(row.TugId, row.Fingerprint);
            return changes.ToArray();
        }
        public void Reset() { previous.Clear(); }
    }
}
