using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using TS3Modding.API;

namespace TS3Modding.FlightStrips
{
    [BepInPlugin("nl.ts3modding.strips", "TS3 Automatic Flight Strips", "0.6.6")]
    [BepInDependency("nl.ts3modding.loader", "0.8.19")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private IDisposable registration;
        private void Awake()
        {
            var host = FrameworkServices.Current;
            var commands = host as IStripMovementService;
            var archival = host as IFlightArchivalService;
            if (host == null || commands == null) { Logger.LogError("Framework strip service unavailable."); return; }
            var legacyPath = Path.Combine(Paths.ConfigPath, "nl.ts3modding.loader.cfg");
            var legacy = new ConfigFile(legacyPath, false) { SaveOnConfigSet = false };
            var enabled = Config.Bind("FlightStrips", "AutomaticMoves", legacy.Bind("FlightStrips", "AutomaticMoves", true).Value, "Automatically advance matched strips; false gives preview only.");
            var arrival = Config.Bind("FlightStrips", "ArrivalGroundBlock", legacy.Bind("FlightStrips", "ArrivalGroundBlock", "GND").Value, "Unique arrival ground bay header; empty disables.");
            var departure = Config.Bind("FlightStrips", "DepartureAirborneBlock", legacy.Bind("FlightStrips", "DepartureAirborneBlock", "DEP").Value, "Unique airborne departure bay header; empty disables.");
            var runway = Config.Bind("FlightStrips", "RunwayAssignmentMoves", true, "Advance along the current block's configured next links when an assigned runway uniquely matches.");
            var archive = Config.Bind("FlightStrips", "ArchiveCompletedFlights", true, "Bin strips after recorded parking, an accepted airborne contact-departure command, or the native missed-departure automatic handoff.");
            registration = host.RegisterModule(new StripModule(commands, archival, enabled.Value, runway.Value, archive.Value, arrival.Value, departure.Value));
            Logger.LogInfo("Separate strip mod ready. Automatic=" + enabled.Value + "; runway routing=" + runway.Value);
        }
        private void OnApplicationQuit() { registration?.Dispose(); }
        private void OnDestroy() { Logger.LogInfo("Strip plugin wrapper destroyed; module lifetime is owned by the framework."); }
        private sealed class StripModule : ITS3Module
        {
            private readonly IStripMovementService commands;
            private readonly IFlightArchivalService archival;
            private readonly bool archiveCompleted;
            private readonly StripOperationGuard operations=new StripOperationGuard();
            private readonly StripMoveTracker tracker = new StripMoveTracker();
            private readonly bool runwayMoves;
            private readonly string arrival, departure;
            private readonly string directory = Path.Combine(Paths.BepInExRootPath, "TS3ModLoader");
            private bool enabled;
            private long moved;
            private long archived;
            private IFrameworkContext context;
            public StripModule(IStripMovementService commands, IFlightArchivalService archival, bool enabled, bool runwayMoves, bool archiveCompleted, string arrival, string departure)
            { this.commands = commands; this.archival = archival; this.archiveCompleted = archiveCompleted; this.enabled = enabled; this.runwayMoves = runwayMoves; this.arrival = arrival; this.departure = departure; }
            public string Id => "nl.ts3modding.strips.module";
            public void Initialize(IFrameworkContext context)
            {
                this.context = context; Directory.CreateDirectory(directory);
                context.Events.Subscribe<AirportObserved>(e => Observe(e.Observation));
                if(TS3Modding.Preferences.DeveloperMode.Enabled)Write("strip-mod-status.txt", "UTC=" + DateTime.UtcNow.ToString("O") + "\nVersion=0.6.6\nState=AwaitingObservation\n");
            }
            private void Observe(AirportObservation observation)
            {
                var planned = FlightStripPlanner.Plan(observation);
                var diagnostics=TS3Modding.Preferences.DeveloperMode.Enabled;
                var rows = diagnostics?new StringBuilder("ObservedUtc,Callsign,CurrentBlock,AssignedRunway,ProposedStage,TargetBlock,Mode,CompletionEvidence\n"):null;
                operations.Retain(observation.Strips.Select(strip=>strip.InstanceId));
                tracker.BeginScan();
                var movedThisScan = false;
                try
                {
                    foreach (var rec in planned)
                    {
                        operations.Run(rec.Strip.InstanceId,()=>{
                        var plane = rec.Aircraft.HasValue ? observation.Aircraft.Single(a => a.Id.Equals(rec.Aircraft.Value)) : null;
                        var proof = archiveCompleted && archival != null ? CompletionPolicy.Match(rec.Strip, observation) : null;
                        var target = plane == null ? null : StripMovementPolicy.Target(rec, plane, arrival, departure);
                        var phase = rec.Strip.Departure ? "departure" : "arrival";
                        var revision=plane==null?0:tracker.AssignmentRevision(StripBoardPolicy.OperationKey(plane.Id,rec.Strip,phase),plane.Details.AssignedRunway);
                        if (target == null && runwayMoves && plane != null)
                        { target = StripMovementPolicy.RunwayTarget(rec, plane); phase += ":runway:" + target+":"+revision; }
                        if (proof != null) { target = "BIN"; phase = "archive"; }
                        if(diagnostics)Row(rows, observation.CapturedUtc.ToString("O"), rec.Strip.Callsign, rec.Strip.Block, plane?.Details.AssignedRunway ?? rec.Strip.AssignedRunway, rec.Stage, target, enabled ? "Automatic" : "PreviewOnly", proof?.Kind.ToString());
                        if (!enabled || movedThisScan || target == null || (plane == null && proof == null) ||
                            !StripBoardPolicy.UniqueWithinBoard(rec.Strip,observation.Strips)) return;
                        var key = StripBoardPolicy.OperationKey(proof != null ? proof.Aircraft : plane.Id,rec.Strip,phase);
                        var fingerprint = rec.Strip.InstanceId + "|" + rec.Strip.Block + "|" + target;
                        if (!tracker.Ready(key, fingerprint)) return;
                        var committed = proof != null ? archival.TryArchiveStrip(rec.Strip, proof) : commands.TryMoveStrip(rec.Strip, target);
                        if (!committed) return;
                        tracker.Complete(key); moved++; movedThisScan = true;
                        if (proof != null) archived++;
                        context.Log("Strip mod moved " + rec.Strip.Callsign + " -> " + target);
                        },error=>{movedThisScan=true;context.Log("Strip automation held for "+rec.Strip.Callsign+" (strip "+rec.Strip.InstanceId+"): "+error+". Other strips remain active.");});
                    }
                }

                finally { tracker.EndScan(); }
                if(!diagnostics)return;
                Write("strip-preview.csv", rows.ToString());
                Write("strip-mod-status.txt", "UTC=" + DateTime.UtcNow.ToString("O") + "\nVersion=0.6.6\nState=Running\nAutomaticStripMoves=" + enabled + "\nRunwayAssignmentMoves=" + runwayMoves + "\nArchiveCompletedFlights=" + archiveCompleted + "\nStripsArchived=" + archived + "\nStripsMoved=" + moved + "\n");
            }
            private void Write(string name, string content)
            {
                var path = Path.Combine(directory, name); File.WriteAllText(path + ".tmp", content);
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null); else File.Move(path + ".tmp", path);
            }
            private static void Row(StringBuilder target, params object[] fields) => target.AppendLine(string.Join(",", fields.Select(value =>
            {
                var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                if (text.Length > 0 && "=+-@".IndexOf(text[0]) >= 0) text = "'" + text;
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            })));
            public void Tick() { }
            public void Dispose() { }
        }
    }
}



