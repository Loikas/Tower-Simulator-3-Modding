using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using TS3Modding.API;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed partial class RejectedTakeoffModule
    {
        private sealed class ExitCandidate {internal object Road;internal int Index,ApproachIndex;internal float Distance;}
        private sealed class RecoveryPlan
        {
            internal Component Plane;internal Incident Incident;internal object Target,Router,OriginalRoute;
            internal int TargetIndex,SourceIndex,CandidateIndex,Stage,CommandCount,State;internal Vector3 Start;internal float Heading,Started,Requested;
            internal object[] Vias;internal List<ExitCandidate> Candidates;internal List<Vector3> First;
            internal Action<TowRecoveryPoint[]> TowDone;internal Action<string> TowFailed;
        }
        private readonly List<RecoveryPlan> retiringRecoveryPlans=new List<RecoveryPlan>();
        private readonly Dictionary<Component,RecoveryPlan> recoveryPlans=new Dictionary<Component,RecoveryPlan>();
        private static bool RecoveryRoutePrefix(Component __instance,object __0,int __1,ref bool __result)
        {
            var self=current;Incident incident;
            if(self==null||!self.enabled.Value||!self.incidents.TryGetValue(__instance,out incident)||!self.Same(__instance,incident)||!incident.Ready||(Convert.ToInt32(self.Get(__instance,"_state"))!=12&&Convert.ToInt32(self.Get(__instance,"_state"))!=9))return true;
            double remaining,offset,alignment;
            if(!self.RunwayGeometry(__instance,incident.Runway,out remaining,out offset,out alignment)||offset>15)return true;
            try{self.BeginRecoveryPlan(__instance,incident,__0,__1);}
            catch(Exception error){self.context.Log("Rejected-takeoff recovery setup failed: "+error.GetBaseException().Message);self.Report(__instance,"Unable, recovery route could not be calculated");}
            __result=true;return false;
        }
        private void StopPlan(RecoveryPlan plan)
        {
            if(plan.Router!=null){Set(plan.Router,"finished",true);if(!retiringRecoveryPlans.Contains(plan))retiringRecoveryPlans.Add(plan);}
        }
        private void CancelRecoveryPlans(){foreach(var plan in recoveryPlans.Values)StopPlan(plan);recoveryPlans.Clear();}
        private void BeginRecoveryPlan(Component plane,Incident incident,object target,int targetIndex,object[] towVias=null,Action<TowRecoveryPoint[]> towDone=null,Action<string> towFailed=null)
        {
            RecoveryPlan old;if(recoveryPlans.TryGetValue(plane,out old)){StopPlan(old);recoveryPlans.Remove(plane);}
            var position=(Vector3)Get(plane,"_pos");var points=((IList)Get(incident.Runway,"points")).Cast<Vector3>().ToArray();
            var sourceIndex=Enumerable.Range(0,points.Length).OrderBy(i=>(points[i]-position).sqrMagnitude).First();
            var vias=towVias??((IEnumerable)Get(plane,"_via")).Cast<object>().ToArray();var candidates=new List<ExitCandidate>();
            var heading=(float)Get(plane,"_rot")*Mathf.Deg2Rad;var forward=new Vector3(Mathf.Sin(heading),0,Mathf.Cos(heading));
            var clearance=RecoveryRules.ExitHandoffClearance((float)Get(incident.Runway,"width")*.3048f);
            foreach(var cross in ((IEnumerable)Get(incident.Runway,"crosses")).Cast<object>())
            {
                var road=Get(cross,"cross");if(Convert.ToInt32(Get(road,"type"))!=0)continue;
                var contact=(Vector3)Get(cross,"contact");var runwayIndex=Convert.ToInt32(Get(cross,"thisidx"));if(Vector3.Dot(contact-position,forward)<10||runwayIndex<sourceIndex)continue;
                var taxi=((IList)Get(road,"points")).Cast<Vector3>().ToArray();var junction=Convert.ToInt32(Get(cross,"crossidx"));
                foreach(var direction in new[]{-1,1})for(var n=junction+direction;n>=0&&n<taxi.Length;n+=direction)
                {
                    var distance=RunwayDistance(taxi[n],points);
                    if(!RecoveryRules.ForwardExit(Vector3.Dot(contact-position,forward),distance,clearance))continue;
                    var pivot=Math.Max(0,Math.Min(taxi.Length-1,n+2*direction));
                    candidates.Add(new ExitCandidate{Road=road,Index=pivot,ApproachIndex=RecoveryRules.ExitApproach(sourceIndex,runwayIndex),Distance=(contact-position).magnitude+(taxi[n]-contact).magnitude});break;
                }
            }
            var plan=new RecoveryPlan{Requested=Time.realtimeSinceStartup,Plane=plane,Incident=incident,Target=target,TargetIndex=targetIndex,SourceIndex=sourceIndex,Start=position,Heading=(float)Get(plane,"_rot"),Vias=vias,Candidates=candidates.OrderBy(c=>RecoveryRules.ExitRank(vias.Length>0,vias.Length>0&&(string)Get(c.Road,"name")== (string)Get(vias[0],"name"))).ThenBy(c=>c.Distance).Take(64).ToList(),State=Convert.ToInt32(Get(plane,"_state")),CommandCount=Convert.ToInt32(Get(plane,"_roadcommandcnt")),OriginalRoute=Get(plane,"_route")};
            plan.TowDone=towDone;plan.TowFailed=towFailed;recoveryPlans[plane]=plan;
            if(plan.Candidates.Count==0){FailRecoveryPlan(plan,"no connected forward exit matches the requested route");return;}
            StartRecoveryLeg(plan);
        }
        private static float RunwayDistance(Vector3 position,Vector3[] points)
        {
            var best=float.PositiveInfinity;
            for(var i=1;i<points.Length;i++)
            {
                var a=points[i-1];var delta=points[i]-a;a.y=position.y;delta.y=0;
                var t=delta.sqrMagnitude<.001f?0:Mathf.Clamp01(Vector3.Dot(position-a,delta)/delta.sqrMagnitude);
                best=Mathf.Min(best,(position-a-delta*t).magnitude);
            }
            return best;
        }
        private void StartRecoveryLeg(RecoveryPlan plan)
        {
            // A replacement waits for its own retired worker to exit; native
            // workers are cancelled cooperatively, never aborted or joined.
            if(retiringRecoveryPlans.Any(p=>ReferenceEquals(p.Plane,plan.Plane)))return;
            var candidate=plan.Candidates[plan.CandidateIndex];var roadType=candidate.Road.GetType();var routerType=planeType.Assembly.GetType("Router",true);
            var listType=typeof(List<>).MakeGenericType(roadType);var via=(IList)Activator.CreateInstance(listType);
            if(plan.Stage==1)foreach(var road in plan.Vias.Skip(plan.Vias.Length>0&&((string)Get(plan.Vias[0],"name")== (string)Get(candidate.Road,"name"))?1:0))via.Add(road);
            var start=plan.Stage==0?plan.Incident.Runway:candidate.Road;var index=plan.Stage==0?candidate.ApproachIndex:candidate.Index;
            var target=plan.Stage==0?candidate.Road:plan.Target;var targetIndex=plan.Stage==0?candidate.Index:plan.TargetIndex;
            var runwayPoints=((IList)Get(plan.Incident.Runway,"points")).Cast<Vector3>().ToArray();
            var position=plan.Stage==0?runwayPoints[candidate.ApproachIndex]:plan.First[plan.First.Count-1];
            var heading=plan.Heading;
            if(plan.Stage==1){var d=plan.First[plan.First.Count-1]-plan.First[plan.First.Count-2];heading=Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg;}
            var ctor=routerType.GetConstructors(Flags).Single(c=>c.GetParameters().Length==11);
            // Native calculations publish nothing to the aircraft until both legs have succeeded.
            plan.Router=ctor.Invoke(new object[]{start,index,target,targetIndex,position,heading,via,plan.Stage==0,plan.Plane,false,false});
            var worker=routerType.GetMethod("RouteCalculator",Flags);var thread=new Thread((ThreadStart)Delegate.CreateDelegate(typeof(ThreadStart),plan.Router,worker));
            Set(plan.Router,"thread",thread);plan.Started=Time.realtimeSinceStartup;thread.Start();
            context.Log("Rejected-takeoff via sequence: "+string.Join(",",plan.Vias.Select(v=>(string)Get(v,"name")).ToArray())+"; start heading="+heading);
            context.Log("Rejected-takeoff recovery leg "+(plan.Stage+1)+": "+plan.Incident.Callsign+" "+Get(start,"name")+"["+index+"] -> "+Get(target,"name")+"["+targetIndex+"]");
        }
        private void RetryRecoveryCandidate(RecoveryPlan plan,string reason)
        {
            context.Log("Rejected-takeoff candidate retry: "+reason);
            plan.Stage=0;plan.First=null;plan.CandidateIndex++;
            if(plan.CandidateIndex>=plan.Candidates.Count){FailRecoveryPlan(plan,"all native exit and return routes failed");return;}
            StartRecoveryLeg(plan);
        }
        private void FailRecoveryPlan(RecoveryPlan plan,string reason)
        {
            StopPlan(plan);recoveryPlans.Remove(plan.Plane);
            if(plan.TowFailed!=null){plan.TowFailed(reason);return;}
            context.Log("Rejected-takeoff staged routing failed: "+plan.Incident.Callsign+": "+reason);
            if(Same(plan.Plane,plan.Incident))Report(plan.Plane,"Unable, no valid runway exit and taxi route, please revise routing");
        }
        private void TickRecoveryPlans()
        {
            retiringRecoveryPlans.RemoveAll(p=>{var worker=Get(p.Router,"thread") as Thread;return worker==null||!worker.IsAlive;});
            foreach(var plan in recoveryPlans.Values.ToArray())try
            {
                if(!enabled.Value||!Same(plan.Plane,plan.Incident)||(bool)Get(plan.Incident.Game,"loading")||Convert.ToInt32(Get(plan.Plane,"_roadcommandcnt"))!=plan.CommandCount||plan.TowDone==null&&!ReferenceEquals(Get(plan.Plane,plan.State==9?"_terminal":"_trgrunway"),plan.Target)||Convert.ToInt32(Get(plan.Plane,"_state"))!=plan.State||!ReferenceEquals(Get(plan.Plane,"_route"),plan.OriginalRoute)||((Vector3)Get(plan.Plane,"_pos")-plan.Start).magnitude>1)
                {StopPlan(plan);recoveryPlans.Remove(plan.Plane);plan.TowFailed?.Invoke("Aircraft or recovery request changed");continue;}
                if(Time.realtimeSinceStartup-plan.Requested>5){FailRecoveryPlan(plan,"route calculation too complex, please revise taxiways");continue;}
                if(plan.Router==null){StartRecoveryLeg(plan);continue;}
                var thread=Get(plan.Router,"thread") as Thread;
                if(thread!=null&&thread.IsAlive){if(Time.realtimeSinceStartup-plan.Started>2)FailRecoveryPlan(plan,"route calculation too complex, please revise taxiways");continue;}
                var path=Get(plan.Router,"_route") as List<Vector3>;var error=Get(plan.Router,"error") as string;
                if(!(bool)Get(plan.Router,"finished")||!string.IsNullOrWhiteSpace(error)||path==null||path.Count<2)
                {
                    context.Log("Rejected-takeoff candidate rejected: "+error);
                    RetryRecoveryCandidate(plan,"native planner rejected this exit or return route");continue;
                }
                if(plan.Stage==0)
                {
                    var candidate=plan.Candidates[plan.CandidateIndex];var endpoint=(Vector3)((IList)Get(candidate.Road,"points"))[candidate.Index];
                    var taxi=((IList)Get(candidate.Road,"points")).Cast<Vector3>().ToArray();
                    var end=path.Count-1;
                    var actual=Enumerable.Range(0,taxi.Length).OrderBy(i=>(taxi[i]-path[end]).sqrMagnitude).First();
                    var runway=((IList)Get(plan.Incident.Runway,"points")).Cast<Vector3>().ToArray();
                    var clearance=RecoveryRules.ExitHandoffClearance((float)Get(plan.Incident.Runway,"width")*.3048f);
                    context.Log("Rejected-takeoff exit geometry: "+plan.Incident.Callsign+", points="+path.Count+", startGap="+(path[0]-plan.Start).magnitude+", requestedGap="+(path[end]-endpoint).magnitude+", taxiGap="+(taxi[actual]-path[end]).magnitude+", runwayOffset="+RunwayDistance(path[end],runway));
                    if(end<1||(taxi[actual]-path[end]).magnitude>5||RunwayDistance(path[end],runway)<clearance)
                    {RetryRecoveryCandidate(plan,"native exit stops before its taxiway handoff");continue;}
                    // Native intersection routes stop within two samples of the requested point.
                    // Start the second leg at that actual native endpoint, never bridge the difference.
                    var prefix=new List<Vector3>{plan.Start};
                    for(var n=plan.SourceIndex+1;n<=candidate.ApproachIndex;n++)prefix.Add(runway[n]);
                    var approach=runway[candidate.ApproachIndex];
                    var approachHeading=plan.Heading*Mathf.Deg2Rad;var approachForward=new Vector3(Mathf.Sin(approachHeading),0,Mathf.Cos(approachHeading));
                    var approachSpacing=(runway[Math.Min(runway.Length-1,candidate.ApproachIndex+1)]-runway[Math.Max(0,candidate.ApproachIndex-1)]).magnitude;
                    var near=Enumerable.Range(0,Math.Min(6,path.Count)).Where(i=>RecoveryRules.StartConnection(Vector3.Dot(path[i]-approach,approachForward),RunwayDistance(path[i],runway),(path[i]-approach).magnitude,approachSpacing)).OrderBy(i=>(path[i]-approach).sqrMagnitude).DefaultIfEmpty(-1).First();
                    if(near<0){RetryRecoveryCandidate(plan,"native exit does not join its runway approach");continue;}
                    prefix.AddRange(path.Skip(near));path=prefix;end=path.Count-1;
                    var angle=plan.Heading*Mathf.Deg2Rad;var forward=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                    var sourceSpacing=(runway[Math.Min(runway.Length-1,plan.SourceIndex+1)]-runway[Math.Max(0,plan.SourceIndex-1)]).magnitude;
                    var start=Enumerable.Range(0,Math.Min(6,path.Count)).Where(i=>RecoveryRules.StartConnection(Vector3.Dot(path[i]-plan.Start,forward),RunwayDistance(path[i],runway),(path[i]-plan.Start).magnitude,sourceSpacing)).OrderBy(i=>(path[i]-plan.Start).sqrMagnitude).DefaultIfEmpty(-1).First();
                    if(start<0){RetryRecoveryCandidate(plan,"native exit does not begin along aircraft runway heading");continue;}
                    candidate.Index=actual;plan.First=new List<Vector3>{plan.Start};plan.First.AddRange(path.Skip(start));
                    if(plan.First.Count<2){RetryRecoveryCandidate(plan,"native exit too short");continue;}
                    plan.Stage=1;StartRecoveryLeg(plan);continue;
                }
                var joined=new List<Vector3>(plan.First);
                var nearest=RecoveryRules.JoinIndex(path.Take(6).Select(p=>(double)(p-joined[joined.Count-1]).sqrMagnitude).ToArray());
                if(nearest<0){FailRecoveryPlan(plan,"taxi leg does not join the exit");continue;}
                joined.AddRange(path.Skip(nearest));
                if(joined.Any(v=>float.IsNaN(v.x)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.z))){FailRecoveryPlan(plan,"invalid native geometry");continue;}
                if(plan.TowDone!=null){recoveryPlans.Remove(plan.Plane);plan.TowDone(joined.Select(v=>new TowRecoveryPoint(v.x,v.y,v.z)).ToArray());continue;}
                Set(plan.Plane,"_routestart",plan.Incident.Runway);Set(plan.Plane,"_routestartidx",plan.SourceIndex);plan.Incident.RecoveryRoute=joined;Set(plan.Plane,"_route",joined);planeType.GetMethod("SetLastGateOnTheRoute",Flags).Invoke(plan.Plane,null);Set(plan.Plane,"_routepos",1);Set(plan.Plane,"_substate",0);Set(plan.Plane,"_roadend",false);Set(plan.Plane,"_need_roadcalculator",false);
                recoveryPlans.Remove(plan.Plane);context.Log("Rejected-takeoff recovery route ready: "+plan.Incident.Callsign+", "+joined.Count+" native points via exit "+Get(plan.Candidates[plan.CandidateIndex].Road,"name"));
            }
            catch(Exception e){FailRecoveryPlan(plan,e.GetBaseException().Message);}
        }
    }
}
