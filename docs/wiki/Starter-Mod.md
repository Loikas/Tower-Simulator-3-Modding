# Starter mod structure

The starter is read-only. Its example logs an aircraft count on demand, optionally logs newly observed aircraft in Developer mode, and exposes live configuration/help.

```text
StarterMod/
  mod-project.json
  Plugin.cs
  DeveloperMode.cs
  Generate-Metadata.ps1
  MyTS3Mod.csproj
  Build-Package.ps1
  README.md
  TEMPLATE-LICENSE.txt
  icon.png                  # optional, supplied by the author
```

## One identity source

`mod-project.json` provides `Id`, `Name`, `Version`, `Description`, required package dependencies, listing fields, `Keybinds` and `Commands`. Before compilation, the project runs `Generate-Metadata.ps1` to create `obj/.../ModIdentity.g.cs`.

The constants feed BepInEx identity and framework help. The package script generates matching `mod-info.json`. Do not edit generated source. For an extra hard runtime dependency, add its BepInEx attribute as well; the packager checks both declarations.

## Runtime ownership

`Plugin.Awake` creates an independent persistent GameObject. `ModRuntime.Initialize` owns subscriptions and registrations. Its `OnDestroy` disposes them. TS3 removes bootstrap plugin components during startup, so runtime teardown in the bootstrap's `OnDestroy` would stop the mod too early.

`Update` only reconnects the optional input provider when it or the live setting changes. It does not scan aircraft every frame. Counts take one snapshot only when requested.

The included `DeveloperMode.cs` is a source helper, not a public framework service. It reads the launcher's preference with a two-second cache and never changes it. Optional diagnostic callbacks therefore do not perform repeated disk reads.

Use events and registered input actions. Only opt into live settings when values are read/applied during play. Preserve main-thread access and cleanup. Help entries must describe implemented behavior; `Commands` metadata does not create voice commands.
