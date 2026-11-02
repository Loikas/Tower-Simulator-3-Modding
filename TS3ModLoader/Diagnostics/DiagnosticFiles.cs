using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using TS3Modding.API;
using System.Collections.Generic;
using System.Globalization;

namespace TS3Modding.Diagnostics
{
    internal sealed class DiagnosticFiles
    {
        private readonly string directory;
        public DiagnosticFiles(string directory) { this.directory = directory; Directory.CreateDirectory(directory); }
        private int performanceWriter;
        public void Performance(string snapshot)
        {
            if(System.Threading.Interlocked.CompareExchange(ref performanceWriter,1,0)!=0)return;
            System.Threading.ThreadPool.QueueUserWorkItem(_=>{
                try { WriteAtomic("performance-latest.txt",snapshot); }
                catch { /* Diagnostics must not interrupt the game. */ }
                finally { System.Threading.Interlocked.Exchange(ref performanceWriter,0); }
            });
        }
        public void Help(ModHelpPage page)
        {
            var validated=ModHelpPage.Copy(page);
            var helpDirectory=Path.Combine(directory,"help");Directory.CreateDirectory(helpDirectory);
            var path=Path.Combine(helpDirectory,validated.Id+".json");var temporary=path+".tmp";
            File.WriteAllText(temporary,JsonUtility.ToJson(validated),new UTF8Encoding(false));
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
        public void Append(string message)
        {
            try { File.AppendAllText(Path.Combine(directory, "framework-lifecycle.log"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
            catch { /* Logging must not interrupt Unity updates. */ }
        }
        public void MovementAudit(string message)
        {
            // Fail before the native move if an attempt cannot be recorded.
            File.AppendAllText(Path.Combine(directory, "strip-moves.log"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }
        public void Status(string value)
        {
            WriteAtomic("framework-status.txt", "UTC=" + DateTime.UtcNow.ToString("O") + Environment.NewLine + value);
        }
        private void WriteAtomic(string name, string content)
        {
            var destination = Path.Combine(directory, name);
            var temporary = destination + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        public void ExportAirport(AirportObservation observation, bool available)
        {
            var rows = new StringBuilder("ObservedUtc,AdapterAvailable,AircraftId,Callsign,ObjectName,Scene,NetworkIndex,State,Action,SimulationActive,SceneActive,ReadyToTaxi,MarkedDeleted,AssignedJetwayGate,X,Y,Z,AssignedRunway,FinalReportedRunway\n");
            foreach (var a in observation.Aircraft)
                Row(rows, observation.CapturedUtc.ToString("O"), available, a.Id, a.Details.Callsign, a.ObjectName, a.Scene, a.Details.NetworkIndex,
                    a.Details.RawState, a.Details.RawAction, a.Details.SimulationActive, a.Active, a.ReadyToTaxi, a.MarkedDeleted, a.Details.AssignedGate, a.X, a.Y, a.Z, a.Details.AssignedRunway, a.Details.FinalReportedRunway);
            WriteAtomic("aircraft-observations.csv", rows.ToString());
            rows = new StringBuilder("ObservedUtc,AdapterAvailable,InstanceId,Scene,GateName,State,LinkedAircraftId\n");
            foreach (var g in observation.Gates) Row(rows, observation.CapturedUtc.ToString("O"), available, g.InstanceId, g.Scene, g.Name, g.RawState, g.LinkedAircraft);
            WriteAtomic("gate-observations.csv", rows.ToString());
            rows = new StringBuilder("ObservedUtc,AdapterAvailable,InstanceId,Scene,Callsign,NetworkIndex,CurrentBlock,Departure,InBin,AssignedRunway,BoardInstanceId\n");
            foreach (var s in observation.Strips) Row(rows, observation.CapturedUtc.ToString("O"), available, s.InstanceId, s.Scene, s.Callsign, s.NetworkIndex, s.Block, s.Departure, s.InBin, s.AssignedRunway, s.BoardInstanceId);
            WriteAtomic("strip-observations.csv", rows.ToString());
            rows = new StringBuilder("AircraftId,Callsign,Scene,NetworkIndex,CompletionKind,ConfirmedUtc\n");
            foreach (var completion in observation.Completions) Row(rows, completion.Aircraft, completion.Callsign, completion.Scene,
                completion.NetworkIndex, completion.Kind, completion.ConfirmedUtc.ToString("O"));
            WriteAtomic("completion-evidence.csv", rows.ToString());
        }
        private static void Row(StringBuilder target, params object[] values)
        {
            target.AppendLine(string.Join(",", values.Select(value =>
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                if (value is string && text.Length > 0 && "=+-@".IndexOf(text[0]) >= 0) text = "'" + text;
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            })));
        }
        public static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        public void ExportReconnaissance()
        {
            var report = new StringBuilder();
            report.AppendLine("TS3 runtime reconnaissance " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("Unity=" + Application.unityVersion + " CLR=" + Environment.Version);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
            {
                report.AppendLine("ASSEMBLY " + assembly.FullName);
                var name = assembly.GetName().Name;
                if (name != "Assembly-CSharp" && name != "Assembly-CSharp-firstpass" && name != "FTL") continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException error)
                {
                    types = error.Types.Where(t => t != null).ToArray();
                    foreach (var problem in error.LoaderExceptions) report.AppendLine("LOAD ERROR " + problem.Message);
                }
                foreach (var type in types.OrderBy(t => t.FullName))
                {
                    report.AppendLine("TYPE " + type.FullName + " : " + type.BaseType);
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    if (!Relevant(type.FullName)) continue;
                    foreach (var field in type.GetFields(flags).OrderBy(f => f.Name)) report.AppendLine("  FIELD " + field.FieldType + " " + field.Name);
                    foreach (var method in type.GetMethods(flags).OrderBy(m => m.Name)) report.AppendLine("  METHOD " + method);
                }
            }
            File.WriteAllText(Path.Combine(directory, "runtime-reconnaissance.txt"), report.ToString());
        }
        private static bool Relevant(string value) =>
            new[] { "aircraft", "airplane", "flight", "strip", "gate", "park", "runway", "taxi", "clearance", "pushback", "depart", "arrival", "plane" }
                .Any(word => value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}


