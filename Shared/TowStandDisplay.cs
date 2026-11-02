using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace TS3Modding.DisplayBridge
{
    // Reuses the native arrival stand marker and its per-view map projection.
    internal sealed class TowStandDisplay:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static TowStandDisplay current;private readonly Harmony harmony;
        private readonly Func<TowStandMarker[]> marker;private readonly Action<string> log;
        private readonly FieldInfo selected,terminal,zoom,name,id;private readonly MethodInfo convert;
        private readonly HashSet<Transform> shown=new HashSet<Transform>();private bool failed;
        internal TowStandDisplay(Assembly assembly,Func<TowStandMarker[]> marker,Action<string> log,string owner="nl.ts3modding.towstand.display")
        {
            harmony=new Harmony(owner);
            this.marker=marker;this.log=log;var display=assembly.GetType("Adirs",true);
            selected=display.GetField("prevplane",Flags);terminal=display.GetField("terminal_marker",Flags);zoom=display.GetField("zoom",Flags);
            name=selected.FieldType.GetField("name",Flags);id=selected.FieldType.GetField("netidx",Flags);
            convert=display.GetMethod("ConvertPosition2Screen",Flags,null,new[]{typeof(Vector3),typeof(float)},null);
            if(current!=null)throw new InvalidOperationException("Tow stand display already registered.");current=this;
            try{harmony.Patch(display.GetMethod("LateUpdate",Flags),postfix:new HarmonyMethod(typeof(TowStandDisplay),nameof(Render)));}catch{Dispose();throw;}
        }
        private static void Render(object __instance)
        {
            var self=current;if(self==null)return;
            try{
                var plane=self.selected.GetValue(__instance);if(plane==null)return;
                var matches=(self.marker()??new TowStandMarker[0]).Where(row=>(int)self.id.GetValue(plane)==row.NetworkId&&string.Equals((string)self.name.GetValue(plane),row.Callsign,StringComparison.OrdinalIgnoreCase)&&DateTime.UtcNow-row.CreatedUtc<=TimeSpan.FromSeconds(3)).ToArray();if(matches.Length!=1)return;var m=matches[0];
                var target=self.terminal.GetValue(__instance) as Transform;if(!target)return;
                var scale=(float)self.zoom.GetValue(__instance);if(scale<=0)return;
                target.localPosition=(Vector3)self.convert.Invoke(__instance,new object[]{new Vector3(m.X,m.Y,m.Z),0f});
                target.localScale=Vector3.one/scale;target.localRotation=Quaternion.Euler(0,0,m.Heading);target.gameObject.SetActive(true);self.shown.Add(target);
            }catch(Exception e){if(!self.failed){self.failed=true;self.log("Tow stand marker unavailable: "+e.Message);}}
        }
        public void Dispose(){harmony.UnpatchSelf();foreach(var target in shown)if(target)target.gameObject.SetActive(false);shown.Clear();if(ReferenceEquals(current,this))current=null;}
    }
}
