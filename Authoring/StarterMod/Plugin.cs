using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using TS3Modding.API;
using UnityEngine;

namespace MyTS3Mod
{
    // ModIdentity is generated from mod-project.json before compilation.
    [BepInPlugin(ModIdentity.Id, ModIdentity.Name, ModIdentity.Version)]
    [BepInDependency("nl.ts3modding.loader", ModIdentity.FrameworkMinimumVersion)]
    [BepInDependency("nl.ts3modding.input", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("Tower! Simulator 3.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var runtimeObject = new GameObject(ModIdentity.Id + ".Runtime");
            runtimeObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(runtimeObject);
            try { runtimeObject.AddComponent<ModRuntime>().Initialize(Config); }
            catch (Exception error) { Destroy(runtimeObject); Logger.LogError(error); }
        }
        // TS3 removes bootstrap components. The persistent runtime owns cleanup.
    }

    public sealed class ModRuntime : MonoBehaviour
    {
        private ConfigEntry<bool> logAircraft, enableShortcut;
        private IDisposable observed, loggingSetting, inputSetting, inputVisibility, keybind, help, radio;
        private IInputBindings input;

        public void Initialize(ConfigFile config)
        {
            var framework = FrameworkServices.Current;
            if (framework == null) throw new InvalidOperationException("The mod framework is unavailable.");
            logAircraft = config.Bind("Diagnostics", "LogNewAircraft", false,
                "Log newly observed aircraft in Developer mode. Applies immediately.");
            enableShortcut = config.Bind("Input", "EnableCountShortcut", true,
                "Enable the aircraft-count shortcut when Input Panel is available. Applies immediately.");

            observed = framework.Events.Subscribe<AircraftObserved>(message => {
                if (logAircraft.Value && TS3Modding.Preferences.DeveloperMode.Enabled)
                    framework.Log("[" + ModIdentity.Name + "] Observed " +
                        message.Aircraft.Details.Callsign + " (" + message.Aircraft.Id + ")");
            });
            var settings = ModSettingsServices.Current;
            loggingSetting = settings?.RegisterLiveSetting(ModIdentity.Id, "Diagnostics", "LogNewAircraft");
            inputSetting = settings?.RegisterLiveSetting(ModIdentity.Id, "Input", "EnableCountShortcut");
            inputVisibility = (settings as IModSettingsVisibility)?.RequireModsForSection(
                ModIdentity.Id, "Input", new[] { "nl.ts3modding.input" }, () => InputServices.Current != null);
            help = ModHelp.Register(ModIdentity.CreateHelp());
            radio = RadioCommands.Current?.Register(new RadioCommand(ModIdentity.Id + ".status",
                new[] { "report mod status", "report observer status" }, command =>
                    command.Reply("Aircraft observer is ready")));
            framework.Log("[" + ModIdentity.Name + "] " + ModIdentity.Version + " ready.");
        }

        private void Update()
        {
            // Rebind only when the provider or the live setting changes.
            var desired = enableShortcut != null && enableShortcut.Value ? InputServices.Current : null;
            if (ReferenceEquals(input, desired)) return;
            keybind?.Dispose(); keybind = null; input = desired;
            if (input != null)
                keybind = input.Register(new InputActionDefinition(ModIdentity.Id + ".count",
                    "Log aircraft count", ModIdentity.Name,
                    new KeyChord("F8", KeyModifiers.Control | KeyModifiers.Shift)), LogCount);
        }

        private static void LogCount()
        {
            var framework = FrameworkServices.Current;
            if (framework == null) return;
            // Explicit user action: take one snapshot, rather than polling every frame.
            var aircraft = framework.Aircraft.Snapshot();
            var active = aircraft.Count(a => a.Details.SimulationActive && !a.MarkedDeleted);
            framework.Log("[" + ModIdentity.Name + "] Observation available=" +
                framework.ObservationAvailable + ", objects=" + aircraft.Count + ", active=" + active);
        }

        private void OnDestroy()
        {
            radio?.Dispose(); radio=null;
            keybind?.Dispose(); observed?.Dispose(); loggingSetting?.Dispose();
            inputSetting?.Dispose(); inputVisibility?.Dispose(); help?.Dispose();
            keybind = observed = loggingSetting = inputSetting = inputVisibility = help = null;
        }
    }
}
