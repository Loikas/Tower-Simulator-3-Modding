# Packaging and distribution

## Package layout

```text
example.my-mod-0.1.0.zip
  mod.json
  payload/
    MyMod.dll
    mod-info.json
    TEMPLATE-LICENSE.txt
    icon.png                   # optional
```

`mod.json` contains schema version 1, identity, required dependency IDs/minimum versions, supported game hashes and a SHA-256 map for every payload file. Paths must be relative. Do not include undeclared files or copied game/framework libraries.

The starter's script generates this layout, verifies compiled BepInEx identity and hard dependencies, includes its license and optional icon, and refuses to overwrite a version ZIP. It reads `AssemblyName` instead of assuming a DLL filename.

Soft BepInEx dependencies are not hard package dependencies. Input Panel is optional in the starter. Extra hard dependencies need both plugin attributes and manifest entries.

## Validate locally

With the complete launcher folder available:

```powershell
$manager = 'C:\Tools\TS3ModManager\TS3ModManager.dll'
dotnet $manager --validate-package '.\dist\example.my-mod-0.1.0.zip'
```

The path is an example. The UI uses the same validation backend. Checksums verify integrity, not publisher identity or behavior.

## Prepare a submission

Replace the placeholder author and choose a license. Preserve the starter's MIT notice when using its source. Game/third-party libraries and the main project do not become MIT-licensed because the starter is.

```powershell
dotnet $manager --prepare-submission '.\dist\example.my-mod-0.1.0.zip' '.\mod-project.json' '.\submission.zip'
dotnet $manager --import-submission '.\submission.zip' '.\local-store'
```

Preparation produces `listing.json` and `package.zip`. Import creates a local listing; it does not install in TS3 or upload it. Use an HTTPS website URL or leave it empty. Increase versions for changed content.

Online Nexus/ModDB catalog integration is hidden in the first public launcher release. Local submissions are available under Mod development.
