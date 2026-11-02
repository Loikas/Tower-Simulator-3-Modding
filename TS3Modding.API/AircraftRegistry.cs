using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.API
{
    public sealed class AircraftRegistry : IAircraftRegistry
    {
        private Dictionary<AircraftId, AircraftSnapshot> aircraft = new Dictionary<AircraftId, AircraftSnapshot>();
        private readonly EventHub events;
        private bool applying;
        public AircraftRegistry(EventHub events) { this.events = events ?? throw new ArgumentNullException(nameof(events)); }
        public IReadOnlyList<AircraftSnapshot> Snapshot() => Array.AsReadOnly(aircraft.Values.OrderBy(a => a.Id.Sequence).ToArray());
        public void ApplyCompleteObservation(IEnumerable<AircraftSnapshot> observations)
        {
            if (observations == null) throw new ArgumentNullException(nameof(observations));
            if (applying) throw new InvalidOperationException("Reentrant aircraft observation is not supported.");
            // Validate everything before replacing state or emitting events.
            var next = observations.ToDictionary(a => a.Id);
            var previous = aircraft;
            applying = true;
            try
            {
                aircraft = next;
                foreach (var current in next.Values)
                {
                    AircraftSnapshot old;
                    if (!previous.TryGetValue(current.Id, out old)) events.Publish(new AircraftObserved(current));
                    else
                    {
                        if (!SameObservation(old, current)) events.Publish(new AircraftUpdated(old, current));
                        if (old.Details.RawState != current.Details.RawState || old.Details.RawAction != current.Details.RawAction)
                            events.Publish(new AircraftStateChanged(old, current));
                    }
                }
                foreach (var old in previous.Values)
                    if (!next.ContainsKey(old.Id)) events.Publish(new AircraftNoLongerObserved(old));
            }
            finally { applying = false; }
        }
        private static bool SameObservation(AircraftSnapshot a, AircraftSnapshot b) =>
            a.ObjectName == b.ObjectName && a.AirlineType == b.AirlineType && a.Scene == b.Scene &&
            a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.Active == b.Active &&
            a.ReadyToTaxi == b.ReadyToTaxi && a.MarkedDeleted == b.MarkedDeleted &&
            a.Details.Callsign == b.Details.Callsign && a.Details.RawState == b.Details.RawState &&
            a.Details.RawAction == b.Details.RawAction && a.Details.SimulationActive == b.Details.SimulationActive &&
            a.Details.NetworkIndex == b.Details.NetworkIndex && a.Details.AssignedGate == b.Details.AssignedGate &&
            a.Details.AssignedRunway == b.Details.AssignedRunway && a.Details.FinalReportedRunway == b.Details.FinalReportedRunway;
    }
}
