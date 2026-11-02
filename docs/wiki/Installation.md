# Installation

## Before you start

Use a supported Windows TS3 installation with BepInEx 5.x. The tested BepInEx baseline is 5.4.23.5. The standalone launcher includes its .NET runtime; mod authors still need a .NET SDK.

Use files supplied by the maintainer. Back up saves before testing a new build.

## Install

1. Extract the complete launcher bundle, keeping its supporting files and `packages` folder together.
2. Select the TS3 game folder. Use **Detect**, **Browse** or **Refresh** to confirm it.
3. Choose **Complete setup**. If needed, select the supported official BepInEx archive when prompted. Setup also installs the required framework.
4. Close TS3 and its added windows normally.
5. Optional mods are separate downloads; use **Install / update ZIP**.
6. Review dependencies and versions. Install the framework before its dependent mods.
7. Start TS3. Open **Settings > Mods** to configure installed mods.

A running game or added window blocks installation changes. The launcher checks declared file hashes and package dependencies. It does not authenticate a publisher or prove code is safe.

## Update, disable or remove

Select the mod and use its action buttons. Framework removal is not offered; disable/re-enable it instead. **Dependencies** shows required mods and other installed mods that need this one. Disable enabled dependent mods first when removing their provider.

Settings are kept when a mod is disabled or removed. Managed changes create transaction records/backups under `.ts3-mod-manager` in the game directory. Use **Open backups** to inspect them. Do not move the manager's disabled payloads into `BepInEx/plugins` manually.

Settings marked **applies immediately**, **main menu only** or **restart required** have different application rules. See [Settings](Settings.md).
