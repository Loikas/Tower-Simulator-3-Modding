using System;
using System.Collections.Generic;
using System.Linq;

namespace TS3Modding.API
{
    public sealed class AircraftDetails
    {
        public static readonly AircraftDetails Unknown = new AircraftDetails(null, null, null, false, 0, null);
        public AircraftDetails(string callsign, string state, string action, bool simulationActive, int networkIndex, string assignedGate)
            : this(callsign, state, action, simulationActive, networkIndex, assignedGate, null) { }
        public AircraftDetails(string callsign, string state, string action, bool simulationActive, int networkIndex, string assignedGate, string assignedRunway)
            : this(callsign, state, action, simulationActive, networkIndex, assignedGate, assignedRunway, null) { }
        public AircraftDetails(string callsign, string state, string action, bool simulationActive, int networkIndex, string assignedGate, string assignedRunway, string finalReportedRunway)
        { Callsign = callsign; RawState = state; RawAction = action; SimulationActive = simulationActive; NetworkIndex = networkIndex; AssignedGate = assignedGate; AssignedRunway = assignedRunway; FinalReportedRunway = finalReportedRunway; }
        public string Callsign { get; }
        public string RawState { get; }
        public string RawAction { get; }
        public bool SimulationActive { get; }
        public int NetworkIndex { get; }
        // Airplane._ikgate -> IKGate.gate.gatename, when a jetway link exists.
        public string AssignedGate { get; }
        public string AssignedRunway { get; private set; }
        public string FinalReportedRunway { get; }
    }
    public sealed class GateSnapshot
    {
        public GateSnapshot(int instanceId, string scene, string name, string state, AircraftId? linkedAircraft)
        { InstanceId = instanceId; Scene = scene; Name = name; RawState = state; LinkedAircraft = linkedAircraft; }
        public int InstanceId { get; }
        public string Scene { get; }
        public string Name { get; }
        public string RawState { get; }
        public AircraftId? LinkedAircraft { get; }
    }
    public sealed class StripSnapshot
    {
        public StripSnapshot(int instanceId, string scene, string callsign, int networkIndex, string block, bool departure, bool inBin)
            : this(instanceId, scene, callsign, networkIndex, block, departure, inBin, null, new StripRoute[0]) { }
        public StripSnapshot(int instanceId, string scene, string callsign, int networkIndex, string block, bool departure, bool inBin,
            string assignedRunway, IEnumerable<StripRoute> nextRoutes)
            : this(instanceId,scene,callsign,networkIndex,block,departure,inBin,assignedRunway,nextRoutes,0) { }
        public StripSnapshot(int instanceId, string scene, string callsign, int networkIndex, string block, bool departure, bool inBin,
            string assignedRunway, IEnumerable<StripRoute> nextRoutes, int boardInstanceId)
        { InstanceId = instanceId; Scene = scene; Callsign = callsign; NetworkIndex = networkIndex; Block = block; Departure = departure; InBin = inBin;
            AssignedRunway = assignedRunway; NextRoutes = Array.AsReadOnly(nextRoutes.ToArray()); BoardInstanceId=boardInstanceId; }
        public int BoardInstanceId { get; }
        public int InstanceId { get; }
        public string Scene { get; }
        public string Callsign { get; }
        public int NetworkIndex { get; }
        public string Block { get; }
        public bool Departure { get; }
        public bool InBin { get; }
        public string AssignedRunway { get; private set; }
        public IReadOnlyList<StripRoute> NextRoutes { get; private set; }
    }
    public sealed class StripRoute
    {
        public StripRoute(string header, IEnumerable<string> runways) { Header = header; Runways = Array.AsReadOnly(runways.ToArray()); }
        public string Header { get; }
        public IReadOnlyList<string> Runways { get; }
    }
    public interface IStripMovementService { bool TryMoveStrip(StripSnapshot expected, string targetHeader); }
    public sealed class AirportObservation
    {
        public AirportObservation(IEnumerable<AircraftSnapshot> aircraft, IEnumerable<GateSnapshot> gates, IEnumerable<StripSnapshot> strips)
            : this(aircraft, gates, strips, new FlightCompletion[0]) { }
        public AirportObservation(IEnumerable<AircraftSnapshot> aircraft, IEnumerable<GateSnapshot> gates, IEnumerable<StripSnapshot> strips, IEnumerable<FlightCompletion> completions)
        {
            CapturedUtc = DateTime.UtcNow;
            Aircraft = Array.AsReadOnly(aircraft.ToArray()); Gates = Array.AsReadOnly(gates.ToArray()); Strips = Array.AsReadOnly(strips.ToArray());
            Completions = Array.AsReadOnly(completions.ToArray());
        }
        public IReadOnlyList<AircraftSnapshot> Aircraft { get; }
        public DateTime CapturedUtc { get; }
        public IReadOnlyList<GateSnapshot> Gates { get; }
        public IReadOnlyList<StripSnapshot> Strips { get; }
        public IReadOnlyList<FlightCompletion> Completions { get; }
    }
    public interface IAirportObservationSource { AirportObservation CurrentAirport { get; } }
    public sealed class AirportObserved
    {
        public AirportObserved(AirportObservation observation) { Observation = observation; }
        public AirportObservation Observation { get; }
    }
    public sealed class AircraftStateChanged
    {
        public AircraftStateChanged(AircraftSnapshot previous, AircraftSnapshot current) { Previous = previous; Current = current; }
        public AircraftSnapshot Previous { get; }
        public AircraftSnapshot Current { get; }
    }
}
