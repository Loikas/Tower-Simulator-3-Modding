# Mod authoring and local store preview

The manager now includes Create a mod, Local mod store and Import submission. This is an offline development and distribution workflow; there are no public accounts, server uploads, payments or hosted downloads yet.

## Authors

Use Create a mod → Save starter template. Extract the ZIP to your own working folder, then follow README.md. Install the .NET SDK and framework first. The starter references libraries in your local TS3 installation; the distributed template contains source only. It demonstrates observation events, live grouped settings and an optional Input Panel keybind. Its persistent runtime survives TS3 removing bootstrap plugin components.

Build-Package.ps1 builds a single-DLL mod ZIP, records its SHA256, declares framework compatibility and pins the locally inspected game assembly hash. Keep the BepInPlugin ID/version and mod-project.json synchronized. A local hash only identifies the build you compiled against; test behavior before claiming support. Validate package checks archive structure and payload integrity without loading or executing the DLL.

Install and test the package through the existing manager. After testing, replace the template author/license, describe behavior, increase the version as needed, and choose Prepare submission. Select the mod ZIP and mod-project.json, then save a new submission ZIP. Preparation does not execute your mod or upload anything.

## Local store

Import submission verifies listing identity/version, required metadata, declared game hashes, package structure and payload hashes. Known proprietary/shared library payloads are refused in author submissions. This is a guard against accidental packaging, not exhaustive license verification or code review. Unreviewed DLLs can execute arbitrary code when installed and loaded: import only submissions you intend to test.

Local listings live in %LOCALAPPDATA%/TS3ModManager/store. Each ID/version is immutable; publish a new version for changes. A submission contains exactly listing.json and package.zip. listing.json includes SchemaVersion (1), Id, Name, Version, Author, Summary, Description, License, optional HTTPS Website and PackageSha256. That checksum detects changed bytes and is not a publisher signature. Local author names are self-declared, not verified accounts.

Browse Local mod store to choose versions and review installation. The bundled framework can satisfy dependencies automatically. Importing a submission only copies it into the local catalog; installing is a separate explicit action. Existing install ownership, game compatibility checks, stopped-game protection and per-package backups still apply. Corrupt local listings are reported rather than silently installed. Failed import staging folders are ignored and retained for diagnosis.

## Public store next

Keep the existing install package format separate from store metadata. A hosted service needs authenticated author accounts, server-owned mod ownership, immutable release versions, private pending submissions, moderation before public visibility, platform-controlled package storage and download hashes, and an authenticated catalog/update feed. Client-supplied Author is display text, never authorization. The server must enforce ownership and validate archives independently; client validation is only author feedback.

Do not make local imports appear reviewed or official. Do not automatically install downloaded updates. A public release should list supported game builds, dependencies, changelogs, license and source/support links, and give users a concrete version/dependency review before installation. Revocation/removal and audit history need defined behavior before public publishing.

Current preview deliberately stops at a tested local author→submission→catalog→installation flow. Hosting selection, account provider, public name/domain and moderation workflow remain decisions for the public service.

Verification: the starter built successfully against the installed TS3/framework with zero warnings/errors. Its real output ZIP passed manager CLI validation, submission preparation and local import in a workspace fixture. The manager/core test suite covers submission→local listing→installation, immutable versions, changed hashes, metadata mismatch, shared-library exclusions and conflicting version selections. No example mod was installed into the user's game.

CLI author tools share the UI backend: --validate-package <zip>; --prepare-submission <package-zip> <mod-project.json> <new-output-zip>; --import-submission <submission-zip> <local-store-folder>. These commands prepare or import local artifacts only.

## Framework 0.8.4 settings application policy

The original IModSettingsRegistry live-setting interface remains binary compatible. IModSettingsEditPolicy adds RegisterMenuSetting, ChangeMode and CanEdit with IsAirportSessionActive. Unregistered settings remain restart-required. RegisterLiveSetting is appropriate only for settings the mod consumes during play. RegisterMenuSetting is appropriate only if the consumer can reconfigure without a process restart before an airport loads. MenuConfiguredModule<T> provides this lifecycle for immutable value snapshots: compare configuration in the main menu, dispose prior children, initialise replacements, and restore the prior snapshot on failure. It never replaces children while an airport exists (including loading/paused sessions). Dispose registrations with the owning module. Existing mods retain their previous live/read-only behavior until explicitly opting in.

## In-game mod presentation (framework 0.8.7)

The Mods screen displays names without a leading TS3, a small version/status line, a description and an icon. Built-in icons/descriptions cover the suite. Third-party mods can declare `mod-info.json` alongside their plugin DLL:

