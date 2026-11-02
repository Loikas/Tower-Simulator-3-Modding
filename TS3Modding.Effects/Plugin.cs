using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding.Effects
{
    [BepInPlugin("nl.ts3modding.effects", "TS3 Aircraft Effects", "0.2.5")]
    [BepInDependency("nl.ts3modding.loader", "0.8.3")]
    [BepInProcess("Tower! Simulator 3.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            if (FrameworkServices.Current?.GameAssemblySha256 != EffectsDriver.Hash)
            { Logger.LogError("Aircraft Effects disabled: unreviewed game assembly."); return; }
            var host = new GameObject("TS3Modding.Effects");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            try { host.AddComponent<EffectsDriver>().Initialize(Config, s => Logger.LogInfo(s)); }
            catch (Exception e) { Destroy(host); Logger.LogError("Aircraft Effects disabled: " + e); }
        }
        // The game removes bootstrap components; the persistent driver owns runtime cleanup.
    }

    public sealed class EffectsDriver : MonoBehaviour
    {
        internal const string Hash = "26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7";
        private static EffectsDriver instance;
        private Harmony harmony;
        private Soundscape soundscape;
        private WheelMaterials wheelMaterials;
        private Action<string> log;
        private bool failed, reported, smokeReported;
        private readonly Dictionary<Component,float> touchdownTimes=new Dictionary<Component,float>();
        private ConfigEntry<bool> modEnabled, smoke, spray, mix;
        private ConfigEntry<float> smokeStrength, sprayStrength, engineVolume, reverserVolume;
        private FieldInfo plane, active, deleted, state, altitude, dust, splash, particle, fullRate, power, fullSpeed;
        private FieldInfo weatherInstance, rain, wetness, gameInstance, loading;
        private MethodInfo speed;
        private int landingState, takeoffState;
        private FieldInfo[] sounds;
        private FieldInfo source;
        private readonly List<IDisposable> settings = new List<IDisposable>();
        private readonly Dictionary<AudioSource, VolumeLease> volumes = new Dictionary<AudioSource, VolumeLease>();
        private readonly HashSet<ParticleSystem> sprayEmitters = new HashSet<ParticleSystem>();
        private readonly Dictionary<Component,TouchdownGate> touchdownGates=new Dictionary<Component,TouchdownGate>();
        private FieldInfo assetsInstance;
        private MethodInfo createEffect;
        private readonly Dictionary<string,FieldInfo> wheelPrefabs=new Dictionary<string,FieldInfo>();
        private readonly Dictionary<Component,List<OwnedWheel>> ownedWheels=new Dictionary<Component,List<OwnedWheel>>();
        private readonly HashSet<Component> wheelScanned=new HashSet<Component>();
        private sealed class OwnedWheel { internal IList List;internal object Block;internal ParticleSystem Particle; }
        private sealed class VolumeLease { internal float Native, Applied; }

        internal void Initialize(ConfigFile config, Action<string> logger)
        {
            log = logger;
            wheelMaterials=new WheelMaterials(log);
            modEnabled = config.Bind("General", "Enabled", true, "Enable Aircraft Effects. Applies immediately.");
            smoke = config.Bind("Touchdown visuals", "EnhanceTireSmoke", true, "Add a wheel-smoke puff when an observed airborne aircraft touches down, and adjust native smoke intensity. Disabling keeps native game smoke.");
            smokeStrength = Number(config,"Touchdown visuals","SmokeIntensity",1.4f,0,3,"Native landing smoke multiplier.");
            spray = config.Bind("Wet runway visuals", "EnableWheelSpray", true, "Enable native wheel spray on wet runways during landing and takeoff rolls.");
            sprayStrength = Number(config,"Wet runway visuals","SprayIntensity",1,0,3,"Wheel spray multiplier; strength follows current rain/wetness and speed.");
            mix = config.Bind("Aircraft sound", "EnableSoundMix", true, "Separate native engine and reverser volume controls. Preserves native radio, distance attenuation and tug muting; does not replace recordings.");
            engineVolume = Number(config,"Aircraft sound","EngineVolume",1,0,2,"Native engine volume multiplier.");
            reverserVolume = Number(config,"Aircraft sound","ReverserVolume",1,0,2,"Native reverser volume multiplier.");
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
            var effect = assembly.GetType("AirplaneEffect",true);
            var aircraft = assembly.GetType("Airplane",true);
            var block = effect.GetNestedType("OJGCNNDOCHB",BindingFlags.Public|BindingFlags.NonPublic);
            plane = Field(effect,"BNMGGAOAPNE"); dust = Field(effect,"IAHNJNDEMNC"); splash = Field(effect,"IPJJIFKMGKC");
            active = Field(aircraft,"_active"); deleted = Field(aircraft,"_deleted"); state = Field(aircraft,"_state"); altitude = Field(aircraft,"_alt");
            landingState=Convert.ToInt32(Enum.Parse(state.FieldType,"STATE_LAND"));
            takeoffState=Convert.ToInt32(Enum.Parse(state.FieldType,"STATE_TAKEOFFUP"));
            speed = AccessTools.Method(aircraft,"GetSpeedInKnot",Type.EmptyTypes) ?? throw new MissingMethodException("GetSpeedInKnot");
            particle = Field(block,"ps"); fullRate = Field(block,"fullrate"); fullSpeed = Field(block,"fullspeed"); power = Field(block,"power");
            var assets=assembly.GetType("AssetsManager",true);assetsInstance=Field(assets,"instance");
            createEffect=AccessTools.Method(effect,"BIPKHEGBNEK",new[]{typeof(GameObject),typeof(Transform)})??throw new MissingMethodException("Native particle factory");
            foreach(var size in new[]{"small","medium","large"})foreach(var kind in new[]{"dust","splash"})
                wheelPrefabs[kind+size]=Field(assets,"prefab_fx_"+kind+"_wheel_"+size);
            var weather = assembly.GetType("Weather",true);
            weatherInstance = Field(weather,"instance"); rain = Field(weather,"rain"); wetness = Field(weather,"wetness");
            var game = assembly.GetType("Game",true); gameInstance=Field(game,"instance"); loading=Field(game,"loading");
            var sound = assembly.GetType("AirplaneSound",true);
            sounds = new[]{"sfx_start","sfx_shutdown","sfx_idle","sfx_taxi","sfx_takeoff","sfx_landing","sfx_flight","sfx_reverser_open","sfx_reverser_loop","sfx_reverser_close"}.Select(n=>Field(sound,n)).ToArray();
            source = Field(sounds[0].FieldType,"src");
            instance=this;
            harmony=new Harmony("nl.ts3modding.effects");
            harmony.Patch(AccessTools.Method(effect,"Update"),postfix:new HarmonyMethod(typeof(EffectsDriver),nameof(Visuals)));
            harmony.Patch(AccessTools.Method(sound,"Update"),prefix:new HarmonyMethod(typeof(EffectsDriver),nameof(BeforeSound)),postfix:new HarmonyMethod(typeof(EffectsDriver),nameof(AfterSound)));
            soundscape=new Soundscape(transform,config,assembly);
            if(ModSettingsServices.Current!=null) foreach(var key in config.Keys)
                settings.Add(ModSettingsServices.Current.RegisterLiveSetting("nl.ts3modding.effects",key.Section,key.Key));
            log("Aircraft Effects 0.2.5 ready: lifetime-faded soft smoke/spray, recorded jet loops, APU/ground ambience, wind and thunder. Settings > Mods. Live visual/audio validation pending.");
        }
        private static ConfigEntry<float> Number(ConfigFile c,string group,string key,float value,float min,float max,string description)
            => c.Bind(group,key,value,new ConfigDescription(description,new AcceptableValueRange<float>(min,max)));
        private static FieldInfo Field(Type t,string n) => AccessTools.Field(t,n) ?? throw new MissingFieldException(t?.FullName,n);
        private bool Ready()
        {
            var game=gameInstance.GetValue(null) as UnityEngine.Object;
            return game && !(bool)loading.GetValue(game) && FrameworkServices.Current?.ObservationAvailable==true;
        }
        private static void Visuals(object __instance)
        {
            var self=instance; if(!self || self.failed)return;
            try { self.ApplyVisuals(__instance); } catch(Exception e) { self.Fail(e); }
        }
        private void ApplyVisuals(object effect)
        {
            var ap=plane.GetValue(effect) as Component;
            if(!ap)return;
            bool running=Ready();
            bool live=running && (bool)active.GetValue(ap) && !(bool)deleted.GetValue(ap);
            int phase=Convert.ToInt32(state.GetValue(ap));
            float alt=Convert.ToSingle(altitude.GetValue(ap));
            var component=effect as Component;
            if(live && component && modEnabled.Value && (smoke.Value || spray.Value))EnsureWheelEmitters(component,ap);
            if(component)
            {
                if(!touchdownGates.TryGetValue(component,out var gate))touchdownGates[component]=gate=new TouchdownGate();
                bool touchdown=gate.Observe(live,phase==landingState,alt);
                if(touchdown && modEnabled.Value && smoke.Value && smokeStrength.Value>0)
                {
                    touchdownTimes[component]=Time.time;
                    int emitters=0;
                    foreach(var item in Items(dust.GetValue(effect)))
                    {
                        var ps=particle.GetValue(item) as ParticleSystem;if(!ps)continue;
                        // Explicit emission is independent of native power/timer and the
                        // native world-Y height check, which can otherwise leave rate zero.
                        if(!ps.isPlaying)ps.Play(false);
                        int count=Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(8,Convert.ToSingle(fullRate.GetValue(item))*.22f)*smokeStrength.Value),1,90);
                        ps.Emit(count);emitters++;
                    }
                    log("Touchdown smoke puff: wheel emitters="+emitters+", altitude="+alt+", intensity="+smokeStrength.Value+". "+(emitters==0?"No native wheel smoke emitters available on this aircraft.":"Triggered once after observed flight."));
                }
            }
            // Native state 3 is LANDING; the native dust updater computes its own touchdown pulse.
            if(live && phase==landingState && alt<=1 && modEnabled.Value && smoke.Value)
                foreach(var item in Items(dust.GetValue(effect)))
                {
                    var ps=particle.GetValue(item) as ParticleSystem;
                    if(ps)
                    {
                        float pulse=component && touchdownTimes.TryGetValue(component,out var contact) ? ParticleFade.EmissionAt(Time.time-contact) : 0;
                        float rate=Convert.ToSingle(fullRate.GetValue(item))*pulse*smokeStrength.Value;
                        SetRate(ps,rate);
                        if(rate>0 && !smokeReported)
                        {
                            smokeReported=true;
                            log("Touchdown smoke enhancement triggered: multiplier="+smokeStrength.Value+", emission="+rate+", playing="+ps.isPlaying+", emissionEnabled="+ps.emission.enabled+". Native wheel smoke is brief; compare a close landing view.");
                        }
                    }
                }
            float knots=live ? Convert.ToSingle(speed.Invoke(ap,null)) : 0;
            var weather=weatherInstance.GetValue(null) as UnityEngine.Object;
            float wet=weather ? Mathf.Clamp01(Mathf.Max(Convert.ToSingle(rain.GetValue(weather)),Convert.ToSingle(wetness.GetValue(weather)))) : 0;
            // Only native landing/takeoff rolls, never taxiing/towing or airborne flight.
            float intensity=modEnabled.Value && spray.Value && live && (phase==landingState || phase==takeoffState) && alt<=1
                ? Mathf.Clamp01((knots-30)/90f)*wet*sprayStrength.Value : 0;
            foreach(var item in Items(splash.GetValue(effect)))
            {
                var ps=particle.GetValue(item) as ParticleSystem;if(!ps)continue;
                sprayEmitters.Add(ps);
                SetRate(ps,Convert.ToSingle(fullRate.GetValue(item))*intensity);
                if(intensity>0)
                {
                    var main=ps.main; main.startSpeed=Mathf.Min(2,Convert.ToSingle(fullSpeed.GetValue(item)))*Mathf.Min(intensity,1);
                    if(!ps.isPlaying)ps.Play();
                    if(!reported){reported=true;log("Wet runway wheel spray active: knots="+knots+", wetness="+wet+", intensity="+intensity+", emission="+Convert.ToSingle(fullRate.GetValue(item))*intensity+"; using authored wheel emitters.");}
                }
            }
        }
        private static IEnumerable Items(object list) => list as IEnumerable ?? new object[0];
        private void EnsureWheelEmitters(Component effect,Component aircraft)
        {
            var dustList=dust.GetValue(effect) as IList;var sprayList=splash.GetValue(effect) as IList;
            if(dustList==null || sprayList==null)return;
            if(wheelScanned.Contains(effect))
            {
                if(ownedWheels.TryGetValue(effect,out var previous))
                {
                    if(previous.Any(p=>!ReferenceEquals(p.List,dustList)&&!ReferenceEquals(p.List,sprayList)))ReleaseWheels(effect);
                    else return;
                }
                else if(dustList.Count>0 && sprayList.Count>0)return;
                else wheelScanned.Remove(effect);
            }
            if(dustList.Count>0 && sprayList.Count>0){wheelScanned.Add(effect);return;}
            // Stock models use uppercase WHEEL_* while native Update tests lowercase
            // names with case-sensitive Contains. Reuse exact authored markers, never
            // geometry guesses, mesh names or renamed original model transforms.
            var markers=aircraft.GetComponentsInChildren<Transform>(true).Where(t=>WheelMarker.Size(t.name)!=null).Take(32).ToArray();
            if(markers.Length==0)return; // model may still be loading; retry later.
            var assets=assetsInstance.GetValue(null) as UnityEngine.Object;if(!assets)return;
            var owned=new List<OwnedWheel>();ownedWheels[effect]=owned;
            bool addDust=dustList.Count==0,addSpray=sprayList.Count==0;
            foreach(var marker in markers)
            {
                string size=WheelMarker.Size(marker.name);
                foreach(var kind in new[]{"dust","splash"})
                {
                    if(kind=="dust"?!addDust:!addSpray)continue;
                    var prefab=wheelPrefabs[kind+size].GetValue(assets) as GameObject;if(!prefab)continue;
                    var block=createEffect.Invoke(effect,new object[]{prefab,marker});
                    var ps=particle.GetValue(block) as ParticleSystem;if(!ps)continue;
                    if(!wheelMaterials.Apply(ps,kind=="splash")){Destroy(ps.gameObject);continue;}
                    var list=kind=="dust"?dustList:sprayList;list.Add(block);owned.Add(new OwnedWheel{List=list,Block=block,Particle=ps});
                }
            }
            if(owned.Count>0)
            {wheelScanned.Add(effect);log("Wheel emitter repair: "+markers.Length+" authored markers, dust="+dustList.Count+", spray="+sprayList.Count+"; original model unchanged.");}
        }
        private void ReleaseWheels(Component effect)
        {
            if(ownedWheels.TryGetValue(effect,out var items))
            {foreach(var item in items){item.List.Remove(item.Block);if(item.Particle)Destroy(item.Particle.gameObject);}ownedWheels.Remove(effect);}
            wheelScanned.Remove(effect);
        }
        private static void SetRate(ParticleSystem ps,float rate)
        { var emission=ps.emission;emission.rateOverTime=Mathf.Max(0,rate); }
        private static void BeforeSound(object __instance)
        {
            var self=instance;if(!self)return;
            try { self.RestoreSound(__instance); self.soundscape?.ApplyJetLoops(__instance,!self.failed && self.modEnabled.Value && self.Ready()); } catch(Exception e){self.Fail(e);}
        }
        private void RestoreSound(object sound)
        {
            foreach(var field in sounds)
            {
                var block=field.GetValue(sound);var src=block==null ? null : source.GetValue(block) as AudioSource;
                if(!src || !volumes.TryGetValue(src,out var lease))continue;
                // Yield if another owner has changed the volume since our last write.
                if(Mathf.Approximately(src.volume,lease.Applied))src.volume=lease.Native;
                volumes.Remove(src);
            }
        }
        private static void AfterSound(object __instance)
        {
            var self=instance;if(!self || self.failed)return;
            try
            {
                if(!self.modEnabled.Value || !self.mix.Value || !self.Ready())return;
                for(int i=0;i<self.sounds.Length;i++)
                {
                    var block=self.sounds[i].GetValue(__instance);var src=block==null ? null : self.source.GetValue(block) as AudioSource;
                    if(!src)continue;
                    float scale=i>=7 ? self.reverserVolume.Value : self.engineVolume.Value;
                    if(Mathf.Approximately(scale,1))continue;
                    if(self.volumes.ContainsKey(src))continue;
                    var lease=new VolumeLease{Native=src.volume,Applied=Mathf.Clamp01(src.volume*scale)};
                    self.volumes[src]=lease;src.volume=lease.Applied;
                    // Never unmute, start or restart an engine source: towing owns engine muting.
                }
            }
            catch(Exception e){self.Fail(e);}
        }
        private void LateUpdate()
        {
            if(!failed)try{soundscape?.Tick(modEnabled.Value && Ready());}catch(Exception e){Fail(e);}
            if(!failed && !modEnabled.Value)foreach(var key in ownedWheels.Keys.ToArray())ReleaseWheels(key);
            foreach(var src in volumes.Keys.Where(s=>!s).ToArray())volumes.Remove(src);
            sprayEmitters.RemoveWhere(ps=>!ps);
            foreach(var key in touchdownGates.Keys.Where(c=>!c).ToArray())touchdownGates.Remove(key);
            foreach(var key in ownedWheels.Keys.Where(c=>!c).ToArray())ReleaseWheels(key);
            wheelScanned.RemoveWhere(c=>!c);
        }
        private void Fail(Exception e)
        { if(failed)return;failed=true;soundscape?.Dispose();soundscape=null;RestoreAll();log("Aircraft Effects stopped after runtime error: "+e); }
        private void RestoreAll()
        {
            foreach(var pair in volumes)if(pair.Key && Mathf.Approximately(pair.Key.volume,pair.Value.Applied))pair.Key.volume=pair.Value.Native;
            touchdownTimes.Clear();volumes.Clear();foreach(var ps in sprayEmitters)if(ps)SetRate(ps,0);sprayEmitters.Clear();
            foreach(var effect in ownedWheels.Keys.ToArray())ReleaseWheels(effect);
            wheelMaterials?.Dispose();
        }
        private void OnDestroy()
        {
            harmony?.UnpatchSelf();soundscape?.Dispose();soundscape=null;RestoreAll();foreach(var s in settings)s.Dispose();settings.Clear();if(instance==this)instance=null;
        }
    }
}
