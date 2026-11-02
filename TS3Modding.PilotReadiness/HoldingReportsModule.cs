using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TS3Modding.API;
using TS3Modding.Adapters;

namespace TS3Modding.PilotReadiness
{
    internal sealed class HoldingReportsModule : ITS3Module
    {
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static HoldingReportsModule current;
        private readonly Harmony harmony = new Harmony("nl.ts3modding.pilotreadiness.holding");
        private readonly ConditionalWeakTable<object, HoldingReportTracker> trackers = new ConditionalWeakTable<object, HoldingReportTracker>();
        private readonly List<WeakReference> aircraft = new List<WeakReference>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private IFrameworkContext context;
        private FieldInfo hold, gateHold, previousCross, previousGate, speed, altitude, active, deleted;
        private FieldInfo crossRoad, thisRoad, roadName, roadType, knotName, gateRoads;
        private FieldInfo assignedRunway, assignedGate, action, state;
        private readonly HoldingReportOptions options;
        public HoldingReportsModule(HoldingReportOptions options) { this.options = options; }
        private MethodInfo reply, sayName;
        private FieldInfo planeGame, commandWindow, callsign;
        private double nextScan, nextReport;
        public string Id => "nl.ts3modding.pilotreadiness.holding";
        public void Initialize(IFrameworkContext context)
        {
            this.context = context;
            if (current != null) throw new InvalidOperationException("Holding reports already registered.");
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
            var plane = assembly.GetType("Airplane", true);
            var road = assembly.GetType("Road", true);
            var cross = assembly.GetType("Road+FFNEIEEFNKF", true);
            var knot = assembly.GetType("Road+Knots_t", true);
            hold = Field(plane, "_hold_position_taxistop"); gateHold = Field(plane, "_gate_taxistop");
            previousCross = Field(plane, "_prev_holdshort"); previousGate = Field(plane, "_prevgate");
            speed = Field(plane, "_lv"); altitude = Field(plane, "_alt");
            active = Field(plane, "_active"); deleted = Field(plane, "_deleted");
            crossRoad = Field(cross, "cross"); thisRoad = Field(cross, "thisroad");
            roadName = Field(road, "name"); roadType = Field(road, "type");
            knotName = Field(knot, "name"); gateRoads = Field(knot, "gateroads");
            assignedRunway = Field(plane, "_trgrunway"); assignedGate = Field(plane, "_last_gate");
            action = Field(plane, "_action"); state = Field(plane, "_state");
            reply = plane.GetMethod("CGHDBAAKDDB", Fields, null, new[] { typeof(string) }, null);
            planeGame=Field(plane,"BMCCOLLPALC"); commandWindow=Field(planeGame.FieldType,"cmdwin"); callsign=Field(plane,"_name");
            sayName=plane.GetMethod("get__sayname",Fields,null,Type.EmptyTypes,null);
            var crossStop = plane.GetMethods(Fields).Single(m => m.Name == "DFEHFKAFIMI" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == cross);
            var knotStop = plane.GetMethods(Fields).Single(m => m.Name == "OCGFCEPBKMF" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == knot);
            var taxi = plane.GetMethod("AGHOOFEDHAC", Fields, null, Type.EmptyTypes, null);
            var send = assembly.GetType("CommandWin", true).GetMethod("SendMsg", Fields, null, new[] { typeof(string), assembly.GetType("MFECMBFOJCM+LEHLCPMEDNK", true), typeof(string), plane, assembly.GetType("MFECMBFOJCM+PLOKIFJGPLC", true), typeof(string) }, null);
            if (reply == null || reply.ReturnType != typeof(void) || taxi == null || send == null || sayName==null) throw new NotSupportedException("Holding-report signatures changed.");
            reply=send;
            current = this;
            try
            {
                var prefix = new HarmonyMethod(typeof(HoldingReportsModule), nameof(StopPrefix));
                harmony.Patch(crossStop, prefix: prefix, postfix: new HarmonyMethod(typeof(HoldingReportsModule), nameof(CrossPostfix)));
                harmony.Patch(knotStop, prefix: prefix, postfix: new HarmonyMethod(typeof(HoldingReportsModule), nameof(KnotPostfix)));
                harmony.Patch(taxi, postfix: new HarmonyMethod(typeof(HoldingReportsModule), nameof(TaxiPostfix)));
                harmony.Patch(send, transpiler: new HarmonyMethod(typeof(HoldingReportsModule), nameof(RadioTranspiler)));
                context.Log("Pilot holding reports 0.4.8 enabled; tow-owned aircraft use their operations module. AssignedRunway=" + options.AssignedRunway + "; Crossings=" + options.CrossingRunway + "; Taxiways=" + options.Taxiway);
            }
            catch { Dispose(); throw; }
        }
        private static FieldInfo Field(Type type, string name) => type.GetField(name, Fields) ?? throw new MissingFieldException(type.FullName, name);
        private HoldingReportTracker Track(object plane)
        {
            return trackers.GetValue(plane, p => { aircraft.Add(new WeakReference(p)); return new HoldingReportTracker(); });
        }
        private static void StopPrefix(object __instance, out bool __state)
        {
            __state = true;
            try { if (current != null) __state = (bool)current.hold.GetValue(__instance); }
            catch (Exception error) { current?.LogError(error); }
        }
        private bool Departure(object plane)
        {
            var value=action.GetValue(plane).ToString();
            return value=="ACTION_OUTGOING" || value=="ACTION_TERMINALSTART";
        }
        private void Observe(object plane, object target, string name, string message, HoldingReportKind kind)
        {
            var tracker=Track(plane);
            if (HoldingReportOwnership.IsOwned(plane)||!options.Enabled(kind)) { tracker.Cancel(); return; }
            tracker.Observe(target,name,message,clock.Elapsed.TotalSeconds);
        }
        private static void CrossPostfix(object __instance, object __0, bool __state)
        {
            var self = current;
            if (self == null || __state || __0 == null) return;
            try
            {
                if (!(bool)self.hold.GetValue(__instance) || !ReferenceEquals(self.previousCross.GetValue(__instance), __0)) return;
                var cross = self.crossRoad.GetValue(__0); var from = self.thisRoad.GetValue(__0);
                if (cross == null) return;
                var name = (string)self.roadName.GetValue(cross);
                if (string.IsNullOrWhiteSpace(name)) return;
                var runway = self.roadType.GetValue(cross).ToString() == "RT_RUNWAY";
                var location = from == null ? null : (string)self.roadName.GetValue(from);
                var message = "Holding short of " + (runway ? "runway " : "taxiway ") + name;
                if (!string.IsNullOrWhiteSpace(location)) message += " at " + location;
                var assigned = self.assignedRunway.GetValue(__instance);
                var kind = HoldingReportOptions.Classify(runway, self.Departure(__instance), false,
                    ReferenceEquals(assigned,cross) && self.state.GetValue(__instance).ToString()=="STATE_TO_RUNWAY");
                self.Observe(__instance, __0, name, message + ".", kind);
            }
            catch (Exception error) { self.LogError(error); }
        }
        private static void KnotPostfix(object __instance, object __0, bool __state)
        {
            var self = current;
            if (self == null || __state || __0 == null) return;
            try
            {
                if (!(bool)self.hold.GetValue(__instance)) return;
                var name = (string)self.knotName.GetValue(__0);
                self.Observe(__instance, __0, name, "Holding at " + name + ".", HoldingReportKind.Taxiway);
            }
            catch (Exception error) { self.LogError(error); }
        }
        private static void TaxiPostfix(object __instance)
        {
            var self = current;
            if (self == null) return;
            try
            {
                if (!(bool)self.gateHold.GetValue(__instance)) return;
                var gate = self.previousGate.GetValue(__instance);
                if (gate == null) return;
                var roads = ((IEnumerable)self.gateRoads.GetValue(gate)).Cast<object>().Where(r => r != null && self.roadType.GetValue(r).ToString() == "RT_RUNWAY").ToArray();
                var name = RunwayNames.Label(roads.Select(r => (string)self.roadName.GetValue(r)));
                if (name == null) return;
                var targetName=name;
                var kind = HoldingReportOptions.Classify(true, self.Departure(__instance), ReferenceEquals(self.assignedGate.GetValue(__instance),gate), false);
                var assigned=self.assignedRunway.GetValue(__instance);
                if(kind==HoldingReportKind.AssignedRunway && assigned!=null && roads.Any(r=>ReferenceEquals(r,assigned)))
                    name=(string)self.roadName.GetValue(assigned);
                self.Observe(__instance, gate, targetName, "Holding short of runway " + name + ".", kind);
            }
            catch (Exception error) { self.LogError(error); }
        }
        private static IEnumerable<CodeInstruction> RadioTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var history = code.Select(i => i.operand as MethodInfo).FirstOrDefault(m => m != null && m.DeclaringType.FullName == "CommandWin" && m.Name == "AddHistory");
            return AcceptedContactInstrumentation.InjectRadio(code, history, typeof(HoldingReportsModule).GetMethod(nameof(RadioPosted)));
        }
        public static void RadioPosted(object plane, string message, int kind)
        {
            var self = current;
            if (self == null || plane == null || kind != 4 || message == null) return;
            try
            {
                var text = message.ToLowerInvariant();
                if (text.Contains("holding short") || text.Contains("holding at") || text.Contains("request crossing runway") || text.Contains("ready for departure"))
                    self.Track(plane).NativeReport(message, self.clock.Elapsed.TotalSeconds);
            }
            catch (Exception error) { self.LogError(error); }
        }
        public void Tick()
        {
            var now = clock.Elapsed.TotalSeconds;
            if (now < nextScan) return;
            nextScan = now + 0.25;
            for (var i = aircraft.Count - 1; i >= 0; i--)
            {
                var plane = aircraft[i].Target;
                if (plane == null) { aircraft.RemoveAt(i); continue; }
                HoldingReportTracker tracker;
                if (!trackers.TryGetValue(plane, out tracker)) continue;
                if(HoldingReportOwnership.IsOwned(plane)){tracker.Cancel();continue;}
                var holding = (bool)hold.GetValue(plane) || (bool)gateHold.GetValue(plane);
                var eligible = (bool)active.GetValue(plane) && !(bool)deleted.GetValue(plane) && (float)altitude.GetValue(plane) <= 0;
                if (!tracker.Ready(holding, eligible, (float)speed.GetValue(plane), now) || now < nextReport) continue;
                // Commit before posting: our own radio observer must not schedule a second call.
                tracker.Sent(); nextReport = now + 2;
                var parameters=reply.GetParameters();
                var window=commandWindow.GetValue(planeGame.GetValue(plane));
                var spoken=HoldingSpeech.WithCallsign(tracker.Message,(string)sayName.Invoke(plane,null));
                reply.Invoke(window,new object[]{(string)callsign.GetValue(plane)+": "+tracker.Message,Enum.ToObject(parameters[1].ParameterType,4),spoken,plane,Enum.ToObject(parameters[4].ParameterType,3),null});
                context.Log("Pilot holding report: " + tracker.Message);
            }
        }
        private void LogError(Exception error) { try { context.Log("Holding-report observer error: " + error.Message); } catch { } }
        public void Dispose() { harmony.UnpatchSelf(); aircraft.Clear(); if (ReferenceEquals(current, this)) current = null; }
    }
}


