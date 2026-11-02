# Input Panel

Configure supported native shortcuts and mod actions under **Settings > Input**. The default panel shortcut is **Ctrl+Shift+F10**.

Bindings support a named key with Ctrl, Shift and Alt modifiers. The editor supports conflict checks, unbinding and restoring defaults. Changes save and apply immediately.

Mods register actions with `InputServices.Current.Register(...)`. Input Panel owns physical input sampling and binding persistence. An action can have no callback when it describes a reviewed native binding.

A default binding is not necessarily the user's current binding. Launcher help should label defaults. Look in the in-game Input menu for the effective assignment.

Missing Input Panel does not disable every mod. Optional consumers must handle `InputServices.Current == null` and can offer a documented fallback. The [starter](Starter-Mod.md) leaves its optional count action unavailable until the provider exists.
