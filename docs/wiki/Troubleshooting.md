# Troubleshooting

## The mod does not appear

Check `BepInEx/LogOutput.log` for dependency/loading errors. Confirm the framework is enabled, the ID is unique and the game build is supported. Keep the complete launcher bundle together. A changed game folder needs confirmation with Refresh/Browse.

## The starter does not build

Pass the actual TS3 directory with `-GameDir`. Install the declared framework and BepInEx first. The project references installed libraries; it does not download or ship game DLLs. Generation runs `Generate-Metadata.ps1` through Windows `powershell.exe`.

For identity/dependency mismatch, correct the declarations rather than editing a ZIP. Edit `mod-project.json`, not generated source. When a version ZIP exists, increase Version or choose a different output directory.

## The example shortcut is missing

Input Panel is optional. Without its provider, the starter's Input section is hidden and no count action is registered. With it, check the live toggle and binding/conflicts in the Input menu. Published shortcuts are defaults.

## Logging is quiet

This is intentional. Both Developer mode and Diagnostics > Log New Aircraft are required for the starter's observation log. Enabling it does not replay existing aircraft. The ready message and explicit count action work normally.

## A strip remains on the board

Completion needs recorded native evidence. Check Archive Completed Flights, exact matching, duplicate active strips and board configuration. Framework 0.8.19/strips 0.6.6 include missed-departure handoffs and per-strip failure isolation. A held-strip warning does not mean all automation is disabled. Disappearance alone is not proof.

## Follow selected finds no aircraft

Camera Tools 0.2.4 uses the native command target, then persistent callsign-highlight selection. Choose an active aircraft in the current session and check that following is enabled. Deleted/inactive objects are excluded.

## Report a problem

Include versions, airport, action/instruction, expected and actual behavior, whether a save was loaded, and a short reproduction. Keep a relevant log excerpt and a pre-failure save when possible. Collect heavy diagnostics only as needed; compare normal play with Developer mode off.
