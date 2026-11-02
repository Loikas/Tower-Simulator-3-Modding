using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding.Tugs
{
    internal sealed class DormantTowStripCleanup
    {
        private sealed class Pending
        {
            internal Component Plane;internal object Schedule;internal string Callsign;internal int Network;internal float Queued;
        }
        private readonly IAirportObservationSource source;
        private readonly IDormantTowStripArchivalService archive;
        private readonly Action<string> log;
        private readonly List<Pending> pending=new List<Pending>();
        private float nextTick;private bool failed;
        internal DormantTowStripCleanup(IAirportObservationSource source,IDormantTowStripArchivalService archive,Action<string> log)
        {this.source=source;this.archive=archive;this.log=log;}
        internal static DormantTowStripCleanup FromHost(IFrameworkHost host,Action<string> log)=>new DormantTowStripCleanup(host as IAirportObservationSource,host as IDormantTowStripArchivalService,log);
        internal void Queue(Component plane,object schedule)
        {
            if(source==null||archive==null){log("[Tugs] Dormant tow strip cleanup unavailable; framework update required.");return;}
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            pending.RemoveAll(p=>ReferenceEquals(p.Plane,plane));
            pending.Add(new Pending{Plane=plane,Schedule=schedule,Callsign=(string)plane.GetType().GetField("_name",flags).GetValue(plane),Network=(int)plane.GetType().GetField("_netidx",flags).GetValue(plane),Queued=Time.realtimeSinceStartup});
            nextTick=0;
        }
        internal void Tick()
        {
            if(failed||Time.realtimeSinceStartup<nextTick)return;nextTick=Time.realtimeSinceStartup+.5f;
            foreach(var request in pending.ToArray())
            {
                if(!request.Plane||Time.realtimeSinceStartup-request.Queued>30){pending.Remove(request);continue;}
                var rows=source.CurrentAirport.Strips.Where(s=>s.Departure&&!s.InBin&&s.NetworkIndex==request.Network&&string.Equals(s.Callsign,request.Callsign,StringComparison.OrdinalIgnoreCase)).ToArray();
                try{foreach(var strip in rows)
                    if(archive.TryArchiveDormantTowStrip(strip,request.Plane,request.Schedule))log("[Tugs] Retired temporary tow strip "+request.Callsign+" -> BIN, board="+strip.BoardInstanceId);}
                catch(Exception error){failed=true;log("[Tugs] Temporary strip cleanup stopped after native UI failure: "+error.Message);return;}
                // Keep the exact-lifetime request for the full 30 seconds: a board can be created late.
            }
        }
    }
}
