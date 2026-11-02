using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using TS3Modding.API;
using TS3Modding.Adapters;

namespace TS3Modding.PilotReadiness
{
    [BepInPlugin("nl.ts3modding.pilotreadiness", "TS3 Pilot Reports", "0.5.2")]
    [BepInDependency("nl.ts3modding.loader", "0.8.4")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var host = FrameworkServices.Current;
            if (host == null || host.GameAssemblySha256 != "26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7")
            { Logger.LogError("Pilot reports disabled: unreviewed game assembly."); return; }
            try { host.RegisterModule(new ExternalWindowLaunchModule()); }
            catch (Exception error) { Logger.LogError("External-window launch initialization failed: " + error); }
            var legacyReports = Config.Bind("Reports", "ReportHoldingPoints", true, "Legacy default for migration only; use the three individual holding-report options.").Value;
            var assigned=Config.Bind("Reports","ReportAssignedRunway",legacyReports,"Report holding short of the assigned departure runway. Change from the main menu without restarting.");
            var crossing=Config.Bind("Reports","ReportRunwayCrossings",legacyReports,"Report holding short of other runways. Change from the main menu without restarting.");
            var taxiway=Config.Bind("Reports","ReportTaxiways",legacyReports,"Report taxiway holds. Change from the main menu without restarting.");
            var dashes=Config.Bind("Display","DashedRoutesBeyondHold",true,"Draw ADIRS routes dashed beyond the next hold. Change from the main menu without restarting.");
            try{host.RegisterModule(new MenuConfiguredModule<Tuple<bool,bool,bool,bool>>(
                "nl.ts3modding.pilotreadiness.configuration","nl.ts3modding.pilotreadiness",
                new[]{new[]{"Reports","ReportAssignedRunway"},new[]{"Reports","ReportRunwayCrossings"},new[]{"Reports","ReportTaxiways"},new[]{"Display","DashedRoutesBeyondHold"}},
                ()=>Tuple.Create(assigned.Value,crossing.Value,taxiway.Value,dashes.Value),
                value=>{
                    var modules=new List<ITS3Module>{new CrossingPreferencesModule(value.Item2)};var options=new HoldingReportOptions(value.Item1,value.Item2,value.Item3);
                    if(options.Any)modules.Add(new HoldingReportsModule(options));if(value.Item4)modules.Add(new AdirsDashModule());return modules.ToArray();
                }));}
            catch(Exception error){Logger.LogError("Pilot reports configuration failed: "+error);}

        }
        private sealed class CrossingPreferencesModule:ITS3Module
        {
            private readonly bool enabled;private IDisposable token;
            internal CrossingPreferencesModule(bool enabled){this.enabled=enabled;}
            public string Id=>"nl.ts3modding.pilotreadiness.crossingpreferences";
            public void Initialize(IFrameworkContext context){token=PilotReportPreferences.RegisterCrossingReports(()=>enabled);}
            public void Tick(){}
            public void Dispose(){token?.Dispose();token=null;}
        }

    }

}




