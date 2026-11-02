using System;
using BepInEx;
using BepInEx.Configuration;
using TS3Modding.API;

namespace TS3Modding.Tugs
{
    [BepInPlugin("nl.ts3modding.tugs", "TS3 Tug Operations", "0.13.14")]
    [BepInDependency("nl.ts3modding.loader", "0.8.21")]
    [BepInDependency("nl.ts3modding.input", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var host = FrameworkServices.Current;
            if (host == null || host.GameAssemblySha256 != NativeTugModule.ReviewedHash)
            { Logger.LogError("Tug preview disabled: framework unavailable or unreviewed game assembly."); return; }
            var panel = Config.Bind("Display", "EnablePanel", true, "Allow the F9 tug status and return-to-stand panel.");
            var trace = Config.Bind("Diagnostics", "LogTransitions", true, "Record native tug assignments, coupling changes and flight transitions in the framework log.");
            var returns = Config.Bind("Operations", "EnableReturnToStand", true, "Allow local tug recall and return to the original stand after pushback, in single player.");
            var transfers=Config.Bind("Stand transfers","EnableStandTransfers",true,"Allow single-player native tug transfers between supported stands. Stop and obtain separate clearance at every runway crossing. Remote parking execution remains unsupported.");
            var voice=Config.Bind("Stand transfers","EnableTowVoiceCommands",true,"Accept addressed tow to stand, tow hold, tow continue, tow cancel, tow back to stand and stopped-tow cross runway commands through the native command/voice dispatch.");
            var dormant=Config.Bind("Parked aircraft","EnableDormantTowRequests",true,"Generate tow requests from dormant aircraft; approve each request through F9 or voice. Transfers and pending requests can be saved; transfers resume their previous movement/hold state.");
            var routePreview=Config.Bind("Display","ShowProposedTowRouteOnAdirs",true,"Show the selected aircraft proposed tow route on ADIRS before towing. Amber dashes are topology preview, cyan dashes are native checked. Applies immediately.");
            var aircraftLabel=Config.Bind("Display","ShowTowDestinationOnAircraftLabel",true,"Show the requested tow destination in a blue badge in the native 3D aircraft label runway field. Applies immediately.");
            var stripDestination=Config.Bind("Display","ShowTowDestinationOnStrip",true,"Show TOW and destination stand on the aircraft strip while requested or towing. Applies immediately.");
            var interval=Config.Bind("Parked aircraft","RequestIntervalMinutes",5f,new ConfigDescription("Minimum game minutes between automatic request attempts. Cannot exceed maximum. Applies immediately.",new AcceptableValueRange<float>(0,120)));
            var maxInterval=Config.Bind("Parked aircraft","MaximumRequestIntervalMinutes",Math.Max(interval.Value,10f),new ConfigDescription("Maximum game minutes between attempts. Random delay from minimum (RequestIntervalMinutes) to maximum. Equal values give fixed timing. Zero disables automatic requests. Applies immediately.",new AcceptableValueRange<float>(0,120)));
            Action normalize=()=>{maxInterval.Value=TowRequestCadence.Bound(maxInterval.Value);interval.Value=Math.Min(TowRequestCadence.Bound(interval.Value),maxInterval.Value);};
            normalize();interval.SettingChanged+=(sender,args)=>normalize();maxInterval.SettingChanged+=(sender,args)=>normalize();
            var maximum=Config.Bind("Parked aircraft","MaximumConcurrentTows",8,new ConfigDescription("Maximum simultaneous tow-only requests and maximum active transfers. Change from the main menu without restarting.",new AcceptableValueRange<int>(1,32)));
            try { host.RegisterModule(new MenuConfiguredModule<Tuple<bool,bool,bool,bool,bool,bool,int>>(
                "nl.ts3modding.tugs.configuration","nl.ts3modding.tugs",
                new[]{new[]{"Display","EnablePanel"},new[]{"Diagnostics","LogTransitions"},new[]{"Operations","EnableReturnToStand"},new[]{"Stand transfers","EnableStandTransfers"},new[]{"Stand transfers","EnableTowVoiceCommands"},new[]{"Parked aircraft","EnableDormantTowRequests"},new[]{"Parked aircraft","MaximumConcurrentTows"}},
                ()=>Tuple.Create(panel.Value,trace.Value,returns.Value,transfers.Value,voice.Value,dormant.Value,maximum.Value),
                value=>new ITS3Module[]{new NativeTugModule(value.Item1,value.Item2,value.Item3,value.Item4,value.Item5,value.Item6,()=>interval.Value,value.Item7,()=>stripDestination.Value,()=>maxInterval.Value,()=>routePreview.Value,()=>aircraftLabel.Value)})); }
            catch (Exception error) { Logger.LogError("Tug preview initialization failed: " + error); }
        }
    }
}








