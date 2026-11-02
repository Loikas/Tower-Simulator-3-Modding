using System;
using System.Collections.Generic;
using System.Linq;
namespace TS3Modding.API
{
    // Optional interface: old live/edit-policy consumers remain binary compatible.
    public interface IModSettingsVisibility
    {
        IDisposable RequireModsForSetting(string pluginId,string section,string key,string[] requiredModIds,Func<bool> available=null);
        IDisposable RequireModsForSection(string pluginId,string section,string[] requiredModIds,Func<bool> available=null);
        bool IsVisible(string pluginId,string section,string key);
    }
    public sealed partial class ModSettingsRegistry
    {
        private readonly Func<string,bool> modLoaded;
        private sealed class VisibilityRule {internal string[] Mods;internal Func<bool> Available;}
        private readonly Dictionary<Tuple<string,string,string>,VisibilityRule> visibility=new Dictionary<Tuple<string,string,string>,VisibilityRule>();
        public IDisposable RequireModsForSetting(string pluginId,string section,string key,string[] requiredModIds,Func<bool> available=null)
        {if(string.IsNullOrWhiteSpace(key))throw new ArgumentException("Setting key is required.");return RequireMods(pluginId,section,key,requiredModIds,available);}
        public IDisposable RequireModsForSection(string pluginId,string section,string[] requiredModIds,Func<bool> available=null)=>RequireMods(pluginId,section,null,requiredModIds,available);
        private IDisposable RequireMods(string pluginId,string section,string key,string[] mods,Func<bool> available)
        {
            if(string.IsNullOrWhiteSpace(pluginId)||string.IsNullOrWhiteSpace(section)||mods==null||mods.Length==0||mods.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("Owner, section and at least one required mod ID are required.");
            var id=Tuple.Create(pluginId,section,key);if(visibility.ContainsKey(id))throw new InvalidOperationException("Visibility rule already registered.");
            var rule=new VisibilityRule{Mods=mods.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),Available=available};visibility.Add(id,rule);
            return new Token(()=>{VisibilityRule active;if(visibility.TryGetValue(id,out active)&&ReferenceEquals(active,rule))visibility.Remove(id);});
        }
        private bool VisibleRule(Tuple<string,string,string> id)
        {
            VisibilityRule rule;if(!visibility.TryGetValue(id,out rule))return true;
            try{return rule.Mods.All(modLoaded)&&(rule.Available==null||rule.Available());}catch{return false;}
        }
        public bool IsVisible(string pluginId,string section,string key)=>VisibleRule(Tuple.Create(pluginId,section,(string)null))&&(key==null||VisibleRule(Tuple.Create(pluginId,section,key)));
    }
}
