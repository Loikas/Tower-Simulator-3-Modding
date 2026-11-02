using System;
using System.Collections.Generic;
using System.Linq;
using TS3Modding.API;
using UnityEngine;
namespace TS3Modding.Tugs
{
    internal sealed partial class NativeTugModule
    {
        private sealed class Recovery {internal TowRecoveryRequest Request;internal TowPlanningSession Session;internal TowDestination Destination;internal int Generation;internal bool Planning;}
        private readonly Dictionary<Component,Recovery> recoveries=new Dictionary<Component,Recovery>();
        private IDisposable recoveryRegistration;
        private object Value(object obj,string field)=>Required(obj.GetType(),field).GetValue(obj);
        private string RecoveryAlias(Component p){Recovery r;return recoveries.TryGetValue(p,out r)?TowStandAliases.For(r.Session).Alias(r.Destination.Name):null;}
        private string RecoveryDestination(Component p){Recovery r;return recoveries.TryGetValue(p,out r)?r.Destination.Name:null;}
        private sealed class RecoveryTicket:IDisposable {private Action release;internal RecoveryTicket(Action release){this.release=release;}public void Dispose(){var action=release;release=null;action?.Invoke();}}
        private IDisposable RegisterRecovery(TowRecoveryRequest request)
        {
            var aircraft=request?.Aircraft as Component;
            if(!aircraft||request.IsValid==null||!request.IsValid()||request.PlanRoute==null||request.Stand==null||request.Runway==null||recoveries.ContainsKey(aircraft)||transfer==null)return null;
            if(Convert.ToInt32(Value(Value(aircraft,"_type"),"_weight_class"))==0||Value(Value(aircraft,"ac"),"front_wheel")==null)return null;
            var session=towPlanning.Inspect(instance.GetValue(null),aircraft,(string)planeName.GetValue(aircraft),request.Runway);
            var position=(Vector3)Value(aircraft,"_pos");session.SourceIndex=Enumerable.Range(0,session.Roads[session.SourceRoad].Points.Length).OrderBy(i=>session.Roads[session.SourceRoad].Points[i].Distance(new TowPoint(position.x,position.y,position.z))).First();
            var destination=session.Destinations.SingleOrDefault(d=>ReferenceEquals(session.NativeRoads[d.Road],request.Stand));
            if(destination==null||destination.Occupied||!ReferenceEquals(Value(request.Stand,"locked"),aircraft))return null;
            // The reservation belongs to this recovery. It must not make its own
            // preview/via search reject every route; native ownership stays locked.
            destination=TowRouteInstructions.WithOwnedReservation(destination,true);
            destination.Index=transfer.ParkingIndex(aircraft,session,destination);
            request.DestinationAlias=TowStandAliases.For(session).Alias(destination.Name);
            recoveries.Add(aircraft,new Recovery{Request=request,Session=session,Destination=destination});
            context.Log("[Tugs] Runway recovery requested: "+session.Callsign+" -> "+destination.Name);
            return new RecoveryTicket(()=>RemoveRecovery(aircraft));
        }
        private void RemoveRecovery(Component aircraft)
        {
            Recovery r;if(!recoveries.TryGetValue(aircraft,out r))return;r.Generation++;recoveries.Remove(aircraft);
            recoveryApproach?.Cancel(aircraft);
            if(transfer?.Owns(aircraft)==true)transfer.Cancel(aircraft,"Recovery owner stopped");
        }
        private Action RestoredRecoveryCompletion(Component plane)
        {
            Recovery r;if(!recoveries.TryGetValue(plane,out r)||!r.Request.IsValid())return null;
            return ()=>{recoveries.Remove(plane);r.Request.Completed?.Invoke();};
        }
        private void ForgetRecoveriesForLoad(){foreach(var p in recoveries.Keys.ToArray())recoveryApproach?.Cancel(p);recoveries.Clear();}
        private void TickRecoveries()
        {
            foreach(var pair in recoveries.ToArray())if(!pair.Key||!pair.Value.Request.IsValid())RemoveRecovery(pair.Key);
        }
        private void CancelRecoveryClearance(Component aircraft)
        {
            var r=recoveries[aircraft];r.Generation++;r.Planning=false;
            if(transfer.Owns(aircraft))transfer.Hold(aircraft,true);
            else Reply(aircraft,"Tow clearance cancelled, holding position, tug assistance still required");
        }
        private void RecoveryVoice(Component aircraft,TowVoiceIntent intent)
        {
            var r=recoveries[aircraft];
            if(intent.Action=="cancel"||intent.Action=="hold"){CancelRecoveryClearance(aircraft);return;}
            if(intent.Action!="transfer")throw new InvalidOperationException("Awaiting tow clearance to stand "+RecoveryAlias(aircraft));
            if(!string.Equals(RecoveryAlias(aircraft),intent.Destination,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("wrong stand");

            if(!r.Request.IsValid())throw new InvalidOperationException("Recovery request is no longer available");
            // Resolve split named taxiways through the existing connectivity planner.
            var vias=TowRouteInstructions.ResolveVias(r.Session.Roads,r.Session.SourceRoad,r.Session.SourceIndex,r.Destination,intent.Via);
            r.Planning=true;var generation=++r.Generation;
            try{r.Request.PlanRoute(r.Destination.Index,vias.Select(v=>r.Session.NativeRoads[v.Id]).ToArray(),points=>{
                Recovery current;if(!recoveries.TryGetValue(aircraft,out current)||!ReferenceEquals(current,r)||r.Generation!=generation)return;r.Planning=false;
                try{
                    if(!r.Request.IsValid())throw new InvalidOperationException("Recovery request changed");
                    var gateType=aircraft.GetType().Assembly.GetType("IKGateManager",true);var manager=gateType.GetField("instance",System.Reflection.BindingFlags.Static|Flags).GetValue(null);
                    var gate=gateType.GetMethod("getGateByTerminal",Flags).Invoke(manager,new object[]{r.Destination.Name});var truck=Value(gate,"truck") as Component;
                    var path=points.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
                    var approachLength=recoveryApproach.Begin(truck,aircraft,path,r.Session.Roads,(string)Value(r.Request.Runway,"name"),r.Destination.Road);
                    transfer.Start(truck,new NativeTowRouteCheck.CheckedTowRoute{Session=r.Session,Destination=r.Destination,Points=path,ApproachLength=approachLength,Via=intent.Via,HoldShort=intent.HoldShort,CrossRunway=intent.CrossRunway},()=>{recoveries.Remove(aircraft);r.Request.Completed?.Invoke();});
                }catch(Exception e){recoveryApproach?.Cancel(aircraft);context.Log("[Tugs] Recovery refused: "+e);Reply(aircraft,"Unable, "+e.GetBaseException().Message);}
            },error=>{if(r.Generation!=generation)return;r.Planning=false;Reply(aircraft,"Unable, "+error);});}
            catch{r.Planning=false;throw;}
        }
    }
}
