using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TS3Modding.InputMod
{
    public sealed partial class InputDriver
    {
        // Read by the native TCP callback; all Unity input sampling stays on the main thread.
        private volatile int speechOverride=-1;
        private MethodInfo nativeSpeechButton,nativeTestMode;
        private FieldInfo speechInstance,nativeGameInstance,nativeGameLoading;
        private object drivenSpeaker;
        private void InitializeSpeechInput()
        {
            var speech=assembly.GetType("TowerSpeak",true);
            nativeSpeechButton=AccessTools.Method(speech,"SetRecButtonState",new[]{typeof(bool)});
            nativeTestMode=AccessTools.Method(speech,"GetTestmode");speechInstance=AccessTools.Field(speech,"instance");
            var game=assembly.GetType("Game",true);nativeGameInstance=AccessTools.Field(game,"instance");nativeGameLoading=AccessTools.Field(game,"loading");
            if(nativeSpeechButton==null||nativeTestMode==null||speechInstance==null)throw new NotSupportedException("Native speech input contract changed.");
            harmony.Patch(AccessTools.Method(assembly.GetType("TCPBridge",true),"ELAMIPMCJGM"),transpiler:new HarmonyMethod(typeof(InputDriver),nameof(RewriteSpeechBridge)));
        }
        private static IEnumerable<CodeInstruction> RewriteSpeechBridge(IEnumerable<CodeInstruction> instructions)
        {
            int changed=0;
            foreach(var instruction in instructions)
            {
                var method=instruction.operand as MethodInfo;
                if(method!=null&&method.DeclaringType.Name=="TowerSpeak"&&method.Name=="SetRecButtonState")
                {instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=AccessTools.Method(typeof(InputDriver),nameof(SpeechButton));changed++;}
                yield return instruction;
            }
            if(changed!=2)throw new NotSupportedException("Native TCP push-to-talk sites changed: "+changed);
        }
        public static void SpeechButton(object speaker,bool nativeHeld)
        {
            var self=current;
            if(ReferenceEquals(self,null))return;
            var state=self.speechOverride;
            self.nativeSpeechButton.Invoke(speaker,new object[]{state<0?nativeHeld:state==1});
        }
        private void UpdateSpeechInput()
        {
            var speaker=speechInstance.GetValue(null) as UnityEngine.Object;
            var game=nativeGameInstance.GetValue(null) as UnityEngine.Object;
            bool canDrive=bindings.Overridden("game.push_to_talk")&&speaker&&game&&(bool)AccessTools.Field(speaker.GetType(),"inited").GetValue(speaker)&&!(bool)nativeGameLoading.GetValue(game)&&!(bool)nativeTestMode.Invoke(speaker,null);
            if(canDrive)
            {
                var settingsType=assembly.GetType("Settings");
                var cfg=AccessTools.Method(settingsType,"get_CFG").Invoke(null,null);
                canDrive=cfg!=null&&!(bool)AccessTools.Field(cfg.GetType(),"disable_voice_recog_control").GetValue(cfg);
            }
            if(!canDrive){ReleaseSpeechInput();return;}
            var held=Application.isFocused&&!visible&&!NativeMenuOpen&&listening==null&&!Typing()&&bindings.Query("game.push_to_talk",0);
            speechOverride=held?1:0;
            if(!ReferenceEquals(drivenSpeaker,speaker)&&drivenSpeaker!=null)ReleaseSpeechInput();
            speechOverride=held?1:0;drivenSpeaker=speaker;
            nativeSpeechButton.Invoke(speaker,new object[]{held});
        }
        private void ReleaseSpeechInput()
        {
            speechOverride=-1;
            if(drivenSpeaker is UnityEngine.Object obj&&obj)nativeSpeechButton?.Invoke(drivenSpeaker,new object[]{false});
            drivenSpeaker=null;
        }
    }
}
