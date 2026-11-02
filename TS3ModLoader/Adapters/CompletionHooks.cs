using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TS3Modding.Adapters
{
    // Injects observation only at the accepted RunContact assignment, never changes game results.
    internal sealed class CompletionHooks : IDisposable
    {
        private static MonoAircraftAdapter observer;
        private static Action<string> logger;
        private static FieldInfo ownerField;
        private static FieldInfo departureAltitudeField;
        private static FieldInfo flyAwayDepartureField;
        private readonly Harmony harmony = new Harmony("nl.ts3modding.loader.completion");
        public CompletionHooks(Assembly assembly, MonoAircraftAdapter adapter, Action<string> log)
        {
            if (observer != null) throw new InvalidOperationException("Completion observer already installed.");
            var airplane = assembly.GetType("Airplane", true);
            var command = assembly.GetType("DHOPLOEENOK+MCPCADKMMAI", true);
            var contact = airplane.GetMethod("RunContact", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { command, typeof(List<>).MakeGenericType(command) }, null);
            var setState = airplane.GetMethod("SetState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { assembly.GetType("Airplane+KLNOGMBLABM", true) }, null);
            // CONTACT_DEPARTURE has a dedicated dispatch target, separate from generic CONTACT.
            // Name/signature and RunCommand dispatch are verified against the pinned assembly profile.
            var departureContact = airplane.GetMethod("CIAIHAMIKHO", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { command, typeof(List<>).MakeGenericType(command) }, null);
            if (contact == null || departureContact == null || setState == null || contact.ReturnType != typeof(void) ||
                departureContact.ReturnType != typeof(void) || setState.ReturnType != typeof(void))
                throw new NotSupportedException("Completion hook signatures changed.");
            var sendMsg = assembly.GetType("CommandWin", true).GetMethod("SendMsg", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string), assembly.GetType("MFECMBFOJCM+LEHLCPMEDNK", true), typeof(string), airplane,
                    assembly.GetType("MFECMBFOJCM+PLOKIFJGPLC", true), typeof(string) }, null);
            if (sendMsg == null || sendMsg.ReturnType != typeof(int)) throw new NotSupportedException("Aircraft radio report signature changed.");
            observer = adapter; logger = log;
            ownerField = airplane.GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            departureAltitudeField = airplane.GetField("_departureautoalt", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            flyAwayDepartureField=airplane.GetField("_fly_away_departure",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
            var flightUpdate = airplane.GetMethod("ACIMHIKGGKF", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
            try
            {
                if (flightUpdate == null || flightUpdate.ReturnType != typeof(void)) throw new NotSupportedException("Scheduled departure update changed.");
                harmony.Patch(flightUpdate, transpiler: new HarmonyMethod(typeof(CompletionHooks), nameof(DeferredContactTranspiler)));
                harmony.Patch(contact, transpiler: new HarmonyMethod(typeof(CompletionHooks), nameof(ContactTranspiler)));
                harmony.Patch(departureContact, transpiler: new HarmonyMethod(typeof(CompletionHooks), nameof(ContactTranspiler)));
                harmony.Patch(setState, postfix: new HarmonyMethod(typeof(CompletionHooks), nameof(StatePostfix)));
                harmony.Patch(sendMsg, transpiler: new HarmonyMethod(typeof(CompletionHooks), nameof(RadioTranspiler)));
                log("Observational hooks installed: scheduled altitude departure, missed-departure automatic handoff, generic contact, dedicated contact-departure, parking and posted final reports.");
            }
            catch { Dispose(); throw; }
        }
        private static IEnumerable<CodeInstruction> ContactTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return AcceptedContactInstrumentation.Inject(instructions, ownerField, typeof(CompletionHooks).GetMethod(nameof(ContactAssigned)));
        }
        private static IEnumerable<CodeInstruction> DeferredContactTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var scheduled=AcceptedContactInstrumentation.InjectDeferred(instructions, ownerField, departureAltitudeField, typeof(CompletionHooks).GetMethod(nameof(ContactAssigned)));
            return AcceptedContactInstrumentation.InjectMissedDeparture(scheduled,ownerField,flyAwayDepartureField,typeof(CompletionHooks).GetMethod(nameof(MissedDepartureAssigned)));
        }
        public static void MissedDepartureAssigned(object instance)
        {
            try{observer?.CaptureCompletion(instance,false,true);}
            catch(Exception error){try{logger?.Invoke("Automatic departure observer failed: "+error.Message);}catch{}}
        }
        public static void ContactAssigned(object instance)
        {
            try { observer?.CaptureCompletion(instance, true); }
            catch (Exception error) { try { logger?.Invoke("Contact observer failed: " + error.Message); } catch { } }
        }
        private static IEnumerable<CodeInstruction> RadioTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var history = code.Select(i => i.operand as MethodInfo).FirstOrDefault(m => m != null && m.DeclaringType.FullName == "CommandWin" && m.Name == "AddHistory");
            return AcceptedContactInstrumentation.InjectRadio(code, history, typeof(CompletionHooks).GetMethod(nameof(RadioPosted)));
        }
        public static void RadioPosted(object aircraft, string message, int messageKind)
        {
            try { observer?.CaptureFinalReport(aircraft, message, messageKind); }
            catch (Exception error) { try { logger?.Invoke("Final report observer failed: " + error.Message); } catch { } }
        }
        private static void StatePostfix(object __instance)
        {
            try { observer?.StateChanged(__instance); }
            catch (Exception error) { try { logger?.Invoke("Parking observer failed: " + error.Message); } catch { } }
        }
        public void Dispose()
        {
            try { harmony.UnpatchSelf(); } finally { observer = null; logger = null; ownerField = null; departureAltitudeField = null; flyAwayDepartureField=null; }
        }
    }
}
