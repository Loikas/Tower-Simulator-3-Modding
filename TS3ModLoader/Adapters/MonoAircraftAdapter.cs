using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Linq;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding.Adapters
{
    internal sealed class MonoAircraftAdapter
    {
        internal const string KnownAssemblyHash = "26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7";
        private readonly Guid session = Guid.NewGuid();
        private readonly int mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        private readonly Type airplane;
        private readonly FieldInfo airline, ready, deleted;
        private readonly FieldInfo callsign, state, action, active, network, gateLink, terminalGate, gateName;
        private readonly FieldInfo targetRunway, roadName;
        private readonly FieldInfo schedule;
        private readonly FieldInfo atTerminal, showStrip, terminalRoad, speed, altitude, owner, areas, areaTarget;
        private readonly List<FlightCompletion> completions = new List<FlightCompletion>();
        private readonly Dictionary<AircraftId, string> finalReports = new Dictionary<AircraftId, string>();
        private Dictionary<int, Identity> identities = new Dictionary<int, Identity>();
        private long sequence;
        public MonoAircraftAdapter(Assembly assembly, string hash)
        {
            if (!string.Equals(hash, KnownAssemblyHash, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Game assembly hash is not a reviewed observation profile: " + hash);
            airplane = assembly.GetType("Airplane", true, false);
            schedule=airplane.GetField("_timetable",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)??throw new MissingFieldException("Airplane", "_timetable");
            if (!typeof(MonoBehaviour).IsAssignableFrom(airplane)) throw new NotSupportedException("Airplane is no longer a MonoBehaviour.");
            airline = RequireField("typeAirline", typeof(string));
            ready = RequireField("_ready_to_taxi", typeof(bool));
            deleted = RequireField("_deleted", typeof(bool));
            callsign = RequireField("_name", typeof(string));
            active = RequireField("_active", typeof(bool));
            network = RequireField("_netidx", typeof(int));
            state = RequireField("_state", assembly.GetType("Airplane+KLNOGMBLABM", true));
            action = RequireField("_action", assembly.GetType("Airplane+GKEPLNMJHIE", true));
            if (!state.FieldType.IsEnum || !action.FieldType.IsEnum) throw new NotSupportedException("Aircraft state/action are no longer enum types.");
            var gateType = ProfileBindings.ComponentType(assembly, "IKGate");
            var terminalType = ProfileBindings.ComponentType(assembly, "TerminalGate");
            gateLink = RequireField("_ikgate", gateType);
            terminalGate = ProfileBindings.Field(gateType, "gate", terminalType);
            gateName = ProfileBindings.Field(terminalType, "gatename", typeof(string));
            var roadType = assembly.GetType("Road", true);
            targetRunway = RequireField("_trgrunway", roadType);
            roadName = ProfileBindings.Field(roadType, "name", typeof(string));
            atTerminal = RequireField("_imatterminal", typeof(bool)); showStrip = RequireField("_showstrip", typeof(bool));
            terminalRoad = RequireField("_terminal", roadType); speed = RequireField("_lv", typeof(float)); altitude = RequireField("_alt", typeof(float));
            var frequency = assembly.GetType("DatFreq+Freq_t", true); var area = assembly.GetType("DatFreq+Freq_area", true);
            owner = RequireField("_owner", frequency);
            areas = ProfileBindings.Field(frequency, "areas", typeof(List<>).MakeGenericType(area));
            areaTarget = ProfileBindings.Field(area, "target", typeof(string));
        }
        private FieldInfo RequireField(string name, Type type)
        {
            var field = airplane.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != type) throw new NotSupportedException("Incompatible Airplane field: " + name);
            return field;
        }
        private FieldInfo flyAwayDeparture;
        public IReadOnlyList<AircraftSnapshot> Observe()
        {
            var observations = new List<AircraftSnapshot>();
            var next = new Dictionary<int, Identity>();
            var nextSequence = sequence;
            foreach (var candidate in Resources.FindObjectsOfTypeAll(airplane))
            {
                var component = candidate as Component;
                if (!ProfileBindings.InLoadedScene(component)) continue;
                var instance = component.GetInstanceID();
                Identity identity;
                if (!identities.TryGetValue(instance, out identity) || !ReferenceEquals(identity.Component, component))
                    identity = new Identity(component, new AircraftId(session, ++nextSequence));
                var position = component.transform.position;
                var gate = gateLink.GetValue(component) as Component;
                var terminal = gate ? terminalGate.GetValue(gate) as Component : null;
                var assignedGate = terminal ? (string)gateName.GetValue(terminal) : null;
                var runway = targetRunway.GetValue(component);
                string finalRunway; finalReports.TryGetValue(identity.Id, out finalRunway);
                observations.Add(new AircraftSnapshot(identity.Id, component.name,
                    (string)airline.GetValue(component), component.gameObject.scene.name,
                    position.x, position.y, position.z, component.gameObject.activeInHierarchy,
                    (bool)ready.GetValue(component), (bool)deleted.GetValue(component),
                    new AircraftDetails((string)callsign.GetValue(component), state.GetValue(component).ToString(),
                        action.GetValue(component).ToString(), (bool)active.GetValue(component), (int)network.GetValue(component), assignedGate,
                        runway == null ? null : (string)roadName.GetValue(runway), finalRunway)));
                next.Add(instance, identity);
                CaptureCompletion(component, false);
            }
            // Failed scans do not change observation lifetimes.
            identities = next;
            sequence = nextSequence;
            return observations.AsReadOnly();
        }
        internal void CaptureFinalReport(object instance, string message, int messageKind)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread) return;
            var component = instance as Component;
            var known = ObservedId(component);
            if (known.HasValue && messageKind == 4 && message != null && message.IndexOf("going around", StringComparison.OrdinalIgnoreCase) >= 0)
                finalReports.Remove(known.Value);
            if (!ProfileBindings.InLoadedScene(component) || !CompletionEvidenceRules.FinalApproachReport(action.GetValue(component).ToString(),
                (bool)active.GetValue(component), (bool)deleted.GetValue(component), messageKind, message)) return;
            var runway = targetRunway.GetValue(component);
            if (runway == null) return;
            Identity identity;
            if (!identities.TryGetValue(component.GetInstanceID(), out identity) || !ReferenceEquals(identity.Component, component))
            {
                identity = new Identity(component, new AircraftId(session, ++sequence));
                identities[component.GetInstanceID()] = identity;
            }
            finalReports[identity.Id] = (string)roadName.GetValue(runway);
            if (finalReports.Count > 512) finalReports.Remove(finalReports.Keys.First());
        }
        internal AircraftId? ObservedId(Component component)
        {
            Identity identity;
            return component && identities.TryGetValue(component.GetInstanceID(), out identity) && ReferenceEquals(identity.Component, component)
                ? (AircraftId?)identity.Id : null;
        }
        internal void CaptureCompletion(object instance, bool acceptedContactBranch,bool automaticHandoff=false)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread) return;
            var component = instance as Component;
            if (!ProfileBindings.InLoadedScene(component) || !(bool)active.GetValue(component) || (bool)deleted.GetValue(component)) return;
            var id = ObservedId(component);
            if (!id.HasValue) return; // Never attach evidence to an aircraft lifetime we have not observed.
            var rawAction = action.GetValue(component).ToString(); var rawState = state.GetValue(component).ToString();
            FlightCompletionKind kind;
            if (acceptedContactBranch||automaticHandoff)
            {
                var frequency = owner.GetValue(component);
                var region = frequency == null ? null : areas.GetValue(frequency) as IList;
                // The verified missed-departure branch fetched this owner using the literal departure target.
                var target = automaticHandoff&&frequency!=null?"departure":region == null || region.Count == 0 ? null : (string)areaTarget.GetValue(region[0]);
                if (!CompletionEvidenceRules.AcceptedDepartureContact(rawAction, rawState, (bool)active.GetValue(component), (bool)deleted.GetValue(component),
                    (float)altitude.GetValue(component), target, true)) return;
                if(automaticHandoff){
                    if(flyAwayDeparture==null)flyAwayDeparture=RequireField("_fly_away_departure",typeof(bool));
                    if(!CompletionEvidenceRules.AutomaticDepartureHandoff(rawAction,rawState,(bool)active.GetValue(component),(bool)deleted.GetValue(component),(float)altitude.GetValue(component),target,true,(bool)flyAwayDeparture.GetValue(component)))return;
                }
                kind = automaticHandoff?FlightCompletionKind.DepartureAutomaticHandoff:FlightCompletionKind.DepartureContactAccepted;
            }
            else
            {
                if (!CompletionEvidenceRules.ParkedArrival(rawAction, rawState, (bool)active.GetValue(component), (bool)deleted.GetValue(component),
                    (bool)atTerminal.GetValue(component), (bool)showStrip.GetValue(component), terminalRoad.GetValue(component) != null, (float)speed.GetValue(component))) return;
                kind = FlightCompletionKind.ParkedArrival;
            }
            if (completions.Any(c => c.Aircraft.Equals(id.Value) && c.Callsign==(string)callsign.GetValue(component) && c.NetworkIndex==(int)network.GetValue(component) && c.Scene==component.gameObject.scene.name && (c.Kind == kind||CompletionEvidenceRules.IsDeparture(c.Kind)&&CompletionEvidenceRules.IsDeparture(kind)))) return;
            completions.Add(new FlightCompletion(id.Value, (string)callsign.GetValue(component), (int)network.GetValue(component),
                component.gameObject.scene.name, kind, DateTime.UtcNow));
            if (completions.Count > 512) completions.RemoveAt(0);
        }
        internal IReadOnlyList<FlightCompletion> CompletionEvidence()
        {
            completions.RemoveAll(c => DateTime.UtcNow - c.ConfirmedUtc > TimeSpan.FromMinutes(5));
            return completions.AsReadOnly();
        }
        internal bool MatchesRetiredTow(StripSnapshot strip,object instance,object expectedSchedule)
        {
            var plane=instance as Component;
            return strip!=null&&strip.Departure&&!strip.InBin&&ProfileBindings.InLoadedScene(plane)&&airplane.IsInstanceOfType(plane)&&expectedSchedule!=null&&
                ReferenceEquals(schedule.GetValue(plane),expectedSchedule)&&!(bool)active.GetValue(plane)&&!(bool)showStrip.GetValue(plane)&&!(bool)deleted.GetValue(plane)&&
                (bool)atTerminal.GetValue(plane)&&terminalRoad.GetValue(plane)!=null&&Math.Abs((float)speed.GetValue(plane))<.1&&(float)altitude.GetValue(plane)<=1&&
                state.GetValue(plane).ToString()=="STATE_WAITING"&&action.GetValue(plane).ToString()=="ACTION_OUTGOING"&&
                strip.Scene==plane.gameObject.scene.name&&strip.NetworkIndex==(int)network.GetValue(plane)&&strip.Callsign==(string)callsign.GetValue(plane);
        }
        internal void StateChanged(object instance)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread) return;
            var component = instance as Component;
            if (!component) return;
            var rawState = state.GetValue(component).ToString();
            var id = ObservedId(component);
            if (id.HasValue && rawState != "STATE_INCOMING" && rawState != "STATE_FINALRUNWAY" && rawState != "STATE_LAND") finalReports.Remove(id.Value);
            CaptureCompletion(instance, false);
        }
        private sealed class Identity
        {
            public Identity(Component component, AircraftId id) { Component = component; Id = id; }
            public Component Component { get; }
            public AircraftId Id { get; }
        }
    }
}
