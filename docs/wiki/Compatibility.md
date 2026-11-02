# Compatibility

## Tested development baseline

| Component | Baseline |
| --- | --- |
| Platform | Windows, TS3 Mono/Unity installation |
| Unity reported by the reviewed game | 2022.3.62f3 |
| BepInEx tested with the suite | 5.4.23.5 |
| Framework | 0.8.21 |
| API package version | 0.8.9 |
| API assembly identity | 0.2.0.0 |
| Plugin target | .NET Framework 4.6 |
| Starter build SDK | .NET SDK 10.0.401 |
| Launcher | .NET 10 Windows Desktop runtime |

Reviewed `Assembly-CSharp.dll` SHA-256:

```text
26F836244CEB94C8EB4C1308D367AE449E2A890C422C345015372F847C0B76A7
```

The framework checks the actual game assembly. Native adapters/patches must fail closed on an unreviewed build. A larger installed version number does not prove an older native patch remains compatible.

`SupportedGameAssemblyHashes` identifies supported builds. The starter records the installed hash when packaging; authors must test before sharing that compatibility claim. Do not list hashes you have not reviewed.

The API's stable assembly identity supports loading existing binaries. Feature availability still depends on the required framework version. Missed-departure completion evidence requires framework 0.8.19. Keep minimum versions accurate.
