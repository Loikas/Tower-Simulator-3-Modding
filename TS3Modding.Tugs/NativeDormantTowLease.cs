using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
namespace TS3Modding.Tugs
{
    // Scoped activation owned by NativeTowOnly, retaining the native flight schedule.
    internal sealed class NativeDormantTowLease
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private readonly Component plane;private readonly object schedule,owner;private readonly string callsign;
        private readonly IList activePlanes;private readonly bool contact,alreadyListed;
        private bool released;
        private readonly object activationTime;
        private readonly object showStrip;
        private static FieldInfo Field(object target,string key)=>target.GetType().GetField(key,Flags)??throw new MissingFieldException(target.GetType().FullName,key);
        private static object Get(object target,string key)=>Field(target,key).GetValue(target);
        private static void Set(object target,string key,object value)=>Field(target,key).SetValue(target,value);
        internal NativeDormantTowLease(object game,Component plane,object groundOwner,float minutesUntilDeparture)
        {
            if(game==null||!plane||groundOwner==null||float.IsNaN(minutesUntilDeparture)||float.IsInfinity(minutesUntilDeparture)||minutesUntilDeparture<30)
                throw new InvalidOperationException("A parked aircraft with at least thirty minutes before departure is required.");
            if((bool)Get(game,"loading")||(bool)Get(plane,"_active")||(bool)Get(plane,"_deleted")||!(bool)Get(plane,"_imatterminal")||Get(plane,"_terminal")==null||
                Convert.ToInt32(Get(plane,"_state"))!=8||Convert.ToInt32(Get(plane,"_action"))!=2||Math.Abs((float)Get(plane,"_lv"))>.1||(float)Get(plane,"_alt")>1||
                Get(plane,"_router")!=null||((IList)Get(plane,"_holdshort")).Count!=0)
                throw new InvalidOperationException("Aircraft is not an eligible dormant outbound stand aircraft.");
            this.plane=plane;schedule=Get(plane,"_timetable");callsign=(string)Get(plane,"_name");
            if(schedule==null||string.IsNullOrWhiteSpace(callsign))throw new InvalidOperationException("Existing flight identity is required.");
            activePlanes=(IList)Get(game,"active_airplanes")??throw new InvalidOperationException("Native active-aircraft list is unavailable.");
            owner=Get(plane,"_owner");contact=(bool)Get(plane,"_trycontact");alreadyListed=activePlanes.Contains(plane);
            activationTime=Get(plane,"_acttime");
            showStrip=Get(plane,"_showstrip");
            // Validate the owner type before any mutation.
            if(!Field(plane,"_owner").FieldType.IsInstanceOfType(groundOwner))throw new InvalidOperationException("Invalid native ground frequency owner.");
            try{Set(plane,"_owner",groundOwner);Set(plane,"_trycontact",true);Set(plane,"_showstrip",true);if(!alreadyListed)activePlanes.Add(plane);Set(plane,"_active",true);}
            catch{Restore();throw;}
        }
        internal System.Collections.Generic.Dictionary<string,string> Export()
        {
            return new System.Collections.Generic.Dictionary<string,string>{{"contact",contact.ToString()},{"listed",alreadyListed.ToString()},{"acttime",Convert.ToString(activationTime,System.Globalization.CultureInfo.InvariantCulture)},{"showstrip",Convert.ToString(showStrip)},{"owner",owner==null?"":(string)Get(owner,"freq")}};
        }
        internal NativeDormantTowLease(object game,Component plane,System.Collections.Generic.Dictionary<string,string> data)
        {
            this.plane=plane;schedule=Get(plane,"_timetable");callsign=(string)Get(plane,"_name");
            if(schedule==null||!(bool)Get(plane,"_active")||(bool)Get(plane,"_deleted"))throw new InvalidOperationException("Saved dormant flight is unavailable.");
            activePlanes=(IList)Get(game,"active_airplanes")??throw new InvalidOperationException("Native aircraft list unavailable.");
            contact=bool.Parse(data["contact"]);alreadyListed=bool.Parse(data["listed"]);activationTime=float.Parse(data["acttime"],System.Globalization.CultureInfo.InvariantCulture);showStrip=bool.Parse(data["showstrip"]);
            var frequencies=Get(game,"datfreq");owner=data["owner"].Length==0?null:frequencies.GetType().GetMethod("EDCGCFPGCGH",Flags).Invoke(frequencies,new object[]{data["owner"]});
            if(data["owner"].Length!=0&&owner==null)throw new InvalidOperationException("Saved original radio owner unavailable.");
            if(!activePlanes.Contains(plane))activePlanes.Add(plane);
        }
        internal object Schedule=>schedule;internal bool CreatedStrip=>!(bool)showStrip;
        internal bool SameFlight=>plane&&!(bool)Get(plane,"_deleted")&&ReferenceEquals(Get(plane,"_timetable"),schedule)&&(string)Get(plane,"_name")==callsign;
        internal void RestoreParked()
        {
            if(released)return;
            if(!SameFlight)throw new InvalidOperationException("Flight identity changed; do not overwrite the new native flight.");
            if(!(bool)Get(plane,"_imatterminal")||Get(plane,"_terminal")==null||Convert.ToInt32(Get(plane,"_state"))!=8||Math.Abs((float)Get(plane,"_lv"))>.1)
                throw new InvalidOperationException("A tow aircraft must be parked and stationary before deactivation.");
            Restore();
        }
        private void Restore()
        {
            Set(plane,"_active",false);Set(plane,"_trycontact",contact);Set(plane,"_owner",owner);
            Set(plane,"_acttime",activationTime);
            Set(plane,"_showstrip",showStrip);
            if(!alreadyListed)activePlanes.Remove(plane);released=true;
            TS3Modding.API.AircraftMaintenanceServices.ReturnedToStand(plane);
        }
    }
}
