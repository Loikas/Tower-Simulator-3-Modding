using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace TS3Modding.Effects
{
    // Owns only added emitters and explicitly enabled native loop replacements.
    // Aircraft engine mute/play state remains exclusively native/Tug Operations owned.
    internal sealed class Soundscape : IDisposable
    {
        private readonly ConfigEntry<bool> jets, apu, ground, weatherEnabled;
        private readonly ConfigEntry<float> apuVolume, groundVolume, windVolume, thunderVolume, interior;
        private readonly AudioClip jetClip, apuClip, groundClip, windClip, thunderClip;
        private readonly FieldInfo gameInstance, gameSpeed, weatherInstance, wind, thunder, towerInstance, inside;
        private readonly FieldInfo aircraftList, aircraftActive, aircraftDeleted, aircraftStand, aircraftSound, aircraftContainer, soundType, soundPlane, slotSource;
        private readonly FieldInfo[] loopSlots, servicing;
        private readonly Type tugType;
        private readonly FieldInfo tugSleep, tugPlane;
        private readonly MethodInfo getConfig;
        private readonly FieldInfo masterVolume, effectVolume;
        private readonly int[] jetTypes;
        private readonly AudioSource windSource, thunderSource;
        private readonly Transform root;
        private readonly Dictionary<AudioSource, AudioClip> originals = new Dictionary<AudioSource, AudioClip>();
        private readonly Dictionary<Component, Emitter> emitters = new Dictionary<Component, Emitter>();
        private float nextScan, nextThunder;
        private bool wasReady;
        private sealed class Emitter { internal AudioSource Source; internal bool Apu; }

        internal Soundscape(Transform owner, ConfigFile config, Assembly game)
        {
            root=owner;
            jets=config.Bind("Aircraft sound","UseRecordedJetLoops",true,"Use a generic recorded jet loop for jet idle/taxi/takeoff. Props retain native sounds. Not an aircraft-specific sound pack. Applies immediately.");
            apu=config.Bind("APU sound","EnableApuAmbience",true,"Add a quiet APU layer to active parked jets with engines off. This uses a cabin APU recording adapted as ambience, not a simulated APU system.");
            apuVolume=Volume(config,"APU sound","ApuVolume",.12f,"Parked jet APU ambience level.");
            ground=config.Bind("Ground sound","EnableGroundActivity",true,"Add spatial vehicle engine audio to active tugs and aircraft with native servicing flags. No radio or speech added.");
            groundVolume=Volume(config,"Ground sound","GroundVolume",.16f,"Tug and servicing vehicle engine level.");
            weatherEnabled=config.Bind("Weather sound","EnableWeatherAmbience",true,"Wind follows native wind force. Random distant thunder plays only in native thunder conditions; it is not synchronized to lightning flashes.");
            windVolume=Volume(config,"Weather sound","WindVolume",.18f,"Wind level at strong wind conditions.");
            thunderVolume=Volume(config,"Weather sound","ThunderVolume",.25f,"Distant thunder level.");
            interior=Volume(config,"Soundscape","TowerInteriorVolume",.25f,"Added sound attenuation inside the tower; native jet loops retain native attenuation.");
            var gameType=game.GetType("Game",true);gameInstance=F(gameType,"instance");gameSpeed=F(gameType,"speedup");aircraftList=F(gameType,"airplanes");
            var weather=game.GetType("Weather",true);weatherInstance=F(weather,"instance");wind=F(weather,"windforce");thunder=F(weather,"thunder");
            var tower=game.GetType("TowerBuildingControl",true);towerInstance=F(tower,"instance");inside=F(tower,"playerIsInTower");
            var settings=game.GetType("Settings",true);getConfig=AccessTools.Method(settings,"get_CFG",Type.EmptyTypes)??throw new MissingMethodException("Settings.get_CFG");
            masterVolume=F(getConfig.ReturnType,"vol_master");effectVolume=F(getConfig.ReturnType,"vol_sfx");
            var plane=game.GetType("Airplane",true);aircraftActive=F(plane,"_active");aircraftDeleted=F(plane,"_deleted");aircraftStand=F(plane,"_imatterminal");aircraftSound=F(plane,"sound");aircraftContainer=F(plane,"ac");
            servicing=new[]{"_live_food","_live_bus","_live_tanker","_live_cargo","_live_package"}.Select(n=>F(plane,n)).ToArray();
            var container=game.GetType("AirplaneContainer",true);soundType=F(container,"sfx_type");
            jetTypes=new[]{"WIDE_BODY_JET","NARROW_BODY_JET","REGIONAL_JET","BUSINESS"}.Select(n=>Convert.ToInt32(Enum.Parse(soundType.FieldType,n))).ToArray();
            var sound=game.GetType("AirplaneSound",true);soundPlane=F(sound,"BNMGGAOAPNE");loopSlots=new[]{"sfx_idle","sfx_taxi","sfx_takeoff"}.Select(n=>F(sound,n)).ToArray();slotSource=F(loopSlots[0].FieldType,"src");
            tugType=game.GetType("TowTruck",true);tugSleep=F(tugType,"sleep");tugPlane=F(tugType,"_plane");
            // Decode owned PCM resources once. Never copy or redistribute game recordings.
            jetClip=Load("jet");apuClip=Load("apu");groundClip=Load("ground");windClip=Load("wind");thunderClip=Load("thunder");
            windSource=Create("Wind",windClip,false);thunderSource=Create("Thunder",thunderClip,false);thunderSource.loop=false;
            nextThunder=Time.unscaledTime+UnityEngine.Random.Range(30,60);
        }
        private static FieldInfo F(Type type,string name)=>AccessTools.Field(type,name)??throw new MissingFieldException(type.FullName,name);
        private static ConfigEntry<float> Volume(ConfigFile config,string group,string name,float value,string text)
            =>config.Bind(group,name,value,new ConfigDescription(text,new AcceptableValueRange<float>(0,1)));
        private static AudioClip Load(string name)
        {
            using(var stream=typeof(Soundscape).Assembly.GetManifestResourceStream("TS3Modding.Effects.Audio."+name+".wav"))
            {
                var pcm=PcmWave.Read(stream);
                var clip=AudioClip.Create("TS3 Effects "+name,pcm.Samples.Length/pcm.Channels,pcm.Channels,pcm.Rate,false);
                clip.SetData(pcm.Samples,0);return clip;
            }
        }
        private AudioSource Create(string name,AudioClip clip,bool spatial)
        {
            var go=new GameObject("TS3 Effects "+name);go.transform.SetParent(root,false);
            var src=go.AddComponent<AudioSource>();src.playOnAwake=false;src.loop=true;src.clip=clip;src.volume=0;
            src.spatialBlend=spatial?1:0;src.dopplerLevel=0;src.minDistance=15;src.maxDistance=350;src.rolloffMode=AudioRolloffMode.Logarithmic;
            return src;
        }
        private bool IsJet(Component plane)
        {var ac=aircraftContainer.GetValue(plane) as Component;return ac && jetTypes.Contains(Convert.ToInt32(soundType.GetValue(ac)));}
        internal void ApplyJetLoops(object sound,bool allowed)
        {
            var plane=soundPlane.GetValue(sound) as Component;
            var game=gameInstance.GetValue(null) as UnityEngine.Object;
            bool replace=allowed && game && Convert.ToSingle(gameSpeed.GetValue(game))>0 && jets.Value && plane && IsJet(plane);
            foreach(var slot in loopSlots)
            {
                var block=slot.GetValue(sound);var src=block==null?null:slotSource.GetValue(block) as AudioSource;
                if(!src)continue;
                if(replace && src.loop)
                {
                    if(!originals.ContainsKey(src))originals[src]=src.clip;
                    if(src.clip!=jetClip)src.clip=jetClip;
                    // Let native Update start the source when appropriate; never override mute.
                }
                else RestoreClip(src);
            }
        }
        private void RestoreClip(AudioSource src)
        {if(originals.TryGetValue(src,out var clip)){if(src && src.clip==jetClip)src.clip=clip;originals.Remove(src);}}
        internal void Tick(bool ready)
        {
            var game=gameInstance.GetValue(null) as UnityEngine.Object;
            bool playing=ready && game && Convert.ToSingle(gameSpeed.GetValue(game))>0 && !AudioListener.pause;
            if(!playing)
            {
                windSource.Stop();thunderSource.Stop();foreach(var item in emitters.Values)if(item.Source)item.Source.Stop();
                foreach(var src in originals.Keys.ToArray())RestoreClip(src);
                wasReady=false;return;
            }
            if(!wasReady){nextScan=0;nextThunder=Time.unscaledTime+UnityEngine.Random.Range(30,60);wasReady=true;}
            var tower=towerInstance.GetValue(null) as UnityEngine.Object;
            float attenuation=tower && (bool)inside.GetValue(tower)?interior.Value:1;
            var config=getConfig.Invoke(null,null);
            attenuation*=Mathf.Clamp01(Convert.ToSingle(masterVolume.GetValue(config)))*Mathf.Clamp01(Convert.ToSingle(effectVolume.GetValue(config)));
            var weather=weatherInstance.GetValue(null) as UnityEngine.Object;
            float force=weather?Convert.ToSingle(wind.GetValue(weather)):0;
            float storm=weather?Mathf.Clamp01(Convert.ToSingle(thunder.GetValue(weather))):0;
            PlayLoop(windSource,weatherEnabled.Value?Mathf.Clamp01(force/35)*windVolume.Value*attenuation:0);
            if(!weatherEnabled.Value || storm<=.01f)thunderSource.Stop();
            else if(Time.unscaledTime>=nextThunder && !thunderSource.isPlaying)
            {thunderSource.volume=thunderVolume.Value*attenuation*storm;thunderSource.Play();nextThunder=Time.unscaledTime+UnityEngine.Random.Range(45,120)/Mathf.Max(.25f,storm);}
            else if(thunderSource.isPlaying)thunderSource.volume=thunderVolume.Value*attenuation*storm;
            if(Time.unscaledTime>=nextScan){Scan(game);nextScan=Time.unscaledTime+1;}
            foreach(var pair in emitters.ToArray())
            {
                var target=pair.Key;var emitter=pair.Value;
                if(!target || !target.gameObject.activeInHierarchy){Remove(target);continue;}
                emitter.Source.transform.position=target.transform.position;
                bool live;
                if(emitter.Apu)
                {
                    var sound=aircraftSound.GetValue(target);var status=sound==null?null:AccessTools.Field(sound.GetType(),"current").GetValue(sound);
                    live=apu.Value && (bool)aircraftStand.GetValue(target) && status!=null && (status.ToString()=="NONE" || status.ToString()=="SHUTDOWN");
                }
                else live=ground.Value && (target.GetType()==tugType?!(bool)tugSleep.GetValue(target) && tugPlane.GetValue(target)!=null:servicing.Any(f=>(bool)f.GetValue(target)));
                PlayLoop(emitter.Source,live?(emitter.Apu?apuVolume.Value:groundVolume.Value)*attenuation:0);
            }
            foreach(var src in originals.Keys.Where(s=>!s).ToArray())originals.Remove(src);
        }
        private void Scan(UnityEngine.Object game)
        {
            var desired=new Dictionary<Component,bool>();
            var planes=aircraftList.GetValue(game) as System.Collections.IEnumerable;
            if(planes!=null)foreach(var obj in planes)
            {
                var p=obj as Component;if(!p || !(bool)aircraftActive.GetValue(p) || (bool)aircraftDeleted.GetValue(p))continue;
                if(ground.Value && servicing.Any(f=>(bool)f.GetValue(p)))desired[p]=false;
                // A servicing emitter shares the aircraft position; no fictional ground crews are spawned.
                else if(apu.Value && IsJet(p) && (bool)aircraftStand.GetValue(p))desired[p]=true;
            }
            if(ground.Value)foreach(var obj in UnityEngine.Object.FindObjectsOfType(tugType))
            {var tug=obj as Component;if(tug && !(bool)tugSleep.GetValue(tug) && tugPlane.GetValue(tug)!=null)desired[tug]=false;}
            // Bound active voices and prefer objects close to the listening view.
            var listener=UnityEngine.Object.FindObjectOfType<AudioListener>();
            var origin=listener?listener.transform.position:Vector3.zero;
            var nearest=desired.Where(p=>(p.Key.transform.position-origin).sqrMagnitude<350*350).OrderBy(p=>(p.Key.transform.position-origin).sqrMagnitude).Take(24).ToArray();
            var keep=new HashSet<Component>(nearest.Select(p=>p.Key));
            foreach(var target in emitters.Keys.Where(p=>!p || !keep.Contains(p)).ToArray())Remove(target);
            foreach(var pair in nearest)
            {
                if(emitters.TryGetValue(pair.Key,out var existing) && existing.Apu!=pair.Value)Remove(pair.Key);
                if(!emitters.ContainsKey(pair.Key))emitters[pair.Key]=new Emitter{Apu=pair.Value,Source=Create(pair.Value?"APU":"Ground",pair.Value?apuClip:groundClip,true)};
            }
        }
        private void Remove(Component target)
        {if(emitters.TryGetValue(target,out var item)){if(item.Source)UnityEngine.Object.Destroy(item.Source.gameObject);emitters.Remove(target);}}
        private static void PlayLoop(AudioSource src,float volume)
        {src.volume=Mathf.MoveTowards(src.volume,Mathf.Clamp01(volume),Time.unscaledDeltaTime*.5f);if(volume<=0){src.Stop();src.volume=0;}else if(!src.isPlaying)src.Play();}
        public void Dispose()
        {
            foreach(var src in originals.Keys.ToArray())RestoreClip(src);
            foreach(var p in emitters.Keys.ToArray())Remove(p);
            if(windSource)UnityEngine.Object.Destroy(windSource.gameObject);if(thunderSource)UnityEngine.Object.Destroy(thunderSource.gameObject);
            foreach(var c in new[]{jetClip,apuClip,groundClip,windClip,thunderClip})if(c)UnityEngine.Object.Destroy(c);
        }
    }
}
