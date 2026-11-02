using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using TS3Modding.API;
using UnityEngine;
using TS3Modding.DisplayBridge;

namespace TS3Modding.Tugs
{
    internal sealed partial class NativeTugModule : ITS3Module
    {
        public const string ReviewedHash = "26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly bool panelEnabled, logTransitions, returnsEnabled;
        private NativeReturnController returns;
        private NativeTowScore towScore;
        private NativeTowPlanning towPlanning;
        private NativeTowRouteCheck routeCheck;
        private NativeTowTransfer transfer;private TowCommandRegistration voice;private IDisposable intervalSetting,maximumIntervalSetting,stripSetting;
        private RecoveryTugApproach recoveryApproach;
        private TowStandDisplay standDisplay;private TowStripDisplay stripDisplay;private TowStandMarker[] standMarkers=new TowStandMarker[0];private string standPath;private float nextStandPublish;private bool standPublishFailed;
        private readonly bool transfersEnabled,voiceEnabled;
        private int pendingTransfer;private Component pendingAircraft;private string pendingCrossRunway;
        private string transferFailure;
        internal bool TransferBusy=>transfer?.Busy==true||pendingTransfer!=0;
        internal string TransferStatus=>transferFailure??(pendingTransfer!=0?routeCheck.Message:transfer?.Message??"Stand transfers disabled.");
        internal string RouteCheckStatus=>routeCheck?.Message??"Native route check unavailable.";
        internal bool RouteCheckBusy=>routeCheck?.Busy??false;
        private readonly Dictionary<int, Component> trucks = new Dictionary<int, Component>();
        private readonly TugObservationTracker tracker = new TugObservationTracker();
        private DormantTowStripCleanup stripCleanup;private NativeTowOnly towOnly;private readonly bool dormantEnabled;private double nextDormantRequest,dormantClockMinutes,lastDormantDay;private object dormantGame;
        private readonly Func<bool> showAircraftLabel;private IDisposable aircraftLabelSetting;private TowAircraftLabelDisplay aircraftLabels;
        private readonly Func<bool> showTowRoute;private IDisposable routeSetting,holdingReportOwner;
        private TowRouteDisplay routeDisplay;private TowRoutePreview[] routePreviews=new TowRoutePreview[0];private string routePath;private float nextRoutePublish;private object lastCheckedProposal;
        private sealed class ProposedRoute{internal object Game,Schedule;internal string Destination,Callsign;internal bool Native,RequiresRequest;internal TowPoint[] Points;}
        private readonly Dictionary<Component,ProposedRoute> proposedRoutes=new Dictionary<Component,ProposedRoute>();
        private readonly Func<float> requestInterval,maximumRequestInterval;private readonly System.Random intervalRandom=new System.Random();private readonly Func<bool> showStripDestination;private readonly int maxTows;
        private float MaximumInterval=>TowRequestCadence.Bound(maximumRequestInterval?.Invoke()??10);
        private float IntervalMinutes=>Math.Min(TowRequestCadence.Bound(requestInterval?.Invoke()??5),MaximumInterval);
        private void ScheduleRequest()=>nextDormantRequest=dormantClockMinutes+TowRequestCadence.Delay(IntervalMinutes,MaximumInterval,intervalRandom.NextDouble());
        private float lastInterval=-1,lastMaximum=-1;
        internal Component[] DormantAircraft=>(towOnly?.AircraftList??new Component[0]).Concat(recoveries.Keys).Distinct().ToArray();
        internal Component[] TowingAircraft=>transfer?.AircraftList??new Component[0];
        internal string TowDestinationLabel(Component p)=>transfer?.DestinationAliasFor(p)??RecoveryAlias(p)??towOnly?.DestinationAliasFor(p);
        internal string TowDestination(Component p)=>transfer?.DestinationFor(p)??RecoveryDestination(p)??towOnly?.DestinationFor(p);
        internal string TowCallsign(Component p)=>p?(string)planeName.GetValue(p):"";
        internal string TowRunwayStatus(Component p)=>recoveryApproach?.Status(p)??transfer?.RunwayStatus(p);
        internal string WaitingRunway(Component p)=>recoveryApproach?.WaitingRunway(p)??transfer?.WaitingRunway(p);
        internal void ClearTowRunway(Component p,string runway){try{if(recoveryApproach?.Clear(p,runway)!=true)transfer.ClearRunway(p,runway);}catch(Exception error){Reply(p,"Unable, "+error.Message);}}
        internal bool IsTowing(Component p)=>transfer?.Owns(p)==true;
        internal bool NeedsTowRecovery(Component p)=>transfer?.NeedsRecovery(p)==true||recoveryApproach?.NeedsRecovery(p)==true;
        internal void RecoverTow(Component p){try{if(recoveryApproach?.Recover(p)!=true)transfer.Recover(p);}catch(Exception e){Reply(p,"Unable to recover tow, "+e.Message);}}
        internal void HoldTransfer(Component p,bool held)=>transfer?.Hold(p,held);
        private readonly HashSet<int> dormantRequested=new HashSet<int>();
        internal string DormantRequestStatus=>recoveries.Count>0?recoveries.Count+" rejected-takeoff recoveries; "+(towOnly?.AircraftList.Length??0)+" parked-aircraft tows.":towOnly?.Active==true?towOnly.AircraftList.Length+" tow-only aircraft active.":"No tow-only request pending.";
        internal bool CanGenerateParkedRequest=>dormantEnabled&&towOnly!=null;
        internal bool DormantRequestsEnabled=>dormantEnabled&&towOnly!=null||recoveries.Count>0;
        internal bool HasDormantRequest=>towOnly?.Active==true;
        private NativeParkedInventory parkedInventory;
        internal string ParkedInventoryStatus=>parkedInventory?.Summary??"Parked-aircraft inventory unavailable.";
        private Type truckType;
        private FieldInfo terminal, plane, linked, home, sleeping, roadName, planeName, state, deleted, waiting, atStand,
            timetable, outgoing, scheduleName, instance;
        private IFrameworkContext context;
        private TugPanel panel;
        private float nextScan;
        private int gameId;
        private bool scanFailed;
        internal TugObservation[] Rows { get; private set; } = new TugObservation[0];
        internal string Status { get; private set; } = "Waiting for an airport session.";
        internal string OperationStatus => pendingTransfer!=0?routeCheck.Message:transfer?.Busy==true?transfer.Message:returns?.Message ?? "Return controller unavailable.";
        public string Id => "nl.ts3modding.tugs.observation";
        public NativeTugModule(bool panelEnabled, bool logTransitions, bool returnsEnabled,bool transfersEnabled=true,bool voiceEnabled=true,bool dormantEnabled=true,Func<float> requestInterval=null,int maxTows=8,Func<bool> showStripDestination=null,Func<float> maximumRequestInterval=null,Func<bool> showTowRoute=null,Func<bool> showAircraftLabel=null)
        { this.showAircraftLabel=showAircraftLabel;this.showTowRoute=showTowRoute;this.panelEnabled = panelEnabled; this.logTransitions = logTransitions; this.returnsEnabled = returnsEnabled;this.transfersEnabled=transfersEnabled;this.voiceEnabled=voiceEnabled;this.dormantEnabled=dormantEnabled;this.requestInterval=requestInterval;this.maximumRequestInterval=maximumRequestInterval;this.maxTows=maxTows;this.showStripDestination=showStripDestination; }
        public void Initialize(IFrameworkContext context)
        {
            this.context = context;
            if(ModSettingsServices.Current!=null)aircraftLabelSetting=ModSettingsServices.Current.RegisterLiveSetting("nl.ts3modding.tugs","Display","ShowTowDestinationOnAircraftLabel");
            holdingReportOwner=HoldingReportOwnership.Register(p=>p is Component&&((transfer?.Owns((Component)p)==true)||(towOnly?.Owns((Component)p)==true)||recoveries.ContainsKey((Component)p)));
            if(ModSettingsServices.Current!=null)routeSetting=ModSettingsServices.Current.RegisterLiveSetting("nl.ts3modding.tugs","Display","ShowProposedTowRouteOnAdirs");
            if(ModSettingsServices.Current!=null)stripSetting=ModSettingsServices.Current.RegisterLiveSetting("nl.ts3modding.tugs","Display","ShowTowDestinationOnStrip");
            if(ModSettingsServices.Current!=null)intervalSetting=ModSettingsServices.Current.RegisterLiveSetting("nl.ts3modding.tugs","Parked aircraft","RequestIntervalMinutes");
            if(ModSettingsServices.Current!=null)maximumIntervalSetting=ModSettingsServices.Current.RegisterLiveSetting("nl.ts3modding.tugs","Parked aircraft","MaximumRequestIntervalMinutes");
            if (context.GameAssemblySha256 != ReviewedHash) throw new NotSupportedException("Unreviewed tug profile.");
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
            try{parkedInventory=new NativeParkedInventory(assembly);}catch(Exception error){context.Log("[Tugs] Parked inventory unavailable: "+error.Message);}
            truckType = assembly.GetType("TowTruck", true);
            var aircraft = assembly.GetType("Airplane", true);
            var road = assembly.GetType("Road", true);
            terminal = Required(truckType, "_terminal", road); plane = Required(truckType, "_plane", aircraft);
            linked = Required(truckType, "_car_linked", typeof(bool)); home = Required(truckType, "home", typeof(bool));
            sleeping = Required(truckType, "sleep", typeof(bool)); roadName = Required(road, "name", typeof(string));
            planeName = Required(aircraft, "_name", typeof(string)); state = Required(aircraft, "_state");
            deleted = Required(aircraft, "_deleted", typeof(bool)); waiting = Required(aircraft, "wait_for_trailer", typeof(bool));
            atStand = Required(aircraft, "_imatterminal", typeof(bool)); timetable = Required(aircraft, "_timetable");
            outgoing = Required(timetable.FieldType, "outgoing", timetable.FieldType);
            scheduleName = Required(timetable.FieldType, "plane", typeof(string));
            instance = assembly.GetType("Game", true).GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (instance == null || !typeof(Component).IsAssignableFrom(truckType)) throw new NotSupportedException("Tug profile changed.");
            returns = new NativeReturnController(assembly, context, returnsEnabled);
            try{towScore=new NativeTowScore(assembly,context.Log);returns.SuccessfulReturn=towScore.Award;}
            catch(Exception error){context.Log("[Tugs] Tow rewards unavailable: "+error.Message);}
            try{towPlanning=new NativeTowPlanning(assembly);routeCheck=new NativeTowRouteCheck(assembly,context.Log);routeCheck.CaptureFailure=CaptureTowFailure;}catch(Exception error){context.Log("[Tugs] Tow planning unavailable: "+error.Message);}
            if(transfersEnabled&&routeCheck!=null)
            {
                transfer=new NativeTowTransfer(assembly,context.Log,Reply);transfer.SuccessfulTow=p=>towScore?.Award(p);transfer.CaptureFailure=CaptureTowFailure;transfer.MaximumTransfers=maxTows;
                recoveryApproach=new RecoveryTugApproach(assembly,transfer.Owns,transfer.IsHeld,Reply,context.Log);transfer.ExternalWaiting=recoveryApproach.Waiting;
                stripCleanup=DormantTowStripCleanup.FromHost(FrameworkServices.Current,context.Log);
                transfer.MaintenanceParked=p=>stripCleanup.Queue(p,Required(p.GetType(),"_timetable").GetValue(p));
                towOnly=new NativeTowOnly(assembly,context.Log,Reply);towOnly.RetiredStrip=stripCleanup.Queue;towOnly.MaximumRequests=maxTows;towOnly.IsTransferAircraft=p=>transfer.Owns(p)||ReferenceEquals(pendingAircraft,p);transfer.IsTowOnly=towOnly.Owns;transfer.TowOnlyParked=p=>towOnly.Restore(p);transfer.AllowSave=towOnly.PermitSave;transfer.AllowCommand=towOnly.PermitCommand;
                transfer.BeforeSave=()=>{if(pendingTransfer!=0||routeCheck.Busy)throw new InvalidOperationException("Wait for the initial tow route check before saving.");};transfer.ExportRequests=towOnly.ExportState;transfer.ImportRequest=towOnly.ImportState;transfer.ForgetRequests=()=>{towOnly.ForgetForLoad();ForgetRecoveriesForLoad();};transfer.RecoveryCompletion=RestoredRecoveryCompletion;transfer.ExportApproach=recoveryApproach.Export;transfer.ImportApproach=recoveryApproach.Restore;
                transfer.BeforeCommand=aircraft=>{if(ReferenceEquals(pendingAircraft,aircraft))CancelTransfer();};
                recoveryRegistration=TowRecoveryServices.Register(RegisterRecovery);
                var oldBeforeSave=transfer.BeforeSave;transfer.BeforeSave=()=>{if(recoveries.Values.Any(r=>r.Planning))throw new InvalidOperationException("Wait for the recovery route calculation before saving.");oldBeforeSave?.Invoke();};
                try{standPath=TowStandTelemetry.PathForGame(BepInEx.Paths.GameRootPath);standDisplay=new TowStandDisplay(assembly,()=>standMarkers,context.Log);stripDisplay=new TowStripDisplay(assembly,()=>standMarkers);}catch(Exception error){context.Log("[Tugs] Stand display unavailable: "+error.Message);}
            }
            routePath=TowRouteTelemetry.PathForGame(BepInEx.Paths.GameRootPath);try{routeDisplay=new TowRouteDisplay(assembly,()=>routePreviews,context.Log);}catch(Exception error){context.Log("[Tugs] Route preview display unavailable: "+error.Message);}
            aircraftLabels=new TowAircraftLabelDisplay(assembly,p=>(showAircraftLabel?.Invoke()??true)&&!(bool)deleted.GetValue(p)&&(bool)Required(p.GetType(),"_active").GetValue(p)?TowDestinationLabel(p):null,context.Log);
            if(voiceEnabled)voice=new TowCommandRegistration(assembly,VoiceCommand,Reply,ResolveTowSpeech,context.Log);
            if (panelEnabled)
            {
                // The game can disable BepInEx plugin components; keep the panel on its own object, like the framework driver.
                var obj = new GameObject("TS3Modding.TugPanel");
                obj.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(obj);
                panel = obj.AddComponent<TugPanel>(); panel.Module = this;
            }
            context.Log("Tug Operations "+typeof(NativeTugModule).Assembly.GetName().Version.ToString(3)+" ready. F9 panel=" + panelEnabled + "; local return-to-stand=" + returnsEnabled);
        }
        private static FieldInfo Required(Type type, string name, Type expected = null)
        {
            var field = type.GetField(name, Flags);
            if (field == null || (expected != null && field.FieldType != expected)) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        public void Tick()
        {
            var airportNow=instance.GetValue(null);towOnly?.ValidateSession(airportNow);
            TickRecoveries();
            if(!ReferenceEquals(dormantGame,airportNow)){dormantGame=airportNow;dormantRequested.Clear();dormantClockMinutes=0;lastDormantDay=airportNow==null?0:(float)Required(airportNow.GetType(),"daytime").GetValue(airportNow);ScheduleRequest();}
            if(airportNow!=null){var day=(double)(float)Required(airportNow.GetType(),"daytime").GetValue(airportNow);var delta=day-lastDormantDay;lastDormantDay=day;if(delta<-.5)delta+=1;if(delta>0)dormantClockMinutes+=delta*1440;}
            var intervalNow=IntervalMinutes;var maximumNow=MaximumInterval;if(lastInterval!=intervalNow||lastMaximum!=maximumNow)ScheduleRequest();lastInterval=intervalNow;lastMaximum=maximumNow;
            returns?.Tick();
            voice?.PublishHoldingAircraft(TowingAircraft.Concat(DormantAircraft).Concat(pendingAircraft?new[]{pendingAircraft}:new Component[0]).Distinct().ToArray());voice?.Tick();transfer?.Tick();
            towOnly?.ExpireWaiting();stripCleanup?.Tick();
            PublishStandMarker(airportNow);
            routeCheck?.Tick();
            if(routeCheck?.Completed!=null&&!ReferenceEquals(lastCheckedProposal,routeCheck.Completed))
            {var complete=routeCheck.Completed;lastCheckedProposal=complete;RememberProposal(complete.Session,complete.Destination,complete.Points.Select(p=>new TowPoint(p.x,p.y,p.z)).ToArray(),true);}
            PublishProposals(airportNow);
            if(pendingTransfer!=0&&!routeCheck.Busy)
            {
                var id=pendingTransfer;pendingTransfer=0;var aircraft=pendingAircraft;pendingAircraft=null;var crossRunway=pendingCrossRunway;pendingCrossRunway=null;
                try
                {
                    var checkedRoute=routeCheck.Completed;
                    if(checkedRoute==null)throw new InvalidOperationException(routeCheck.Message);
                    checkedRoute.CrossRunway=crossRunway;
                    Component truck;if(!trucks.TryGetValue(id,out truck)||!truck)throw new InvalidOperationException("Tug is no longer available.");
                    transfer.Start(truck,checkedRoute);
                }
                catch(Exception error)
                {
                    transferFailure="Transfer refused: "+error.Message;context.Log("[Tugs] Transfer refused: "+error);
                    var retained=aircraft&&towOnly?.RetainAfterRouteFailure(aircraft)==true;
                    if(aircraft)Reply(aircraft,retained?"Unable to calculate tow route, revise taxiways, tow request remains active":"Unable, "+error.Message);
                }
            }
            if (Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 1f;
            try
            {
                var game = instance.GetValue(null) as UnityEngine.Object;
                try{if(TS3Modding.Preferences.DeveloperMode.Enabled)parkedInventory?.Observe(game,logTransitions?context.Log:(Action<string>)(_=>{}));}catch(Exception error){context.Log("[Tugs] Parked inventory scan failed: "+error.Message);parkedInventory=null;}
                var currentGameId = game ? game.GetInstanceID() : 0;
                if (currentGameId != gameId) { returns?.Cancel("Airport session changed"); tracker.Reset(); trucks.Clear(); gameId = currentGameId; Rows = new TugObservation[0]; }
                if (!game) { Rows = new TugObservation[0]; Status = "Waiting for an airport session."; return; }
                var fresh = new List<TugObservation>(); trucks.Clear();
                using(returns.ObserveAssociations())
                foreach (var obj in UnityEngine.Object.FindObjectsOfType(truckType))
                {
                    var truck = obj as Component;
                    if (!truck || !truck.gameObject.scene.IsValid() || !truck.gameObject.activeInHierarchy) continue;
                    var aircraft = plane.GetValue(truck) as Component;
                    var inferred=false;
                    if(!aircraft){aircraft=returns.AssociatedAircraft(truck);inferred=aircraft;}
                    var stand = terminal.GetValue(truck);
                    var schedule = aircraft ? timetable.GetValue(aircraft) : null;
                    var next = schedule == null ? null : outgoing.GetValue(schedule);
                    var row = new TugObservation {
                        TugId = truck.GetInstanceID(), Gate = stand == null ? "-" : (string)roadName.GetValue(stand),
                        AircraftId = aircraft ? aircraft.GetInstanceID() : 0,
                        StandAssociation=inferred,
                        Callsign = aircraft ? (string)planeName.GetValue(aircraft) : "-",
                        AircraftState = aircraft ? state.GetValue(aircraft).ToString() : "-",
                        OutgoingCallsign = next == null ? "-" : (string)scheduleName.GetValue(next),
                        Linked = (bool)linked.GetValue(truck), Home = (bool)home.GetValue(truck), Sleeping = (bool)sleeping.GetValue(truck),
                        AircraftDeleted = aircraft && (bool)deleted.GetValue(aircraft),
                        WaitingForTug = aircraft && (bool)waiting.GetValue(aircraft), AtStand = aircraft && (bool)atStand.GetValue(aircraft)
                    };
                    returns.Describe(truck, row);if(TransferBusy){row.CanReturn=false;row.ReturnReason="Finish or cancel the stand transfer first.";}fresh.Add(row); trucks.Add(row.TugId, truck);
                }

                Rows = fresh.OrderBy(row => row.Gate, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.TugId).ToArray();
                if (logTransitions&&TS3Modding.Preferences.DeveloperMode.Enabled) {foreach (var change in tracker.Observe(fresh)) context.Log("[Tugs] " + change);}else tracker.Reset();
                Status = Rows.Length + " native tugs observed; " + Rows.Count(row => row.Linked && !row.Home) + " coupled.";
                if(dormantEnabled&&MaximumInterval>0&&dormantClockMinutes>=nextDormantRequest){try{RequestDormantTow();}finally{ScheduleRequest();}}
                if (scanFailed) context.Log("[Tugs] Observation recovered.");
                scanFailed = false;
            }
            catch (Exception error)
            {
                Rows = new TugObservation[0]; Status = "Tug observation unavailable; see framework log.";
                if (!scanFailed) context.Log("[Tugs] Scan failed: " + error);
                scanFailed = true;
            }
        }
        public void Dispose()
        {
            recoveryRegistration?.Dispose();recoveryRegistration=null;foreach(var p in recoveries.Keys.ToArray())RemoveRecovery(p);
            recoveryApproach?.Dispose();recoveryApproach=null;
            aircraftLabels?.Dispose();aircraftLabels=null;aircraftLabelSetting?.Dispose();aircraftLabelSetting=null;
            holdingReportOwner?.Dispose();holdingReportOwner=null;
            routeSetting?.Dispose();routeSetting=null;routeDisplay?.Dispose();routeDisplay=null;proposedRoutes.Clear();routePreviews=new TowRoutePreview[0];if(routePath!=null)try{TowRouteTelemetry.Write(routePath,routePreviews);}catch(IOException){}
            intervalSetting?.Dispose();intervalSetting=null;maximumIntervalSetting?.Dispose();maximumIntervalSetting=null;stripSetting?.Dispose();stripSetting=null;
            standDisplay?.Dispose();standDisplay=null;stripDisplay?.Dispose();stripDisplay=null;standMarkers=new TowStandMarker[0];
            if(standPath!=null)try{TowStandTelemetry.Write(standPath,null);}catch(IOException){}
            routeCheck?.Dispose();routeCheck=null;
            voice?.Dispose();voice=null;transfer?.Dispose();transfer=null;
            towOnly?.Dispose();towOnly=null;stripCleanup?.Tick();stripCleanup=null;
            returns?.Dispose(); returns = null; trucks.Clear();
            if (panel) { panel.Module = null; UnityEngine.Object.Destroy(panel.gameObject); }
            Rows = new TugObservation[0]; tracker.Reset();
        }
        internal TowPlanningSession TransferDestinations(int tugId)
        {
            context.Log("[Tugs] Tow planning requested for tug="+tugId);
            try
            {
                var result=InspectTransferDestinations(tugId);
                context.Log("[Tugs] Tow planning opened for "+result.Callsign+" at "+result.Source+"; roads="+result.Roads.Length+"; destinations="+result.Destinations.Length+"; "+result.DiagnosticSummary);
                return result;
            }
            catch(Exception error)
            {
                context.Log("[Tugs] Tow planning unavailable: "+error);
                throw;
            }
        }
        private void CaptureTowFailure(Component aircraft,Vector3[] points,string reason)
        {
            if(!TS3Modding.Preferences.DeveloperMode.Enabled)return;
            var path=Path.Combine(BepInEx.Paths.GameRootPath,"BepInEx","TS3ModLoader","tow-route-failure.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path));var culture=System.Globalization.CultureInfo.InvariantCulture;
            File.WriteAllText(path,"# "+DateTime.UtcNow.ToString("o")+" "+TowCallsign(aircraft)+" "+reason+"\nindex,x,y,z\n"+string.Join("\n",points.Select((p,i)=>i.ToString(culture)+","+p.x.ToString("R",culture)+","+p.y.ToString("R",culture)+","+p.z.ToString("R",culture))));
            context.Log("[Tugs] Captured held tow route geometry: "+path);
        }
        private void RememberProposal(TowPlanningSession session,TowDestination target,TowPoint[] points,bool native)
        {
            if(native)try{points=TowParkingGeometry.Trim(points,session.Roads.Single(r=>r.Id==target.Road).Points[target.Index]);}catch(Exception error){context.Log("[Tugs] Checked route preview unavailable: "+error.Message);return;}
            if(!session.Aircraft||points==null||points.Length<2||points.Length>4096||points.Any(p=>!p.Finite))return;
            proposedRoutes[session.Aircraft]=new ProposedRoute{Game=session.Game,Schedule=session.Schedule,Callsign=session.Callsign,Destination=target.Name,Points=points.ToArray(),Native=native,RequiresRequest=towOnly?.Owns(session.Aircraft)==true};nextRoutePublish=0;
        }
        private void PublishProposals(object airport)
        {
            if(routePath==null||Time.realtimeSinceStartup<nextRoutePublish)return;nextRoutePublish=Time.realtimeSinceStartup+.5f;
            try
            {
                var rows=new List<TowRoutePreview>();var count=0;
                foreach(var pair in proposedRoutes.ToArray())
                {
                    var aircraft=pair.Key;var proposal=pair.Value;
                    if(!aircraft||!ReferenceEquals(airport,proposal.Game)||(bool)deleted.GetValue(aircraft)||!ReferenceEquals(timetable.GetValue(aircraft),proposal.Schedule)||!string.Equals((string)planeName.GetValue(aircraft),proposal.Callsign,StringComparison.Ordinal)||proposal.RequiresRequest&&towOnly?.Owns(aircraft)!=true)
                    {proposedRoutes.Remove(aircraft);continue;}
                    if(!(showTowRoute?.Invoke()??true))continue;
                    var points=proposal.Points;var native=proposal.Native;
                    if(transfer?.Owns(aircraft)==true){var pending=transfer.RouteForPreview(aircraft);if(pending==null)continue;points=pending.Select(p=>new TowPoint(p.x,p.y,p.z)).ToArray();native=true;}
                    else if(!(bool)atStand.GetValue(aircraft)){proposedRoutes.Remove(aircraft);continue;}
                    if(rows.Count>=64||count+points.Length>32768)continue;count+=points.Length;
                    rows.Add(new TowRoutePreview{CreatedUtc=DateTime.UtcNow,NetworkId=(int)Required(aircraft.GetType(),"_netidx").GetValue(aircraft),Callsign=(string)planeName.GetValue(aircraft),NativeChecked=native,Points=points.Select(p=>new TowRouteVertex(p.X,p.Y,p.Z)).ToArray()});
                }
                routePreviews=rows.ToArray();TowRouteTelemetry.Write(routePath,routePreviews);
            }
            catch(Exception error){routePreviews=new TowRoutePreview[0];context.Log("[Tugs] Proposed route publication failed: "+error.Message);}
        }
        private void PublishStandMarker(object airport)
        {
            if(standPath==null||Time.realtimeSinceStartup<nextStandPublish)return;nextStandPublish=Time.realtimeSinceStartup+.2f;
            try{
                var markers=new List<TowStandMarker>();
                if(airport!=null){
                    var field=Required(airport.GetType(),"airfield").GetValue(airport);var data=Required(field.GetType(),"aircont").GetValue(field);
                    var roads=((System.Collections.IEnumerable)Required(data.GetType(),"roads").GetValue(data)).Cast<object>().Where(r=>r!=null).ToArray();
                    foreach(var aircraft in TowingAircraft.Concat(DormantAircraft).Distinct()){
                        if(!aircraft||(bool)deleted.GetValue(aircraft))continue;var destination=TowDestination(aircraft);if(destination==null)continue;
                        var road=roads.SingleOrDefault(r=>string.Equals((string)Required(r.GetType(),"name").GetValue(r),destination,StringComparison.Ordinal));if(road==null)continue;
                        var points=Required(road.GetType(),"points").GetValue(road) as List<Vector3>;if(points==null||points.Count<2)continue;
                        var first=points.Count/2;var second=Math.Min(first+2,points.Count-1);if(first==second)first--;
                        var p=points[first];var angle=(float)aircraft.GetType().Assembly.GetType("ANFGFEFKGOH",true).GetMethod("KBOOGOGMMPA",BindingFlags.Static|Flags,null,new[]{typeof(Vector3),typeof(Vector3)},null).Invoke(null,new object[]{p,points[second]});
                        markers.Add(new TowStandMarker{CreatedUtc=DateTime.UtcNow,NetworkId=(int)Required(aircraft.GetType(),"_netidx").GetValue(aircraft),Callsign=(string)planeName.GetValue(aircraft),X=p.x,Y=p.y,Z=p.z,Heading=-angle,Destination=(showStripDestination?.Invoke()??true)?TowDestinationLabel(aircraft):""});
                    }
                }
                standMarkers=markers.ToArray();TowStandTelemetry.WriteMany(standPath,standMarkers);
            }catch(Exception error){standMarkers=new TowStandMarker[0];if(!standPublishFailed){standPublishFailed=true;context.Log("[Tugs] Stand marker publication failed: "+error.Message);}}
        }
        private TowPlanningSession InspectTransferDestinations(int tugId)
        {
            if(towPlanning==null)throw new InvalidOperationException("Tow planning profile unavailable.");
            Component truck;if(!trucks.TryGetValue(tugId,out truck)||!truck)throw new InvalidOperationException("Tug is no longer available.");
            var aircraft=plane.GetValue(truck) as Component;if(!aircraft)aircraft=returns.AssociatedAircraft(truck);
            var row=Rows.FirstOrDefault(r=>r.TugId==tugId);
            if(row==null||row.ReturnActive)throw new InvalidOperationException("Finish the active return before planning another transfer.");
            var game=instance.GetValue(null);if(game==null)throw new InvalidOperationException("Load an airport session first.");
            return towPlanning.Inspect(game,aircraft,row.Callsign);
        }
        internal TowPreview PreviewTransfer(TowPlanningSession session,TowDestination destination)
        {
            var result=TowPreviewPlanner.Plan(session.Roads,session.SourceRoad,session.SourceIndex,destination,true);
            if(result.Found)RememberProposal(session,destination,result.Points,false);
            var source=session.Roads.First(r=>r.Id==session.SourceRoad);
            context.Log("[Tugs] Tow route preview "+session.Callsign+": "+session.Source+" -> "+destination.Name+"; source index="+session.SourceIndex+"; source direction="+source.Direction+"; source crossings="+source.Connections.Length+"; found="+result.Found+"; "+result.Message);
            return result;
        }
        internal string PreviewCrossings(int tugId,TowPlanningSession snapshot,TowDestination destination)
        {
            var fresh=InspectTransferDestinations(tugId);
            if(!ReferenceEquals(fresh.Aircraft,snapshot.Aircraft)||!ReferenceEquals(fresh.Game,snapshot.Game)||!ReferenceEquals(fresh.Schedule,snapshot.Schedule))throw new InvalidOperationException("Airport or flight changed. Refresh destinations.");
            var target=fresh.Destinations.SingleOrDefault(d=>d.Name==destination.Name&&ReferenceEquals(fresh.NativeRoads[d.Road],snapshot.NativeRoads[destination.Road]));
            if(target==null)throw new InvalidOperationException("Destination changed. Refresh destinations.");
            var plan=TowPreviewPlanner.Plan(fresh.Roads,fresh.SourceRoad,fresh.SourceIndex,target,true);
            if(!plan.Found)return "Crossing preview: "+plan.Message;
            var type=Required(fresh.Aircraft.GetType(),"_type").GetValue(fresh.Aircraft);double length=(float)Required(type.GetType(),"_length").GetValue(type),width=(float)Required(type.GetType(),"_width").GetValue(type);
            var events=new TowRunwayCrossings(plan.Points,fresh.Roads,Math.Sqrt(length*length+width*width)+30).Events;
            var text="READ-ONLY CROSSING PREVIEW - no clearance or movement.\n"+string.Join(" -> ",plan.Roads)+"\n"+events.Length+" runway protection encounters. Aircraft/tug footprint plus braking allowance included; actual native route is checked again before movement.";
            foreach(var e in events)text+="\n"+e.Name+": enter at "+e.Entry.ToString("0")+", clear at "+e.Exit.ToString("0")+"; "+(e.HasApproachSpace?"candidate stop at "+e.Hold.ToString("0")+" ("+e.HoldPosition.X.ToString("0.0")+", "+e.HoldPosition.Z.ToString("0.0")+")":"insufficient approach/departure space");
            context.Log("[Tugs] "+text);return text;
        }
        internal void CheckTransfer(int tugId,TowPlanningSession snapshot,TowDestination destination)
        {
            if(routeCheck==null)throw new InvalidOperationException("Native route check unavailable.");
            var fresh=InspectTransferDestinations(tugId);
            if(!ReferenceEquals(fresh.Aircraft,snapshot.Aircraft)||!ReferenceEquals(fresh.Schedule,snapshot.Schedule)||!ReferenceEquals(fresh.Game,snapshot.Game)||fresh.Callsign!=snapshot.Callsign||
                !ReferenceEquals(fresh.NativeRoads[fresh.SourceRoad],snapshot.NativeRoads[snapshot.SourceRoad]))throw new InvalidOperationException("Aircraft or airport changed. Refresh the plan.");
            var target=fresh.Destinations.SingleOrDefault(d=>d.Name==destination.Name&&d.Index==destination.Index&&ReferenceEquals(fresh.NativeRoads[d.Road],snapshot.NativeRoads[destination.Road]));
            if(target==null)throw new InvalidOperationException("Destination changed. Refresh the plan.");
            routeCheck.Start(fresh,target);
        }
        internal void CancelRouteCheck()=>routeCheck?.Cancel("Native route check cancelled by controller.");
        internal void RequestTransfer(int tugId,TowPlanningSession snapshot,TowDestination destination,string[] via=null,string holdShort=null,string crossRunway=null)
        {
            if(transfer==null)throw new InvalidOperationException("Stand transfers are disabled in mod options.");
            if(pendingTransfer!=0||routeCheck.Busy)throw new InvalidOperationException("Wait for the current route check to finish.");
            var fresh=InspectTransferDestinations(tugId);
            var modeRefusal=TowModeRefusal(fresh.Aircraft);if(modeRefusal!=null)throw new InvalidOperationException(modeRefusal);
            if(!ReferenceEquals(fresh.Aircraft,snapshot.Aircraft)||!ReferenceEquals(fresh.Schedule,snapshot.Schedule)||!ReferenceEquals(fresh.Game,snapshot.Game))throw new InvalidOperationException("Flight or airport changed. Refresh destinations.");
            var target=fresh.Destinations.SingleOrDefault(d=>d.Name==destination.Name&&ReferenceEquals(fresh.NativeRoads[d.Road],snapshot.NativeRoads[destination.Road]));
            if(target==null)throw new InvalidOperationException("Destination changed. Refresh destinations.");
            towOnly?.RequireRequestedStand(fresh.Aircraft,target.Name,nativeName:true);
            if(DormantAircraft.Any(p=>!ReferenceEquals(p,fresh.Aircraft)&&towOnly.DestinationFor(p)==target.Name))throw new InvalidOperationException("Destination is already requested by another tow.");
            var aligned=new TowDestination{Road=target.Road,Index=transfer.ParkingIndex(fresh.Aircraft,fresh,target),Name=target.Name,Kind=target.Kind,Occupied=target.Occupied,Reserved=target.Reserved};
            routeCheck.Start(fresh,aligned,via,holdShort);pendingTransfer=tugId;pendingAircraft=fresh.Aircraft;pendingCrossRunway=crossRunway;transferFailure=null;
            Reply(fresh.Aircraft,"Towing to stand "+TowVoiceIntent.Spoken(TowStandAliases.For(fresh).Alias(target.Name))+(via==null||via.Length==0?"":", via taxiways "+string.Join(", ",via.Select(TowVoiceIntent.Spoken))));

        }
        internal void HoldTransfer(bool held)=>transfer?.Hold(held);
        internal void RequestDormantTow()
        {
            if(!dormantEnabled||towOnly==null||towOnly.AircraftList.Length>=maxTows||routeCheck?.Busy==true)return;
            var airport=instance.GetValue(null);if(airport==null||(bool)Required(airport.GetType(),"loading").GetValue(airport)||(float)Required(airport.GetType(),"speedup").GetValue(airport)<=0)return;
            var inspected=0;
            foreach(var row in Rows.Where(r=>r.AtStand&&r.AircraftId!=0&&!r.ReturnActive&&!dormantRequested.Contains(r.AircraftId)))
            {
                try{
                    var snapshot=InspectTransferDestinations(row.TugId);var aircraft=snapshot.Aircraft;
                    if((bool)Required(aircraft.GetType(),"_active").GetValue(aircraft)||Convert.ToInt32(Required(aircraft.GetType(),"_action").GetValue(aircraft))!=2)continue;
                    var maintenance=TS3Modding.API.AircraftMaintenanceServices.Contains(aircraft);
                    var minutes=maintenance?1440f:(float)aircraft.GetType().GetMethod("getTimeToGoMins",Flags).Invoke(aircraft,null);if(float.IsNaN(minutes)||float.IsInfinity(minutes)||minutes<60)continue;
                    if(++inspected>2)break;
                    dormantRequested.Add(row.AircraftId);
                    var available=snapshot.Destinations.Where(d=>!d.Occupied&&!d.Reserved&&snapshot.Roads.Single(r=>r.Id==d.Road).Type==1).ToArray();
                    Func<TowDestination,double> distance=d=>snapshot.Roads[d.Road].Points[d.Index].Distance(snapshot.Roads[snapshot.SourceRoad].Points[snapshot.SourceIndex]);
                    foreach(var target in available.OrderByDescending(distance).Take(4).Concat(available.OrderBy(distance).Take(4)).Distinct())
                    {
                        try{
                            var index=transfer.ParkingIndex(aircraft,snapshot,target);
                            var aligned=new TowDestination{Road=target.Road,Index=index,Name=target.Name,Kind=target.Kind};
                            var preview=TowPreviewPlanner.Plan(snapshot.Roads,snapshot.SourceRoad,snapshot.SourceIndex,aligned,true);
                            if(!preview.Found||preview.Length>TowRouteSafety.MaximumRequestLength||minutes<Math.Max(60,preview.Length/90+30))continue;
                            context.Log("[Tugs] Generated tow "+snapshot.Callsign+" -> "+target.Name+"; topology length="+preview.Length.ToString("0.0")+" world units.");
                            towOnly.Request(airport,aircraft,target.Name,TowStandAliases.For(snapshot).Alias(target.Name));RememberProposal(snapshot,aligned,preview.Points,false);dormantRequested.Add(row.AircraftId);
                            transferFailure=null;return;
                        }catch(Exception){/* A different compatible stand can still be available. */}
                    }
                }catch(Exception error){context.Log("[Tugs] Dormant candidate skipped: "+error.Message);}
            }
            context.Log("[Tugs] No eligible dormant aircraft with a compatible nearby stand found.");
        }
        internal void AcceptDormantTow()=>AcceptDormantTow(towOnly?.Aircraft);
        internal void AcceptDormantTow(Component aircraft)
        {
            if(aircraft&&recoveries.ContainsKey(aircraft)){try{VoiceCommand(aircraft,new TowVoiceIntent{Action="transfer",Destination=RecoveryAlias(aircraft)});}catch(Exception e){Reply(aircraft,"Unable, "+e.GetBaseException().Message);}return;}
            if(aircraft==null||towOnly?.Owns(aircraft)!=true)return;
            try{
                VoiceCommand(aircraft,new TowVoiceIntent{Action="transfer",Destination=towOnly.DestinationAliasFor(aircraft)?.ToLowerInvariant()});
            }catch(Exception error){transferFailure=error.Message;Reply(aircraft,"Unable, "+error.Message);}
        }
        internal void CancelTransfer(){foreach(var aircraft in TowingAircraft.Concat(DormantAircraft).Concat(pendingAircraft?new[]{pendingAircraft}:new Component[0]).Distinct().ToArray())CancelTransfer(aircraft);}
        internal void CancelTransfer(Component aircraft)
        {
            if(recoveries.ContainsKey(aircraft)){CancelRecoveryClearance(aircraft);return;}
            proposedRoutes.Remove(aircraft);nextRoutePublish=0;
            if((towOnly?.Owns(aircraft)==true||transfer?.HasCrossingRoute(aircraft)==true)&&transfer?.Owns(aircraft)==true&&towOnly?.ParkedAt(aircraft)!=true&&!transfer.CanCancelBeforeDeparture(aircraft)){transfer.Hold(aircraft,true);Reply(aircraft,"Tow held, continue tow to complete parking before cancellation");return;}
            if(ReferenceEquals(pendingAircraft,aircraft)){pendingTransfer=0;pendingAircraft=null;pendingCrossRunway=null;routeCheck?.Cancel("Transfer cancelled before coupling.");}
            transfer?.Cancel(aircraft,"Cancelled by controller");
            if(towOnly?.ParkedAt(aircraft)==true)towOnly.Restore(aircraft);
        }
        private void VoiceCommand(Component aircraft,TowVoiceIntent intent)
        {
            if(recoveries.ContainsKey(aircraft)&&transfer?.Owns(aircraft)!=true){RecoveryVoice(aircraft,intent);return;}
            if(intent.Action=="reroute")
            {
                intent.Destination=transfer?.DestinationAliasFor(aircraft)??towOnly?.DestinationAliasFor(aircraft);
                if(intent.Destination==null)throw new InvalidOperationException("No known tow destination; say tow to stand and the stand identifier.");
                intent.Destination=intent.Destination.ToLowerInvariant();intent.Action="transfer";
            }
            if(intent.Action=="return")
            {
                if(TransferBusy)throw new InvalidOperationException("Finish or cancel the stand transfer first.");
                var candidates=trucks.Where(t=>t.Value&&(ReferenceEquals(plane.GetValue(t.Value),aircraft)||ReferenceEquals(returns.AssociatedAircraft(t.Value),aircraft))).ToArray();
                if(candidates.Length!=1)throw new InvalidOperationException("No unique available tug is associated with this aircraft.");
                if(!returns.Request(candidates[0].Value))throw new InvalidOperationException(returns.Message);
                Reply(aircraft,"Returning to the original stand");nextScan=0;return;
            }
            if(intent.Action!="transfer")
            {
                if(transfer?.Owns(aircraft)!=true&&!ReferenceEquals(pendingAircraft,aircraft)&&towOnly?.Owns(aircraft)!=true){Reply(aircraft,"We are not being towed");return;}
                if(intent.Action=="cross")
                {
                    if(ReferenceEquals(pendingAircraft,aircraft)){pendingCrossRunway=intent.Destination;Reply(aircraft,"Crossing clearance received, checking tow route");return;}
                    if(recoveryApproach?.Clear(aircraft,intent.Destination)!=true)transfer.ClearRunway(aircraft,intent.Destination);return;
                }
                if(intent.Action=="runwayhold"||intent.Action=="revokecross")
                {
                    if(ReferenceEquals(pendingAircraft,aircraft)){pendingCrossRunway=null;Reply(aircraft,"Runway crossing clearance cancelled");return;}
                    if(recoveryApproach?.Revoke(aircraft,intent.Destination)!=true)transfer.HoldRunway(aircraft,intent.Destination);return;
                }
                if(intent.Action=="cancel"){CancelTransfer(aircraft);return;}
                if(intent.Action=="holdshort"){transfer.HoldShort(aircraft,intent.HoldShort);return;}
                if(ReferenceEquals(pendingAircraft,aircraft))throw new InvalidOperationException("Wait until route checking finishes before holding or continuing the tow.");
                if(intent.Action=="recover"){RecoverTow(aircraft);return;}
                transfer.Hold(aircraft,intent.Action=="hold");return;
            }
            if(transfer?.Owns(aircraft)==true)
            {
                if(intent.CrossRunway!=null && intent.Via.Length==0 && intent.HoldShort==null)
                {
                    if(!string.Equals(transfer.DestinationAliasFor(aircraft),intent.Destination,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("wrong stand");
                    transfer.ClearRunway(aircraft,intent.CrossRunway);return;
                }
                transfer.RequestReroute(aircraft,intent.Destination,intent.Via,intent.HoldShort,intent.CrossRunway);return;
            }
            towOnly?.RequireRequestedStand(aircraft,intent.Destination);
            var refusal=TowModeRefusal(aircraft);if(refusal!=null){Reply(aircraft,"Unable, "+refusal);return;}
            var matches=trucks.Where(t=>ReferenceEquals(plane.GetValue(t.Value),aircraft)||ReferenceEquals(returns.AssociatedAircraft(t.Value),aircraft)).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("No unique available tug is associated with this aircraft.");
            var snapshot=InspectTransferDestinations(matches[0].Key);
            var aliases=TowStandAliases.For(snapshot);
            var destinations=snapshot.Destinations.Where(d=>string.Equals(aliases.Alias(d.Name),intent.Destination,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(destinations.Length!=1)throw new InvalidOperationException("Stand identifier is unknown or ambiguous. Use the tug panel.");
            RequestTransfer(matches[0].Key,snapshot,destinations[0],intent.Via,intent.HoldShort,intent.CrossRunway);
        }
        private string TowModeRefusal(Component aircraft)=>TowCommandMode.Refusal(towOnly?.Owns(aircraft)==true||transfer?.Owns(aircraft)==true||ReferenceEquals(pendingAircraft,aircraft),(bool)Required(aircraft.GetType(),"_requestingpush").GetValue(aircraft),(bool)atStand.GetValue(aircraft),state.GetValue(aircraft).ToString());
        private void Reply(Component aircraft,string message)
        {
            var diagnostic=message;message=TowRadioText.ForPilot(message);if(message!=diagnostic)context.Log("[Tugs] Radio diagnostic retained in log: "+diagnostic);
            try
            {
                var radio=RadioCommands.Current as IRadioCommandProviderService;
                if(radio==null)return;
                radio.Reply(aircraft,message,TowRadioText.Display(message));
                context.Log("[Tugs] Radio: "+message);
            }
            catch(Exception error){context.Log("[Tugs] Radio response failed: "+error.Message);}
        }
        private string ResolveTowSpeech(string transcript)
        {
            var game=instance.GetValue(null);if(game==null)throw new InvalidOperationException("No airport is loaded.");
            var window=Required(game.GetType(),"cmdwin").GetValue(game);
            try
            {
                var planes=((System.Collections.IEnumerable)Required(game.GetType(),"airplanes").GetValue(game)).Cast<Component>().Where(p=>p&&!(bool)deleted.GetValue(p)).ToArray();
                var live=planes.Select(p=>(string)planeName.GetValue(p)).ToArray();
                // Native speech output is authoritative for the active flight. The recognizer's
                // exported lexicon can lag behind loaded saves or contain a different alias spelling.
                var aliases=planes.Select(p=>new TowSpeechAircraft{id=(string)planeName.GetValue(p),pronunciations=new[]{(string)p.GetType().GetMethod("get__sayname",Flags).Invoke(p,null)}}).ToList();
                try
                {
                    var native=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEngine.JsonUtility")).FirstOrDefault(t=>t!=null)??Type.GetType("UnityEngine.JsonUtility, UnityEngine.JSONSerializeModule",true);
                    var json=File.ReadAllText(Path.Combine(Application.temporaryCachePath,"session_lexicon.json"));
                    var lexicon=(TowSpeechLexicon)native.GetMethod("FromJson",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string),typeof(Type)},null).Invoke(null,new object[]{json,typeof(TowSpeechLexicon)});
                    aliases.AddRange((lexicon.airplane??new TowSpeechAircraft[0]).Where(a=>a!=null&&live.Contains(a.id,StringComparer.OrdinalIgnoreCase)));
                }
                catch(Exception error){context.Log("[Tugs] Tow speech using live callsigns; optional lexicon unavailable: "+error.Message);}
                if(!TowVoiceIntent.IsTow(transcript))aliases=aliases.Where(a=>TowingAircraft.Any(p=>string.Equals((string)planeName.GetValue(p),a.id,StringComparison.OrdinalIgnoreCase))).ToList();
                var command=TowSpokenAddress.Command(transcript,aliases);
                context.Log("[Tugs] Tow speech addressed command: "+command);
                // Native Command gathers the player's controlled frequencies and calls HandleCommand;
                // the framework then queues the registered tow parser on the main thread.
                return command;
            }
            catch(Exception error)
            {
                window.GetType().GetMethod("AddHistory",Flags,null,new[]{typeof(string)},null)?.Invoke(window,new object[]{"Tow voice: "+error.Message});throw;
            }
        }
        internal void RequestReturn(int tugId)
        {
            if(TransferBusy){context.Log("[Tugs] Return refused while stand transfer is active.");return;}
            Component truck;
            try { if (trucks.TryGetValue(tugId, out truck) && truck) returns.Request(truck); }
            catch (Exception error) { context.Log("[Tugs] Return request failed: " + error); }
            nextScan = 0;
        }
        internal void CancelReturn() { returns?.Cancel("Return cancelled by controller; aircraft held"); nextScan = 0; }
    }
}
