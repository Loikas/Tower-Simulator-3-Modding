using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace TS3Modding.Tugs
{
    // Presentation only: never assigns the aircraft's runway or flight identity.
    internal sealed class TowAircraftLabelDisplay:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.tugs.aircraftlabel");
        private static TowAircraftLabelDisplay current;
        private readonly FieldInfo plane,text,background,layout;
        private readonly Func<Component,string> destination;
        private readonly Action<string> log;private bool warned;
        private sealed class Original
        {
            internal Text Text;internal Image Background;internal LayoutElement Layout;
            internal string Content;internal Color TextColor,BackgroundColor;internal bool Visible;
            internal float Width,MinWidth;
        }
        private readonly Dictionary<object,Original> applied=new Dictionary<object,Original>();
        internal TowAircraftLabelDisplay(Assembly assembly,Func<Component,string> destination,Action<string> log)
        {
            this.destination=destination;this.log=log;var type=assembly.GetType("VTag",true);
            plane=type.GetField("ap",Flags);text=type.GetField("txt_runway",Flags);background=type.GetField("img_runway_bg",Flags);layout=type.GetField("titlelayout",Flags);
            if(plane==null||text?.FieldType!=typeof(Text)||background?.FieldType!=typeof(Image)||layout?.FieldType!=typeof(LayoutElement))throw new NotSupportedException("Aircraft runway-label boundary changed.");
            if(current!=null)throw new InvalidOperationException("Aircraft tow label already registered.");current=this;
            try{harmony.Patch(type.GetMethod("LateUpdate",Flags),prefix:new HarmonyMethod(typeof(TowAircraftLabelDisplay),nameof(Before)),postfix:new HarmonyMethod(typeof(TowAircraftLabelDisplay),nameof(After)));}catch{Dispose();throw;}
        }
        private void Restore(object tag)
        {
            Original old;if(!applied.TryGetValue(tag,out old))return;applied.Remove(tag);
            if(old.Text){old.Text.text=old.Content;old.Text.color=old.TextColor;}
            if(old.Background){old.Background.color=old.BackgroundColor;old.Background.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,old.Width);old.Background.gameObject.SetActive(old.Visible);}
            if(old.Layout)old.Layout.minWidth=old.MinWidth;
        }
        private static void Before(object __instance)=>current?.Restore(__instance);
        private static void After(object __instance)
        {
            var self=current;if(self==null)return;
            try
            {
                foreach(var stale in self.applied.Where(p=>!p.Value.Text).Select(p=>p.Key).ToArray())self.applied.Remove(stale);
                var p=self.plane.GetValue(__instance) as Component;if(!p)return;
                var alias=self.destination(p);if(string.IsNullOrEmpty(alias))return;
                var label=self.text.GetValue(__instance) as Text;var badge=self.background.GetValue(__instance) as Image;var title=self.layout.GetValue(__instance) as LayoutElement;
                if(!label||!badge||!title)return;
                var old=new Original{Text=label,Background=badge,Layout=title,Content=label.text,TextColor=label.color,BackgroundColor=badge.color,Visible=badge.gameObject.activeSelf,Width=badge.rectTransform.rect.width,MinWidth=title.minWidth};
                self.applied.Add(__instance,old);
                label.text=alias.ToUpperInvariant();label.color=Color.white;
                badge.color=new Color(.08f,.38f,.78f,1);badge.gameObject.SetActive(true);
                float width=Mathf.Max(old.Width,label.preferredWidth+12);
                badge.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
                title.minWidth=old.MinWidth+(old.Visible?Mathf.Max(0,width-old.Width):width);
            }
            catch(Exception e){self.Restore(__instance);if(!self.warned){self.warned=true;self.log("[Tugs] Aircraft tow label unavailable: "+e.Message);}}
        }
        public void Dispose(){harmony.UnpatchSelf();foreach(var tag in applied.Keys.ToArray())Restore(tag);if(ReferenceEquals(current,this))current=null;}
    }
}
