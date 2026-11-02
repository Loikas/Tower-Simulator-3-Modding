using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.UI;
namespace TS3Modding.DisplayBridge
{
    // Presentation only. Notes, strip data and native flight identities are never edited.
    internal sealed class TowStripDisplay:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static TowStripDisplay current;private readonly Harmony harmony=new Harmony("nl.ts3modding.towstrip.display");
        private readonly Func<TowStandMarker[]> markers;private readonly FieldInfo record,label,id,name;
        private sealed class Applied{internal object Record;internal string Original,Display,Callsign;internal int NetworkId;}
        private readonly Dictionary<Text,Applied> applied=new Dictionary<Text,Applied>();
        internal TowStripDisplay(Assembly assembly,Func<TowStandMarker[]> markers)
        {
            this.markers=markers;var type=assembly.GetType("StripItem",true);record=type.GetField("rsi",Flags);label=type.GetField("txt_schedule",Flags);
            id=record.FieldType.GetField("netidx",Flags);name=record.FieldType.GetField("name",Flags);
            if(current!=null)throw new InvalidOperationException("Tow strip display already registered.");current=this;
            try{foreach(var method in new[]{"Update","Refresh"})harmony.Patch(type.GetMethod(method,Flags),postfix:new HarmonyMethod(typeof(TowStripDisplay),nameof(Render)));}catch{Dispose();throw;}
        }
        private static void Render(object __instance)
        {
            var self=current;if(self==null)return;var text=self.label.GetValue(__instance) as Text;if(!text)return;var row=self.record.GetValue(__instance);
            var matching=row==null?new TowStandMarker[0]:(self.markers()??new TowStandMarker[0]).Where(m=>m.NetworkId==(int)self.id.GetValue(row)&&string.Equals(m.Callsign,(string)self.name.GetValue(row),StringComparison.OrdinalIgnoreCase)&&DateTime.UtcNow-m.CreatedUtc<=TimeSpan.FromSeconds(3)&&!string.IsNullOrEmpty(m.Destination)).ToArray();
            Applied old;self.applied.TryGetValue(text,out old);
            var same=old!=null&&row!=null&&ReferenceEquals(old.Record,row)&&old.NetworkId==(int)self.id.GetValue(row)&&old.Callsign==(string)self.name.GetValue(row);
            if(matching.Length!=1){if(same&&text.text==old.Display)text.text=old.Original;self.applied.Remove(text);return;}
            var original=same&&text.text==old.Display?old.Original:text.text;
            var display="TOW\n"+matching[0].Destination;self.applied[text]=new Applied{Record=row,Original=original,Display=display,Callsign=matching[0].Callsign,NetworkId=matching[0].NetworkId};text.text=display;
        }
        public void Dispose(){harmony.UnpatchSelf();foreach(var pair in applied)if(pair.Key&&pair.Key.text==pair.Value.Display)pair.Key.text=pair.Value.Original;applied.Clear();if(ReferenceEquals(current,this))current=null;}
    }
}