```json
{"Name":"Example mod","Description":"A short description of what this mod does.","Icon":"icon.png"}
```

All fields are optional. Declare both metadata and icon in your package manifest Files map. Icons must be PNG, at most 1024 × 1024 and 256 KiB; use a square transparent 256 × 256 asset. Paths must stay inside the plugin directory. `icon.png` is detected automatically. Missing metadata/icons use a generic description/icon. Display names do not change plugin IDs, dependency IDs or config filenames. Suite icon sources and regeneration script live in TS3ModLoader/Assets/ModIcons and Generate-ModIcons.py.

## Settings that require another mod (Framework 0.8.14)

Loaded mods can opt into `IModSettingsVisibility` through the existing settings service. Rules belong to the calling mod and are disposed with its other registrations. The framework checks loaded plugin IDs generically; no consumer/provider IDs are built into its dependency policy.

```csharp
var visibility = ModSettingsServices.Current as IModSettingsVisibility;
if (visibility != null)
{
    tokens.Add(visibility.RequireModsForSection(
        Id, "Integration", new[] { "example.provider" }));
    tokens.Add(visibility.RequireModsForSetting(
        Id, "Operations", "UseIntegration", new[] { "example.provider" },
        () => IntegrationServices.Available));
}
```

Every listed provider must be loaded in the current session; a disabled or missing plugin does not satisfy the rule. The optional callback can require an enabled capability as well as installation. Section and setting requirements combine, and their results are evaluated when rendering/editing settings. Hidden entries retain their configuration values and cannot be edited through the framework. Empty sections disappear naturally. Keep and dispose returned tokens on module disposal. Visibility is independent of live/main-menu/restart edit modes. The original settings interfaces and registry constructor remain binary compatible; API assembly identity stays 0.2.0.0.


## Launcher detail tabs and framework help API (0.8.16)

The launcher separates Overview, Dependencies, Keybinds and Commands. Dependencies are declared in `mod.json` and their installed versions and status are computed by the launcher. Help registration does not declare or bypass dependencies, install commands, or change bindings.

For help that is available before the first game launch, ship `mod-info.json` as a declared package file:

```json
{
  "Description": "A short description of your mod.",
  "Icon": "icon.png",
  "Keybinds": [{"Syntax": "Ctrl+T (default)", "Description": "Open the panel."}],
  "Commands": [{"Syntax": "<callsign> example", "Description": "Explain the command."}]
}
```

For dynamically registered help, reference API 0.8.7 and require framework 0.8.16. Register on the Unity main thread after the framework is loaded, and keep the returned registration for the lifetime of your module:

```csharp
helpRegistration = ModHelp.Register(new ModHelpPage(
    "example.my-mod", "1.0.0", "A short description.",
    keybinds: new[] { new ModHelpItem("Ctrl+T (default)", "Open the panel.") },
    commands: new[] { new ModHelpItem("<callsign> example", "Explain the command.") }));
// In module Dispose(): helpRegistration.Dispose();
```

Use the exact package ID and version. One registration owns a mod's complete help page; dispose it before replacing it. The framework exports bounded metadata to `BepInEx/TS3ModLoader/help/<id>.json`. The launcher accepts a runtime snapshot only for an enabled installed package with the same ID, version and schema; otherwise it falls back to package metadata. The last exported snapshot remains available after exiting the game. These are documentation snapshots, not live binding state: label defaults explicitly, or re-register when publishing changed bindings. Empty tabs show a clear absence message. Up to 64 commands and 64 keybinds can be registered, with at most 32,768 characters of combined help text. Existing API assembly identity remains 0.2.0.0.


## Updated starter and wiki — 2026-10-25

Use the current starter in `Authoring/StarterMod`. `mod-project.json` is now the identity/help source; `Generate-Metadata.ps1` creates compiler constants and runtime help. The package script verifies compiled BepInEx metadata and hard dependencies, generates declared `mod-info.json`, includes the starter license and optional icon, and discovers the DLL from `AssemblyName`. It checks the installed minimum framework version without executing plugin code.

Diagnostics default off and require Developer mode; the read-only preference helper caches access. The example demonstrates live input enable/disable and provider-dependent visibility. The optional count shortcut reads one snapshot per user action.

Start the prepared English documentation at [docs/wiki/Home.md](docs/wiki/Home.md). The 23 Markdown files include player guides, API examples, troubleshooting, sidebar/footer and a local-only wiki export helper. See [Publishing the Wiki](docs/wiki/Publishing-the-Wiki.md). The launcher bundle now ships these files under `documentation`; no GitHub/ModDB publication is performed.
