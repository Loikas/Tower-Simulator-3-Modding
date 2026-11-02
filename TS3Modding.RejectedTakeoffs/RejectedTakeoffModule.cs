using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TS3Modding.API;
using UnityEngine;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed partial class RejectedTakeoffModule:ITS3Module
    {
        private const string Hash="26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7";
        private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static RejectedTakeoffModule current;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.rejectedtakeoffs");
        private readonly ConfigEntry<bool> enabled,test;
        private readonly ConfigEntry<float> chance,interval,checks;private readonly RecoveryOutcomeSettings outcomes;
        private readonly ConfigFile config;
        private IFrameworkContext context;
        private Type planeType,gameType;
        private FieldInfo gameInstance,commandKind;
        private MethodInfo cancel,speedInKnots,send,sayName,multiplayer;
        private readonly Dictionary<string,FieldInfo> fields=new Dictionary<string,FieldInfo>();
        private readonly Dictionary<Component,object> sampled=new Dictionary<Component,object>();
        private readonly Dictionary<Component,Incident> incidents=new Dictionary<Component,Incident>();
        private readonly List<IDisposable> settings=new List<IDisposable>();
        private readonly System.Random random=new System.Random();
        private sealed class Trigger {internal object Schedule;internal double Target,Maximum;}
        private readonly Dictionary<Component,Trigger> triggers=new Dictionary<Component,Trigger>();
        private object session;private double clock,nextIncident;private float lastDay;private bool clockStarted;
        private readonly Dictionary<Component,double> runwayRefusals=new Dictionary<Component,double>();
        private sealed class Incident {internal object Runway,RecoveryRoute,ReturnStand;internal bool Returned,TugRequested;internal IDisposable TugTicket;internal bool RunwayOccupied=true;internal int ClearSamples;internal object Schedule,Game;internal string Callsign;internal double Stopped,ChecksDuration;internal bool Ready,StopReported;internal double LastRefusal=-100;}
        public string Id=>"nl.ts3modding.rejectedtakeoffs";
        internal RejectedTakeoffModule(ConfigFile config,ConfigEntry<bool> enabled,ConfigEntry<float> chance,ConfigEntry<float> interval,ConfigEntry<float> checks,ConfigEntry<bool> test,RecoveryOutcomeSettings outcomes)
        {this.config=config;this.enabled=enabled;this.chance=chance;this.interval=interval;this.checks=checks;this.test=test;this.outcomes=outcomes;maintenanceOptions=new MaintenanceSettings(config);}
        private FieldInfo F(Type t,string name){var key=t.FullName+"/"+name;FieldInfo f;if(!fields.TryGetValue(key,out f))fields[key]=f=t.GetField(name,Flags)??throw new MissingFieldException(t.FullName,name);return f;}
        private object Get(object o,string name)=>F(o.GetType(),name).GetValue(o);
        private void Set(object o,string name,object value)=>F(o.GetType(),name).SetValue(o,value);
        public void Initialize(IFrameworkContext context)
        {
            this.context=context;if(context.GameAssemblySha256!=Hash)throw new NotSupportedException("Unreviewed game assembly.");
            var a=AppDomain.CurrentDomain.GetAssemblies().Single(x=>x.GetName().Name=="Assembly-CSharp");
            planeType=a.GetType("Airplane",true);gameType=a.GetType("Game",true);gameInstance=F(gameType,"instance");
            var command=a.GetType("DHOPLOEENOK+MCPCADKMMAI",true);commandKind=F(command,"command");
            cancel=planeType.GetMethod("RunCancelTakeoff",Flags,null,new[]{command},null);speedInKnots=planeType.GetMethod("GetSpeedInKnot",Flags,null,Type.EmptyTypes,null);sayName=planeType.GetMethod("get__sayname",Flags);
            multiplayer=a.GetType("TCP",true).GetMethod("get_mp_state",Flags);
            send=a.GetType("CommandWin",true).GetMethod("SendMsg",Flags,null,new[]{typeof(string),a.GetType("MFECMBFOJCM+LEHLCPMEDNK",true),typeof(string),planeType,a.GetType("MFECMBFOJCM+PLOKIFJGPLC",true),typeof(string)},null);
            if(cancel==null||speedInKnots==null||send==null||sayName==null||multiplayer==null)throw new NotSupportedException("Rejected-takeoff native contract changed.");
            current=this;InitializeMaintenance(a);
            standMarkerPath=TS3Modding.DisplayBridge.TowStandTelemetry.PathForGame(BepInEx.Paths.GameRootPath)+".rto";
            try{standDisplay=new TS3Modding.DisplayBridge.TowStandDisplay(a,ReturnStandMarkers,context.Log,"nl.ts3modding.rejectedtakeoffs.standdisplay");}catch(Exception e){context.Log("Taxi-back stand display unavailable: "+e.Message);}
            harmony.Patch(a.GetType("DHOPLOEENOK",true).GetMethod("HAEDOMLCHKP",Flags),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(ReturnTaxiTargetPrefix)));
            harmony.Patch(planeType.GetMethod("SetState",Flags),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(ReturnedStandPrefix)));
            harmony.Patch(planeType.GetMethod("GHINOHCGAAD",Flags),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(DrivePrefix)),transpiler:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(RouteProgressTranspiler)));
            harmony.Patch(planeType.GetMethods(Flags).Single(m=>m.Name=="RunCommand"),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(CommandPrefix)){priority=Priority.First});
            harmony.Patch(planeType.GetMethod("RunRunwayGround",Flags),transpiler:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(ReplanTranspiler)));
            harmony.Patch(a.GetType("DHOPLOEENOK",true).GetMethod("MKECMJOHINE",Flags),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(ValidationPrefix)){priority=Priority.First});
            harmony.Patch(planeType.GetMethod("FGHBLGBMODD",Flags),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(RecoveryRoutePrefix)));
            harmony.Patch(planeType.GetMethod("SayRoadCalcError",Flags),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(RouteErrorPrefix)));
            if(ModSettingsServices.Current!=null)foreach(var key in config.Keys)settings.Add(ModSettingsServices.Current.RegisterLiveSetting(Id,key.Section,key.Key));
            var visibility=ModSettingsServices.Current as IModSettingsVisibility;
            if(visibility!=null){
                settings.Add(visibility.RequireModsForSection(Id,"Maintenance",new[]{"nl.ts3modding.tugs"},()=>TowRecoveryServices.Available));
                settings.Add(visibility.RequireModsForSetting(Id,"Recovery","TugRecoveryPercent",new[]{"nl.ts3modding.tugs"},()=>TowRecoveryServices.Available));
            }
            context.Log("Rejected Takeoffs 0.5.1 ready: retry, taxi-back and optional tug recovery outcomes; native braking and checked runway exits.");
        }
        public void Tick()
        {
            PublishReturnStandMarkers();
            if(TowRecoveryServices.Available&&outcomes.Tug==null){outcomes.BindTugRecovery(config);if(ModSettingsServices.Current!=null)settings.Add(ModSettingsServices.Current.RegisterLiveSetting(Id,"Recovery","TugRecoveryPercent"));}
            if(test.Value&&!Preferences.DeveloperMode.Enabled)test.Value=false;
            TickRecoveryPlans();
            var game=gameInstance.GetValue(null) as UnityEngine.Object;
            if(!game){TickMaintenance(game);session=null;sampled.Clear();triggers.Clear();ReleaseStandOffers();incidents.Clear();runwayRefusals.Clear();clockStarted=false;return;}
            if(!ReferenceEquals(session,game)){session=game;sampled.Clear();triggers.Clear();ReleaseStandOffers();incidents.Clear();runwayRefusals.Clear();clock=0;nextIncident=0;clockStarted=false;}
            TickMaintenance(game);
            if((bool)Get(game,"loading"))return;
            var day=(float)Get(game,"daytime");
            if(clockStarted){double delta=day-lastDay;if(delta<-.5)delta+=1;if(delta>=0&&delta<.1)clock+=delta*86400;}lastDay=day;clockStarted=true;
            foreach(var plane in sampled.Keys.Where(p=>!p).ToArray())sampled.Remove(plane);
            foreach(var plane in triggers.Keys.Where(p=>!p).ToArray())triggers.Remove(plane);
            foreach(var pair in incidents.ToArray())if(!Same(pair.Key,pair.Value)){ReleaseStandOffer(pair.Key,pair.Value);incidents.Remove(pair.Key);}
            if(!enabled.Value){ReleaseStandOffers();incidents.Clear();runwayRefusals.Clear();return;}
            foreach(var pair in incidents.ToArray())
            {
                var incident=pair.Value;if(!incident.RunwayOccupied||incident.Runway==null)continue;
                double remaining,distance,alignment;
                if(!RunwayGeometry(pair.Key,incident.Runway,out remaining,out distance,out alignment))continue;
                var type=Get(pair.Key,"_type");
                if(RecoveryRules.Vacated(distance,(float)Get(incident.Runway,"width")*.3048,(float)Get(type,"_length"),(float)Get(type,"_width"),(float)Get(pair.Key,"_alt")))incident.ClearSamples++;else incident.ClearSamples=0;
                if(incident.ClearSamples>=2){incident.RunwayOccupied=false;context.Log("Rejected-takeoff runway vacated by "+incident.Callsign);}
            }
        }
        private bool Same(Component plane,Incident incident)=>plane&&ReferenceEquals(gameInstance.GetValue(null),incident.Game)&&ReferenceEquals(Get(plane,"_timetable"),incident.Schedule)&&(string)Get(plane,"_name")==incident.Callsign&&!(bool)Get(plane,"_deleted");
        private static void DrivePrefix(Component __instance)
        {try{current?.Drive(__instance);}catch(Exception e){current?.context.Log("Rejected-takeoff observer skipped: "+e.Message);}}
        private void Drive(Component plane)
        {
            if(test.Value&&!Preferences.DeveloperMode.Enabled)test.Value=false;
            if(!enabled.Value||session==null||!ReferenceEquals(session,gameInstance.GetValue(null))||(bool)Get(session,"loading")||Convert.ToInt32(multiplayer.Invoke(null,null))!=0)return;
            Incident incident;
            if(incidents.TryGetValue(plane,out incident)&&Same(plane,incident))
            {
                var dt=(float)Get(plane,"KJEOBOIKNPD");var speed=(float)Get(plane,"_lv");
                if(dt>0&&dt<=.2&&Math.Abs(speed)<=.1)incident.Stopped+=dt;
                if(!incident.StopReported&&Math.Abs(speed)<=.1){incident.StopReported=true;Set(plane,"_ready_to_taxi",true);Set(plane,"_routestart",null);}
                if(!incident.Ready&&RejectionPolicy.Ready(speed,incident.Stopped,incident.ChecksDuration))
                {
                    incident.Ready=true;Set(plane,"_takeofftravel",0f);
                    var roll=random.NextDouble()*100;var taxiChance=RecoveryOutcomePolicy.Percent(outcomes.Taxi.Value);
                    var tugChance=TowRecoveryServices.Available&&outcomes.Tug!=null?RecoveryOutcomePolicy.Limit(outcomes.Tug.Value,taxiChance):0;
                    if(roll<taxiChance&&OfferReturnStand(plane,incident)){}
                    else if(roll>=taxiChance&&roll<taxiChance+tugChance&&OfferReturnStand(plane,incident,true)){}
                    else Report(plane,"Checks complete, ready to taxi for another departure");
                }
                CompleteStandReturn(plane,incident);
                return;
            }
            if(Convert.ToInt32(Get(plane,"_state"))!=14)return;
            var schedule=Get(plane,"_timetable");object previous;if(sampled.TryGetValue(plane,out previous)&&ReferenceEquals(previous,schedule))return;
            var runway=Get(plane,"_trgrunway");if(runway==null)return;
            double remaining,offset,alignment;if(!RunwayGeometry(plane,runway,out remaining,out offset,out alignment))return;
            Trigger trigger;
            if(!triggers.TryGetValue(plane,out trigger)||!ReferenceEquals(trigger.Schedule,schedule))
            {
                var type=Get(plane,"_type");var vmax=(float)type.GetType().GetMethod("CJMKPIHMEGG",Flags).Invoke(type,null)*1.9438444;
                trigger=new Trigger{Schedule=schedule,Target=RejectionPolicy.TriggerKnots(vmax,random.NextDouble()),Maximum=Math.Min(110,vmax*.7)};triggers[plane]=trigger;
                context.Log("Rejected-takeoff trigger armed: "+Get(plane,"_name")+", target="+trigger.Target.ToString("0.0")+" knots, aircraft roll maximum="+vmax.ToString("0.0"));
            }
            if(trigger.Target<=0||(float)speedInKnots.Invoke(plane,null)<trigger.Target)return;
            if(!RejectionPolicy.Eligible(14,Convert.ToInt32(Get(plane,"_substate")),(bool)Get(plane,"_active"),(bool)Get(plane,"_deleted"),(bool)Get(plane,"_takeoff"),(bool)Get(plane,"_lineup_and_wait"),(float)Get(plane,"_alt"),(float)speedInKnots.Invoke(plane,null),(float)Get(plane,"_lv"),(float)Get(plane,"KJEOBOIKNPD"),remaining,offset,alignment,trigger.Maximum))return;
            if(!test.Value&&clock<nextIncident)return;
            sampled[plane]=schedule;
            if(!test.Value&&random.NextDouble()*100>=chance.Value)return;
            cancel.Invoke(plane,new object[]{null});
            incidents[plane]=new Incident{ChecksDuration=RejectionPolicy.ChecksDuration(checks.Value,random.NextDouble()),Runway=runway,Schedule=schedule,Game=session,Callsign=(string)Get(plane,"_name")};nextIncident=clock+interval.Value*60;
            if(test.Value)test.Value=false;
            context.Log("Rejected takeoff: "+Get(plane,"_name")+", remaining="+remaining.ToString("0")+", offset="+offset.ToString("0.0"));
            Report(plane,"Rejecting takeoff, stopping on the runway and performing checks");
        }
        private bool RunwayGeometry(Component plane,object runway,out double remaining,out double offset,out double alignment)
        {
            remaining=0;offset=double.MaxValue;alignment=-1;
            var points=Get(runway,"points") as IList; if(points==null||points.Count<2||points.Count>20000)return false;
            var position=(Vector3)Get(plane,"_pos");var heading=(float)Get(plane,"_rot")*Math.PI/180;
            var forwardX=Math.Sin(heading);var forwardZ=Math.Cos(heading);double tail=0;
            for(var i=points.Count-2;i>=0;i--)
            {
                var a=(Vector3)points[i];var b=(Vector3)points[i+1];double x=b.x-a.x,z=b.z-a.z,len=Math.Sqrt(x*x+z*z);if(len<.001)continue;
                var t=Math.Max(0,Math.Min(1,((position.x-a.x)*x+(position.z-a.z)*z)/(len*len)));
                var dx=position.x-a.x-t*x;var dz=position.z-a.z-t*z;var distance=Math.Sqrt(dx*dx+dz*dz);
                if(distance<offset){offset=distance;remaining=tail+(1-t)*len;alignment=(x*forwardX+z*forwardZ)/len;}
                tail+=len;
            }
            return offset<double.MaxValue;
        }
        private static IEnumerable<CodeInstruction> ReplanTranspiler(IEnumerable<CodeInstruction> code)
            =>RecoveryRules.InjectReplan(code,current.F(current.planeType,"_negative_action"),typeof(RejectedTakeoffModule).GetMethod(nameof(ForceReplan),Flags));
        private static bool ForceReplan(bool native,object plane,object command,object commands)
        {
            var self=current;if(self==null||!self.enabled.Value)return native;Incident incident;
            var owned=self.incidents.TryGetValue((Component)plane,out incident)&&self.Same((Component)plane,incident);
            var combined=((IEnumerable)commands).Cast<object>().Any(c=>RejectionPolicy.TakeoffClearance(Convert.ToInt32(self.commandKind.GetValue(c))));
            var result=RecoveryRules.Replan(native,owned,Convert.ToInt32(self.Get(plane,"_state")),(float)self.Get(plane,"_lv"),Convert.ToInt32(self.commandKind.GetValue(command)),combined);
            if(result&&!native)self.context.Log("Rejected-takeoff recovery: forcing fresh native runway taxi route for "+self.Get(plane,"_name"));
            return result;
        }
        private static float InitialRouteDistance(float distance,Component plane)
        {
            var self=current;Incident incident;
            var owned=self!=null&&self.enabled.Value&&self.incidents.TryGetValue(plane,out incident)&&self.Same(plane,incident)&&incident.RecoveryRoute!=null&&ReferenceEquals(incident.RecoveryRoute,self.Get(plane,"_route"));
            return RecoveryRules.InitialDistance(distance,owned);
        }
        private static IEnumerable<CodeInstruction> RouteProgressTranspiler(IEnumerable<CodeInstruction> source)
            =>RecoveryRules.InjectInitialDistance(source,typeof(RejectedTakeoffModule).GetMethod(nameof(InitialRouteDistance),Flags));
        private static bool ValidationPrefix(object __instance,Component __0,object __1,ref bool __result)
        {
            if(current==null||!current.enabled.Value)return true;
            foreach(var command in ((IEnumerable)__1).Cast<object>())
                if(!CommandPrefix(__0,command,__1)){((IList)current.Get(__instance,"OCBFAJNOCDK")).Clear();__result=false;return false;}
            return true;
        }
        private static void RouteErrorPrefix(Component __instance)
        {
            var self=current;Incident incident;
            if(self!=null&&self.incidents.TryGetValue(__instance,out incident)&&self.Same(__instance,incident))
                self.context.Log("Rejected-takeoff native route failed for "+incident.Callsign+"; state="+self.Get(__instance,"_state")+", start="+(self.Get(__instance,"_routestart")==null?"none":self.Get(self.Get(__instance,"_routestart"),"name"))+". Native refusal retained; no unchecked route substituted.");
        }
        private object CommandRunway(object command,object commands,Component plane)
        {
            var get=command.GetType().GetMethod("LHDHLGGPCNL",Flags);
            foreach(var item in new[]{command}.Concat(((IEnumerable)commands).Cast<object>()))
                try{var road=get.Invoke(item,new[]{Enum.ToObject(get.GetParameters()[0].ParameterType,2),(object)1});if(road!=null)return road;}catch{}
            return Get(plane,"_trgrunway");
        }
        private static bool CommandPrefix(Component __instance,object __0,object __1)
        {
            var self=current;if(self==null||!self.enabled.Value)return true;
            var kind=Convert.ToInt32(self.commandKind.GetValue(__0));
            if(RejectionPolicy.TakeoffClearance(kind)&&Convert.ToInt32(self.multiplayer.Invoke(null,null))==0)
            {
                var runway=self.CommandRunway(__0,__1,__instance);
                if(runway!=null)
                {
                    var key=RecoveryRules.RunwayKey((string)self.Get(runway,"name"));
                    if(self.incidents.Any(pair=>!ReferenceEquals(pair.Key,__instance)&&self.Same(pair.Key,pair.Value)&&pair.Value.RunwayOccupied&&(ReferenceEquals(runway,pair.Value.Runway)||key!=null&&key==RecoveryRules.RunwayKey((string)self.Get(pair.Value.Runway,"name")))))
                    {
                        double last;if(!self.runwayRefusals.TryGetValue(__instance,out last)||self.clock-last>3){self.runwayRefusals[__instance]=self.clock;self.Report(__instance,"Unable, runway occupied following a rejected takeoff");}
                        return false;
                    }
                }
            }
            Incident incident;
            if(!self.incidents.TryGetValue(__instance,out incident)||!self.Same(__instance,incident))return true;
            if(RecoveryRules.BlockLineup(kind,incident.RunwayOccupied))
            {
                if(self.clock-incident.LastRefusal>3){incident.LastRefusal=self.clock;self.Report(__instance,"Unable, vacate the runway before lining up again");}
                return false;
            }
            if(incident.Ready){if(!self.CheckStandCommand(__instance,incident,__0,kind))return false;return true;}
            if(!RejectionPolicy.RequiresChecks(kind))return true;
            if(self.clock-incident.LastRefusal>3){incident.LastRefusal=self.clock;self.Report(__instance,"Unable, still performing checks after rejected takeoff");}
            return false;
        }
        private void Report(Component plane,string message,string spoken=null)
        {
            var game=Get(plane,"BMCCOLLPALC");var window=Get(game,"cmdwin");var p=send.GetParameters();
            send.Invoke(window,new object[]{Get(plane,"_name")+": "+message,Enum.ToObject(p[1].ParameterType,4),(spoken??message)+", "+sayName.Invoke(plane,null),plane,Enum.ToObject(p[4].ParameterType,3),null});
        }
        private void ResetIncidentsForLoad(){ReleaseStandOffers();CancelRecoveryPlans();incidents.Clear();sampled.Clear();triggers.Clear();runwayRefusals.Clear();session=null;clock=0;nextIncident=0;clockStarted=false;}
        public void Dispose(){maintenanceProvider?.Dispose();maintenanceProvider=null;maintenance.Clear();ReleaseStandOffers();CancelRecoveryPlans();standDisplay?.Dispose();standDisplay=null;harmony.UnpatchSelf();foreach(var token in settings)token.Dispose();settings.Clear();incidents.Clear();sampled.Clear();if(standMarkerPath!=null)try{TS3Modding.DisplayBridge.TowStandTelemetry.WriteMany(standMarkerPath,new TS3Modding.DisplayBridge.TowStandMarker[0]);}catch(Exception e){context?.Log("Taxi-back marker cleanup failed: "+e.Message);}if(ReferenceEquals(current,this))current=null;}
    }
}
