using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace TS3Modding.Tugs
{
    internal sealed class NativeTowOnly:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static NativeTowOnly current;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.tugs.dormant");
        private readonly Action<string> log;private readonly Action<Component,string> reply;
        private readonly MethodInfo brake,multiplayer;
        private sealed class RequestEntry
        {
            internal NativeDormantTowLease Lease;internal object Game;internal Component Plane;internal string Destination,Alias;
            internal double LastDay,Elapsed,DepartureMinutes;
        }
        private readonly List<RequestEntry> requests=new List<RequestEntry>();
        internal int MaximumRequests=8;internal Action<Component,object> RetiredStrip;
        internal Component[] AircraftList=>requests.Select(r=>r.Plane).ToArray();
        internal Component Aircraft=>requests.FirstOrDefault()?.Plane;
        internal string Destination=>requests.FirstOrDefault()?.Destination;
        internal string DestinationFor(Component plane)=>Find(plane)?.Destination;
        internal string DestinationAliasFor(Component plane)=>Owns(plane)?Find(plane).Alias:null;
        private RequestEntry Find(Component plane)=>requests.FirstOrDefault(r=>ReferenceEquals(r.Plane,plane));
        internal bool Owns(Component plane)=>Find(plane)?.Lease.SameFlight==true;
        internal bool ParkedAt(Component plane)=>Find(plane)!=null&&IsParked(plane);
        private static bool IsParked(Component plane)=>plane&&(bool)Get(plane,"_imatterminal")&&Convert.ToInt32(Get(plane,"_state"))==8&&Math.Abs((float)Get(plane,"_lv"))<.1;
        internal bool Parked=>Active&&requests.All(r=>IsParked(r.Plane));
        internal bool Active=>requests.Count!=0;
        internal Func<Component,bool> IsTransferAircraft;
        private static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
        internal NativeTowOnly(Assembly assembly,Action<string> log,Action<Component,string> reply,bool patch=true)
        {
            this.log=log;this.reply=reply;var plane=assembly.GetType("Airplane",true);
            brake=plane.GetMethod("PPPMPGFDJMM",Flags,null,new[]{typeof(float),typeof(bool)},null);
            multiplayer=assembly.GetType("TCP",true).GetMethod("get_mp_state",Flags);
            if(!patch)return;
            if(current!=null)throw new InvalidOperationException("Dormant tow owner already registered.");current=this;
            try{
                foreach(var method in new[]{"WaitForStart","SayReadyToTaxi"})harmony.Patch(plane.GetMethod(method,Flags),prefix:new HarmonyMethod(typeof(NativeTowOnly),nameof(DeparturePrefix)));
                harmony.Patch(plane.GetMethod("GHINOHCGAAD",Flags),prefix:new HarmonyMethod(typeof(NativeTowOnly),nameof(WaitingPrefix)));
                harmony.Patch(plane.GetMethod("Command",Flags),prefix:new HarmonyMethod(typeof(NativeTowOnly),nameof(AddressedCommandPrefix)));
            }catch{Dispose();throw;}
        }
        internal void RequireRequestedStand(Component plane,string identifier,bool nativeName=false)
        {
            var entry=Find(plane);if(entry==null||!entry.Lease.SameFlight)return;
            if(!string.Equals(identifier,nativeName?entry.Destination:entry.Alias,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("wrong stand, we requested stand "+TowVoiceIntent.Spoken(entry.Alias));
        }
        internal void Request(object airport,Component plane,string destination,string alias=null)
        {
            if(requests.Count>=MaximumRequests)throw new InvalidOperationException("The configured tow request limit has been reached.");
            if(Find(plane)!=null)throw new InvalidOperationException("Aircraft already has a tow request.");
            if(requests.Any(r=>ReferenceEquals(r.Game,airport)&&r.Destination==destination))throw new InvalidOperationException("Stand is already requested by another tow.");
            if(Convert.ToInt32(multiplayer.Invoke(null,null))!=0)throw new InvalidOperationException("Tow-only requests support single player only.");
            var frequency=Get(airport,"datfreq");var filter=Get(Get(plane,"_terminal"),"terminal_filter");
            var owner=frequency.GetType().GetMethod("KNALNCAFFMC",Flags,null,new[]{typeof(string)},null).Invoke(frequency,new[]{Get(filter,"terminal")});
            var minutes=TS3Modding.API.AircraftMaintenanceServices.Contains(plane)?1440f:(float)plane.GetType().GetMethod("getTimeToGoMins",Flags).Invoke(plane,null);
            if(minutes<60)throw new InvalidOperationException("Tow-only requests require at least sixty minutes before departure.");
            var entry=new RequestEntry{Lease=new NativeDormantTowLease(airport,plane,owner,minutes),Plane=plane,Game=airport,Destination=destination,Alias=alias??TowStandAliases.Preferred(destination),LastDay=(float)Get(airport,"daytime"),DepartureMinutes=minutes};
            requests.Add(entry);log("[Tugs] Tow-only activated "+Get(plane,"_name")+" -> "+destination+"; departure in "+minutes+" minutes; schedule retained.");
            reply(plane,"Requesting tow to stand "+TowVoiceIntent.Spoken(entry.Alias));
        }
        internal bool ValidateSession(object airport)
        {
            var valid=true;foreach(var entry in requests.ToArray())if(!ReferenceEquals(entry.Game,airport)||!entry.Lease.SameFlight){log("[Tugs] Tow-only ownership ended after session/flight change; new flight fields preserved.");requests.Remove(entry);valid=false;}return valid;
        }
        internal void Restore(){foreach(var entry in requests.ToArray())Restore(entry.Plane);}
        internal void Restore(Component plane)
        {
            var entry=Find(plane);if(entry==null)return;entry.Lease.RestoreParked();requests.Remove(entry);
            if(entry.Lease.CreatedStrip&&!(bool)Get(plane,"_active"))try{RetiredStrip?.Invoke(plane,entry.Lease.Schedule);}catch(Exception error){log("[Tugs] Strip retirement queue failed: "+error.Message);}
            log("[Tugs] Tow-only restored dormant parking for "+Get(plane,"_name")+"; native schedule retained.");
        }
        internal bool RetainAfterRouteFailure(Component plane)
        {
            var entry=Find(plane);
            if(entry==null||!entry.Lease.SameFlight||!IsParked(plane))return false;
            // Give a revised clearance a fresh request window without extending the real departure time.
            var day=(double)(float)Get(entry.Game,"daytime");var delta=day-entry.LastDay;
            if(delta<-.5)delta+=1;
            var elapsed=entry.Elapsed+(delta>0&&!double.IsNaN(delta)&&!double.IsInfinity(delta)?delta*86400:0);
            entry.DepartureMinutes-=elapsed/60;entry.Elapsed=0;entry.LastDay=day;
            log("[Tugs] Tow request retained after route failure for "+Get(plane,"_name")+" -> "+entry.Destination);
            return true;
        }
        internal void ExpireWaiting()
        {
            foreach(var entry in requests.ToArray()){
                if(!IsParked(entry.Plane)||IsTransferAircraft?.Invoke(entry.Plane)==true)continue;
                var day=(double)(float)Get(entry.Game,"daytime");var delta=day-entry.LastDay;entry.LastDay=day;
                if(delta<-.5)delta+=1;
                if(delta>0&&!double.IsNaN(delta)&&!double.IsInfinity(delta))entry.Elapsed+=delta*86400;
                var minutes=entry.DepartureMinutes-entry.Elapsed/60;
                if(entry.Elapsed<300&&minutes>=30)continue;
                log("[Tugs] Tow request expiry: aircraft="+Get(entry.Plane,"_name")+" gameSeconds="+entry.Elapsed+" remainingDepartureMinutes="+minutes);
                reply(entry.Plane,"Tow request withdrawn, remaining parked");Restore(entry.Plane);
            }
        }
        internal bool PermitSave()
        {
            return requests.All(r=>r.Lease.SameFlight);
        }
        internal List<Dictionary<string,string>> ExportState()
        {
            return requests.Select(r=>{if(!r.Lease.SameFlight)throw new InvalidOperationException("Tow request identity changed.");var d=r.Lease.Export();d["kind"]="request";d["name"]=(string)Get(r.Plane,"_name");d["net"]=Convert.ToString(Get(r.Plane,"_netidx"),System.Globalization.CultureInfo.InvariantCulture);var p=(Vector3)Get(r.Plane,"_pos");d["position"]=string.Join(",",new[]{p.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),p.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),p.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)});d["destination"]=r.Destination;d["alias"]=r.Alias;d["elapsed"]=r.Elapsed.ToString("R",System.Globalization.CultureInfo.InvariantCulture);d["departure"]=r.DepartureMinutes.ToString("R",System.Globalization.CultureInfo.InvariantCulture);return d;}).ToList();
        }
        internal void ForgetForLoad()=>requests.Clear();
        internal void ImportState(object airport,Dictionary<string,string> d,Component plane)
        {
            if(Find(plane)!=null||requests.Any(r=>r.Destination==d["destination"]))throw new InvalidOperationException("Duplicate saved tow request.");
            requests.Add(new RequestEntry{Game=airport,Plane=plane,Destination=d["destination"],Alias=d["alias"],Lease=new NativeDormantTowLease(airport,plane,d),LastDay=(float)Get(airport,"daytime"),Elapsed=double.Parse(d["elapsed"],System.Globalization.CultureInfo.InvariantCulture),DepartureMinutes=double.Parse(d["departure"],System.Globalization.CultureInfo.InvariantCulture)});
        }
        internal bool PermitCommand(Component plane)
        {
            var transferring=IsTransferAircraft?.Invoke(plane)==true;if(!Owns(plane)&&!transferring)return true;
            reply(plane,transferring?"We are being towed, unable to taxi under our own power":"We require a tow, unable to taxi under our own power");return false;
        }
        private static bool DeparturePrefix(object __instance)=>!(current?.Owns((Component)__instance)==true||current?.IsTransferAircraft?.Invoke((Component)__instance)==true);
        private static bool AddressedCommandPrefix(object __instance,string __0,ref bool __result)
        {
            var self=current;if(self==null||TowVoiceIntent.IsTow(__0)||TowVoiceIntent.IsScopedInstruction(__0)||(!self.Owns((Component)__instance)&&self.IsTransferAircraft?.Invoke((Component)__instance)!=true))return true;
            self.PermitCommand((Component)__instance);__result=false;return false;
        }
        private static bool WaitingPrefix(object __instance)
        {
            var self=current;var plane=(Component)__instance;if((self?.Owns(plane)!=true&&self?.IsTransferAircraft?.Invoke(plane)!=true)||Convert.ToInt32(Get(plane,"_state"))!=8)return true;
            self.brake.Invoke(plane,new object[]{Get(plane,"KJEOBOIKNPD"),true});return false;
        }
        public void Dispose()
        {
            foreach(var entry in requests.ToArray())if(IsParked(entry.Plane)&&entry.Lease.SameFlight)Restore(entry.Plane);else log("[Tugs] Tow-only module unloaded away from stand; aircraft remains active.");
            requests.Clear();harmony.UnpatchSelf();if(ReferenceEquals(current,this))current=null;
        }
    }
}
