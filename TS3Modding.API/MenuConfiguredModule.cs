using System;
using System.Collections.Generic;
using System.Linq;
namespace TS3Modding.API
{
    // Owns module replacement only outside an airport session. T must be an
    // immutable value snapshot so rollback never rereads changed configuration.
    public sealed class MenuConfiguredModule<T>:ITS3Module
    {
        public string Id {get;}
        private readonly Func<T> read;
        private readonly Func<T,ITS3Module[]> create;
        private readonly List<IDisposable> settings=new List<IDisposable>();
        private readonly string pluginId;private readonly string[][] keys;
        private IFrameworkContext context;private T applied,attempted;
        private ITS3Module[] children=new ITS3Module[0];
        public MenuConfiguredModule(string id,string pluginId,string[][] keys,Func<T> read,Func<T,ITS3Module[]> create)
        {Id=id;this.pluginId=pluginId;this.keys=keys;this.read=read;this.create=create;}
        public void Initialize(IFrameworkContext context)
        {
            this.context=context;applied=attempted=read();Start(applied);
            try{var registry=ModSettingsServices.Current as IModSettingsEditPolicy;if(registry!=null)foreach(var key in keys)settings.Add(registry.RegisterMenuSetting(pluginId,key[0],key[1]));}
            catch{Dispose();throw;}
        }
        private void Start(T snapshot)
        {
            var next=create(snapshot)??new ITS3Module[0];var started=new List<ITS3Module>();
            try{foreach(var module in next){started.Add(module);module.Initialize(context);}children=next;}
            catch{foreach(var module in started.AsEnumerable().Reverse())try{module.Dispose();}catch{}throw;}
        }
        private void Stop(){foreach(var module in Enumerable.Reverse(children))module.Dispose();children=new ITS3Module[0];}
        public void Tick()
        {
            var registry=ModSettingsServices.Current as IModSettingsEditPolicy;
            if(registry!=null&&!registry.IsAirportSessionActive)
            {
                var value=read();if(!EqualityComparer<T>.Default.Equals(value,attempted))
                {
                    attempted=value;
                    try{Stop();Start(value);applied=value;context.Log("Menu settings applied: "+pluginId);}
                    catch(Exception error){context.Log("Menu settings failed; restoring previous module options: "+error.Message);Start(applied);}
                }
            }
            foreach(var module in children)module.Tick();
        }
        public void Dispose(){Stop();foreach(var token in settings)token.Dispose();settings.Clear();}
    }
}
