using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TS3Modding.API;

namespace TS3Modding.PilotReadiness
{
    // Buffers native pilot SendMsg calls BEFORE both speech submission and history
    // insertion, so replay gets one consistent speech/history ID and no duplicate row.
    internal sealed class StartupSpeechModule : ITS3Module
    {
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static StartupSpeechModule current;
        private readonly Harmony harmony = new Harmony("nl.ts3modding.pilotreadiness.startupspeech");
        private readonly StartupSpeechQueue<Pending> queue = new StartupSpeechQueue<Pending>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private FieldInfo coreInstance, ttsReady, towerInstance, deleted, aircraftState;
        private MethodInfo send;
        private IFrameworkContext context;
        private bool replay;
        private double nextSend;
        private sealed class Pending { public object Sender; public object[] Args; }
        public string Id => "nl.ts3modding.pilotreadiness.startupspeech";
        public void Initialize(IFrameworkContext context)
        {
            this.context = context;
            if (current != null) throw new InvalidOperationException("Startup speech already registered.");
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
            var core = assembly.GetType("HBNFAMGCNAF", true);
            coreInstance = core.GetField("instance", Fields); ttsReady = core.GetField("readytts", Fields);
            towerInstance = assembly.GetType("TowerSpeak", true).GetField("instance", Fields);
            deleted = assembly.GetType("Airplane", true).GetField("_deleted", Fields);
            aircraftState = assembly.GetType("Airplane", true).GetField("_state", Fields);
            send = assembly.GetType("CommandWin", true).GetMethods(Fields).Single(m => m.Name == "SendMsg" && m.GetParameters().Length == 6);
            if (coreInstance == null || ttsReady == null || towerInstance == null || deleted == null) throw new NotSupportedException("Startup speech profile changed.");
            current = this;
            try
            {
                harmony.Patch(send, prefix: new HarmonyMethod(typeof(StartupSpeechModule), nameof(SendPrefix)));
                context.Log("Startup pilot speech buffering enabled; waiting messages retain native speech/history delivery.");
            }
            catch { Dispose(); throw; }
        }
        private bool Ready()
        {
            var core = coreInstance.GetValue(null);
            // No external speech core: retain native handling (including disabled speech).
            return core == null || (bool)ttsReady.GetValue(core);
        }
        private static bool SendPrefix(object __instance, object[] __args, ref int __result)
        {
            var self = current;
            if (self == null || self.replay) return true;
            try
            {
                if (Convert.ToInt32(__args[1]) != 4 || __args[3] == null || string.IsNullOrWhiteSpace(__args[2] as string) ||
                    self.towerInstance.GetValue(null) == null || self.Ready()) return true;
                if (!self.queue.Enqueue(new Pending { Sender = __instance, Args = (object[])__args.Clone() }, self.clock.Elapsed.TotalSeconds)) return true;
                __result = -1;
                try { self.context.Log("Pilot message deferred until TTS ready: " + __args[0]); } catch { }
                return false;
            }
            catch (Exception error) { try { self.context.Log("Startup speech buffering failed: " + error.Message); } catch { } return true; }
        }
        public void Tick()
        {
            var now = clock.Elapsed.TotalSeconds;
            if (now < nextSend) return;
            Pending pending;
            if (!queue.TryTake(Ready(), now, out pending)) return;
            nextSend = now + 0.5;
            if ((bool)deleted.GetValue(pending.Args[3])) return;
            var text = pending.Args[0] as string;
            if (text != null && (text.IndexOf("requesting push and start", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("ready to start.", StringComparison.OrdinalIgnoreCase) >= 0) &&
                aircraftState.GetValue(pending.Args[3]).ToString() != "STATE_WAITING")
            { context.Log("Discarded obsolete buffered push/start request after aircraft left waiting state."); return; }
            replay = true;
            try { send.Invoke(pending.Sender, pending.Args); context.Log("Startup pilot message released to native speech/history: " + pending.Args[0]); }
            finally { replay = false; }
        }
        public void Dispose() { harmony.UnpatchSelf(); queue.Clear(); if (ReferenceEquals(current, this)) current = null; }
    }
}


