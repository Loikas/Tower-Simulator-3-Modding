using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding.Tugs
{
    internal sealed class NativeReturnController : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static NativeReturnController current;
        private readonly Harmony harmony = new Harmony("nl.ts3modding.tugs.return");
        private readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();
        private readonly IFrameworkContext context;
        private readonly bool enabled;
        private readonly Type planeType, truckType, gameType;
        private readonly MethodInfo multiplayer, pullBack, setState, setPlane, waitForStart, setGatePlane;
        private readonly FieldInfo gateManagerInstance;
        private Job job;
        internal Action<Component> SuccessfulReturn;
        private readonly HashSet<string> reportedRoutes=new HashSet<string>();
        private readonly ConditionalWeakTable<Component,PushbackEnd> completedPushbacks=new ConditionalWeakTable<Component,PushbackEnd>();
        private sealed class PushbackEnd
        {
            public object Stand,Schedule;
            public string Callsign;
            public List<Vector3> Route;
            public Vector3[] Points;
            public Vector3 Position;
            public int Index;
            public object Intersection,Knot;
            public object[] Holds;
        }
        internal string Message { get; private set; } = "Return is available for stopped aircraft just after pushback.";
        private sealed class Job
        {
            public Component Truck, Aircraft;
            public object Stand, Gate, OriginalLock, Schedule;
            public string Callsign;
            public List<Vector3> OriginalRoute;
            public Vector3[] RoutePoints;
            public Vector3 StartPosition, StandPosition;
            public int RoutePosition;
            public List<Vector3> ReturnRoute;
            public int ReturnRoutePosition;
            public bool Returning, OldWait, OldHold, OldNeedCalculation;
            public float Elapsed;
        }
        internal NativeReturnController(Assembly assembly, IFrameworkContext context, bool enabled)
        {
            this.context = context; this.enabled = enabled;
            planeType = assembly.GetType("Airplane", true); truckType = assembly.GetType("TowTruck", true);
            gameType = assembly.GetType("Game", true);
            Require(truckType, "_plane", "_terminal", "_gate", "_car_linked", "home", "sleep", "need_ready", "_link", "holdtimer");
            Require(planeType, "_name", "_timetable", "_state", "_action", "_deleted", "_ikgate", "_oldterminal", "_terminal",
                "_terminal_start_pos", "_pos", "_lv", "_alt", "_route", "_routepos", "_router", "_need_roadcalculator",
                "_ready_to_taxi", "_requestingpush", "_trailing", "_trailingend", "_imatterminal", "_hold_position_taxistop", "wait_for_trailer",
                "_holdshort", "_target_intersection", "_target_knot", "ac", "_type", "_set_term_gate");
            Require(assembly.GetType("Airptype", true), "_weight_class");
            Require(assembly.GetType("Router", true), "finished");
            Require(assembly.GetType("Road", true), "name", "occupied", "locked");
            Require(assembly.GetType("IKGate", true), "truck", "plane", "road", "state");
            Require(assembly.GetType("AirplaneContainer", true), "front_wheel");
            Require(gameType, "instance", "airplanes", "speedup");
            multiplayer = assembly.GetType("TCP", true).GetMethod("get_mp_state", Flags, null, Type.EmptyTypes, null);
            pullBack = planeType.GetMethod("RunPullBack", Flags, null, Type.EmptyTypes, null);
            setState = planeType.GetMethod("SetState", Flags, null, new[] { F(planeType, "_state").FieldType }, null);
            setPlane = truckType.GetMethod("SetPlane", Flags, null, new[] { planeType }, null);
            waitForStart = planeType.GetMethod("WaitForStart", Flags, null, Type.EmptyTypes, null);
            var gateManagerType=assembly.GetType("IKGateManager",true);
            gateManagerInstance=F(gateManagerType,"instance");
            setGatePlane=gateManagerType.GetMethod("SetGatePlane",Flags,null,new[]{typeof(string),planeType},null);
            if (multiplayer == null || pullBack == null || setState == null || setPlane == null || waitForStart == null || setGatePlane==null) throw new NotSupportedException("Return method signatures changed.");
            if (!enabled) { Message = "Return-to-stand is disabled in mod options."; return; }
            if (current != null) throw new InvalidOperationException("Return controller already registered.");
            current = this;
            try
            {
                harmony.Patch(truckType.GetMethod("DMCOGALGKKC", Flags, null, new[] { typeof(float) }, null),
                    transpiler: new HarmonyMethod(typeof(NativeReturnController), nameof(HomeTranspiler)));
                harmony.Patch(assembly.GetType("SavedGame", true).GetMethod("EJLLBCNLCGN", Flags, null, new[] { typeof(string) }, null),
                    prefix: new HarmonyMethod(typeof(NativeReturnController), nameof(SavePrefix)));
                harmony.Patch(planeType.GetMethods(Flags).Single(m => m.Name == "RunCommand"),
                    prefix: new HarmonyMethod(typeof(NativeReturnController), nameof(CommandPrefix)));
                harmony.Patch(planeType.GetMethod("AGHOOFEDHAC",Flags,null,Type.EmptyTypes,null),
                    prefix:new HarmonyMethod(typeof(NativeReturnController),nameof(PushStarting)),
                    postfix:new HarmonyMethod(typeof(NativeReturnController),nameof(PushFinished)));
            }
            catch { Dispose(); throw; }
        }
        private FieldInfo F(Type type, string name)
        {
            var key = type.FullName + "." + name;
            FieldInfo field;
            if (!fields.TryGetValue(key, out field)) fields.Add(key, field = type.GetField(name, Flags) ?? throw new MissingFieldException(type.FullName, name));
            return field;
        }
        private void Require(Type type, params string[] names) { foreach (var name in names) F(type, name); }
        private object Get(object obj, string name) => F(obj.GetType(), name).GetValue(obj);
        private T Get<T>(object obj, string name) => (T)Get(obj, name);
        private void Set(object obj, string name, object value) => F(obj.GetType(), name).SetValue(obj, value);
        private object Game => F(gameType, "instance").GetValue(null);
        private bool SinglePlayer => Convert.ToInt32(multiplayer.Invoke(null, null)) == 0;
        private StandAssociationIndex<Component> observationIndex;
        internal IDisposable ObserveAssociations()
        {
            if(observationIndex!=null)throw new InvalidOperationException("Nested tug observation.");
            var game=Game;
            var entries=game==null?new StandAssociationIndex<Component>.Entry[0]:((IEnumerable)Get(game,"airplanes")).Cast<Component>()
                .Where(p=>p&&!Get<bool>(p,"_deleted")).Select(p=>new StandAssociationIndex<Component>.Entry{Aircraft=p,Stand=Get(p,"_terminal"),OldStand=Get(p,"_oldterminal"),Gate=Get(p,"_ikgate"),Parked=Get<bool>(p,"_imatterminal")}).ToArray();
            observationIndex=new StandAssociationIndex<Component>(entries);return new ObservationScope(()=>observationIndex=null);
        }
        private sealed class ObservationScope:IDisposable {private Action release;internal ObservationScope(Action release){this.release=release;}public void Dispose(){var action=release;release=null;action?.Invoke();}}
        private Component Candidate(Component truck)
        {
            var assigned = Get(truck, "_plane") as Component;
            if (assigned && !Get<bool>(assigned, "_deleted")) return assigned;
            var gate = Get(truck, "_gate"); var stand = Get(truck, "_terminal");
            if (gate == null || stand == null || Game == null) return null;
            if(observationIndex!=null)return observationIndex.Candidate(stand,gate);
            var candidates = ((IEnumerable)Get(Game, "airplanes")).Cast<Component>().Where(p => p && !Get<bool>(p, "_deleted") &&
                (Get(p, "_ikgate") == null || ReferenceEquals(Get(p, "_ikgate"), gate)) && (ReferenceEquals(Get(p, "_oldterminal"), stand) ||
                    ReferenceEquals(Get(p, "_terminal"), stand))).ToArray();
            var parked=candidates.Where(p=>ReferenceEquals(Get(p,"_terminal"),stand)&&Get<bool>(p,"_imatterminal")).ToArray();
            if(parked.Length>0)return parked.Length==1?parked[0]:null;
            return candidates.Length == 1 ? candidates[0] : null;
        }
        internal Component AssociatedAircraft(Component truck)=>Candidate(truck);
        private ReturnFacts Facts(Component truck, Component aircraft)
        {
            var f = new ReturnFacts { Enabled = enabled, SinglePlayer = SinglePlayer, HasAircraft = aircraft };
            if (!aircraft) return f;
            f.Deleted=Get<bool>(aircraft,"_deleted");f.State=Convert.ToInt32(Get(aircraft,"_state"));f.AtStand=Get<bool>(aircraft,"_imatterminal");
            f.Outgoing=Convert.ToInt32(Get(aircraft,"_action"))==2;f.ReadyToTaxi=Get<bool>(aircraft,"_ready_to_taxi");f.PushInProgress=Get<bool>(aircraft,"_requestingpush")||Get<bool>(aircraft,"_trailing");
            if(!f.Enabled||!f.SinglePlayer||f.Deleted||!ReturnPolicy.InReturnPhase(f))return f;
            var stand = Get(truck, "_terminal"); var gate = Get(truck, "_gate"); var assigned = Get(truck, "_plane") as UnityEngine.Object;
            var container = Get(aircraft, "ac");
            f.Deleted = Get<bool>(aircraft, "_deleted"); f.HasOriginalStand = stand != null && ReferenceEquals(Get(aircraft, "_oldterminal"), stand);
            f.GateMatches = gate != null && (Get(aircraft, "_ikgate") == null || ReferenceEquals(Get(aircraft, "_ikgate"), gate)) &&
                ReferenceEquals(Get(gate, "truck"), truck) && (Get(gate, "plane") == null || ReferenceEquals(Get(gate, "plane"), aircraft)) &&
                ReferenceEquals(Get(gate, "road"), stand) && Convert.ToInt32(Get(gate, "state")) == 0;
            f.TugAvailable = !assigned || ReferenceEquals(assigned, aircraft);
            var aircraftType = Get(aircraft, "_type");
            f.HasNoseWheel = container != null && (Get(container, "front_wheel") as UnityEngine.Object) &&
                aircraftType != null && Convert.ToInt32(Get(aircraftType, "_weight_class")) != 0;
            f.StandOccupied = stand != null && Get<bool>(stand, "occupied");
            var standLock = stand == null ? null : Get(stand, "locked") as UnityEngine.Object;
            f.StandClaimedByOther = standLock && !ReferenceEquals(standLock, aircraft);
            f.OtherAircraftClaimsStand = observationIndex!=null?observationIndex.ClaimedByOther(stand,aircraft):stand != null && ((IEnumerable)Get(Game, "airplanes")).Cast<Component>().Any(p => p &&
                !ReferenceEquals(p, aircraft) && !Get<bool>(p, "_deleted") && ReferenceEquals(Get(p, "_terminal"), stand));
            f.State = Convert.ToInt32(Get(aircraft, "_state")); f.AtStand = Get<bool>(aircraft, "_imatterminal");
            f.Outgoing = Convert.ToInt32(Get(aircraft, "_action")) == 2;
            f.ReadyToTaxi = Get<bool>(aircraft, "_ready_to_taxi"); f.PushInProgress = Get<bool>(aircraft, "_requestingpush") || Get<bool>(aircraft, "_trailing");
            f.Speed = Get<float>(aircraft, "_lv"); f.Altitude = Get<float>(aircraft, "_alt");
            var router=Get(aircraft,"_router");
            f.RouterBusy = (router != null && !Get<bool>(router,"finished")) || Get<bool>(aircraft, "_need_roadcalculator");
            f.HasExplicitHold = ((IList)Get(aircraft, "_holdshort")).Count != 0 || Get(aircraft, "_target_intersection") != null || Get(aircraft, "_target_knot") != null;
            var route = Get<List<Vector3>>(aircraft, "_route"); var position = Get<Vector3>(aircraft, "_pos");
            var start = Get<Vector3>(aircraft, "_terminal_start_pos");
            f.RouteCount = route == null ? 0 : route.Count; f.RoutePosition = Get<int>(aircraft, "_routepos");
            int returnPosition;
            var returnRoute=ReturnRoute(route,f.RoutePosition,start,out returnPosition);
            f.RouteMatchesOrigin = returnRoute != null;
            f.VerifiedPushbackEnd=VerifiedEnd(aircraft);
            // An unchanged, observed endpoint with no intervening command can retain native forward-route work.
            // A running worker is never bypassed. Pending forward work is suspended only after accepting recall.
            f.RouterBusy=(router!=null&&!Get<bool>(router,"finished"))||
                (Get<bool>(aircraft,"_need_roadcalculator")&&!f.VerifiedPushbackEnd);
            PushbackEnd baseline;
            if(f.VerifiedPushbackEnd&&completedPushbacks.TryGetValue(aircraft,out baseline))
                f.HasExplicitHold=!ReferenceEquals(Get(aircraft,"_target_intersection"),baseline.Intersection)||
                    !ReferenceEquals(Get(aircraft,"_target_knot"),baseline.Knot)||
                    !((IEnumerable)Get(aircraft,"_holdshort")).Cast<object>().SequenceEqual(baseline.Holds);
            if(f.VerifiedPushbackEnd&&returnRoute!=null)
            {
                returnRoute=EndpointRoute(returnRoute,returnPosition,position,out returnPosition);
                f.RouteMatchesOrigin=returnRoute!=null;
            }
            f.StandDistance = Vector3.Distance(position, start); f.TugDistance = Vector3.Distance(truck.transform.position, aircraft.transform.position);
            if (returnRoute != null)
            {
                f.FirstReturnPointDistance = Vector3.Distance(position, returnRoute[returnPosition - 2]);
                f.ReturnPathLength = f.FirstReturnPointDistance;
                for (var i = returnPosition - 2; i > 0; i--) f.ReturnPathLength += Vector3.Distance(returnRoute[i], returnRoute[i - 1]);
            }
            else f.FirstReturnPointDistance = f.ReturnPathLength = float.PositiveInfinity;
            return f;
        }
        private bool VerifiedEnd(Component plane)
        {
            PushbackEnd end;
            return completedPushbacks.TryGetValue(plane,out end)&&ReferenceEquals(Get(plane,"_oldterminal"),end.Stand)&&
                ReferenceEquals(Get(plane,"_timetable"),end.Schedule)&&Get<string>(plane,"_name")==end.Callsign&&
                ReferenceEquals(Get(plane,"_route"),end.Route)&&end.Route.SequenceEqual(end.Points)&&
                Get<int>(plane,"_routepos")==end.Index&&Vector3.Distance(Get<Vector3>(plane,"_pos"),end.Position)<0.5f&&
                Get<bool>(plane,"_hold_position_taxistop");
        }
        private static void PushStarting(object __instance,out bool __state)
        {
            var self=current;
            __state=false;
            try { __state=self!=null&&self.Get<bool>(__instance,"_requestingpush")&&Convert.ToInt32(self.Get(__instance,"_state"))!=10; }
            catch(Exception error) { self?.context.Log("[Tugs] Pushback observation skipped: "+error.Message); }
        }
        private static void PushFinished(object __instance,bool __state)
        {
            var self=current; var plane=__instance as Component;
            if(!__state||self==null||!plane) return;
            try { self.RecordEndpoint(plane); }
            catch(Exception error) { self.context.Log("[Tugs] Pushback endpoint could not be recorded: "+error.Message); }
        }
        private void RecordEndpoint(Component plane)
        {
            var self=this; var state=Convert.ToInt32(Get(plane,"_state"));
            if((state!=12&&state!=15)||Get<bool>(plane,"_trailing")||self.Get<bool>(plane,"_requestingpush")||
                !self.Get<bool>(plane,"_hold_position_taxistop")||Convert.ToInt32(self.Get(plane,"_action"))!=2) return;
            var route=self.Get<List<Vector3>>(plane,"_route");
            if(route==null||route.Count<3||route.Count>8192) return;
            self.completedPushbacks.Remove(plane);
            self.completedPushbacks.Add(plane,new PushbackEnd{Stand=self.Get(plane,"_oldterminal"),Schedule=self.Get(plane,"_timetable"),
                Callsign=self.Get<string>(plane,"_name"),Route=route,Points=route.ToArray(),Position=self.Get<Vector3>(plane,"_pos"),Index=self.Get<int>(plane,"_routepos"),
                Intersection=Get(plane,"_target_intersection"),Knot=Get(plane,"_target_knot"),Holds=((IEnumerable)Get(plane,"_holdshort")).Cast<object>().ToArray()});
            self.context.Log("[Tugs] Recorded native pushback endpoint for "+self.Get<string>(plane,"_name")+"; route points="+route.Count);
        }
        private static List<Vector3> EndpointRoute(List<Vector3> route,int position,Vector3 endpoint,out int index)
        {
            index=0; double best=2; int segment=-1;
            for(int i=0;i<position-2;i++)
            {
                var a=route[i];var b=route[i+1];double dx=b.x-a.x,dy=b.y-a.y,dz=b.z-a.z;
                var size=dx*dx+dy*dy+dz*dz;if(size<0.000001) continue;
                var t=Math.Max(0,Math.Min(1,((endpoint.x-a.x)*dx+(endpoint.y-a.y)*dy+(endpoint.z-a.z)*dz)/size));
                var point=new Vector3((float)(a.x+t*dx),(float)(a.y+t*dy),(float)(a.z+t*dz));
                var distance=Vector3.Distance(point,endpoint);
                if(distance<best){best=distance;segment=i;}
            }
            if(segment<0) return null;
            var result=route.Take(segment+1).ToList(); result.Add(endpoint);
            // Native RunPullBack subtracts two before reversing. One trailing sentinel makes it start at this endpoint.
            result.Add(endpoint); index=result.Count; return result;
        }
        // Native routes can include stand-road points before the actual parking knot.
        // Retain only the observed native corridor back to the recorded aircraft stand position.
        private static List<Vector3> ReturnRoute(List<Vector3> route,int position,Vector3 start,out int returnPosition)
        {
            returnPosition=position;
            if(route==null||route.Count<3||position<3||position>route.Count) return null;
            if(Vector3.Distance(route[0],start)<=2f) return new List<Vector3>(route);
            float best=2f; int segment=-1;
            for(int i=0;i<=position-3;i++)
            {
                var a=route[i]; var b=route[i+1];
                double dx=b.x-a.x,dy=b.y-a.y,dz=b.z-a.z;
                var length=dx*dx+dy*dy+dz*dz; if(length<0.000001) continue;
                var t=Math.Max(0,Math.Min(1,((start.x-a.x)*dx+(start.y-a.y)*dy+(start.z-a.z)*dz)/length));
                var point=new Vector3((float)(a.x+t*dx),(float)(a.y+t*dy),(float)(a.z+t*dz));
                var distance=Vector3.Distance(start,point);
                if(distance<best && ((t>0&&t<1)||distance<0.25f)) { best=distance; segment=i; }
            }
            if(segment<0) return null;
            var result=new List<Vector3>{start}; result.AddRange(route.Skip(segment+1));
            returnPosition=position-segment;
            return result;
        }
        internal void Describe(Component truck, TugObservation row)
        {
            if (job != null)
            {
                row.ReturnReason = ReferenceEquals(job.Truck, truck) ? (job.Returning ? "Returning to stand" : "Tug recalled; waiting for coupling") : "Another return operation is active.";
                row.ReturnActive = ReferenceEquals(job.Truck, truck); return;
            }
            var candidate = Candidate(truck);
            row.ReturnCallsign = candidate ? Get<string>(candidate, "_name") : "-";
            var facts=Facts(truck,candidate);
            row.ReturnReason = ReturnPolicy.Refusal(facts); row.CanReturn = row.ReturnReason == null;
            if(TS3Modding.Preferences.DeveloperMode.Enabled && candidate && row.ReturnReason!=null && facts.ReadyToTaxi && !facts.PushInProgress)
            {
                var key=row.ReturnCallsign+":"+row.ReturnReason;
                if(reportedRoutes.Count>256) reportedRoutes.Clear();
                if(reportedRoutes.Add(key)) context.Log("[Tugs] Return route rejected for "+row.ReturnCallsign+
                    ": points="+facts.RouteCount+", index="+facts.RoutePosition+", standOnRoute="+facts.RouteMatchesOrigin+
                    ", routerBusy="+facts.RouterBusy+", holdsChanged="+facts.HasExplicitHold+
                    ", pendingCalculation="+Get<bool>(candidate,"_need_roadcalculator")+
                    ", holdCount="+((IList)Get(candidate,"_holdshort")).Count+
                    ", verifiedEndpoint="+facts.VerifiedPushbackEnd+", standDistance="+facts.StandDistance+
                    ", tugDistance="+facts.TugDistance+", firstReturnDistance="+facts.FirstReturnPointDistance+
                    ", pathLength="+facts.ReturnPathLength+", reason="+row.ReturnReason);
            }
        }
        internal bool Request(Component truck)
        {
            if (job != null) { Message = "Another return operation is already active."; return false; }
            var aircraft = Candidate(truck); var reason = ReturnPolicy.Refusal(Facts(truck, aircraft));
            if (reason != null) { Message = reason; return false; }
            var route = Get<List<Vector3>>(aircraft, "_route"); var stand = Get(truck, "_terminal");
            int returnPosition;
            var returnRoute=ReturnRoute(route,Get<int>(aircraft,"_routepos"),Get<Vector3>(aircraft,"_terminal_start_pos"),out returnPosition);
            if(VerifiedEnd(aircraft)) returnRoute=EndpointRoute(returnRoute,returnPosition,Get<Vector3>(aircraft,"_pos"),out returnPosition);
            var pending = new Job { Truck = truck, Aircraft = aircraft, Stand = stand, Gate = Get(truck, "_gate"),
                OriginalLock = Get(stand, "locked"), Schedule = Get(aircraft, "_timetable"), Callsign = Get<string>(aircraft, "_name"),
                OriginalRoute = route, RoutePoints = route.ToArray(), RoutePosition = Get<int>(aircraft, "_routepos"),
                ReturnRoute=returnRoute, ReturnRoutePosition=returnPosition,
                StartPosition = Get<Vector3>(aircraft, "_pos"), StandPosition = Get<Vector3>(aircraft, "_terminal_start_pos"),
                OldNeedCalculation=Get<bool>(aircraft,"_need_roadcalculator"),
                OldHold = Get<bool>(aircraft, "_hold_position_taxistop"), OldWait = Get<bool>(aircraft, "wait_for_trailer") };
            job = pending;
            try
            {
                Set(stand, "occupied", true); Set(stand, "locked", aircraft);
                if(pending.OldNeedCalculation) Set(aircraft,"_need_roadcalculator",false);
                Set(aircraft, "_hold_position_taxistop", true); Set(aircraft, "wait_for_trailer", true);
                // Native home travel can retain _car_linked until reaching home. That stale flag is not a new coupling.
                if (Get<bool>(truck, "home")) Set(truck, "_car_linked", false);
                setPlane.Invoke(truck, new object[] { aircraft }); Set(truck, "home", false); Set(truck, "sleep", false);
                Message = "Tug recalled for " + pending.Callsign + "; aircraft held until coupled.";
                context.Log("[Tugs] " + Message);
            }
            catch { Cancel("Recall failed"); throw; }
            return true;
        }
        private bool SameFlight(Job j) => j.Truck && j.Aircraft && !Get<bool>(j.Aircraft, "_deleted") &&
            ReferenceEquals(Get(j.Aircraft, "_timetable"), j.Schedule) && Get<string>(j.Aircraft, "_name") == j.Callsign;
        private bool OriginalRouteIntact(Job j) => ReferenceEquals(Get(j.Aircraft, "_route"), j.OriginalRoute) &&
            j.OriginalRoute.SequenceEqual(j.RoutePoints) && Get<int>(j.Aircraft, "_routepos") == j.RoutePosition &&
            Vector3.Distance(Get<Vector3>(j.Aircraft, "_pos"), j.StartPosition) < 0.5f;
        internal void Tick()
        {
            var j = job; if (j == null) return;
            try
            {
                if (!SameFlight(j) || !SinglePlayer || !ReferenceEquals(Get(j.Truck, "_plane"), j.Aircraft) ||
                    (Get(j.Gate, "plane") != null && !ReferenceEquals(Get(j.Gate, "plane"), j.Aircraft)) || !ReferenceEquals(Get(j.Aircraft, "_oldterminal"), j.Stand))
                { Cancel("Aircraft, tug or stand association changed"); return; }
                var other = ((IEnumerable)Get(Game, "airplanes")).Cast<Component>().Any(p => p && !ReferenceEquals(p, j.Aircraft) &&
                    !Get<bool>(p, "_deleted") && ReferenceEquals(Get(p, "_terminal"), j.Stand));
                if (other) { Cancel("Stand was assigned to another aircraft"); return; }
                var state = Convert.ToInt32(Get(j.Aircraft, "_state"));
                if (j.Returning && state == 8 && Get<bool>(j.Aircraft, "_imatterminal") && ReferenceEquals(Get(j.Aircraft, "_terminal"), j.Stand) &&
                    Vector3.Distance(Get<Vector3>(j.Aircraft, "_pos"), j.StandPosition) < 10f)
                {
                    Set(j.Stand, "occupied", true); Set(j.Aircraft, "wait_for_trailer", false);
                    // Let native FixedUpdate rebuild the gate association now that the aircraft is parked.
                    Set(j.Aircraft, "_ikgate", null); Set(j.Aircraft, "_set_term_gate", true);
                    Set(j.Aircraft, "_ready_to_taxi", false); Detach(j);
                    // TO_RUNWAY only generates a new pushback prefix when this marker is negative.
                    // Pull-back reused the previous route; WAITING does not reset its old endpoint.
                    Set(j.Aircraft, "_trailingend", -1); Set(j.Aircraft, "_trailing", false);
                    // SetGatePlane resets gate state only when the aircraft reference changes.
                    // Release our stale reference so native FixedUpdate can perform a fresh association.
                    if (ReferenceEquals(Get(j.Gate, "plane"), j.Aircraft)) Set(j.Gate, "plane", null);
                    var manager=gateManagerInstance.GetValue(null);
                    if(manager==null)throw new InvalidOperationException("Native gate manager unavailable after return.");
                    var gate=setGatePlane.Invoke(manager,new object[]{Get<string>(j.Stand,"name"),j.Aircraft});
                    if(!ReferenceEquals(gate,j.Gate))throw new InvalidOperationException("Original gate could not be reassigned after return.");
                    Set(j.Aircraft,"_ikgate",gate);
                    // Do not depend on another IKGate/FixedUpdate tick before a second approval.
                    // Native gate processing can clear its association as soon as WaitForStart runs.
                    setPlane.Invoke(j.Truck,new object[]{j.Aircraft});
                    Set(j.Truck,"home",false);Set(j.Truck,"sleep",false);
                    Set(j.Aircraft,"wait_for_trailer",true);
                    completedPushbacks.Remove(j.Aircraft);
                    job=null;
                    try{SuccessfulReturn?.Invoke(j.Aircraft);}catch(Exception error){context.Log("[Tugs] Return completion reward failed: "+error.Message);}
                    Message = j.Callsign + " returned to stand " + Get<string>(j.Stand, "name") + ".";
                    context.Log("[Tugs] " + Message);
                    context.Log("[Tugs] Reassigned original tug for next pushback: "+j.Callsign+"; waiting for native coupling; endpoint="+Get<int>(j.Aircraft,"_trailingend")+"; gateState="+Get(j.Gate,"state"));
                    waitForStart.Invoke(j.Aircraft, null); return;
                }
                if (!ReferenceEquals(Get(j.Stand, "locked"), j.Aircraft)) { Cancel("Stand reservation changed"); return; }
                if (Get<float>(Game, "speedup") > 0) j.Elapsed += Time.unscaledDeltaTime;
                if (j.Elapsed > 180f) { Cancel("Return operation timed out; aircraft held"); return; }
                if (!j.Returning)
                {
                    if (!OriginalRouteIntact(j)) { Cancel("Pushback route changed during recall"); return; }
                    Set(j.Aircraft, "_hold_position_taxistop", true); Set(j.Aircraft, "wait_for_trailer", true);
                    if (!Get<bool>(j.Truck, "_car_linked")) return;
                    // The native method subtracts two from routepos, then reverses that prefix in SetState.
                    Set(j.Aircraft, "wait_for_trailer", false);
                    Set(j.Aircraft,"_route",j.ReturnRoute); Set(j.Aircraft,"_routepos",j.ReturnRoutePosition);
                    pullBack.Invoke(j.Aircraft, null);
                    if (Convert.ToInt32(Get(j.Aircraft, "_state")) != 10) { Cancel("Native pull-back transition was rejected"); return; }
                    j.Returning = true; j.Elapsed = 0;
                    Message = "Returning " + j.Callsign + " to stand " + Get<string>(j.Stand, "name") + ".";
                    context.Log("[Tugs] " + Message); return;
                }
                if (state == 10) return;
                Cancel("Native return ended before reaching the stand");
            }
            catch (Exception error) { Cancel("Return error: " + error.Message); }
        }
        internal void Cancel(string reason)
        {
            var j = job; if (j == null) return;
            job = null;
            try
            {
                if (SameFlight(j))
                {
                    Set(j.Aircraft, "wait_for_trailer", false);
                    if (j.Returning || !OriginalRouteIntact(j))
                    {
                        Set(j.Aircraft, "_hold_position_taxistop", true);
                        if (Convert.ToInt32(Get(j.Aircraft, "_state")) == 10) setState.Invoke(j.Aircraft, new[] { Enum.ToObject(F(planeType, "_state").FieldType, 8) });
                        if (Vector3.Distance(Get<Vector3>(j.Aircraft, "_pos"), j.StandPosition) >= 10f)
                        {
                            Set(j.Aircraft, "_imatterminal", false);
                            if (ReferenceEquals(Get(j.Aircraft, "_terminal"), j.Stand)) Set(j.Aircraft, "_terminal", null);
                        }
                    }
                    else { Set(j.Aircraft, "_hold_position_taxistop", j.OldHold); Set(j.Aircraft, "wait_for_trailer", j.OldWait);
                        if(j.OldNeedCalculation) Set(j.Aircraft,"_need_roadcalculator",true); }
                }
                if (ReferenceEquals(Get(j.Stand, "locked"), j.Aircraft))
                {
                    var present = SameFlight(j) && ReferenceEquals(Get(j.Aircraft, "_terminal"), j.Stand) && Get<bool>(j.Aircraft, "_imatterminal");
                    var otherPresent = Game != null && ((IEnumerable)Get(Game, "airplanes")).Cast<Component>().Any(p => p &&
                        !Get<bool>(p, "_deleted") && ReferenceEquals(Get(p, "_terminal"), j.Stand));
                    // The same physical object can already represent a new flight at this stand.
                    if (!j.Aircraft || !ReferenceEquals(Get(j.Aircraft, "_terminal"), j.Stand)) Set(j.Stand, "locked", j.OriginalLock);
                    Set(j.Stand, "occupied", present || otherPresent);
                }
                if (j.Truck && SameFlight(j) && ReferenceEquals(Get(j.Truck, "_plane"), j.Aircraft)) Detach(j);
            }
            catch (Exception error) { context.Log("[Tugs] Cleanup error: " + error); }
            Message = reason + "."; context.Log("[Tugs] " + Message);
        }
        private void Detach(Job j)
        {
            Set(j.Truck, "need_ready", false); Set(j.Truck, "_car_linked", false); Set(j.Truck, "_link", null);
            Set(j.Truck, "_plane", null); Set(j.Truck, "holdtimer", 0f); Set(j.Truck, "home", true); Set(j.Truck, "sleep", false);
        }
        private static IEnumerable<CodeInstruction> HomeTranspiler(IEnumerable<CodeInstruction> instructions) =>
            TugHomeInstrumentation.Inject(instructions, current.F(current.truckType, "home"),
                typeof(NativeReturnController).GetMethod(nameof(DecideHome), BindingFlags.Static | BindingFlags.Public));
        public static bool DecideHome(object truck, bool nativeHome)
        {
            var self = current; var j = self?.job;
            if (j == null || j.Returning || !ReferenceEquals(j.Truck, truck)) return nativeHome;
            try { return self.SameFlight(j) && self.SinglePlayer && ReferenceEquals(self.Get(j.Truck, "_plane"), j.Aircraft) ? false : nativeHome; }
            catch { return nativeHome; }
        }
        private static void SavePrefix()
        {
            // End temporary reservations and recall overrides before native save capture.
            // A moving return is stopped on the apron; it is not serialized as a resumable mod job.
            current?.Cancel("Return cancelled before saving; aircraft remains at its current location");
        }
        private static void CommandPrefix(object __instance)
        {
            if(__instance is Component plane) current?.completedPushbacks.Remove(plane);
            if (current?.job != null && ReferenceEquals(current.job.Aircraft, __instance)) current.Cancel("Return cancelled by a new aircraft instruction");
        }
        public void Dispose()
        {
            Cancel("Return controller stopped"); harmony.UnpatchSelf();
            if (ReferenceEquals(current, this)) current = null;
        }
    }
}
