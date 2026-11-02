using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using TS3Modding.API;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed partial class RejectedTakeoffModule
    {
        private static string StandIdentifier(string name)=>Regex.Replace(name??"",@"^(gate|stand|parking)[ _-]*","",RegexOptions.IgnoreCase).Replace("_","").Replace(" ","").ToUpperInvariant();
        private static string StandSpeech(string name)
        {
            var alphabet=new[]{"alfa","bravo","charlie","delta","echo","foxtrot","golf","hotel","india","juliet","kilo","lima","mike","november","oscar","papa","quebec","romeo","sierra","tango","uniform","victor","whiskey","x ray","yankee","zulu"};
            var digits=new[]{"zero","one","two","three","four","five","six","seven","eight","niner"};
            return string.Join(" ",StandIdentifier(name).Select(c=>c>='A'&&c<='Z'?alphabet[c-'A']:c>='0'&&c<='9'?digits[c-'0']:"").Where(s=>s.Length>0).ToArray());
        }
        private bool StandAvailable(Component plane,object stand)
        {
            if(stand==null||(bool)Get(stand,"occupied")||Get(stand,"locked")!=null&&!ReferenceEquals(Get(stand,"locked"),plane))return false;
            var game=Get(plane,"BMCCOLLPALC");
            return !((IEnumerable)Get(game,"active_airplanes")).Cast<object>().Any(p=>!ReferenceEquals(p,plane)&&!(bool)Get(p,"_deleted")&&ReferenceEquals(Get(p,"_terminal"),stand));
        }
        private bool OfferReturnStand(Component plane,Incident incident,bool tow=false)
        {
            object stand=null;
            try
            {
                if(Get(plane,"_terminal")!=null)return false;
                var airport=Get(incident.Game,"airfield");
                // Native selection applies airline/terminal filters and aircraft size limits, then reserves it.
                stand=airport.GetType().GetMethod("PPBAPGBFPKK",Flags).Invoke(airport,new object[]{plane});
                if(!StandAvailable(plane,stand)||!ReferenceEquals(Get(stand,"locked"),plane))
                {if(stand!=null&&ReferenceEquals(Get(stand,"locked"),plane))Set(stand,"locked",null);return false;}
                incident.ReturnStand=stand;
                var name=(string)Get(stand,"name");
                if(tow)
                {
                    incident.TugRequested=true;
                    var request=RecoveryRequest(plane,incident,stand);
                    var ticket=TowRecoveryServices.Request(request);
                    if(ticket==null){incident.TugRequested=false;incident.ReturnStand=null;Set(stand,"locked",null);return false;}
                    incident.TugTicket=ticket;
                    Report(plane,"Checks complete, unable to taxi, request tow to stand "+(request.DestinationAlias??StandIdentifier(name)),"Checks complete, unable to taxi, request tow to stand "+StandSpeech(request.DestinationAlias??name));
                    return true;
                }
                Report(plane,"Checks complete, unable to continue departure, request taxi to terminal, stand "+StandIdentifier(name),"Checks complete, unable to continue departure, request taxi to terminal, stand "+StandSpeech(name));
                context.Log("Rejected-takeoff return stand reserved: "+incident.Callsign+" -> "+name);
                return true;
            }
            catch(Exception error)
            {
                incident.TugTicket?.Dispose();incident.TugTicket=null;
                incident.TugRequested=false;incident.ReturnStand=null;
                if(stand!=null&&ReferenceEquals(Get(stand,"locked"),plane))Set(stand,"locked",null);
                context.Log("Rejected-takeoff return stand unavailable: "+error.GetBaseException().Message);return false;
            }
        }
        private TowRecoveryRequest RecoveryRequest(Component plane,Incident incident,object stand)=>new TowRecoveryRequest{
            Aircraft=plane,Runway=incident.Runway,Stand=stand,
            IsValid=()=>enabled.Value&&Same(plane,incident)&&incidents.ContainsKey(plane)&&ReferenceEquals(Get(stand,"locked"),plane),
            PlanRoute=(index,vias,done,failed)=>BeginRecoveryPlan(plane,incident,stand,index,vias,done,failed),
            Completed=()=>CompleteTugRecovery(plane,incident)};
        private void ReleaseStandOffer(Component plane,Incident incident)
        {
            incident.TugTicket?.Dispose();incident.TugTicket=null;
            if(incident.ReturnStand!=null&&ReferenceEquals(Get(incident.ReturnStand,"locked"),plane)&&(!plane||!ReferenceEquals(Get(plane,"_terminal"),incident.ReturnStand)))Set(incident.ReturnStand,"locked",null);
        }
        private void ReleaseStandOffers(){foreach(var pair in incidents)ReleaseStandOffer(pair.Key,pair.Value);}
        private bool CheckStandCommand(Component plane,Incident incident,object command,int kind)
        {
            if(incident.ReturnStand==null||incident.Returned)return true;
            if(incident.TugRequested&&(RejectionPolicy.RequiresChecks(kind)||kind==2||kind==15||kind==16||kind==17||kind==27||kind==28))
            {if(clock-incident.LastRefusal>3){incident.LastRefusal=clock;Report(plane,"Unable, we need a tow to stand "+StandSpeech((string)Get(incident.ReturnStand,"name")));}return false;}
            var blocked=RejectionPolicy.TakeoffClearance(kind)||kind==27||kind==28||kind==2;
            if(kind==1)
            {
                var target=command.GetType().GetMethod("NNHMCGMFGNI",Flags);
                var word=target.Invoke(command,new[]{Enum.ToObject(target.GetParameters()[0].ParameterType,5),(object)1});
                var identifier=word==null?null:(string)Get(word,"value");
                if(!StandReturnPolicy.Matches(identifier,(string)Get(incident.ReturnStand,"name")))blocked=true;
                else if(!StandAvailable(plane,incident.ReturnStand))
                {
                    if(clock-incident.LastRefusal>3){incident.LastRefusal=clock;Report(plane,"Unable, requested stand is no longer available, hold position");}return false;
                }
                else Set(plane,"_terminal",incident.ReturnStand);
            }
            if(!blocked)return true;
            if(clock-incident.LastRefusal>3){incident.LastRefusal=clock;Report(plane,"Unable, we need to return to stand "+StandSpeech((string)Get(incident.ReturnStand,"name")));}
            return false;
        }
        private static bool ReturnTaxiTargetPrefix(object __instance,object __0,ref bool __result)
        {
            var self=current;if(self==null||!self.enabled.Value)return true;
            var plane=self.Get(__instance,"BNMGGAOAPNE") as Component;Incident incident;
            if(!plane||!self.incidents.TryGetValue(plane,out incident)||!self.Same(plane,incident)||!incident.Ready||incident.ReturnStand==null||incident.Returned||incident.TugRequested)return true;
            var method=__0.GetType().GetMethod("NNHMCGMFGNI",Flags);
            var word=method.Invoke(__0,new[]{Enum.ToObject(method.GetParameters()[0].ParameterType,5),(object)1});
            if(word==null||!StandReturnPolicy.Matches((string)self.Get(word,"value"),(string)self.Get(incident.ReturnStand,"name"))||!self.StandAvailable(plane,incident.ReturnStand))return true;
            self.Set(__0,"config","terminal");self.Set(plane,"_terminal",incident.ReturnStand);
            self.Set(word,"sayvalue","terminal, stand "+StandSpeech((string)self.Get(incident.ReturnStand,"name")));
            __result=true;return false;
        }
        private static void ReturnedStandPrefix(Component __instance,object[] __args)
        {
            var self=current;Incident incident;
            if(self==null||!self.enabled.Value||Convert.ToInt32(__args[0])!=16||!self.incidents.TryGetValue(__instance,out incident)||!self.Same(__instance,incident)||incident.ReturnStand==null||!ReferenceEquals(self.Get(__instance,"_terminal"),incident.ReturnStand)||!(bool)self.Get(__instance,"_imatterminal"))return;
            incident.Returned=true;__args[0]=Enum.ToObject(__args[0].GetType(),8);
        }
        private void CompleteStandReturn(Component plane,Incident incident)
        {
            if(!incident.Returned||Convert.ToInt32(Get(plane,"_state"))!=8)return;
            Set(incident.ReturnStand,"occupied",true);
            StartMaintenance(plane,incident.Game,false);
            Report(plane,"Back on stand, shutting down for maintenance");
            context.Log("Rejected-takeoff stand return completed: "+incident.Callsign+" at "+Get(incident.ReturnStand,"name"));
            incidents.Remove(plane);
        }
    }
}
