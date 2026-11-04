using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using TS3Modding.API;
using UnityEngine;

namespace TS3Modding;

internal sealed class NativeRadioCommands : IRadioCommandProviderService, IDisposable
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static NativeRadioCommands current;
    readonly Harmony harmony = new("nl.ts3modding.framework.commands");
    readonly int thread = Thread.CurrentThread.ManagedThreadId;
    readonly RadioCommandRegistry registrations = new();
    volatile RadioCommand[] commands = [];
    volatile RadioCommandProvider[] providers = [];
    readonly Queue<Pending> queue = new();
    readonly FieldInfo instance, name, schedule, active, contact, departed, deleted, airplanes, commandWindow;
    readonly MethodInfo sayName;
    readonly Action<string> log;
    readonly Type helperType; readonly FieldInfo grammar, targets; readonly MethodInfo fromJson, toJson; readonly string grammarMessage;
    bool disposed;
    volatile Pending[] addresses = [];
    long nextAddresses;
    Pending last;

    sealed class Pending
    {
        internal object Plane, Schedule, Game;
        internal string Callsign, Text;
        internal RadioCommand Command;
        internal RadioCommandProvider Provider;
        internal long Time;
        internal Pending[] Addresses;
    }

    internal NativeRadioCommands(Assembly assembly, Action<string> log, bool patch = true)
    {
        this.log = log; var game = assembly.GetType("Game", true); var plane = assembly.GetType("Airplane", true);
        instance = game.GetField("instance", F); airplanes = game.GetField("airplanes", F); commandWindow = game.GetField("cmdwin", F);
        name = plane.GetField("_name", F); schedule = plane.GetField("_timetable", F); active = plane.GetField("_active", F);
        contact = plane.GetField("_trycontact", F); departed = plane.GetField("_fly_away_departure", F); deleted = plane.GetField("_deleted", F); sayName = plane.GetMethod("get__sayname", F);
        if (name == null || schedule == null || instance == null || active == null || contact == null || departed == null || deleted == null || sayName == null || airplanes == null || commandWindow == null) throw new NotSupportedException("Command bridge contract changed.");
        if (current != null) throw new InvalidOperationException("Command bridge already active."); current = this;
        try
        {
            if (patch)
            {
                helperType = assembly.GetType("TCP+RespondRecogHelper", true); grammar = helperType.GetField("commands", F); targets = helperType.GetField("targets", F);
                var json = Type.GetType("UnityEngine.JsonUtility, UnityEngine.JSONSerializeModule", true);
                fromJson = json.GetMethod("FromJson", new[] { typeof(string), typeof(Type) }); toJson = json.GetMethod("ToJson", new[] { typeof(object) });
                grammarMessage = (string)assembly.GetType("TCP", true).GetField("CMD_RESPOND_RECOG", F).GetValue(null);
                if (grammar == null || targets == null || fromJson == null || toJson == null) throw new NotSupportedException("Strict grammar contract changed.");
                harmony.Patch(plane.GetMethod("Command", F, null, new[] { typeof(string) }, null), prefix: new HarmonyMethod(typeof(NativeRadioCommands), nameof(Addressed)) { priority = Priority.First });
                harmony.Patch(assembly.GetType("HBNFAMGCNAF", true).GetMethod("HKJADHNKDJK", F, null, new[] { typeof(string), typeof(bool) }, null), prefix: new HarmonyMethod(typeof(NativeRadioCommands), nameof(Speech)));
                harmony.Patch(assembly.GetType("TCPBridge", true).GetMethod("PCGMLBFLNGP", F, null, new[] { assembly.GetType("TCP+Connection", true), typeof(string), typeof(string), typeof(int) }, null), prefix: new HarmonyMethod(typeof(NativeRadioCommands), nameof(Grammar)));
            }
            RadioCommands.Attach(this);
        }
        catch { Dispose(); throw; }
    }

    void Require()
    {
        if (Thread.CurrentThread.ManagedThreadId != thread)
            throw new InvalidOperationException("Commands require the game thread.");

        if (disposed)
            throw new ObjectDisposedException(nameof(NativeRadioCommands));
    }

    public IDisposable Register(RadioCommand command)
    {
        Require();
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        if (providers.Any(p => p.Id == command.Id))
            throw new InvalidOperationException("Command ID already registered.");

        registrations.Add(command);
        commands = registrations.Snapshot;
        return new Lease(() =>
        {
            if (disposed)
                return;
            Require();
            registrations.Remove(command.Id);
            commands = registrations.Snapshot;
            ClearUnusedAddresses();
        });
    }

    public IDisposable RegisterProvider(RadioCommandProvider provider)
    {
        Require();
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        if (providers.Length >= 16 || providers.Any(p => p.Id == provider.Id) || commands.Any(c => c.Id == provider.Id))
            throw new InvalidOperationException("Provider ID already registered or limit reached.");

        providers = [.. providers, provider];
        nextAddresses = 0;
        return new Lease(() =>
        {
            if (disposed)
                return;
            Require();
            providers = [.. providers.Where(p => !ReferenceEquals(p, provider))];
            ClearUnusedAddresses();
        });
    }

    void ClearUnusedAddresses()
    {
        if (commands.Length == 0 && providers.Length == 0)
            addresses = [];
    }

    sealed class Lease : IDisposable
    {
        Action dispose;
        internal Lease(Action action)
        {
            dispose = action;
        }

        public void Dispose()
        {
            var action = dispose;
            if (action == null)
                return;
            action();
            dispose = null;
        }
    }

    static bool SafeAccept(RadioCommandProvider provider, object plane, string text)
    {
        try
        {
            return provider.Accepts(new RadioCommandAddress(plane, text));
        }
        catch
        {
            return false;
        }
    }

    static string SafeCapture(RadioCommandProvider provider, string text)
    {
        try
        {
            var captured = provider.CaptureSpeech?.Invoke(text);
            return !string.IsNullOrWhiteSpace(captured) && captured.Length <= 512 ? captured : null;
        }
        catch
        {
            return null;
        }
    }

    static bool Addressed(object __instance, string __0, ref bool __result)
    {
        var self = current;
        if (self == null || __0 == null || __0.Length > 512 || (self.commands.Length == 0 && self.providers.Length == 0))
            return true;
        var text = RadioCommand.Normalize(__0);
        var fixedMatches = self.commands.Where(c => c.Phrases.Contains(text)).ToArray();
        var parserMatches = self.providers.Where(p => SafeAccept(p, __instance, __0)).ToArray();
        if (fixedMatches.Length + parserMatches.Length == 0)
            return true;
        // Ambiguous extensions never execute either handler or fall through into native taxi parsing.
        if (fixedMatches.Length + parserMatches.Length != 1)
        {
            __result = false;
            return false;
        }

        var provider = parserMatches.FirstOrDefault();
        if ((provider == null || provider.RequireRadioContact) && !self.CanReceive(__instance))
        {
            __result = false;
            return false;
        }
        self.Enqueue(new Pending { Plane = __instance, Schedule = self.schedule.GetValue(__instance), Callsign = (string)self.name.GetValue(__instance), Text = __0, Command = fixedMatches.FirstOrDefault(), Provider = provider });
        __result = true;
        return false;
    }

    bool CanReceive(object plane) => (bool)active.GetValue(plane) && (bool)contact.GetValue(plane) && !(bool)departed.GetValue(plane);

    static bool Speech(string __0, bool __1)
    {
        var self = current;
        if (self == null || __0 == null || __0.Length > 512 || (self.commands.Length == 0 && self.providers.Length == 0))
            return true;

        var text = RadioCommand.Normalize(__0);
        var fixedMatches = self.commands.Where(c => c.Phrases.Any(p => text.EndsWith(" " + p, StringComparison.Ordinal))).ToArray();
        var parserMatches = self.providers.Select(p => new { Provider = p, Text = SafeCapture(p, __0) }).Where(p => p.Text != null).ToArray();
        var count = fixedMatches.Length + parserMatches.Length;

        if (count == 0)
            return true;
        if (count != 1)
            return false;

        if (!__1)
            self.Enqueue(new Pending { Text = parserMatches.Length == 1 ? parserMatches[0].Text : text, Command = fixedMatches.FirstOrDefault(), Provider = parserMatches.FirstOrDefault()?.Provider, Addresses = self.addresses });
        return false;
    }

    void Enqueue(Pending pending)
    {
        pending.Game = instance.GetValue(null); pending.Time = Stopwatch.GetTimestamp();
        lock (queue)
        {
            var duplicate = pending.Provider?.DuplicateWindowSeconds ?? 0;
            if (duplicate > 0 && last != null && ReferenceEquals(last.Provider, pending.Provider) && ReferenceEquals(last.Plane, pending.Plane) && ReferenceEquals(last.Schedule, pending.Schedule) && ReferenceEquals(last.Game, pending.Game) && last.Text == pending.Text && (pending.Time - last.Time) / (double)Stopwatch.Frequency < duplicate)
                return;
            if (queue.Count < 24)
            {
                queue.Enqueue(pending);
                last = pending;
            }
        }
    }

    static void Grammar(string __1, ref string __2)
    {
        var self = current; if (self == null || __1 != self.grammarMessage || (self.commands.Length == 0 && self.providers.Length == 0)) return;
        try
        {
            var helper = self.fromJson.Invoke(null, [__2, self.helperType]);
            var commandText = (string)self.grammar.GetValue(helper) ?? "";
            var targetText = (string)self.targets.GetValue(helper) ?? "";
            var extended = self.ExtendGrammar(commandText, targetText);
            commandText = extended.Key;
            targetText = extended.Value;
            self.grammar.SetValue(helper, commandText);
            self.targets.SetValue(helper, targetText);
            __2 = (string)self.toJson.Invoke(null, [helper]);
        }
        catch (Exception error)
        {
            self.log("Custom command grammar unchanged: " + error.Message);
        }
    }

    internal KeyValuePair<string, string> ExtendGrammar(string commandText, string targetText)
    {
        var existing = new HashSet<string>(commandText.Split('|').Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
        var extra = commands.SelectMany(c => c.Phrases).Where(p => !existing.Contains(p)).ToArray();
        if (extra.Length > 0)
            commandText = commandText.TrimEnd('|') + "|" + string.Join("|", extra) + "|";
        foreach (var provider in providers)
        {
            // Each provider transforms a local copy; a failed extension cannot corrupt other grammars.
            try
            {
                var c = provider.ExtendCommands?.Invoke(commandText) ?? commandText;
                var t = provider.ExtendTargets?.Invoke(targetText) ?? targetText;
                if (c.Length > 2 * 1024 * 1024 || t.Length > 2 * 1024 * 1024)
                    continue;
                commandText = c; targetText = t;
            }
            catch { }
        }
        return new KeyValuePair<string, string>(commandText, targetText);
    }

    static string Address(string text)
    {
        var digits = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
        text = (text ?? "").ToLowerInvariant().Replace("nyner", "nine").Replace("niner", "nine");
        for (var i = 0; i < digits.Length; i++)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b" + digits[i] + @"\b", i.ToString());
        return System.Text.RegularExpressions.Regex.Replace(text, "[^a-z0-9]", "");
    }

    bool ValidFlight(Pending pending)
    {
        var plane = pending.Plane as Component;
        return plane && !(bool)deleted.GetValue(plane) && ReferenceEquals(pending.Game, instance.GetValue(null)) && ReferenceEquals(pending.Schedule, schedule.GetValue(plane)) && pending.Callsign == (string)name.GetValue(plane);
    }

    internal void Tick()
    {
        Require();
        var now = Stopwatch.GetTimestamp();
        if ((commands.Length > 0 || providers.Length > 0) && now >= nextAddresses)
        {
            nextAddresses = now + Stopwatch.Frequency / 2;
            var game = instance.GetValue(null);
            var previous = addresses.ToDictionary(p => p.Plane); var result = new List<Pending>();
            if ((game == null ? null : airplanes.GetValue(game)) is System.Collections.IEnumerable planes)
                foreach (var value in planes)
                {
                    var plane = value as Component;
                    if (!plane)
                        continue;
                    try
                    {
                        if ((bool)deleted.GetValue(plane))
                            continue;
                        var callsign = (string)name.GetValue(plane);
                        var flight = schedule.GetValue(plane);
                        if (string.IsNullOrWhiteSpace(callsign) || flight == null)
                            continue;
                        previous.TryGetValue(plane, out Pending cached);
                        if (cached != null && (!ReferenceEquals(cached.Game, game) || !ReferenceEquals(cached.Schedule, flight) || cached.Callsign != callsign))
                            cached = null;
                        result.Add(cached ?? new Pending { Plane = plane, Game = game, Schedule = flight, Callsign = callsign, Text = (string)sayName.Invoke(plane, null) });
                    }
                    catch { /* A partially initialized aircraft must not disable other command registrations. */ }
                }
            addresses = [.. result];
        }
        Pending pending;
        lock (queue)
        {
            if (queue.Count == 0)
                return;
            pending = queue.Dequeue();
        }
        if (!ReferenceEquals(pending.Game, instance.GetValue(null)) || pending.Game == null || (now - pending.Time) / (double)Stopwatch.Frequency > 3 ||
            (pending.Command != null && !commands.Contains(pending.Command)) || (pending.Provider != null && !providers.Contains(pending.Provider)))
            return;
        try
        {
            if (pending.Plane == null)
            {
                DispatchSpeech(pending);
                return;
            }
            if (!ValidFlight(pending) || ((pending.Provider == null || pending.Provider.RequireRadioContact) && !CanReceive(pending.Plane)))
                return;
            bool live = true;
            var context = new RadioCommandContext(pending.Plane, pending.Callsign, pending.Text, message =>
            {
                Require();
                if (!live)
                    throw new InvalidOperationException("Reply must occur during the command callback.");
                if (ValidFlight(pending))
                    Reply(pending.Plane, message);
            });
            try
            {
                if (pending.Provider != null)
                    pending.Provider.Execute(context);
                else pending.Command.Execute(context);
            }
            finally
            {
                live = false;
            }
        }
        catch (Exception error)
        {
            log("Custom radio command failed (" + (pending.Provider?.Id ?? pending.Command?.Id) + "): " + error.Message);
        }
    }

    void DispatchSpeech(Pending pending)
    {
        string addressed;
        if (pending.Provider != null)
            addressed = pending.Provider.ResolveSpeech(pending.Text);
        else
        {
            var phrase = pending.Command.Phrases.Where(p => pending.Text.EndsWith(" " + p, StringComparison.Ordinal)).OrderByDescending(p => p.Length).First();
            var address = Address(pending.Text.Substring(0, pending.Text.Length - phrase.Length));
            var matches = (pending.Addresses ?? []).Where(p => ReferenceEquals(p.Game, pending.Game) && (Address(p.Callsign) == address || Address(p.Text) == address)).ToArray();
            if (matches.Length != 1)
                return;
            addressed = matches[0].Callsign + " " + phrase;
        }

        if (string.IsNullOrWhiteSpace(addressed) || addressed.Length > 512)
            return;

        var split = addressed.IndexOf(' ');
        if (split < 1)
            return;
        var callsign = addressed[..split];
        var captured = (pending.Addresses ?? []).Where(p => string.Equals(p.Callsign, callsign, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(p.Game, pending.Game)).ToArray();

        if (captured.Length != 1 || !ValidFlight(captured[0]))
            return;

        if (pending.Provider != null && !SafeAccept(pending.Provider, captured[0].Plane, addressed[(split + 1)..]))
            return;

        var window = commandWindow.GetValue(pending.Game);

        window.GetType().GetMethod("Command", F, null, [typeof(string)], null).Invoke(window, [addressed]);
    }

    public void Reply(object nativeAircraft, string message, string displayText = null)
    {
        Require();
        if (string.IsNullOrWhiteSpace(message) || message.Length > 240 || message.Any(char.IsControl))
            throw new ArgumentException("Pilot replies must be 1–240 characters.");

        if (displayText != null && (displayText.Length > 240 || displayText.Any(char.IsControl)))
            throw new ArgumentException("Invalid radio display text.");

        var plane = nativeAircraft as Component;
        var game = instance.GetValue(null);
        if (!plane || game == null || (bool)deleted.GetValue(plane))
            return;

        if (airplanes.GetValue(game) is not System.Collections.IEnumerable live || !live.Cast<object>().Any(p => ReferenceEquals(p, plane)))
            return;

        var window = commandWindow.GetValue(game);
        var send = window.GetType().GetMethods(F).Single(m => m.Name == "SendMsg" && m.GetParameters().Length == 6);
        var args = send.GetParameters();
        var say = (string)sayName.Invoke(plane, null);

        send.Invoke(window, [name.GetValue(plane) + ": " + (displayText ?? message), Enum.ToObject(args[1].ParameterType, 4), message.TrimEnd('.', ',') + ", " + say, plane, Enum.ToObject(args[4].ParameterType, 3), null]);
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        harmony.UnpatchSelf();
        RadioCommands.Detach(this);
        if (ReferenceEquals(current, this))
            current = null;
        commands = [];
        providers = [];
        addresses = [];
        registrations.Clear();
        lock (queue)
        {
            queue.Clear();
            last = null;
        }
    }
}
