using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace TS3Modding.Tugs
{
    internal sealed partial class NativeTowTransfer
    {
        private static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
        private readonly HashSet<Component> quarantined=new HashSet<Component>();
        private readonly Dictionary<Job,Tuple<bool,float,Vector3>> saving=new Dictionary<Job,Tuple<bool,float,Vector3>>();
        private List<Dictionary<string,string>> readRecords,pendingRestore;private object restoreGame;
        private readonly ConditionalWeakTable<object,List<Dictionary<string,string>>> loaded=new ConditionalWeakTable<object,List<Dictionary<string,string>>>();
        internal Func<List<Dictionary<string,string>>> ExportRequests;
        internal Action<object,Dictionary<string,string>,Component> ImportRequest;
        internal Action ForgetRequests;
        internal Func<Component,Action> RecoveryCompletion;
        internal Action<Component,Dictionary<string,string>> ExportApproach;
        internal Action<Component,Component,TowRoadSnapshot[],Dictionary<string,string>> ImportApproach;
        internal bool NeedsRecovery(Component p)=>jobs.Any(j=>ReferenceEquals(j.Plane,p)&&j.Fault!=null);
        private static string V(Vector3 p)=>string.Join(",",new[]{p.x.ToString("R",Invariant),p.y.ToString("R",Invariant),p.z.ToString("R",Invariant)});
        private static Vector3 V(string s){var n=s.Split(',').Select(x=>float.Parse(x,Invariant)).ToArray();if(n.Length!=3||n.Any(x=>float.IsNaN(x)||float.IsInfinity(x)))throw new InvalidDataException("Invalid saved position.");return new Vector3(n[0],n[1],n[2]);}
        private static bool Scalar(Type t)=>t==typeof(string)||t==typeof(bool)||t==typeof(int)||t==typeof(float)||t==typeof(double)||t==typeof(Vector3)||t==typeof(Vector3[])||t==typeof(List<Vector3>)||t==typeof(string[]);
        private static string Pack(object v,Type t)
        {
            if(v==null)return "";
            if(t==typeof(Vector3))return V((Vector3)v);
            if(t==typeof(Vector3[])||t==typeof(List<Vector3>))return string.Join(";",((IEnumerable<Vector3>)v).Select(V));
            if(t==typeof(string[]))return string.Join(";",(string[])v);
            if(t==typeof(float))return ((float)v).ToString("R",Invariant);
            if(t==typeof(double))return ((double)v).ToString("R",Invariant);
            return Convert.ToString(v,Invariant);
        }
        private static object Unpack(string v,Type t)
        {
            if(t==typeof(string))return v.Length==0?null:v;
            if(t==typeof(Vector3))return V(v);
            if(t==typeof(Vector3[])||t==typeof(List<Vector3>)){var a=v.Length==0?new Vector3[0]:v.Split(';').Select(V).ToArray();if(a.Length>20000)throw new InvalidDataException("Saved route too large.");return t==typeof(Vector3[])?(object)a:a.ToList();}
            if(t==typeof(string[]))return v.Length==0?new string[0]:v.Split(';');
            var result=Convert.ChangeType(v,t,Invariant);
            if(t==typeof(float)&&(float.IsNaN((float)result)||float.IsInfinity((float)result))||t==typeof(double)&&(double.IsNaN((double)result)||double.IsInfinity((double)result)))throw new InvalidDataException("Invalid saved number.");
            return result;
        }
        private static string Geometry(TowRoadSnapshot[] roads)
        {
            using(var h=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("|",roads.Select(r=>r.Id+":"+r.Type+":"+r.Name+":"+r.ProtectionRadius.ToString("R",Invariant)+":"+string.Join(";",r.Points.Select(p=>V(new Vector3(p.X,p.Y,p.Z)))))))));
        }
        internal List<Dictionary<string,string>> ExportState()
        {
            var records=ExportRequests?.Invoke()??new List<Dictionary<string,string>>();
            foreach(var j in jobs)
            {
                Validate(j);if(!j.Route.SequenceEqual(j.CheckedRoute))PrepareCrossings(j);if(j.ReplanPending)throw new InvalidOperationException("Wait for the tow route calculation before saving.");
                var d=new Dictionary<string,string>{{"kind","transfer"},{"name",j.Callsign},{"net",Convert.ToString(Get(j.Plane,"_netidx"),Invariant)},{"position",V(Get<Vector3>(j.Plane,"_pos"))},{"truckPosition",V(j.Truck.transform.position)},{"truckRotation",V(j.Truck.transform.eulerAngles)},{"coupled",Get<bool>(j.Truck,"_car_linked").ToString()},{"source",Get<string>(j.Source,"name")},{"geometry",Geometry(j.Roads)},{"cursor",Convert.ToString(Get(j.Plane,"_routepos"),Invariant)},{"event",j.Crossings.Event.ToString(Invariant)},{"occupying",j.Crossings.Occupying.ToString()},{"cleared",j.Crossings.Cleared.ToString()},{"crossHolding",j.Crossings.Holding.ToString()},{"added",string.Join(",",j.AddedCrossings.Select(r=>Array.IndexOf(j.NativeRoads,r).ToString(Invariant)))},{"sourceLock",ReferenceEquals(j.SourceLock,j.Plane).ToString()},{"holdRoad",j.HoldRoad?.Id.ToString(Invariant)??"-1"}};
                foreach(var f in typeof(Job).GetFields(Flags).Where(f=>Scalar(f.FieldType)))d.Add("j."+f.Name,Pack(f.GetValue(j),f.FieldType));
                d["crossOrigin"]=j.Crossings.OccupiedOrigin??"";d["initialVacated"]=string.Join(";",j.Crossings.InitialVacatedRunways);
                if(j.IncidentRecovery)ExportApproach?.Invoke(j.Plane,d);
                records.Add(d);
            }
            return records;
        }
        private static bool SavePrefix()
        {
            var self=current;if(self==null)return true;
            try
            {
                if(self.quarantined.Count!=0)throw new InvalidOperationException("Unrestored tow records require the original compatible installation before saving.");
                if(self.AllowSave?.Invoke()==false)return false;
                self.ExportState(); // Validate before cancelling a pending request or changing any live fields.
                self.BeforeSave?.Invoke();
                foreach(var j in self.jobs){self.saving.Add(j,Tuple.Create(self.Get<bool>(j.Plane,"_hold_position_taxistop"),self.Get<float>(j.Plane,"_lv"),self.Get<Vector3>(j.Plane,"_v")));self.Set(j.Plane,"_hold_position_taxistop",true);self.Set(j.Plane,"_lv",0f);self.Set(j.Plane,"_v",new Vector3());}
                return true;
            }
            catch(Exception error){SaveFinished(error);self.Message="Unable to save tow: "+error.Message;self.log("[Tugs] "+self.Message);foreach(var j in self.jobs)self.reply(j.Plane,"Unable to save, "+error.Message);return false;}
        }
        private static Exception SaveFinished(Exception __exception)
        {
            var self=current;if(self==null)return __exception;
            foreach(var pair in self.saving)if(self.Same(pair.Key)){self.Set(pair.Key.Plane,"_hold_position_taxistop",pair.Value.Item1);self.Set(pair.Key.Plane,"_lv",pair.Value.Item2);self.Set(pair.Key.Plane,"_v",pair.Value.Item3);}
            self.saving.Clear();return __exception;
        }
        private static IEnumerable<CodeInstruction> ReplaceIO(IEnumerable<CodeInstruction> code,string original,string replacement)
        {
            int count=0;foreach(var i in code){if(i.operand is MethodInfo m&&m.DeclaringType==typeof(File)&&m.Name==original){i.opcode=OpCodes.Call;i.operand=typeof(NativeTowTransfer).GetMethod(replacement,Flags);count++;}yield return i;}
            if(count!=1)throw new NotSupportedException("Native tow save IO boundary changed.");
        }
        private static IEnumerable<CodeInstruction> SaveTranspiler(IEnumerable<CodeInstruction> instructions)=>ReplaceIO(instructions,"WriteAllText",nameof(WriteSave));
        private static IEnumerable<CodeInstruction> LoadTranspiler(IEnumerable<CodeInstruction> instructions)=>ReplaceIO(instructions,"ReadAllText",nameof(ReadSave));
        private static void WriteSave(string path,string json)
        {
            var self=current;var text=self==null?json:TowSaveCodec.Attach(json,self.ExportState());
            var temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
            try{File.WriteAllText(temporary,text);if(File.Exists(path))File.Replace(temporary,path,path+".previous-"+Guid.NewGuid().ToString("N"));else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            self?.log("[Tugs] Native save written atomically with versioned tow metadata: "+path);
        }
        private static string ReadSave(string path){if(current!=null)current.readRecords=null;var text=File.ReadAllText(path);if(current!=null)current.readRecords=TowSaveCodec.Read(text);return text;}
        private static void LoadedPostfix(object __result){if(current==null||__result==null)return;current.loaded.Remove(__result);current.loaded.Add(__result,current.readRecords??new List<Dictionary<string,string>>());current.readRecords=null;}
        private static void RestorePrefix(){var self=current;if(self==null)return;foreach(var j in self.jobs){j.Replan?.Dispose();self.RestoreSound(j);}self.jobs.Clear();self.deferredReplans.Clear();self.supersededReplans.Clear();self.job=null;self.quarantined.Clear();self.pendingRestore=null;self.restoreGame=null;self.ForgetRequests?.Invoke();}
        private static void RestorePostfix(object __instance)
        {
            var self=current;if(self==null)return;List<Dictionary<string,string>> records;if(!self.loaded.TryGetValue(__instance,out records))return;
            var game=self.F(self.gameType,"instance").GetValue(null);self.StageRestore(game,records);self.loaded.Remove(__instance);
        }
        internal void StageRestore(object game,List<Dictionary<string,string>> records)
        {
            pendingRestore=records;restoreGame=game;
            foreach(var d in records)foreach(var p in ((IEnumerable)Get(game,"airplanes")).Cast<Component>().Where(p=>Get<string>(p,"_name")==d["name"]&&Convert.ToString(Get(p,"_netidx"),Invariant)==d["net"]))
            {quarantined.Add(p);Set(p,"_hold_position_taxistop",true);Set(p,"_lv",0f);Set(p,"_v",new Vector3());}
            Message="Restoring saved tows; aircraft held while native tugs initialise.";
        }
        private void TickRestoration()
        {
            if(restoreGame!=null&&!ReferenceEquals(F(gameType,"instance").GetValue(null),restoreGame)){pendingRestore=null;quarantined.Clear();restoreGame=null;return;}
            if(pendingRestore==null)return;
            if(!ReferenceEquals(F(gameType,"instance").GetValue(null),restoreGame)){pendingRestore=null;quarantined.Clear();return;}
            if(Get<bool>(restoreGame,"loading"))return;
            // Gate updates create trucks lazily. Do not race their native initialisation.
            foreach(var d in pendingRestore.Where(d=>d["kind"]=="transfer"))
            {
                if(d.ContainsKey("j.IncidentRecovery")&&bool.Parse(d["j.IncidentRecovery"])){
                    var matches=((IEnumerable)Get(restoreGame,"airplanes")).Cast<Component>().Where(p=>Get<string>(p,"_name")==d["name"]&&Convert.ToString(Get(p,"_netidx"),Invariant)==d["net"]).ToArray();
                    if(matches.Length!=1){Message="Saved recovery identity is missing or ambiguous; tow remains held.";log("[Tugs] "+Message);pendingRestore=null;return;}
                    if(RecoveryCompletion?.Invoke(matches[0])==null)return;
                }
                foreach(var name in d.ContainsKey("j.IncidentRecovery")&&bool.Parse(d["j.IncidentRecovery"])?new[]{d["j.Destination"]}:new[]{d["source"],d["j.Destination"]})
                {
                    var gate=getGate.Invoke(Manager,new object[]{name});if(gate==null)continue;
                    var truck=Get(gate,"truck") as Component;if(!truck)return;
                    var saved=Get(gate,"saved_truck");
                    if(saved!=null){saved.GetType().GetMethod("PLOMGBEOILE",Flags,null,new[]{truck.GetType()},null).Invoke(saved,new object[]{truck});Set(gate,"saved_truck",null);}
                }
            }
            var records=pendingRestore;pendingRestore=null;ImportState(restoreGame,records);
        }
        internal void ImportState(object game,List<Dictionary<string,string>> records)
        {
            restoreGame=game;
            var planes=((IEnumerable)Get(game,"airplanes")).Cast<Component>().ToArray();
            // Quarantine every matching owned flight before attempting any reconstruction.
            foreach(var d in records)foreach(var p in planes.Where(p=>Get<string>(p,"_name")==d["name"]&&Convert.ToString(Get(p,"_netidx"),Invariant)==d["net"]))
            {quarantined.Add(p);Set(p,"_hold_position_taxistop",true);Set(p,"_lv",0f);Set(p,"_v",new Vector3());}
            try
            {
                if(Convert.ToInt32(multiplayer.Invoke(null,null))!=0)throw new InvalidOperationException("Saved tows require single player.");
                foreach(var d in records)
                {
                    var p=planes.Single(p=>Get<string>(p,"_name")==d["name"]&&Convert.ToString(Get(p,"_netidx"),Invariant)==d["net"]);
                    if(Get<bool>(p,"_deleted")||!Get<bool>(p,"_active")||Vector3.Distance(Get<Vector3>(p,"_pos"),V(d["position"]))>.1)throw new InvalidDataException("Saved tow flight/position mismatch.");
                    if(d["kind"]=="request"){if(ImportRequest==null)throw new InvalidDataException("Tow request service unavailable.");ImportRequest(game,d,p);continue;}
                    if(d["kind"]!="transfer")throw new InvalidDataException("Unknown tow record kind.");
                    var incidentRecovery=d.ContainsKey("j.IncidentRecovery")&&bool.Parse(d["j.IncidentRecovery"]);
                    var sourceGate=getGate.Invoke(Manager,new object[]{incidentRecovery?d["j.Destination"]:d["source"]});if(sourceGate==null)throw new InvalidDataException("Saved source stand unavailable.");
                    var source=incidentRecovery?((IEnumerable)Get(Get(Get(game,"airfield"),"aircont"),"roads")).Cast<object>().Single(r=>(string)Get(r,"name")==d["source"]&&Convert.ToInt32(Get(r,"type"))==2):Get(sourceGate,"road");var snapshot=new NativeTowPlanning(assembly).Inspect(game,p,d["name"],source);
                    if(Geometry(snapshot.Roads)!=d["geometry"])throw new InvalidDataException("Airport tow geometry changed since saving.");
                    var j=new Job();foreach(var f in typeof(Job).GetFields(Flags).Where(f=>Scalar(f.FieldType)))f.SetValue(j,Unpack(d["j."+f.Name],f.FieldType));
                    if(j.IncidentRecovery){j.RecoveryCompleted=RecoveryCompletion?.Invoke(p);if(j.RecoveryCompleted==null)throw new InvalidDataException("Saved recovery owner has not been restored.");}
                    j.Game=game;j.Plane=p;j.Schedule=Get(p,"_timetable");j.Source=source;j.SourceGate=sourceGate;j.Truck=Get(sourceGate,"truck") as Component;j.TargetGate=getGate.Invoke(Manager,new object[]{j.Destination});
                    if(j.TargetGate==null||!j.Truck)throw new InvalidDataException("Saved tug/destination unavailable.");
                    j.Target=Get(j.TargetGate,"road");j.TargetTruck=Get(j.TargetGate,"truck");j.OldGate=sourceGate;j.NativeRoads=snapshot.NativeRoads;j.Roads=snapshot.Roads;j.SourceLock=bool.Parse(d["sourceLock"])?p:null;
                    if(j.TargetRoad<0||j.TargetRoad>=j.NativeRoads.Length||!ReferenceEquals(j.Target,j.NativeRoads[j.TargetRoad])||j.NativeRoads.Any(r=>ReferenceEquals(r,j.Target)&&Get<bool>(r,"occupied"))||Get(j.TargetGate,"plane")!=null)throw new InvalidDataException("Saved destination is no longer available.");
                    if(jobs.Any(x=>ReferenceEquals(x.Truck,j.Truck)||ReferenceEquals(x.Plane,p)||ReferenceEquals(x.Target,j.Target)))throw new InvalidDataException("Conflicting saved tow ownership.");
                    foreach(var r in new[]{j.Source,j.Target})if(Get(r,"locked")!=null&&!ReferenceEquals(Get(r,"locked"),p))throw new InvalidDataException("Saved stand is owned by another aircraft.");
                    if(!ReferenceEquals(Get(j.Truck,"_plane"),p))throw new InvalidDataException("Saved tug association mismatch.");
                    var issue=TowRouteSafety.Check(j.Route.Select(Point).ToArray(),Point(j.RouteStart),Point(j.End),j.Roads,true);if(issue!=null)throw new InvalidDataException(issue);
                    j.Crossings=new TowRunwayCrossings(j.Route.Select(Point).ToArray(),j.Roads,j.Footprint,d.ContainsKey("crossOrigin")?(d["crossOrigin"].Length==0?null:d["crossOrigin"]):j.IncidentRecovery?Get<string>(j.Source,"name"):null,vacatedRunways:d.ContainsKey("initialVacated")?d["initialVacated"].Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries):null);
                    j.Crossings.Restore(int.Parse(d["event"],Invariant),bool.Parse(d["occupying"]),bool.Parse(d["cleared"]),j.CrossingProgress,bool.Parse(d["crossHolding"]));
                    j.HoldRoad=int.Parse(d["holdRoad"],Invariant)<0?null:j.Roads.Single(r=>r.Id==int.Parse(d["holdRoad"],Invariant));
                    j.Sound=Get(p,"sound");j.Held=j.Held||j.Fault!=null;j.ReplanPending=false;j.Replan=null;j.Elapsed=0;j.NextLog=0;j.LastCrossingPosition=Get<Vector3>(p,"_pos");
                    if(j.Moving&&!Get<List<Vector3>>(p,"_route").SequenceEqual(j.Route))throw new InvalidDataException("Native saved route differs from tow metadata.");
                    var cursor=int.Parse(d["cursor"],Invariant);if(j.Moving&&(cursor<0||cursor>=j.Route.Count||Get<int>(p,"_routepos")!=cursor))throw new InvalidDataException("Saved tow cursor mismatch.");
                    if(j.Moving)j.Route=Get<List<Vector3>>(p,"_route");
                    if(Convert.ToInt32(Get(p,"_state"))!=12)throw new InvalidDataException("Saved aircraft is not in the owned tow state.");
                    j.Truck.transform.position=V(d["truckPosition"]);j.Truck.transform.eulerAngles=V(d["truckRotation"]);
                    Set(j.Truck,"_car_linked",bool.Parse(d["coupled"]));Set(j.Truck,"_link",Get(Get(p,"ac"),"front_wheel"));Set(j.Truck,"home",false);
                    if(j.Sound!=null){var desired=j.DesiredMute;muteSound.Invoke(j.Sound,new object[]{true});j.DesiredMute=desired;}
                    jobs.Add(j);if(!j.IncidentRecovery){Set(j.Source,"locked",p);Set(j.Source,"occupied",true);}Set(j.Target,"locked",p);
                    if(j.IncidentRecovery&&d.ContainsKey("a.path")){if(ImportApproach==null)throw new InvalidDataException("Saved tug approach support unavailable.");ImportApproach(p,j.Truck,j.Roads,d);}
                    // Saved native permissions are already restored. Only track the roads
                    // originally inserted by this job; never claim unrelated permissions.
                    foreach(var id in d["added"].Split(new[]{','},StringSplitOptions.RemoveEmptyEntries).Select(x=>int.Parse(x,Invariant)))
                    {if(id<0||id>=j.NativeRoads.Length||j.Roads[id].Type!=2)throw new InvalidDataException("Invalid saved native crossing permission.");j.AddedCrossings.Add(j.NativeRoads[id]);}
                    j.NativeClearEvent=j.Crossings.Event;
                    if(!j.Crossings.Cleared)RemoveNativeCrossings(j);
                    log("[Tugs] Restored tow "+j.Callsign+" -> "+j.Destination+"; crossing="+j.Crossings.Event+", occupying="+j.Crossings.Occupying);
                }
                foreach(var j in jobs)
                {
                    ValidateRestoredPosition(j);
                    // Independent controller-issued permissions remain independent on reload.
                }
                foreach(var j in jobs)
                {
                    if(j.Crossings.Cleared)GrantNativeCrossing(j);
                    Set(j.Plane,"_hold_position_taxistop",j.Held||j.HoldReached||j.Crossings.Holding||!j.Moving);
                }
                quarantined.Clear();Message=records.Count==0?"No saved tow jobs.":"Saved tow jobs restored. Moving transfers resume automatically; existing holds remain in place.";
            }
            catch(Exception error){Message="Saved tows remain held: "+error.Message;log("[Tugs] "+Message);}
        }
        private void ValidateRestoredPosition(Job j)
        {
            if(!j.Moving)return;
            var at=TowRouteInstructions.ProgressAtWaypoint(j.Route.Select(Point).ToArray(),Point(Get<Vector3>(j.Plane,"_pos")),Get<int>(j.Plane,"_routepos"));
            if(!j.Crossings.Complete&&(Math.Abs(at-j.CrossingProgress)>50||!j.Crossings.CanRecover(j.CrossingProgress,at)))throw new InvalidDataException("Saved physical tow position crosses a protection boundary for "+j.Callsign+" (saved="+j.CrossingProgress+", local="+at+").");
            if(j.Crossings.Complete){j.CrossingProgress=at;j.CrossingTravel=Math.Max(j.CrossingTravel,at);}
            if(Vector3.Distance(Get<Vector3>(j.Plane,"_pos"),j.Truck.transform.position)+15>j.Footprint)throw new InvalidDataException("Saved tug is outside its protected footprint.");
        }
        internal void Recover(Component plane)
        {
            var j=jobs.SingleOrDefault(v=>ReferenceEquals(v.Plane,plane));if(j==null)throw new InvalidOperationException("No active tow to recover.");
            if(quarantined.Contains(plane))throw new InvalidOperationException("Restore the original compatible tow save before recovering this flight.");
            Validate(j);
            if(j.ReplanPending||Math.Abs(Get<float>(plane,"_lv"))>=.1||!Get<bool>(j.Truck,"_car_linked"))throw new InvalidOperationException("Stop and couple the tug before recovery.");
            if(!j.Route.SequenceEqual(j.CheckedRoute))throw new InvalidOperationException("Checked tow route changed; recovery cannot replace it implicitly.");
            var issue=TowRouteSafety.Check(j.Route.Select(Point).ToArray(),Point(j.RouteStart),Point(j.End),j.Roads,true);if(issue!=null)throw new InvalidOperationException(issue);
            if(j.Moving)
            {
                var at=TowRouteInstructions.ProgressAtWaypoint(j.Route.Select(Point).ToArray(),Point(Get<Vector3>(plane,"_pos")),Get<int>(plane,"_routepos"));
                if(!j.Crossings.Complete&&(Math.Abs(at-j.CrossingProgress)>50||!j.Crossings.CanRecover(j.CrossingProgress,at)))throw new InvalidOperationException("Recovery would skip a protected runway boundary.");
                if(Vector3.Distance(Get<Vector3>(plane,"_pos"),j.Truck.transform.position)+15>j.Footprint)throw new InvalidOperationException("Tug is outside the protected combination footprint.");
                j.CrossingProgress=at;j.CrossingTravel=Math.Max(j.CrossingTravel,at);j.LastCrossingPosition=Get<Vector3>(plane,"_pos");
            }
            j.Fault=null;j.Held=true;j.Elapsed=0;j.NextLog=0;Set(plane,"_hold_position_taxistop",true);
            Message="Tow recovered and held for "+j.Callsign+". Continue tow when ready.";log("[Tugs] "+Message);reply(plane,"Tow recovered, holding position");
        }
    }
}
