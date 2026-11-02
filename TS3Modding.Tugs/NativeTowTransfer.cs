using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace TS3Modding.Tugs
{
    // One owned, single-player transfer. Aircraft translation/steering stays native.
    internal sealed partial class NativeTowTransfer:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private readonly Dictionary<string,FieldInfo> fields=new Dictionary<string,FieldInfo>();
        private readonly Harmony harmony=new Harmony("nl.ts3modding.tugs.transfer");
        private readonly Type planeType,gameType,managerType;private readonly Assembly assembly;
        private readonly MethodInfo taxi,brake,pushbackController,setPlane,setGate,getGate,waitForStart,setState,getKnot,multiplayer;
        private readonly Action<string> log;private readonly Action<Component,string> reply;
        private static NativeTowTransfer current;
        private Component nativeSteering;
        private readonly MethodInfo muteSound;
        private Job job;private readonly List<Job> jobs=new List<Job>();
        private readonly HashSet<Job> deferredReplans=new HashSet<Job>(),supersededReplans=new HashSet<Job>();
        internal int MaximumTransfers=8;
        internal Component[] AircraftList=>jobs.Select(j=>j.Plane).ToArray();
        internal bool CanCancelBeforeDeparture(Component plane){var j=jobs.FirstOrDefault(v=>ReferenceEquals(v.Plane,plane));return j!=null&&!j.Moving&&Vector3.Distance(Get<Vector3>(plane,"_pos"),j.Start)<=.5f;}
        internal Vector3[] RouteForPreview(Component plane){var j=jobs.FirstOrDefault(v=>ReferenceEquals(v.Plane,plane));return j!=null&&!j.Moving?j.Route.ToArray():null;}
        internal bool Owns(Component plane)=>quarantined.Contains(plane)||jobs.Any(j=>ReferenceEquals(j.Plane,plane));
        internal string DestinationAliasFor(Component plane)=>jobs.FirstOrDefault(j=>ReferenceEquals(j.Plane,plane)&&Same(j))?.DestinationAlias;
        internal string DestinationFor(Component plane)=>jobs.FirstOrDefault(j=>ReferenceEquals(j.Plane,plane))?.Destination;
        private sealed class Job
        {
            internal Component Plane,Truck;internal object Game,Schedule,Source,Target,SourceGate,TargetGate,TargetTruck;
            internal string Callsign,Destination,DestinationAlias;internal string[] Via=new string[0];internal Vector3 Start,End;internal List<Vector3> Route,OriginalRoute;internal int OriginalRouteIndex;
            internal bool OldHold,OldWait,OldNeed,OldReady,OldRequest,OldSetGate,Moving,Held;
            internal object OldGate;internal float Elapsed;
            internal object SourceLock;internal bool SourceOccupied;internal int CheckedCount;
            internal TowRoadSnapshot[] Roads;
            internal float EndHeading,NextLog;internal double MovementTimeout;
            internal bool AnnouncedMoving;
            internal bool ParkingBraking;internal TowRoadSnapshot HoldRoad;internal double HoldDistance,RouteProgress;internal int HoldRouteCount;internal bool HoldReached,HoldReported;
            internal object Sound;internal bool DesiredMute;internal readonly List<object> AddedCrossings=new List<object>();internal int NativeClearEvent=-1;
            internal object[] NativeRoads;internal int TargetRoad,TargetIndex;internal Vector3 RouteStart;internal NativeTowRouteCheck Replan;internal bool ReplanPending,ResumeAfterReplan;internal string[] ReplanVia;internal string ReplanHold,ReplanCross;
            internal TowRunwayCrossings Crossings;internal double Footprint,CrossingProgress,CrossingTravel;internal Vector3[] CheckedRoute;internal Vector3 LastCrossingPosition;internal string Fault;
            internal float StopSince=-1;internal Vector3 StopPosition;internal string StopReportKey;
            internal bool IncidentRecovery;internal Action RecoveryCompleted;internal double CouplingTimeout=180;
        }
        internal bool Busy=>jobs.Count!=0;
        internal string Message{get;private set;}="No stand transfer active.";
        internal Component Aircraft=>jobs.FirstOrDefault()?.Plane;
        internal string Destination=>jobs.FirstOrDefault()?.Destination;
        internal Action<Component,Vector3[],string> CaptureFailure;
        internal Action BeforeSave;
        internal Action<Component> BeforeCommand;
        internal Func<bool> AllowSave;
        internal Func<Component,bool> AllowCommand,IsTowOnly;
        internal Func<Component,bool> ExternalWaiting;
        internal bool IsHeld(Component plane)=>jobs.Any(j=>ReferenceEquals(j.Plane,plane)&&(j.Held||j.Fault!=null));
        internal Action<Component> TowOnlyParked,MaintenanceParked,SuccessfulTow;
        private FieldInfo F(Type type,string name)
        {var key=type.FullName+"."+name;FieldInfo f;if(!fields.TryGetValue(key,out f))fields.Add(key,f=type.GetField(name,Flags)??throw new MissingFieldException(type.FullName,name));return f;}
        private object Get(object o,string name)=>F(o.GetType(),name).GetValue(o);
        private T Get<T>(object o,string name)=>(T)Get(o,name);
        private void Set(object o,string name,object value){var f=F(o.GetType(),name);f.SetValue(o,f.FieldType.IsEnum?Enum.ToObject(f.FieldType,value):value);}
        internal NativeTowTransfer(Assembly assembly,Action<string> log,Action<Component,string> reply,bool patch=true)
        {
            this.assembly=assembly;this.log=log;this.reply=reply;planeType=assembly.GetType("Airplane",true);gameType=assembly.GetType("Game",true);managerType=assembly.GetType("IKGateManager",true);
            var truck=assembly.GetType("TowTruck",true);
            var gateFilter=planeType.GetMethod("MCJNHJMOKEF",Flags,null,new[]{assembly.GetType("Road",true).GetNestedType("Knots_t",BindingFlags.Public|BindingFlags.NonPublic)},null);
            if(gateFilter==null||gateFilter.ReturnType!=typeof(bool))throw new NotSupportedException("Native runway gate filter changed.");
            var sound=assembly.GetType("AirplaneSound",true);muteSound=sound.GetMethod("BFKIKIOGFDI",Flags,null,new[]{typeof(bool)},null);
            if(muteSound==null)throw new NotSupportedException("Native engine mute boundary changed.");
            taxi=planeType.GetMethod("AGHOOFEDHAC",Flags,null,Type.EmptyTypes,null);brake=planeType.GetMethod("PPPMPGFDJMM",Flags,null,new[]{typeof(float),typeof(bool)},null);
            pushbackController=planeType.GetMethod("PushBackController",Flags,null,Type.EmptyTypes,null);
            getKnot=planeType.GetMethod("GetGateKnot",Flags,null,new[]{assembly.GetType("IKGate",true)},null);
            setPlane=truck.GetMethod("SetPlane",Flags,null,new[]{planeType},null);waitForStart=planeType.GetMethod("WaitForStart",Flags,null,Type.EmptyTypes,null);
            setState=planeType.GetMethod("SetState",Flags,null,new[]{F(planeType,"_state").FieldType},null);
            setGate=managerType.GetMethod("SetGatePlane",Flags,null,new[]{typeof(string),planeType},null);getGate=managerType.GetMethod("getGateByTerminal",Flags,null,new[]{typeof(string)},null);
            multiplayer=assembly.GetType("TCP",true).GetMethod("get_mp_state",Flags,null,Type.EmptyTypes,null);
            if(new[]{taxi,brake,pushbackController,setPlane,waitForStart,setState,setGate,getGate,getKnot,multiplayer}.Any(m=>m==null))throw new NotSupportedException("Tow transfer signatures changed.");
            foreach(var name in new[]{"_acttime","_trgrunway","_takeoff","_lineup_and_wait","_beaconon","_navon","_navstrobeon","_landon","_crossrunways","_last_gate","_prevgate","_prev_holdshort","_crossrequest_timer","_active","_action","_routestart","_roadend","_secondarycoll","KJEOBOIKNPD","_trailingend","_trailing","_set_term_gate","_need_roadcalculator"})F(planeType,name);
            if(!patch)return;
            if(current!=null)throw new InvalidOperationException("Transfer controller already active.");current=this;
            try
            {
                harmony.Patch(gateFilter,prefix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(NativeGatePrefix)));
                harmony.Patch(planeType.GetMethod("GHINOHCGAAD",Flags,null,Type.EmptyTypes,null),prefix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(DrivePrefix)));
                harmony.Patch(truck.GetMethod("DMCOGALGKKC",Flags,null,new[]{typeof(float)},null),transpiler:new HarmonyMethod(typeof(NativeTowTransfer),nameof(HomeTranspiler)));
                harmony.Patch(muteSound,prefix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(MutePrefix)));
                harmony.Patch(planeType.GetMethods(Flags).Single(m=>m.Name=="RunCommand"),prefix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(CommandPrefix)));
                harmony.Patch(assembly.GetType("SavedGame",true).GetMethod("EJLLBCNLCGN",Flags,null,new[]{typeof(string)},null),prefix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(SavePrefix)),transpiler:new HarmonyMethod(typeof(NativeTowTransfer),nameof(SaveTranspiler)),finalizer:new HarmonyMethod(typeof(NativeTowTransfer),nameof(SaveFinished)));
                harmony.Patch(assembly.GetType("SavedGame",true).GetMethod("BNMANBKINED",Flags,null,new[]{typeof(string)},null),transpiler:new HarmonyMethod(typeof(NativeTowTransfer),nameof(LoadTranspiler)),postfix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(LoadedPostfix)));
                harmony.Patch(assembly.GetType("SavedGame",true).GetMethod("BHHHNCBJOAL",Flags,null,Type.EmptyTypes,null),prefix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(RestorePrefix)),postfix:new HarmonyMethod(typeof(NativeTowTransfer),nameof(RestorePostfix)));
            }
            catch{Dispose();throw;}
        }
        private object Manager=>F(managerType,"instance").GetValue(null);
        internal int ParkingIndex(Component aircraft,TowPlanningSession session,TowDestination target)
        {
            if(session.Roads.Single(r=>r.Id==target.Road).Type!=1)throw new InvalidOperationException("This first transfer version supports native stands. Remote parking remains a preview.");
            var gate=getGate.Invoke(Manager,new object[]{target.Name});
            if(gate==null||Get(gate,"truck")==null)throw new InvalidOperationException("Destination has no compatible native gate/tug association.");
            if(Get(Get(aircraft,"ac"),"front_left_door")==null)throw new InvalidOperationException("Aircraft has no front-door marker for native stand alignment.");
            // Execution preparation only, on the Unity thread, with no yield or native update between.
            // GetGateKnot reads _terminal internally even when passed a destination gate.
            var original=Get(aircraft,"_terminal");
            try{Set(aircraft,"_terminal",session.NativeRoads[target.Road]);return (int)getKnot.Invoke(aircraft,new[]{gate});}
            finally{Set(aircraft,"_terminal",original);}
        }
        internal void Start(Component truck,NativeTowRouteCheck.CheckedTowRoute plan,Action recoveryCompleted=null)
        {
            var recovery=recoveryCompleted!=null;
            if(jobs.Count>=MaximumTransfers)throw new InvalidOperationException("The configured concurrent tow limit has been reached.");
            if(jobs.Any(j=>ReferenceEquals(j.Plane,plan.Session.Aircraft)||ReferenceEquals(j.Truck,truck)))throw new InvalidOperationException("Aircraft or tug already has an active transfer.");
            var s=plan.Session;var d=plan.Destination;var plane=s.Aircraft;
            if(Convert.ToInt32(multiplayer.Invoke(null,null))!=0||!ReferenceEquals(F(gameType,"instance").GetValue(null),s.Game)||(bool)Get(s.Game,"loading"))throw new InvalidOperationException("Load a single-player airport first.");
            if(!plane||!truck||Get<bool>(plane,"_deleted")||!Get<bool>(plane,"_active")||Convert.ToInt32(Get(plane,"_action"))!=2||!recovery&&(!Get<bool>(plane,"_imatterminal")||Convert.ToInt32(Get(plane,"_state"))!=8)||Math.Abs(Get<float>(plane,"_lv"))>.1||Get<float>(plane,"_alt")>1||!ReferenceEquals(Get(plane,"_timetable"),s.Schedule)||Get<string>(plane,"_name")!=s.Callsign)throw new InvalidOperationException("Aircraft is not eligible for tug coupling.");
            var source=s.NativeRoads[s.SourceRoad];var target=s.NativeRoads[d.Road];
            if(!recovery&&(!ReferenceEquals(Get(plane,"_terminal"),source)||!ReferenceEquals(Get(truck,"_terminal"),source))||Get<bool>(target,"occupied")||Get(target,"locked")!=null&&!(recovery&&ReferenceEquals(Get(target,"locked"),plane)))throw new InvalidOperationException("Source association or destination availability changed.");
            var sourceGate=Get(truck,"_gate");var targetGate=getGate.Invoke(Manager,new object[]{d.Name});
            if(!recovery&&Get(source,"locked")!=null&&!ReferenceEquals(Get(source,"locked"),plane)||sourceGate!=null&&Get(sourceGate,"plane")!=null&&!ReferenceEquals(Get(sourceGate,"plane"),plane))throw new InvalidOperationException("Source stand is owned by another aircraft.");
            if(sourceGate==null||targetGate==null||Get(targetGate,"plane")!=null||Get(targetGate,"truck")==null||!ReferenceEquals(Get(targetGate,"road"),target))throw new InvalidOperationException("Destination gate is unavailable.");
            var targetTruck=Get(targetGate,"truck");
            if(Get(targetTruck,"_plane")!=null||Get<bool>(targetTruck,"_car_linked"))throw new InvalidOperationException("Destination tug is already in use.");
            if(Get(truck,"_plane")!=null&&!ReferenceEquals(Get(truck,"_plane"),plane))throw new InvalidOperationException("Source tug is assigned elsewhere.");
            if(Convert.ToInt32(Get(Get(plane,"_type"),"_weight_class"))==0||Get(Get(plane,"ac"),"front_wheel")==null)throw new InvalidOperationException("Aircraft does not support native tug coupling.");
            if(Get(plane,"_router")!=null||((IList)Get(plane,"_holdshort")).Count!=0||Get(plane,"_target_intersection")!=null||Get(plane,"_target_knot")!=null)throw new InvalidOperationException("Finish pending routing or holding instructions first.");
            var issue=TowRouteSafety.Check(plan.Points.Select(p=>new TowPoint(p.x,p.y,p.z)).ToArray(),new TowPoint(Get<Vector3>(plane,"_pos").x,Get<Vector3>(plane,"_pos").y,Get<Vector3>(plane,"_pos").z),s.Roads.Single(r=>r.Id==d.Road).Points[d.Index],s.Roads,true);
            if(issue!=null)throw new InvalidOperationException(issue);
            var stop=s.Roads.Single(r=>r.Id==d.Road).Points[d.Index];
            var approach=TowParkingGeometry.Trim(plan.Points.Select(p=>new TowPoint(p.x,p.y,p.z)).ToArray(),stop).Select(p=>new Vector3(p.X,p.Y,p.Z)).ToList();
            var j=new Job{NativeRoads=s.NativeRoads,TargetRoad=d.Road,TargetIndex=d.Index,Roads=s.Roads,Game=s.Game,Plane=plane,Truck=truck,Schedule=s.Schedule,Callsign=s.Callsign,Destination=d.Name,DestinationAlias=TowStandAliases.For(s,d.Name).Alias(d.Name),Source=source,Target=target,SourceGate=sourceGate,TargetGate=targetGate,TargetTruck=targetTruck,
                Via=(plan.Via??new string[0]).ToArray(),Start=Get<Vector3>(plane,"_pos"),End=new Vector3(stop.X,stop.Y,stop.Z),Route=approach,OriginalRoute=Get<List<Vector3>>(plane,"_route"),OriginalRouteIndex=Get<int>(plane,"_routepos"),
                OldHold=Get<bool>(plane,"_hold_position_taxistop"),OldWait=Get<bool>(plane,"wait_for_trailer"),OldNeed=Get<bool>(plane,"_need_roadcalculator"),OldReady=Get<bool>(plane,"_ready_to_taxi"),OldRequest=Get<bool>(plane,"_requestingpush"),OldGate=Get(plane,"_ikgate"),OldSetGate=Get<bool>(plane,"_set_term_gate"),SourceLock=Get(source,"locked"),SourceOccupied=Get<bool>(source,"occupied")};
            var viaIssue=TowRouteInstructions.ValidateNamedVias(approach.Select(Point).ToArray(),s.Roads,plan.Via);if(viaIssue!=null)throw new InvalidOperationException(viaIssue);
            var dimensions=Get(plane,"_type");double bodyLength=Get<float>(dimensions,"_length"),bodyWidth=Get<float>(dimensions,"_width");
            j.IncidentRecovery=recovery;j.RecoveryCompleted=recoveryCompleted;
            if(recovery)j.CouplingTimeout=Math.Max(180,Math.Max(plan.ApproachLength,Vector3.Distance(truck.transform.position,j.Start))/1.5+120);
            if(double.IsNaN(bodyLength)||double.IsNaN(bodyWidth)||bodyLength<=0||bodyWidth<=0||bodyLength>300||bodyWidth>300)throw new InvalidOperationException("Aircraft protection dimensions are unavailable.");
            j.RouteStart=j.Start;
            j.Footprint=Math.Sqrt(bodyLength*bodyLength+bodyWidth*bodyWidth)+30;
            PrepareCrossings(j);
            if(plan.CrossRunway!=null)
            {
                ValidateRunwayClearance(j,plan.CrossRunway);
                j.Crossings.Clear(plan.CrossRunway,true);
            }
            if(plan.HoldShort!=null)PrepareHold(j,plan.HoldShort);
            job=j;
            j.Sound=Get(plane,"sound");if(j.Sound!=null)j.DesiredMute=Get<bool>(j.Sound,"OOHGEBHBBDD");
            var terminalGeometry=s.Roads.Single(r=>r.Id==d.Road).Points;
            if(d.Index<1){job=null;throw new InvalidOperationException("Destination lacks an alignment segment.");}
            var a=terminalGeometry[d.Index-1];var b=terminalGeometry[d.Index];
            j.MovementTimeout=Math.Max(1200,j.Route.Zip(j.Route.Skip(1),(p,q)=>(double)Vector3.Distance(p,q)).Sum()/1.5+300);
            if(recovery)j.CouplingTimeout=Math.Max(j.CouplingTimeout,j.Route.Zip(j.Route.Skip(1),(p,q)=>(double)Vector3.Distance(p,q)).Sum()/3+120);
            j.EndHeading=(float)(Math.Atan2(b.X-a.X,b.Z-a.Z)*180/Math.PI);
            try
            {
                jobs.Add(j);
                if(plan.CrossRunway!=null)GrantNativeCrossing(j);
                if(!recovery){Set(source,"occupied",true);Set(source,"locked",plane);}Set(target,"locked",plane);
                Set(plane,"_hold_position_taxistop",true);Set(plane,"wait_for_trailer",true);Set(plane,"_need_roadcalculator",false);Set(plane,"_ready_to_taxi",false);Set(plane,"_requestingpush",false);
                // Own the ground-state dispatch instead of invoking TO_RUNWAY's runway-dependent initializer.
                Set(plane,"_state",12);Set(plane,"_set_term_gate",false);
                if(recovery){Set(plane,"_trailing",false);Set(plane,"_pushback_start_delay",0f);if(j.Crossings.Occupying)GrantNativeCrossing(j);}
                if(Get<bool>(truck,"home"))Set(truck,"_car_linked",false);
                setPlane.Invoke(truck,new object[]{plane});Set(truck,"home",false);Set(truck,"sleep",false);
                Message="Coupling tug for "+j.Callsign+" → "+j.Destination+". Both stands reserved.";log("[Tugs] "+Message);
                if(plan.CrossRunway!=null)reply(plane,"Cleared to cross runway "+TowRunwayCrossings.Spoken(plan.CrossRunway));

            }
            catch{CancelOne("Transfer preparation failed");throw;}
        }
        private static TowPoint Point(Vector3 p)=>new TowPoint(p.x,p.y,p.z);
        private void PrepareCrossings(Job j)
        {
            var previous=j.Crossings;
            bool preserve=previous?.Cleared==true;
            if(previous!=null&&(previous.Event!=0||previous.Occupying||preserve&&(j.CrossingProgress>1||Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.Start)>1)))throw new InvalidOperationException("Tow route changed after runway clearance or crossing.");
            var route=j.Route.Select(Point).ToArray();
            var issue=TowRouteSafety.Check(route,Point(j.RouteStart),Point(j.End),j.Roads,true);if(issue!=null)throw new InvalidOperationException(issue);
            var candidate=new TowRunwayCrossings(route,j.Roads,j.Footprint,j.IncidentRecovery?Get<string>(j.Source,"name"):null);
            if(preserve)
            {
                if(!TowInitialRouteChange.Safe(j.CheckedRoute.Select(Point).ToArray(),route,previous.Events,candidate.Events))throw new InvalidOperationException("Tow route changed after runway clearance outside the permitted stand push-out adjustment.");
                candidate.Clear(previous.Name.Split('/')[0],true);
                log("[Tugs] Preserved advance crossing clearance across verified runway-free stand scaffold: "+j.Callsign+" points="+j.CheckedRoute.Length+" -> "+route.Length);
            }
            j.Crossings=candidate;j.CheckedRoute=j.Route.ToArray();j.CrossingProgress=0;j.CrossingTravel=25;j.LastCrossingPosition=Get<Vector3>(j.Plane,"_pos");
        }
        internal bool HasCrossingRoute(Component plane)=>jobs.Any(j=>ReferenceEquals(j.Plane,plane)&&j.Crossings.Events.Length>0);
        internal string RunwayStatus(Component plane)
        {
            var j=jobs.FirstOrDefault(v=>ReferenceEquals(v.Plane,plane));if(j==null)return null;
            if(j.Fault!=null)return "Tow held: "+j.Fault;
            if(j.ReplanPending)return deferredReplans.Contains(j)?"New route queued after the current runway crossing.":"Checking replacement tow route; aircraft held.";
            if(j.Crossings.Complete)return null;
            return (j.Crossings.Holding?"Holding short of runway ":j.Crossings.Occupying?"Crossing runway ":j.Crossings.Cleared?"Cleared to cross runway ":"Next runway: ")+j.Crossings.Name;
        }
        internal string WaitingRunway(Component plane)
        {
            var j=jobs.FirstOrDefault(v=>ReferenceEquals(v.Plane,plane));
            return j!=null&&j.Fault==null&&j.Crossings.Holding&&Math.Abs(Get<float>(plane,"_lv"))<.1?j.Crossings.Name:null;
        }
        internal void ClearRunway(Component plane,string runway)
        {
            var j=jobs.SingleOrDefault(v=>ReferenceEquals(v.Plane,plane));if(j==null)throw new InvalidOperationException("No active tow for this aircraft.");
            Validate(j);if(j.ReplanPending)throw new InvalidOperationException("Wait for the tow route check to finish.");if(j.Fault!=null)throw new InvalidOperationException(j.Fault);
            ValidateRunwayClearance(j,runway);
            j.Crossings.Clear(runway,Math.Abs(Get<float>(plane,"_lv"))<.1);GrantNativeCrossing(j);Set(plane,"_hold_position_taxistop",j.Held||j.HoldReached);
            reply(plane,"Cleared to cross runway "+TowRunwayCrossings.Spoken(runway));log("[Tugs] Crossing clearance "+j.Callsign+" runway="+j.Crossings.Name+" event="+j.Crossings.Event);
        }
        private void ValidateRunwayClearance(Job j,string runway,TowRunwayCrossings candidate=null)
        {
            var crossings=candidate??j.Crossings;
            if(crossings.Complete||crossings.Occupying||!TowRunwayCrossings.Matches(crossings.Name,runway))throw new InvalidOperationException("Runway does not match this tow's next available crossing.");
            // Clearance belongs to the addressed tow/encounter. Other tows may
            // also be cleared by the controller; native collision stops still apply.
        }
        internal void HoldRunway(Component plane,string runway=null)
        {
            var j=jobs.SingleOrDefault(v=>ReferenceEquals(v.Plane,plane));if(j==null)throw new InvalidOperationException("No active tow for this aircraft.");
            Validate(j);if(runway!=null&&!TowRunwayCrossings.Matches(j.Crossings.Name,runway))throw new InvalidOperationException("Runway does not match this tow's next crossing.");
            if(j.Crossings.Complete)throw new InvalidOperationException("No runway crossing remains.");
            j.Crossings.Revoke();RemoveNativeCrossings(j);reply(plane,"Will hold short of runway "+TowRunwayCrossings.Spoken(j.Crossings.Name??throw new InvalidOperationException("No runway crossing remains.")));
        }
        private void RemoveNativeCrossings(Job j)
        {
            if(Same(j)){var list=Get(j.Plane,"_crossrunways") as IList;if(list!=null)foreach(var road in j.AddedCrossings)list.Remove(road);}
            j.AddedCrossings.Clear();j.NativeClearEvent=-1;
        }
        private void GrantNativeCrossing(Job j)
        {
            var list=Get(j.Plane,"_crossrunways") as IList;if(list==null)throw new InvalidOperationException("Native crossing permission list unavailable.");
            var name=j.Crossings.Name;
            foreach(var r in j.Roads.Where(r=>r.Type==2&&TowRunwayCrossings.Key(r.Name)==name))
            {var road=j.NativeRoads[r.Id];if(!list.Contains(road)){list.Add(road);j.AddedCrossings.Add(road);}}
            j.NativeClearEvent=j.Crossings.Event;
            var gate=Get(j.Plane,"_prevgate");var gateRoads=gate==null?null:Get(gate,"gateroads") as IList;
            var gateRunways=gateRoads==null?new object[0]:gateRoads.Cast<object>().Where(r=>r!=null&&Convert.ToInt32(Get(r,"type"))==2).ToArray();
            var matchesGate=gateRunways.Length>0&&gateRunways.All(r=>TowRunwayCrossings.Key(Get<string>(r,"name"))==name);
            var hold=Get(j.Plane,"_prev_holdshort");var heldRoad=hold==null?null:Get(hold,"cross");
            var matchesCross=heldRoad!=null&&Convert.ToInt32(Get(heldRoad,"type"))==2&&TowRunwayCrossings.Key(Get<string>(heldRoad,"name"))==name;
            if(matchesGate)Set(j.Plane,"_gate_taxistop",false);
            if(matchesGate||matchesCross){Set(j.Plane,"_escape_needstop",false);Set(j.Plane,"_crossrequest_timer",0f);}
        }
        private static bool NativeGatePrefix(Component __instance,ref bool __result)
        {
            // Called only during our owned native steering invocation, after the
            // complete tow envelope, controller holds and collisions were checked.
            // The aircraft gate scanner cannot distinguish an exit from an entry.
            if(current==null||!ReferenceEquals(current.nativeSteering,__instance))return true;
            __result=true;return false;
        }
        private bool CrossingBrake(Job j)
        {
            if(!j.Route.SequenceEqual(j.CheckedRoute))PrepareCrossings(j);
            if(j.NativeClearEvent>=0&&j.NativeClearEvent!=j.Crossings.Event)RemoveNativeCrossings(j);
            if(j.Crossings.Complete)return false;
            if(Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.Truck.transform.position)+15>j.Footprint)throw new InvalidOperationException("Coupled tug moved outside the checked protection footprint.");
            var position=Get<Vector3>(j.Plane,"_pos");
            var moved=Vector3.Distance(position,j.LastCrossingPosition);j.CrossingTravel+=moved;
            // Native push-out/turn prefixes can pass close to a later outgoing leg. A global
            // nearest point then jumps to that leg. Project only within physically reachable
            // arc length, using the native waypoint as a tie-breaker rather than skipping ahead.
            var maximum=Math.Min(j.CrossingProgress+moved+10,j.CrossingTravel+10);
            var tailOutside=j.Crossings.OutsideNext(Point(position),Point(j.Truck.transform.position));
            var path=j.Route.Select(Point).ToArray();var cursor=Get<int>(j.Plane,"_routepos");double progress;
            try{progress=TowRouteInstructions.ProgressNear(path,Point(position),Math.Max(0,j.CrossingProgress-2),maximum,cursor);}
            catch(InvalidOperationException)
            {
                var candidate=TowRouteInstructions.ProgressAtWaypoint(path,Point(position),cursor);
                if(!j.Crossings.CanReanchor(j.CrossingProgress,candidate,tailOutside))throw new InvalidOperationException("Native tow waypoint change would skip a runway protection boundary.");
                log("[Tugs] Reanchored native turn scaffold "+j.Callsign+" waypoint="+cursor+" progress="+j.CrossingProgress.ToString("0.0")+" -> "+candidate.ToString("0.0"));
                progress=candidate;j.CrossingTravel=Math.Max(j.CrossingTravel,candidate);
            }
            j.CrossingProgress=Math.Max(j.CrossingProgress,progress);j.LastCrossingPosition=position;
            if(j.Crossings.ReleaseOccupiedOrigin(j.CrossingProgress,Point(position),Point(j.Truck.transform.position)))log("[Tugs] Recovery combination physically clear of origin runway: "+j.Callsign);
            var stopped=j.Crossings.Update(j.CrossingProgress,Get<float>(j.Plane,"_lv"),Get<float>(j.Plane,"KJEOBOIKNPD"),tailOutside);
            if(j.NativeClearEvent>=0&&j.NativeClearEvent!=j.Crossings.Event)RemoveNativeCrossings(j);
            Set(j.Plane,"_hold_position_taxistop",stopped||j.Held||j.HoldReached);return stopped;
        }
        private void Fail(Job j,string message)
        {
            if(j.Fault==null)log("[Tugs] Tow failure "+j.Callsign+": "+message);
            if(j.Crossings!=null&&j.Crossings.Events.Length>0&&Same(j)&&ReferenceEquals(F(gameType,"instance").GetValue(null),j.Game))
            {if(j.Fault==null){j.Fault=message;log("[Tugs] Crossing tow held: "+message+"; callsign="+j.Callsign+"; waypoint="+Get<int>(j.Plane,"_routepos")+"; progress="+j.CrossingProgress+"; travel="+j.CrossingTravel+"; position="+Get<Vector3>(j.Plane,"_pos"));try{CaptureFailure?.Invoke(j.Plane,j.Route.ToArray(),message+"; waypoint="+Get<int>(j.Plane,"_routepos")+"; progress="+j.CrossingProgress+"; travel="+j.CrossingTravel+"; position="+Get<Vector3>(j.Plane,"_pos"));}catch(Exception captureError){log("[Tugs] Failure snapshot unavailable: "+captureError.Message);}reply(j.Plane,"Unable, tow held, check the tug panel");}Set(j.Plane,"_hold_position_taxistop",true);brake.Invoke(j.Plane,new object[]{Get<float>(j.Plane,"KJEOBOIKNPD"),true});return;}
            if(Same(j)&&ReferenceEquals(F(gameType,"instance").GetValue(null),j.Game)){j.Fault=message;j.Held=true;Set(j.Plane,"_hold_position_taxistop",true);brake.Invoke(j.Plane,new object[]{Get<float>(j.Plane,"KJEOBOIKNPD"),true});reply(j.Plane,"Unable, tow held, check the tug panel");return;}
            CancelOne(message);
        }
        internal void RequestReroute(Component plane,string destination,string[] via,string holdShort,string crossRunway=null)
        {
            var j=jobs.SingleOrDefault(v=>ReferenceEquals(v.Plane,plane));if(j==null)throw new InvalidOperationException("No active tow for this aircraft.");
            Validate(j);
            if(!string.Equals(destination,j.DestinationAlias,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("wrong stand, we are towing to stand "+TowVoiceIntent.Spoken(j.DestinationAlias));
            if(via==null||via.Length==0)throw new InvalidOperationException("Specify via taxiways to change a tow route.");
            foreach(var id in via)TowRouteInstructions.ResolveAll(j.Roads,id);if(holdShort!=null)TowRouteInstructions.ResolveAll(j.Roads,holdShort);
            if(j.Moving&&j.Fault==null)CrossingBrake(j);
            if(!j.ReplanPending)j.ResumeAfterReplan=!j.Held&&!j.HoldReached;
            if(j.Replan!=null){j.Replan.Cancel("Superseded by a newer tow route instruction.");supersededReplans.Add(j);}
            j.ReplanVia=via.ToArray();j.ReplanHold=holdShort;j.ReplanCross=crossRunway;j.ReplanPending=true;
            if(j.Crossings.Occupying)
            {
                deferredReplans.Add(j);
                reply(plane,"Route change received, will apply after crossing runway "+TowRunwayCrossings.Spoken(j.Crossings.Name));
                return;
            }
            deferredReplans.Remove(j);j.Held=true;
            Set(plane,"_hold_position_taxistop",true);
        }
        private void TickReplan(Job j)
        {
            try
            {
                if(deferredReplans.Contains(j))
                {
                    if(j.Crossings.Occupying)return;
                    deferredReplans.Remove(j);j.Held=true;Set(j.Plane,"_hold_position_taxistop",true);
                }
                if(supersededReplans.Contains(j))
                {
                    j.Replan?.Tick();if(j.Replan?.Busy==true)return;
                    j.Replan?.Dispose();j.Replan=null;supersededReplans.Remove(j);
                }
                if(j.Replan==null)
                {
                    if(Math.Abs(Get<float>(j.Plane,"_lv"))>=.1)return;
                    var position=Point(Get<Vector3>(j.Plane,"_pos"));
                    var preferred=Array.FindIndex(j.NativeRoads,r=>ReferenceEquals(r,Get(j.Plane,"_atroad")));
                    var snapshot=TowRerouteAnchor.Resolve(j.Roads,position,preferred);var id=snapshot.Id;
                    var index=Enumerable.Range(0,snapshot.Points.Length).OrderBy(i=>snapshot.Points[i].Distance(position)).First();
                    var session=new TowPlanningSession{Game=j.Game,Aircraft=j.Plane,Schedule=j.Schedule,Callsign=j.Callsign,NativeRoads=j.NativeRoads,Roads=j.Roads,SourceRoad=id,SourceIndex=index,Source=snapshot.Name};
                    var target=new TowDestination{Road=j.TargetRoad,Index=j.TargetIndex,Name=j.Destination};
                    j.Replan=new NativeTowRouteCheck(assembly,log);j.Replan.StartTow(session,target,j.ReplanVia,j.ReplanHold,j.Crossings.VacatedRunways);return;
                }
                j.Replan.Tick();if(j.Replan.Busy)return;
                var plan=j.Replan.Completed;if(plan==null)throw new InvalidOperationException(j.Replan.Message);
                var points=TowParkingGeometry.Trim(plan.Points.Select(Point).ToArray(),Point(j.End));
                var candidate=new Job{Plane=j.Plane,Roads=j.Roads,Route=points.Select(p=>new Vector3(p.X,p.Y,p.Z)).ToList()};
                var crossings=new TowRunwayCrossings(points,j.Roads,j.Footprint,vacatedRunways:j.Crossings.VacatedRunways);
                if(j.ReplanCross!=null){ValidateRunwayClearance(j,j.ReplanCross,crossings);crossings.Clear(j.ReplanCross,true);}
                if(j.ReplanHold!=null)PrepareHold(candidate,j.ReplanHold);
                // All checks completed before replacing the owned native route. Stand locks, tug,
                // flight identity and destination remain owned by the same job.
                RemoveNativeCrossings(j);
                j.Route=candidate.Route;j.RouteStart=Get<Vector3>(j.Plane,"_pos");j.Crossings=crossings;j.CheckedRoute=j.Route.ToArray();j.CrossingProgress=0;j.CrossingTravel=25;j.LastCrossingPosition=j.RouteStart;
                j.Via=j.ReplanVia;j.HoldRoad=candidate.HoldRoad;j.HoldDistance=candidate.HoldDistance;j.HoldRouteCount=candidate.HoldRouteCount;j.HoldReached=false;j.HoldReported=false;j.ParkingBraking=false;j.CheckedCount=j.Route.Count;
                j.Fault=null;j.Held=!j.ResumeAfterReplan;j.Elapsed=0;
                if(j.ReplanCross!=null)GrantNativeCrossing(j);
                if(j.Moving){Set(j.Plane,"_route",j.Route);Set(j.Plane,"_routepos",0);}Set(j.Plane,"_routestart",j.NativeRoads[plan.Session.SourceRoad]);Set(j.Plane,"_routestartidx",plan.Session.SourceIndex);Set(j.Plane,"_roadend",false);Set(j.Plane,"_trailing",false);Set(j.Plane,"_trailingend",-1);Set(j.Plane,"_need_roadcalculator",false);Set(j.Plane,"_hold_position_taxistop",j.Held);
                reply(j.Plane,"Tow route updated via taxiways "+string.Join(", ",j.Via.Select(TowVoiceIntent.Spoken)));log("[Tugs] Updated tow route "+j.Callsign+" -> "+j.Destination+"; old crossing permissions discarded.");
                if(j.ReplanCross!=null)reply(j.Plane,"Cleared to cross runway "+TowRunwayCrossings.Spoken(j.ReplanCross));j.ReplanCross=null;
                j.Replan.Dispose();j.Replan=null;j.ReplanPending=false;
            }
            catch(Exception error)
            {
                // A cancelled worker is retained until its own Cleanup finishes. Never abort it.
                j.Replan?.Cancel(error.Message);
                if(j.Replan?.Busy==true){j.Replan.Tick();if(j.Replan.Busy)return;}
                j.Replan?.Dispose();j.Replan=null;j.ReplanPending=false;j.Held=true;Set(j.Plane,"_hold_position_taxistop",true);
                log("[Tugs] Reroute failed for "+j.Callsign+": "+error);
                reply(j.Plane,"Unable to change tow route, previous route retained, holding position");
            }
        }
        private void PrepareHold(Job j,string identifier)
        {
            var type=Get(j.Plane,"_type");double length=Get<float>(type,"_length"),width=Get<float>(type,"_width");
            if(double.IsNaN(length)||double.IsNaN(width)||length<=0||width<=0||length>300||width>300)throw new InvalidOperationException("Aircraft dimensions are unavailable for a taxiway hold.");
            var route=j.Route.Select(Point).ToArray();var progress=TowRouteInstructions.Progress(route,Point(Get<Vector3>(j.Plane,"_pos")));
            // Whole footprint plus a conservative tug/braking allowance, in native world units.
            TowRoadSnapshot road;var hold=TowRouteInstructions.HoldNamed(route,j.Roads,identifier,Math.Sqrt(length*length+width*width)/2+20,progress,out road);
            j.RouteProgress=progress;j.HoldRoad=road;j.HoldDistance=hold.Hold;j.HoldRouteCount=j.Route.Count;j.HoldReached=false;j.HoldReported=false;
        }
        internal void HoldShort(Component plane,string identifier)
        {
            var j=jobs.SingleOrDefault(v=>ReferenceEquals(v.Plane,plane));if(j==null)throw new InvalidOperationException("No active tow for this aircraft.");
            Validate(j);PrepareHold(j,identifier);reply(plane,"Will hold short of taxiway "+TowVoiceIntent.Spoken(identifier));
        }
        private bool Same(Job j)=>j.Plane&&!Get<bool>(j.Plane,"_deleted")&&ReferenceEquals(Get(j.Plane,"_timetable"),j.Schedule)&&Get<string>(j.Plane,"_name")==j.Callsign;
        private void Validate(Job j)
        {
            if(!Same(j)||!j.Truck||!Get<bool>(j.Plane,"_active")||!ReferenceEquals(F(gameType,"instance").GetValue(null),j.Game)||Convert.ToInt32(multiplayer.Invoke(null,null))!=0||Get<bool>(j.Game,"loading")||!ReferenceEquals(Get(j.Truck,"_plane"),j.Plane))throw new InvalidOperationException("Airport, flight or tug association changed");
            if(!j.IncidentRecovery&&!ReferenceEquals(Get(j.Source,"locked"),j.Plane)||!ReferenceEquals(Get(j.Target,"locked"),j.Plane)||Get<bool>(j.Target,"occupied")||Get(j.TargetGate,"plane")!=null)throw new InvalidOperationException("Stand ownership changed");
            if(Convert.ToInt32(Get(j.Plane,"_state"))!=12)throw new InvalidOperationException("Aircraft state changed");
            if(j.Moving&&!ReferenceEquals(Get(j.Plane,"_route"),j.Route))throw new InvalidOperationException("Aircraft route changed");
        }
        private void ObserveStop(Job j)
        {
            if(!j.Moving)return;
            var position=Get<Vector3>(j.Plane,"_pos");var clock=Time.realtimeSinceStartup;
            if(j.StopSince<0||Vector3.Distance(position,j.StopPosition)>.25||Math.Abs(Get<float>(j.Plane,"_lv"))>.1)
            {j.StopSince=clock;j.StopPosition=position;j.StopReportKey=null;return;}
            if(clock-j.StopSince<3)return;
            try
            {
                var collision=Get(j.Plane,"_secondarycoll") as Component;
                var reason=j.Fault!=null?"safety fault":j.ReplanPending?"route calculation":j.Held?"controller tow hold":j.HoldReached?"taxiway hold":j.Crossings.Holding?"runway clearance "+j.Crossings.Name:collision?"collision with "+Get<string>(collision,"_name"):!Get<bool>(j.Truck,"_car_linked")?"uncoupled tug":j.ParkingBraking?"destination braking":"native steering stationary";
                var key=reason+"; event="+j.Crossings.Event;
                if(j.StopReportKey==key)return;j.StopReportKey=key;
                Func<string,string> gateRoads=field=>{var gate=Get(j.Plane,field);var roads=gate==null?null:Get(gate,"gateroads") as IList;return roads==null?"none":string.Join(",",roads.Cast<object>().Select(r=>Get<string>(r,"name")));};
                var hold=Get(j.Plane,"_prev_holdshort");var heldRoad=hold==null?null:Get(hold,"cross");
                var next=j.Crossings.Next;var index=Get<int>(j.Plane,"_routepos");
                var detail="stationary reason="+reason+"; position="+position+"; heading="+Get(j.Plane,"_rot")+"; waypoint="+index+"/"+j.Route.Count+"; target="+(index>=0&&index<j.Route.Count?j.Route[index].ToString():"outside route")+
                    "; progress="+j.CrossingProgress+"; crossing="+(next==null?"complete":next.Name+" entry="+next.Entry+" exit="+next.Exit+" hold="+next.Hold)+"; cleared="+j.Crossings.Cleared+"; occupying="+j.Crossings.Occupying+
                    "; nativeGate="+Get(j.Plane,"_gate_taxistop")+"; nativeEscape="+Get(j.Plane,"_escape_needstop")+"; nativeHold="+Get(j.Plane,"_hold_position_taxistop")+"; roadEnd="+Get(j.Plane,"_roadend")+"; gateRoads="+gateRoads("_prevgate")+"; holdRoad="+(heldRoad==null?"none":Get<string>(heldRoad,"name"));
                log("[Tugs] Stop snapshot "+j.Callsign+": "+detail);
                CaptureFailure?.Invoke(j.Plane,j.Route.ToArray(),detail);
            }
            catch(Exception error){log("[Tugs] Stop snapshot unavailable: "+error.Message);}
        }
        internal void Tick(){TickRestoration();foreach(var active in jobs.ToArray()){job=active;TickOne();}job=null;}
        private void TickOne()
        {
            var j=job;if(j==null||quarantined.Contains(j.Plane))return;
            try
            {
                if(!ReferenceEquals(F(gameType,"instance").GetValue(null),j.Game)){jobs.Remove(j);RestoreSound(j);return;}
                if(Get<bool>(j.Game,"loading"))return;
                Validate(j);if(Get<float>(j.Game,"speedup")<=0)return;
                if(j.ReplanPending)TickReplan(j);
                ObserveStop(j);
                if(j.Fault!=null){Message="Tow held: "+j.Fault;return;}
                if(!j.Held&&!j.HoldReached&&!j.Crossings.Holding&&ExternalWaiting?.Invoke(j.Plane)!=true)j.Elapsed+=Time.unscaledDeltaTime;
                if(j.Elapsed>=j.NextLog)
                {
                    j.NextLog=j.Elapsed+10;
                    log("[Tugs] Transfer "+j.Callsign+" moving="+j.Moving+" held="+j.Held+" coupled="+Get(j.Truck,"_car_linked")+" jetway="+Get(j.SourceGate,"state")+" route="+Get(j.Plane,"_routepos")+"/"+j.Route.Count+" speed="+Get(j.Plane,"_lv")+" roadEnd="+Get(j.Plane,"_roadend")+" nativeGateHold="+Get(j.Plane,"_gate_taxistop")+" trailingEnd="+Get(j.Plane,"_trailingend")+" startDelay="+Get(j.Plane,"_pushback_start_delay")+" waitingForTug="+Get(j.Plane,"wait_for_trailer"));
                }
                if(j.Elapsed>(j.Moving?j.MovementTimeout:j.CouplingTimeout))throw new InvalidOperationException("Tow operation timed out");
                if(!j.Moving)
                {
                    if(Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.Start)>.5f)throw new InvalidOperationException("Aircraft moved before coupling");
                    if(!Get<bool>(j.Truck,"_car_linked")||!j.IncidentRecovery&&Convert.ToInt32(Get(j.SourceGate,"state"))!=0)return;
                    Set(j.Plane,"_route",j.Route);Set(j.Plane,"_routepos",0);Set(j.Plane,"_routestart",j.Source);Set(j.Plane,"_trailingend",-1);Set(j.Plane,"_roadend",false);
                    Set(j.Plane,"_imatterminal",false);Set(j.Plane,"_terminal",null);Set(j.Plane,"_oldterminal",j.IncidentRecovery?null:j.Source);Set(j.Plane,"wait_for_trailer",false);Set(j.Plane,"_hold_position_taxistop",j.Held);
                    j.Moving=true;j.Elapsed=0;Message="Tug coupled for "+j.Callsign+"; waiting for native movement.";log("[Tugs] "+Message);
                }
                if(j.Moving&&!j.AnnouncedMoving&&(Math.Abs(Get<float>(j.Plane,"_lv"))>.1||Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.Start)>1))
                {j.AnnouncedMoving=true;}
                if(j.Crossings.Holding&&!j.Crossings.Reported&&Math.Abs(Get<float>(j.Plane,"_lv"))<.1)
                {j.Crossings.Reported=true;if(TS3Modding.API.PilotReportPreferences.CrossingReportsEnabled)reply(j.Plane,"Holding short of runway "+TowRunwayCrossings.Spoken(j.Crossings.Name)+", request crossing");}
                if(j.HoldReached&&!j.HoldReported&&Math.Abs(Get<float>(j.Plane,"_lv"))<.1)
                {j.HoldReported=true;reply(j.Plane,"Holding short of taxiway "+TowVoiceIntent.Spoken(j.HoldRoad.Name));}
                if(j.Moving&&j.ParkingBraking&&Math.Abs(Get<float>(j.Plane,"_lv"))<.1)
                {
                    if(Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.End)>2)throw new InvalidOperationException("Native movement ended outside destination stop tolerance");
                    var heading=Get<float>(j.Plane,"_rot");
                    if(float.IsNaN(heading)||Math.Abs(((heading-j.EndHeading+540)%360)-180)>20)throw new InvalidOperationException("Native movement stopped without destination stand alignment");
                    if(!j.Crossings.Complete)throw new InvalidOperationException("Tow cannot park before its complete combination clears every runway.");
                    Complete(j);
                }
                else if(j.Moving)Message=j.Crossings.Holding?"Holding short of runway "+j.Crossings.Name+" for "+j.Callsign+".":j.HoldReached?"Holding short of taxiway "+j.HoldRoad.Name+" for "+j.Callsign+".":j.Held?"Tow held for "+j.Callsign+".":Get<bool>(j.Plane,"_gate_taxistop")||Get<bool>(j.Plane,"_escape_needstop")?"Tow stopped by a native crossing/holding instruction. Cancel to use ordinary aircraft instructions.":!Get<bool>(j.Truck,"_car_linked")?"Tow stopped; waiting for native tug coupling.":!j.AnnouncedMoving?"Tug coupled; waiting for native start (delay "+Get<float>(j.Plane,"_pushback_start_delay").ToString("0.0")+" s).":"Towing "+j.Callsign+" to "+j.Destination+".";
            }
            catch(Exception error){Fail(j,error.Message);}
        }
        internal bool Drive(Component plane)
        {
            if(quarantined.Contains(plane)){brake.Invoke(plane,new object[]{Get<float>(plane,"KJEOBOIKNPD"),true});return false;}
            job=jobs.FirstOrDefault(j=>ReferenceEquals(j.Plane,plane));var j=job;if(j==null)return true;
            try
            {
                Validate(j);if(Get<float>(j.Game,"speedup")<=0)return false;
                if(j.Fault!=null){brake.Invoke(plane,new object[]{Get<float>(plane,"KJEOBOIKNPD"),true});return false;}
                if(j.Moving&&CrossingBrake(j)){brake.Invoke(plane,new object[]{Get<float>(plane,"KJEOBOIKNPD"),true});return false;}
                // TO_RUNWAY normally runs this before ground steering. Besides releasing the
                // old terminal it counts down _pushback_start_delay, which gates acceleration.
                if(j.Moving&&!j.IncidentRecovery)pushbackController.Invoke(plane,null);
                if(j.Moving&&j.HoldRoad!=null)
                {
                    if(j.HoldRouteCount!=j.Route.Count)PrepareHold(j,TowVoiceIntent.Identifier(j.HoldRoad.Name));
                    var progress=TowRouteInstructions.Progress(j.Route.Select(Point).ToArray(),Point(Get<Vector3>(plane,"_pos")),Math.Max(0,j.RouteProgress-2));j.RouteProgress=Math.Max(j.RouteProgress,progress);progress=j.RouteProgress;
                    if(j.HoldReached||progress>=j.HoldDistance-Math.Max(5,Math.Abs(Get<float>(plane,"_lv"))*Get<float>(plane,"KJEOBOIKNPD")+2))
                    {j.HoldReached=true;Set(plane,"_hold_position_taxistop",true);brake.Invoke(plane,new object[]{Get<float>(plane,"KJEOBOIKNPD"),true});return false;}
                }
                if(j.Moving)
                {
                    var distance=Vector3.Distance(Get<Vector3>(plane,"_pos"),j.End);
                    var angle=((Get<float>(plane,"_rot")-j.EndHeading+540)%360)-180;
                    var position=Get<Vector3>(plane,"_pos");var radians=j.EndHeading*Math.PI/180;
                    var remaining=(j.End.x-position.x)*Math.Sin(radians)+(j.End.z-position.z)*Math.Cos(radians);
                    var finalSegment=Get<int>(plane,"_routepos")>=j.Route.Count-3;
                    if(TowParkingGeometry.Brake(Get<float>(plane,"_lv"),Get<float>(plane,"KJEOBOIKNPD"),distance,angle,finalSegment)||finalSegment&&Get<float>(plane,"_lv")>=0&&remaining<=0)j.ParkingBraking=true;
                    if(j.ParkingBraking){brake.Invoke(plane,new object[]{Get<float>(plane,"KJEOBOIKNPD"),true});return false;}
                    // Native taxi steering raises roadend up to five units before its last point.
                    // Own the precise stand stop instead of accepting that loose taxi threshold.
                    if(distance<8&&Get<int>(plane,"_routepos")>=j.Route.Count-3)Set(plane,"_roadend",false);
                }
                var collision=Get(plane,"_secondarycoll") as UnityEngine.Object;
                if(!j.Moving||j.Held||!Get<bool>(j.Truck,"_car_linked")||collision)
                    brake.Invoke(plane,new object[]{Get<float>(plane,"KJEOBOIKNPD"),false});
                else
                {
                    // Suppress the ordinary push-complete handoff; this job retains the coupled tug.
                    Set(plane,"_requestingpush",false);
                    // This route's exact destination and all stop conditions are owned
                    // above; discard native departure-gate latches before steering.
                    Set(plane,"_gate_taxistop",false);Set(plane,"_escape_needstop",false);Set(plane,"_hold_position_taxistop",false);Set(plane,"_roadend",false);
                    var previousSteering=nativeSteering;var departureGate=Get(plane,"_last_gate");nativeSteering=plane;
                    // The native departure-destination stop bypasses its crossing
                    // gate filter. A managed tow is headed to a stand, not that
                    // retained takeoff gate; suppress it only for this steering call.
                    Set(plane,"_last_gate",null);
                    try{taxi.Invoke(plane,null);}
                    finally{nativeSteering=previousSteering;Set(plane,"_last_gate",departureGate);}
                    Set(plane,"_lv",Math.Max(-3f,Math.Min(3f,Get<float>(plane,"_lv"))));
                    if(j.CheckedCount!=j.Route.Count)
                    {
                        j.CheckedCount=j.Route.Count;
                        // The prepared route can acquire a native pushback-turn prefix on its first step.
                        // Keep the previously checked airport graph; no scan of a moving aircraft is needed.
                        var issue=TowRouteSafety.Check(j.Route.Select(p=>new TowPoint(p.x,p.y,p.z)).ToArray(),new TowPoint(j.RouteStart.x,j.RouteStart.y,j.RouteStart.z),new TowPoint(j.End.x,j.End.y,j.End.z),j.Roads,true);
                        if(issue!=null)throw new InvalidOperationException(issue);
                    }
                }
            }
            catch(Exception error){Fail(j,"Tow movement error: "+error.Message);}
            return false;
        }
        internal void Hold(bool held){foreach(var active in jobs.ToArray())Hold(active.Plane,held);}
        internal void Hold(Component plane,bool held){job=jobs.FirstOrDefault(j=>ReferenceEquals(j.Plane,plane));HoldOne(held);}
        private void HoldOne(bool held)
        {
            var j=job;if(j==null)return;if(quarantined.Contains(j.Plane)){reply(j.Plane,"Unable, saved tow restoration failed, check the tug panel and loader log");return;}if(j.ReplanPending){j.ResumeAfterReplan=!held;if(deferredReplans.Contains(j)){j.Held=held;Set(j.Plane,"_hold_position_taxistop",held);reply(j.Plane,held?"Holding tow":"Continuing runway crossing, route change queued");}else reply(j.Plane,"Tow held while replacement route is checked");return;}if(j.Fault!=null){reply(j.Plane,"Unable, tow is held after a safety failure");return;}if(!held&&j.HoldReached){j.HoldRoad=null;j.HoldReached=false;j.HoldReported=false;}j.Held=held;Set(j.Plane,"_hold_position_taxistop",held||!j.Moving||j.Crossings.Holding);
            Message=held?"Tow held for "+j.Callsign+".":"Tow continuing for "+j.Callsign+".";reply(j.Plane,held?"Holding tow":j.Crossings.Holding?"Unable, runway crossing clearance required":"Continuing tow");log("[Tugs] "+Message);
        }
        private void Detach(Job j)
        {
            if(!j.Truck||!ReferenceEquals(Get(j.Truck,"_plane"),j.Plane))return;
            Set(j.Truck,"_car_linked",false);Set(j.Truck,"_link",null);Set(j.Truck,"_plane",null);Set(j.Truck,"need_ready",false);Set(j.Truck,"holdtimer",0f);Set(j.Truck,"home",true);Set(j.Truck,"sleep",false);
        }
        private void Complete(Job j)
        {
            if(!jobs.Contains(j))return;
            Validate(j);var gate=setGate.Invoke(Manager,new object[]{j.Destination,j.Plane});
            if(!ReferenceEquals(gate,j.TargetGate))throw new InvalidOperationException("Destination gate reassociation failed");
            Set(j.Target,"occupied",true);Set(j.Plane,"_terminal",j.Target);Set(j.Plane,"_oldterminal",j.Target);Set(j.Plane,"_terminal_start_pos",Get(j.Plane,"_pos"));Set(j.Plane,"_imatterminal",true);
            Set(j.Plane,"_ikgate",gate);Set(j.Plane,"_set_term_gate",true);Set(j.Plane,"_trailing",false);Set(j.Plane,"_trailingend",-1);Set(j.Plane,"_routestart",null);Set(j.Plane,"_requestingpush",false);Set(j.Plane,"_ready_to_taxi",false);Set(j.Plane,"_need_roadcalculator",false);
            Set(j.Plane,"_route",new List<Vector3>());Set(j.Plane,"_routepos",0);Set(j.Plane,"_hold_position_taxistop",true);
            setState.Invoke(j.Plane,new[]{Enum.ToObject(F(planeType,"_state").FieldType,8)});
            Detach(j);if(!ReferenceEquals(j.SourceGate,j.TargetGate)&&ReferenceEquals(Get(j.SourceGate,"plane"),j.Plane))Set(j.SourceGate,"plane",null);
            if(!j.IncidentRecovery){Set(j.Source,"occupied",false);Set(j.Source,"locked",null);}
            var towOnly=IsTowOnly?.Invoke(j.Plane)==true;
            if(!towOnly&&!j.IncidentRecovery){setPlane.Invoke(j.TargetTruck,new object[]{j.Plane});Set(j.TargetTruck,"home",false);Set(j.TargetTruck,"sleep",false);Set(j.Plane,"wait_for_trailer",true);}
            else Set(j.Plane,"wait_for_trailer",false);
            jobs.Remove(j);job=null;if(!j.IncidentRecovery&&!(towOnly&&TS3Modding.API.AircraftMaintenanceServices.Contains(j.Plane)))RestoreSound(j);
            Message=j.Callsign+" parked at "+j.Destination+(j.IncidentRecovery?"; handed back to incident recovery.":". Flight and departure schedule preserved.");log("[Tugs] "+Message);
            try{SuccessfulTow?.Invoke(j.Plane);}catch(Exception error){log("[Tugs] Tow completion reward failed: "+error.Message);}
            if(j.IncidentRecovery){j.RecoveryCompleted?.Invoke();MaintenanceParked?.Invoke(j.Plane);}
            else{reply(j.Plane,"Tow complete at stand "+TowVoiceIntent.Spoken(j.DestinationAlias));if(towOnly)TowOnlyParked?.Invoke(j.Plane);else waitForStart.Invoke(j.Plane,null);}
        }
        internal void Cancel(string reason){foreach(var active in jobs.ToArray())Cancel(active.Plane,reason);job=null;}
        internal void Cancel(Component plane,string reason){job=jobs.FirstOrDefault(j=>ReferenceEquals(j.Plane,plane));CancelOne(reason);}
        private void CancelOne(string reason)
        {
            var j=job;if(j==null)return;deferredReplans.Remove(j);supersededReplans.Remove(j);RemoveNativeCrossings(j);j.Replan?.Dispose();j.Replan=null;jobs.Remove(j);job=null;
            RestoreSound(j);
            if(j.IncidentRecovery)
            {
                if(Same(j)){Set(j.Plane,"_hold_position_taxistop",true);Set(j.Plane,"_route",new List<Vector3>());Set(j.Plane,"_routepos",0);Set(j.Plane,"wait_for_trailer",false);Set(j.Plane,"_need_roadcalculator",false);Set(j.Plane,"_ready_to_taxi",false);Detach(j);}
                Message="Recovery tow stopped: "+reason;return;
            }
            try
            {
                if(Same(j))
                {
                    var departed=j.Moving&&(Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.Start)>1||Math.Abs(Get<float>(j.Plane,"_lv"))>.1);
                    if(!departed)
                    {
                        Set(j.Plane,"_terminal",j.Source);Set(j.Plane,"_imatterminal",true);
                        Set(j.Plane,"_route",j.OriginalRoute);Set(j.Plane,"_routepos",j.OriginalRouteIndex);Set(j.Plane,"_hold_position_taxistop",j.OldHold);Set(j.Plane,"wait_for_trailer",j.OldWait);
                        Set(j.Plane,"_need_roadcalculator",j.OldNeed);Set(j.Plane,"_ready_to_taxi",j.OldReady);Set(j.Plane,"_requestingpush",j.OldRequest);Set(j.Plane,"_ikgate",j.OldGate);Set(j.Plane,"_set_term_gate",j.OldSetGate);
                    }
                    else
                    {
                        Set(j.Plane,"_route",new List<Vector3>());Set(j.Plane,"_routepos",0);Set(j.Plane,"_hold_position_taxistop",true);Set(j.Plane,"wait_for_trailer",false);Set(j.Plane,"_imatterminal",false);Set(j.Plane,"_terminal",null);Set(j.Plane,"_ready_to_taxi",true);Set(j.Plane,"_requestingpush",false);Set(j.Plane,"_trailing",false);Set(j.Plane,"_trailingend",-1);Set(j.Plane,"_routestart",null);Set(j.Plane,"_need_roadcalculator",false);
                    }
                    setState.Invoke(j.Plane,new[]{Enum.ToObject(F(planeType,"_state").FieldType,8)});Detach(j);
                    if(!departed&&ReferenceEquals(F(gameType,"instance").GetValue(null),j.Game))
                    {
                        var gate=setGate.Invoke(Manager,new object[]{Get<string>(j.Source,"name"),j.Plane});
                        Set(j.Plane,"_ikgate",gate);Set(j.Plane,"_set_term_gate",true);
                        setPlane.Invoke(j.Truck,new object[]{j.Plane});Set(j.Truck,"home",false);Set(j.Truck,"sleep",false);Set(j.Plane,"wait_for_trailer",true);
                    }
                }
                if(ReferenceEquals(Get(j.Target,"locked"),j.Plane)&&!(j.Plane&&!Same(j)&&ReferenceEquals(Get(j.Plane,"_terminal"),j.Target)))Set(j.Target,"locked",null);
                if(ReferenceEquals(Get(j.Source,"locked"),j.Plane))
                {
                    var reusedAtSource=!Same(j)&&j.Plane&&ReferenceEquals(Get(j.Plane,"_terminal"),j.Source);
                    var parked=Same(j)&&ReferenceEquals(Get(j.Plane,"_terminal"),j.Source)&&Get<bool>(j.Plane,"_imatterminal");
                    if(!reusedAtSource){Set(j.Source,"locked",parked?j.SourceLock:null);Set(j.Source,"occupied",parked||Same(j)&&Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.Start)<8);}
                }
                Message="Tow cancelled: "+reason+(j.Moving?". Aircraft held at its current position.":".");log("[Tugs] "+Message);
                if(Same(j))reply(j.Plane,"Tow cancelled, holding position");
            }
            catch(Exception error){log("[Tugs] Transfer cleanup failed: "+error);}
        }
        private static bool DrivePrefix(object __instance)=>current?.Drive((Component)__instance)??true;
        private void RestoreSound(Job j){if(j.Sound!=null)try{muteSound.Invoke(j.Sound,new object[]{Same(j)&&TS3Modding.API.AircraftMaintenanceServices.Contains(j.Plane)?true:j.DesiredMute});}catch(Exception error){log("[Tugs] Engine mute restoration failed: "+error.Message);}}
        private static void MutePrefix(object __instance,ref bool __0)
        {var j=current?.jobs.FirstOrDefault(j=>ReferenceEquals(j.Sound,__instance));if(j==null)return;j.DesiredMute=__0;__0=true;}
        private static bool CommandPrefix(object __instance){if(current?.AllowCommand?.Invoke((Component)__instance)==false)return false;current?.BeforeCommand?.Invoke((Component)__instance);if(current?.HasCrossingRoute((Component)__instance)==true){current.reply((Component)__instance,"Unable, aircraft is being towed; use tow instructions");return false;}if(current?.Owns((Component)__instance)==true)current.Cancel((Component)__instance,"New aircraft instruction");return true;}
        private static IEnumerable<CodeInstruction> HomeTranspiler(IEnumerable<CodeInstruction> instructions)=>TugHomeInstrumentation.Inject(instructions,current.F(current.setPlane.DeclaringType,"home"),typeof(NativeTowTransfer).GetMethod(nameof(DecideHome),Flags));
        public static bool DecideHome(object truck,bool nativeHome)
        {
            var self=current;var j=self?.jobs.FirstOrDefault(j=>ReferenceEquals(j.Truck,truck));if(j==null)return self!=null&&self.quarantined.Contains(self.Get(truck,"_plane") as Component)?false:nativeHome;
            try{return self.Same(j)&&Convert.ToInt32(self.multiplayer.Invoke(null,null))==0&&ReferenceEquals(self.Get(truck,"_plane"),j.Plane)?false:nativeHome;}
            catch{return nativeHome;}
        }
        public void Dispose(){Cancel("Tug module stopped");harmony.UnpatchSelf();if(ReferenceEquals(current,this))current=null;}
    }
}
