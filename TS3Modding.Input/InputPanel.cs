using System;
using System.Linq;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding.InputMod
{
    public sealed partial class InputDriver
    {
        private void OnGUI()
        {
            if(!visible||bindings==null)return;
            if(captureFrame==Time.frameCount&&(Event.current.type==EventType.MouseDown||Event.current.type==EventType.MouseUp||Event.current.type==EventType.KeyDown))Event.current.Use();
            bounds.width=Math.Max(320,Math.Min(850,Screen.width-20));bounds.height=Math.Max(200,Math.Min(650,Screen.height-20));
            bounds.x=Mathf.Clamp(bounds.x,0,Math.Max(0,Screen.width-bounds.width));bounds.y=Mathf.Clamp(bounds.y,0,Math.Max(0,Screen.height-bounds.height));
            bounds=GUILayout.Window(GetInstanceID(),bounds,Draw,"Input & keybinds — 0.2.3");
        }
        private void Draw(int window)
        {
            GUILayout.Label("Changes save immediately. Ctrl / Shift / Alt combinations are supported.");
            GUILayout.Label("The full editor is also in Settings > Input. Other game shortcuts remain native.");
            GUILayout.BeginHorizontal();GUILayout.Label("Search",GUILayout.Width(55));search=GUILayout.TextField(search);
            if(GUILayout.Button("Close",GUILayout.Width(80))){visible=false;listening=null;consumedFrame=Time.frameCount;}
            GUILayout.EndHorizontal();
            if(listening!=null){GUILayout.Label("Press a shortcut for "+(bindings.Actions.FirstOrDefault(a=>a.Definition.Id==listening)?.Definition.Label??listening)+". Escape closes this panel.");
                if(GUILayout.Button("Cancel capture"))listening=null;}
            if(!string.IsNullOrEmpty(status))GUILayout.Label(status);
            scroll=GUILayout.BeginScrollView(scroll);
            foreach(var group in bindings.Actions.Where(a=>(a.Definition.Label+" "+a.Definition.Group).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).GroupBy(a=>a.Definition.Group))
            {
                GUILayout.Space(10);GUILayout.Label(group.Key,GUI.skin.box);
                foreach(var action in group)
                {
                    GUILayout.BeginHorizontal();GUILayout.Label(action.Definition.Label,GUILayout.MinWidth(180));
                    var caption=action.Definition.Native&&!action.Overridden?"Game default ("+action.Binding+")":action.Binding.ToString();
                    if(GUILayout.Button(listening==action.Definition.Id?"Press shortcut…":caption,GUILayout.Width(230))){listening=action.Definition.Id;listenFrame=Time.frameCount;GUI.FocusControl(null);}
                    if(GUILayout.Button("Unbind",GUILayout.Width(65)))TryBind(action.Definition.Id,new KeyChord("None"));
                    if(GUILayout.Button("Default",GUILayout.Width(65)))TryBind(action.Definition.Id,null);
                    GUILayout.EndHorizontal();
                    var conflicts=bindings.Conflicts(action.Definition.Id,action.Binding);
                    if(conflicts.Length>0)GUILayout.Label("Shared shortcut: "+string.Join(", ",conflicts));
                }
            }
            GUILayout.EndScrollView();GUILayout.Label("Native mouse/sensitivity settings remain in Settings. Defaults follow the game's saved key settings.");
            GUI.DragWindow(new Rect(0,0,bounds.width,22));
        }
    }
}



