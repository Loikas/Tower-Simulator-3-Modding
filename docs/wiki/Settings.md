# Settings

BepInEx config entries define values; framework registrations define when the in-game Mods menu may edit them.

| Mode | Author responsibility |
| --- | --- |
| Live | Consume/apply changes during an airport session |
| Main menu only | Reconfigure before loading an airport, without restarting the process |
| Restart required | Default when no edit policy is registered |

## Live example

```csharp
var enabled = config.Bind("Operations", "Enabled", true, "Applies immediately.");
var token = ModSettingsServices.Current?.RegisterLiveSetting(
    Id, "Operations", "Enabled");
// Read enabled.Value where the action is performed.
// Dispose token with the owning runtime.
```

Registration is a promise about implementation, not automatic reconfiguration. If a value is captured once at startup, do not mark it live.

For menu changes, cast to `IModSettingsEditPolicy` and call `RegisterMenuSetting(Id, section, key)`. `MenuConfiguredModule<T>` can replace child modules from an immutable configuration snapshot outside an airport session, restoring the prior snapshot on failure. It does not replace children while an airport is loaded, loading or paused.

## Optional-mod visibility

```csharp
var visibility = ModSettingsServices.Current as IModSettingsVisibility;
var token = visibility?.RequireModsForSection(
    Id, "Integration", new[] { "example.provider" },
    () => MyIntegration.IsAvailable);
```

`MyIntegration.IsAvailable` is a placeholder for your own bounded check, not a framework member. Use `RequireModsForSetting` for one key. Loaded-mod requirements and the callback must both pass. Missing/disabled providers do not satisfy the rule; hidden values are kept.

The starter uses this for Input Panel and checks `InputServices.Current != null`. Keep/dispose visibility tokens. Visibility does not change the live/menu/restart mode.

The suite hides expensive Diagnostics settings outside [Developer mode](Performance-and-Diagnostics.md). Keep normal gameplay controls available without developer tools.
