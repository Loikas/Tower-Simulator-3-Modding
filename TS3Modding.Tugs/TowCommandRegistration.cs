using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using TS3Modding.API;
using UnityEngine;
namespace TS3Modding.Tugs
{
    // Tow owns parsing and immutable ownership/stand snapshots. The framework owns every native voice/radio hook.
    internal sealed class TowCommandRegistration:IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        private readonly FieldInfo name,gameInstance,altitude,schedule;
        private readonly Dictionary<Component,GroundAddress> groundCache=new Dictionary<Component,GroundAddress>();
        private sealed class GroundAddress{internal object Schedule;internal TowSpeechAircraft Address;}
        private volatile TowSpeechAircraft[] groundAddresses=new TowSpeechAircraft[0];
        private float nextGroundSnapshot;private object groundGame;
        private readonly IDisposable registration;
        private volatile TowSpeechAircraft[] holdAddresses=new TowSpeechAircraft[0];
        private volatile Component[] holdPlanes=new Component[0];
        private volatile KeyValuePair<string,string>[] standAliases=new KeyValuePair<string,string>[0];
        private object aliasGame;
        private volatile string[] taxiwayNames=new string[0];
        internal TowCommandRegistration(Assembly assembly,Action<Component,TowVoiceIntent> handle,Action<Component,string> reply,
            Func<string,string> resolveSpeech,Action<string> log=null)
        {
            gameInstance=assembly.GetType("Game",true).GetField("instance",Flags);name=assembly.GetType("Airplane",true).GetField("_name",Flags);altitude=assembly.GetType("Airplane",true).GetField("_alt",Flags);schedule=assembly.GetType("Airplane",true).GetField("_timetable",Flags);
            var service=RadioCommands.Current as IRadioCommandProviderService;
            if(service==null)throw new NotSupportedException("Tow voice requires Framework 0.8.21 command providers.");
            registration=service.RegisterProvider(new RadioCommandProvider("nl.ts3modding.tugs.commands",
                address=>OnGround(address.NativeAircraft)&&((TowVoiceIntent.IsTow(address.Instruction)&&(Owns(address.NativeAircraft)||TowVoiceIntent.Parse(TowStrictGrammar.NormalizeTargets(address.Instruction,standAliases))!=null))||(Owns(address.NativeAircraft)&&(TowVoiceIntent.IsScopedInstruction(address.Instruction)||TowVoiceIntent.RecoverTaxiSpeech(address.Instruction)!=address.Instruction))),
                command=>{
                    var plane=command.NativeAircraft as Component;
                    var instruction=Owns(plane)?TowVoiceIntent.RecoverTaxiSpeech(command.Phrase):command.Phrase;
                    var intent=TowVoiceIntent.Parse(TowTaxiwaySpeech.Normalize(TowStrictGrammar.NormalizeTargets(instruction,standAliases),taxiwayNames));
                    if(intent==null){reply(plane,"Unable, tow instruction not understood, say again");return;}
                    try{handle(plane,intent);}catch(Exception error){log?.Invoke("Tow command failed: "+error);reply(plane,"Unable, "+error.Message);}
                },CaptureSpeech,text=>{
                    var recovered=TowVoiceIntent.RecoverTaxiSpeech(text);
                    if(recovered!=text){try{TowSpokenAddress.Command(recovered,holdAddresses);}catch{return null;}}
                    return resolveSpeech(TowTaxiwaySpeech.Normalize(recovered,taxiwayNames));
                },TowStrictGrammar.ExtendCommands,
                original=>TowStrictGrammar.ExtendTargets(original,standAliases),requireRadioContact:false,duplicateWindowSeconds:2));
        }
        internal void PublishHoldingAircraft(Component[] aircraft)
        {
            aircraft=aircraft.Where(p=>p&&OnGround(p)).ToArray();
            holdAddresses=aircraft.Select(p=>{string spoken=null;try{spoken=(string)p.GetType().GetMethod("get__sayname",Flags)?.Invoke(p,null);}catch{}
                return new TowSpeechAircraft{id=(string)name.GetValue(p),pronunciations=new[]{spoken}};}).ToArray();holdPlanes=aircraft;
        }
        private bool OnGround(object aircraft)
        {
            if(aircraft==null)return false;
            var height=(float)altitude.GetValue(aircraft);
            return !float.IsNaN(height)&&!float.IsInfinity(height)&&height<3;
        }
        private bool Owns(object aircraft)=>holdPlanes.Any(p=>ReferenceEquals(p,aircraft));
        private string CaptureSpeech(string text)
        {
            var normalized=TowVoiceIntent.NormalizeSpeech(text);
            var recoveredTaxi=TowVoiceIntent.RecoverTaxiSpeech(normalized);
            if(recoveredTaxi!=normalized)
            {
                try{TowSpokenAddress.Command(recoveredTaxi,holdAddresses);return normalized;}catch{return null;}
            }
            if(!TowVoiceIntent.IsTow(normalized))
            {
                var candidate=TowVoiceIntent.IsScopedInstruction(normalized)?normalized:TowVoiceIntent.RecoverOwnedSpeech(normalized);
                if(candidate==normalized&&!TowVoiceIntent.IsScopedInstruction(normalized))return null;
                try{TowSpokenAddress.Command(candidate,holdAddresses);normalized=candidate;}catch{return null;}
            }
            // Explicit tow text must still address one eligible ground aircraft. A stray
            // tow word in a native instruction is never enough to capture unrelated traffic.
            try
            {
                var command=TowSpokenAddress.Command(normalized,groundAddresses.Concat(holdAddresses));
                var split=command.IndexOf(' ');var callsign=command.Substring(0,split);
                var owned=holdAddresses.Any(a=>string.Equals(a.id,callsign,StringComparison.OrdinalIgnoreCase));
                if(!owned&&TowVoiceIntent.Parse(command.Substring(split+1))==null)return null;
            }catch{return null;}
            return normalized;
        }
        private void PublishGroundAddresses()
        {
            var game=gameInstance.GetValue(null);if(!ReferenceEquals(game,groundGame)){groundGame=game;groundCache.Clear();nextGroundSnapshot=0;groundAddresses=new TowSpeechAircraft[0];}
            if(game==null||Time.realtimeSinceStartup<nextGroundSnapshot)return;nextGroundSnapshot=Time.realtimeSinceStartup+.5f;
            var planes=game.GetType().GetField("airplanes",Flags)?.GetValue(game) as System.Collections.IEnumerable;
            var result=new List<TowSpeechAircraft>();
            if(planes!=null)foreach(var value in planes)
            {
                var plane=value as Component;if(!plane||!OnGround(plane))continue;
                try{
                    if((bool)plane.GetType().GetField("_deleted",Flags).GetValue(plane))continue;
                    var flight=schedule.GetValue(plane);var callsign=(string)name.GetValue(plane);GroundAddress cached;
                    if(!groundCache.TryGetValue(plane,out cached)||!ReferenceEquals(cached.Schedule,flight)||cached.Address.id!=callsign)
                        groundCache[plane]=cached=new GroundAddress{Schedule=flight,Address=new TowSpeechAircraft{id=callsign,pronunciations=new[]{(string)plane.GetType().GetMethod("get__sayname",Flags).Invoke(plane,null)}}};
                    result.Add(cached.Address);
                }catch{}
            }
            groundAddresses=result.ToArray();
            var live=new HashSet<TowSpeechAircraft>(groundAddresses);foreach(var entry in groundCache.Where(p=>!live.Contains(p.Value.Address)).Select(p=>p.Key).ToArray())groundCache.Remove(entry);
        }
        private void PublishStandAliases()
        {
            var game=gameInstance.GetValue(null);if(game==null){aliasGame=null;taxiwayNames=new string[0];standAliases=new KeyValuePair<string,string>[0];return;}
            if(ReferenceEquals(game,aliasGame))return;
            var loading=game.GetType().GetField("loading",Flags)?.GetValue(game) is bool busy&&busy;
            var field=game.GetType().GetField("airfield",Flags)?.GetValue(game);if(field==null)return;
            var data=field.GetType().GetField("aircont",Flags)?.GetValue(field);if(data==null)return;
            var roads=data.GetType().GetField("roads",Flags)?.GetValue(data) as System.Collections.IEnumerable;if(roads==null)return;
            var nativeRoads=roads.Cast<object>().Where(r=>r!=null).ToArray();
            taxiwayNames=nativeRoads.Where(r=>Convert.ToInt32(r.GetType().GetField("type",Flags).GetValue(r))==0).Select(r=>(string)r.GetType().GetField("name",Flags).GetValue(r)).Where(n=>!string.IsNullOrEmpty(n)).Distinct().ToArray();
            var names=nativeRoads.Where(r=>r!=null&&Convert.ToInt32(r.GetType().GetField("type",Flags).GetValue(r))==1).Select(r=>(string)r.GetType().GetField("name",Flags).GetValue(r)).Where(n=>!string.IsNullOrEmpty(n)).Distinct().ToArray();
            if(names.Length==0)return;
            var map=new TowStandAliases(names);standAliases=names.Select(n=>new KeyValuePair<string,string>(n,map.Alias(n))).ToArray();aliasGame=loading?null:game;
        }
        internal void Tick(){PublishGroundAddresses();PublishStandAliases();}
        public void Dispose(){registration.Dispose();taxiwayNames=new string[0];groundCache.Clear();groundAddresses=new TowSpeechAircraft[0];holdPlanes=new Component[0];holdAddresses=new TowSpeechAircraft[0];standAliases=new KeyValuePair<string,string>[0];}
    }
}
