using BepInEx;
using HarmonyLib;
using System;
using System.IO;
using TS3Modding.Diagnostics;
using UnityEngine;

namespace TS3Modding;

[BepInPlugin(Id, "TS3 Mod Loader", "0.8.21")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "nl.ts3modding.loader";
    private Harmony harmony;
    private FrameworkDriver driver;
    private DiagnosticFiles files;
    private bool quitting;
    private void Awake()
    {
        FrameworkRuntime runtime = null;
        GameObject driverObject = null;
        try
        {
            files = new DiagnosticFiles(Path.Combine(Paths.BepInExRootPath, "TS3ModLoader"));
            var export = Config.Bind("Diagnostics", "ExportReconnaissance", true,
                "Export type, field and method signatures synchronously during startup.");
            var observe = Config.Bind("Framework", "ObserveAircraft", true,
                "Read-only airport polling for the reviewed game assembly profile. Unknown profiles disable observation.");
            var interval = Config.Bind("Framework", "ObservationIntervalSeconds", 1f,
                "Unscaled real-time seconds between aircraft scans (minimum 0.25, maximum 60).");
            var hash = DiagnosticFiles.Hash(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"));
            harmony = new Harmony(Id); // No gameplay hooks are installed in v0.2.
            files.Append("Bootstrap 0.8.21 Unity=" + Application.unityVersion + " CLR=" + Environment.Version + " SHA256=" + hash);

            if (export.Value && TS3Modding.Preferences.DeveloperMode.Enabled)
            {
                try
                {
                    files.ExportReconnaissance();
                    Logger.LogInfo("Reconnaissance exported during startup.");
                }
                catch (Exception error)
                {
                    files.Append("Reconnaissance failed: " + error);
                    Logger.LogError(error);
                }
            }

            var seconds = interval.Value;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                seconds = 1;

            runtime = new FrameworkRuntime(files, message => Logger.LogInfo(message), hash, observe.Value,
                Math.Max(0.25, Math.Min(60, seconds)));
            driverObject = new GameObject("TS3Modding.Framework")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(driverObject);
            driver = driverObject.AddComponent<FrameworkDriver>();
            driver.Initialize(runtime, files);
            ModsMenu modsMenu = null;
            try
            {
                if (hash == "26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7")
                {
                    modsMenu = driverObject.AddComponent<ModsMenu>();
                    modsMenu.Initialize(message => Logger.LogInfo(message));
                }
            }
            catch (Exception error)
            {
                if (modsMenu)
                    Destroy(modsMenu);
                Logger.LogError("In-game Mods tab unavailable: " + error);
            }
            Logger.LogInfo("TS3 Mod Framework 0.8.21 ready; mods load separately.");
            Logger.LogInfo("Diagnostics: " + Path.Combine(Paths.BepInExRootPath, "TS3ModLoader"));
        }
        catch (Exception error)
        {
            runtime?.Dispose();
            if (driverObject)
                Destroy(driverObject);
            files?.Append("Bootstrap failed: " + error);
            Logger.LogError("Bootstrap failed: " + error);
        }
    }
    private void OnDisable()
    {
        files?.Append("BepInEx plugin disabled; independent framework driver retained.");
    }

    private void OnApplicationQuit()
    {
        quitting = true;
        if (driver)
            Destroy(driver.gameObject);
    }

    private void OnDestroy()
    {
        files?.Append("BepInEx plugin destroyed; quitting=" + quitting);
        harmony?.UnpatchSelf();
    }
}





