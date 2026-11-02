using System;

namespace TS3Modding.API
{
    public enum FlightCompletionKind { ParkedArrival, DepartureContactAccepted, DepartureAutomaticHandoff }
    public sealed class FlightCompletion
    {
        public FlightCompletion(AircraftId aircraft, string callsign, int networkIndex, string scene, FlightCompletionKind kind, DateTime confirmedUtc)
        { Aircraft = aircraft; Callsign = callsign; NetworkIndex = networkIndex; Scene = scene; Kind = kind; ConfirmedUtc = confirmedUtc; }
        public AircraftId Aircraft { get; }
        public string Callsign { get; }
        public int NetworkIndex { get; }
        public string Scene { get; }
        public FlightCompletionKind Kind { get; }
        public DateTime ConfirmedUtc { get; }
    }
    public interface IFlightArchivalService { bool TryArchiveStrip(StripSnapshot expected, FlightCompletion completion); }
}
