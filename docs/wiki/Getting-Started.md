# Getting started as a mod author

## First build

1. Install the framework and build prerequisites from [Compatibility](Compatibility.md).
2. In the launcher, choose **Create a mod > Save starter template**. Extract the complete `TS3Mod-Starter.zip` into your own working folder.
3. Edit `mod-project.json`: set a unique ID and name, replace the author, and describe your mod. The starting version is 0.1.0.
4. Run:

   ```powershell
   ./Build-Package.ps1 -GameDir 'C:\Games\Tower! Simulator 3'
   ```

5. Validate the output ZIP with the launcher, close TS3/added windows, then install the ZIP.
6. Load an airport. Find the ready message in `BepInEx/LogOutput.log`.
7. With Input Panel, Ctrl+Shift+F8 logs a count once. The binding is configurable. Aircraft observation logging defaults off and additionally requires Developer mode.

Extracting or building the starter does not install the example in your game.

## Next edit

Change the event handler or add a small feature in `ModRuntime`. Read [Framework API](Framework-API.md) before retaining aircraft references or making decisions about flight completion.

For another package, increase `Version` in `mod-project.json` and rebuild. Identity/help constants are generated; no manual version changes in `Plugin.cs` are needed. Existing package ZIPs are never overwritten.

Before sharing multiple mods, give each project a unique `AssemblyName`. The packager uses it automatically. Keep the original starter license notice and choose the appropriate license for your additions. See [Packaging and Distribution](Packaging-and-Distribution.md).
