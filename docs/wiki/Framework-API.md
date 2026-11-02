# Framework API

Reference the installed `TS3Modding.API.dll` with `Private=false`. Do not copy it into your own payload. The framework owns service providers; consumers must not call `Attach` to replace them.

## Read observations

```csharp
var host = FrameworkServices.Current;
if (host == null) throw new InvalidOperationException("Framework unavailable.");
IDisposable subscription = host.Events.Subscribe<AirportObserved>(message => {
    var observation = message.Observation;
    // Read snapshots here. Keep the handler short.
});
// Dispose subscription with the owning runtime/module.
```

`AircraftObserved`, `AircraftUpdated`, `AircraftNoLongerObserved`, `AircraftStateChanged` and `SceneLoaded` provide narrower events. `host.Aircraft.Snapshot()` returns the current registry snapshot. Reuse one snapshot within an operation instead of taking it for each aircraft.

Observation IDs are session/observation lifetimes. Callsigns and native objects can be reused. Missing/inactive/deleted flags alone do not establish completion. Use recorded `FlightCompletion` evidence where appropriate:

| Kind | Meaning |
| --- | --- |
| `ParkedArrival` | Reviewed native arrival-parking signal |
| `DepartureContactAccepted` | Accepted controller handoff, including the supported deferred-altitude branch |
| `DepartureAutomaticHandoff` | Reviewed native missed-departure automatic handoff |

The framework owns native capture. Do not manufacture proof from a disappearance timer.

## Modules

Implement `ITS3Module` and retain the token from `host.RegisterModule(module)`. The framework supplies a scoped event context, calls `Tick()` on Unity's main thread and cleans up scoped subscriptions on removal. A failing module is isolated from other modules.

`IFrameworkContext` provides `Events`, `Aircraft`, `GameAssemblySha256`, `ObservationAvailable` and `Log`. Native movement/archival services are optional interfaces on the host; check availability and preserve validation rules.

See [Settings](Settings.md), [Input and Commands](Input-and-Commands.md) and [Help and Icons](Help-and-Icons.md). Framework 0.8.21 also exposes `RadioCommands.Current` for fixed voice/radio phrases and pilot replies; see the command guide. Native Harmony integration beyond this service still needs separate game-build verification.
