using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace TS3Modding.RejectedTakeoffs
{
    // Native parked state only. Never despawn, move, unlock or replace a flight.
    internal static class MaintenanceAircraft
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
        private static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
        internal static bool Parked(Component plane)=>plane&&!(bool)Get(plane,"_deleted")&&(bool)Get(plane,"_imatterminal")&&Get(plane,"_terminal")!=null&&Convert.ToInt32(Get(plane,"_state"))==8&&Math.Abs((float)Get(plane,"_lv"))<.1;
        private static void Mute(Component plane,bool muted){var sound=Get(plane,"sound");if(sound!=null)sound.GetType().GetMethod("BFKIKIOGFDI",Flags).Invoke(sound,new object[]{muted});}
        internal static void Park(object game,Component plane)
        {
            if(!Parked(plane))throw new InvalidOperationException("Maintenance handover requires a parked aircraft.");
            Set(plane,"_acttime",float.MaxValue);Set(plane,"_active",false);Set(plane,"_trycontact",false);Set(plane,"_owner",null);Set(plane,"_showstrip",false);
            Set(plane,"_trgrunway",null);Set(plane,"_last_gate",null);Set(plane,"_takeoff",false);Set(plane,"_lineup_and_wait",false);Set(plane,"_ready_to_taxi",false);Set(plane,"_requestingpush",false);Set(plane,"wait_for_trailer",false);
            foreach(var light in new[]{"_beaconon","_navon","_navstrobeon","_landon"})Set(plane,light,false);
            var active=Get(game,"active_airplanes") as IList;active?.Remove(plane);Mute(plane,true);
        }
        internal static bool ReadyToResume(Component plane)
        {
            if(!Parked(plane))return false;
            var gate=Get(plane,"_ikgate");var truck=gate==null?null:Get(gate,"truck");if(truck==null)return false;
            var assigned=Get(truck,"_plane");return assigned==null||ReferenceEquals(assigned,plane);
        }
        internal static void Resume(object game,Component plane)
        {
            if(!ReadyToResume(plane))throw new InvalidOperationException("Repair completion must wait until parked with its stand tug available.");
            var gate=Get(plane,"_ikgate");var truck=Get(gate,"truck");
            truck.GetType().GetMethod("SetPlane",Flags).Invoke(truck,new object[]{plane});Set(truck,"home",false);Set(truck,"sleep",false);Set(plane,"wait_for_trailer",true);
            Set(plane,"_trgrunway",null);
            Set(plane,"_waitpenalty_timer",900f);
            Set(plane,"_acttime",Get(game,"daytime"));Set(plane,"_active",true);Set(plane,"_ready_to_taxi",false);
            var active=Get(game,"active_airplanes") as IList;if(active!=null&&!active.Contains(plane))active.Add(plane);
            plane.GetType().GetMethod("WaitForStart",Flags).Invoke(plane,null);Mute(plane,false);
        }
    }
}
