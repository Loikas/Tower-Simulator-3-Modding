# Help and icons

The launcher separates **Overview**, **Dependencies**, **Keybinds** and **Commands**. Required dependencies come from `mod.json`; the launcher computes installed status and the reverse “Required by” list.

## Package metadata

Declare `mod-info.json` in the manifest's `Files` map:

```json
{
  "Name": "My Mod",
  "Description": "A short explanation of the feature.",
  "Icon": "icon.png",
  "Keybinds": [{"Syntax": "Ctrl+F8 (default)", "Description": "Open the panel."}],
  "Commands": []
}
```

The starter generates this from `mod-project.json`. Packaged metadata is available before the game has run.

Icons are optional PNGs, at most 1024 × 1024 and 256 KiB. Use a simple square asset, typically 256 × 256. Keep icon paths to a declared file in the plugin folder. Missing/invalid icons use a fallback.

## Runtime help

```csharp
var help = ModHelp.Register(new ModHelpPage(
    Id, Version, "A short explanation of the feature.",
    keybinds: new[] { new ModHelpItem("Ctrl+F8 (default)", "Open the panel.") },
    commands: new ModHelpItem[0]));
// Dispose help with the owning runtime.
```

Register on the main thread. One registration owns a mod ID's page; dispose it before replacing it. Use the exact package version. Each category permits 64 entries. Syntax: 512 characters; item description: 2048; page description: 4096; total help text: 32768.

Versioned snapshots are exported under `BepInEx/TS3ModLoader/help`. The launcher only uses runtime data for an enabled package with matching ID, version and schema; otherwise it uses packaged metadata/fallbacks. Help does not replace package dependencies or register executable commands.
