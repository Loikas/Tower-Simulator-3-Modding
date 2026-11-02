# Input and commands

## Register a shortcut

```csharp
var provider = InputServices.Current;
IDisposable registration = null;
if (provider != null)
    registration = provider.Register(
        new InputActionDefinition("example.my-mod.open", "Open panel", "My Mod",
            new KeyChord("F8", KeyModifiers.Control | KeyModifiers.Shift)),
        OpenPanel);
// OpenPanel is your Action callback. Dispose registration with the runtime.
```

IDs must be unique. Modifiers are separate flags, not part of the key name. Input Panel owns sampling, conflict checks and persisted overrides. Handle provider absence; do not dereference `Current` unconditionally.

The starter detects provider changes and its live shortcut setting instead of repeatedly registering the action. Disposing an action releases ownership; it does not erase the user's saved binding override.

For an effective binding, look up the action in `provider.Actions`. Label published shortcuts as defaults unless help is updated to reflect an actual assignment.

## Radio and text commands

`ModHelp.Register` and `mod-info.json` publish documentation. They do not create commands, speech grammar or dispatch handlers. Only document commands the mod implements.

Framework 0.8.21 provides a shared service for aircraft-addressed commands. API package 0.8.11 keeps assembly identity 0.2.0.0. No additional native patches are needed in a consumer mod.

```csharp
IDisposable command = RadioCommands.Current?.Register(new RadioCommand(
    "example.my-mod.status",
    new[] { "report mod status", "report observer status" },
    request => request.Reply("Aircraft observer is ready")));
// Dispose command when your runtime unloads.
```

Say or enter `<callsign> report mod status`. The framework appends the aircraft's native spoken callsign to the reply. Fixed phrases and aliases are added to Fast and strict and Fast and strict Legacy grammar; final speech interception supports Flexible AI. These paths have build/profile checks; live recognition still needs in-game testing with the updated starter.

Register on the game thread. The callback also runs there. `Callsign` identifies the addressed flight and `Phrase` contains the received instruction (voice input may include the address). Reply during the callback, not later from a worker or timer. Keep handlers short. Dispose registrations with your persistent runtime.

Registration accepts up to 16 fixed phrases per command, 96 characters per phrase and 64 commands overall. Letters, digits and spaces are supported; this simple registration does not parse variable arguments. Duplicate IDs, duplicate phrases and suffix-overlapping phrases across commands are rejected. Choose distinctive phrases that do not replace native or existing mod instructions. The framework cannot infer every native-command collision.

Only recognized custom phrases are intercepted. Unknown instructions continue through the game. Voice addresses must match one live callsign or native spoken callsign; ambiguous addresses do not execute. Voice dispatch uses the native command window to retain frequency handling. Queued commands expire after three seconds and validate game, aircraft, callsign and flight identity. There is no multiplayer synchronization guarantee for custom handlers.

`RadioCommands.Current` is null when the reviewed bridge is unavailable. Check availability rather than silently claiming command support. Register before loading an airport so the recognizer receives the phrases; if you add commands mid-session, reload recognition/the airport to refresh strict grammar. Tug Operations 0.13.0 registers its variable tow parser through the provider service described below.

`ModHelp` remains separate documentation: add matching syntax to `mod-project.json` / `ModHelpPage.Commands` so the launcher shows commands. Help registration alone does not execute anything.


## Register a parser with arguments

Framework 0.8.21 adds `IRadioCommandProviderService`, available by casting `RadioCommands.Current`. Use `RegisterProvider(new RadioCommandProvider(...))` for commands whose values need a dedicated parser. Tug Operations is a working example in `TS3Modding.Tugs/TowCommandRegistration.cs`.

The constructor takes these callbacks:

| Callback | Purpose | Thread |
| --- | --- | --- |
| `Accepts(RadioCommandAddress)` | Claim an addressed instruction using its text and opaque aircraft identity | May be a recognition thread |
| `Execute(RadioCommandContext)` | Parse/validate arguments and perform the operation; `NativeAircraft` is the addressed object | Main game thread |
| `CaptureSpeech(string)` | Return normalized speech to claim it, or null to leave it native | May be a recognition thread |
| `ResolveSpeech(string)` | Return the full native callsign and instruction, or null to refuse | Main game thread |
| `ExtendCommands(string)` | Append grammar clauses, including reviewed native placeholders such as `#target1`, `#taxiway1` and `#runway1` | May be a recognition thread |
| `ExtendTargets(string)` | Extend target pronunciations/aliases while preserving existing entries | May be a recognition thread |

Supply both speech callbacks or neither. Capture and grammar callbacks must be fast, exception-safe and limited to immutable published snapshots. Do not call Unity or mutate operations there. The framework does not interpret arguments: your main-thread parser must refuse invalid or mixed instructions atomically. Do not claim all native commands merely to inspect them.

The bridge consumes matching hypotheses but queues only final speech. It validates the captured aircraft/flight before dispatching the resolved command through the native command window, then queues the provider's handler. Commands claimed by multiple providers (or by a fixed command and a provider) execute neither handler. Provider exceptions are isolated; a failed grammar extension leaves that provider's input intact. There are at most 16 providers and 24 pending commands, with a three-second queue lifetime. Provider grammar results are bounded to 2 MiB per field.

`requireRadioContact` defaults to true, preserving native active/contact checks. A reviewed adapter can explicitly opt out when managing special aircraft; Tug Operations does this to retain its existing dormant/recovery command behavior. This does not add multiplayer synchronization. `duplicateWindowSeconds` optionally suppresses identical repeated input for the same game/aircraft/flight/provider, from zero to five seconds. Tug Operations uses two seconds.

Dispose the registration with its module. This removes its grammar callbacks and claims and prevents queued work from executing after unload. Restart/reload recognition to refresh exported strict grammar after registration changes.

For operational reports outside a command callback, the provider service also supplies `Reply(nativeAircraft, spokenMessage, displayText: optionalText)`. Call it on the main thread with a current aircraft. The bridge appends the native spoken callsign and uses the current command window. Keep spoken/display text within 240 characters and sanitize internal diagnostics in the mod before sending them. The ordinary callback's `Reply(message)` remains available for simple synchronous responses.
