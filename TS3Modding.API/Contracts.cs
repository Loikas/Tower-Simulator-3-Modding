using System;
using System.Collections.Generic;

namespace TS3Modding.API
{
    // IDs identify an observation lifetime, not a flight number or a saved aircraft.
    public struct AircraftId : IEquatable<AircraftId>
    {
        public AircraftId(Guid session, long sequence) { Session = session; Sequence = sequence; }
        public Guid Session { get; }
        public long Sequence { get; }
        public bool Equals(AircraftId other) => Session == other.Session && Sequence == other.Sequence;
        public override bool Equals(object obj) => obj is AircraftId && Equals((AircraftId)obj);
        public override int GetHashCode() => Session.GetHashCode() ^ Sequence.GetHashCode();
        public override string ToString() => Session.ToString("N") + ":" + Sequence;
    }

    public sealed class AircraftSnapshot
    {
        public AircraftSnapshot(AircraftId id, string objectName, string airlineType, string scene,
            float x, float y, float z, bool active, bool readyToTaxi, bool markedDeleted)
            : this(id, objectName, airlineType, scene, x, y, z, active, readyToTaxi, markedDeleted, AircraftDetails.Unknown) { }
        public AircraftSnapshot(AircraftId id, string objectName, string airlineType, string scene,
            float x, float y, float z, bool active, bool readyToTaxi, bool markedDeleted, AircraftDetails details)
        {
            Id = id; ObjectName = objectName; AirlineType = airlineType; Scene = scene;
            X = x; Y = y; Z = z; Active = active; ReadyToTaxi = readyToTaxi; MarkedDeleted = markedDeleted;
            Details = details ?? AircraftDetails.Unknown;
        }
        public AircraftId Id { get; }
        public AircraftDetails Details { get; }
        public string ObjectName { get; }
        // Raw TS3 typeAirline value: not assumed to be a callsign or airline identifier.
        public string AirlineType { get; }
        public string Scene { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public bool Active { get; }
        // Raw flags; no operational state is inferred from these yet.
        public bool ReadyToTaxi { get; }
        public bool MarkedDeleted { get; }
    }

    public sealed class AircraftObserved { public AircraftObserved(AircraftSnapshot aircraft) { Aircraft = aircraft; } public AircraftSnapshot Aircraft { get; } }
    public sealed class AircraftUpdated { public AircraftUpdated(AircraftSnapshot previous, AircraftSnapshot current) { Previous = previous; Current = current; } public AircraftSnapshot Previous { get; } public AircraftSnapshot Current { get; } }
    // Missing from a complete scan does not prove physical despawn or flight completion.
    public sealed class AircraftNoLongerObserved { public AircraftNoLongerObserved(AircraftSnapshot last) { Last = last; } public AircraftSnapshot Last { get; } }
    public sealed class SceneLoaded { public SceneLoaded(string name) { Name = name; } public string Name { get; } }

    public interface IEventStream { IDisposable Subscribe<T>(Action<T> handler); }
    public interface IAircraftRegistry { IReadOnlyList<AircraftSnapshot> Snapshot(); }
    public interface IFrameworkContext
    {
        IEventStream Events { get; }
        IAircraftRegistry Aircraft { get; }
        string GameAssemblySha256 { get; }
        bool ObservationAvailable { get; }
        void Log(string message);
    }
    public interface ITS3Module : IDisposable
    {
        string Id { get; }
        void Initialize(IFrameworkContext context);
        void Tick();
    }
    public interface IFrameworkHost : IFrameworkContext
    {
        // Main Unity thread only. Accepted modules are disposed on removal or initialization failure.
        IDisposable RegisterModule(ITS3Module module);
    }
    public static class FrameworkServices
    {
        public static IFrameworkHost Current { get; private set; }
        public static void Attach(IFrameworkHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (Current != null) throw new InvalidOperationException("A framework host is already attached.");
            Current = host;
        }
        public static void Detach(IFrameworkHost host) { if (ReferenceEquals(Current, host)) Current = null; }
    }
}
