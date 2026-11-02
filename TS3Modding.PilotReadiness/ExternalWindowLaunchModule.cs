using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TS3Modding.API;
using TS3Modding.DisplayBridge;

namespace TS3Modding.PilotReadiness
{
    internal sealed class ExternalWindowLaunchModule : ITS3Module
    {
        private static IFrameworkContext context;
        private readonly Harmony harmony=new Harmony("nl.ts3modding.pilotreadiness.externalwindow");
        public string Id=>"nl.ts3modding.pilotreadiness.externalwindow";
        public void Initialize(IFrameworkContext host)
        {
            var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp");
            var method=assembly.GetType("LegacyWindows",true).GetMethod("AddWindow",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            if(method==null||method.GetParameters().Length!=0) throw new NotSupportedException("External-window launch signature changed.");
            context=host;
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(ExternalWindowLaunchModule),nameof(Launch)));
        }
        private static bool Launch()
        {
            try { Process.Start(ExternalWindowLaunch.Create(Paths.GameRootPath)); context.Log("External window launched with isolated loader environment and game data directory."); }
            catch(Exception error) { context.Log("External-window launch failed: "+error); }
            return false;
        }
        public void Tick() { }
        public void Dispose() { harmony.UnpatchSelf(); context=null; }
    }
}
