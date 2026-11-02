using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TS3Modding.API;
using UnityEngine;
namespace TS3Modding.RejectedTakeoffs
{
    internal sealed partial class RejectedTakeoffModule
    {
        private sealed class MaintenanceRecord{internal Component Plane;internal object Schedule,Game;internal string Callsign;internal MaintenanceClock Timer;}
        private readonly Dictionary<Component,MaintenanceRecord> maintenance=new Dictionary<Component,MaintenanceRecord>();
        private readonly MaintenanceSettings maintenanceOptions;
        private bool maintenanceStripFailure;
        private IDisposable maintenanceProvider;private object maintenanceGame;private bool adoptLegacy=true;
        private bool SameMaintenance(MaintenanceRecord r)=>r.Plane&&!(bool)Get(r.Plane,"_deleted")&&ReferenceEquals(gameInstance.GetValue(null),r.Game)&&ReferenceEquals(Get(r.Plane,"_timetable"),r.Schedule)&&(string)Get(r.Plane,"_name")==r.Callsign;
        private bool InMaintenance(object plane){MaintenanceRecord r;return plane is Component p&&maintenance.TryGetValue(p,out r)&&SameMaintenance(r);}
        private void InitializeMaintenance(Assembly assembly,bool patchPersistence=true)
        {
            maintenanceProvider=AircraftMaintenanceServices.Register(InMaintenance,p=>{MaintenanceRecord r;if(p is Component plane&&maintenance.TryGetValue(plane,out r)&&SameMaintenance(r)&&MaintenanceAircraft.Parked(plane)){if(r.Timer.RemainingSeconds<=0)FinishMaintenance(r);else MaintenanceAircraft.Park(r.Game,plane);}});
            if(patchPersistence)InitializeMaintenancePersistence(assembly);
        }
        private void StartMaintenance(Component plane,object game,bool breakdown=true)
        {
            if(maintenance.ContainsKey(plane))return;
            var timer=new MaintenanceClock(MaintenanceClock.Duration(breakdown?maintenanceOptions.Minimum.Value:maintenanceOptions.TaxiMinimum.Value,breakdown?maintenanceOptions.Maximum.Value:maintenanceOptions.TaxiMaximum.Value,random.NextDouble()));
            timer.Advance((float)Get(game,"daytime"),false);
            var record=new MaintenanceRecord{Plane=plane,Schedule=Get(plane,"_timetable"),Game=game,Callsign=(string)Get(plane,"_name"),Timer=timer};
            MaintenanceAircraft.Park(game,plane);maintenance[plane]=record;
            context.Log("Maintenance started: "+record.Callsign+", simulation minutes="+(timer.RemainingSeconds/60).ToString("0.0"));
        }
        private void CompleteTugRecovery(Component plane,Incident incident)
        {
            StartMaintenance(plane,incident.Game);incident.Returned=true;incident.TugRequested=false;incident.TugTicket=null;incidents.Remove(plane);
            Report(plane,"On stand, shutting down for maintenance");
        }
        private void FinishMaintenance(MaintenanceRecord record)
        {
            if(!SameMaintenance(record)||!MaintenanceAircraft.ReadyToResume(record.Plane))return;
            // Do not interrupt an active subsequent tow, including coupling on stand.
            if((bool)Get(record.Plane,"_active"))return;
            MaintenanceAircraft.Resume(record.Game,record.Plane);maintenance.Remove(record.Plane);
            context.Log("Maintenance complete; native push/start readiness restored: "+record.Callsign);
        }
        private void RetireMaintenanceStrips(MaintenanceRecord record)
        {
            if(maintenanceStripFailure||(bool)Get(record.Plane,"_active"))return;
            var source=FrameworkServices.Current as IAirportObservationSource;
            var archive=FrameworkServices.Current as IDormantTowStripArchivalService;
            if(source==null||archive==null)return;
            try{
                var network=(int)Get(record.Plane,"_netidx");
                foreach(var strip in source.CurrentAirport.Strips.Where(s=>s.Departure&&!s.InBin&&s.NetworkIndex==network&&s.Callsign==record.Callsign))
                    archive.TryArchiveDormantTowStrip(strip,record.Plane,record.Schedule);
            }catch(Exception error){maintenanceStripFailure=true;context.Log("Maintenance strip cleanup unavailable: "+error.Message);}
        }
        private void TickMaintenance(object game)
        {
            if(!ReferenceEquals(maintenanceGame,game)){maintenance.Clear();maintenanceGame=game;adoptLegacy=true;}
            RestoreMaintenanceRecords(game);
            if(game==null||(bool)Get(game,"loading"))return;
            if(adoptLegacy){
                foreach(var plane in ((IEnumerable)Get(game,"airplanes")).Cast<Component>().Where(p=>p&&MaintenanceAircraft.Parked(p)&&!(bool)Get(p,"_active")&&(float)Get(p,"_acttime")==float.MaxValue))
                    if(!maintenance.ContainsKey(plane))StartMaintenance(plane,game);
            }
            foreach(var record in maintenance.Values.ToArray()){
                if(!SameMaintenance(record)){maintenance.Remove(record.Plane);continue;}
                record.Timer.Advance((float)Get(game,"daytime"),(float)Get(game,"speedup")>0);
                if(record.Timer.RemainingSeconds<=0)FinishMaintenance(record);
                else RetireMaintenanceStrips(record);
            }
        }
    }
}
