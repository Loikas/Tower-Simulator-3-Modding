using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using HarmonyLib;
using TS3Modding.API;
using TS3Modding.Adapters;

namespace TS3Modding.PilotReadiness
{
    // The only game-specific boundary of this optional mod. No game DLL is rewritten.
    internal sealed class NativeReadinessModule : ITS3Module
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static NativeReadinessModule current;
        private readonly Harmony harmony = new Harmony("nl.ts3modding.pilotreadiness");
        private readonly ConditionalWeakTable<object, Readiness> readiness = new ConditionalWeakTable<object, Readiness>();
        private readonly bool guardPush;
        private FieldInfo commandField, responsesField, aircraftField, actionField, stateField, deletedField, requestingPushField;
        private ConstructorInfo responseConstructor;
        private IFrameworkContext context;
        private sealed class Readiness { public bool Push; }
        public NativeReadinessModule(bool push) { guardPush = push; }
        public string Id => "nl.ts3modding.pilotreadiness.module";
        public void Initialize(IFrameworkContext context)
        {
            this.context = context;
            if (current != null) throw new InvalidOperationException("Readiness module already registered.");
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp");
            var plane = assembly.GetType("Airplane", true);
            var parser = assembly.GetType("DHOPLOEENOK", true);
            var command = assembly.GetType("DHOPLOEENOK+MCPCADKMMAI", true);
            var response = assembly.GetType("MFECMBFOJCM+NHPCBBLNCHK", true);
            var kind = assembly.GetType("MFECMBFOJCM+LEHLCPMEDNK", true);
            var priority = assembly.GetType("MFECMBFOJCM+PLOKIFJGPLC", true);
            commandField = RequiredField(command, "command");
            responsesField = RequiredField(parser, "OCBFAJNOCDK");
            aircraftField = RequiredField(parser, "BNMGGAOAPNE");
            actionField = RequiredField(plane, "_action"); stateField = RequiredField(plane, "_state");
            deletedField = RequiredField(plane, "_deleted");
            requestingPushField = RequiredField(plane, "_requestingpush");
            responseConstructor = response.GetConstructor(Flags, null, new[] { kind, priority, typeof(string), typeof(string), plane }, null);
            var validate = parser.GetMethod("MKECMJOHINE", Flags, null, new[] { plane, typeof(List<>).MakeGenericType(command) }, null);
            var send = assembly.GetType("CommandWin", true).GetMethod("SendMsg", Flags, null,
                new[] { typeof(string), kind, typeof(string), plane, priority, typeof(string) }, null);
            var setState = plane.GetMethod("SetState", Flags, null, new[] { stateField.FieldType }, null);
            if (responseConstructor == null || validate == null || validate.ReturnType != typeof(bool) || send == null || setState == null)
                throw new NotSupportedException("Readiness hook signatures changed.");
            current = this;
            try
            {
                harmony.Patch(validate, prefix: new HarmonyMethod(typeof(NativeReadinessModule), nameof(ValidatePrefix)));
                harmony.Patch(send, transpiler: new HarmonyMethod(typeof(NativeReadinessModule), nameof(RadioTranspiler)));
                harmony.Patch(setState, postfix: new HarmonyMethod(typeof(NativeReadinessModule), nameof(StatePostfix)));
                harmony.Patch(plane.GetMethod("WaitForStart", Flags, null, Type.EmptyTypes, null), postfix: new HarmonyMethod(typeof(NativeReadinessModule), nameof(PushRequestPostfix)));
                context.Log("Pilot readiness 0.4.5 enabled. PushRequest=" + guardPush);
            }
            catch { Dispose(); throw; }
        }
        private static FieldInfo RequiredField(Type type, string name) => type.GetField(name, Flags) ?? throw new MissingFieldException(type.FullName, name);
        private static IEnumerable<CodeInstruction> RadioTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var history = code.Select(i => i.operand as MethodInfo).FirstOrDefault(m => m != null && m.DeclaringType.FullName == "CommandWin" && m.Name == "AddHistory");
            return AcceptedContactInstrumentation.InjectRadio(code, history, typeof(NativeReadinessModule).GetMethod(nameof(RadioPosted), BindingFlags.Static | BindingFlags.Public));
        }
        public static void RadioPosted(object aircraft, string message, int messageKind)
        {
            var self = current;
            if (self == null || aircraft == null || messageKind != 4 || message == null) return;
            try
            {
                if ((bool)self.deletedField.GetValue(aircraft)) return;
                var status = self.readiness.GetValue(aircraft, _ => new Readiness());
                var action = self.actionField.GetValue(aircraft).ToString();
                if ((action == "ACTION_OUTGOING" || action == "ACTION_TERMINALSTART") &&
                    (message.IndexOf("requesting push and start.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     message.IndexOf("ready to start.", StringComparison.OrdinalIgnoreCase) >= 0)) status.Push = true;
            }
            catch (Exception error) { self.LogError(error); }
        }
        private static void PushRequestPostfix(object __instance)
        {
            var self = current;
            if (self == null) return;
            try
            {
                // WaitForStart sets this flag immediately before submitting the real request.
                // Loading and speech readiness must not erase that operational event.
                if ((bool)self.requestingPushField.GetValue(__instance) && !(bool)self.deletedField.GetValue(__instance))
                {
                    self.readiness.GetValue(__instance, _ => new Readiness()).Push = true;
                    self.context.Log("Pilot push/start request recorded from native WaitForStart.");
                }
            }
            catch (Exception error) { self.LogError(error); }
        }
        private static void StatePostfix(object __instance)
        {
            var self = current;
            if (self == null) return;
            try
            {
                Readiness status;
                if (!self.readiness.TryGetValue(__instance, out status)) return;
                var state = self.stateField.GetValue(__instance).ToString();
                // Push evidence belongs to one ground leg.
                if (state == "STATE_WAIT_FOR_SPAWN" || state == "STATE_WAIT_FOR_RESPAWN" || state == "STATE_NONE") status.Push = false;
            }
            catch (Exception error) { self.LogError(error); }
        }
        private static bool ValidatePrefix(object __instance, object __0, object __1, ref bool __result)
        {
            var self = current;
            if (self == null || __0 == null) return true;
            try
            {
                var commands = ((IEnumerable)__1).Cast<object>().Select(c => self.commandField.GetValue(c).ToString()).ToArray();
                Readiness status;
                self.readiness.TryGetValue(__0, out status);
                var refusal = ReadinessPolicy.Refusal(commands, status != null && status.Push, self.guardPush);
                if (refusal == null) return true;
                // Construct the entire response before touching the parser. Returning false skips
                // the caller's execution AND success-readback loops for the whole parsed batch.
                var args = self.responseConstructor.GetParameters();
                var reply = self.responseConstructor.Invoke(new[] { Enum.ToObject(args[0].ParameterType, 4), Enum.ToObject(args[1].ParameterType, 3), refusal, refusal, __0 });
                var responses = (IList)Activator.CreateInstance(self.responsesField.FieldType);
                responses.Add(reply);
                self.aircraftField.SetValue(__instance, __0);
                self.responsesField.SetValue(__instance, responses);
                __result = false;
                try { self.context.Log("Pilot readiness refused [" + string.Join(",", commands) + "]: " + refusal); } catch { }
                return false;
            }
            catch (Exception error) { self.LogError(error); return true; }
        }
        private void LogError(Exception error) { try { context.Log("Pilot readiness hook failed; native handling retained: " + error.Message); } catch { } }
        public void Tick() { }
        public void Dispose() { harmony.UnpatchSelf(); if (ReferenceEquals(current, this)) current = null; }
    }
}





