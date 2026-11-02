using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using TS3Modding.API;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UInput=UnityEngine.Input;

namespace TS3Modding.InputMod
{
    public sealed partial class InputDriver:MonoBehaviour
    {
        private static InputDriver current;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.input");
        private readonly List<IDisposable> registrations=new List<IDisposable>();
        private readonly Dictionary<string,ConfigEntry<string>> settings=new Dictionary<string,ConfigEntry<string>>();
        private InputBindingRegistry bindings;
        private ConfigFile config;private Action<string> log;private Assembly assembly;
        private bool visible;private int consumedFrame=-1;
        private GameObject pointerBlocker;
        private string listening,status="",search="";private int listenFrame,captureFrame=-1;private Vector2 scroll;
        private Rect bounds=new Rect(80,80,760,570);
        private static readonly KeyCode[] Keys=(KeyCode[])Enum.GetValues(typeof(KeyCode));
        private static bool Modifier(KeyCode key)=>key==KeyCode.LeftControl||key==KeyCode.RightControl||key==KeyCode.LeftShift||key==KeyCode.RightShift||key==KeyCode.LeftAlt||key==KeyCode.RightAlt;
        private static KeyModifiers Modifiers()=> (UInput.GetKey(KeyCode.LeftControl)||UInput.GetKey(KeyCode.RightControl)?KeyModifiers.Control:0)|
            (UInput.GetKey(KeyCode.LeftShift)||UInput.GetKey(KeyCode.RightShift)?KeyModifiers.Shift:0)|
            (UInput.GetKey(KeyCode.LeftAlt)||UInput.GetKey(KeyCode.RightAlt)?KeyModifiers.Alt:0);
        private static KeyChord Parse(string text)
        {
            if(text=="Default")return null;
            var parts=text.Split('+');KeyModifiers modifiers=0;
            for(int i=0;i<parts.Length-1;i++)switch(parts[i]){case "Ctrl":modifiers|=KeyModifiers.Control;break;case "Shift":modifiers|=KeyModifiers.Shift;break;case "Alt":modifiers|=KeyModifiers.Alt;break;default:throw new FormatException("Unknown modifier.");}
            KeyCode key;if(!Enum.TryParse(parts.Last(),true,out key)||!Enum.IsDefined(typeof(KeyCode),key)||Modifier(key))throw new FormatException("Unknown key or modifier-only shortcut.");
            return new KeyChord(key.ToString(),modifiers);
        }
        private ConfigEntry<string> Setting(string id)
        {
            ConfigEntry<string> entry;if(!settings.TryGetValue(id,out entry))settings.Add(id,entry=config.Bind("Bindings",id,"Default","Default, None, or a key with Ctrl+, Shift+, Alt+ modifiers."));return entry;
        }
        internal void Initialize(ConfigFile file,Action<string> logger)
        {
            if(current!=null)throw new InvalidOperationException("Input driver already active.");current=this;config=file;log=logger;
            assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp");
            bindings=new InputBindingRegistry(id=>{try{return Parse(Setting(id).Value);}catch(Exception e){log("Invalid shortcut "+id+": "+e.Message+"; using default.");return null;}},
                (id,chord)=>{if(chord!=null)Parse(chord.ToString());var entry=Setting(id);var old=entry.Value;try{entry.Value=chord?.ToString()??"Default";config.Save();}catch{entry.Value=old;throw;}},e=>log("Input action failed: "+e),chord=>Parse(chord.ToString()));
            registrations.Add(bindings.Register(new InputActionDefinition("input.open","Open input panel","Mod manager",new KeyChord("F10",KeyModifiers.Control|KeyModifiers.Shift)),()=>{visible=!visible;listening=null;consumedFrame=Time.frameCount;}));
            foreach(var key in new[]{KeyCode.W,KeyCode.S,KeyCode.A,KeyCode.D,KeyCode.E,KeyCode.Q,KeyCode.UpArrow,KeyCode.DownArrow,KeyCode.LeftArrow,KeyCode.RightArrow})
                registrations.Add(bindings.Register(new InputActionDefinition("game.freecam."+key,FreeCameraLabel(key),"Free camera",new KeyChord(key.ToString()),true),null));
            registrations.Add(bindings.Register(new InputActionDefinition("game.push_to_talk","Push to talk","Voice & camera",new KeyChord("None"),true),null));
            registrations.Add(bindings.Register(new InputActionDefinition("game.push_to_look","Push to look","Voice & camera",new KeyChord("None"),true),null));
            foreach(var type in new[]{"FreeCamController","DeskCameraController","PopupMic"})
                harmony.Patch(assembly.GetType(type,true).GetMethod("Update",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic),transpiler:new HarmonyMethod(typeof(InputDriver),nameof(Rewrite)));
            InitializeMenu();InitializeSpeechInput();RefreshNativeDefaults();InputServices.Attach(bindings);
            pointerBlocker=new GameObject("Input panel pointer blocker",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            pointerBlocker.transform.SetParent(transform,false);var canvas=pointerBlocker.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32700;
            var mask=new GameObject("Pointer mask",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));mask.transform.SetParent(pointerBlocker.transform,false);
            var rect=mask.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;mask.GetComponent<Image>().color=Color.clear;
            pointerBlocker.SetActive(false);
            log("Input Panel 0.2.3 ready. Bindings are directly in Settings > Input, or Ctrl+Shift+F10. Keyboard hooks are scoped to reviewed camera/voice callers.");
        }
        private static string FreeCameraLabel(KeyCode key)
        {
            switch(key){case KeyCode.W:case KeyCode.UpArrow:return "Move forward ("+key+")";case KeyCode.S:case KeyCode.DownArrow:return "Move backward ("+key+")";
                case KeyCode.A:case KeyCode.LeftArrow:return "Move left ("+key+")";case KeyCode.D:case KeyCode.RightArrow:return "Move right ("+key+")";case KeyCode.E:return "Move up";default:return "Move down";}
        }
        private float nextDefaults;
        private void RefreshNativeDefaults()
        {
            var settingsType=assembly.GetType("Settings",true);
            var instance=settingsType.GetField("instance",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null) as UnityEngine.Object;
            if(!instance)return; // BepInEx starts before the native settings scene creates this singleton.
            var cfg=settingsType.GetMethod("get_CFG",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,null);
            if(cfg==null)return;
            foreach(var name in new[]{"push_to_talk","push_to_look"})
                bindings.UpdateDefault("game."+name,new KeyChord(((KeyCode)cfg.GetType().GetField("key_"+name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(cfg)).ToString()));
        }
        private static bool Typing()
        {
            var selected=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;if(!selected)return false;
            foreach(var component in selected.GetComponents<Component>())
                if(component)
                {
                    var input=component as InputField;if(input&&input.isFocused)return true;
                    if(component.GetType().Name=="TMP_InputField"&&component.GetType().GetProperty("isFocused")?.GetValue(component,null) is bool focused&&focused)return true;
                }
            return false;
        }
        private void Sample()
        {
            if(bindings==null)return;
            if(pointerBlocker)pointerBlocker.SetActive(visible);
            bindings.Sample(Time.frameCount,chord=>UInput.GetKey((KeyCode)Enum.Parse(typeof(KeyCode),chord.Key,true))&&Modifiers()==chord.Modifiers,
                Application.isFocused&&!visible&&!NativeMenuOpen&&listening==null&&!Typing());
        }
        private void Update()
        {
            if(bindings==null)return;
            // The escape route is available even when the configuration window owns keyboard focus.
            if((visible||listening!=null)&&Application.isFocused&&UInput.GetKeyDown(KeyCode.Escape)){listening=null;visible=false;consumedFrame=Time.frameCount;}
            Sample();UpdateSpeechInput();RefreshNativeMenu();
            if(Time.unscaledTime>=nextDefaults){nextDefaults=Time.unscaledTime+1;try{RefreshNativeDefaults();}catch(Exception e){status=e.Message;}}
            if(listening!=null&&Time.frameCount>listenFrame+1&&Application.isFocused)
                foreach(var key in Keys.Distinct())if(key!=KeyCode.None&&key!=KeyCode.Escape&&!Modifier(key)&&UInput.GetKeyDown(key))
                {TryBind(listening,new KeyChord(key.ToString(),Modifiers()));captureFrame=Time.frameCount;RefreshNativeLabels();break;}
        }
        private void TryBind(string id,KeyChord chord)
        {try{bindings.SetBinding(id,chord);status="Saved. Changes apply immediately.";listening=null;}catch(Exception e){status=e.Message;}}
        private static bool Query(KeyCode native,string id,int kind)
        {
            var self=current;if(!self||self.bindings==null)return kind==0?UInput.GetKey(native):kind==1?UInput.GetKeyDown(native):UInput.GetKeyUp(native);
            self.Sample();
            if(self.visible||self.NativeMenuOpen||self.listening!=null||self.consumedFrame==Time.frameCount)return false;
            if(id=="@guard"||!self.bindings.Overridden(id))
            {
                // A modified shortcut owns its primary key; don't also trigger the native unmodified action.
                if(self.bindings.ClaimsPrimaryKey(id,native.ToString()))return false;
                return kind==0?UInput.GetKey(native):kind==1?UInput.GetKeyDown(native):UInput.GetKeyUp(native);
            }
            return self.bindings.Query(id,kind);
        }
        public static bool Held(KeyCode key,string id)=>Query(key,id,0);
        public static bool Down(KeyCode key,string id)=>Query(key,id,1);
        public static bool Up(KeyCode key,string id)=>Query(key,id,2);
        private static bool BlockMouse(){if(current)current.Sample();return current&&(current.visible||current.listening!=null||current.consumedFrame==Time.frameCount);}
        public static bool MouseHeld(int button)=>!BlockMouse()&&UInput.GetMouseButton(button);
        public static bool MouseDown(int button)=>!BlockMouse()&&UInput.GetMouseButtonDown(button);
        public static bool MouseUp(int button)=>!BlockMouse()&&UInput.GetMouseButtonUp(button);
        public static float Axis(string axis)=>BlockMouse()?0:UInput.GetAxis(axis);
        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions,MethodBase original)
        {
            var code=instructions.ToList();int changed=0;
            for(int i=0;i<code.Count;i++)
            {
                var instruction=code[i];var method=instruction.operand as MethodInfo;string id=null;
                if(method!=null&&method.DeclaringType==typeof(UInput))
                {
                    var guard=method.Name=="GetMouseButton"?nameof(MouseHeld):method.Name=="GetMouseButtonDown"?nameof(MouseDown):
                        method.Name=="GetMouseButtonUp"?nameof(MouseUp):method.Name=="GetAxis"?nameof(Axis):null;
                    if(guard!=null){instruction.operand=typeof(InputDriver).GetMethod(guard);yield return instruction;changed++;continue;}
                }
                if(i>0&&method!=null&&method.DeclaringType==typeof(UInput)&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==typeof(KeyCode))
                {
                    var field=code[i-1].operand as FieldInfo;
                    if(field!=null&&(field.Name=="key_push_to_talk"||field.Name=="key_push_to_look"))id="game."+field.Name.Substring(4);
                    else if(original.DeclaringType.Name=="FreeCamController")
                    {
                        int value;
                        if(code[i-1].opcode==OpCodes.Ldc_I4||code[i-1].opcode==OpCodes.Ldc_I4_S)
                        {value=Convert.ToInt32(code[i-1].operand);var key=(KeyCode)value;if(!Modifier(key)&&Enum.IsDefined(typeof(KeyCode),key))id="game.freecam."+key;}
                    }
                    if(id==null)id="@guard";
                }
                if(id==null){yield return instruction;continue;}
                string target=method.Name=="GetKey"?nameof(Held):method.Name=="GetKeyDown"?nameof(Down):method.Name=="GetKeyUp"?nameof(Up):null;
                if(target==null){yield return instruction;continue;}
                var load=new CodeInstruction(OpCodes.Ldstr,id);load.labels.AddRange(instruction.labels);instruction.labels.Clear();load.blocks.AddRange(instruction.blocks);instruction.blocks.Clear();
                yield return load;instruction.operand=typeof(InputDriver).GetMethod(target);yield return instruction;changed++;
            }
            if(changed==0)throw new NotSupportedException("No reviewed input sites in "+original.Name);
        }
        private void OnDestroy()
        {
            ReleaseSpeechInput();RestoreMenu();harmony.UnpatchSelf();if(bindings!=null)InputServices.Detach(bindings);
            foreach(var registration in registrations)registration.Dispose();registrations.Clear();if(ReferenceEquals(current,this))current=null;
        }
    }
}





