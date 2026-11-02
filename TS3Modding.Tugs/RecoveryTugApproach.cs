using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace TS3Modding.Tugs
{
    // Only the unladen recovery approach is owned here; native coupling remains unchanged.
    internal sealed class RecoveryTugApproach:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.tugs.recoveryapproach");private static RecoveryTugApproach current;
        private readonly Func<Component,bool> owns,held;private readonly Action<Component,string> reply;private readonly Action<string> log;
        private readonly FieldInfo gameInstance;
        private sealed class Approach {internal Component Truck,Plane;internal object Game;internal Vector3 LastPosition;internal Vector3[] Route;internal TowPoint[] Geometry;internal float Speed;internal int Index=1;internal double Progress;internal string Runway;internal TowRunwayCrossings Crossings;internal bool Fault,Reported,OriginRevoked,TrafficWaiting,FacingReady;}
        private readonly Dictionary<Component,Approach> approaches=new Dictionary<Component,Approach>();
        internal RecoveryTugApproach(Assembly assembly,Func<Component,bool> owns,Func<Component,bool> held,Action<Component,string> reply,Action<string> log,bool patch=true)
        {
            this.owns=owns;this.held=held;this.reply=reply;this.log=log;if(current!=null)throw new InvalidOperationException("Recovery tug approach already registered");current=this;
            gameInstance=assembly.GetType("Game",true).GetField("instance",BindingFlags.Static|Flags);
            if(!patch)return;
            try{harmony.Patch(assembly.GetType("TowTruck",true).GetMethod("AKINDILJBNI",Flags),prefix:new HarmonyMethod(typeof(RecoveryTugApproach),nameof(Step)));}catch{Dispose();throw;}
        }
        private static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
        private static TowPoint Point(Vector3 p)=>new TowPoint(p.x,p.y,p.z);
        internal double Begin(Component truck,Component plane,Vector3[] route,TowRoadSnapshot[] roads,string runway,int sourceRoad=-1)
        {
            if(!truck||route==null||route.Length<2)throw new InvalidOperationException("No checked tug approach route");
            var position=truck.transform.position;var aircraft=Point((Vector3)Get(plane,"_pos"));
            var source=sourceRoad<0?roads.Where(r=>r.Type==1).OrderBy(r=>TowRerouteAnchor.Distance(r,Point(position))).FirstOrDefault():roads.SingleOrDefault(r=>r.Id==sourceRoad&&r.Type==1);
            var target=roads.Where(r=>r.Type==2&&TowRunwayCrossings.Matches(r.Name,runway)).OrderBy(r=>TowRerouteAnchor.Distance(r,aircraft)).FirstOrDefault();
            if(source==null||target==null)throw new InvalidOperationException("Recovery tug taxiway endpoints unavailable");
            var startIndex=Enumerable.Range(0,source.Points.Length).OrderBy(i=>source.Points[i].Distance(Point(position))).First();
            var endIndex=Enumerable.Range(0,target.Points.Length).OrderBy(i=>target.Points[i].Distance(aircraft)).First();
            var shortest=TowPreviewPlanner.Plan(roads,source.Id,startIndex,new TowDestination{Road=target.Id,Index=endIndex},true,tugApproach:true,sourcePosition:Point(position),targetPosition:aircraft);
            if(!shortest.Found)throw new InvalidOperationException("No connected taxiway approach for recovery tug: "+shortest.Message);
            var geometry=RecoveryApproachPath.Build(new[]{Point(position)}.Concat(shortest.Points).Concat(new[]{aircraft}).ToArray(),roads);
            var path=geometry.Select(p=>new Vector3(p.X,p.Y,p.Z)).ToArray();
            var crossings=new TowRunwayCrossings(geometry,roads,30,null,runway);
            approaches[plane]=new Approach{Truck=truck,Plane=plane,Game=gameInstance.GetValue(null),LastPosition=position,Route=path,Geometry=geometry,Crossings=crossings,Runway=runway};
            log("[Tugs] Shortest independent taxiway approach: "+shortest.Length.ToString("0.0")+" units via "+string.Join(", ",shortest.Roads)+"; "+path.Length+" points; destination runway="+runway);
            return geometry.Zip(geometry.Skip(1),(a,b)=>a.Distance(b)).Sum();
        }
        private static readonly System.Globalization.CultureInfo SaveCulture=System.Globalization.CultureInfo.InvariantCulture;
        internal void Export(Component plane,Dictionary<string,string> data)
        {
            Approach a;if(!approaches.TryGetValue(plane,out a))return;
            data["a.path"]=string.Join(";",a.Geometry.Select(p=>string.Join(",",new[]{p.X.ToString("R",SaveCulture),p.Y.ToString("R",SaveCulture),p.Z.ToString("R",SaveCulture)})));
            data["a.index"]=a.Index.ToString(SaveCulture);data["a.progress"]=a.Progress.ToString("R",SaveCulture);data["a.speed"]=a.Speed.ToString("R",SaveCulture);data["a.runway"]=a.Runway;
            data["a.event"]=a.Crossings.Event.ToString(SaveCulture);data["a.occupying"]=a.Crossings.Occupying.ToString();data["a.cleared"]=a.Crossings.Cleared.ToString();data["a.holding"]=a.Crossings.Holding.ToString();data["a.revoked"]=a.OriginRevoked.ToString();data["a.fault"]=a.Fault.ToString();
        }
        internal void Restore(Component plane,Component truck,TowRoadSnapshot[] roads,Dictionary<string,string> data)
        {
            var parts=data["a.path"].Split(';');if(parts.Length<2||parts.Length>20000)throw new System.IO.InvalidDataException("Invalid saved tug approach size.");
            var geometry=parts.Select(s=>{var xyz=s.Split(',');if(xyz.Length!=3)throw new System.IO.InvalidDataException("Invalid approach point.");return new TowPoint(float.Parse(xyz[0],SaveCulture),float.Parse(xyz[1],SaveCulture),float.Parse(xyz[2],SaveCulture));}).ToArray();
            var index=int.Parse(data["a.index"],SaveCulture);var progress=double.Parse(data["a.progress"],SaveCulture);var speed=float.Parse(data["a.speed"],SaveCulture);
            if(geometry.Any(p=>!p.Finite)||index<1||index>=geometry.Length||double.IsNaN(progress)||double.IsInfinity(progress)||progress<0||float.IsNaN(speed)||speed<0||speed>6.01||geometry.Zip(geometry.Skip(1),(a,b)=>a.Distance(b)).Any(d=>d>1.01))throw new System.IO.InvalidDataException("Invalid saved approach progress/geometry.");
            var position=truck.transform.position;var anchor=new TowRoadSnapshot{Points=new[]{geometry[index-1],geometry[index]}};
            if(TowRerouteAnchor.Distance(anchor,Point(position))>.15)throw new System.IO.InvalidDataException("Saved approach tug is outside its route segment.");
            var travelled=geometry.Take(index).Zip(geometry.Skip(1).Take(index-1),(a,b)=>a.Distance(b)).Sum()+geometry[index-1].Distance(Point(position));
            if(Math.Abs(travelled-progress)>.2)throw new System.IO.InvalidDataException("Saved approach distance does not match its position.");
            var crossings=new TowRunwayCrossings(geometry,roads,30,null,data["a.runway"]);
            crossings.Restore(int.Parse(data["a.event"],SaveCulture),bool.Parse(data["a.occupying"]),bool.Parse(data["a.cleared"]),progress,bool.Parse(data["a.holding"]));
            approaches[plane]=new Approach{Truck=truck,Plane=plane,Game=gameInstance.GetValue(null),Route=geometry.Select(p=>new Vector3(p.X,p.Y,p.Z)).ToArray(),Geometry=geometry,Index=index,Progress=progress,Speed=speed,Runway=data["a.runway"],Crossings=crossings,LastPosition=position,OriginRevoked=bool.Parse(data["a.revoked"]),Fault=bool.Parse(data["a.fault"]),FacingReady=true};
            log("[Tugs] Restored independent tug approach for "+(string)Get(plane,"_name"));
        }
        internal void Cancel(Component plane)=>approaches.Remove(plane);
        internal bool Waiting(Component p)=>approaches.ContainsKey(p)&&(approaches[p].Fault||approaches[p].TrafficWaiting||approaches[p].Crossings.Holding);
        internal string WaitingRunway(Component p)=>approaches.ContainsKey(p)&&approaches[p].Crossings.Holding?approaches[p].Crossings.Name:null;
        internal string Status(Component p)=>approaches.ContainsKey(p)?approaches[p].Fault?"Tug approach held; see loader log":approaches[p].TrafficWaiting?"Approaching tug yielding to aircraft":approaches[p].Crossings.Holding?"Approaching tug holding short of runway "+approaches[p].Crossings.Name:"Tug approaching aircraft via taxiways":null;
        internal bool NeedsRecovery(Component p)=>approaches.ContainsKey(p)&&approaches[p].Fault;
        internal bool Recover(Component p){Approach a;if(!approaches.TryGetValue(p,out a)||!a.Fault)return false;a.Fault=false;reply(p,"Tug approach recovery requested");return true;}
        internal bool Clear(Component p,string runway)
        {Approach a;if(!approaches.TryGetValue(p,out a))return false;a.Crossings.Clear(runway,true);a.Reported=false;a.OriginRevoked=false;reply(p,"Tug cleared to cross runway "+TowRunwayCrossings.Spoken(runway));return true;}
        internal bool Revoke(Component p,string runway)
        {Approach a;if(!approaches.TryGetValue(p,out a))return false;if(runway!=null&&!TowRunwayCrossings.Matches(a.Crossings.Name,runway))throw new InvalidOperationException("Wrong runway for the approaching tug");a.Crossings.Revoke();a.OriginRevoked=true;reply(p,"Tug will hold short of runway "+TowRunwayCrossings.Spoken(a.Crossings.Name));return true;}
        private static bool Step(Component __instance,float __0)
        {
            var self=current;var a=self?.approaches.Values.FirstOrDefault(v=>ReferenceEquals(v.Truck,__instance));if(a==null)return true;
            try
            {
                if(!a.Plane||!self.owns(a.Plane)){self.approaches.Remove(a.Plane);return true;}
                if(a.Game==null||!ReferenceEquals(self.gameInstance.GetValue(null),a.Game)||(bool)Get(a.Game,"loading")||(float)Get(a.Game,"speedup")<=0)return false;
                if((bool)Get(a.Truck,"_car_linked")){self.approaches.Remove(a.Plane);return true;}
                if(!ReferenceEquals(Get(a.Truck,"_plane"),a.Plane))throw new InvalidOperationException("Recovery tug assignment changed");
                if(a.Fault||__0<=0||__0>2||self.held(a.Plane)){a.Speed=0;return false;}
                var position=a.Truck.transform.position;
                if(Vector3.Distance(position,a.LastPosition)>3)throw new InvalidOperationException("Approaching tug position changed outside its checked path");
                if(a.Index>=a.Route.Length||Vector3.Distance(position,a.Route[a.Route.Length-1])<=20)
                {self.approaches.Remove(a.Plane);self.log("[Tugs] Taxiway approach complete; native nose-wheel coupling takes over.");return true;}
                var next=a.Crossings;
                if(!a.OriginRevoked&&!next.Complete&&next.Event==next.Events.Length-1&&TowRunwayCrossings.Matches(next.Name,a.Runway)&&!next.Cleared&&!next.Occupying)next.Clear(a.Runway,true);
                if(next.Update(a.Progress,Math.Max(1,a.Speed),__0,next.OutsideNext(Point(position),Point(position))))
                {a.Speed=0;if(!a.Reported){a.Reported=true;if(TS3Modding.API.PilotReportPreferences.CrossingReportsEnabled)self.reply(a.Plane,"Tug holding short of runway "+TowRunwayCrossings.Spoken(next.Name)+", request crossing");}return false;}
                a.TrafficWaiting=self.TrafficAhead(a,position);if(a.TrafficWaiting){a.Speed=0;return false;}
                if(!a.FacingReady){
                    var startHeading=RecoveryApproachPath.Heading(a.Geometry,a.Index,Point(position));
                    var facing=Quaternion.LookRotation(new Vector3(startHeading.X,0,startHeading.Z));
                    a.Truck.transform.rotation=Quaternion.RotateTowards(a.Truck.transform.rotation,facing,90*__0);
                    a.Speed=0;if(Quaternion.Angle(a.Truck.transform.rotation,facing)>1)return false;a.FacingReady=true;
                }
                var desired=(float)RecoveryApproachPath.SpeedLimit(a.Geometry,a.Index);a.Speed=a.Speed<desired?Math.Min(desired,a.Speed+2*__0):Math.Max(desired,a.Speed-3*__0);
                var budget=a.Speed*__0;var move=position;
                for(var count=0;budget>.00001f&&a.Index<a.Route.Length&&count<128;count++)
                {var target=a.Route[a.Index];var segment=Vector3.Distance(move,target);var step=Math.Min(budget,segment);move=Vector3.MoveTowards(move,target,step);budget-=step;a.Progress+=step;if(segment<=step+.0001f)a.Index++;else break;}
                var delta=move-position;if(delta.sqrMagnitude>.00001f){var direction=RecoveryApproachPath.Heading(a.Geometry,a.Index,Point(move));a.Truck.transform.rotation=Quaternion.LookRotation(new Vector3(direction.X,0,direction.Z));}
                a.Truck.transform.position=move;a.LastPosition=move;
                return false;
            }
            catch(Exception e){if(!a.Fault){a.Fault=true;self.log("[Tugs] Recovery tug approach held: "+e);self.reply(a.Plane,"Unable, tug approach held");}return false;}
        }
        private bool TrafficAhead(Approach a,Vector3 position)
        {
            var ahead=a.Geometry[Math.Min(a.Geometry.Length-1,a.Index+12)];
            var traffic=Get(a.Game,"active_airplanes") as System.Collections.IEnumerable;if(traffic==null)return false;
            foreach(var value in traffic)
            {
                var p=value as Component;if(!p||ReferenceEquals(p,a.Plane)||(bool)Get(p,"_deleted")||!(bool)Get(p,"_active")||(float)Get(p,"_alt")>1||(bool)Get(p,"_imatterminal"))continue;
                var type=Get(p,"_type");var centre=Point((Vector3)Get(p,"_pos"));
                if(RecoveryApproachPath.TrafficConflict(Point(position),ahead,centre,(float)Get(type,"_length"),(float)Get(type,"_width")))return true;
            }
            return false;
        }
        public void Dispose(){harmony.UnpatchSelf();approaches.Clear();if(ReferenceEquals(current,this))current=null;}
    }
}
