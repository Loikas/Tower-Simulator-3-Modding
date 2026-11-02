using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed partial class RejectedTakeoffModule
    {
        private sealed class MaintenanceBundle{internal bool Present;internal MaintenanceSaveRecord[] Records=new MaintenanceSaveRecord[0];}
        private readonly ConditionalWeakTable<object,MaintenanceBundle> maintenanceLoads=new ConditionalWeakTable<object,MaintenanceBundle>();
        private MaintenanceBundle pendingMaintenance;
        private static Action<string,string> maintenanceNativeWriter;
        private const string MaintenanceSuffix=".rto-maintenance";
        private void InitializeMaintenancePersistence(Assembly assembly)
        {
            var saved=assembly.GetType("SavedGame",true);
            harmony.Patch(saved.GetMethod("EJLLBCNLCGN",Flags,null,new[]{typeof(string)},null),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(IncidentSaveReady)),transpiler:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(MaintenanceSaveTranspiler)){after=new[]{"nl.ts3modding.tugs.transfer"}});
            harmony.Patch(saved.GetMethod("BNMANBKINED",Flags,null,new[]{typeof(string)},null),postfix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(MaintenanceLoaded)));
            harmony.Patch(saved.GetMethod("BHHHNCBJOAL",Flags,null,Type.EmptyTypes,null),prefix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(MaintenanceRestorePrefix)),postfix:new HarmonyMethod(typeof(RejectedTakeoffModule),nameof(MaintenanceRestorePostfix)));
        }
        private static IEnumerable<CodeInstruction> MaintenanceSaveTranspiler(IEnumerable<CodeInstruction> code)
        {
            return MaintenanceSaveBoundary.Rewrite(code,typeof(RejectedTakeoffModule).GetMethod(nameof(WriteMaintenanceSave),Flags),out maintenanceNativeWriter);
        }
        private string MaintenanceFlight(Component plane)
        {
            var schedule=Get(plane,"_timetable");if(schedule==null)return "";
            return string.Join("|",new[]{"call","time","from","to"}.Select(k=>Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Convert.ToString(Get(schedule,k))??""))));
        }
        private static bool IncidentSaveReady(){var self=current;if(self==null||self.recoveryPlans.Count==0)return true;self.context.Log("Save deferred: rejected-takeoff route calculation is still running; retry shortly.");return false;}
        private MaintenanceSaveRecord[] ExportMaintenance()
        {
            var records=maintenance.Values.Where(SameMaintenance).Select(r=>new MaintenanceSaveRecord{Network=(int)Get(r.Plane,"_netidx"),Callsign=r.Callsign,Flight=MaintenanceFlight(r.Plane),Stand=Get(r.Plane,"_terminal")==null?"":(string)Get(Get(r.Plane,"_terminal"),"name"),Remaining=r.Timer.RemainingSeconds}).ToList();
            foreach(var pair in incidents.Where(p=>Same(p.Key,p.Value))){var r=pair.Value;records.Add(new MaintenanceSaveRecord{Kind="incident",Network=(int)Get(pair.Key,"_netidx"),Callsign=r.Callsign,Flight=MaintenanceFlight(pair.Key),Stand=r.ReturnStand==null?"":(string)Get(r.ReturnStand,"name"),Runway=r.Runway==null?"":(string)Get(r.Runway,"name"),Stopped=r.Stopped,Checks=r.ChecksDuration,Ready=r.Ready,StopReported=r.StopReported,Returned=r.Returned,TugRequested=r.TugRequested,RunwayOccupied=r.RunwayOccupied,ClearSamples=r.ClearSamples,OwnedRoute=r.RecoveryRoute!=null&&ReferenceEquals(r.RecoveryRoute,Get(pair.Key,"_route"))});}
            return records.ToArray();
        }
        private static void WriteMaintenanceSave(string path,string json)
        {
            // Chain the tug writer when installed. Its atomic write and embedded
            // tow metadata remain untouched; the sidecar hashes the final bytes.
            maintenanceNativeWriter(path,json);
            var self=current;if(self==null)return;
            var destination=path+MaintenanceSuffix;var temporary=destination+".tmp-"+Guid.NewGuid().ToString("N");
            try{
                var bytes=MaintenanceSaveCodec.Encode(File.ReadAllBytes(path),self.ExportMaintenance());File.WriteAllBytes(temporary,bytes);
                if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
                self.context.Log("Maintenance timers saved: "+path);
            }catch(Exception error){self.context.Log("Maintenance timers could not be saved: "+error.Message);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        private static void MaintenanceLoaded(object __result,string __0)
        {
            var self=current;if(self==null||__result==null)return;var bundle=new MaintenanceBundle{Present=File.Exists(__0+MaintenanceSuffix)};
            if(bundle.Present)try{bundle.Records=MaintenanceSaveCodec.Decode(File.ReadAllBytes(__0),File.ReadAllBytes(__0+MaintenanceSuffix));}catch(Exception error){self.context.Log("Maintenance save metadata rejected: "+error.Message);}
            self.maintenanceLoads.Remove(__result);self.maintenanceLoads.Add(__result,bundle);
        }
        private static void MaintenanceRestorePrefix(){var self=current;if(self==null)return;self.ResetIncidentsForLoad();self.maintenance.Clear();self.pendingMaintenance=null;self.maintenanceGame=null;}
        private static void MaintenanceRestorePostfix(object __instance)
        {
            var self=current;MaintenanceBundle bundle;if(self==null||!self.maintenanceLoads.TryGetValue(__instance,out bundle))return;
            self.maintenanceGame=self.gameInstance.GetValue(null);self.pendingMaintenance=bundle;self.adoptLegacy=!bundle.Present;self.maintenanceLoads.Remove(__instance);
        }
        private void RestoreMaintenanceRecords(object game)
        {
            if(pendingMaintenance==null||game==null||(bool)Get(game,"loading"))return;
            var bundle=pendingMaintenance;pendingMaintenance=null;var retry=new List<MaintenanceSaveRecord>();var planes=((IEnumerable)Get(game,"airplanes")).Cast<Component>().Where(p=>p&&!(bool)Get(p,"_deleted")).ToArray();
            foreach(var saved in bundle.Records)try{
                var matches=planes.Where(p=>(int)Get(p,"_netidx")==saved.Network&&(string)Get(p,"_name")==saved.Callsign&&MaintenanceFlight(p)==saved.Flight).ToArray();
                if(matches.Length!=1)throw new InvalidDataException("Saved maintenance flight is missing or ambiguous: "+saved.Callsign);
                var plane=matches[0];
                if(saved.Kind=="incident"){
                    if(saved.TugRequested&&!TS3Modding.API.TowRecoveryServices.Available){retry.Add(saved);Set(plane,"_hold_position_taxistop",true);continue;}
                    var roads=((IEnumerable)Get(Get(Get(game,"airfield"),"aircont"),"roads")).Cast<object>().ToArray();
                    var runway=roads.Single(r=>Convert.ToInt32(Get(r,"type"))==2&&(string)Get(r,"name")==saved.Runway);
                    var destination=saved.Stand.Length==0?null:roads.Single(r=>Convert.ToInt32(Get(r,"type"))==1&&(string)Get(r,"name")==saved.Stand);
                    if(destination!=null){var owner=Get(destination,"locked");if(owner!=null&&!ReferenceEquals(owner,plane))throw new InvalidDataException("Recovery stand reservation conflicts with another flight.");Set(destination,"locked",plane);}
                    var incident=new Incident{Game=game,Schedule=Get(plane,"_timetable"),Callsign=saved.Callsign,Runway=runway,ReturnStand=destination,Stopped=saved.Stopped,ChecksDuration=saved.Checks,Ready=saved.Ready,StopReported=saved.StopReported,Returned=saved.Returned,TugRequested=saved.TugRequested,RunwayOccupied=saved.RunwayOccupied,ClearSamples=saved.ClearSamples,RecoveryRoute=saved.OwnedRoute?Get(plane,"_route"):null};
                    incidents[plane]=incident;sampled[plane]=incident.Schedule;
                    if(saved.TugRequested){incident.TugTicket=TS3Modding.API.TowRecoveryServices.Request(RecoveryRequest(plane,incident,destination));if(incident.TugTicket==null)throw new InvalidDataException("Saved tug recovery request could not be reconnected.");}
                    context.Log("Restored rejected takeoff: "+saved.Callsign+", checks remaining="+Math.Max(0,saved.Checks-saved.Stopped));continue;
                }
                var stand=Get(plane,"_terminal");
                if(saved.Stand.Length>0&&(stand==null||(string)Get(stand,"name")!=saved.Stand))throw new InvalidDataException("Saved maintenance stand changed: "+saved.Callsign);
                var timer=new MaintenanceClock(saved.Remaining);timer.Advance((float)Get(game,"daytime"),false);
                // Read the legacy runway field for save compatibility, but never restore it.
                maintenance[plane]=new MaintenanceRecord{Plane=plane,Schedule=Get(plane,"_timetable"),Game=game,Callsign=saved.Callsign,Timer=timer};
                if(!(bool)Get(plane,"_active")&&MaintenanceAircraft.Parked(plane))MaintenanceAircraft.Park(game,plane);
                context.Log("Restored maintenance: "+saved.Callsign+", remaining simulation minutes="+(saved.Remaining/60).ToString("0.0"));
            }catch(Exception error){context.Log("Recovery/maintenance restore skipped: "+error.Message);}
            if(retry.Count>0)pendingMaintenance=new MaintenanceBundle{Present=true,Records=retry.ToArray()};
        }
    }
}
