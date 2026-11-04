using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TS3Modding.Adapters;
using TS3Modding.API;
using TS3Modding.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TS3Modding;

internal sealed class FrameworkRuntime : IFrameworkHost, IAirportObservationSource, IStripMovementService, IFlightArchivalService, IDormantTowStripArchivalService, IDisposable
{
    private readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
    private readonly EventHub events;
    private readonly AircraftRegistry aircraft;
    private readonly ModuleManager modules;
    private readonly DiagnosticFiles files;
    private readonly Action<string> logger;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly double scanInterval;
    private MonoAirportAdapter adapter;
    private AirportObservation currentAirport = new AirportObservation(new AircraftSnapshot[0], new GateSnapshot[0], new StripSnapshot[0]);
    private long movedStrips;
    private double nextScan, nextStatus;
    private long ticks, scans;
    private string adapterStatus;
    private bool disposed;
    private readonly PerformanceWindow performance = new PerformanceWindow();
    private readonly Action<string, double> moduleTiming;
    private double nextPerformance = 30;
    private readonly IDisposable helpRegistration;
    private NativeRadioCommands radioCommands;
    public FrameworkRuntime(DiagnosticFiles files, Action<string> logger, string hash, bool observe, double scanInterval)
    {
        moduleTiming = performance.Record;
        this.files = files; this.logger = logger; this.scanInterval = scanInterval;
        GameAssemblySha256 = hash;
        events = new EventHub(error => Log("Event subscriber failed: " + error));
        aircraft = new AircraftRegistry(events);
        modules = new ModuleManager(this, (id, error) => Log("Module disabled: " + id + " " + error));
        adapterStatus = "Disabled by configuration";
        if (observe)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
                adapter = new MonoAirportAdapter(assembly, hash, Log);
                adapterStatus = "Available (reviewed airport profile)";
            }
            catch (Exception error) { adapterStatus = "Unavailable: " + error.Message; Log(adapterStatus); }
        }
        if (hash == "26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7")
        {
            try { radioCommands = new NativeRadioCommands(AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp"), Log); }
            catch (Exception error) { Log("Custom voice/radio commands unavailable: " + error.Message); }
        }
        FrameworkServices.Attach(this);
        ModHelp.Changed += ExportHelp;
        helpRegistration = ModHelp.Register(new ModHelpPage("nl.ts3modding.loader", "0.8.21", "Shared mod services, settings and documentation."));
        SceneManager.sceneLoaded += OnSceneLoaded;
        RegisterModule(new ObservationLogModule());
        WriteStatus("Initialized; awaiting Unity updates");
    }
    private void ExportHelp()
    {
        try { foreach (var page in ModHelp.Snapshot()) files.Help(page); }
        catch (Exception error) { Log("Help export failed: " + error.Message); }
    }
    public IEventStream Events => events;
    public IAircraftRegistry Aircraft => aircraft;
    public string GameAssemblySha256 { get; }
    public bool ObservationAvailable => adapter != null;
    public AirportObservation CurrentAirport => currentAirport;
    public void Log(string message)
    {
        files.Append(message);
        try { logger(message); } catch { }
    }
    private void RequireMainThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Framework access requires Unity's main thread.");
        if (disposed) throw new ObjectDisposedException(nameof(FrameworkRuntime));
    }
    public IDisposable RegisterModule(ITS3Module module) { RequireMainThread(); return modules.Register(module); }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log("Scene loaded: " + scene.name + " (" + mode + ")");
        events.Publish(new SceneLoaded(scene.name));
    }
    private bool profiling;
    public void Tick()
    {
        var enabled = TS3Modding.Preferences.DeveloperMode.Enabled;
        if (enabled != profiling) { profiling = enabled; performance.Reset(); nextPerformance = clock.Elapsed.TotalSeconds + 30; }
        if (!profiling) { TickCore(false); return; }
        performance.BeginFrame(); var started = Stopwatch.GetTimestamp();
        try { TickCore(true); }
        finally { performance.Record("framework.total", PerformanceWindow.Elapsed(started)); }
        if (clock.Elapsed.TotalSeconds >= nextPerformance) { nextPerformance = clock.Elapsed.TotalSeconds + 30; files.Performance(performance.Drain()); }
    }
    private void TickCore(bool measured)
    {
        RequireMainThread();
        ticks++;
        var now = clock.Elapsed.TotalSeconds;
        if (adapter != null && now >= nextScan)
        {
            nextScan = now + scanInterval;
            try
            {
                var scanStarted = measured ? Stopwatch.GetTimestamp() : 0;
                var observation = adapter.Observe();
                if (measured) performance.Record("airport.scan", PerformanceWindow.Elapsed(scanStarted));
                var dispatchStarted = measured ? Stopwatch.GetTimestamp() : 0;
                currentAirport = observation;
                aircraft.ApplyCompleteObservation(observation.Aircraft);
                events.Publish(new AirportObserved(observation));
                scans++;
                if (measured) performance.Record("airport.observation-dispatch", PerformanceWindow.Elapsed(dispatchStarted));
            }
            catch (Exception error)
            {
                adapter?.Dispose();
                adapter = null;
                adapterStatus = "Disabled after observation failure: " + error.Message;
                Log(adapterStatus);
            }
        }
        try { radioCommands?.Tick(); } catch (Exception error) { radioCommands?.Dispose(); radioCommands = null; Log("Custom command bridge disabled: " + error.Message); }
        modules.Tick(measured ? moduleTiming : null);
        if (measured && now >= nextStatus) { nextStatus = now + 5; var exportStarted = Stopwatch.GetTimestamp(); WriteStatus("Running"); performance.Record("diagnostics.export", PerformanceWindow.Elapsed(exportStarted)); }
    }
    public bool TryMoveStrip(StripSnapshot expected, string targetHeader)
    {
        RequireMainThread();
        if (adapter == null || expected == null || string.IsNullOrWhiteSpace(targetHeader)) return false;
        var moved = adapter.TryMove(expected, targetHeader, files.MovementAudit);
        if (moved) movedStrips++;
        return moved;
    }
    public bool TryArchiveDormantTowStrip(StripSnapshot expected, object nativeAircraft, object expectedSchedule)
    {
        RequireMainThread(); if (adapter == null) return false;
        var moved = adapter.TryArchiveDormantTow(expected, nativeAircraft, expectedSchedule, files.MovementAudit);
        if (moved) movedStrips++; return moved;
    }
    public bool TryArchiveStrip(StripSnapshot expected, FlightCompletion completion)
    {
        RequireMainThread();
        if (adapter == null) return false;
        var moved = adapter.TryArchive(expected, completion, files.MovementAudit);
        if (moved) movedStrips++;
        return moved;
    }
    private void WriteStatus(string lifecycle)
    {
        if (!TS3Modding.Preferences.DeveloperMode.Enabled) return;
        try
        {
            files.Status("Framework=0.8.21\nState=" + lifecycle + "\nUnity=" + Application.unityVersion +
                "\nGameAssemblySHA256=" + GameAssemblySha256 + "\nAdapter=" + adapterStatus +
                "\nTicks=" + ticks + "\nCompleteScans=" + scans + "\nObservedAircraft=" + aircraft.Snapshot().Count +
                "\nSimulationActiveAircraft=" + currentAirport.Aircraft.Count(a => a.Details.SimulationActive) +
                "\nObservedGateLinks=" + currentAirport.Gates.Count + "\nObservedStrips=" + currentAirport.Strips.Count +
                "\nNativeStripMoves=" + movedStrips +
                "\nCompletionEvidence=" + currentAirport.Completions.Count + "\nModules=" + modules.Count + "\nObservationalHooks=" + (adapter?.CompletionHookCount ?? 0) + "\nGameplayPatches=0\n");
            if (scans > 0) files.ExportAirport(currentAirport, ObservationAvailable);
        }
        catch (Exception error) { Log("Status write failed: " + error.Message); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        modules.Dispose();
        radioCommands?.Dispose();
        ModHelp.Changed -= ExportHelp;
        helpRegistration.Dispose();
        FrameworkServices.Detach(this);
        adapter?.Dispose();
        adapter = null;
        WriteStatus("Stopped");
        Log("Framework stopped.");
    }
    private sealed class ObservationLogModule : ITS3Module
    {
        public string Id => "nl.ts3modding.observation-log";
        public void Initialize(IFrameworkContext context)
        {
            context.Events.Subscribe<AircraftObserved>(e => { if (TS3Modding.Preferences.DeveloperMode.Enabled) context.Log("Aircraft observed: " + e.Aircraft.Id + " name=" + e.Aircraft.ObjectName); });
            context.Events.Subscribe<AircraftNoLongerObserved>(e => { if (TS3Modding.Preferences.DeveloperMode.Enabled) context.Log("Aircraft no longer observed: " + e.Last.Id); });
            context.Events.Subscribe<AircraftStateChanged>(e => { if (TS3Modding.Preferences.DeveloperMode.Enabled) context.Log("Aircraft state: " + e.Current.Details.Callsign + " " + e.Previous.Details.RawState + " -> " + e.Current.Details.RawState + " / " + e.Current.Details.RawAction); });
        }
        public void Tick() { }
        public void Dispose() { }
    }
}




