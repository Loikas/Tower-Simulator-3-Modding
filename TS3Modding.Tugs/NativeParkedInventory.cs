using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace TS3Modding.Tugs
{
    // Observation only. Presence here does not grant activation or movement permission.
    internal sealed class NativeParkedInventory
    {
        private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        private readonly FieldInfo planes,loading,active,deleted,parked,terminal,state,name,schedule,velocity,altitude;
        private object session;private readonly Dictionary<int,string> reported=new Dictionary<int,string>();
        internal string Summary{get;private set;}="Parked-aircraft inventory awaiting airport.";
        internal NativeParkedInventory(Assembly assembly)
        {
            var game=assembly.GetType("Game",true);var plane=assembly.GetType("Airplane",true);
            planes=Field(game,"airplanes");loading=Field(game,"loading");
            active=Field(plane,"_active");deleted=Field(plane,"_deleted");parked=Field(plane,"_imatterminal");terminal=Field(plane,"_terminal");state=Field(plane,"_state");
            name=Field(plane,"_name");schedule=Field(plane,"_timetable");velocity=Field(plane,"_lv");altitude=Field(plane,"_alt");
        }
        private static FieldInfo Field(Type type,string key)=>type.GetField(key,Flags)??throw new MissingFieldException(type.FullName,key);
        internal void Observe(object game,Action<string> log)
        {
            if(!ReferenceEquals(session,game)){reported.Clear();session=game;}
            if(game==null||(bool)loading.GetValue(game)){Summary="Parked-aircraft inventory awaiting airport.";return;}
            var dormant=0;var unnamed=0;var ids=new HashSet<int>();
            foreach(var plane in ((IEnumerable)planes.GetValue(game)??new object[0]).Cast<object>().OfType<Component>())
            {
                if(!plane||(bool)deleted.GetValue(plane)||(bool)active.GetValue(plane)||!(bool)parked.GetValue(plane)||terminal.GetValue(plane)==null||
                    Math.Abs((float)velocity.GetValue(plane))>.1f||(float)altitude.GetValue(plane)>1f)continue;
                dormant++;var callsign=(string)name.GetValue(plane);if(string.IsNullOrWhiteSpace(callsign))unnamed++;
                var road=terminal.GetValue(plane);var stand=(string)Field(road.GetType(),"name").GetValue(road);
                var fingerprint=stand+"|"+callsign+"|"+state.GetValue(plane)+"|schedule="+(schedule.GetValue(plane)!=null);
                var id=plane.GetInstanceID();ids.Add(id);
                string previous;if(!reported.TryGetValue(id,out previous)||previous!=fingerprint)
                    log("[Tugs] Dormant parked aircraft (observation only): id="+id+" "+fingerprint);
                reported[id]=fingerprint;
            }
            foreach(var id in reported.Keys.Where(id=>!ids.Contains(id)).ToArray())reported.Remove(id);
            Summary=dormant+" dormant parked aircraft observed ("+unnamed+" without an internal callsign). Tow-only activation is not enabled.";
        }
    }
}
