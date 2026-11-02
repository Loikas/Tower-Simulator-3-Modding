# Aircraft Observer starter

A small, read-only example for framework **0.8.20 / API 0.8.10**. It demonstrates persistent runtime ownership, aircraft observations, live settings, optional Input Panel integration, dependency visibility and launcher help. It makes no native patches and does not change aircraft behavior.

## Build and try it

1. Install TS3, BepInEx 5.x and framework 0.8.20 or newer. Install a .NET SDK; this starter was built with SDK 10.0.401 on Windows. The plugin targets **.NET Framework 4.6**, not .NET 10.
2. Extract the starter into your own folder. Edit `mod-project.json`: choose a unique `Id`, `Name`, `Author`, `Version` and description. This is the source for plugin identity and help metadata. `ModIdentity.g.cs` is generated under `obj/`; do not edit it.
3. Run in PowerShell:

   ```powershell
   ./Build-Package.ps1 -GameDir 'C:\Games\Tower! Simulator 3'
   ```

   Replace the example path with your installation. The script builds, checks the compiled identity and hard dependencies, and creates `dist/<id>-<version>.zip`. It does not install the mod or change the game.
4. Close TS3 and added windows. In Mod Manager, validate the ZIP under **Create a mod**, then install it using **Install / update ZIP**.
5. Start TS3 and load an airport. Check `BepInEx/LogOutput.log` for the example's ready message.
6. With Input Panel installed, press **Ctrl+Shift+F8** to log the object count once. In **Settings > Input**, the shortcut can be changed. **Settings > Mods > your mod > Input** contains a live enable toggle and is hidden when Input Panel is unavailable.
7. To inspect observations, enable **Developer mode** in the launcher and **Diagnostics > Log New Aircraft** in the mod settings. Logging defaults off. Existing aircraft are not replayed when logging is enabled; new observation lifetimes are logged. Normal gameplay stays quiet.

## Files

| File | Purpose |
| --- | --- |
| `mod-project.json` | Identity, package dependencies, listing information and help entries |
| `Plugin.cs` | BepInEx bootstrap and persistent runtime |
| `DeveloperMode.cs` | Read-only, two-second cached access to the launcher's Developer preference |
| `Generate-Metadata.ps1` | Generates constants and `ModHelpPage` entries before compilation |
| `MyTS3Mod.csproj` | Local references; game/framework DLLs are not copied into the payload |
| `Build-Package.ps1` | Build, compiled metadata checks, explicit payload and SHA-256 manifest |
| `TEMPLATE-LICENSE.txt` | MIT terms for the starter source |

The package includes only your DLL, `mod-info.json`, the starter license and an optional `icon.png`. Add a square PNG up to 1024 × 1024 and 256 KiB to the starter root to include an icon. A 256 × 256 icon is a useful default.

## Make it your own

- Change `AssemblyName` in the project before sharing several mods, so they do not all use `MyTS3Mod.dll`. The packaging script reads this name automatically. Namespace changes are optional; keep `RootNamespace` and `Plugin.cs` aligned if you change them.
- Increase `Version` in `mod-project.json` for each package. Existing ZIPs are never overwritten. The package script also passes this version to the compiler.
- Add runtime behavior in `ModRuntime` or register an `ITS3Module`. TS3 removes BepInEx bootstrap components: do not put runtime teardown in the bootstrap's `OnDestroy`.
- Register only settings that are actually read during play as live. `Input.EnableCountShortcut` demonstrates an immediately applied setting.
- Keep and dispose subscriptions, input registrations, visibility rules and help registrations. Access Unity and framework services on the main thread.
- Help entries document behavior; they do not register executable voice commands. The starter has no radio commands.
- An observation ID is an observation lifetime, not a permanent flight ID. Missing, inactive or deleted aircraft alone are not proof of flight completion.

## Share a package

Replace the placeholder author before preparing a submission. The starter is MIT-licensed; preserve its notice and declare the license appropriate to your own work. In the manager, **Prepare submission** combines the validated mod ZIP and `mod-project.json`. **Import submission** adds it to a local store. These actions do not publish to GitHub or ModDB.

The recorded game hash identifies the build used for packaging. It does not replace testing compatibility. Never include game DLLs, BepInEx, the framework API or generated `bin/obj` folders in your mod payload.

A build stage remains under `dist/stage-*` for inspection. Share the ZIP, not the stage directory. Use `-OutputDirectory` to choose another output folder and `-ProjectFile` if you rename the project file.

See the prepared wiki's **Getting Started**, **Starter Mod**, **Settings**, **Help and Icons** and **Packaging and Distribution** pages for the full workflow.

### Voice/radio example (Framework 0.8.20)

Say or enter `<callsign> report mod status` (alias `report observer status`). The persistent runtime registers the phrase through `RadioCommands.Current` and replies through the native pilot radio. The framework owns speech/strict-grammar hooks; the starter has no Harmony patches. Registration is disposed with the runtime. Live voice recognition needs in-game validation. See `docs/wiki/Input-and-Commands.md` in the authoring documentation for limits and lifecycle rules.
