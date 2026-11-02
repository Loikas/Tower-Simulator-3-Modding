using System;
using System.Collections.Generic;

namespace TS3Modding.API
{
    public interface IModSettingsRegistry
    {
        // Opt in only when this config entry is read/applied during play. Unknown entries stay read-only.
        IDisposable RegisterLiveSetting(string pluginId,string section,string key);
        bool CanEditLive(string pluginId,string section,string key);
    }
    public enum ModSettingChangeMode { RestartRequired, MainMenuOnly, Live }
    // Separate interface preserves the original live-settings contract for existing mods.
    public interface IModSettingsEditPolicy
    {
        bool IsAirportSessionActive { get; }
        IDisposable RegisterMenuSetting(string pluginId,string section,string key);
        ModSettingChangeMode ChangeMode(string pluginId,string section,string key);
        bool CanEdit(string pluginId,string section,string key);
    }
    public static class ModSettingsServices
    {
        public static IModSettingsRegistry Current {get;private set;}
        public static void Attach(IModSettingsRegistry registry)
        {if(Current!=null)throw new InvalidOperationException("A mod settings provider is already attached.");Current=registry??throw new ArgumentNullException(nameof(registry));}
        public static void Detach(IModSettingsRegistry registry){if(ReferenceEquals(Current,registry))Current=null;}
    }
    public sealed partial class ModSettingsRegistry:IModSettingsRegistry,IModSettingsEditPolicy,IModSettingsVisibility
    {
        private readonly Func<bool> sessionActive;
        public ModSettingsRegistry(Func<bool> sessionActive=null):this(sessionActive,null){}
        public ModSettingsRegistry(Func<bool> sessionActive,Func<string,bool> modLoaded){this.sessionActive=sessionActive??(()=>false);this.modLoaded=modLoaded??(_=>false);}
        public bool IsAirportSessionActive=>sessionActive();
        private readonly Dictionary<Tuple<string,string,string>,object> menu=new Dictionary<Tuple<string,string,string>,object>();
        public IDisposable RegisterMenuSetting(string pluginId,string section,string key)=>Register(pluginId,section,key,false);
        public ModSettingChangeMode ChangeMode(string pluginId,string section,string key)=>CanEditLive(pluginId,section,key)?ModSettingChangeMode.Live:menu.ContainsKey(Tuple.Create(pluginId,section,key))?ModSettingChangeMode.MainMenuOnly:ModSettingChangeMode.RestartRequired;
        public bool CanEdit(string pluginId,string section,string key){var mode=ChangeMode(pluginId,section,key);return IsVisible(pluginId,section,key)&&(mode==ModSettingChangeMode.Live||mode==ModSettingChangeMode.MainMenuOnly&&!IsAirportSessionActive);}
        private readonly Dictionary<Tuple<string,string,string>,object> live=new Dictionary<Tuple<string,string,string>,object>();
        public IDisposable RegisterLiveSetting(string pluginId,string section,string key)
            =>Register(pluginId,section,key,true);
        private IDisposable Register(string pluginId,string section,string key,bool immediate)
        {
            if(string.IsNullOrWhiteSpace(pluginId)||string.IsNullOrWhiteSpace(section)||string.IsNullOrWhiteSpace(key))throw new ArgumentException("Plugin, section and key are required.");
            var id=Tuple.Create(pluginId,section,key);if(live.ContainsKey(id)||menu.ContainsKey(id))throw new InvalidOperationException("Setting already registered.");
            var entries=immediate?live:menu;
            var owner=new object();entries.Add(id,owner);return new Token(()=>{object current;if(entries.TryGetValue(id,out current)&&ReferenceEquals(current,owner))entries.Remove(id);});
        }
        public bool CanEditLive(string pluginId,string section,string key)=>live.ContainsKey(Tuple.Create(pluginId,section,key));
        private sealed class Token:IDisposable
        {private Action release;public Token(Action release){this.release=release;}public void Dispose(){var action=release;release=null;action?.Invoke();}}
    }
}
