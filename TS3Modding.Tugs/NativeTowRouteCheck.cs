using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
namespace TS3Modding.Tugs
{
    // Read-only native-planner probe. It never assigns Airplane._route/_router or reserves a road.
    // Native RouteCalculator owns its own thread and Cleanup. Never join or Abort on the UI thread.
    internal sealed class NativeTowRouteCheck : IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private readonly Type routerType,gameType;
        private readonly MethodInfo start,multiplayer;
        private readonly Action<string> log;
        private object router;
        private TowPlanningSession session;
        private TowDestination destination;
        private Vector3 startPosition;
        private object originalRoute,originalRouter;
        private readonly System.Collections.Generic.HashSet<int> attemptedSources=new System.Collections.Generic.HashSet<int>();
        private float started,requested;
        private string[] vacatedRunways;private bool towReplan;private bool cancelled;private TowRoadSnapshot[] viaRoads=new TowRoadSnapshot[0];private string holdShort;private TowRoadSnapshot[] plannerVias=new TowRoadSnapshot[0];
        internal bool Busy=>router!=null;
        internal Action<Component,Vector3[],string> CaptureFailure;
        internal CheckedTowRoute Completed{get;private set;}
        internal sealed class CheckedTowRoute {internal double ApproachLength;internal TowPlanningSession Session;internal TowDestination Destination;internal Vector3[] Points;internal string HoldShort,CrossRunway;internal string[] Via=new string[0];}
        internal string Message{get;private set;}="Select a route, then check it with the native planner.";
        private static FieldInfo F(Type t,string n)=>t.GetField(n,Flags)??throw new MissingFieldException(t.FullName,n);
        private static object Get(object o,string n)=>F(o.GetType(),n).GetValue(o);
        private static TowPoint Point(Vector3 p)=>new TowPoint(p.x,p.y,p.z);
        internal NativeTowRouteCheck(Assembly assembly,Action<string> log)
        {
            this.log=log;routerType=assembly.GetType("Router",true);gameType=assembly.GetType("Game",true);
            var road=assembly.GetType("Road",true);var plane=assembly.GetType("Airplane",true);
            start=routerType.GetMethod("StartRouterFromAP",Flags,null,new[]{road,typeof(int),road,typeof(int),plane,typeof(bool),typeof(bool)},null);
            multiplayer=assembly.GetType("TCP",true).GetMethod("get_mp_state",Flags,null,Type.EmptyTypes,null);
            foreach(var name in new[]{"thread","finished","_route","error","result","writein"})F(routerType,name);
            if(start==null||multiplayer==null)throw new NotSupportedException("Native route-check signatures changed.");
        }
        internal void Start(TowPlanningSession fresh,TowDestination target,string[] via=null,string holdShort=null)
        {vacatedRunways=null;towReplan=false;StartCore(fresh,target,via,holdShort);}
        internal void StartTow(TowPlanningSession fresh,TowDestination target,string[] via,string holdShort,string[] vacatedRunways=null)
        {this.vacatedRunways=vacatedRunways;towReplan=true;StartCore(fresh,target,via,holdShort);}
        private void StartCore(TowPlanningSession fresh,TowDestination target,string[] via,string holdShort)
        {
            if(Busy)throw new InvalidOperationException("Wait for the current native route check to finish.");
            Completed=null;
            if(Convert.ToInt32(multiplayer.Invoke(null,null))!=0)throw new InvalidOperationException("Tow checks are available in single-player sessions only.");
            if(via!=null&&(via.Length>8||via.Distinct().Count()!=via.Length))throw new InvalidOperationException("Use at most eight distinct via taxiways.");
            var nativePlan=TowNativeViaPlan.Create(fresh.Roads,fresh.SourceRoad,fresh.SourceIndex,target,via);viaRoads=nativePlan.Requested;plannerVias=nativePlan.Planner;
            if(holdShort!=null)TowRouteInstructions.ResolveAll(fresh.Roads,holdShort);this.holdShort=holdShort;
            session=fresh;destination=target;startPosition=(Vector3)Get(fresh.Aircraft,"_pos");cancelled=false;
            requested=Time.realtimeSinceStartup;attemptedSources.Clear();attemptedSources.Add(fresh.SourceRoad);
            originalRoute=Get(fresh.Aircraft,"_route");originalRouter=Get(fresh.Aircraft,"_router");
            Validate();
            StartWorker();
        }
        private void StartWorker()
        {
            var fresh=session;var target=destination;
            // Use the complete native taxi worker, with a private via list. The public
            // factory copies Airplane._via, which can contain a previous taxi instruction.
            // writein=false keeps calculation from publishing into the live aircraft.
            var roadType=fresh.NativeRoads[fresh.SourceRoad].GetType();
            var listType=typeof(List<>).MakeGenericType(roadType);var list=(IList)Activator.CreateInstance(listType);
            foreach(var road in plannerVias)list.Add(fresh.NativeRoads[road.Id]);
            var ctor=routerType.GetConstructor(Flags,null,new[]{roadType,typeof(int),roadType,typeof(int),typeof(Vector3),typeof(float),listType,typeof(bool),fresh.Aircraft.GetType(),typeof(bool),typeof(bool)},null);
            if(ctor==null)throw new NotSupportedException("Native ordered-via constructor changed.");
            var state=Convert.ToInt32(Get(fresh.Aircraft,"_state"));
            router=ctor.Invoke(new object[]{fresh.NativeRoads[fresh.SourceRoad],fresh.SourceIndex,fresh.NativeRoads[target.Road],target.Index,startPosition,(float)Get(fresh.Aircraft,"_rot"),list,state==11||state==15,fresh.Aircraft,false,false});
            var worker=routerType.GetMethod("RouteCalculator",Flags,null,Type.EmptyTypes,null);
            var thread=new Thread((ThreadStart)Delegate.CreateDelegate(typeof(ThreadStart),router,worker));
            F(routerType,"thread").SetValue(router,thread);thread.Start();
            if(router==null)throw new InvalidOperationException("Native planner did not create a route check.");
            started=Time.realtimeSinceStartup;Message="Checking "+fresh.Callsign+" → "+target.Name+" with TS3's planner… This check does not move aircraft.";
            log("[Tugs] "+Message+" source="+fresh.Source+"; nativeWorker=RouteCalculator"+"; requestedVia="+string.Join(",",viaRoads.Select(r=>r.Name))+"; plannerVia="+string.Join(",",plannerVias.Select(r=>r.Name)));
        }
        private bool RetryConnectedSource()
        {
            if(!towReplan||attemptedSources.Count>=4)return false;
            var source=session.Roads.Single(r=>r.Id==session.SourceRoad);
            foreach(var candidate in session.Roads.Where(r=>r.Type==0&&!attemptedSources.Contains(r.Id)&&TowRerouteAnchor.Distance(r,Point(startPosition))<=5&&((source.Connections??new TowConnection[0]).Any(c=>c.OtherRoad==r.Id)||(r.Connections??new TowConnection[0]).Any(c=>c.OtherRoad==source.Id))).OrderBy(r=>TowRerouteAnchor.Distance(r,Point(startPosition))))
            {
                attemptedSources.Add(candidate.Id);var index=Enumerable.Range(0,candidate.Points.Length).OrderBy(i=>candidate.Points[i].Distance(Point(startPosition))).First();
                if(!TowPreviewPlanner.Plan(session.Roads,candidate.Id,index,destination,true).Found)continue;
                TowRoadSnapshot[] resolved;try{resolved=TowRouteInstructions.ResolveVias(session.Roads,candidate.Id,index,destination,viaRoads.Select(r=>TowVoiceIntent.Identifier(r.Name)).ToArray());}catch(InvalidOperationException){continue;}
                session=new TowPlanningSession{Game=session.Game,Aircraft=session.Aircraft,Schedule=session.Schedule,Callsign=session.Callsign,NativeRoads=session.NativeRoads,Roads=session.Roads,Destinations=session.Destinations,SourceRoad=candidate.Id,SourceIndex=index,Source=candidate.Name};viaRoads=resolved;var nativePlan=TowNativeViaPlan.Create(session.Roads,session.SourceRoad,session.SourceIndex,destination,viaRoads.Select(r=>TowVoiceIntent.Identifier(r.Name)).ToArray());plannerVias=nativePlan.Planner;
                log("[Tugs] Retrying native reroute from connected junction road "+candidate.Name+"["+index+"], retaining actual position/heading and ordered via constraints.");StartWorker();return true;
            }
            return false;
        }
        private void Validate()
        {
            var aircraft=session.Aircraft;
            if(!aircraft||!ReferenceEquals(F(gameType,"instance").GetValue(null),session.Game)||(bool)Get(session.Game,"loading")||(bool)Get(aircraft,"_deleted")||(!towReplan&&!(bool)Get(aircraft,"_imatterminal"))||
                !ReferenceEquals(Get(aircraft,"_timetable"),session.Schedule)||(string)Get(aircraft,"_name")!=session.Callsign||(!towReplan&&!ReferenceEquals(Get(aircraft,"_terminal"),session.NativeRoads[session.SourceRoad]))||
                Point(startPosition).Distance(Point((Vector3)Get(aircraft,"_pos")))>.5)
                throw new InvalidOperationException("Airport, aircraft flight or source stand changed. Refresh the plan.");
            if(Convert.ToInt32(multiplayer.Invoke(null,null))!=0)throw new InvalidOperationException("Multiplayer state changed.");
            if(Convert.ToInt32(Get(aircraft,"_state"))!=(towReplan?12:8)||towReplan&&(!(bool)Get(aircraft,"_active")||!(bool)Get(aircraft,"_hold_position_taxistop")||TowRerouteAnchor.Distance(session.Roads.Single(r=>r.Id==session.SourceRoad),Point((Vector3)Get(aircraft,"_pos")))>15)||Math.Abs((float)Get(aircraft,"_lv"))>.1||!ReferenceEquals(Get(aircraft,"_route"),originalRoute)||!ReferenceEquals(Get(aircraft,"_router"),originalRouter))
                throw new InvalidOperationException("Aircraft is moving, receiving instructions or no longer waiting at the stand.");
            var target=session.NativeRoads[destination.Road];
            if((bool)Get(target,"occupied")||(Get(target,"locked")!=null&&(!towReplan||!ReferenceEquals(Get(target,"locked"),aircraft))))throw new InvalidOperationException("Destination became occupied or reserved. Refresh the plan.");
        }
        internal void Cancel(string reason)
        {
            if(!Busy||cancelled)return;
            cancelled=true;Message=reason;log("[Tugs] Native route check cancelled: "+reason);
            // Volatile native flag: worker exits through its own Cleanup. Keep ownership until thread exit.
            F(routerType,"finished").SetValue(router,true);
        }
        internal void Tick()
        {
            if(!Busy)return;
            var retrying=false;Vector3[] candidatePoints=null;
            try
            {
                if(!cancelled){try{Validate();}catch(Exception error){Cancel(error.Message);}}
                var thread=Get(router,"thread") as Thread;
                if(thread!=null&&thread.IsAlive)
                {
                    // Time limits apply to a live worker, not an already completed result polled after a busy frame.
                    // Source retries share the five-second request budget.
                    if(!cancelled&&Time.realtimeSinceStartup-requested>5)
                    {
                        try
                        {
                            var tracks=routerType.GetField("tracks",Flags)?.GetValue(router) as ICollection;
                            log("[Tugs] Planner deadline: "+session.Callsign+" source="+session.Source+" destination="+destination.Name+
                                " via="+string.Join(",",viaRoads.Select(r=>r.Name))+" totalSeconds="+(Time.realtimeSinceStartup-requested)+
                                " workerSeconds="+(Time.realtimeSinceStartup-started)+" steps="+routerType.GetField("stepcnt",Flags)?.GetValue(router)+
                                " branches="+tracks?.Count+" highestCount="+routerType.GetField("highestcount",Flags)?.GetValue(router));
                        }catch{ /* Deadline diagnostics must not prevent cancellation. */ }
                        Cancel("Tow route calculation timed out, please revise taxiways.");
                    }
                    return;
                }
                if(cancelled)return;
                Validate();
                if(!(bool)Get(router,"finished")||(bool)Get(router,"writein"))throw new InvalidOperationException("Native planner did not finish a read-only calculation.");
                var nativeError=Get(router,"error") as string;
                if(!string.IsNullOrWhiteSpace(nativeError))throw new InvalidOperationException("TS3 planner: "+nativeError);
                var path=Get(router,"_route") as List<Vector3>;
                candidatePoints=path?.ToArray();
                var geometry=path?.Select(Point).ToArray();
                if(towReplan&&geometry!=null&&geometry.Length>=2&&geometry[0].Distance(Point(startPosition))>25)
                {
                    try{geometry=TowParkingGeometry.TrimStart(geometry,Point(startPosition));path=geometry.Select(p=>new Vector3(p.X,p.Y,p.Z)).ToList();log("[Tugs] Removed native reroute leading turn scaffold at actual aircraft position.");}catch(InvalidOperationException){}
                }
                var endpoint=session.Roads.Single(r=>r.Id==destination.Road).Points[destination.Index];
                // The native planner may append steering look-ahead beyond its
                // requested stand point. Keep only a route that actually reaches it.
                if(towReplan&&geometry!=null&&geometry.Length>=2&&geometry[geometry.Length-1].Distance(endpoint)>25)
                {
                    try{geometry=TowParkingGeometry.Trim(geometry,endpoint);path=geometry.Select(p=>new Vector3(p.X,p.Y,p.Z)).ToList();log("[Tugs] Trimmed native reroute stand look-ahead at the requested endpoint.");}catch(InvalidOperationException){}
                }
                if(geometry!=null&&geometry.Length>0)log("[Tugs] Reroute endpoint gaps: start="+geometry[0].Distance(Point(startPosition)).ToString("0.00")+", end="+geometry[geometry.Length-1].Distance(endpoint).ToString("0.00"));
                var issue=TowRouteSafety.Check(geometry,Point(startPosition),session.Roads.Single(r=>r.Id==destination.Road).Points[destination.Index],session.Roads,true);
                if(issue==null)issue=TowRouteInstructions.ValidateNamedVias(geometry,session.Roads,viaRoads.Select(r=>TowVoiceIntent.Identifier(r.Name)).ToArray());
                if(issue==null)
                {
                    var type=Get(session.Aircraft,"_type");double length=(float)Get(type,"_length"),width=(float)Get(type,"_width");
                    if(double.IsNaN(length)||double.IsNaN(width)||length<=0||width<=0||length>300||width>300)throw new InvalidOperationException("Aircraft dimensions unavailable for runway protection.");
                    new TowRunwayCrossings(geometry,session.Roads,Math.Sqrt(length*length+width*width)+30,vacatedRunways:vacatedRunways);
                }
                if(issue==null)Completed=new CheckedTowRoute{Session=session,Destination=destination,Points=path.ToArray(),HoldShort=holdShort,Via=viaRoads.Select(r=>TowVoiceIntent.Identifier(r.Name)).ToArray()};
                Message=issue??("Native route found: "+geometry.Length+" points. Read-only check complete. Start tow validates aircraft-specific parking and rechecks availability before coupling.");
                log("[Tugs] Native route check "+session.Callsign+" -> "+destination.Name+": "+Message+"; error="+Get(router,"error")+"; result="+Get(router,"result"));
            }
            catch(Exception error){if(candidatePoints!=null)try{CaptureFailure?.Invoke(session.Aircraft,candidatePoints,error.Message);}catch{}Message="Native route check failed: "+error.Message;log("[Tugs] "+Message);if(error.Message.StartsWith("TS3 planner:",StringComparison.Ordinal))try{retrying=RetryConnectedSource();}catch(Exception retryError){Message="Native route check failed: "+retryError.Message;log("[Tugs] "+Message);}}
            finally
            {
                var thread=router==null?null:Get(router,"thread") as Thread;
                if(!retrying&&(thread==null||!thread.IsAlive)){router=null;session=null;destination=null;}
            }
        }
        public void Dispose(){Cancel("Tug module stopped.");}
    }
}
